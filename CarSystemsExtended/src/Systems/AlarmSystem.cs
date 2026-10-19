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

        // The fob's tiny transmitter reaches the car from about 60 m; the car's transmitter
        // reaches the fob (pager, remote start replies) from about 140 m.
        const float FobRange = 60f;
        const float CarTransmitterRange = 140f;
        const float ArmGrace = 3f;          // sensors ignored right after arming
        const float SirenTime = 30f;
        const float ArmedDrainPerSec = 0.0002f;
        const float SirenDrainPerSec = 0.01f;

        public static Mode Current { get; private set; }
        public static bool Powered { get; private set; }
        public static string LastTrigger { get; private set; }
        /// Went off since it was last armed; the LED tells the owner when they come back.
        public static bool AlarmMemory { get; private set; }

        static float armedAt;
        static float triggeredUntil;
        static float errorUntil;
        static bool pendingRelock;
        static AudioSource sirenSource;
        static FsmBool ignitionAcc;
        static FsmBool hoodOpen;
        static Transform keyInLock;
        static Rigidbody carBody;
        static float movingSince = -1f;
        static SettingsKeybind lockKey;
        static bool consumedLockPress;
        static bool sirenWasReady;
        static float selfTestFrom;

        public static void Init(SettingsKeybind lockKeybind)
        {
            lockKey = lockKeybind;
            Current = Mode.Disarmed;
            Powered = false;
            LastTrigger = null;
            AlarmMemory = false;
            sirenSource = null;
            pendingRelock = false;
            consumedLockPress = false;
            fobReply = FobReply.None;
            sirenWasReady = false;
            // parts restored from the save (some a moment later) are not a fresh install
            selfTestFrom = Time.time + 5f;

            PartCatalog.Fob.OnHeld = FobHeld;
            PartCatalog.FobBattery.OnHeld = BatteryHeld;

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

        /// Back to factory state (mod reset): quiet, disarmed, fob off the keys.
        public static void ResetState()
        {
            StopSiren();
            Flasher.SetContinuous(false);
            Current = Mode.Disarmed;
            LastTrigger = null;
            AlarmMemory = false;
            pendingRelock = false;
            errorUntil = 0f;
        }

        /// Restores the saved mode; the doors are re-locked once power is confirmed.
        public static void Restore(bool armed)
        {
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

        const float SirenFuseChance = 0.03f;

        /// Blows the fuse in the alarm unit (spark, unit goes dead until a new one is fitted).
        public static void BlowFuse(string reason)
        {
            var u = PartManager.FindInstalled(PartCatalog.AlarmUnit.Id);
            var fuse = PartManager.FindInstalled(PartCatalog.Fuse.Id);
            if (u == null || fuse == null || fuse.Damaged || fuse.transform.parent != u.transform) return;
            fuse.Damaged = true;
            fuse.ApplyDamagedLook();
            Sfx.At(Sfx.Spark, fuse.transform.position, 0.9f);
            ModConsole.Print("[CSE] Alarm fuse blew: " + reason);
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

        // A23 battery: about 0.4 % per press (~200 presses), the pager drains while it trills,
        // and a little goes every game day. Below 30 % the range falls off, below 5 % it is dead.
        const float PressDrain = 0.4f;
        const float PagerDrainPerSec = 0.2f;
        const float WeakBattery = 30f;
        const float DeadBattery = 5f;
        const float WeakRange = 10f;

        static string LockKeyName
        {
            get { return (lockKey != null ? lockKey.GetKeyValue.ToString() : "L").ToUpperInvariant(); }
        }

        /// Remote in hand: press L to put it on the keyring in your pocket.
        static bool FobHeld(Part fob)
        {
            var ring = KeySystem.Pocket;
            if (ring == null) { Gui.Interaction(L.T("fob_needs_keys")); return true; }
            if (ring.Fob) { Gui.Interaction(L.T("keys_have_remote")); return true; }

            Gui.Interaction(L.F("fob_attach", LockKeyName));
            if (lockKey != null && lockKey.GetKeybindDown())
            {
                ring.Fob = true;
                ring.Battery = string.IsNullOrEmpty(fob.Data) ? 100f : KeyRing.Parse(fob.Data).Battery;
                Sfx.Game("CarFoley", "carkeys_in", fob.transform.position, 0.6f);
                PartManager.Remove(fob);
                consumedLockPress = true;
            }
            return true;
        }

        /// A23 battery in hand: press L to swap it into the remote on your keyring.
        static bool BatteryHeld(Part battery)
        {
            var ring = KeySystem.Pocket;
            if (ring == null || !ring.Fob) { Gui.Interaction(L.T("battery_needs_remote")); return true; }

            Gui.Interaction(L.F("battery_replace", LockKeyName));
            if (lockKey != null && lockKey.GetKeybindDown())
            {
                ring.Battery = 100f;
                Sfx.Game("CarFoley", "dash_button", battery.transform.position, 0.6f);
                PartManager.Remove(battery);
                consumedLockPress = true;
            }
            return true;
        }

        static float RangeFor(float battery)
        {
            return battery >= WeakBattery ? FobRange : Mathf.Lerp(WeakRange, FobRange, battery / WeakBattery);
        }

        /// Keys in hand with a live remote, car within its (battery dependent) range.
        public static bool FobWorks
        {
            get
            {
                var ring = KeySystem.HandRing;
                if (ring == null || !ring.Fob || ring.Battery < DeadBattery) return false;
                var cam = GameRefs.Camera;
                return cam != null && Vector3.Distance(cam.transform.position, GameRefs.Car.transform.position) <= RangeFor(ring.Battery);
            }
        }

        /// Keys in hand with a remote that still has some battery.
        public static bool HandFobAlive
        {
            get
            {
                var ring = KeySystem.HandRing;
                return ring != null && ring.Fob && ring.Battery >= DeadBattery;
            }
        }

        /// Presses a button on the remote in hand: LED flash and battery drain even out of range.
        /// True if the car will receive it.
        public static bool PressFob()
        {
            var ring = KeySystem.HandRing;
            if (ring == null) KeySystem.RemoteNotInHand();
            if (ring == null || !ring.Fob || ring.Battery < DeadBattery) return false;
            bool reaches = FobWorks; // range from the charge before this press
            ring.Battery = Mathf.Max(0f, ring.Battery - PressDrain);
            KeyRingView.Blink(ring.Battery);
            return reaches;
        }

        /// Some remote can hear the car (pager, remote start replies). where = the loose keyring
        /// it is on, or null if it is with the player / in the car.
        public static bool FobHears(out Transform where)
        {
            return KeySystem.FindListeningRemote(CarTransmitterRange, out where);
        }

        public static bool FobHearsAny
        {
            get
            {
                Transform t;
                return FobHears(out t);
            }
        }

        // ---------------------------------------------------------------- fob replies
        //
        // Two-way remote: the car confirms lock and unlock on the fob (one blip / two blips, the
        // failure tune when it refused to arm). No reply at all - out of range, no power - and
        // the fob buzzes "no link" by itself.

        enum FobReply { None, Locked, Unlocked, Refused, NoLink }

        const float FobReplyDelay = 0.4f;
        const float FobNoLinkDelay = 1.0f;

        static FobReply fobReply;
        static float fobReplyAt;

        static void ReplyToFob(FobReply r)
        {
            fobReply = r;
            fobReplyAt = Time.time + (r == FobReply.NoLink ? FobNoLinkDelay : FobReplyDelay);
        }

        static void TickFobReply()
        {
            if (fobReply == FobReply.None || Time.time < fobReplyAt) return;
            var r = fobReply;
            fobReply = FobReply.None;
            if (r == FobReply.NoLink)
            {
                if (HandFobAlive) Sfx.FobNoLink();
                return;
            }
            if (!FobHearsAny) return;
            if (r == FobReply.Locked) Sfx.FobLocked();
            else if (r == FobReply.Unlocked) Sfx.FobUnlocked();
            else Sfx.FobFail();
        }

        /// Presses the remote for lock/unlock. True if the car got it; otherwise the fob is set
        /// to buzz "no link" (if there is a live remote in hand at all).
        static bool SendFromFob()
        {
            bool live = HandFobAlive;
            if (PressFob() && Powered) return true;
            if (live) ReplyToFob(FobReply.NoLink);
            return false;
        }

        public static void OnLockPressed()
        {
            if (consumedLockPress) { consumedLockPress = false; return; }
            if (!SendFromFob()) return;

            if (DoorLocks.AnyDoorOpen || HoodOpen)
            {
                // Something is open: refuse to arm and complain.
                Chirp(3);
                errorUntil = Time.time + 1.2f;
                ReplyToFob(FobReply.Refused);
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
            ReplyToFob(FobReply.Locked);
        }

        public static void OnUnlockPressed()
        {
            if (!SendFromFob()) return;
            StopSiren();
            Current = Mode.Disarmed;
            DoorLocks.UnlockAll();
            // Disarming after a break-in: four chirps instead of two, like real units.
            Chirp(AlarmMemory ? 4 : 2);
            Flasher.Flash(2);
            AlarmMemory = false;
            ReplyToFob(FobReply.Unlocked);
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
            }
            Powered = now;
            if (Powered) pendingRelock = false;

            // Self test: one chirp once the alarm is complete, whichever part went in last (the
            // siren is often fitted after the power). Not on load, the car was already complete.
            bool sirenReady = SirenReady;
            if (sirenReady && !sirenWasReady && Time.time > selfTestFrom) Chirp(1);
            sirenWasReady = sirenReady;

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
                            // A full half-minute of siren is hard on a 15 A fuse.
                            if (SirenWailing && Random.value < SirenFuseChance) BlowFuse("siren cycle");
                            // Siren cycle over: back on guard with a fresh grace period.
                            StopSiren();
                            armedAt = Time.time;
                        }
                        break;
                }
            }
            else Flasher.SetContinuous(false);

            UpdateSiren();
            // Pager: the fob beeps along while the car is going off and the fob can hear it.
            Transform pagerAt = null;
            bool pager = Powered && Current == Mode.Triggered && FobHears(out pagerAt);
            Sfx.SetFobAlarm(pager, pagerAt);
            if (pager) KeySystem.DrainListening(PagerDrainPerSec * Time.deltaTime);
            Flasher.Tick();
            TickFobReply();
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
            return phase < n * 0.25f && Mathf.Repeat(phase, 0.25f) < 0.12f;
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
