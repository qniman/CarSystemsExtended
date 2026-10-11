using CSE.Parts;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace CSE.Core
{
    // Hooks the game's wrench logic (FPSCamera/2Spanner/Raycast, FSM "Check"). After the game has
    // decided a bolt gets a tighten/untighten step it sends an event to the bolt's own FSM; our
    // bolts have no FSM, so we catch the step in those states and forward it.
    public static class ToolHook
    {
        const string CheckPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/2Spanner/Raycast";

        static FsmGameObject boltVar;

        public static void Init()
        {
            boltVar = null;
            var go = GameObject.Find(CheckPath);
            var check = go != null ? go.GetPlayMaker("Check") : null;
            if (check == null)
            {
                ModConsole.Error("[CSE] Wrench FSM not found, bolts will not work");
                return;
            }
            boltVar = check.FsmVariables.FindFsmGameObject("Bolt");

            // Spanner states and the ratchet ("2") variants.
            check.FsmInject("Tighten", () => Step(1));
            check.FsmInject("Tighten 2", () => Step(1));
            check.FsmInject("Untighten", () => Step(-1));
            check.FsmInject("Untighten 2", () => Step(-1));

            if (LayerMask.NameToLayer("Bolts") != 12)
                ModConsole.Warning("[CSE] Bolts layer is " + LayerMask.NameToLayer("Bolts") + ", wrench raycasts layer 12");
        }

        static void Step(int dir)
        {
            if (boltVar == null || boltVar.Value == null) return;
            var bolt = boltVar.Value.GetComponent<Bolt>();
            if (bolt != null) bolt.Turn(dir);
        }
    }
}
