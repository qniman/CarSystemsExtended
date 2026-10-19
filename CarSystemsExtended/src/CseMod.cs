using System;
using CSE.Core;
using CSE.Debugging;
using CSE.Electrics;
using CSE.Parts;
using CSE.Save;
using CSE.Shop;
using CSE.Systems;
using MSCLoader;
using UnityEngine;

namespace CSE
{
    public class CseMod : Mod
    {
        public override string ID => "CarSystemsExtended";
        public override string Name => "Car Systems Extended";
        public override string Author => "qniman";
        public override string Version => "1.1.1";
        static byte[] icon;

        /// Shown in the MSCLoader mod list; embedded in the DLL (Assets/icon.png).
        public override byte[] Icon
        {
            get
            {
                if (icon != null) return icon;
                using (var s = typeof(CseMod).Assembly.GetManifestResourceStream("CSE.icon.png"))
                {
                    if (s == null) return null;
                    icon = new byte[s.Length];
                    s.Read(icon, 0, icon.Length);
                }
                return icon;
            }
            set { }
        }

        public override string Description => "Aftermarket car alarm with central locking and remote start for the Satsuma, 1995 style. Successor of Key System Extended.";

        SettingsCheckBox debugMode;
        SettingsDropDownList language;
        readonly System.Collections.Generic.Dictionary<string, int> frameErrors = new System.Collections.Generic.Dictionary<string, int>();
        SettingsKeybind lockKey, unlockKey, startKey, keysKey, dropKeysKey;
        SettingsSliderInt warmTemp, warmMinutes;
        SettingsKeybind spawnKitKey, spawnLocksKey, spawnServoKey, deliverKey, mountEditorKey, testSirenKey, auditionKey;
        bool ready;

        public override void ModSetup()
        {
            remoteButtons = RemoteButtons;
            debugKeys = DebugKeys;
            SetupFunction(Setup.ModSettings, CreateSettings);
            SetupFunction(Setup.ModSettingsLoaded, ApplyLanguage);
            SetupFunction(Setup.OnLoad, OnLoad);
            SetupFunction(Setup.OnSave, OnSave);
            SetupFunction(Setup.Update, OnUpdate);
            SetupFunction(Setup.OnGUI, OnGui);
        }

        void CreateSettings()
        {
            language = Settings.AddDropDownList("cse_language", "Language / Язык", L.LanguageNames, 0, ApplyLanguage);

            lockKey = Keybind.Add("cse_lock", "Alarm remote: lock / arm", KeyCode.L);
            unlockKey = Keybind.Add("cse_unlock", "Alarm remote: unlock / disarm", KeyCode.U);
            startKey = Keybind.Add("cse_remote_start", "Alarm remote: remote start / stop", KeyCode.H);
            keysKey = Keybind.Add("cse_keys_hand", "Satsuma keys: take out / put away", KeyCode.Alpha3);
            dropKeysKey = Keybind.Add("cse_keys_drop", "Satsuma keys: drop (while in hand)", KeyCode.G);

            Settings.AddHeader("Remote start");
            warmTemp = Settings.AddSlider("cse_warm_temp", "Warm up until engine reaches (C)", 40, 80, 60, null, null);
            warmMinutes = Settings.AddSlider("cse_warm_minutes", "Stop after at most (minutes)", 3, 20, 10, null, null);

            Settings.AddHeader("Reset");
            Settings.AddText("Removes every part, wire, fob and pending order of this mod and starts over. " +
                             "Your game save is not touched. Works in the main menu and in game.");
            Settings.AddButton("Reset Car Systems Extended", AskReset);

            Settings.AddHeader("Debug");
            debugMode = Settings.AddCheckBox("cse_debug", "Debug mode (overlay, test keys, mount editor)", false, null);
            spawnKitKey = Keybind.Add("cse_spawn_alarm", "Debug: spawn alarm kit in front of you", KeyCode.F7);
            spawnLocksKey = Keybind.Add("cse_spawn_locks", "Debug: spawn door lock kit in front of you", KeyCode.F4);
            spawnServoKey = Keybind.Add("cse_spawn_servo", "Debug: spawn choke servo in front of you", KeyCode.F3);
            deliverKey = Keybind.Add("cse_deliver_now", "Debug: deliver posted orders now", KeyCode.F2);
            mountEditorKey = Keybind.Add("cse_mount_editor", "Debug: toggle mount editor", KeyCode.F6);
            testSirenKey = Keybind.Add("cse_test_siren", "Debug: test siren chirp", KeyCode.F5);
            auditionKey = Keybind.Add("cse_sound_audition", "Debug: play next candidate game sound", KeyCode.F1);
        }

        bool Debug
        {
            get { return debugMode != null && debugMode.GetValue(); }
        }

        void OnLoad()
        {
            try
            {
                if (!GameRefs.Init()) return;
                CatalogUI.ResetState();
                frameErrors.Clear();
                ToolHook.Init();
                PartManager.Reset();
                AlarmWiring.Init();
                Flasher.Init();
                DoorLocks.Init();
                AlarmSystem.Init(lockKey);
                KeySystem.Init(keysKey, dropKeysKey);
                RemoteStart.Init(warmTemp, warmMinutes);
                MailOrder.Init();
                MountEditor.Init(ModLoader.GetModSettingsFolder(this));
                SoundAudition.Init();
                var save = SaveSystem.Load(this);
                KeySystem.Restore(save != null ? save.KeysPocket : null, save != null ? save.KeysIgnition : null,
                    save != null && save.KeysSaved, save != null && save.FobOnKeys);
                KeySystem.SpareGiven = save != null && save.SpareKeyGiven;
                MailOrder.AfterLoad();
                KeySystem.AfterLoad();
                if (ModLoader.IsModPresent("Key"))
                    ModConsole.Warning("[CSE] The Key mod is also installed: it hangs its own keyring on the ignition key. Disable it to avoid two keyrings.");
                ready = true;
                ModConsole.Print("[CSE] Car Systems Extended " + Version + " loaded, " + PartManager.Parts.Count + " parts restored");
            }
            catch (Exception e)
            {
                ModConsole.Error("[CSE] Load failed: " + e);
            }
        }

        void OnSave()
        {
            if (!ready) return;
            try
            {
                SaveSystem.Save(this);
            }
            catch (Exception e)
            {
                ModConsole.Error("[CSE] Save failed: " + e);
            }
        }

        // Per-frame systems in order. Each runs on its own, so one that keeps failing (another
        // mod changed the car, an unexpected game state) does not take the rest down with it:
        // the keys, doors and alarm keep working even if, say, the mail order breaks.
        static readonly Action tickLook = Look.Update, tickKeys = KeySystem.Tick, tickParts = PartManager.Tick,
            tickDoors = DoorLocks.Tick, tickTool = ToolHook.Tick, tickWiring = AlarmWiring.Tick,
            tickAlarm = AlarmSystem.Tick, tickRemoteStart = RemoteStart.Tick, tickMail = MailOrder.Tick,
            tickCatalog = CatalogUI.Tick, tickMountEditor = MountEditor.Tick, tickHud = Gui.Apply;
        Action remoteButtons, debugKeys;

        void OnUpdate()
        {
            if (!ready) return;
            // While the catalog is open, clicks belong to the page, not the world.
            bool reading = CatalogUI.IsOpen;
            if (reading) Run("catalog", tickCatalog);
            else
            {
                Run("look", tickLook);
                Run("keys", tickKeys);
                Run("parts", tickParts);
                Run("doors", tickDoors);
                Run("remote", remoteButtons);
            }

            Run("wrench", tickTool);
            Run("wiring", tickWiring);
            Run("alarm", tickAlarm);
            Run("remote start", tickRemoteStart);
            Run("mail order", tickMail);
            if (Debug && !reading) Run("debug", debugKeys);
            Run("mount editor", tickMountEditor);
            Run("hud", tickHud);
        }

        void RemoteButtons()
        {
            if (lockKey.GetKeybindDown()) AlarmSystem.OnLockPressed();
            if (unlockKey.GetKeybindDown()) AlarmSystem.OnUnlockPressed();
            if (startKey.GetKeybindDown()) RemoteStart.OnButton();
        }

        void DebugKeys()
        {
            if (deliverKey.GetKeybindDown()) MailOrder.DeliverAllNow();
            if (spawnKitKey.GetKeybindDown()) SpawnKit(PartCatalog.Kits.Alarm);
            if (spawnLocksKey.GetKeybindDown()) SpawnKit(PartCatalog.Kits.DoorLocks);
            if (spawnServoKey.GetKeybindDown()) SpawnKit(PartCatalog.Kits.ChokeServo);
            if (mountEditorKey.GetKeybindDown()) MountEditor.Toggle();
            if (testSirenKey.GetKeybindDown()) AlarmSystem.Chirp(2);
            if (auditionKey.GetKeybindDown()) SoundAudition.PlayNext();
        }

        void Run(string system, Action tick)
        {
            try
            {
                tick();
            }
            catch (Exception e)
            {
                // Keep running, but do not flood the console if it repeats every frame.
                int n;
                frameErrors.TryGetValue(system, out n);
                frameErrors[system] = ++n;
                if (n <= 3) ModConsole.Error("[CSE] " + system + ": " + e);
                if (n == 3) ModConsole.Error("[CSE] Further " + system + " errors suppressed until the next load");
            }
        }

        void OnGui()
        {
            if (!ready) return;
            if (Debug) DebugOverlay.Draw();
            CatalogUI.Draw();
        }

        void ApplyLanguage()
        {
            if (language == null) return;
            L.Current = language.GetSelectedItemIndex() == 1 ? L.Lang.Russian : L.Lang.English;
            if (ready) PartManager.RenameAll();
        }

        // ---------------------------------------------------------------- reset

        void AskReset()
        {
            ModUI.ShowYesNoMessage(
                "Remove every Car Systems Extended part, wire and pending order and start over?\n" +
                "The old state is kept as a backup file.",
                "Reset Car Systems Extended", DoReset);
        }

        void DoReset()
        {
            try
            {
                // Keep the previous state as a backup either way.
                string folder = ModLoader.GetModSettingsFolder(this);
                string file = System.IO.Path.Combine(folder, "cse_save");
                if (System.IO.File.Exists(file))
                {
                    string backup = file + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    System.IO.File.Copy(file, backup, true);
                    System.IO.File.Delete(file);
                }

                if (ready)
                {
                    // In game: undo everything live, then write the empty state right away so
                    // quitting without saving does not bring the old parts back.
                    CatalogUI.Close();
                    RemoteStart.ResetState();
                    DoorLocks.ForceUnlockAll();
                    AlarmSystem.ResetState();
                    PartManager.RemoveAll();
                    AlarmWiring.ResetState();
                    MailOrder.Pending.Clear();
                    MailOrder.BrochureGiven = false;
                    MailOrder.AfterLoad(); // hands out a fresh brochure
                    KeySystem.ResetState(); // keys back in the pocket (or lock), fresh spare key
                    SaveSystem.Save(this);
                }
                ModConsole.Print("[CSE] Mod reset" + (ready ? " in game" : ", takes effect when you load the game"));
            }
            catch (Exception e)
            {
                ModConsole.Error("[CSE] Reset failed: " + e);
            }
        }

        static void SpawnKit(PartDef[] kit)
        {
            var cam = GameRefs.Camera.transform;
            Vector3 origin = cam.position + cam.forward * 0.8f;
            for (int i = 0; i < kit.Length; i++)
            {
                Vector3 pos = origin + cam.right * ((i - (kit.Length - 1) * 0.5f) * 0.2f);
                PartManager.Spawn(kit[i], pos, Quaternion.identity);
            }
            ModConsole.Print("[CSE] Spawned kit: " + kit.Length + " parts");
        }
    }
}
