using System;
using System.Collections.Generic;
using CSE.Electrics;
using CSE.Parts;
using CSE.Shop;
using CSE.Systems;
using MSCLoader;
using UnityEngine;

namespace CSE.Save
{
    // Plain data written with MSCLoader's SaveLoad. Vectors are stored as float arrays to keep
    // the serializer away from Unity types.
    public class SaveData
    {
        public int Version = 1;
        public List<PartSave> Parts = new List<PartSave>();
        public bool AlarmPower;
        public bool AlarmArmed;
        /// Saves before physical keys: the remote was simply "on the keys".
        public bool FobOnKeys;
        public bool BrochureGiven;
        /// Physical keys: set once keyring state is saved (KeyRing.Serialize, null = none there).
        public bool KeysSaved;
        public string KeysPocket;
        public string KeysIgnition;
        public bool SpareKeyGiven;
        public List<MailOrder.Order> Orders = new List<MailOrder.Order>();
    }

    public class PartSave
    {
        public string Def;
        public string Uid;
        public bool Installed;
        public int Mount; // 0 in saves from before multi-mount parts, which is correct for them
        public float[] Pos;
        public float[] Rot;
        public int[] Bolts;
        public bool Wire;
        public bool Damaged;
        public string Data;
        public int Price;
    }

    public static class SaveSystem
    {
        const string File = "cse_save";

        public static void Save(Mod mod)
        {
            var data = new SaveData
            {
                AlarmPower = AlarmWiring.PowerConnected,
                AlarmArmed = AlarmSystem.Current != AlarmSystem.Mode.Disarmed,
                BrochureGiven = MailOrder.BrochureGiven,
                KeysSaved = true,
                KeysPocket = KeySystem.Pocket != null ? KeySystem.Pocket.Serialize() : null,
                KeysIgnition = KeySystem.Ignition != null ? KeySystem.Ignition.Serialize() : null,
                SpareKeyGiven = KeySystem.SpareGiven,
                Orders = new List<MailOrder.Order>(MailOrder.Pending),
            };
            foreach (var p in PartManager.Parts)
            {
                if (p == null) continue;
                var s = new PartSave
                {
                    Def = p.Def.Id,
                    Uid = p.Uid,
                    Installed = p.Installed,
                    Mount = p.MountIndex,
                    Wire = p.WireConnected,
                    Damaged = p.Damaged,
                    Data = p.Data,
                    Price = p.Price,
                    Pos = V(p.transform.position),
                    Rot = Q(p.transform.rotation),
                    Bolts = new int[p.Bolts.Count],
                };
                for (int i = 0; i < p.Bolts.Count; i++) s.Bolts[i] = p.Bolts[i].Stage;
                data.Parts.Add(s);
            }
            SaveLoad.SerializeSaveFile(mod, data, File);
        }

        /// Restores parts and systems. Returns the save (null if there is none) for the systems
        /// that need to restore after everything else is in place (keys).
        public static SaveData Load(Mod mod)
        {
            SaveData data;
            try
            {
                data = SaveLoad.DeserializeSaveFile<SaveData>(mod, File);
            }
            catch (Exception e)
            {
                ModConsole.Warning("[CSE] Could not read save, starting fresh: " + e.Message);
                return null;
            }
            if (data == null || data.Parts == null) return null;

            // Spawn everything loose first, then install in catalog order so parents exist
            // before the parts that mount onto them.
            var spawned = new List<KeyValuePair<Part, PartSave>>();
            foreach (var s in data.Parts)
            {
                var def = PartManager.GetDef(s.Def);
                if (def == null)
                {
                    ModConsole.Warning("[CSE] Unknown part in save: " + s.Def);
                    continue;
                }
                var part = PartManager.Spawn(def, ToV(s.Pos), ToQ(s.Rot), s.Uid);
                part.Damaged = s.Damaged;
                part.ApplyDamagedLook();
                part.Data = s.Data;
                if (s.Price > 0) part.SetForSale(s.Price);
                if (def == PartCatalog.Keys) KeyRingView.Decorate(part, KeyRing.Parse(s.Data));
                spawned.Add(new KeyValuePair<Part, PartSave>(part, s));
            }

            foreach (var def in PartCatalog.All)
            {
                foreach (var kv in spawned)
                {
                    if (kv.Key.Def != def || !kv.Value.Installed) continue;
                    if (kv.Key.Install(kv.Value.Mount, kv.Value.Bolts, true))
                        kv.Key.WireConnected = kv.Value.Wire;
                    else
                        ModConsole.Warning("[CSE] Could not reinstall " + kv.Key.Uid + ", left loose");
                }
            }

            AlarmWiring.PowerConnected = data.AlarmPower;
            AlarmWiring.Refresh();
            AlarmSystem.Restore(data.AlarmArmed);
            MailOrder.BrochureGiven = data.BrochureGiven;
            if (data.Orders != null) MailOrder.Pending.AddRange(data.Orders);
            return data;
        }

        static float[] V(Vector3 v) { return new[] { v.x, v.y, v.z }; }
        static float[] Q(Quaternion q) { return new[] { q.x, q.y, q.z, q.w }; }

        static Vector3 ToV(float[] a)
        {
            return a != null && a.Length == 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        }

        static Quaternion ToQ(float[] a)
        {
            return a != null && a.Length == 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.identity;
        }
    }
}
