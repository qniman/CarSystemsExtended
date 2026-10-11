using System.IO;
using CSE.Core;
using CSE.Parts;
using MSCLoader;
using UnityEngine;

namespace CSE.Debugging
{
    // Debug-only tool to place mount points in game. Shows a ghost of the selected part at its
    // mount and moves it with the numpad:
    //   4/6 X, 8/2 Z, 9/3 Y (1 cm, hold Shift for 2 mm)
    //   7/1 rotate Y (hold Ctrl: X, Alt: Z), 15 deg (Shift: 5 deg)
    //   5 next part, Enter print + append to mounts.txt
    public static class MountEditor
    {
        public static bool Active { get; private set; }

        static int index;
        static GameObject ghost;
        static string outFile;

        public static void Init(string configFolder)
        {
            outFile = Path.Combine(configFolder, "mounts.txt");
        }

        static PartDef Selected
        {
            get { return PartCatalog.All[index]; }
        }

        public static void Toggle()
        {
            Active = !Active;
            if (Active) BuildGhost();
            else DestroyGhost();
            ModConsole.Print("[CSE] Mount editor " + (Active ? "ON: " + Selected.Id : "OFF"));
        }

        static void BuildGhost()
        {
            DestroyGhost();
            ghost = new GameObject("cse_ghost_" + Selected.Id);
            Selected.Build(ghost.transform);
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

            bool shift = Input.GetKey(KeyCode.LeftShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt);
            float step = shift ? 0.002f : 0.01f;
            float angle = shift ? 5f : 15f;
            var m = Selected.Mount;
            bool changed = false;

            if (Input.GetKeyDown(KeyCode.Keypad4)) { m.Offset.x -= step; changed = true; }
            if (Input.GetKeyDown(KeyCode.Keypad6)) { m.Offset.x += step; changed = true; }
            if (Input.GetKeyDown(KeyCode.Keypad8)) { m.Offset.z += step; changed = true; }
            if (Input.GetKeyDown(KeyCode.Keypad2)) { m.Offset.z -= step; changed = true; }
            if (Input.GetKeyDown(KeyCode.Keypad9)) { m.Offset.y += step; changed = true; }
            if (Input.GetKeyDown(KeyCode.Keypad3)) { m.Offset.y -= step; changed = true; }

            int rot = Input.GetKeyDown(KeyCode.Keypad7) ? -1 : Input.GetKeyDown(KeyCode.Keypad1) ? 1 : 0;
            if (rot != 0)
            {
                if (ctrl) m.Euler.x += rot * angle;
                else if (alt) m.Euler.z += rot * angle;
                else m.Euler.y += rot * angle;
                changed = true;
            }

            if (Input.GetKeyDown(KeyCode.Keypad5))
            {
                index = (index + 1) % PartCatalog.All.Length;
                BuildGhost();
                ModConsole.Print("[CSE] Mount editor: " + Selected.Id);
            }

            if (Input.GetKeyDown(KeyCode.KeypadEnter)) Print();

            if (changed)
                foreach (var p in PartManager.Parts)
                    if (p != null && p.Def == Selected) p.Reseat();

            PlaceGhost();
        }

        static void PlaceGhost()
        {
            if (ghost == null) return;
            // Resolve against a throwaway part-less lookup: reuse any spawned instance if there is one.
            Part any = null;
            foreach (var p in PartManager.Parts)
                if (p != null && p.Def == Selected) { any = p; break; }

            Transform parent = null;
            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;
            bool ok = any != null && PartManager.ResolveMount(any, out parent, out pos, out rot);
            ghost.SetActive(ok);
            if (!ok) return;
            ghost.transform.position = parent.TransformPoint(pos);
            ghost.transform.rotation = parent.rotation * rot;
        }

        static void Print()
        {
            var m = Selected.Mount;
            string line = string.Format("{0}: Offset = new Vector3({1:0.###}f, {2:0.###}f, {3:0.###}f), Euler = new Vector3({4:0.#}f, {5:0.#}f, {6:0.#}f)",
                Selected.Id, m.Offset.x, m.Offset.y, m.Offset.z, m.Euler.x, m.Euler.y, m.Euler.z);
            ModConsole.Print("[CSE] " + line);
            if (outFile != null) File.AppendAllText(outFile, line + "\n");
        }
    }
}
