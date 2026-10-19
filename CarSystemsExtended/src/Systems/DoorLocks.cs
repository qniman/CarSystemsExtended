using CSE.Core;
using CSE.Parts;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace CSE.Systems
{
    // Central locking through the lock actuators. A locked door has the LMB action of its game
    // "Use" FSM disabled, so clicking it does nothing from outside. From inside the player can pull
    // the lock knob, which unlocks that door (and sets off the alarm if it is armed).
    public static class DoorLocks
    {
        class Door
        {
            public string Path;
            public int Mount; // LockActuator mount index
            public Transform T;
            public PlayMakerFSM Use;
            public FsmBool Open;
            /// The LMB check in the "closed door, mouse over" state; disabled while locked.
            public FsmStateAction OpenClick;
            public bool Locked;
        }

        static readonly Door[] doors =
        {
            new Door { Path = "Body/pivot_door_left/door left(Clone)", Mount = 0 },
            new Door { Path = "Body/pivot_door_right/door right(Clone)", Mount = 1 },
        };

        public static void Init()
        {
            foreach (var d in doors)
            {
                d.T = null;
                d.Use = null;
                d.Open = null;
                d.Locked = false;
            }
        }

        /// Refresh references: doors can be removed from the car and put back.
        static void Resolve(Door d)
        {
            var t = GameRefs.FindInCar(d.Path);
            if (t == d.T) return;
            if (d.T != null && d.Locked) SetLocked(d, false, true);
            d.T = t;
            d.Use = t != null ? t.gameObject.GetPlayMaker("Use") : null;
            d.Open = d.Use != null ? d.Use.FsmVariables.FindFsmBool("Open") : null;
            d.OpenClick = d.Use != null ? FindOpenClick(d.Use) : null;
            if (d.Use != null && d.OpenClick == null)
                ModConsole.Warning("[CSE] Door open action not found on " + d.Path + ", it cannot be locked");
        }

        static FsmStateAction FindOpenClick(PlayMakerFSM use)
        {
            use.InitializeFSM();
            foreach (var s in use.FsmStates)
            {
                if (s.Name != "Mouse over 1") continue;
                foreach (var a in s.Actions)
                    if (a is HutongGames.PlayMaker.Actions.GetMouseButtonDown) return a;
            }
            return null;
        }

        static bool ActuatorReady(Door d)
        {
            return WhyNotReady(d) == null;
        }

        /// What keeps a door's actuator from working, or null. Logged on every remote press so a
        /// player's output_log tells what is missing.
        static string WhyNotReady(Door d)
        {
            if (d.T == null) return "door not on the car";
            if (d.OpenClick == null) return "door FSM action not found (changed by another mod?)";
            var a = PartManager.FindInstalled(PartCatalog.LockActuator.Id, d.Mount);
            if (a == null) return "no actuator installed";
            if (!a.AllBoltsTight) return "actuator bolts not tight";
            if (!a.WireConnected) return "actuator not wired to the alarm unit";
            if (!AlarmSystem.Powered) return "alarm unit has no power";
            return null;
        }

        public static bool AnyLocked
        {
            get
            {
                foreach (var d in doors) if (d.Locked) return true;
                return false;
            }
        }

        /// Every door that is on the car is locked (false if one has no working actuator).
        public static bool AllClosedDoorsLocked
        {
            get
            {
                foreach (var d in doors) if (d.T != null && !d.Locked) return false;
                return true;
            }
        }

        public static string StatusText
        {
            get
            {
                string s = "";
                foreach (var d in doors)
                    s += (d.T == null ? "-" : d.Locked ? "L" : d.Open != null && d.Open.Value ? "O" : "c");
                return s;
            }
        }

        public static bool AnyDoorOpen
        {
            get
            {
                foreach (var d in doors)
                {
                    Resolve(d);
                    if (d.Open != null && d.Open.Value) return true;
                }
                return false;
            }
        }

        /// Locks every closed door that has a working actuator.
        public static void LockAll(bool silent = false)
        {
            foreach (var d in doors)
            {
                Resolve(d);
                if (d.T == null || d.Locked) continue;
                string why = WhyNotReady(d);
                if (why != null)
                {
                    if (!silent) ModConsole.Log("[CSE] " + d.Path + " not locked: " + why);
                    continue;
                }
                if (d.Open != null && d.Open.Value) continue;
                SetLocked(d, true, silent);
            }
        }

        /// Unlocks everything regardless of actuators (mod reset).
        public static void ForceUnlockAll()
        {
            foreach (var d in doors)
                if (d.Locked) SetLocked(d, false, true);
        }

        public static void UnlockAll(bool silent = false)
        {
            foreach (var d in doors)
            {
                Resolve(d);
                // Unlocking needs the actuator too; a dead actuator leaves the door locked
                // until it is opened from inside.
                if (d.Locked && ActuatorReady(d)) SetLocked(d, false, silent);
            }
        }

        static void SetLocked(Door d, bool locked, bool silent)
        {
            // Never disable the door FSM itself: re-enabling restarts it, and its start state adds
            // a second FixedJoint, leaving the door welded shut while it thinks it is open.
            d.Locked = locked;
            if (d.OpenClick != null)
            {
                d.OpenClick.Enabled = !locked;
                // The flag is read on state entry; bounce the FSM out of the hover state so the
                // change applies to the very next click.
                if (d.Use.ActiveStateName == "Mouse over 1") d.Use.SendEvent("LOOP");
            }
            if (!silent && d.T != null) Sfx.At(Sfx.Thunk, d.T.position, 0.8f);
            GameRefs.DrainBattery(0.02f);
            if (!silent) CountActuation();
        }

        // Actuators pull a lot of current; hammering lock/unlock overheats the circuit.
        const int SpamActuations = 8;
        const float SpamWindow = 20f;
        static readonly System.Collections.Generic.Queue<float> actuations = new System.Collections.Generic.Queue<float>();

        static void CountActuation()
        {
            float now = Time.time;
            actuations.Enqueue(now);
            while (actuations.Count > 0 && now - actuations.Peek() > SpamWindow) actuations.Dequeue();
            // one press works both doors; count presses, not doors
            if (actuations.Count > SpamActuations * 2)
            {
                actuations.Clear();
                AlarmSystem.BlowFuse("lock actuators hammered");
            }
        }

        /// Cabin box in car space, used to tell "inside" from "outside".
        static bool PlayerInCabin()
        {
            var cam = GameRefs.Camera;
            if (cam == null || GameRefs.Car == null) return false;
            var p = GameRefs.Car.transform.InverseTransformPoint(cam.transform.position);
            return Mathf.Abs(p.x) < 0.65f && p.y > -0.1f && p.y < 1.0f && p.z > -1.0f && p.z < 0.7f;
        }

        /// Nearest non-trigger collider along the ray (the car's big PlayerTrigger box would
        /// otherwise swallow every ray). Unity 5.0 has no QueryTriggerInteraction.
        static Collider FirstSolidHit(Vector3 origin, Vector3 dir, float dist)
        {
            Collider best = null;
            float bestDist = float.MaxValue;
            foreach (var h in Physics.RaycastAll(origin, dir, dist))
            {
                if (h.collider.isTrigger || h.distance >= bestDist) continue;
                best = h.collider;
                bestDist = h.distance;
            }
            return best;
        }

        public static void Tick()
        {
            foreach (var d in doors) Resolve(d);
            if (!AnyLocked) return;

            var cam = GameRefs.Camera;
            if (cam == null) return;
            var hitCol = FirstSolidHit(cam.transform.position, cam.transform.forward, 1.5f);
            if (hitCol == null) return;

            foreach (var d in doors)
            {
                if (!d.Locked || d.T == null || !hitCol.transform.IsChildOf(d.T)) continue;

                if (PlayerInCabin())
                {
                    Gui.Interaction(L.T("unlock_door"));
                    if (Input.GetMouseButtonDown(0))
                    {
                        SetLocked(d, false, false);
                        AlarmSystem.TriggerIfArmed("door unlocked from inside");
                    }
                }
                else
                {
                    Gui.Interaction(L.T("locked"));
                    if (Input.GetMouseButtonDown(0))
                        Sfx.Game("CarFoley", "dash_button", d.T.position, 0.8f);
                }
                return;
            }
        }
    }
}
