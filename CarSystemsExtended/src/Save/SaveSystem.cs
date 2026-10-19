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
        const string Backup = "cse_save.bak";

        /// The main file read fine (or was written by us) this session, so it is a good backup.
        static bool mainIsGood;

        public static void Save(Mod mod)
        {
            // A crash or a full disk mid-write would otherwise leave nothing to come back to.
            try
            {
                string folder = ModLoader.GetModSettingsFolder(mod);
                string main = System.IO.Path.Combine(folder, File);
                if (mainIsGood && System.IO.File.Exists(main))
                    System.IO.File.Copy(main, System.IO.Path.Combine(folder, Backup), true);
            }
            catch (Exception e)
            {
                ModConsole.Warning("[CSE] Could not back up the save: " + e.Message);
            }

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
                    // still waiting for its mount after the last load: keep it installed
                    Installed = p.Installed || p.AwaitingRestore,
                    Mount = p.AwaitingRestore ? p.RestoreMount : p.MountIndex,
                    Wire = p.AwaitingRestore ? p.RestoreWire : p.WireConnected,
                    Damaged = p.Damaged,
                    Data = p.Data,
                    Price = p.Price,
                    Pos = V(p.transform.position),
                    Rot = Q(p.transform.rotation),
                    Bolts = new int[p.Bolts.Count],
                };
                for (int i = 0; i < p.Bolts.Count; i++) s.Bolts[i] = p.Bolts[i].Stage;
                if (p.AwaitingRestore && p.RestoreBolts != null) s.Bolts = p.RestoreBolts;
                data.Parts.Add(s);
            }
            SaveLoad.SerializeSaveFile(mod, data, File);
            mainIsGood = true;
        }

        static SaveData Read(Mod mod, string file)
        {
            try
            {
                return SaveLoad.DeserializeSaveFile<SaveData>(mod, file);
            }
            catch (Exception e)
            {
                ModConsole.Warning("[CSE] Could not read " + file + ": " + e.Message);
                return null;
            }
        }

        /// Restores parts and systems. Returns the save (null if there is none) for the systems
        /// that need to restore after everything else is in place (keys).
        public static SaveData Load(Mod mod)
        {
            mainIsGood = false;
            string folder = ModLoader.GetModSettingsFolder(mod);
            bool mainExists = System.IO.File.Exists(System.IO.Path.Combine(folder, File));
            var data = mainExists ? Read(mod, File) : null;
            if (data != null && data.Parts != null) mainIsGood = true;
            else if (mainExists)
            {
                // Unreadable (not missing: a deleted save means a fresh start). Keep the broken
                // file for a bug report and continue from the backup.
                try
                {
                    System.IO.File.Copy(System.IO.Path.Combine(folder, File),
                        System.IO.Path.Combine(folder, File + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")), true);
                }
                catch (Exception) { }
                data = Read(mod, Backup);
                if (data != null && data.Parts != null)
                    ModConsole.Warning("[CSE] Save was unreadable, restored the previous one (" + Backup + ")");
                else
                {
                    ModConsole.Warning("[CSE] No readable save, starting fresh");
                    return null;
                }
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
                    if (kv.Key.Install(kv.Value.Mount, kv.Value.Bolts, true, true))
                        kv.Key.WireConnected = kv.Value.Wire;
                    else
                    {
                        // The mount may only appear a moment later (game parts still attaching,
                        // other mods); keep trying instead of dropping the part.
                        ModConsole.Log("[CSE] Mount of " + kv.Key.Uid + " not there yet, retrying");
                        kv.Key.RestoreLater(kv.Value.Mount, kv.Value.Bolts, kv.Value.Wire);
                    }
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
