using System.Collections.Generic;
using System.IO;
using CSE.Core;
using CSE.Parts;
using MSCLoader;
using UnityEngine;

namespace CSE.Debugging
{
    // Debug-only tool to place mount points in game. Shows a ghost of the selected part at its
    // mount. Keys while active (no numpad needed):
    //   Arrows       move left/right (X) and forward/back (Z)
    //   PgUp / PgDn  move up/down (Y)
    //   , / .        rotate around Y   (hold Ctrl: around X, Alt: around Z)
    //   Shift        fine steps (2 mm / 5 deg instead of 1 cm / 15 deg)
    //   Insert       move the mount to the point under the crosshair
    //   Home         next mount
    //   End          print + append to mounts.txt
    public static class MountEditor
    {
        public static bool Active { get; private set; }

        struct Entry
        {
            public PartDef Def;
            public int Index;

            public MountDef Mount
            {
                get { return Def.Mounts[Index]; }
            }

            public override string ToString()
            {
                return Def.Id + " [" + Index + (Mount.Label != null ? " " + Mount.Label : "") + "]";
            }
        }

        static readonly List<Entry> entries = new List<Entry>();
        static int current;
        static GameObject ghost;
        static string outFile;

        public static void Init(string configFolder)
        {
            outFile = Path.Combine(configFolder, "mounts.txt");
            entries.Clear();
            foreach (var d in PartCatalog.All)
                for (int i = 0; i < d.Mounts.Length; i++)
                    entries.Add(new Entry { Def = d, Index = i });
        }

        static Entry Selected
        {
            get { return entries[current]; }
        }

        public static string Status
        {
            get { return Active && entries.Count > 0 ? Selected.ToString() : null; }
        }

        public static void Toggle()
        {
            if (entries.Count == 0) return;
            Active = !Active;
            if (Active) BuildGhost();
            else DestroyGhost();
            ModConsole.Print("[CSE] Mount editor " + (Active ? "ON: " + Selected : "OFF"));
        }

        static void BuildGhost()
        {
            DestroyGhost();
            ghost = new GameObject("cse_ghost");
            Selected.Def.Build(ghost.transform);
            Shapes.Add(ghost.transform, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.012f, Color.magenta);
        }

        static void DestroyGhost()
        {
            if (ghost != null) Object.Destroy(ghost);
            ghost = null;
        }

        public static void Tick()
        {
            if (!Active) return;

            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            float step = shift ? 0.002f : 0.01f;
            float angle = shift ? 5f : 15f;
            var m = Selected.Mount;

            Vector3 move = Vector3.zero;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) move.x -= step;
            if (Input.GetKeyDown(KeyCode.RightArrow)) move.x += step;
            if (Input.GetKeyDown(KeyCode.UpArrow)) move.z += step;
            if (Input.GetKeyDown(KeyCode.DownArrow)) move.z -= step;
            if (Input.GetKeyDown(KeyCode.PageUp)) move.y += step;
            if (Input.GetKeyDown(KeyCode.PageDown)) move.y -= step;
            bool changed = move != Vector3.zero;
            m.Offset += move;

            int rot = Input.GetKeyDown(KeyCode.Comma) ? -1 : Input.GetKeyDown(KeyCode.Period) ? 1 : 0;
            if (rot != 0)
            {
                if (ctrl) m.Euler.x += rot * angle;
                else if (alt) m.Euler.z += rot * angle;
                else m.Euler.y += rot * angle;
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.Insert)) changed |= SnapToCrosshair(m);

            if (Input.GetKeyDown(KeyCode.Home))
            {
                current = (current + 1) % entries.Count;
                BuildGhost();
                ModConsole.Print("[CSE] Mount editor: " + Selected);
            }

            if (Input.GetKeyDown(KeyCode.End)) Print();

            if (changed)
                foreach (var p in PartManager.Parts)
                    if (p != null && p.Def == Selected.Def && p.MountIndex == Selected.Index) p.Reseat();

            PlaceGhost();
        }

        /// Moves the mount so its origin lands on the first solid surface under the crosshair.
        static bool SnapToCrosshair(MountDef m)
        {
            var cam = GameRefs.Camera;
            var anchor = PartManager.Anchor(m);
            if (cam == null || anchor == null) return false;

            RaycastHit hit;
            bool found = false;
            var hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, 3f);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                // skip triggers and our own ghost/parts
                if (h.collider.isTrigger || h.collider.GetComponentInParent<Part>() != null) continue;
                hit = h;
                found = true;
                Vector3 p = hit.point;
                var car = GameRefs.Car.transform;
                if (m.ParentPart != null) m.Offset = anchor.InverseTransformPoint(p);
                else if (m.AttachToRef) m.Offset = car.InverseTransformDirection(p - anchor.position);
                else m.Offset = car.InverseTransformPoint(p) - car.InverseTransformPoint(anchor.position);
                break;
            }
            if (!found) ModConsole.Print("[CSE] Mount editor: nothing under the crosshair");
            return found;
        }

        static void PlaceGhost()
        {
            if (ghost == null) return;
            Transform parent;
            Vector3 pos;
            Quaternion rot;
            bool ok = PartManager.ResolveMount(Selected.Mount, null, out parent, out pos, out rot);
            ghost.SetActive(ok);
            if (!ok) return;
            ghost.transform.position = parent.TransformPoint(pos);
            ghost.transform.rotation = parent.rotation * rot;
        }

        static void Print()
        {
            var m = Selected.Mount;
            string line = string.Format("{0}: Offset = new Vector3({1:0.###}f, {2:0.###}f, {3:0.###}f), Euler = new Vector3({4:0.#}f, {5:0.#}f, {6:0.#}f)",
                Selected, m.Offset.x, m.Offset.y, m.Offset.z, m.Euler.x, m.Euler.y, m.Euler.z);
            ModConsole.Print("[CSE] " + line);
            if (outFile != null) File.AppendAllText(outFile, line + "\n");
        }
    }
}
