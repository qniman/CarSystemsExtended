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

        public static void Remove(Part part)
        {
            Parts.Remove(part);
            if (part != null) Object.Destroy(part.gameObject);
        }

        public static Part FindInstalled(string defId)
        {
            foreach (var p in Parts)
                if (p != null && p.Installed && p.Def.Id == defId) return p;
            return null;
        }

        public static Part FindInstalled(string defId, int mountIndex)
        {
            foreach (var p in Parts)
                if (p != null && p.Installed && p.Def.Id == defId && p.MountIndex == mountIndex) return p;
            return null;
        }

        public static bool AlarmUnitInstalled
        {
            get { return FindInstalled(PartCatalog.AlarmUnit.Id) != null; }
        }

        // ---------------------------------------------------------------- mounts

        /// Mount transform for a part: parent plus local position/rotation.
        public static bool ResolveMount(Part part, int index, out Transform parent, out Vector3 pos, out Quaternion rot)
        {
            parent = null;
            pos = Vector3.zero;
            rot = Quaternion.identity;
            var mounts = part.Def.Mounts;
            if (index < 0 || index >= mounts.Length) return false;
            return ResolveMount(mounts[index], part, out parent, out pos, out rot);
        }

        /// The transform a mount is measured from: the parent part, the RefPath object or the car.
        public static Transform Anchor(MountDef m, Part self = null)
        {
            if (m.ParentPart != null)
            {
                // Mounts onto another part, loose or installed.
                foreach (var p in Parts)
                    if (p != null && p != self && p.Def.Id == m.ParentPart) return p.transform;
                return null;
            }
            var car = GameRefs.Car;
            if (car == null) return null;
            if (m.RefPath == null) return car.transform;
            var anchor = GameRefs.FindInCar(m.RefPath);
            return anchor != null && anchor.gameObject.activeInHierarchy ? anchor : null;
        }

        public static bool ResolveMount(MountDef m, Part self, out Transform parent, out Vector3 pos, out Quaternion rot)
        {
            parent = null;
            pos = Vector3.zero;
            rot = Quaternion.identity;
            var anchor = Anchor(m, self);
            if (anchor == null) return false;

            if (m.ParentPart != null)
            {
                parent = anchor;
                pos = m.Offset;
                rot = Quaternion.Euler(m.Euler);
                return true;
            }

            var car = GameRefs.Car;
            if (m.AttachToRef)
            {
                // Express the car-space offset in the anchor's rest frame (its parent), so the
                // result is right even if the anchor (a door) is swung open right now.
                Transform frame = anchor.parent ?? anchor;
                Quaternion carToFrame = Quaternion.Inverse(frame.rotation) * car.transform.rotation;
                parent = anchor;
                pos = carToFrame * m.Offset;
                rot = carToFrame * Quaternion.Euler(m.Euler);
                return true;
            }

            parent = car.transform;
            pos = car.transform.InverseTransformPoint(anchor.position) + m.Offset;
            rot = Quaternion.Euler(m.Euler);
            return true;
        }

        public static bool MountWorld(Part part, int index, out Vector3 world)
        {
            Transform parent;
            Vector3 pos;
            Quaternion rot;
            world = Vector3.zero;
            if (!ResolveMount(part, index, out parent, out pos, out rot)) return false;
            world = parent.TransformPoint(pos);
            return true;
        }

        static bool SlotTaken(Part part, int index)
        {
            foreach (var p in Parts)
                if (p != null && p != part && p.Installed && p.Def.Id == part.Def.Id && p.MountIndex == index) return true;
            return false;
        }

        /// Index of the free mount the held part is close enough to, or -1.
        public static int AssemblyCandidate(Part part)
        {
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < part.Def.Mounts.Length; i++)
            {
                if (SlotTaken(part, i)) continue;
                Vector3 world;
                if (!MountWorld(part, i, out world)) continue;
                float d = Vector3.Distance(part.transform.position, world);
                if (d <= part.Def.Mounts[i].Tolerance && d < bestDist)
                {
                    best = i;
                    bestDist = d;
                }
            }
            return best;
        }

        /// Reason a part cannot be taken off yet, or null if it can.
        public static string RemovalBlocker(Part part)
        {
            if (part.Def == PartCatalog.AlarmUnit)
            {
                // Device wires are unplugged at the device end.
                foreach (var p in Parts)
                    if (p != null && p.WireConnected) return "UNPLUG " + p.Def.Name.ToUpper() + " FIRST";
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
            for (int i = Parts.Count - 1; i >= 0; i--) Parts[i].Tick();
            if (Look.Hovered != null) Look.Hovered.OnHover();
        }
    }
}
