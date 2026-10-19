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
        static FsmBool guiAssemble, guiDisassemble, guiUse, guiBuy;
        static FsmBool playerInMenu, playerStop;
        static FsmString guiInteraction;
        static FsmFloat batteryCharge;

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
            guiBuy = g.FindFsmBool("GUIbuy");
            playerInMenu = g.FindFsmBool("PlayerInMenu");
            playerStop = g.FindFsmBool("PlayerStop");
            guiInteraction = g.FindFsmString("GUIinteraction");

            var battery = GameObject.Find("Database/PartsStatus/Battery");
            var batteryData = battery != null ? battery.GetPlayMaker("Data") : null;
            batteryCharge = batteryData != null ? batteryData.FsmVariables.FindFsmFloat("Charge") : null;

            if (Car == null) ModConsole.Error("[CSE] SATSUMA not found");
            if (ItemPivot == null) ModConsole.Error("[CSE] ItemPivot not found");
            return Car != null && ItemPivot != null;
        }

        static Camera camera;
        static int cameraFrame = -1;

        /// Camera.main searches by tag on every call; looked up once per frame.
        public static Camera Camera
        {
            get
            {
                if (cameraFrame != Time.frameCount || camera == null)
                {
                    camera = Camera.main;
                    cameraFrame = Time.frameCount;
                }
                return camera;
            }
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

        /// The game's "Use" button (rebindable in its options; F by default).
        public static bool UseButtonDown
        {
            get
            {
                try { return cInput.GetButtonDown("Use"); }
                catch { return Input.GetKeyDown(KeyCode.F); }
            }
        }

        public static bool IsHeld(GameObject go)
        {
            return ItemPivot != null && go.transform.parent == ItemPivot;
        }

        public static float BatteryCharge
        {
            get { return batteryCharge != null ? batteryCharge.Value : 0f; }
        }

        public static void DrainBattery(float amount)
        {
            if (batteryCharge != null) batteryCharge.Value = Mathf.Max(0f, batteryCharge.Value - amount);
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

        // ---- menu mode: the game shows the cursor and stops mouse look while PlayerInMenu is
        // set (PLAYER "Crouch" FSM), and stops movement while PlayerStop is set.

        static bool stopBeforeMenu;

        public static void EnterMenu()
        {
            stopBeforeMenu = playerStop != null && playerStop.Value;
            if (playerInMenu != null) playerInMenu.Value = true;
            if (playerStop != null) playerStop.Value = true;
        }

        public static void ExitMenu()
        {
            if (playerInMenu != null) playerInMenu.Value = false;
            if (playerStop != null) playerStop.Value = stopBeforeMenu;
        }

        // ---- GUI flags, written through Gui so we only clear what we set

        internal static void SetGui(bool assemble, bool disassemble, bool use, bool buy, string text)
        {
            if (guiBuy != null) guiBuy.Value = buy;
            if (guiAssemble != null) guiAssemble.Value = assemble;
            if (guiDisassemble != null) guiDisassemble.Value = disassemble;
            if (guiUse != null) guiUse.Value = use;
            if (guiInteraction != null) guiInteraction.Value = text ?? "";
        }
    }
}
