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
