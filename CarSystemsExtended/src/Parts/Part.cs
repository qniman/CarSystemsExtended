using System.Collections.Generic;
using CSE.Core;
using CSE.Electrics;
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
        /// Which of the definition's mounts the part sits on (valid while installed).
        public int MountIndex = -1;
        /// Free-form payload (order contents of an envelope or parcel).
        public string Data;
        /// Unpaid price in marks; while above zero the item sits on the counter and cannot be taken.
        public int Price { get; private set; }
        public readonly List<Bolt> Bolts = new List<Bolt>();
        public Transform Plug;

        Transform boltRoot;
        float pendingInstall = -1f;
        int pendingMount = -1;

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

        /// Puts the item up for sale: frozen in place and not pickable until paid.
        public void SetForSale(int price)
        {
            Price = price;
            var rb = GetComponent<Rigidbody>();
            if (price > 0)
            {
                if (rb != null) rb.isKinematic = true;
                gameObject.tag = "Untagged";
            }
            else MakeLoose();
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
        public bool Install(int mountIndex, int[] boltStages = null, bool silent = false)
        {
            Transform parent;
            Vector3 pos;
            Quaternion rot;
            if (!PartManager.ResolveMount(this, mountIndex, out parent, out pos, out rot)) return false;
            MountIndex = mountIndex;

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
            MountIndex = -1;
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
            if (!Installed || !PartManager.ResolveMount(this, MountIndex, out parent, out pos, out rot)) return;
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
                    Install(pendingMount);
                }
                return;
            }

            if (Installed || !GameRefs.IsHeld(gameObject)) return;
            if (Def.OnHeld != null && Def.OnHeld(this)) return;

            int mount = PartManager.AssemblyCandidate(this);
            if (mount < 0) return;

            Gui.Assemble();
            if (Input.GetMouseButtonDown(0))
            {
                pendingInstall = Time.time + 0.05f;
                pendingMount = mount;
            }
        }

        public override void OnHover()
        {
            if (!Installed)
            {
                // The game itself handles picking up loose items; we only add extras.
                if (Def.OnLooseHover != null) Def.OnLooseHover(this);
                return;
            }

            // Wires are laid with the wiring mess (AlarmWiring); pulling a plug needs no tool.
            if (Def.Wired && WireConnected)
            {
                Gui.Interaction("DISCONNECT WIRE (RMB)");
                if (Input.GetMouseButtonDown(1))
                {
                    WireConnected = false;
                    Sfx.Game("CarFoley", "carkeys_out", Plug.position, 0.6f);
                    PartManager.OnPartChanged(this);
                }
                return;
            }
            if (Def == PartCatalog.AlarmUnit && AlarmWiring.PowerConnected)
            {
                Gui.Interaction("DISCONNECT POWER (RMB)");
                if (Input.GetMouseButtonDown(1))
                {
                    AlarmWiring.PowerConnected = false;
                    Sfx.Game("CarFoley", "carkeys_out", Plug.position, 0.6f);
                    PartManager.OnPartChanged(this);
                }
                return;
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
