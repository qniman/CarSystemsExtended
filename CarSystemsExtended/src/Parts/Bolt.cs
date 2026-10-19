using CSE.Core;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace CSE.Parts
{
    // A bolt on an installed part. Built so the game's own wrench treats it as one of its bolts:
    // named "BoltPM", on the Bolts layer, with the size in mm/10 as the X scale. The game does the
    // aiming, size check, ratchet direction and highlighting; ToolHook forwards the resulting
    // tighten/untighten steps here.
    //
    // Like the game's bolts it also carries a "Screw" FSM (Stage, ThisBolt, TIGHTEN/UNTIGHTEN):
    // mods that animate the wrench (BetterMSC) read it and send the step to it instead of
    // going through the game's wrench FSM.
    public class Bolt : MonoBehaviour
    {
        public const int MaxStage = 8;
        // Per step, in the bolt object's (size-scaled) space, like the game's bolts: 60 degrees
        // and 2.5 mm * size/10 out. BetterMSC animates the mesh by the same amounts around and
        // along its local Z, so its animation ends exactly where ours puts the head.
        const float StepDepth = 0.0025f;
        const float StepAngle = 60f;

        public Part Owner;
        public int Size;
        public int Stage;

        Transform head;
        FsmInt fsmStage;

        public static Bolt Create(Part owner, Transform parent, BoltDef def)
        {
            float s = def.Size / 10f;
            var go = new GameObject("BoltPM");
            go.layer = LayerMask.NameToLayer("Bolts");
            go.transform.parent = parent;
            go.transform.localPosition = def.Pos;
            go.transform.localRotation = Quaternion.Euler(def.Euler);
            go.transform.localScale = new Vector3(s, s, s);

            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.015f / s;

            var bolt = go.AddComponent<Bolt>();
            bolt.Owner = owner;
            bolt.Size = def.Size;

            // The game highlights only the first child's renderer, so head and shank are one mesh
            // on a single child. Its scale undoes the size-encoding scale of the bolt object.
            var mesh = new GameObject("mesh");
            mesh.transform.parent = go.transform;
            mesh.transform.localPosition = Vector3.zero;
            mesh.transform.localScale = Vector3.one / s;
            mesh.AddComponent<MeshFilter>().sharedMesh = BoltMesh(def.Size);
            mesh.AddComponent<MeshRenderer>().sharedMaterial = SteelMaterial;
            bolt.head = mesh.transform;
            bolt.AddScrewFsm(go, mesh);
            bolt.UpdateVisual();
            return bolt;
        }

        void AddScrewFsm(GameObject go, GameObject mesh)
        {
            var fsm = go.AddComponent<PlayMakerFSM>();
            fsm.FsmName = "Screw";
            fsm.AddState("Wait");
            fsm.AddState("Tighten").AddAction(new StepAction { Bolt = this, Dir = 1 });
            fsm.AddState("Untighten").AddAction(new StepAction { Bolt = this, Dir = -1 });
            fsm.GetState("Tighten").AddTransition("FINISHED", "Wait");
            fsm.GetState("Untighten").AddTransition("FINISHED", "Wait");
            fsm.AddGlobalTransition("TIGHTEN", "Tighten");
            fsm.AddGlobalTransition("UNTIGHTEN", "Untighten");
            fsmStage = new FsmInt("Stage") { Value = Stage };
            fsm.AddVariable(fsmStage);
            fsm.AddVariable(new FsmGameObject("ThisBolt") { Value = mesh });
            fsm.AddVariable(new FsmGameObject("PartAssembled") { Value = Owner != null ? Owner.gameObject : null });
            fsm.Fsm.StartState = "Wait";
            fsm.InitializeFSM();
        }

        /// A step sent to the bolt's own FSM (another mod's wrench).
        class StepAction : FsmStateAction
        {
            public Bolt Bolt;
            public int Dir;

            public override void OnEnter()
            {
                if (Bolt != null) ToolHook.Step(Bolt, Dir);
                Finish();
            }
        }

        static Material steel;

        static Material SteelMaterial
        {
            get { return steel != null ? steel : (steel = Shapes.Material(Shapes.Steel)); }
        }

        static readonly System.Collections.Generic.Dictionary<int, Mesh> meshes =
            new System.Collections.Generic.Dictionary<int, Mesh>();

        /// Head at the origin, shank going down -Z (into the part), in metres: the bolt axis is
        /// the mesh's own Z, which is what other mods' wrench animations spin around.
        static Mesh BoltMesh(int size)
        {
            Mesh m;
            if (meshes.TryGetValue(size, out m) && m != null) return m;

            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var cyl = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(tmp);

            float head = size * 0.0016f;
            float shank = head * 0.55f;
            var toZ = Quaternion.Euler(90, 0, 0); // cylinder Y onto Z
            var parts = new CombineInstance[2];
            parts[0].mesh = cyl;
            parts[0].transform = Matrix4x4.TRS(Vector3.zero, toZ, new Vector3(head, 0.003f, head));
            parts[1].mesh = cyl;
            parts[1].transform = Matrix4x4.TRS(toZ * new Vector3(0, -0.0135f, 0), toZ, new Vector3(shank, 0.0105f, shank));

            m = new Mesh { name = "cse_bolt_" + size };
            m.CombineMeshes(parts, true, true);
            meshes[size] = m;
            return m;
        }

        public bool Tight
        {
            get { return Stage >= MaxStage; }
        }

        public void SetStage(int stage)
        {
            Stage = Mathf.Clamp(stage, 0, MaxStage);
            if (fsmStage != null) fsmStage.Value = Stage;
            UpdateVisual();
        }

        /// One wrench step from the game: +1 tighten, -1 loosen.
        public void Turn(int dir)
        {
            int next = Mathf.Clamp(Stage + dir, 0, MaxStage);
            if (next == Stage) return;
            SetStage(next);
            Sfx.BoltScrew(transform.position);
            Owner.OnBoltsChanged();
        }

        void UpdateVisual()
        {
            if (head == null) return;
            head.localPosition = new Vector3(0, 0, (MaxStage - Stage) * StepDepth);
            head.localRotation = Quaternion.AngleAxis(Stage * StepAngle, Vector3.forward);
        }
    }
}
