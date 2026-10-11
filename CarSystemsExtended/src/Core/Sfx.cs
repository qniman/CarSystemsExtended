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

        // ---- synthesized clips

        const int Rate = 22050;

        static AudioClip chirp, beep;

        /// Short siren chirp used for lock/unlock.
        public static AudioClip Chirp
        {
            get { return chirp ?? (chirp = Sweep("cse_chirp", 0.12f, 1900f, 2600f, 0.8f)); }
        }

        /// Plain piezo beep (fob errors, parking sensors).
        public static AudioClip Beep
        {
            get { return beep ?? (beep = Sweep("cse_beep", 0.08f, 2700f, 2700f, 0.5f)); }
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

        /// 3D audio source attached to a device.
        public static AudioSource CreateSource(GameObject host, float maxDistance = 40f)
        {
            var src = host.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1f;
            src.maxDistance = maxDistance;
            return src;
        }
    }
}
