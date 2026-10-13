using CSE.Core;
using UnityEngine;

namespace CSE.Parts
{
    // Definitions of every part in the mod. Mount offsets are first guesses and get tuned in game
    // with the mount editor (debug mode).
    //
    // Car space: +X = right (passenger side), +Y = up, +Z = forward. The car is left-hand drive.
    public static class PartCatalog
    {
        /// Build action: the Blender model when it is embedded, else the placeholder shapes. Parts
        /// with a status LED get the mod's glowing lens on the model's "led" marker.
        static System.Action<Transform> WithModel(string model, float ledSize, System.Action<Transform> placeholder)
        {
            return t =>
            {
                var m = Models.BuildOr(t, model, placeholder);
                if (m != null && ledSize > 0f && m.HasMarker("led")) Shapes.AddLed(t, m.Marker("led", Vector3.zero), ledSize);
            };
        }

        // ---------------------------------------------------------------- alarm kit

        public static readonly PartDef AlarmUnit = new PartDef
        {
            Id = "cse_alarm_unit",
            Name = "alarm unit",
            Mass = 0.6f,
            ColliderSize = new Vector3(0.19f, 0.04f, 0.095f),
            Mounts = new[]
            {
                new MountDef
                {
                    Label = "under dash",
                    RefPath = "Dashboard/Steering/steering_column2",
                    // placed in game with the mount editor
                    Offset = new Vector3(0.232f, 0.008f, 0.276f),
                    Euler = new Vector3(90f, 0f, 180f),
                },
            },
            Bolts = new[]
            {
                // Hangs under the dash: bolts go up through the ears, heads face down.
                new BoltDef(new Vector3(-0.085f, -0.0175f, 0f), new Vector3(90, 0, 0), 7),
                new BoltDef(new Vector3(0.085f, -0.0175f, 0f), new Vector3(90, 0, 0), 7),
            },
            PlugPos = new Vector3(-0.04f, 0f, -0.05f),
            Model = "alarm_unit",
            Build = WithModel("alarm_unit", 0.008f, t =>
            {
                Shapes.Add(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.13f, 0.035f, 0.09f), Shapes.Black);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(-0.08f, -0.0155f, 0f), new Vector3(0.035f, 0.004f, 0.03f), Shapes.Steel);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(0.08f, -0.0155f, 0f), new Vector3(0.035f, 0.004f, 0.03f), Shapes.Steel);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(-0.04f, 0f, -0.05f), new Vector3(0.04f, 0.018f, 0.012f), Shapes.DarkGrey);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(0.045f, 0.0215f, -0.03f), new Vector3(0.03f, 0.008f, 0.014f), Shapes.DarkGrey);
                Shapes.AddLed(t, new Vector3(0.03f, 0.005f, 0.045f), 0.008f);
            }),
        };

        public static readonly PartDef Siren = new PartDef
        {
            Id = "cse_siren",
            Name = "alarm siren",
            Mass = 0.8f,
            ColliderSize = new Vector3(0.095f, 0.11f, 0.07f),
            ColliderCenter = new Vector3(0f, -0.01f, 0f),
            Mounts = new[]
            {
                new MountDef
                {
                    Label = "engine bay",
                    RefPath = "MiscParts/pivot_battery",
                    // placed in game with the mount editor
                    Offset = new Vector3(-0.06f, 0.104f, -0.513f),
                    Euler = new Vector3(-90f, 0f, 180f),
                },
            },
            Bolts = new[]
            {
                new BoltDef(new Vector3(0f, -0.048f, -0.015f), new Vector3(-90, 0, 0), 10),
            },
            Wired = true,
            PlugPos = new Vector3(0f, 0f, -0.035f),
            Model = "siren",
            Build = WithModel("siren", 0f, t =>
            {
                Shapes.Add(t, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.09f, 0.03f, 0.09f), Shapes.Black, new Vector3(90, 0, 0));
                Shapes.Add(t, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.031f), new Vector3(0.07f, 0.002f, 0.07f), Shapes.DarkGrey, new Vector3(90, 0, 0));
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(0f, -0.05f, -0.01f), new Vector3(0.03f, 0.004f, 0.06f), Shapes.Steel);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(0f, -0.027f, 0.018f), new Vector3(0.03f, 0.05f, 0.004f), Shapes.Steel);
            }),
        };

        public static readonly PartDef Fuse = new PartDef
        {
            Id = "cse_fuse",
            Name = "fuse 15a",
            Mass = 0.05f,
            ColliderSize = new Vector3(0.02f, 0.02f, 0.008f),
            Mounts = new[]
            {
                new MountDef
                {
                    Label = "alarm unit fuse slot",
                    ParentPart = "cse_alarm_unit",
                    Offset = new Vector3(0.045f, 0.031f, -0.03f),
                    Tolerance = 0.08f,
                },
            },
            Model = "fuse",
            Build = WithModel("fuse", 0f, t =>
            {
                Shapes.Add(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.019f, 0.012f, 0.005f), new Color(0.15f, 0.35f, 0.9f), default(Vector3), "body");
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(-0.005f, -0.009f, 0f), new Vector3(0.003f, 0.007f, 0.001f), Shapes.Steel);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(0.005f, -0.009f, 0f), new Vector3(0.003f, 0.007f, 0.001f), Shapes.Steel);
            }),
        };

        /// Status LED on top of the dash by the windscreen, visible from outside.
        public static readonly PartDef StatusLed = new PartDef
        {
            Id = "cse_status_led",
            Name = "alarm led",
            Mass = 0.05f,
            ColliderSize = new Vector3(0.03f, 0.02f, 0.03f),
            ColliderCenter = new Vector3(0f, 0.005f, 0f),
            Mounts = new[]
            {
                new MountDef
                {
                    Label = "dash top by the windscreen",
                    // placed in game with the mount editor
                    Offset = new Vector3(-0.364f, 0.45f, 0.64f),
                    Euler = new Vector3(0f, -5f, 0f),
                },
            },
            // No bolts: like the real thing it is pushed into a hole drilled in the dash top.
            Wired = true,
            PlugPos = new Vector3(0f, -0.004f, -0.012f),
            Model = "status_led",
            Build = WithModel("status_led", 0.0085f, t =>
            {
                Shapes.Add(t, PrimitiveType.Cylinder, new Vector3(0f, -0.002f, 0f), new Vector3(0.018f, 0.0015f, 0.018f), Shapes.Black);
                Shapes.Add(t, PrimitiveType.Cylinder, new Vector3(0f, 0.003f, 0f), new Vector3(0.012f, 0.005f, 0.012f), Shapes.Black);
                Shapes.AddLed(t, new Vector3(0f, 0.009f, 0f), 0.008f);
            }),
        };

        /// Key fob. Not mounted: it goes onto the Satsuma keyring (see AlarmSystem).
        public static readonly PartDef Fob = new PartDef
        {
            Id = "cse_fob",
            Name = "alarm remote",
            Mass = 0.05f,
            ColliderSize = new Vector3(0.035f, 0.014f, 0.06f),
            Model = "fob",
            Build = t =>
            {
                // Blender model with a glowing LED on its marker; placeholder shapes without it.
                var m = Models.BuildOr(t, "fob", FobPlaceholder);
                if (m != null) Shapes.AddLed(t, m.Marker("led", new Vector3(0f, 0.006f, 0.024f)), 0.0034f);
            },
        };

        /// Where the remote hangs from a keyring, in remote space (its lug hole).
        public static Vector3 FobRingHole
        {
            get
            {
                var m = Models.Get("fob");
                return m != null ? m.Marker("ring", new Vector3(0f, 0f, -0.03f)) : new Vector3(0f, 0f, -0.03f);
            }
        }

        static void FobPlaceholder(Transform t)
        {
            {
                // Three buttons: lock, unlock, remote start. Keyring hole at the back end (-Z).
                Shapes.Add(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.032f, 0.012f, 0.056f), Shapes.Black);
                Shapes.Add(t, PrimitiveType.Cylinder, new Vector3(-0.008f, 0.0065f, 0.01f), new Vector3(0.01f, 0.001f, 0.01f), Shapes.DarkGrey);
                Shapes.Add(t, PrimitiveType.Cylinder, new Vector3(0.008f, 0.0065f, 0.01f), new Vector3(0.01f, 0.001f, 0.01f), Shapes.DarkGrey);
                Shapes.Add(t, PrimitiveType.Cylinder, new Vector3(0f, 0.0065f, -0.008f), new Vector3(0.01f, 0.001f, 0.01f), new Color(0.45f, 0.08f, 0.06f));
                Shapes.AddLed(t, new Vector3(0f, 0.006f, 0.022f), 0.004f);
                Shapes.Add(t, PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.03f), new Vector3(0.008f, 0.0015f, 0.008f), Shapes.Steel, new Vector3(0, 0, 90));
            }
        }

        /// Satsuma keys as an item. The keyring state (remote, battery) lives in Part.Data and
        /// KeySystem; picking it up puts it in the pocket.
        public static readonly PartDef Keys = new PartDef
        {
            Id = "cse_keys",
            Name = "satsuma keys",
            Mass = 0.06f,
            ColliderSize = new Vector3(0.04f, 0.02f, 0.08f),
            ColliderCenter = new Vector3(0f, 0f, -0.025f),
            Model = "key",
            Build = Systems.KeyRingView.BuildKey,
        };

        /// 12 V A23 battery for the remote.
        public static readonly PartDef FobBattery = new PartDef
        {
            Id = "cse_fob_battery",
            Name = "a23 battery",
            Mass = 0.01f,
            ColliderSize = new Vector3(0.02f, 0.03f, 0.02f),
            Model = "a23_battery",
            Build = WithModel("a23_battery", 0f, t =>
            {
                Shapes.Add(t, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.01f, 0.0125f, 0.01f), new Color(0.1f, 0.25f, 0.6f));
                Shapes.Add(t, PrimitiveType.Cylinder, new Vector3(0f, 0.0128f, 0f), new Vector3(0.004f, 0.0006f, 0.004f), Shapes.Steel);
            }),
        };

        // ---------------------------------------------------------------- door lock kit

        public static readonly PartDef LockActuator = new PartDef
        {
            Id = "cse_lock_actuator",
            Name = "door lock actuator",
            Mass = 0.3f,
            ColliderSize = new Vector3(0.04f, 0.07f, 0.09f),
            ColliderCenter = new Vector3(0f, 0.01f, 0f),
            // Inside each door near the latch. Bolt head faces the cabin: +X on the left door,
            // the right-door mount turns the part around.
            Mounts = new[]
            {
                new MountDef
                {
                    Label = "left door",
                    RefPath = "Body/pivot_door_left/door left(Clone)",
                    AttachToRef = true,
                    // placed in game with the mount editor
                    Offset = new Vector3(0.044f, 0.07f, -0.89f),
                },
                new MountDef
                {
                    Label = "right door",
                    RefPath = "Body/pivot_door_right/door right(Clone)",
                    AttachToRef = true,
                    Offset = new Vector3(-0.044f, 0.08f, -0.888f),
                    Euler = new Vector3(0, 180, 0),
                },
            },
            Bolts = new[]
            {
                new BoltDef(new Vector3(0.019f, 0f, 0f), new Vector3(0, 90, 0), 6),
            },
            Wired = true,
            PlugPos = new Vector3(0f, 0f, -0.045f),
            Model = "lock_actuator",
            Build = WithModel("lock_actuator", 0f, t =>
            {
                Shapes.Add(t, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.03f, 0.04f, 0.03f), Shapes.Black, new Vector3(90, 0, 0));
                Shapes.Add(t, PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0.03f), new Vector3(0.005f, 0.03f, 0.005f), Shapes.Steel);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(0.017f, 0f, 0f), new Vector3(0.004f, 0.03f, 0.05f), Shapes.Steel);
            }),
        };

        // ---------------------------------------------------------------- remote start

        /// Pulls the choke cable for remote starts. Sits on the body next to the carburettor,
        /// so it stays put when the engine comes out (but needs the engine in place to fit).
        public static readonly PartDef ChokeServo = new PartDef
        {
            Id = "cse_choke_servo",
            Name = "choke servo",
            Mass = 0.4f,
            ColliderSize = new Vector3(0.065f, 0.045f, 0.055f),
            Mounts = new[]
            {
                new MountDef
                {
                    Label = "by the carburettor",
                    RefPath = "Chassis/sub frame(xxxxx)/CarMotorPivot/block(Clone)/pivot_cylinder head/cylinder head(Clone)/pivot_carburator",
                    // placed in game with the mount editor
                    Offset = new Vector3(0.122f, 0.072f, -0.217f),
                    Euler = new Vector3(-90f, 180f, 0f),
                },
            },
            Bolts = new[]
            {
                new BoltDef(new Vector3(0f, -0.0225f, 0.035f), new Vector3(90, 0, 0), 8),
            },
            Wired = true,
            PlugPos = new Vector3(-0.033f, 0f, 0f),
            Model = "choke_servo",
            Build = WithModel("choke_servo", 0f, t =>
            {
                Shapes.Add(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.06f, 0.04f, 0.05f), Shapes.DarkGrey);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(0f, -0.0215f, 0.03f), new Vector3(0.03f, 0.003f, 0.025f), Shapes.Steel);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(0.035f, 0.01f, 0f), new Vector3(0.006f, 0.035f, 0.008f), Shapes.Steel);
                Shapes.Add(t, PrimitiveType.Cylinder, new Vector3(0.06f, 0.025f, 0f), new Vector3(0.003f, 0.03f, 0.003f), Shapes.Black, new Vector3(0, 0, 90));
            }),
        };

        // ---------------------------------------------------------------- mail order items
        // Behaviour (reading, posting, opening) is attached by Shop.MailOrder.Init.

        static readonly Color Paper = new Color(0.93f, 0.91f, 0.84f);
        static readonly Color Cardboard = new Color(0.62f, 0.47f, 0.3f);

        public static readonly PartDef Brochure = new PartDef
        {
            Id = "cse_brochure",
            Name = "autotarvike catalog",
            Mass = 0.2f,
            ColliderSize = new Vector3(0.21f, 0.01f, 0.297f),
            Build = t =>
            {
                // A4 stack of pages with the spring 1995 cover printed on top.
                Shapes.Add(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.21f, 0.006f, 0.297f), Paper);
                Shapes.AddPicture(t, new Vector3(0f, 0.0031f, 0f), new Vector2(0.21f, 0.297f),
                    Textures.Embedded("catalog_cover.jpg"), new Color(0.2f, 0.3f, 0.55f));
            },
        };

        public static readonly PartDef Envelope = new PartDef
        {
            Id = "cse_envelope",
            Name = "order envelope",
            Mass = 0.05f,
            ColliderSize = new Vector3(0.22f, 0.01f, 0.11f),
            Model = "envelope",
            Build = WithModel("envelope", 0f, t =>
            {
                Shapes.Add(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.22f, 0.004f, 0.11f), Paper);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(0.085f, 0.0025f, 0.03f), new Vector3(0.025f, 0.001f, 0.03f), new Color(0.75f, 0.1f, 0.08f));
            }),
        };

        public static readonly PartDef Parcel = new PartDef
        {
            Id = "cse_parcel",
            Name = "parcel",
            Mass = 3f,
            ColliderSize = new Vector3(0.4f, 0.22f, 0.3f),
            Model = "parcel",
            Build = WithModel("parcel", 0f, t =>
            {
                Shapes.Add(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.4f, 0.22f, 0.3f), Cardboard);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(0f, 0.111f, 0f), new Vector3(0.05f, 0.001f, 0.302f), new Color(0.8f, 0.75f, 0.6f));
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(0.08f, 0.111f, 0.07f), new Vector3(0.12f, 0.001f, 0.08f), Paper);
            }),
        };

        // Parents before children: load order depends on it.
        public static readonly PartDef[] All =
        {
            AlarmUnit, Siren, Fuse, StatusLed, Fob, LockActuator, ChokeServo,
            Brochure, Envelope, Parcel,
            Keys, FobBattery,
        };

        public static class Kits
        {
            public static readonly PartDef[] Alarm = { AlarmUnit, Siren, Fuse, Fuse, StatusLed, Fob };
            public static readonly PartDef[] DoorLocks = { LockActuator, LockActuator };
            public static readonly PartDef[] ChokeServo = { PartCatalog.ChokeServo };
            public static readonly PartDef[] Batteries = { FobBattery, FobBattery };
            public static readonly PartDef[] Fuses = { Fuse, Fuse, Fuse, Fuse, Fuse };
            public static readonly PartDef[] Remote = { Fob };
        }
    }
}
