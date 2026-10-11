using System.Collections.Generic;
using CSE.Core;
using MSCLoader;
using UnityEngine;

namespace CSE.Parts
{
    // A physical part: lies in the world as a pickable item, snaps onto its mount when held close
    // and clicked, is held by bolts, and can be removed with RMB once all bolts are out.
    public class Part : Interactable
    {
        public PartDef Def;
        public string Uid;
        public bool Installed { get; private set; }
        public bool WireConnected;
        /// Blown fuse, broken device. Meaning depends on the part.
        public bool Damaged;
        public readonly List<Bolt> Bolts = new List<Bolt>();
        public Transform Plug;

        Transform boltRoot;
        float pendingInstall = -1f;

        public static Part Create(PartDef def, string uid)
        {
            var go = new GameObject(def.Name + "(itemx)");
            go.layer = LayerMask.NameToLayer("Parts");

            var col = go.AddComponent<BoxCollider>();
            col.size = def.ColliderSize;
            col.center = def.ColliderCenter;

            var part = go.AddComponent<Part>();
            part.Def = def;
            part.Uid = uid;

            if (def.Build != null) def.Build(go.transform);

            var plug = new GameObject("plug");
            plug.transform.parent = go.transform;
            plug.transform.localPosition = def.PlugPos;
            part.Plug = plug.transform;

            part.boltRoot = new GameObject("Bolts").transform;
            part.boltRoot.parent = go.transform;
            part.boltRoot.localPosition = Vector3.zero;
            part.boltRoot.localRotation = Quaternion.identity;
            foreach (var b in def.Bolts)
                part.Bolts.Add(Bolt.Create(part, part.boltRoot, b));
            part.boltRoot.gameObject.SetActive(false);

            part.MakeLoose();
            return part;
        }

        // ---------------------------------------------------------------- state

        public bool AllBoltsTight
        {
            get
            {
                foreach (var b in Bolts) if (!b.Tight) return false;
                return true;
            }
        }

        public bool AnyBoltIn
        {
            get
            {
                foreach (var b in Bolts) if (b.Stage > 0) return true;
                return false;
            }
        }

        /// Installed and every bolt fully tightened.
        public bool Secured
        {
            get { return Installed && AllBoltsTight; }
        }

        void MakeLoose()
        {
            var rb = GetComponent<Rigidbody>();
            if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = Def.Mass;
            rb.isKinematic = false;
            gameObject.MakePickable();
            gameObject.tag = "PART";
            gameObject.layer = LayerMask.NameToLayer("Parts");
        }

        /// Snaps the part onto its mount. Returns false if the mount is not available.
        public bool Install(int[] boltStages = null, bool silent = false)
        {
            Transform parent;
            Vector3 pos;
            Quaternion rot;
            if (!PartManager.ResolveMount(this, out parent, out pos, out rot)) return false;

            var rb = GetComponent<Rigidbody>();
            if (rb != null) Destroy(rb);
            gameObject.tag = "Untagged";
            transform.parent = parent;
            transform.localPosition = pos;
            transform.localRotation = rot;

            Installed = true;
            boltRoot.gameObject.SetActive(true);
            for (int i = 0; i < Bolts.Count; i++)
                Bolts[i].SetStage(boltStages != null && i < boltStages.Length ? boltStages[i] : 0);

            if (!silent) Sfx.Assemble(transform.position);
            PartManager.OnPartChanged(this);
            return true;
        }

        public void Uninstall(bool silent = false)
        {
            Installed = false;
            WireConnected = false;
            foreach (var b in Bolts) b.SetStage(0);
            boltRoot.gameObject.SetActive(false);
            transform.parent = null;
            MakeLoose();
            if (!silent) Sfx.Disassemble(transform.position);
            PartManager.OnPartChanged(this);
        }

        /// Re-applies the mount position (used by the mount editor).
        public void Reseat()
        {
            Transform parent;
            Vector3 pos;
            Quaternion rot;
            if (!Installed || !PartManager.ResolveMount(this, out parent, out pos, out rot)) return;
            transform.parent = parent;
            transform.localPosition = pos;
            transform.localRotation = rot;
        }

        public void OnBoltsChanged()
        {
            PartManager.OnPartChanged(this);
        }

        // ---------------------------------------------------------------- per-frame

        /// Called every frame by PartManager.
        public void Tick()
        {
            if (pendingInstall >= 0f)
            {
                // Give the game's hand a moment to drop the item before we re-parent it,
                // the same delay the game's own assembly triggers use.
                if (Time.time >= pendingInstall)
                {
                    pendingInstall = -1f;
                    Install();
                }
                return;
            }

            if (Installed || !GameRefs.IsHeld(gameObject)) return;
            if (!PartManager.CanAssembleHere(this)) return;

            Gui.Assemble();
            if (Input.GetMouseButtonDown(0))
                pendingInstall = Time.time + 0.05f;
        }

        public override void OnHover()
        {
            if (!Installed) return; // the game itself handles picking up loose items

            if (Def.Wired && PartManager.AlarmUnitInstalled)
            {
                if (!WireConnected)
                {
                    Gui.Interaction("CONNECT WIRE");
                    if (Input.GetMouseButtonDown(0))
                    {
                        WireConnected = true;
                        Sfx.Game("CarFoley", "carkeys_in", Plug.position, 0.6f);
                        PartManager.OnPartChanged(this);
                    }
                    // fall through so the part can still be removed while unwired
                }
                else
                {
                    Gui.Interaction("DISCONNECT WIRE");
                    if (Input.GetMouseButtonDown(1))
                    {
                        WireConnected = false;
                        Sfx.Game("CarFoley", "carkeys_out", Plug.position, 0.6f);
                        PartManager.OnPartChanged(this);
                    }
                    return;
                }
            }

            if (AnyBoltIn) return;

            string blocker = PartManager.RemovalBlocker(this);
            if (blocker != null)
            {
                Gui.Interaction(blocker);
                return;
            }

            Gui.Disassemble();
            if (Input.GetMouseButtonDown(1))
                Uninstall();
        }
    }
}
