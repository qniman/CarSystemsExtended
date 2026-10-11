using CSE.Core;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace CSE.Systems
{
    // The alarm's own relay for the turn signal lamps. The game's indicator lights live under
    // Electricity/PowerON, which is switched off with the ignition, so we clone them next to it
    // and switch the copies ourselves.
    public static class Flasher
    {
        const float Interval = 0.35f;

        static GameObject left, right;
        static int togglesLeft;
        static bool continuous;
        static bool lit;
        static float nextToggle;

        public static void Init()
        {
            left = Clone("Electricity/PowerON/Indicators/Left");
            right = Clone("Electricity/PowerON/Indicators/Right");
            togglesLeft = 0;
            continuous = false;
            lit = false;
        }

        static GameObject Clone(string path)
        {
            var orig = GameRefs.FindInCar(path);
            var host = GameRefs.FindInCar("Electricity");
            if (orig == null || host == null)
            {
                ModConsole.Warning("[CSE] Indicator lights not found: " + path);
                return null;
            }
            var copy = (GameObject)Object.Instantiate(orig.gameObject);
            foreach (var fsm in copy.GetComponentsInChildren<PlayMakerFSM>(true))
                Object.DestroyImmediate(fsm);
            copy.name = "cse_alarm_" + orig.name;
            copy.transform.parent = host;
            copy.transform.position = orig.position;
            copy.transform.rotation = orig.rotation;
            copy.SetActive(false);
            return copy;
        }

        /// Blink n times.
        public static void Flash(int times)
        {
            continuous = false;
            togglesLeft = times * 2;
            nextToggle = Time.time;
        }

        public static void SetContinuous(bool on)
        {
            continuous = on;
            if (!on) togglesLeft = lit ? 1 : 0;
        }

        public static void Tick()
        {
            if (!continuous && togglesLeft <= 0) return;
            if (Time.time < nextToggle) return;
            nextToggle = Time.time + Interval;
            Set(!lit);
            if (!continuous) togglesLeft--;
        }

        static void Set(bool on)
        {
            lit = on;
            if (left != null) left.SetActive(on);
            if (right != null) right.SetActive(on);
            if (on) GameRefs.DrainBattery(0.016f); // same as the game's hazard blink
        }
    }
}
