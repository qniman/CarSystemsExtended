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
            sb.AppendLine("alarm power: " + AlarmSystem.Diagnose());
            sb.AppendLine("alarm: " + AlarmSystem.Current + (AlarmSystem.FobWorks ? ", remote in range" : "") +
                          (AlarmSystem.LastTrigger != null ? ", last trigger: " + AlarmSystem.LastTrigger : ""));
            sb.AppendLine("doors (L=locked O=open c=closed): " + DoorLocks.StatusText);
            sb.Append("mail orders posted: " + Shop.MailOrder.Pending.Count);
            foreach (var o in Shop.MailOrder.Pending) sb.Append(" [" + o.Kits + ", " + o.DaysLeft + "d]");
            sb.AppendLine();
            sb.AppendLine("keys: " + KeySystem.StatusText);
            sb.AppendLine("remote start: " + RemoteStart.Current +
                          (RemoteStart.LastResult != null ? ", last: " + RemoteStart.LastResult : ""));
            foreach (var p in PartManager.Parts)
            {
                if (p == null) continue;
                sb.Append("  ").Append(p.Uid).Append(p.Installed ? " installed@" + p.MountIndex : " loose");
                if (p.Bolts.Count > 0)
                {
                    sb.Append(" bolts ");
                    foreach (var b in p.Bolts) sb.Append(b.Stage);
                }
                if (p.Def.Wired) sb.Append(p.WireConnected ? " wired" : " unwired");
                if (p.Damaged) sb.Append(" DAMAGED");
                sb.AppendLine();
            }
            if (MountEditor.Active)
            {
                sb.AppendLine("MOUNT EDITOR: " + MountEditor.Status);
                sb.AppendLine("  arrows/PgUp/PgDn move, , . rotate (Ctrl/Alt axis), Shift fine");
                sb.AppendLine("  Insert = to crosshair, Home = next, End = save");
            }

            string text = sb.ToString();
            var size = style.CalcSize(new GUIContent(text));
            GUI.Box(new Rect(10, 10, size.x + 10, size.y + 6), text, style);
        }
    }
}
