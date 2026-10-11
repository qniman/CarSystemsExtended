using System;
using System.Collections.Generic;
using CSE.Electrics;
using CSE.Parts;
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
    }

    public class PartSave
    {
        public string Def;
        public string Uid;
        public bool Installed;
        public float[] Pos;
        public float[] Rot;
        public int[] Bolts;
        public bool Wire;
        public bool Damaged;
    }

    public static class SaveSystem
    {
        const string File = "cse_save";

        public static void Save(Mod mod)
        {
            var data = new SaveData { AlarmPower = AlarmWiring.PowerConnected };
            foreach (var p in PartManager.Parts)
            {
                if (p == null) continue;
                var s = new PartSave
                {
                    Def = p.Def.Id,
                    Uid = p.Uid,
                    Installed = p.Installed,
                    Wire = p.WireConnected,
                    Damaged = p.Damaged,
                    Pos = V(p.transform.position),
                    Rot = Q(p.transform.rotation),
                    Bolts = new int[p.Bolts.Count],
                };
                for (int i = 0; i < p.Bolts.Count; i++) s.Bolts[i] = p.Bolts[i].Stage;
                data.Parts.Add(s);
            }
            SaveLoad.SerializeSaveFile(mod, data, File);
        }

        public static void Load(Mod mod)
        {
            SaveData data;
            try
            {
                data = SaveLoad.DeserializeSaveFile<SaveData>(mod, File);
            }
            catch (Exception e)
            {
                ModConsole.Warning("[CSE] Could not read save, starting fresh: " + e.Message);
                return;
            }
            if (data == null || data.Parts == null) return;

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
                spawned.Add(new KeyValuePair<Part, PartSave>(part, s));
            }

            foreach (var def in PartCatalog.All)
            {
                foreach (var kv in spawned)
                {
                    if (kv.Key.Def != def || !kv.Value.Installed) continue;
                    if (kv.Key.Install(kv.Value.Bolts, true))
                        kv.Key.WireConnected = kv.Value.Wire;
                    else
                        ModConsole.Warning("[CSE] Could not reinstall " + kv.Key.Uid + ", left loose");
                }
            }

            AlarmWiring.PowerConnected = data.AlarmPower;
            AlarmWiring.Refresh();
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
