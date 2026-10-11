using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace CSE.Research
{
    // Read-only research mod: dumps game objects and PlayMaker FSMs to text files.
    // F8  - full dump of SATSUMA, global variables, scene roots, keyword search
    // F9  - snapshot of all variable values (Satsuma + globals) and diff vs previous snapshot
    // F10 - full dump of the object under the crosshair (and its parents' FSMs)
    public class Research : Mod
    {
        public override string ID => "CSE_Research";
        public override string Name => "Car Systems Extended - Research";
        public override string Author => "CSE";
        public override string Version => "0.1.0";
        public override string Description => "Debug tool: dumps Satsuma FSMs to text files. Read-only.";

        static readonly string[] Keywords =
        {
            "catalog", "envelope", "mail", "post", "order", "package", "parcel",
            "key", "door", "ignition", "starter", "choke", "battery", "fuse", "wiring",
            "gauge", "dashboard", "bumper", "teimo", "fleetari", "store", "shop"
        };

        SettingsKeybind dumpAllKey, snapshotKey, lookKey, playerKey;
        string outDir;
        Dictionary<string, string> lastSnapshot;
        int snapshotIndex;

        public override void ModSetup()
        {
            SetupFunction(Setup.OnLoad, OnLoad);
            SetupFunction(Setup.Update, OnUpdate);
            SetupFunction(Setup.ModSettings, CreateSettings);
        }

        void CreateSettings()
        {
            dumpAllKey = Keybind.Add("cse_dump_all", "Full dump (Satsuma + world)", KeyCode.F8);
            snapshotKey = Keybind.Add("cse_snapshot", "Variable snapshot + diff", KeyCode.F9);
            lookKey = Keybind.Add("cse_look", "Dump object under crosshair", KeyCode.F10);
            playerKey = Keybind.Add("cse_player", "Dump PLAYER (hands, tools)", KeyCode.F11);
        }

        void OnLoad()
        {
            outDir = System.IO.Path.Combine(ModLoader.GetModSettingsFolder(this), "Dumps");
            Directory.CreateDirectory(outDir);
            ModConsole.Print("[CSE Research] Ready. F8 = full dump, F9 = snapshot/diff, F10 = look dump. Output: " + outDir);
        }

        void OnUpdate()
        {
            try
            {
                if (dumpAllKey.GetKeybindDown()) DumpAll();
                if (snapshotKey.GetKeybindDown()) Snapshot();
                if (lookKey.GetKeybindDown()) DumpLook();
                if (playerKey.GetKeybindDown()) DumpPlayer();
            }
            catch (Exception e)
            {
                ModConsole.Error("[CSE Research] " + e);
            }
        }

        // ---------------------------------------------------------------- F8

        void DumpAll()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            GameObject car = FindSatsuma();
            if (car == null)
            {
                ModConsole.Error("[CSE Research] SATSUMA not found");
                return;
            }

            var sb = new StringBuilder();
            DumpHierarchy(car.transform, 0, sb);
            Write("satsuma_hierarchy.txt", sb);

            sb = new StringBuilder();
            foreach (var fsm in car.GetComponentsInChildren<PlayMakerFSM>(true))
                DumpFsm(fsm, sb, true);
            Write("satsuma_fsm.txt", sb);

            sb = new StringBuilder();
            sb.AppendLine("== GLOBAL VARIABLES ==");
            foreach (var v in FsmVariables.GlobalVariables.GetAllNamedVariables())
                sb.AppendLine(VarLine(v));
            Write("globals.txt", sb);

            sb = new StringBuilder();
            var all = AllSceneObjects();
            foreach (var go in all)
                if (go.transform.parent == null)
                    sb.AppendLine((go.activeSelf ? "[+] " : "[-] ") + go.name + "  " + V(go.transform.position) + "  fsm:" + FsmNames(go));
            Write("scene_roots.txt", sb);

            sb = new StringBuilder();
            foreach (var go in all)
            {
                string lower = go.name.ToLowerInvariant();
                foreach (var k in Keywords)
                {
                    if (lower.Contains(k))
                    {
                        sb.AppendLine(Path(go.transform) + "  [" + k + "]  active:" + go.activeInHierarchy + "  fsm:" + FsmNames(go));
                        break;
                    }
                }
            }
            Write("keyword_search.txt", sb);

            ModConsole.Print("[CSE Research] Full dump done in " + sw.ElapsedMilliseconds + " ms -> " + outDir);
        }

        // ---------------------------------------------------------------- F9

        void Snapshot()
        {
            var current = new Dictionary<string, string>();
            GameObject car = FindSatsuma();
            if (car != null)
            {
                foreach (var fsm in car.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    string prefix = Path(fsm.transform) + " :: " + fsm.FsmName;
                    current[prefix + " :: <state>"] = fsm.ActiveStateName;
                    foreach (var v in fsm.FsmVariables.GetAllNamedVariables())
                        current[prefix + " :: " + v.Name] = ValueOf(v);
                }
                current["<car> position"] = V(car.transform.position);
            }
            foreach (var v in FsmVariables.GlobalVariables.GetAllNamedVariables())
                current["<global> :: " + v.Name] = ValueOf(v);

            snapshotIndex++;
            var sb = new StringBuilder();
            foreach (var kv in current) sb.AppendLine(kv.Key + " = " + kv.Value);
            Write("snapshot_" + snapshotIndex + ".txt", sb);

            if (lastSnapshot != null)
            {
                var diff = new StringBuilder();
                diff.AppendLine("== DIFF snapshot " + (snapshotIndex - 1) + " -> " + snapshotIndex + " ==");
                int n = 0;
                foreach (var kv in current)
                {
                    string old;
                    if (!lastSnapshot.TryGetValue(kv.Key, out old)) old = "<missing>";
                    if (old != kv.Value)
                    {
                        diff.AppendLine(kv.Key + " : " + old + "  ->  " + kv.Value);
                        n++;
                    }
                }
                Write("diff_" + (snapshotIndex - 1) + "_" + snapshotIndex + ".txt", diff);
                ModConsole.Print("[CSE Research] Snapshot " + snapshotIndex + " saved, " + n + " values changed since previous.");
            }
            else
            {
                ModConsole.Print("[CSE Research] Snapshot " + snapshotIndex + " saved. Change something and press F9 again for a diff.");
            }
            lastSnapshot = current;
        }

        // ---------------------------------------------------------------- F11

        void DumpPlayer()
        {
            GameObject player = GameObject.Find("PLAYER");
            if (player == null)
            {
                ModConsole.Error("[CSE Research] PLAYER not found");
                return;
            }
            var sb = new StringBuilder();
            DumpHierarchy(player.transform, 0, sb);
            Write("player_hierarchy.txt", sb);

            sb = new StringBuilder();
            foreach (var fsm in player.GetComponentsInChildren<PlayMakerFSM>(true))
                DumpFsm(fsm, sb, true);
            Write("player_fsm.txt", sb);
            ModConsole.Print("[CSE Research] PLAYER dump done -> " + outDir);
        }

        // ---------------------------------------------------------------- F10

        void DumpLook()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                ModConsole.Error("[CSE Research] No main camera");
                return;
            }
            var hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, 5f);
            if (hits.Length == 0)
            {
                ModConsole.Print("[CSE Research] Nothing under crosshair (5 m)");
                return;
            }
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            var sb = new StringBuilder();
            foreach (var hit in hits)
            {
                Transform t = hit.collider.transform;
                sb.AppendLine("################ HIT " + Path(t) + "  dist:" + hit.distance.ToString("0.00") +
                              "  layer:" + LayerMask.LayerToName(t.gameObject.layer) + "  trigger:" + hit.collider.isTrigger);
                DumpHierarchy(t, 0, sb);
                for (Transform p = t; p != null; p = p.parent)
                    foreach (var fsm in p.GetComponents<PlayMakerFSM>())
                        DumpFsm(fsm, sb, true);
            }
            string file = "look_" + DateTime.Now.ToString("HHmmss") + ".txt";
            Write(file, sb);
            ModConsole.Print("[CSE Research] Look dump: " + Path(hits[0].collider.transform) + " -> " + file);
        }

        // ---------------------------------------------------------------- helpers

        static GameObject FindSatsuma()
        {
            foreach (var go in AllSceneObjects())
                if (go.transform.parent == null && go.name.StartsWith("SATSUMA"))
                    return go;
            return null;
        }

        static List<GameObject> AllSceneObjects()
        {
            var list = new List<GameObject>();
            foreach (GameObject go in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
                if (go.hideFlags == HideFlags.None)
                    list.Add(go);
            return list;
        }

        static void DumpHierarchy(Transform t, int depth, StringBuilder sb)
        {
            string ind = new string(' ', depth * 2);
            sb.Append(ind).Append(t.gameObject.activeSelf ? "[+] " : "[-] ").Append(t.name)
              .Append("  lp:").Append(V(t.localPosition))
              .Append("  lr:").Append(V(t.localEulerAngles))
              .Append("  layer:").Append(LayerMask.LayerToName(t.gameObject.layer));
            sb.Append("  {");
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                sb.Append(c.GetType().Name);
                var fsm = c as PlayMakerFSM;
                if (fsm != null) sb.Append("(").Append(fsm.FsmName).Append(")");
                sb.Append(", ");
            }
            sb.AppendLine("}");
            for (int i = 0; i < t.childCount; i++)
                DumpHierarchy(t.GetChild(i), depth + 1, sb);
        }

        static void DumpFsm(PlayMakerFSM pm, StringBuilder sb, bool withActions)
        {
            Fsm fsm = pm.Fsm;
            sb.AppendLine("==================================================================");
            sb.AppendLine("FSM " + Path(pm.transform) + " :: " + pm.FsmName + "   active state: " + pm.ActiveStateName + "   enabled:" + pm.enabled);

            sb.AppendLine("  -- variables");
            foreach (var v in pm.FsmVariables.GetAllNamedVariables())
                sb.AppendLine("    " + VarLine(v));

            sb.AppendLine("  -- events: " + string.Join(", ", Array.ConvertAll(pm.FsmEvents, e => e.Name + (e.IsGlobal ? "(G)" : ""))));
            foreach (var gt in pm.FsmGlobalTransitions)
                sb.AppendLine("  -- global transition: " + gt.EventName + " -> " + gt.ToState);

            foreach (var st in fsm.States)
            {
                sb.AppendLine("  STATE [" + st.Name + "]" + (st.Name == fsm.StartState ? " (start)" : ""));
                foreach (var tr in st.Transitions)
                    sb.AppendLine("      on " + tr.EventName + " -> " + tr.ToState);
                if (!withActions) continue;
                FsmStateAction[] actions;
                try
                {
                    if (!st.ActionsLoaded) st.LoadActions();
                    actions = st.Actions;
                }
                catch (Exception e)
                {
                    sb.AppendLine("      <actions failed: " + e.Message + ">");
                    continue;
                }
                foreach (var a in actions)
                {
                    if (a == null) continue;
                    sb.Append("      * ").Append(a.GetType().Name).Append(a.Enabled ? "" : " (disabled)").Append(" ");
                    sb.AppendLine(ActionFields(a));
                }
            }
        }

        static string ActionFields(FsmStateAction a)
        {
            var parts = new List<string>();
            foreach (var f in a.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.DeclaringType == typeof(FsmStateAction)) continue;
                object val;
                try { val = f.GetValue(a); } catch { continue; }
                parts.Add(f.Name + "=" + Describe(val));
            }
            return "{" + string.Join("; ", parts.ToArray()) + "}";
        }

        static string Describe(object val)
        {
            if (val == null) return "null";
            var nv = val as NamedVariable;
            if (nv != null)
            {
                if (nv.UseVariable && !string.IsNullOrEmpty(nv.Name)) return "$" + nv.Name + "(" + ValueOf(nv) + ")";
                if (nv.IsNone) return "none";
                return ValueOf(nv);
            }
            var ev = val as FsmEvent;
            if (ev != null) return "event:" + ev.Name;
            var owner = val as FsmOwnerDefault;
            if (owner != null)
                return owner.OwnerOption == OwnerDefaultOption.UseOwner ? "<owner>" : Describe(owner.GameObject);
            var go = val as GameObject;
            if (go != null) return "go:" + Path(go.transform);
            var comp = val as Component;
            if (comp != null) return comp.GetType().Name + "@" + Path(comp.transform);
            var uo = val as UnityEngine.Object;
            if (uo != null) return uo.GetType().Name + ":" + uo.name;
            if (val is string) return "\"" + val + "\"";
            if (val is Array)
            {
                var items = new List<string>();
                foreach (var o in (IEnumerable)val) items.Add(Describe(o));
                return "[" + string.Join(", ", items.ToArray()) + "]";
            }
            if (val is Vector3) return V((Vector3)val);
            var fp = val as FsmProperty;
            if (fp != null)
            {
                // Only the interesting fields; the rest are unused parameter slots.
                var parts = new List<string>();
                parts.Add("target=" + Describe(fp.TargetObject));
                parts.Add("type=" + fp.TargetTypeName);
                parts.Add("prop=" + fp.PropertyName);
                parts.Add(fp.setProperty ? "SET" : "GET");
                foreach (var f in typeof(FsmProperty).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!f.Name.EndsWith("Parameter") && f.Name != "SetVariable" && f.Name != "GetVariable") continue;
                    var nv2 = f.GetValue(fp) as NamedVariable;
                    if (nv2 == null || (nv2.IsNone && !nv2.UseVariable)) continue;
                    string d = Describe(nv2);
                    if (d == "none" || d == "0" || d == "False" || d == "\"\"" || d == "null") continue;
                    parts.Add(f.Name + "=" + d);
                }
                return "{" + string.Join(", ", parts.ToArray()) + "}";
            }
            return val.ToString();
        }

        static string VarLine(NamedVariable v)
        {
            return v.GetType().Name.Replace("Fsm", "") + " " + v.Name + " = " + ValueOf(v);
        }

        static string ValueOf(NamedVariable v)
        {
            try
            {
                PropertyInfo p = v.GetType().GetProperty("Value");
                if (p == null) return "?";
                object val = p.GetValue(v, null);
                if (val == null) return "null";
                var go = val as GameObject;
                if (go != null) return "go:" + Path(go.transform);
                var uo = val as UnityEngine.Object;
                if (uo != null) return uo.GetType().Name + ":" + uo.name;
                if (val is float) return ((float)val).ToString("0.####");
                if (val is Vector3) return V((Vector3)val);
                return val.ToString();
            }
            catch (Exception e)
            {
                return "<err " + e.GetType().Name + ">";
            }
        }

        static string FsmNames(GameObject go)
        {
            var fsms = go.GetComponents<PlayMakerFSM>();
            if (fsms.Length == 0) return "-";
            return string.Join(",", Array.ConvertAll(fsms, f => f.FsmName));
        }

        static string Path(Transform t)
        {
            string p = t.name;
            for (Transform c = t.parent; c != null; c = c.parent) p = c.name + "/" + p;
            return p;
        }

        static string V(Vector3 v)
        {
            return "(" + v.x.ToString("0.###") + "," + v.y.ToString("0.###") + "," + v.z.ToString("0.###") + ")";
        }

        void Write(string file, StringBuilder sb)
        {
            File.WriteAllText(System.IO.Path.Combine(outDir, file), sb.ToString());
        }
    }
}
