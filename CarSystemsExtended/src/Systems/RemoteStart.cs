using CSE.Core;
using CSE.Parts;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace CSE.Systems
{
    // Remote start and warm-up, done the way a 1995 aftermarket unit would: it powers the
    // ignition circuit itself (no key), cranks in a few short bursts, keeps the engine running
    // until it is warm or a time limit passes, then cuts the ignition. The game simulates the
    // rest (fuel, battery, starter wear), so a car that will not start by key will not start
    // remotely either.
    //
    // The driver takes over by disarming and turning the key: the unit then lets go and the
    // engine keeps running on the key.
    public static class RemoteStart
    {
        public enum Phase { Off, Preparing, Cranking, Pausing, Running }

        const int MaxAttempts = 3;
        const float CrankTime = 2.5f;
        const float PauseTime = 3f;

        public static Phase Current { get; private set; }
        public static string LastResult { get; private set; }

        static int attempt;
        static float phaseUntil;
        static float runStart;

        static GameObject electrics;
        static GameObject warnings;
        static PlayMakerFSM starter;
        static FsmBool starterAcc, starting, shutOff, electricsOk, ignitionAcc;
        static FsmFloat handbrake, engineTemp;
        static FsmFloat chokeKnob, chokeKnobPos, chokeLevel;
        static Transform chokeKnobMesh;
        static Drivetrain drivetrain;

        static SettingsSliderInt targetTempSetting, maxMinutesSetting;

        public static void Init(SettingsSliderInt targetTemp, SettingsSliderInt maxMinutes)
        {
            targetTempSetting = targetTemp;
            maxMinutesSetting = maxMinutes;
            Current = Phase.Off;
            LastResult = null;

            var st = GameRefs.FindInCar("CarSimulation/Car/Starter");
            starter = st != null ? st.gameObject.GetPlayMaker("Starter") : null;
            if (starter != null)
            {
                starterAcc = starter.FsmVariables.FindFsmBool("ACC");
                starting = starter.FsmVariables.FindFsmBool("Starting");
                shutOff = starter.FsmVariables.FindFsmBool("ShutOff");
            }

            var el = GameRefs.FindInCar("CarSimulation/Car/Electrics");
            electrics = el != null ? el.gameObject : null;
            var elFsm = electrics != null ? electrics.GetPlayMaker("Electrics") : null;
            electricsOk = elFsm != null ? elFsm.FsmVariables.FindFsmBool("ElectricsOK") : null;

            var w = GameRefs.FindInCar("Dashboard/pivot_dashboard/dashboard(Clone)/pivot_meters/dashboard meters(Clone)/Warnings");
            warnings = w != null ? w.gameObject : null;

            var ign = GameRefs.FindInCar("Dashboard/Steering/steering_column2/IgnitionSatsuma");
            var ignFsm = ign != null ? ign.gameObject.GetPlayMaker("UseNew") : null;
            ignitionAcc = ignFsm != null ? ignFsm.FsmVariables.FindFsmBool("ACC") : null;

            var hb = GameRefs.FindInCar("MiscParts/HandBrake/handbrake(xxxxx)/handbrake lever");
            var hbFsm = hb != null ? hb.gameObject.GetPlayMaker("Use") : null;
            handbrake = hbFsm != null ? hbFsm.FsmVariables.FindFsmFloat("KnobPos") : null;

            engineTemp = FsmVariables.GlobalVariables.FindFsmFloat("EngineTemp");
            drivetrain = GameRefs.Car.GetComponent<Drivetrain>();

            var ch = GameRefs.FindInCar("Dashboard/pivot_dashboard/dashboard(Clone)/pivot_meters/dashboard meters(Clone)/Knobs/ButtonsDash/Choke");
            if (ch != null)
            {
                var use = ch.gameObject.GetPlayMaker("Use");
                var lvl = ch.gameObject.GetPlayMaker("Choke");
                chokeKnob = use != null ? use.FsmVariables.FindFsmFloat("Choke") : null;
                chokeKnobPos = use != null ? use.FsmVariables.FindFsmFloat("KnobPos") : null;
                var target = use != null ? use.FsmVariables.FindFsmGameObject("Target") : null;
                chokeKnobMesh = target != null && target.Value != null ? target.Value.transform : null;
                chokeLevel = lvl != null ? lvl.FsmVariables.FindFsmFloat("ChokeLevel") : null;
            }

            if (starter == null || electrics == null || drivetrain == null)
                ModConsole.Error("[CSE] Remote start: starter/electrics/drivetrain not found, remote start disabled");
        }

        public static bool Active
        {
            get { return Current != Phase.Off; }
        }

        static bool EngineRunning
        {
            get { return starter != null && starter.ActiveStateName == "Running"; }
        }

        static bool KeyOn
        {
            get { return ignitionAcc != null && ignitionAcc.Value; }
        }

        static float TargetTemp
        {
            get { return targetTempSetting != null ? targetTempSetting.GetValue() : 60; }
        }

        static float MaxSeconds
        {
            get { return (maxMinutesSetting != null ? maxMinutesSetting.GetValue() : 10) * 60f; }
        }

        // ---------------------------------------------------------------- fob button

        // Two-way remote: the fob beeps when it sends, the car acts when the signal arrives a
        // moment later, and the fob plays a tune when the car reports back.
        const float SignalDelay = 0.6f;
        const float ReplyDelay = 0.5f;

        enum Reply { None, Ok, Fail, Stopped }

        static bool commandPending;
        static float commandAt;
        static Reply reply;
        static float replyAt;

        public static void OnButton()
        {
            if (!AlarmSystem.FobWorks || starter == null) return;
            Sfx.FobSend();
            // Out of power the car simply never answers.
            if (!AlarmSystem.Powered) return;
            commandPending = true;
            commandAt = Time.time + SignalDelay;
        }

        static void SendReply(Reply r)
        {
            reply = r;
            replyAt = Time.time + ReplyDelay;
        }

        static void TickRadio()
        {
            if (commandPending && Time.time >= commandAt)
            {
                commandPending = false;
                if (AlarmSystem.Powered) Execute();
            }
            if (reply != Reply.None && Time.time >= replyAt)
            {
                // The fob only hears the car while in range.
                if (AlarmSystem.FobWorks)
                {
                    if (reply == Reply.Ok) Sfx.FobOk();
                    else if (reply == Reply.Fail) Sfx.FobFail();
                    else Sfx.FobStop();
                }
                reply = Reply.None;
            }
        }

        static void Execute()
        {
            if (Active)
            {
                Stop("stopped from remote");
                SendReply(Reply.Stopped);
                return;
            }

            string refuse = WhyNot();
            if (refuse != null)
            {
                LastResult = "refused: " + refuse;
                AlarmSystem.Chirp(3);
                SendReply(Reply.Fail);
                return;
            }

            LastResult = null;
            attempt = 1;
            electrics.SetActive(true);
            starterAcc.Value = true;
            SetPhase(Phase.Preparing, 0.8f);
            Flasher.Flash(1);
        }

        static string WhyNot()
        {
            if (EngineRunning) return "engine already running";
            if (KeyOn) return "key is turned";
            if (drivetrain.gear != drivetrain.neutral) return "not in neutral";
            if (handbrake != null && handbrake.Value < 15f) return "handbrake off";
            if (AlarmSystem.HoodOpen) return "hood open";
            if (AlarmSystem.Current == AlarmSystem.Mode.Triggered) return "alarm going off";
            return null;
        }

        static void SetPhase(Phase p, float duration)
        {
            Current = p;
            phaseUntil = Time.time + duration;
        }

        // ---------------------------------------------------------------- frame

        public static void Tick()
        {
            TickRadio();
            if (!Active) return;

            // Driver turned the key: hand over, the engine keeps running on the key.
            if (KeyOn)
            {
                LastResult = "handed over to key";
                Current = Phase.Off;
                if (starting != null) starting.Value = false;
                return;
            }

            if (!AlarmSystem.Powered) { Stop("alarm unit lost power"); return; }
            if (AlarmSystem.Current == AlarmSystem.Mode.Triggered) { Stop("alarm triggered"); return; }
            if (drivetrain.gear != drivetrain.neutral) { Stop("put in gear"); return; }

            UpdateChoke();

            switch (Current)
            {
                case Phase.Preparing:
                    if (Time.time >= phaseUntil) Crank();
                    break;

                case Phase.Cranking:
                    if (EngineRunning)
                    {
                        starting.Value = false;
                        runStart = Time.time;
                        SetPhase(Phase.Running, 0f);
                        Flasher.Flash(2);
                        SendReply(Reply.Ok);
                    }
                    else if (Time.time >= phaseUntil)
                    {
                        starting.Value = false;
                        if (++attempt > MaxAttempts)
                        {
                            Stop("did not start");
                            AlarmSystem.Chirp(3);
                            SendReply(Reply.Fail);
                        }
                        else SetPhase(Phase.Pausing, PauseTime);
                    }
                    else starting.Value = true; // the game drops it if the player clicks LMB
                    break;

                case Phase.Pausing:
                    if (Time.time >= phaseUntil) Crank();
                    break;

                case Phase.Running:
                    if (!EngineRunning) Stop("engine stalled");
                    else if (engineTemp != null && engineTemp.Value >= TargetTemp) Stop("warmed up to " + engineTemp.Value.ToString("0") + " C");
                    else if (Time.time - runStart > MaxSeconds) Stop("time limit");
                    break;
            }
        }

        static void Crank()
        {
            starting.Value = true;
            SetPhase(Phase.Cranking, CrankTime);
        }

        /// Cuts the ignition the way the game's own "Motor OFF" does.
        public static void Stop(string reason)
        {
            if (!Active) return;
            Current = Phase.Off;
            LastResult = reason;
            ModConsole.Print("[CSE] Remote start: " + reason);

            if (starting != null) starting.Value = false;
            if (starterAcc != null) starterAcc.Value = false;
            if (shutOff != null) shutOff.Value = true;
            if (electricsOk != null) electricsOk.Value = false;
            if (electrics != null) electrics.SetActive(false);
            if (warnings != null) warnings.SetActive(false);
            SetChoke(0f);
        }

        // ---------------------------------------------------------------- choke servo

        static bool ServoReady
        {
            get
            {
                var s = PartManager.FindInstalled(PartCatalog.ChokeServo.Id);
                return s != null && s.Secured && s.WireConnected;
            }
        }

        /// Full choke when cold, easing off to none by 45 C.
        static void UpdateChoke()
        {
            if (!ServoReady || engineTemp == null) return;
            SetChoke(Mathf.Clamp01((45f - engineTemp.Value) / 30f));
        }

        static void SetChoke(float level)
        {
            if (!ServoReady || chokeLevel == null) return;
            chokeLevel.Value = level;
            if (chokeKnob != null) chokeKnob.Value = level;
            if (chokeKnobPos != null) chokeKnobPos.Value = -0.03f * level;
            if (chokeKnobMesh != null)
            {
                var p = chokeKnobMesh.localPosition;
                chokeKnobMesh.localPosition = new Vector3(p.x, -0.03f * level, p.z);
            }
        }
    }
}
