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
        // Rest pose of the head mesh: the game bolt's tilt and seat (identity / zero for ours).
        Quaternion headBase = Quaternion.identity;
        Vector3 headSeat;

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
            // on a single child. Looks like the game's own bolts when one is found to copy;
            // otherwise our own mesh, whose scale undoes the size-encoding scale of the bolt.
            var mesh = new GameObject("mesh");
            mesh.transform.parent = go.transform;
            mesh.transform.localPosition = Vector3.zero;
            var look = GameLook(def.Size);
            if (look != null)
            {
                mesh.transform.localScale = look.Scale;
                mesh.AddComponent<MeshFilter>().sharedMesh = look.Mesh;
                mesh.AddComponent<MeshRenderer>().sharedMaterials = look.Materials;
                bolt.headBase = look.Base;
                bolt.headSeat = look.Seat;
            }
            else
            {
                mesh.transform.localScale = Vector3.one / s;
                mesh.AddComponent<MeshFilter>().sharedMesh = BoltMesh(def.Size);
                mesh.AddComponent<MeshRenderer>().sharedMaterial = SteelMaterial;
            }
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

        // ---------------------------------------------------------------- the game's bolt look

        class Look
        {
            public Mesh Mesh;
            public Material[] Materials;
            public Vector3 Scale;
            public Quaternion Base;
            public Vector3 Seat; // head position when fully tight
            public int Size;
        }

        static readonly System.Collections.Generic.List<Look> looks = new System.Collections.Generic.List<Look>();
        static bool looked;

        /// New game session: the game's bolts are new objects.
        public static void ForgetGameLook()
        {
            looks.Clear();
            looked = false;
        }

        /// The mesh, material and rest pose of a game bolt of this size (the nearest size if
        /// there is none), copied from the head mesh the bolt's own "Screw" FSM names.
        static Look GameLook(int size)
        {
            if (looks.Exists(l => l.Mesh == null)) ForgetGameLook(); // unloaded with a scene
            if (!looked)
            {
                looked = true;
                ScanGameBolts();
            }
            Look best = null;
            foreach (var l in looks)
                if (best == null || Mathf.Abs(l.Size - size) < Mathf.Abs(best.Size - size)) best = l;
            return best;
        }

        static void ScanGameBolts()
        {
            var root = GameRefs.Car;
            if (root == null) return;
            foreach (var fsm in root.GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (fsm.FsmName != "Screw" || fsm.gameObject.name != "BoltPM" || fsm.GetComponent<Bolt>() != null) continue;
                var thisBolt = fsm.FsmVariables.FindFsmGameObject("ThisBolt");
                var stage = fsm.FsmVariables.FindFsmInt("Stage");
                var m = thisBolt != null ? thisBolt.Value : null;
                if (m == null || m.transform.parent != fsm.transform) continue;
                var mf = m.GetComponent<MeshFilter>();
                var mr = m.GetComponent<MeshRenderer>();
                if (mf == null || mf.sharedMesh == null || mr == null) continue;
                int sz = Mathf.RoundToInt(fsm.transform.localScale.x * 10f);
                if (looks.Exists(l => l.Size == sz)) continue;

                var t = m.transform;
                var e = t.localEulerAngles;
                int st = stage != null ? stage.Value : MaxStage;
                looks.Add(new Look
                {
                    Mesh = mf.sharedMesh,
                    Materials = mr.sharedMaterials,
                    Scale = t.localScale,
                    Base = Quaternion.Euler(e.x, e.y, 0f),
                    Seat = t.localPosition - new Vector3(0f, 0f, (MaxStage - st) * StepDepth),
                    Size = sz,
                });
                ModConsole.Log("[CSE] Bolt look " + sz + " mm from " + fsm.transform.parent.name + ": mesh " + mf.sharedMesh.name);
            }
            if (looks.Count == 0) ModConsole.Warning("[CSE] No game bolt found to copy, using our own bolt mesh");
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
            head.localPosition = headSeat + new Vector3(0, 0, (MaxStage - Stage) * StepDepth);
            head.localRotation = headBase * Quaternion.AngleAxis(Stage * StepAngle, Vector3.forward);
        }
    }
}
