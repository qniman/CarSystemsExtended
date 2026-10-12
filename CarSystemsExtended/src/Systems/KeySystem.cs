using System.Globalization;
using CSE.Core;
using CSE.Parts;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace CSE.Systems
{
    // One Satsuma keyring: the key, and maybe the alarm remote with its battery.
    public class KeyRing
    {
        public bool Fob;
        public float Battery = 100f; // fob battery, percent
        public bool Spare;           // the spare key from the nail at home

        public string Serialize()
        {
            return (Fob ? "1" : "0") + ";" + Battery.ToString("0.###", CultureInfo.InvariantCulture) + ";" + (Spare ? "1" : "0");
        }

        public static KeyRing Parse(string s)
        {
            var r = new KeyRing();
            if (string.IsNullOrEmpty(s)) return r;
            var p = s.Split(';');
            if (p.Length > 0) r.Fob = p[0] == "1";
            float b;
            if (p.Length > 1 && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out b)) r.Battery = Mathf.Clamp(b, 0f, 100f);
            if (p.Length > 2) r.Spare = p[2] == "1";
            return r;
        }
    }

    // Satsuma keys as a real object. A keyring is in exactly one place:
    //   in the world   - a loose "satsuma keys" Part (Part.Data holds the KeyRing)
    //   in the pocket  - picked up; key 3 takes it out into the right hand
    //   in the ignition - the game's own key mesh, with our keyring hanging from it
    // The game only lets a key into the lock while the global PlayerKeySatsuma is 1, so we keep
    // it at 1 only while keys are in hand or already in the lock.
    public static class KeySystem
    {
        const float DrunkLevel = 1.5f;       // PlayerDrunk above this: keys may fall out
        const float SoberLevel = 0.3f;       // below this the "already lost them tonight" resets
        const float DrunkRollEvery = 60f;
        const float DrunkLoseChance = 0.04f;
        const float HintTime = 2f;
        const float StandbyDrainPerDay = 1f; // remote battery, percent

        static FsmInt globalDay;
        static int lastDay;

        public static KeyRing Pocket { get; private set; }
        public static KeyRing Ignition { get; private set; }
        public static bool InHand { get; private set; }
        public static bool SpareGiven;

        static FsmInt playerKey;
        static FsmFloat drunk;
        static FsmBool handRight;
        static PlayMakerFSM selection;
        static Transform keyInLock;
        static bool lockWasOccupied;
        static float nextDrunkRoll;
        static bool lostWhileDrunk;
        static FsmString subtitle;
        static float hintUntil;
        static SettingsKeybind takeOutKey, dropKey;

        public static void Init(SettingsKeybind takeOut, SettingsKeybind drop)
        {
            takeOutKey = takeOut;
            dropKey = drop;
            Pocket = null;
            Ignition = null;
            InHand = false;
            SpareGiven = false;
            lostWhileDrunk = false;
            nextDrunkRoll = Time.time + DrunkRollEvery;

            var g = FsmVariables.GlobalVariables;
            playerKey = g.FindFsmInt("PlayerKeySatsuma");
            drunk = g.FindFsmFloat("PlayerDrunk");
            handRight = g.FindFsmBool("PlayerHandRight");
            subtitle = g.FindFsmString("GUIsubtitle");
            globalDay = g.FindFsmInt("GlobalDay");
            lastDay = globalDay != null ? globalDay.Value : 0;

            var sel = GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera/SelectItem");
            selection = sel != null ? sel.GetPlayMaker("Selection") : null;
            keyInLock = GameRefs.FindInCar("Dashboard/Steering/steering_column2/Ignition/Keys/Key");

            PartCatalog.Keys.OnHeld = KeysHeld;
            KeyRingView.Init();

            if (playerKey == null || keyInLock == null)
                ModConsole.Error("[CSE] Satsuma key globals/mesh not found, physical keys disabled");
            if (selection == null)
                ModConsole.Warning("[CSE] Item selection FSM not found, key 3 cannot put tools away");
        }

        static bool LockOccupied
        {
            get { return keyInLock != null && keyInLock.gameObject.activeSelf; }
        }

        /// After loading: the saved keyrings, or (old save / fresh install) the key where the
        /// game has it: in the lock, otherwise in the pocket. legacyFob = remote was on the keys.
        public static void Restore(string pocket, string ignition, bool hasSave, bool legacyFob)
        {
            // hasSave with both null is valid: the keys are lying somewhere in the world.
            if (hasSave)
            {
                Pocket = pocket != null ? KeyRing.Parse(pocket) : null;
                Ignition = ignition != null ? KeyRing.Parse(ignition) : null;
            }
            else
            {
                var ring = new KeyRing { Fob = legacyFob };
                if (LockOccupied) Ignition = ring;
                else Pocket = ring;
            }
            // The game decides what is in the lock; keep our record in line with it. (A key in
            // the lock we have no record of: take the pocket ring rather than inventing one.)
            if (LockOccupied && Ignition == null)
            {
                Ignition = Pocket ?? new KeyRing();
                Pocket = null;
            }
            if (!LockOccupied && Ignition != null)
            {
                if (Pocket == null) Pocket = Ignition;
                Ignition = null;
            }
            lockWasOccupied = LockOccupied;
        }

        /// The spare key hangs at home once per game (next to the parts magazine).
        public static void AfterLoad()
        {
            if (SpareGiven) return;
            SpareGiven = true;
            var magazine = GameObject.Find("ITEMS/parts magazine(itemx)");
            Vector3 pos = magazine != null
                ? magazine.transform.position + new Vector3(-0.3f, 0.1f, 0.1f)
                : GameRefs.Camera.transform.position + GameRefs.Camera.transform.forward * 0.8f;
            Spawn(new KeyRing { Spare = true }, pos, Vector3.zero);
            ModConsole.Print("[CSE] Spare Satsuma key left next to the parts magazine");
        }

        public static void ResetState()
        {
            HideHand();
            Pocket = LockOccupied ? null : new KeyRing();
            Ignition = LockOccupied ? new KeyRing() : null;
            SpareGiven = false;
            AfterLoad();
        }

        // ---------------------------------------------------------------- world <-> pocket

        static Part Spawn(KeyRing ring, Vector3 pos, Vector3 velocity)
        {
            var part = PartManager.Spawn(PartCatalog.Keys, pos, Random.rotation);
            part.Data = ring.Serialize();
            KeyRingView.Decorate(part, ring);
            var rb = part.GetComponent<Rigidbody>();
            if (rb != null) rb.velocity = velocity;
            return part;
        }

        /// Keyring held in the game's hand (LMB, like any item - to move it around).
        /// Use (F) puts it in the pocket.
        static bool KeysHeld(Part keys)
        {
            if (Pocket != null)
            {
                Gui.Interaction(L.T("keys_pocket_full"));
                return true;
            }
            Gui.Interaction(L.T("keys_to_pocket"));
            if (!GameRefs.UseButtonDown) return true;

            Pocket = KeyRing.Parse(keys.Data);
            Sfx.Game("PlayerMisc", "pocket", keys.transform.position, 0.8f);
            PartManager.Remove(keys);
            Hint(L.F("keys_picked", KeyName(takeOutKey)));
            return true;
        }

        static void Drop(Vector3 pos, Vector3 velocity)
        {
            if (Pocket == null) return;
            HideHand();
            Spawn(Pocket, pos, velocity);
            Pocket = null;
        }

        // ---------------------------------------------------------------- hand

        public static void TakeOutOrPutAway()
        {
            if (InHand)
            {
                HideHand();
                Sfx.Game("PlayerMisc", "pocket", GameRefs.Camera.transform.position, 0.6f);
                return;
            }
            if (Pocket == null)
            {
                Hint(L.T(Ignition != null ? "keys_in_ignition" : "keys_none"));
                return;
            }
            // Keys go in the right hand: put the tools away the way key 1 does.
            if (handRight != null && handRight.Value && selection != null) selection.SendEvent("ITEM1");
            InHand = true;
            KeyRingView.ShowInHand(Pocket);
            Sfx.Game("PlayerMisc", "pocket", GameRefs.Camera.transform.position, 0.6f);
        }

        public static void HideHand()
        {
            InHand = false;
            KeyRingView.HideHand();
        }

        /// The ring whose remote can be pressed right now (keys in hand).
        public static KeyRing HandRing
        {
            get { return InHand ? Pocket : null; }
        }

        // ---------------------------------------------------------------- frame

        public static void Tick()
        {
            if (playerKey == null || keyInLock == null) return;

            // The game's 1/2 keys take the hand for themselves.
            if (InHand && (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Alpha2))) HideHand();
            if (takeOutKey != null && takeOutKey.GetKeybindDown()) TakeOutOrPutAway();
            if (InHand && dropKey != null && dropKey.GetKeybindDown())
            {
                var cam = GameRefs.Camera.transform;
                Drop(cam.position + cam.forward * 0.4f - cam.up * 0.15f, cam.forward * 1.5f);
            }

            SyncIgnition();
            playerKey.Value = InHand || Ignition != null ? 1 : 0;

            TickDrunk();
            if (globalDay != null && globalDay.Value != lastDay)
            {
                lastDay = globalDay.Value;
                OnNewDay(StandbyDrainPerDay);
            }
            KeyRingView.Tick(InHand ? Pocket : null, Ignition);

            if (hintUntil > 0f && Time.time > hintUntil)
            {
                hintUntil = 0f;
                if (subtitle != null) subtitle.Value = "";
            }
        }

        /// Follows the game's key mesh: shown = the key went in, hidden = it came out.
        static void SyncIgnition()
        {
            bool now = LockOccupied;
            if (now == lockWasOccupied) return;
            lockWasOccupied = now;

            if (now)
            {
                // Only possible with keys in hand (PlayerKeySatsuma), so it is the pocket ring.
                Ignition = Pocket ?? new KeyRing();
                Pocket = null;
                HideHand();
            }
            else if (Ignition != null)
            {
                if (Pocket == null)
                {
                    Pocket = Ignition;
                    InHand = true;
                    KeyRingView.ShowInHand(Pocket);
                }
                else
                {
                    // Already carrying the other keyring: this one lands on the driver's seat.
                    var cam = GameRefs.Camera.transform;
                    Spawn(Ignition, cam.position + cam.forward * 0.3f - cam.up * 0.3f, Vector3.zero);
                }
                Ignition = null;
            }
        }

        static void TickDrunk()
        {
            if (drunk == null) return;
            if (drunk.Value < SoberLevel) lostWhileDrunk = false;
            if (Pocket == null || lostWhileDrunk || drunk.Value < DrunkLevel || Time.time < nextDrunkRoll) return;
            nextDrunkRoll = Time.time + DrunkRollEvery;
            if (Random.value > DrunkLoseChance) return;

            // They slip out unnoticed where you stand - no hint, you find out later.
            lostWhileDrunk = true;
            var cam = GameRefs.Camera.transform;
            ModConsole.Log("[CSE] Keys fell out of the pocket (drunk)");
            Drop(cam.position - Vector3.up * 1.2f + Random.insideUnitSphere * 0.3f, Vector3.zero);
        }

        // ---------------------------------------------------------------- hints

        static string KeyName(SettingsKeybind k)
        {
            return k != null ? k.GetKeyValue.ToString().Replace("Alpha", "") : "3";
        }

        /// A remote button was pressed with the keys not in hand: say where they are.
        public static void RemoteNotInHand()
        {
            if (InHand) return;
            if (Pocket != null && Pocket.Fob) Hint(L.F("keys_take_out", KeyName(takeOutKey)));
        }

        static void Hint(string text)
        {
            if (subtitle == null) return;
            subtitle.Value = text;
            hintUntil = Time.time + HintTime;
        }

        // ---------------------------------------------------------------- all keyrings

        /// Every keyring with a remote that could hear the car, and where it is (null = with the
        /// player or in the car, so a 2D sound in the player's hand).
        public static bool FindListeningRemote(float range, out Transform where)
        {
            where = null;
            var car = GameRefs.Car.transform.position;
            if (Ignition != null && Ignition.Fob && Ignition.Battery > 0f) return true;
            var cam = GameRefs.Camera;
            if (Pocket != null && Pocket.Fob && Pocket.Battery > 0f && cam != null &&
                Vector3.Distance(cam.transform.position, car) <= range) return true;
            foreach (var p in PartManager.Parts)
            {
                if (p == null || p.Def != PartCatalog.Keys) continue;
                var r = KeyRing.Parse(p.Data);
                if (!r.Fob || r.Battery <= 0f) continue;
                if (Vector3.Distance(p.transform.position, car) > range) continue;
                where = p.transform;
                return true;
            }
            return false;
        }

        /// Drains every remote that is listening to the pager.
        public static void DrainListening(float amount)
        {
            if (Ignition != null && Ignition.Fob) Ignition.Battery = Mathf.Max(0f, Ignition.Battery - amount);
            if (Pocket != null && Pocket.Fob) Pocket.Battery = Mathf.Max(0f, Pocket.Battery - amount);
        }

        /// Remote standby drain, once per game day.
        public static void OnNewDay(float amount)
        {
            DrainListening(amount);
            foreach (var p in PartManager.Parts)
            {
                if (p == null || p.Def != PartCatalog.Keys) continue;
                var r = KeyRing.Parse(p.Data);
                if (!r.Fob) continue;
                r.Battery = Mathf.Max(0f, r.Battery - amount);
                p.Data = r.Serialize();
            }
        }

        public static string StatusText
        {
            get
            {
                string Ring(KeyRing r)
                {
                    return r == null ? "-" : (r.Spare ? "spare" : "main") + (r.Fob ? " fob " + r.Battery.ToString("0") + "%" : " no fob");
                }
                return "pocket " + Ring(Pocket) + (InHand ? " (in hand)" : "") + ", ignition " + Ring(Ignition) +
                       ", PlayerKeySatsuma " + (playerKey != null ? playerKey.Value.ToString() : "?");
            }
        }
    }
}
