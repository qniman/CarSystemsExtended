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

    // Where a part goes. Either on the car (anchored to a reference object, offset in car space)
    // or on another part (offset in that part's local space).
    public class MountDef
    {
        /// Path under the car root used as anchor. Null = car root.
        public string RefPath;
        /// Id of the part this one mounts onto (e.g. a fuse into the alarm unit). Overrides RefPath.
        public string ParentPart;
        public Vector3 Offset;
        public Vector3 Euler;
        /// Max distance between held part and mount point to allow assembly.
        public float Tolerance = 0.12f;
    }

    public class PartDef
    {
        public string Id;
        /// Shown in game when looking at the part; lowercase like the game's own parts.
        public string Name;
        public float Mass = 0.5f;
        public Vector3 ColliderSize = new Vector3(0.1f, 0.1f, 0.1f);
        public Vector3 ColliderCenter;
        public MountDef Mount;
        public BoltDef[] Bolts = new BoltDef[0];
        /// Needs a signal wire to the alarm unit.
        public bool Wired;
        /// Local position of the wire plug on the part.
        public Vector3 PlugPos;
        /// Builds placeholder visuals under the part root.
        public Action<Transform> Build;
    }
}
