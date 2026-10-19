using System.Collections.Generic;
using CSE.Parts;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace CSE.Core
{
    // Hooks the game's wrench logic (FPSCamera/2Spanner/Raycast, FSM "Check"). After the game has
    // decided a bolt gets a tighten/untighten step it sends an event to the bolt's own FSM; our
    // bolts have no FSM, so we catch the step in those states and forward it.
    //
    // Mods that animate the wrench (BetterMSC) add their own tighten states to the same FSM and
    // may route around the game's, so any state named like one is hooked too, also when it
    // appears after we loaded. BetterMSC's animated wrench skips this FSM and sends TIGHTEN /
    // UNTIGHTEN to the bolt's own "Screw" FSM (see Bolt). The same step arriving by several of
    // these paths within a moment counts once.
    public static class ToolHook
    {
        const string CheckPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/2Spanner/Raycast";

        static FsmGameObject boltVar;
        static PlayMakerFSM check;
        static readonly HashSet<string> hooked = new HashSet<string>();
        static int knownStates;
        const float SameStep = 0.08f;

        static float lastStepTime = -1f;
        static Bolt lastStepBolt;
        static int lastStepDir;

        public static void Init()
        {
            boltVar = null;
            hooked.Clear();
            knownStates = 0;
            lastStepTime = -1f;
            lastStepBolt = null;
            var go = GameObject.Find(CheckPath);
            check = go != null ? go.GetPlayMaker("Check") : null;
            if (check == null)
            {
                ModConsole.Error("[CSE] Wrench FSM not found, bolts will not work");
                return;
            }
            boltVar = check.FsmVariables.FindFsmGameObject("Bolt");

            // Spanner states and the ratchet ("2") variants.
            Hook("Tighten", 1);
            Hook("Tighten 2", 1);
            Hook("Untighten", -1);
            Hook("Untighten 2", -1);
            HookOthers();

            if (LayerMask.NameToLayer("Bolts") != 12)
                ModConsole.Warning("[CSE] Bolts layer is " + LayerMask.NameToLayer("Bolts") + ", wrench raycasts layer 12");
        }

        /// Picks up tighten states other mods add to the wrench FSM after we loaded.
        public static void Tick()
        {
            if (check != null && check.FsmStates.Length != knownStates) HookOthers();
        }

        static void Hook(string state, int dir)
        {
            if (!hooked.Add(state)) return;
            check.FsmInject(state, () => Step(dir));
        }

        static void HookOthers()
        {
            knownStates = check.FsmStates.Length;
            foreach (var s in check.FsmStates)
            {
                string n = s.Name.ToLowerInvariant();
                if (!n.Contains("tighten") || hooked.Contains(s.Name)) continue;
                Hook(s.Name, n.Contains("untighten") ? -1 : 1);
                ModConsole.Log("[CSE] Also hooked wrench state '" + s.Name + "' (another mod's wrench)");
            }
        }

        static void Step(int dir)
        {
            if (boltVar == null || boltVar.Value == null) return;
            var bolt = boltVar.Value.GetComponent<Bolt>();
            if (bolt != null) Step(bolt, dir);
        }

        public static void Step(Bolt bolt, int dir)
        {
            if (lastStepBolt == bolt && lastStepDir == dir && Time.time - lastStepTime < SameStep) return;
            lastStepTime = Time.time;
            lastStepBolt = bolt;
            lastStepDir = dir;
            bolt.Turn(dir);
        }
    }
}
