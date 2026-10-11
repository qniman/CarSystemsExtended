using System.Collections;
using CSE.Core;
using CSE.Electrics;
using CSE.Parts;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace CSE.Systems
{
    // The alarm brain: power, arming with the key fob, sensors, siren and status LEDs.
    //
    //   Disarmed --lock--> Armed --sensor--> Triggered --30 s--> Armed
    //       ^                |                   |
    //       +----unlock------+-------unlock------+
    public static class AlarmSystem
    {
        public enum Mode { Disarmed, Armed, Triggered }

        const float FobRange = 50f;
        const float ArmGrace = 3f;          // sensors ignored right after arming
        const float SirenTime = 30f;
        const float ArmedDrainPerSec = 0.0002f;
        const float SirenDrainPerSec = 0.01f;

        public static Mode Current { get; private set; }
        public static bool FobOnKeys;
        public static bool Powered { get; private set; }
        public static string LastTrigger { get; private set; }
        /// Went off since it was last armed; the LED tells the owner when they come back.
        public static bool AlarmMemory { get; private set; }

        static float armedAt;
        static float triggeredUntil;
        static float errorUntil;
        static bool pendingRelock;
        static AudioSource sirenSource;
        static FsmInt playerKeySatsuma;
        static FsmBool ignitionAcc;
        static FsmBool hoodOpen;
        static Transform keyInLock;
        static Rigidbody carBody;
        static float movingSince = -1f;
        static SettingsKeybind lockKey;
        static bool consumedLockPress;

        public static void Init(SettingsKeybind lockKeybind)
        {
            lockKey = lockKeybind;
            Current = Mode.Disarmed;
            FobOnKeys = false;
            Powered = false;
            LastTrigger = null;
            AlarmMemory = false;
            sirenSource = null;
            pendingRelock = false;
            consumedLockPress = false;

            PartCatalog.Fob.OnHeld = FobHeld;
            playerKeySatsuma = FsmVariables.GlobalVariables.FindFsmInt("PlayerKeySatsuma");

            var ign = GameRefs.FindInCar("Dashboard/Steering/steering_column2/IgnitionSatsuma");
            var ignFsm = ign != null ? ign.gameObject.GetPlayMaker("UseNew") : null;
            ignitionAcc = ignFsm != null ? ignFsm.FsmVariables.FindFsmBool("ACC") : null;
            // The ignition FSM shows this key mesh while the key is in the lock.
            keyInLock = GameRefs.FindInCar("Dashboard/Steering/steering_column2/Ignition/Keys/Key");

            var hood = GameRefs.FindInCar("Body/pivot_hood/hood(Clone)");
            var hoodFsm = hood != null ? hood.gameObject.GetPlayMaker("Use") : null;
            hoodOpen = hoodFsm != null ? hoodFsm.FsmVariables.FindFsmBool("Open") : null;

            carBody = GameRefs.Car.GetComponent<Rigidbody>();
            if (GameRefs.Car.GetComponent<CarShockRelay>() == null) GameRefs.Car.AddComponent<CarShockRelay>();

            if (ignitionAcc == null) ModConsole.Warning("[CSE] Ignition FSM not found, ignition sensor off");
            if (hoodOpen == null) ModConsole.Warning("[CSE] Hood FSM not found, hood sensor off");
            if (keyInLock == null) ModConsole.Warning("[CSE] Ignition key mesh not found");
        }

        /// Restores the saved mode; the doors are re-locked once power is confirmed.
        public static void Restore(bool armed, bool fobOnKeys)
        {
            FobOnKeys = fobOnKeys;
            if (armed)
            {
                Current = Mode.Armed;
                armedAt = Time.time;
                pendingRelock = true;
            }
        }

        // ---------------------------------------------------------------- power and devices

        static Part Device(PartDef def)
        {
            var p = PartManager.FindInstalled(def.Id);
            return p != null && p.Secured && p.WireConnected ? p : null;
        }

        static bool SirenReady
        {
            get { return Powered && Device(PartCatalog.Siren) != null; }
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
            if (!AlarmWiring.PowerConnected) return "not wired to the fuse box";
            if (!AlarmWiring.BatteryOk) return "battery missing or flat";
            if (!FuseOk(u)) return "fuse missing or blown";
            return "OK";
        }

        public static bool HoodOpen
        {
            get { return hoodOpen != null && hoodOpen.Value; }
        }

        public static bool KeyInIgnition
        {
            get { return keyInLock != null && keyInLock.gameObject.activeSelf; }
        }

        // ---------------------------------------------------------------- fob

        static bool FobHeld(Part fob)
        {
            string key = lockKey != null ? lockKey.GetKeyValue.ToString() : "L";
            Gui.Interaction("PRESS " + key.ToUpper() + " TO PUT ON SATSUMA KEYS");
            if (lockKey != null && lockKey.GetKeybindDown())
            {
                FobOnKeys = true;
                Sfx.Game("CarFoley", "carkeys_in", fob.transform.position, 0.6f);
                PartManager.Remove(fob);
                consumedLockPress = true;
            }
            return true;
        }

        /// Fob on the keyring, keys with the player (not in the ignition), car within range.
        public static bool FobWorks
        {
            get
            {
                if (!FobOnKeys || playerKeySatsuma == null || playerKeySatsuma.Value != 1) return false;
                if (KeyInIgnition) return false;
                var cam = GameRefs.Camera;
                return cam != null && Vector3.Distance(cam.transform.position, GameRefs.Car.transform.position) <= FobRange;
            }
        }

        public static void OnLockPressed()
        {
            if (consumedLockPress) { consumedLockPress = false; return; }
            if (!FobWorks || !Powered) return;

            if (DoorLocks.AnyDoorOpen || HoodOpen)
            {
                // Something is open: refuse to arm and complain.
                Chirp(3);
                errorUntil = Time.time + 1.2f;
                return;
            }
            if (Current == Mode.Triggered) StopSiren();
            Current = Mode.Armed;
            armedAt = Time.time;
            movingSince = -1f;
            AlarmMemory = false;
            DoorLocks.LockAll();
            Chirp(1);
            Flasher.Flash(1);
        }

        public static void OnUnlockPressed()
        {
            if (!FobWorks || !Powered) return;
            StopSiren();
            Current = Mode.Disarmed;
            DoorLocks.UnlockAll();
            // Disarming after a break-in: four chirps instead of two, like real units.
            Chirp(AlarmMemory ? 4 : 2);
            Flasher.Flash(2);
            AlarmMemory = false;
        }

        // ---------------------------------------------------------------- sensors

        public static void TriggerIfArmed(string reason)
        {
            if (Current != Mode.Armed || !Powered) return;
            if (Time.time - armedAt < ArmGrace) return;
            Current = Mode.Triggered;
            LastTrigger = reason;
            AlarmMemory = true;
            triggeredUntil = Time.time + SirenTime;
            Flasher.SetContinuous(true);
            ModConsole.Print("[CSE] Alarm triggered: " + reason);
        }

        public static void OnShock(float strength)
        {
            TriggerIfArmed("shock " + strength.ToString("0.0"));
        }

        static void CheckSensors()
        {
            if (DoorLocks.AnyDoorOpen) TriggerIfArmed("door opened");
            else if (HoodOpen) TriggerIfArmed("hood opened");
            else if (ignitionAcc != null && ignitionAcc.Value) TriggerIfArmed("ignition on");

            // Pushed or towed: moving for more than half a second.
            if (carBody != null && carBody.velocity.magnitude > 0.8f)
            {
                if (movingSince < 0f) movingSince = Time.time;
                else if (Time.time - movingSince > 0.5f) TriggerIfArmed("car moved");
            }
            else movingSince = -1f;
        }

        // ---------------------------------------------------------------- siren

        static AudioSource SirenSource()
        {
            var s = PartManager.FindInstalled(PartCatalog.Siren.Id);
            if (s == null) return null;
            if (sirenSource == null || sirenSource.gameObject != s.gameObject)
            {
                sirenSource = s.GetComponent<AudioSource>();
                if (sirenSource == null) sirenSource = Sfx.CreateSource(s.gameObject, 4f, 250f);
            }
            return sirenSource;
        }

        static bool SirenWailing
        {
            get { return sirenSource != null && sirenSource.loop && sirenSource.isPlaying; }
        }

        /// While triggered, follows the siren's wiring live: unplug it and it goes quiet,
        /// plug it back in and it starts again.
        static void UpdateSiren()
        {
            bool want = Current == Mode.Triggered && SirenReady;
            if (want && !SirenWailing)
            {
                var src = SirenSource();
                src.clip = Sfx.Wail;
                src.loop = true;
                src.volume = 1f;
                src.Play();
            }
            else if (!want && SirenWailing)
            {
                sirenSource.Stop();
                sirenSource.loop = false;
            }
        }

        static void StopSiren()
        {
            Flasher.SetContinuous(false);
            if (Current == Mode.Triggered) Current = Mode.Armed;
            if (SirenWailing)
            {
                sirenSource.Stop();
                sirenSource.loop = false;
            }
        }

        public static void Chirp(int count)
        {
            if (!SirenReady) return;
            var src = SirenSource();
            src.GetComponent<Part>().StartCoroutine(ChirpRoutine(src, count));
        }

        static IEnumerator ChirpRoutine(AudioSource src, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (src == null) yield break;
                src.PlayOneShot(Sfx.Chirp, 1f);
                GameRefs.DrainBattery(0.01f);
                yield return new WaitForSeconds(0.2f);
            }
        }

        // ---------------------------------------------------------------- frame

        public static void Tick()
        {
            bool now = Diagnose() == "OK";
            if (now && !Powered)
            {
                Powered = true;
                if (pendingRelock) DoorLocks.LockAll(true);
                else if (SirenReady) Chirp(1); // power-up self test
            }
            Powered = now;
            if (Powered) pendingRelock = false;

            if (Powered)
            {
                switch (Current)
                {
                    case Mode.Armed:
                        GameRefs.DrainBattery(ArmedDrainPerSec * Time.deltaTime);
                        CheckSensors();
                        break;
                    case Mode.Triggered:
                        if (SirenWailing) GameRefs.DrainBattery(SirenDrainPerSec * Time.deltaTime);
                        if (Time.time > triggeredUntil)
                        {
                            // Siren cycle over: back on guard with a fresh grace period.
                            StopSiren();
                            armedAt = Time.time;
                        }
                        break;
                }
            }
            else Flasher.SetContinuous(false);

            UpdateSiren();
            Flasher.Tick();
            UpdateLeds();
        }

        // ---------------------------------------------------------------- LEDs

        static Material unitLed, dashLed;
        static Part unitLedOwner, dashLedOwner;

        static Material LedOf(Part p, ref Part owner, ref Material mat)
        {
            if (p != owner)
            {
                owner = p;
                var t = p != null ? p.transform.Find("led") : null;
                mat = t != null ? t.GetComponent<Renderer>().material : null;
            }
            return mat;
        }

        /// n short flashes at the start of every 1.5 s cycle.
        static bool Flashes(float t, int n)
        {
            float phase = Mathf.Repeat(t, 1.5f);
            return phase < n * 0.25f && Mathf.Repeat(phase, 0.25f) < 0.08f;
        }

        /// The status pattern, shared by both LEDs:
        ///   tripping now        fast flashing
        ///   warming up          steady on
        ///   refused to arm      rapid burst for a second
        ///   armed               1 flash   (2 = a door is not locked, 3 = went off while away)
        ///   disarmed            off
        static bool StatusOn(float t)
        {
            if (!Powered) return false;
            if (Current == Mode.Triggered) return Mathf.Repeat(t, 0.25f) < 0.12f;
            if (t < errorUntil) return Mathf.Repeat(t, 0.15f) < 0.07f;
            if (RemoteStart.Active) return true;
            if (Current == Mode.Armed)
            {
                if (AlarmMemory) return Flashes(t, 3);
                if (!DoorLocks.AllClosedDoorsLocked) return Flashes(t, 2);
                return Flashes(t, 1);
            }
            return false;
        }

        static void UpdateLeds()
        {
            float t = Time.time;
            bool status = StatusOn(t);

            var u = PartManager.FindInstalled(PartCatalog.AlarmUnit.Id);
            var um = LedOf(u, ref unitLedOwner, ref unitLed);
            if (um != null)
            {
                // The unit's own LED also shows a green heartbeat when powered and idle,
                // handy when checking the wiring under the dash.
                if (status) um.color = Shapes.LedRed;
                else if (Powered && Current == Mode.Disarmed && Mathf.Repeat(t, 3f) < 0.08f) um.color = Shapes.LedGreen;
                else um.color = Shapes.LedOff;
            }

            var d = Device(PartCatalog.StatusLed);
            var dm = LedOf(d ?? PartManager.FindInstalled(PartCatalog.StatusLed.Id), ref dashLedOwner, ref dashLed);
            if (dm != null) dm.color = status && d != null ? Shapes.LedRed : Shapes.LedOff;
        }
    }

    // Lives on the car root to receive its collisions (only components on the rigidbody's own
    // GameObject get OnCollisionEnter).
    public class CarShockRelay : MonoBehaviour
    {
        const float Threshold = 1.5f; // m/s relative impact speed

        void OnCollisionEnter(Collision c)
        {
            float v = c.relativeVelocity.magnitude;
            if (v >= Threshold) AlarmSystem.OnShock(v);
        }
    }
}
