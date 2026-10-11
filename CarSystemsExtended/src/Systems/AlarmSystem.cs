using System.Collections;
using CSE.Core;
using CSE.Electrics;
using CSE.Parts;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace CSE.Systems
{
    // The alarm brain: power, arming with the key fob, sensors and the siren.
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

        static float armedAt;
        static float triggeredUntil;
        static bool pendingRelock;
        static Part unit;
        static Material ledMat;
        static AudioSource sirenSource;
        static FsmInt playerKeySatsuma;
        static FsmBool ignitionAcc;
        static FsmBool hoodOpen;
        static Rigidbody carBody;
        static float movingSince = -1f;
        static SettingsKeybind lockKey;

        public static void Init(SettingsKeybind lockKeybind)
        {
            lockKey = lockKeybind;
            Current = Mode.Disarmed;
            FobOnKeys = false;
            Powered = false;
            LastTrigger = null;
            unit = null;
            ledMat = null;
            sirenSource = null;
            pendingRelock = false;

            PartCatalog.Fob.OnHeld = FobHeld;
            playerKeySatsuma = FsmVariables.GlobalVariables.FindFsmInt("PlayerKeySatsuma");

            var ign = GameRefs.FindInCar("Dashboard/Steering/steering_column2/IgnitionSatsuma");
            var ignFsm = ign != null ? ign.gameObject.GetPlayMaker("UseNew") : null;
            ignitionAcc = ignFsm != null ? ignFsm.FsmVariables.FindFsmBool("ACC") : null;

            var hood = GameRefs.FindInCar("Body/pivot_hood/hood(Clone)");
            var hoodFsm = hood != null ? hood.gameObject.GetPlayMaker("Use") : null;
            hoodOpen = hoodFsm != null ? hoodFsm.FsmVariables.FindFsmBool("Open") : null;

            carBody = GameRefs.Car.GetComponent<Rigidbody>();
            var relay = GameRefs.Car.GetComponent<CarShockRelay>();
            if (relay == null) GameRefs.Car.AddComponent<CarShockRelay>();

            if (ignitionAcc == null) ModConsole.Warning("[CSE] Ignition FSM not found, ignition sensor off");
            if (hoodOpen == null) ModConsole.Warning("[CSE] Hood FSM not found, hood sensor off");
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

        // ---------------------------------------------------------------- power

        static Part Siren
        {
            get { return PartManager.FindInstalled(PartCatalog.Siren.Id); }
        }

        static bool SirenReady
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

        static bool consumedLockPress;

        public static bool HoodOpen
        {
            get { return hoodOpen != null && hoodOpen.Value; }
        }

        /// Fob on the keyring, keys with the player, car within range.
        public static bool FobWorks
        {
            get
            {
                if (!FobOnKeys || playerKeySatsuma == null || playerKeySatsuma.Value != 1) return false;
                var cam = GameRefs.Camera;
                return cam != null && Vector3.Distance(cam.transform.position, GameRefs.Car.transform.position) <= FobRange;
            }
        }

        public static void OnLockPressed()
        {
            if (consumedLockPress) { consumedLockPress = false; return; }
            if (!FobWorks || !Powered) return;

            if (DoorLocks.AnyDoorOpen || (hoodOpen != null && hoodOpen.Value))
            {
                // Something is open: refuse to arm and complain.
                Chirp(3);
                return;
            }
            if (Current == Mode.Triggered) StopSiren();
            Current = Mode.Armed;
            armedAt = Time.time;
            movingSince = -1f;
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
            Chirp(2);
            Flasher.Flash(2);
        }

        // ---------------------------------------------------------------- sensors

        public static void TriggerIfArmed(string reason)
        {
            if (Current != Mode.Armed || !Powered) return;
            if (Time.time - armedAt < ArmGrace) return;
            Current = Mode.Triggered;
            LastTrigger = reason;
            triggeredUntil = Time.time + SirenTime;
            StartSiren();
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
            else if (hoodOpen != null && hoodOpen.Value) TriggerIfArmed("hood opened");
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
            var s = Siren;
            if (s == null) return null;
            if (sirenSource == null || sirenSource.gameObject != s.gameObject)
            {
                sirenSource = s.GetComponent<AudioSource>();
                if (sirenSource == null) sirenSource = Sfx.CreateSource(s.gameObject, 150f);
            }
            return sirenSource;
        }

        static void StartSiren()
        {
            if (!SirenReady) return; // silent alarm: lights only
            var src = SirenSource();
            src.clip = Sfx.Wail;
            src.loop = true;
            src.volume = 1f;
            src.Play();
        }

        static void StopSiren()
        {
            Flasher.SetContinuous(false);
            if (sirenSource != null && sirenSource.loop)
            {
                sirenSource.Stop();
                sirenSource.loop = false;
            }
        }

        public static void Chirp(int count)
        {
            var s = Siren;
            if (s == null || !SirenReady) return;
            s.StartCoroutine(ChirpRoutine(SirenSource(), count));
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

        // ---------------------------------------------------------------- frame

        public static void Tick()
        {
            var u = PartManager.FindInstalled(PartCatalog.AlarmUnit.Id);
            if (u != unit)
            {
                unit = u;
                var t = u != null ? u.transform.Find("led") : null;
                ledMat = t != null ? t.GetComponent<Renderer>().material : null;
            }

            bool now = Diagnose() == "OK";
            if (now && !Powered)
            {
                Powered = true;
                if (pendingRelock) DoorLocks.LockAll(true);
                else if (SirenReady) Chirp(1); // power-up self test
            }
            if (!now && Powered && Current == Mode.Triggered) StopSiren();
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
                        GameRefs.DrainBattery(SirenDrainPerSec * Time.deltaTime);
                        if (Time.time > triggeredUntil)
                        {
                            // Siren cycle over: back on guard with a fresh grace period.
                            StopSiren();
                            Current = Mode.Armed;
                            armedAt = Time.time;
                        }
                        break;
                }
            }

            Flasher.Tick();
            UpdateLed();
        }

        static void UpdateLed()
        {
            if (ledMat == null) return;
            bool on = false;
            if (Powered)
            {
                if (Current == Mode.Armed) on = Mathf.Repeat(Time.time, 1f) < 0.08f;
                else if (Current == Mode.Triggered) on = Mathf.Repeat(Time.time, 0.25f) < 0.12f;
            }
            ledMat.color = on ? Shapes.LedRed : Shapes.LedOff;
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
