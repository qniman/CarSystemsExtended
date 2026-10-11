using System.Collections;
using CSE.Core;
using CSE.Electrics;
using CSE.Parts;
using UnityEngine;

namespace CSE.Systems
{
    // Stage 1 brain of the alarm: works out whether the unit has power and drives the status LED.
    // When power first comes up the unit does a self-test chirp through the siren.
    public static class AlarmUnit
    {
        public static bool Powered { get; private set; }

        static Part unit;
        static Renderer led;
        static Material ledMat;
        static AudioSource sirenSource;

        public static void Init()
        {
            Powered = false;
            unit = null;
            led = null;
            sirenSource = null;
        }

        public static Part Siren
        {
            get { return PartManager.FindInstalled(PartCatalog.Siren.Id); }
        }

        public static bool SirenReady
        {
            get
            {
                var s = Siren;
                return Powered && s != null && s.Secured && s.WireConnected;
            }
        }

        static bool FuseOk(Part u)
        {
            var fuse = PartManager.FindInstalled(PartCatalog.Fuse.Id);
            return fuse != null && fuse.transform.parent == u.transform && !fuse.Damaged;
        }

        /// Human-readable reason for no power, for the debug overlay.
        public static string Diagnose()
        {
            var u = PartManager.FindInstalled(PartCatalog.AlarmUnit.Id);
            if (u == null) return "unit not installed";
            if (!u.AllBoltsTight) return "unit bolts loose (no ground)";
            if (!AlarmWiring.PowerConnected) return "power lead not connected";
            if (!AlarmWiring.BatteryOk) return "battery missing or flat";
            if (!FuseOk(u)) return "fuse missing or blown";
            return "OK";
        }

        public static void Tick()
        {
            var u = PartManager.FindInstalled(PartCatalog.AlarmUnit.Id);
            if (u != unit)
            {
                unit = u;
                var t = u != null ? u.transform.Find("led") : null;
                led = t != null ? t.GetComponent<Renderer>() : null;
                ledMat = led != null ? led.material : null;
            }

            bool now = Diagnose() == "OK";
            if (now && !Powered)
            {
                Powered = true;
                if (SirenReady) Chirp(1);
            }
            Powered = now;

            if (ledMat != null)
            {
                // Short green blink every two seconds when armed with power, dark otherwise.
                bool on = Powered && Mathf.Repeat(Time.time, 2f) < 0.1f;
                ledMat.color = on ? Shapes.LedGreen : Shapes.LedOff;
            }
        }

        public static void Chirp(int count)
        {
            var s = Siren;
            if (s == null || !SirenReady) return;
            if (sirenSource == null || sirenSource.gameObject != s.gameObject)
            {
                sirenSource = s.GetComponent<AudioSource>();
                if (sirenSource == null) sirenSource = Sfx.CreateSource(s.gameObject, 60f);
            }
            s.StartCoroutine(ChirpRoutine(sirenSource, count));
        }

        static IEnumerator ChirpRoutine(AudioSource src, int count)
        {
            for (int i = 0; i < count; i++)
            {
                src.PlayOneShot(Sfx.Chirp, 1f);
                GameRefs.DrainBattery(0.01f);
                yield return new WaitForSeconds(0.2f);
            }
        }
    }
}
