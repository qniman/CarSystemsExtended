using System.Collections.Generic;
using CSE.Core;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace CSE.Debugging
{
    // Debug tool for picking game sounds to replace our synthesized ones: each press plays the
    // next game sound whose name sounds useful (locks, latches, sirens, beeps...) in front of the
    // player and prints "n/total group/variation" to the console and the subtitle line.
    public static class SoundAudition
    {
        static readonly string[] Keywords =
        {
            "lock", "latch", "click", "relay", "button", "switch", "siren", "horn", "beep", "buzz", "alarm", "door",
        };

        static readonly List<KeyValuePair<string, string>> sounds = new List<KeyValuePair<string, string>>();
        static int next;

        public static void Init()
        {
            sounds.Clear();
            next = 0;
        }

        static void Scan()
        {
            var ma = Object.FindObjectOfType<MasterAudio>();
            if (ma == null)
            {
                ModConsole.Warning("[CSE] MasterAudio not found");
                return;
            }
            foreach (Transform group in ma.transform)
            {
                foreach (Transform v in group)
                {
                    string n = (group.name + "/" + v.name).ToLowerInvariant();
                    foreach (var k in Keywords)
                    {
                        if (!n.Contains(k)) continue;
                        sounds.Add(new KeyValuePair<string, string>(group.name, v.name));
                        break;
                    }
                }
            }
            ModConsole.Print("[CSE] Sound audition: " + sounds.Count + " candidate game sounds");
            for (int i = 0; i < sounds.Count; i++) ModConsole.Log("[CSE]   " + (i + 1) + ". " + sounds[i].Key + "/" + sounds[i].Value);
        }

        public static void PlayNext()
        {
            if (sounds.Count == 0) Scan();
            if (sounds.Count == 0) return;
            var s = sounds[next % sounds.Count];
            next++;
            var cam = GameRefs.Camera;
            if (cam == null) return;
            Sfx.Game(s.Key, s.Value, cam.transform.position + cam.transform.forward, 1f);
            string label = ((next - 1) % sounds.Count + 1) + "/" + sounds.Count + "  " + s.Key + "/" + s.Value;
            ModConsole.Print("[CSE] Sound " + label);
            var sub = FsmVariables.GlobalVariables.FindFsmString("GUIsubtitle");
            if (sub != null) sub.Value = label;
        }
    }
}
