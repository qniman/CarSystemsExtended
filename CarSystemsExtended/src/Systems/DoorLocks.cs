using CSE.Core;
using CSE.Parts;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace CSE.Systems
{
    // Central locking through the lock actuators. A locked door has its game "Use" FSM disabled,
    // so it cannot be opened from outside. From inside the player can pull the lock knob, which
    // unlocks that door (and sets off the alarm if it is armed).
    public static class DoorLocks
    {
        class Door
        {
            public string Path;
            public int Mount; // LockActuator mount index
            public Transform T;
            public PlayMakerFSM Use;
            public FsmBool Open;
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
        }

        static bool ActuatorReady(Door d)
        {
            var a = PartManager.FindInstalled(PartCatalog.LockActuator.Id, d.Mount);
            return a != null && a.Secured && a.WireConnected && AlarmSystem.Powered;
        }

        public static bool AnyLocked
        {
            get
            {
                foreach (var d in doors) if (d.Locked) return true;
                return false;
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
                if (d.T == null || d.Locked || !ActuatorReady(d)) continue;
                if (d.Open != null && d.Open.Value) continue;
                SetLocked(d, true, silent);
            }
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
            d.Locked = locked;
            if (d.Use != null)
            {
                d.Use.enabled = !locked;
                // A disabled FSM cannot clear the hint it may have been showing.
                if (locked) GameRefs.SetGui(false, false, false, "");
            }
            if (!silent && d.T != null) Sfx.At(Sfx.Thunk, d.T.position, 0.8f);
            GameRefs.DrainBattery(0.02f);
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
                    Gui.Interaction("UNLOCK DOOR");
                    if (Input.GetMouseButtonDown(0))
                    {
                        SetLocked(d, false, false);
                        AlarmSystem.TriggerIfArmed("door unlocked from inside");
                    }
                }
                else
                {
                    Gui.Interaction("LOCKED");
                    if (Input.GetMouseButtonDown(0))
                        Sfx.Game("CarFoley", "dash_button", d.T.position, 0.8f);
                }
                return;
            }
        }
    }
}
