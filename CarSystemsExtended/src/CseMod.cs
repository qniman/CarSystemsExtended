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
        public override string Author => "CSE";
        public override string Version => "0.1.0";
        public override string Description => "Aftermarket car alarm, remote start and parking sensors for the Satsuma, 1995 style.";

        SettingsCheckBox debugMode;
        SettingsDropDownList language;
        int frameErrors;
        SettingsKeybind lockKey, unlockKey, startKey;
        SettingsSliderInt warmTemp, warmMinutes;
        SettingsKeybind spawnKitKey, spawnLocksKey, spawnServoKey, deliverKey, mountEditorKey, testSirenKey;
        bool ready;

        public override void ModSetup()
        {
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
                frameErrors = 0;
                ToolHook.Init();
                PartManager.Reset();
                AlarmWiring.Init();
                Flasher.Init();
                DoorLocks.Init();
                AlarmSystem.Init(lockKey);
                RemoteStart.Init(warmTemp, warmMinutes);
                MailOrder.Init();
                MountEditor.Init(ModLoader.GetModSettingsFolder(this));
                SaveSystem.Load(this);
                MailOrder.AfterLoad();
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

        void OnUpdate()
        {
            if (!ready) return;
            try
            {
                // While the catalog is open, clicks belong to the page, not the world.
                bool reading = CatalogUI.IsOpen;
                if (reading) CatalogUI.Tick();
                else
                {
                    Look.Update();
                    PartManager.Tick();
                    DoorLocks.Tick();
                    if (lockKey.GetKeybindDown()) AlarmSystem.OnLockPressed();
                    if (unlockKey.GetKeybindDown()) AlarmSystem.OnUnlockPressed();
                    if (startKey.GetKeybindDown()) RemoteStart.OnButton();
                }

                AlarmWiring.Tick();
                AlarmSystem.Tick();
                RemoteStart.Tick();
                MailOrder.Tick();

                if (Debug && !reading)
                {
                    if (deliverKey.GetKeybindDown()) MailOrder.DeliverAllNow();
                    if (spawnKitKey.GetKeybindDown()) SpawnKit(PartCatalog.Kits.Alarm);
                    if (spawnLocksKey.GetKeybindDown()) SpawnKit(PartCatalog.Kits.DoorLocks);
                    if (spawnServoKey.GetKeybindDown()) SpawnKit(PartCatalog.Kits.ChokeServo);
                    if (mountEditorKey.GetKeybindDown()) MountEditor.Toggle();
                    if (testSirenKey.GetKeybindDown()) AlarmSystem.Chirp(2);
                }
                MountEditor.Tick();
                Gui.Apply();
            }
            catch (Exception e)
            {
                // Keep running (one broken system should not take the rest down), but do not
                // flood the console if the same error repeats every frame.
                if (++frameErrors <= 3) ModConsole.Error("[CSE] " + e);
                if (frameErrors == 3) ModConsole.Error("[CSE] Further errors suppressed until the next load");
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
