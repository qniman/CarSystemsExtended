using CSE.Core;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace CSE.Systems
{
    // The alarm's own relay for the turn signal lamps. The game's indicator lights live under
    // Electricity/PowerON, which is switched off with the ignition, so we clone them next to it
    // and switch the copies ourselves. Mods that move or hide the car's lamps (Satsuma Estate has
    // its own rear lights) change the originals, so the copies follow them before every flash.
    public static class Flasher
    {
        const float Interval = 0.35f;

        static GameObject left, right;
        static Transform leftOrig, rightOrig;
        static int togglesLeft;
        static bool continuous;
        static bool lit;
        static float nextToggle;

        static FsmBool gameHazards;
        static float gameHazardsOffAt = -1f;

        public static void Init()
        {
            left = Clone("Electricity/PowerON/Indicators/Left", out leftOrig);
            right = Clone("Electricity/PowerON/Indicators/Right", out rightOrig);
            togglesLeft = 0;
            continuous = false;
            lit = false;
            gameHazardsOffAt = -1f;

            var ts = GameRefs.FindInCar("Dashboard/TurnSignals");
            var blinkers = ts != null ? ts.gameObject.GetPlayMaker("Blinkers") : null;
            gameHazards = blinkers != null ? blinkers.FsmVariables.FindFsmBool("BlinkerHazards") : null;
            if (gameHazards == null) ModConsole.Warning("[CSE] Blinkers FSM not found, hazard confirmation off");
        }

        /// Runs the car's real hazard lights (relay click, dash lamps, all four indicators) for
        /// a couple of blinks, without touching the hazard switch. Needs the ignition on.
        public static void GameHazards(int blinks)
        {
            if (gameHazards == null || gameHazards.Value) return; // already on: leave them be
            gameHazards.Value = true;
            // The game blinks 0.4 s on / 0.4 s off and only reads the flag when entering each
            // phase, so clear it during the last "on": the following "off" then ends the cycle.
            gameHazardsOffAt = Time.time + blinks * 0.8f - 0.6f;
        }

        static GameObject Clone(string path, out Transform orig)
        {
            orig = GameRefs.FindInCar(path);
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
            if (gameHazardsOffAt >= 0f && Time.time >= gameHazardsOffAt)
            {
                gameHazardsOffAt = -1f;
                if (gameHazards != null) gameHazards.Value = false;
            }

            if (!continuous && togglesLeft <= 0) return;
            if (Time.time < nextToggle) return;
            nextToggle = Time.time + Interval;
            Set(!lit);
            if (!continuous) togglesLeft--;
        }

        /// Copies where the original lamps are and which of them are shown onto our copy's
        /// children (their active state is the blinking itself).
        static void Follow(Transform orig, Transform copy)
        {
            if (orig == null || copy == null) return;
            foreach (Transform c in copy)
            {
                var o = orig.Find(c.name);
                // a lamp gone from the car (replaced by a mod's own) is hidden on ours too
                c.gameObject.SetActive(o != null && o.gameObject.activeSelf);
                if (o == null) continue;
                c.localPosition = o.localPosition;
                c.localRotation = o.localRotation;
                c.localScale = o.localScale;
                var r = o.GetComponent<Renderer>();
                var cr = c.GetComponent<Renderer>();
                if (r != null && cr != null) cr.enabled = r.enabled;
                var l = o.GetComponent<Light>();
                var cl = c.GetComponent<Light>();
                if (l != null && cl != null) cl.enabled = l.enabled;
                Follow(o, c);
            }
        }

        static void FollowRoot(Transform orig, GameObject copy)
        {
            if (orig == null || copy == null) return;
            copy.transform.position = orig.position;
            copy.transform.rotation = orig.rotation;
            Follow(orig, copy.transform);
        }

        static void Set(bool on)
        {
            lit = on;
            if (on)
            {
                FollowRoot(leftOrig, left);
                FollowRoot(rightOrig, right);
            }
            if (left != null) left.SetActive(on);
            if (right != null) right.SetActive(on);
            if (on) GameRefs.DrainBattery(0.016f); // same as the game's hazard blink
        }
    }
}
