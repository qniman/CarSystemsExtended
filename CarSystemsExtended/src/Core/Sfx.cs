using System;
using MSCLoader;
using UnityEngine;

namespace CSE.Core
{
    // Game sounds through MasterAudio plus procedurally generated clips for our own devices.
    public static class Sfx
    {
        public static void Game(string group, string variation, Vector3 pos, float volume = 1f)
        {
            try
            {
                MasterAudio.PlaySound3DAtVector3AndForget(group, pos, volume, null, 0f, variation);
            }
            catch (Exception e)
            {
                ModConsole.Warning("[CSE] sound " + group + "/" + variation + ": " + e.Message);
            }
        }

        public static void Assemble(Vector3 pos) { Game("CarBuilding", "assemble", pos); }
        public static void Disassemble(Vector3 pos) { Game("CarBuilding", "disassemble", pos); }
        public static void BoltScrew(Vector3 pos) { Game("CarBuilding", "bolt_screw", pos); }

        /// Pulling the handle of a locked door: the latch rattles against the lock (two game
        /// sounds picked by ear, alternated so it does not sound canned).
        public static void LockedDoorRattle(Vector3 pos)
        {
            if (Random.value < 0.5f) Game("Valmet", "tractor_difflock", pos, 0.8f);
            else Game("CarFoley", "bus_door_open", pos, 0.8f);
        }

        // ---- synthesized clips

        const int Rate = 22050;

        static AudioClip chirp, beep;

        /// Short siren chirp used for lock/unlock.
        public static AudioClip Chirp
        {
            get { return chirp != null ? chirp : (chirp = Sweep("cse_chirp", 0.12f, 1900f, 2600f, 0.8f)); }
        }

        /// Plain piezo beep (fob errors, parking sensors).
        public static AudioClip Beep
        {
            get { return beep != null ? beep : (beep = Sweep("cse_beep", 0.08f, 2700f, 2700f, 0.5f)); }
        }

        static AudioClip wail, thunk;

        /// Classic rising/falling siren, loops seamlessly (2 s period).
        public static AudioClip Wail
        {
            get
            {
                if (wail != null) return wail;
                int n = Rate * 2;
                var data = new float[n];
                double phase = 0;
                for (int i = 0; i < n; i++)
                {
                    // triangle sweep 650 -> 1500 -> 650 Hz
                    float t = (float)i / n;
                    float tri = t < 0.5f ? t * 2f : 2f - t * 2f;
                    double f = 650 + 850 * tri;
                    phase += 2 * Math.PI * f / Rate;
                    data[i] = (float)(Math.Sin(phase) + Math.Sin(3 * phase) / 3) * 0.55f;
                }
                wail = AudioClip.Create("cse_wail", n, 1, Rate, false);
                wail.SetData(data, 0);
                return wail;
            }
        }

        /// Dull mechanical thunk of a lock actuator.
        public static AudioClip Thunk
        {
            get
            {
                if (thunk != null) return thunk;
                int n = Rate / 12;
                var data = new float[n];
                var rnd = new System.Random(7);
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / n;
                    float env = Mathf.Exp(-t * 9f);
                    float body = Mathf.Sin(2 * Mathf.PI * 140f * i / Rate);
                    float noise = (float)(rnd.NextDouble() * 2 - 1) * Mathf.Exp(-t * 30f);
                    data[i] = (body * 0.7f + noise * 0.5f) * env;
                }
                thunk = AudioClip.Create("cse_thunk", n, 1, Rate, false);
                thunk.SetData(data, 0);
                return thunk;
            }
        }

        static AudioClip spark;

        /// Short electric crackle of a fuse letting go.
        public static AudioClip Spark
        {
            get
            {
                if (spark != null) return spark;
                int n = Rate / 6;
                var data = new float[n];
                var rnd = new System.Random(3);
                for (int i = 0; i < n; i++)
                {
                    float t = (float)i / n;
                    // noise bursts with a fast decay and a crackly gate
                    float gate = rnd.NextDouble() < 0.35 ? 1f : 0.15f;
                    data[i] = (float)(rnd.NextDouble() * 2 - 1) * Mathf.Exp(-t * 7f) * gate * 0.9f;
                }
                spark = AudioClip.Create("cse_spark", n, 1, Rate, false);
                spark.SetData(data, 0);
                return spark;
            }
        }

        /// Plays a clip once at a world position.
        public static void At(AudioClip clip, Vector3 pos, float volume = 1f)
        {
            AudioSource.PlayClipAtPoint(clip, pos, volume);
        }

        /// Square-ish tone sweeping linearly from f0 to f1, with short fade in/out to avoid clicks.
        public static AudioClip Sweep(string name, float seconds, float f0, float f1, float gain)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            double phase = 0;
            int fade = Rate / 200;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                double f = f0 + (f1 - f0) * t;
                phase += 2 * Math.PI * f / Rate;
                // soft square: sum of the first odd harmonics
                float s = (float)(Math.Sin(phase) + Math.Sin(3 * phase) / 3 + Math.Sin(5 * phase) / 5);
                float env = Mathf.Min(1f, Mathf.Min(i, n - 1 - i) / (float)fade);
                data[i] = s * gain * 0.6f * env;
            }
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// 3D audio source attached to a device. Logarithmic rolloff: full volume within
        /// minDistance, halving with every doubling of distance after that, like a real horn.
        public static AudioSource CreateSource(GameObject host, float minDistance = 2f, float maxDistance = 200f)
        {
            var src = host.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = minDistance;
            src.maxDistance = maxDistance;
            return src;
        }

        // ---- key fob piezo (two-way remote): played in the player's hand, not in 3D

        static AudioSource fobSource;
        static AudioClip fobSend, fobOk, fobFail, fobStop, fobNoLink, fobLocked, fobUnlocked;

        static AudioSource FobSource
        {
            get
            {
                if (fobSource != null) return fobSource;
                // Lives in the game scene, so it goes away with it (no pager trill in the menu).
                var go = new GameObject("cse_fob_audio");
                fobSource = go.AddComponent<AudioSource>();
                fobSource.playOnAwake = false;
                fobSource.spatialBlend = 0f;
                fobSource.volume = 0.35f;
                return fobSource;
            }
        }

        /// Notes as (frequency Hz, seconds); 0 Hz is a rest.
        static AudioClip Melody(string name, params float[] notes)
        {
            var samples = new System.Collections.Generic.List<float>();
            for (int i = 0; i + 1 < notes.Length; i += 2)
            {
                var tone = notes[i] > 0 ? Sweep(name, notes[i + 1], notes[i], notes[i], 0.5f) : null;
                int n = Mathf.CeilToInt(notes[i + 1] * Rate);
                var buf = new float[n];
                if (tone != null) tone.GetData(buf, 0);
                samples.AddRange(buf);
            }
            var clip = AudioClip.Create(name, samples.Count, 1, Rate, false);
            clip.SetData(samples.ToArray(), 0);
            return clip;
        }

        public static void FobSend()
        {
            if (fobSend == null) fobSend = Melody("cse_fob_send", 2093, 0.07f, 0, 0.03f, 2637, 0.07f, 0, 0.03f, 3136, 0.1f);
            FobSource.PlayOneShot(fobSend);
        }

        public static void FobOk()
        {
            if (fobOk == null) fobOk = Melody("cse_fob_ok", 3136, 0.09f, 0, 0.04f, 3136, 0.09f, 0, 0.04f, 4186, 0.25f);
            FobSource.PlayOneShot(fobOk);
        }

        public static void FobFail()
        {
            if (fobFail == null) fobFail = Melody("cse_fob_fail", 880, 0.18f, 0, 0.08f, 880, 0.3f);
            FobSource.PlayOneShot(fobFail);
        }

        static AudioSource fobAlarmSource;

        /// Pager trill on the fob while the car's alarm is going off.
        /// at = the loose keyring the remote is on (a quiet 3D trill there, handy for finding
        /// lost keys), or null when the keys are with the player (in the hand, 2D).
        public static void SetFobAlarm(bool on, Transform at)
        {
            if (!on)
            {
                if (fobAlarmSource != null && fobAlarmSource.isPlaying) fobAlarmSource.Stop();
                return;
            }
            if (fobAlarmSource == null)
            {
                var go = new GameObject("cse_fob_pager");
                fobAlarmSource = go.AddComponent<AudioSource>();
                fobAlarmSource.playOnAwake = false;
                fobAlarmSource.volume = 0.3f;
                fobAlarmSource.loop = true;
                fobAlarmSource.rolloffMode = AudioRolloffMode.Logarithmic;
                fobAlarmSource.minDistance = 0.5f;
                fobAlarmSource.maxDistance = 30f;
                fobAlarmSource.clip = Melody("cse_fob_alarm", 3136, 0.1f, 2349, 0.1f, 3136, 0.1f, 2349, 0.1f, 0, 0.3f);
            }
            fobAlarmSource.spatialBlend = at != null ? 1f : 0f;
            if (at != null) fobAlarmSource.transform.position = at.position;
            if (!fobAlarmSource.isPlaying) fobAlarmSource.Play();
        }

        /// The car confirms locking: one short blip on the fob.
        public static void FobLocked()
        {
            if (fobLocked == null) fobLocked = Melody("cse_fob_locked", 3520, 0.06f);
            FobSource.PlayOneShot(fobLocked);
        }

        /// The car confirms unlocking: two short blips.
        public static void FobUnlocked()
        {
            if (fobUnlocked == null) fobUnlocked = Melody("cse_fob_unlocked", 3520, 0.06f, 0, 0.06f, 3520, 0.06f);
            FobSource.PlayOneShot(fobUnlocked);
        }

        /// The remote got no reply from the car: one long low buzz.
        public static void FobNoLink()
        {
            if (fobNoLink == null) fobNoLink = Melody("cse_fob_nolink", 523, 0.6f);
            FobSource.PlayOneShot(fobNoLink);
        }

        public static void FobStop()
        {
            if (fobStop == null) fobStop = Melody("cse_fob_stop", 3136, 0.08f, 0, 0.03f, 2637, 0.08f, 0, 0.03f, 2093, 0.14f);
            FobSource.PlayOneShot(fobStop);
        }
    }
}
