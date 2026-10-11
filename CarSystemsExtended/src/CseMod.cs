using System;
using CSE.Core;
using CSE.Debugging;
using CSE.Electrics;
using CSE.Parts;
using CSE.Save;
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
        SettingsKeybind spawnKitKey, mountEditorKey, testSirenKey;
        bool ready;

        public override void ModSetup()
        {
            SetupFunction(Setup.ModSettings, CreateSettings);
            SetupFunction(Setup.OnLoad, OnLoad);
            SetupFunction(Setup.OnSave, OnSave);
            SetupFunction(Setup.Update, OnUpdate);
            SetupFunction(Setup.OnGUI, OnGui);
        }

        void CreateSettings()
        {
            Settings.AddHeader("Debug");
            debugMode = Settings.AddCheckBox("cse_debug", "Debug mode (overlay, test keys, mount editor)", false, null);
            spawnKitKey = Keybind.Add("cse_spawn_alarm", "Debug: spawn alarm kit in front of you", KeyCode.F7);
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
                ToolHook.Init();
                PartManager.Reset();
                AlarmWiring.Init();
                AlarmUnit.Init();
                MountEditor.Init(ModLoader.GetModSettingsFolder(this));
                SaveSystem.Load(this);
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
                Look.Update();
                PartManager.Tick();
                AlarmWiring.Tick();
                AlarmUnit.Tick();

                if (Debug)
                {
                    if (spawnKitKey.GetKeybindDown()) SpawnKit(PartCatalog.Kits.Alarm);
                    if (mountEditorKey.GetKeybindDown()) MountEditor.Toggle();
                    if (testSirenKey.GetKeybindDown()) AlarmUnit.Chirp(2);
                }
                MountEditor.Tick();
                Gui.Apply();
            }
            catch (Exception e)
            {
                ModConsole.Error("[CSE] " + e);
                ready = false; // stop spamming the console every frame; reload the game to retry
            }
        }

        void OnGui()
        {
            if (ready && Debug) DebugOverlay.Draw();
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
