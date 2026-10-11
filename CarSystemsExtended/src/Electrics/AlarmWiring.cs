using System.Collections.Generic;
using CSE.Core;
using CSE.Parts;
using MSCLoader;
using UnityEngine;

namespace CSE.Electrics
{
    // Power lead from the battery plus terminal to the alarm unit, and the signal wires from
    // devices to the unit. Ground goes through the unit's mounting bolts.
    public static class AlarmWiring
    {
        public static bool PowerConnected;

        static Transform terminal;
        static Wire powerWire;
        static readonly Dictionary<Part, Wire> signalWires = new Dictionary<Part, Wire>();

        public static void Init()
        {
            PowerConnected = false;
            signalWires.Clear();
            powerWire = new Wire("power", new Color(0.7f, 0.05f, 0.05f));

            var plus = GameRefs.FindInCarByPrefix("battery_terminal_plus");
            if (plus == null)
            {
                ModConsole.Error("[CSE] battery_terminal_plus not found, alarm power cannot be connected");
                return;
            }
            terminal = plus.Find("Bolts") ?? plus;

            var go = new GameObject("cse_alarm_power_tap");
            go.layer = LayerMask.NameToLayer("Parts");
            // Sits just above the clamp: at the clamp itself the battery's own collider is in the way.
            go.transform.parent = terminal;
            go.transform.position = terminal.position + GameRefs.Car.transform.up * 0.03f;
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.04f / Mathf.Max(0.01f, go.transform.lossyScale.x);
            go.AddComponent<PowerTap>();
        }

        /// Battery terminal present and the battery not flat.
        public static bool BatteryOk
        {
            get { return terminal != null && terminal.gameObject.activeInHierarchy && GameRefs.BatteryCharge > 1f; }
        }

        /// Recomputes which wires are visible. Call after any part change.
        public static void Refresh()
        {
            var unit = PartManager.FindInstalled(PartCatalog.AlarmUnit.Id);
            if (unit == null || terminal == null || !terminal.gameObject.activeInHierarchy) PowerConnected = false;

            if (PowerConnected) powerWire.Connect(terminal, unit.Plug);
            else powerWire.Hide();

            foreach (var p in PartManager.Parts)
            {
                if (p == null || !p.Def.Wired) continue;
                Wire w;
                if (!signalWires.TryGetValue(p, out w))
                    signalWires[p] = w = new Wire("signal_" + p.Uid, Shapes.Black, 0.004f);
                if (p.WireConnected && p.Installed && unit != null) w.Connect(p.Plug, unit.Plug);
                else w.Hide();
            }
        }

        public static void Tick()
        {
            // The battery can be pulled out from under the lead.
            if (PowerConnected && (terminal == null || !terminal.gameObject.activeInHierarchy))
                Refresh();

            powerWire.Update();
            foreach (var w in signalWires.Values) w.Update();
        }
    }

    // Clickable spot on the battery plus terminal for the alarm power lead.
    public class PowerTap : Interactable
    {
        public override void OnHover()
        {
            if (!PartManager.AlarmUnitInstalled) return;

            if (!AlarmWiring.PowerConnected)
            {
                Gui.Interaction("CONNECT ALARM POWER");
                if (Input.GetMouseButtonDown(0))
                {
                    AlarmWiring.PowerConnected = true;
                    Sfx.Game("CarFoley", "carkeys_in", transform.position, 0.6f);
                    AlarmWiring.Refresh();
                }
            }
            else
            {
                Gui.Interaction("DISCONNECT ALARM POWER");
                if (Input.GetMouseButtonDown(1))
                {
                    AlarmWiring.PowerConnected = false;
                    Sfx.Game("CarFoley", "carkeys_out", transform.position, 0.6f);
                    AlarmWiring.Refresh();
                }
            }
        }
    }
}
