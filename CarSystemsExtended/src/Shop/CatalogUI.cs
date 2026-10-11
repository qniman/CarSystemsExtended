using System.Collections.Generic;
using CSE.Core;
using UnityEngine;

namespace CSE.Shop
{
    // The brochure, drawn with IMGUI as a printed page. While open the game is in menu mode:
    // cursor visible, no mouse look, no walking.
    public static class CatalogUI
    {
        public static bool IsOpen { get; private set; }

        static readonly HashSet<string> picked = new HashSet<string>();
        static GUIStyle page, title, subtitle, item, blurb, price, small, button, check;
        static Vector2 scroll;

        static readonly Color Ink = new Color(0.12f, 0.1f, 0.08f);
        static readonly Color Red = new Color(0.72f, 0.1f, 0.07f);
        static readonly Color PaperColor = new Color(0.95f, 0.93f, 0.86f);

        public static void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            picked.Clear();
            scroll = Vector2.zero;
            GameRefs.EnterMenu();
            Sfx.Game("HouseFoley", "mail_envelope_open", GameRefs.Camera.transform.position, 0.5f);
        }

        public static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            GameRefs.ExitMenu();
        }

        /// Called from Update while open. RMB closes, like it opened; Esc is the game's pause menu.
        public static void Tick()
        {
            if (Input.GetMouseButtonDown(1)) Close();
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        static void InitStyles()
        {
            if (page != null) return;
            page = new GUIStyle(GUI.skin.box);
            page.normal.background = Solid(PaperColor);

            title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = Red;
            subtitle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleCenter };
            subtitle.normal.textColor = Ink;
            item = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, wordWrap = true };
            item.normal.textColor = Ink;
            blurb = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            blurb.normal.textColor = Ink;
            price = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            price.normal.textColor = Red;
            small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            small.normal.textColor = Ink;
            button = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold };
            check = new GUIStyle(GUI.skin.toggle) { fontSize = 14 };
            check.normal.textColor = check.onNormal.textColor = Ink;
            check.hover.textColor = check.onHover.textColor = Red;
        }

        public static void Draw()
        {
            if (!IsOpen) return;
            InitStyles();

            float w = Mathf.Min(720, Screen.width - 40), h = Mathf.Min(620, Screen.height - 40);
            var rect = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            GUI.Box(rect, GUIContent.none, page);

            GUILayout.BeginArea(new Rect(rect.x + 24, rect.y + 16, rect.width - 48, rect.height - 32));
            GUILayout.Label("AUTOTARVIKE", title);
            GUILayout.Label("Car accessories by mail  ·  Spring 1995  ·  Delivery to your post office", subtitle);
            GUILayout.Space(12);

            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var e in MailOrder.Entries)
            {
                GUILayout.BeginHorizontal();
                bool on = picked.Contains(e.Id);
                bool now = GUILayout.Toggle(on, "", check, GUILayout.Width(24));
                if (now != on)
                {
                    if (now) picked.Add(e.Id);
                    else picked.Remove(e.Id);
                }
                GUILayout.BeginVertical();
                GUILayout.Label(e.Title, item);
                GUILayout.Label(e.Blurb, blurb);
                GUILayout.EndVertical();
                GUILayout.Label(e.Price + " mk", price, GUILayout.Width(110));
                GUILayout.EndHorizontal();
                GUILayout.Space(14);
            }
            GUILayout.EndScrollView();

            int total = 0;
            var picks = new List<CatalogEntry>();
            foreach (var e in MailOrder.Entries)
            {
                if (!picked.Contains(e.Id)) continue;
                picks.Add(e);
                total += e.Price;
            }

            GUILayout.Label("Postage and handling " + MailOrder.Shipping + " mk. Payment cash on delivery " +
                            "(postiennakko) when you collect the parcel. Post the order form in the post box " +
                            "at your local shop; parcels arrive the next day.", small);
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Label(picks.Count > 0 ? "TOTAL " + (total + MailOrder.Shipping) + " mk" : "Tick the items you want", item);
            GUILayout.FlexibleSpace();
            GUI.enabled = picks.Count > 0;
            if (GUILayout.Button("FILL IN ORDER FORM", button, GUILayout.Width(220), GUILayout.Height(34)))
            {
                MailOrder.FillOrderForm(picks);
                Close();
            }
            GUI.enabled = true;
            if (GUILayout.Button("CLOSE", button, GUILayout.Width(100), GUILayout.Height(34))) Close();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
