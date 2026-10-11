using System.Collections.Generic;
using CSE.Core;
using CSE.Parts;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace CSE.Electrics
{
    // Alarm wiring, laid with the game's own "wiring mess" tool, point to point like the car's
    // factory wiring: hold the tool at a connector and press Use, then at the other end and press
    // Use again.
    //
    //   fuse box "ALARM FEED"  ->  alarm unit            (power)
    //   alarm unit             ->  siren, lock actuators, choke servo, status LED  (signal)
    //
    // Ground goes through the unit's mounting bolts. Wires are drawn as short tails disappearing
    // into a grommet, as if routed behind the trim, instead of hanging across the car.
    public static class AlarmWiring
    {
        const float Reach = 0.12f; // the game uses 0.1 for its own wiring points

        public static bool PowerConnected;

        static Transform terminal;  // battery plus, to know the feed is live
        static Transform feed;      // our tap at the fuse box
        static Transform tool;      // ITEMS/wiring mess(itemx)

        static bool pendingSet;
        static Point pending;
        static readonly List<WireStub> stubs = new List<WireStub>();

        struct Point
        {
            public Part Part;   // null for the fuse box feed
            public Transform T;
            public string Label;

            public bool IsFeed
            {
                get { return Part == null; }
            }

            public bool Same(Point o)
            {
                return o.Part == Part && o.T == T;
            }
        }

        public static void Init()
        {
            PowerConnected = false;
            pendingSet = false;
            foreach (var s in stubs) s.Destroy();
            stubs.Clear();

            var plus = GameRefs.FindInCarByPrefix("battery_terminal_plus");
            terminal = plus != null ? (plus.Find("Bolts") ?? plus) : null;

            var tl = FsmVariables.GlobalVariables.FindFsmGameObject("WiringTool");
            tool = tl != null && tl.Value != null ? tl.Value.transform : null;

            // The factory dash harness ends at the fuse box; put our feed tap right below it.
            var fusebox = GameRefs.FindInCarByPrefix("FuseboxDash");
            var car = GameRefs.Car.transform;
            var go = new GameObject("cse_alarm_feed");
            go.transform.parent = car;
            go.transform.position = fusebox != null ? fusebox.position - car.up * 0.04f : car.TransformPoint(0.5f, 0.12f, 0.83f);
            go.transform.localRotation = Quaternion.identity;
            Shapes.Add(go.transform, PrimitiveType.Cube, Vector3.zero, new Vector3(0.025f, 0.015f, 0.02f), Shapes.Black);
            Shapes.Add(go.transform, PrimitiveType.Cube, new Vector3(0f, 0.008f, 0f), new Vector3(0.008f, 0.002f, 0.008f), Shapes.LedRed);
            feed = go.transform;

            if (terminal == null) ModConsole.Error("[CSE] battery_terminal_plus not found");
            if (tool == null) ModConsole.Error("[CSE] wiring mess not found, alarm cannot be wired");
            if (fusebox == null) ModConsole.Warning("[CSE] fuse box point not found, feed placed by estimate");
        }

        /// Battery in place, plus terminal on, not flat.
        public static bool BatteryOk
        {
            get { return terminal != null && terminal.gameObject.activeInHierarchy && GameRefs.BatteryCharge > 1f; }
        }

        static Part Unit
        {
            get { return PartManager.FindInstalled(PartCatalog.AlarmUnit.Id); }
        }

        static IEnumerable<Point> Points()
        {
            yield return new Point { T = feed, Label = "ALARM FEED (FUSE BOX)" };
            foreach (var p in PartManager.Parts)
            {
                if (p == null || !p.Installed) continue;
                if (p.Def == PartCatalog.AlarmUnit || p.Def.Wired)
                    yield return new Point { Part = p, T = p.Plug, Label = p.Def.Name.ToUpper() };
            }
        }

        // ---------------------------------------------------------------- connecting

        public static void Tick()
        {
            // Pulling the unit drops its power wire. A missing battery does not: the wire stays
            // at the fuse box, there is just nothing on it (see BatteryOk).
            if (PowerConnected && Unit == null)
            {
                PowerConnected = false;
                Refresh();
            }
            if (pendingSet && pending.Part != null && !pending.Part.Installed) pendingSet = false;

            foreach (var s in stubs) s.Update();

            if (tool == null || !tool.gameObject.activeInHierarchy) return;

            bool found = false;
            Point near = default(Point);
            float best = Reach;
            foreach (var pt in Points())
            {
                float d = Vector3.Distance(tool.position, pt.T.position);
                if (d < best)
                {
                    best = d;
                    near = pt;
                    found = true;
                }
            }
            if (!found) return;

            Gui.Assemble();
            if (!pendingSet) Gui.Interaction(near.Label);
            else if (pending.Same(near)) Gui.Interaction("CANCEL WIRE");
            else Gui.Interaction(pending.Label + "  >  " + near.Label);

            if (GameRefs.UseButtonDown) OnUse(near);
        }

        static void OnUse(Point pt)
        {
            if (!pendingSet)
            {
                if (IsDone(pt)) { Click(pt); return; }
                pending = pt;
                pendingSet = true;
                Sfx.Game("CarFoley", "dash_button", pt.T.position, 0.6f);
                return;
            }

            if (pending.Same(pt))
            {
                pendingSet = false;
                Click(pt);
                return;
            }

            if (!TryConnect(pending, pt) && !TryConnect(pt, pending))
            {
                Click(pt);
                return;
            }
            pendingSet = false;
            Sfx.Game("CarBuilding", "wiring", pt.T.position);
            Refresh();
        }

        /// Already wired as far as this end is concerned.
        static bool IsDone(Point pt)
        {
            if (pt.IsFeed) return PowerConnected;
            if (pt.Part.Def == PartCatalog.AlarmUnit) return false; // the unit takes many wires
            return pt.Part.WireConnected;
        }

        static bool TryConnect(Point a, Point b)
        {
            if (a.Part == null || a.Part.Def != PartCatalog.AlarmUnit) return false;
            if (b.IsFeed)
            {
                if (PowerConnected) return false;
                PowerConnected = true;
                return true;
            }
            if (!b.Part.Def.Wired || b.Part.WireConnected) return false;
            b.Part.WireConnected = true;
            return true;
        }

        static void Click(Point pt)
        {
            Sfx.Game("CarFoley", "dash_button", pt.T.position, 0.3f);
        }

        // ---------------------------------------------------------------- visuals

        /// Rebuilds the wire tails. Call after any wiring or part change.
        public static void Refresh()
        {
            foreach (var s in stubs) s.Destroy();
            stubs.Clear();

            var unit = Unit;
            if (unit == null) return;
            if (PowerConnected)
            {
                stubs.Add(new WireStub(feed, unit.Plug, new Color(0.7f, 0.05f, 0.05f)));
                stubs.Add(new WireStub(unit.Plug, feed, new Color(0.7f, 0.05f, 0.05f)));
            }
            foreach (var p in PartManager.Parts)
            {
                if (p == null || !p.Installed || !p.Def.Wired || !p.WireConnected) continue;
                stubs.Add(new WireStub(p.Plug, unit.Plug, Shapes.Black));
                stubs.Add(new WireStub(unit.Plug, p.Plug, Shapes.Black));
            }
        }
    }

    // A short length of wire leaving a connector towards the other end and vanishing into a
    // rubber grommet, standing in for a harness routed behind the trim.
    public class WireStub
    {
        const float Length = 0.1f;

        readonly Transform from, toward;
        readonly LineRenderer line;
        readonly Transform grommet;

        public WireStub(Transform from, Transform toward, Color color)
        {
            this.from = from;
            this.toward = toward;
            var go = new GameObject("cse_wire_stub");
            line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.SetWidth(0.005f, 0.005f);
            line.SetVertexCount(2);
            line.material = Shapes.Material(color);
            grommet = Shapes.Add(go.transform, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.014f, Shapes.Black, default(Vector3), "grommet").transform;
            Update();
        }

        public void Update()
        {
            if (from == null || toward == null) return;
            Vector3 a = from.position;
            Vector3 dir = toward.position - a;
            Vector3 b = a + (dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.down) * Length;
            line.SetPosition(0, a);
            line.SetPosition(1, b);
            grommet.position = b;
        }

        public void Destroy()
        {
            if (line != null) Object.Destroy(line.gameObject);
        }
    }
}
