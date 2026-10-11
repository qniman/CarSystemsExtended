namespace CSE.Core
{
    // Collects interaction hints requested during a frame and writes them to the game's GUI
    // globals. Only touches the globals while we have something to show, plus one frame to clear,
    // so the game's own hints are never overwritten.
    public static class Gui
    {
        static bool assemble, disassemble, use, buy;
        static string text;
        static bool wroteLastFrame;

        public static void Assemble() { assemble = true; }
        public static void Disassemble() { disassemble = true; }
        public static void Buy() { buy = true; }

        public static void Interaction(string t)
        {
            use = true;
            text = t;
        }

        public static void Apply()
        {
            bool any = assemble || disassemble || use || buy;
            if (any || wroteLastFrame)
                GameRefs.SetGui(assemble, disassemble, use, buy, text);
            wroteLastFrame = any;
            assemble = disassemble = use = buy = false;
            text = null;
        }
    }
}
