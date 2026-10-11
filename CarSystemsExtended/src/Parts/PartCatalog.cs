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
                    Offset = new Vector3(-0.12f, -0.18f, -0.05f),
                },
            },
            Bolts = new[]
            {
                // Hangs under the dash: bolts go up through the ears, heads face down.
                new BoltDef(new Vector3(-0.085f, -0.0175f, 0f), new Vector3(90, 0, 0), 7),
                new BoltDef(new Vector3(0.085f, -0.0175f, 0f), new Vector3(90, 0, 0), 7),
            },
            PlugPos = new Vector3(-0.04f, 0f, -0.05f),
            Build = t =>
            {
                Shapes.Add(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.13f, 0.035f, 0.09f), Shapes.Black);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(-0.08f, -0.0155f, 0f), new Vector3(0.035f, 0.004f, 0.03f), Shapes.Steel);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(0.08f, -0.0155f, 0f), new Vector3(0.035f, 0.004f, 0.03f), Shapes.Steel);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(-0.04f, 0f, -0.05f), new Vector3(0.04f, 0.018f, 0.012f), Shapes.DarkGrey);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(0.045f, 0.0215f, -0.03f), new Vector3(0.03f, 0.008f, 0.014f), Shapes.DarkGrey);
                Shapes.Add(t, PrimitiveType.Sphere, new Vector3(0.03f, 0.005f, 0.045f), new Vector3(0.008f, 0.008f, 0.008f), Shapes.LedOff, default(Vector3), "led");
            },
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
                    Offset = new Vector3(0.17f, 0.1f, 0.12f),
                },
            },
            Bolts = new[]
            {
                new BoltDef(new Vector3(0f, -0.048f, -0.015f), new Vector3(-90, 0, 0), 10),
            },
            Wired = true,
            PlugPos = new Vector3(0f, 0f, -0.035f),
            Build = t =>
            {
                Shapes.Add(t, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.09f, 0.03f, 0.09f), Shapes.Black, new Vector3(90, 0, 0));
                Shapes.Add(t, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.031f), new Vector3(0.07f, 0.002f, 0.07f), Shapes.DarkGrey, new Vector3(90, 0, 0));
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(0f, -0.05f, -0.01f), new Vector3(0.03f, 0.004f, 0.06f), Shapes.Steel);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(0f, -0.027f, 0.018f), new Vector3(0.03f, 0.05f, 0.004f), Shapes.Steel);
            },
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
            Build = t =>
            {
                Shapes.Add(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.019f, 0.012f, 0.005f), new Color(0.15f, 0.35f, 0.9f), default(Vector3), "body");
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(-0.005f, -0.009f, 0f), new Vector3(0.003f, 0.007f, 0.001f), Shapes.Steel);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(0.005f, -0.009f, 0f), new Vector3(0.003f, 0.007f, 0.001f), Shapes.Steel);
            },
        };

        /// Key fob. Not mounted: it goes onto the Satsuma keyring (see AlarmSystem).
        public static readonly PartDef Fob = new PartDef
        {
            Id = "cse_fob",
            Name = "alarm remote",
            Mass = 0.05f,
            ColliderSize = new Vector3(0.035f, 0.014f, 0.06f),
            Build = t =>
            {
                Shapes.Add(t, PrimitiveType.Cube, Vector3.zero, new Vector3(0.032f, 0.012f, 0.056f), Shapes.Black);
                Shapes.Add(t, PrimitiveType.Cylinder, new Vector3(0f, 0.0065f, 0.008f), new Vector3(0.012f, 0.001f, 0.012f), Shapes.DarkGrey);
                Shapes.Add(t, PrimitiveType.Cylinder, new Vector3(0f, 0.0065f, -0.01f), new Vector3(0.012f, 0.001f, 0.012f), Shapes.DarkGrey);
                Shapes.Add(t, PrimitiveType.Sphere, new Vector3(0f, 0.006f, 0.022f), new Vector3(0.004f, 0.004f, 0.004f), Shapes.LedRed);
                Shapes.Add(t, PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.032f), new Vector3(0.012f, 0.0015f, 0.012f), Shapes.Steel, new Vector3(0, 0, 90));
            },
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
                    Offset = new Vector3(0.06f, -0.2f, -0.9f),
                },
                new MountDef
                {
                    Label = "right door",
                    RefPath = "Body/pivot_door_right/door right(Clone)",
                    AttachToRef = true,
                    Offset = new Vector3(-0.06f, -0.2f, -0.9f),
                    Euler = new Vector3(0, 180, 0),
                },
            },
            Bolts = new[]
            {
                new BoltDef(new Vector3(0.019f, 0f, 0f), new Vector3(0, 90, 0), 6),
            },
            Wired = true,
            PlugPos = new Vector3(0f, 0f, -0.045f),
            Build = t =>
            {
                Shapes.Add(t, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.03f, 0.04f, 0.03f), Shapes.Black, new Vector3(90, 0, 0));
                Shapes.Add(t, PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0.03f), new Vector3(0.005f, 0.03f, 0.005f), Shapes.Steel);
                Shapes.Add(t, PrimitiveType.Cube, new Vector3(0.017f, 0f, 0f), new Vector3(0.004f, 0.03f, 0.05f), Shapes.Steel);
            },
        };

        // Parents before children: load order depends on it.
        public static readonly PartDef[] All = { AlarmUnit, Siren, Fuse, Fob, LockActuator };

        public static class Kits
        {
            public static readonly PartDef[] Alarm = { AlarmUnit, Siren, Fuse, Fuse, Fob };
            public static readonly PartDef[] DoorLocks = { LockActuator, LockActuator };
        }
    }
}
