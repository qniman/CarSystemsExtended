using System.Text;
using CSE.Core;
using CSE.Electrics;
using CSE.Parts;
using CSE.Systems;
using UnityEngine;

namespace CSE.Debugging
{
    // Small text panel in the top-left corner while debug mode is on.
    public static class DebugOverlay
    {
        static GUIStyle style;

        public static void Draw()
        {
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 12 };
                style.normal.textColor = Color.white;
            }

            var sb = new StringBuilder();
            sb.AppendLine("CSE debug");
            var h = Look.Hovered;
            sb.AppendLine("look: " + (h != null ? h.name : "-"));
            sb.AppendLine("wrench raw " + GameRefs.WrenchRaw.ToString("0.###") + " = " + GameRefs.WrenchMm + "mm" +
                          (GameRefs.HasRatchet ? " (ratchet)" : ""));
            sb.AppendLine("battery charge " + GameRefs.BatteryCharge.ToString("0.0"));
            sb.AppendLine("alarm: " + AlarmUnit.Diagnose() + (AlarmUnit.SirenReady ? ", siren ready" : ""));
            sb.AppendLine("power lead: " + AlarmWiring.PowerConnected);
            foreach (var p in PartManager.Parts)
            {
                if (p == null) continue;
                sb.Append("  ").Append(p.Uid).Append(p.Installed ? " installed" : " loose");
                if (p.Bolts.Count > 0)
                {
                    sb.Append(" bolts ");
                    foreach (var b in p.Bolts) sb.Append(b.Stage);
                }
                if (p.Def.Wired) sb.Append(p.WireConnected ? " wired" : " unwired");
                if (p.Damaged) sb.Append(" DAMAGED");
                sb.AppendLine();
            }
            if (MountEditor.Active) sb.AppendLine("MOUNT EDITOR ON (numpad)");

            string text = sb.ToString();
            var size = style.CalcSize(new GUIContent(text));
            GUI.Box(new Rect(10, 10, size.x + 10, size.y + 6), text, style);
        }
    }
}
