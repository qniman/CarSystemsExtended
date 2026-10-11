using System;
using UnityEngine;

namespace CSE.Parts
{
    // A bolt hole on a part, in the part's local space. The bolt screws in along its local -Z.
    public class BoltDef
    {
        public Vector3 Pos;
        public Vector3 Euler;
        public int Size; // mm

        public BoltDef(Vector3 pos, Vector3 euler, int size)
        {
            Pos = pos;
            Euler = euler;
            Size = size;
        }
    }

    // Where a part goes. Offsets and rotations are always given in car space (+X right, +Y up,
    // +Z forward) relative to the anchor, except for ParentPart mounts which use the parent
    // part's local space.
    public class MountDef
    {
        /// Path under the car root used as anchor. Null = car root.
        public string RefPath;
        /// The part becomes a child of the anchor and moves with it (doors, bootlid).
        /// The anchor must sit at its rest pose (closed) relative to its own parent.
        public bool AttachToRef;
        /// Id of the part this one mounts onto (e.g. a fuse into the alarm unit). Overrides RefPath.
        public string ParentPart;
        public Vector3 Offset;
        public Vector3 Euler;
        /// Max distance between held part and mount point to allow assembly.
        public float Tolerance = 0.12f;
        /// Short label for the mount editor.
        public string Label;
    }

    public class PartDef
    {
        public string Id;
        /// Shown in game when looking at the part; lowercase like the game's own parts.
        public string Name;
        public float Mass = 0.5f;
        public Vector3 ColliderSize = new Vector3(0.1f, 0.1f, 0.1f);
        public Vector3 ColliderCenter;
        /// Alternative mounting spots; a part goes into any free one. Empty = loose item only.
        public MountDef[] Mounts = new MountDef[0];
        public BoltDef[] Bolts = new BoltDef[0];
        /// Needs a signal wire to the alarm unit.
        public bool Wired;
        /// Local position of the wire plug on the part.
        public Vector3 PlugPos;
        /// Builds placeholder visuals under the part root.
        public Action<Transform> Build;
        /// Called every frame while the player holds the part. Return true to skip assembly.
        public Func<Part, bool> OnHeld;
        /// Called every frame while the player looks at the part lying loose (not held).
        public Action<Part> OnLooseHover;
    }
}
