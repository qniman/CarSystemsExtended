using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace CSE.Core
{
    // Cached references into the game world and PlayMaker globals.
    public static class GameRefs
    {
        public static GameObject Car;
        public static Transform ItemPivot;

        static FsmFloat toolWrenchSize;
        static FsmBool playerHasRatchet;
        static FsmBool guiAssemble, guiDisassemble, guiUse;
        static FsmString guiInteraction;
        static PlayMakerFSM batteryData;

        public static bool Init()
        {
            var g = FsmVariables.GlobalVariables;
            var car = g.FindFsmGameObject("TheCar");
            Car = car != null ? car.Value : null;
            if (Car == null) Car = GameObject.Find("SATSUMA(557kg, 248)");

            var pivot = g.FindFsmGameObject("ItemPivot");
            ItemPivot = pivot != null && pivot.Value != null ? pivot.Value.transform : null;

            toolWrenchSize = g.FindFsmFloat("ToolWrenchSize");
            playerHasRatchet = g.FindFsmBool("PlayerHasRatchet");
            guiAssemble = g.FindFsmBool("GUIassemble");
            guiDisassemble = g.FindFsmBool("GUIdisassemble");
            guiUse = g.FindFsmBool("GUIuse");
            guiInteraction = g.FindFsmString("GUIinteraction");

            var battery = GameObject.Find("Database/PartsStatus/Battery");
            if (battery != null) batteryData = battery.GetPlayMaker("Data");

            if (Car == null) ModConsole.Error("[CSE] SATSUMA not found");
            if (ItemPivot == null) ModConsole.Error("[CSE] ItemPivot not found");
            return Car != null && ItemPivot != null;
        }

        public static Camera Camera
        {
            get { return Camera.main; }
        }

        /// Raw value of the wrench currently in hand (0 = no wrench).
        public static float WrenchRaw
        {
            get { return toolWrenchSize != null ? toolWrenchSize.Value : 0f; }
        }

        /// Wrench size in millimetres, 0 if no wrench is held.
        public static int WrenchMm
        {
            get { return WrenchRaw > 0.01f ? Mathf.RoundToInt(WrenchRaw * 10f) : 0; }
        }

        public static bool HasRatchet
        {
            get { return playerHasRatchet != null && playerHasRatchet.Value; }
        }

        public static bool IsHeld(GameObject go)
        {
            return ItemPivot != null && go.transform.parent == ItemPivot;
        }

        public static float BatteryCharge
        {
            get
            {
                if (batteryData == null) return 0f;
                var f = batteryData.FsmVariables.FindFsmFloat("Charge");
                return f != null ? f.Value : 0f;
            }
        }

        public static void DrainBattery(float amount)
        {
            if (batteryData == null) return;
            var f = batteryData.FsmVariables.FindFsmFloat("Charge");
            if (f != null) f.Value = Mathf.Max(0f, f.Value - amount);
        }

        public static Transform FindInCar(string path)
        {
            return Car != null ? Car.transform.Find(path) : null;
        }

        /// Depth-first search for the first transform under the car whose name starts with prefix.
        public static Transform FindInCarByPrefix(string prefix)
        {
            if (Car == null) return null;
            foreach (var t in Car.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(prefix)) return t;
            return null;
        }

        // ---- GUI flags, written through Gui so we only clear what we set

        internal static void SetGui(bool assemble, bool disassemble, bool use, string text)
        {
            if (guiAssemble != null) guiAssemble.Value = assemble;
            if (guiDisassemble != null) guiDisassemble.Value = disassemble;
            if (guiUse != null) guiUse.Value = use;
            if (guiInteraction != null) guiInteraction.Value = text ?? "";
        }
    }
}
