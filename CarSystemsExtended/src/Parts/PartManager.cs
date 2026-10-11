using System.Collections.Generic;
using CSE.Core;
using CSE.Electrics;
using UnityEngine;

namespace CSE.Parts
{
    // Owns all spawned parts and the rules that connect them.
    public static class PartManager
    {
        public static readonly List<Part> Parts = new List<Part>();
        static readonly Dictionary<string, PartDef> defs = new Dictionary<string, PartDef>();
        static int nextUid = 1;

        public static void Reset()
        {
            Parts.Clear();
            defs.Clear();
            nextUid = 1;
            foreach (var d in PartCatalog.All) defs[d.Id] = d;
        }

        public static PartDef GetDef(string id)
        {
            PartDef d;
            return defs.TryGetValue(id, out d) ? d : null;
        }

        public static Part Spawn(PartDef def, Vector3 pos, Quaternion rot, string uid = null)
        {
            if (uid == null) uid = def.Id + "#" + nextUid++;
            else
            {
                // keep the counter ahead of loaded uids
                int hash = uid.LastIndexOf('#');
                int n;
                if (hash >= 0 && int.TryParse(uid.Substring(hash + 1), out n) && n >= nextUid) nextUid = n + 1;
            }
            var part = Part.Create(def, uid);
            part.transform.position = pos;
            part.transform.rotation = rot;
            Parts.Add(part);
            return part;
        }

        public static Part FindInstalled(string defId)
        {
            foreach (var p in Parts)
                if (p != null && p.Installed && p.Def.Id == defId) return p;
            return null;
        }

        public static bool AlarmUnitInstalled
        {
            get { return FindInstalled(PartCatalog.AlarmUnit.Id) != null; }
        }

        // ---------------------------------------------------------------- mounts

        /// Mount transform for a part: parent plus local position/rotation.
        public static bool ResolveMount(Part part, out Transform parent, out Vector3 pos, out Quaternion rot)
        {
            var m = part.Def.Mount;
            parent = null;
            pos = m.Offset;
            rot = Quaternion.Euler(m.Euler);

            if (m.ParentPart != null)
            {
                // Mounts onto another part, loose or installed.
                foreach (var p in Parts)
                {
                    if (p != null && p != part && p.Def.Id == m.ParentPart)
                    {
                        parent = p.transform;
                        return true;
                    }
                }
                return false;
            }

            if (GameRefs.Car == null) return false;
            parent = GameRefs.Car.transform;
            if (m.RefPath != null)
            {
                var anchor = GameRefs.FindInCar(m.RefPath);
                if (anchor == null || !anchor.gameObject.activeInHierarchy) return false;
                pos = parent.InverseTransformPoint(anchor.position) + m.Offset;
            }
            return true;
        }

        public static bool MountWorld(Part part, out Vector3 world)
        {
            Transform parent;
            Vector3 pos;
            Quaternion rot;
            world = Vector3.zero;
            if (!ResolveMount(part, out parent, out pos, out rot)) return false;
            world = parent.TransformPoint(pos);
            return true;
        }

        static bool SlotTaken(Part part)
        {
            foreach (var p in Parts)
                if (p != null && p != part && p.Installed && p.Def.Id == part.Def.Id) return true;
            return false;
        }

        public static bool CanAssembleHere(Part part)
        {
            if (SlotTaken(part)) return false;
            Vector3 world;
            if (!MountWorld(part, out world)) return false;
            return Vector3.Distance(part.transform.position, world) <= part.Def.Mount.Tolerance;
        }

        /// Reason a part cannot be taken off yet, or null if it can.
        public static string RemovalBlocker(Part part)
        {
            if (part.Def == PartCatalog.AlarmUnit)
            {
                if (AlarmWiring.PowerConnected) return "DISCONNECT POWER FIRST";
                foreach (var p in Parts)
                    if (p != null && p.WireConnected) return "DISCONNECT WIRES FIRST";
            }
            return null;
        }

        public static void OnPartChanged(Part part)
        {
            // Pulling the alarm unit drops every wire that ran to it.
            if (part.Def == PartCatalog.AlarmUnit && !part.Installed)
            {
                AlarmWiring.PowerConnected = false;
                foreach (var p in Parts) if (p != null) p.WireConnected = false;
            }
            AlarmWiring.Refresh();
        }

        // ---------------------------------------------------------------- frame

        public static void Tick()
        {
            Parts.RemoveAll(p => p == null);
            foreach (var p in Parts) p.Tick();
            if (Look.Hovered != null) Look.Hovered.OnHover();
        }
    }
}
