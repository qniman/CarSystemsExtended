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

        // ---- saved as installed, but its mount was not there yet when the save loaded
        // (engine parts attached a moment later, an optimisation mod toggling the car)
        const float RestoreTimeout = 30f;
        int restoreMount = -1;
        int[] restoreBolts;
        bool restoreWire;
        float restoreUntil;

        // ---- loose-item physics safety (see TickLoosePhysics)
        const float MinColliderSize = 0.02f;
        const float FreezeDistance = 40f;   // parked beyond this from the player
        const float WakeDistance = 35f;     // hysteresis so it does not flicker at the edge
        const float RescueDrop = 4f;        // fell this far below where it last rested

        Rigidbody body;
        bool frozen;
        float settleUntil;
        Vector3 restPos;
        bool hasRest;

        public static Part Create(PartDef def, string uid)
        {
            var go = new GameObject(DisplayName(def));
            go.layer = LayerMask.NameToLayer("Parts");

            // Paper-thin boxes tunnel through floors; give every collider some body.
            var col = go.AddComponent<BoxCollider>();
            var model = def.Model != null ? Models.Get(def.Model) : null;
            col.size = Vector3.Max(model != null ? model.Bounds.size : def.ColliderSize, Vector3.one * MinColliderSize);
            col.center = model != null ? model.Bounds.center : def.ColliderCenter;

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
            // Spawned or loaded parts wait a moment (and for ground under them) before falling.
            part.FreezeUntilSettled();
            return part;
        }

        /// The game shows a loose item's object name up to the bracket, so the name is the label.
        static string DisplayName(PartDef def)
        {
            return L.PartName(def) + "(itemx)";
        }

        /// Re-applies the name after a language change.
        public void Rename()
        {
            gameObject.name = DisplayName(Def);
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
            // Small, light and often thrown: discrete collision lets them slip through floors.
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            body = rb;
            frozen = false;
            restPos = transform.position;
            hasRest = true;
            gameObject.MakePickable();
            gameObject.tag = "PART";
            gameObject.layer = LayerMask.NameToLayer("Parts");
        }

        /// Snaps the part onto its mount. Returns false if the mount is not available. restoring =
        /// loading a save: the mount object only has to exist, it may be switched off for now.
        public bool Install(int mountIndex, int[] boltStages = null, bool silent = false, bool restoring = false)
        {
            Transform parent;
            Vector3 pos;
            Quaternion rot;
            if (!PartManager.ResolveMount(this, mountIndex, out parent, out pos, out rot, restoring)) return false;
            MountIndex = mountIndex;

            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                // Destroy is deferred to the end of the frame; keep it from being simulated as a
                // loose body inside the car until then.
                rb.isKinematic = true;
                Destroy(rb);
            }
            body = null;
            frozen = false;
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

        /// Keeps retrying a saved installation until the mount shows up (or gives up and leaves the
        /// part loose). Meanwhile the part hangs frozen where it was saved.
        public void RestoreLater(int mountIndex, int[] boltStages, bool wire)
        {
            restoreMount = mountIndex;
            restoreBolts = boltStages;
            restoreWire = wire;
            restoreUntil = Time.time + RestoreTimeout;
            SetFrozen(true);
        }

        /// Waiting for its mount after a load; saved as installed meanwhile.
        public bool AwaitingRestore
        {
            get { return restoreMount >= 0; }
        }

        public int RestoreMount
        {
            get { return restoreMount; }
        }

        public int[] RestoreBolts
        {
            get { return restoreBolts; }
        }

        public bool RestoreWire
        {
            get { return restoreWire; }
        }

        void TickRestore()
        {
            if (Install(restoreMount, restoreBolts, true, true))
            {
                ModConsole.Log("[CSE] " + Uid + " reinstalled once its mount appeared");
                restoreMount = -1;
                WireConnected = restoreWire;
                PartManager.OnPartChanged(this);
                return;
            }
            if (Time.time < restoreUntil)
            {
                SetFrozen(true);
                return;
            }
            ModConsole.Warning("[CSE] Could not reinstall " + Uid + ", left loose");
            restoreMount = -1;
            FreezeUntilSettled();
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

        /// Shows a damaged part as such (a blown fuse goes dark). Safe to call any time.
        public void ApplyDamagedLook()
        {
            if (!Damaged) return;
            var burnt = new Color(0.05f, 0.07f, 0.12f);
            var body = transform.Find("body");
            if (body != null)
            {
                var r = body.GetComponent<Renderer>();
                if (r != null) r.material.color = burnt;
                return;
            }
            // Blender model: darken its "fuse_body" material (instance names get a suffix).
            var model = transform.Find("model");
            var mr = model != null ? model.GetComponent<Renderer>() : null;
            if (mr == null) return;
            foreach (var m in mr.materials)
                if (m.name.StartsWith("cse_fuse_body")) m.color = burnt;
        }

        public void OnBoltsChanged()
        {
            PartManager.OnPartChanged(this);
        }

        // ---------------------------------------------------------------- per-frame

        /// Called every frame by PartManager.
        public void Tick()
        {
            if (restoreMount >= 0)
            {
                TickRestore();
                return;
            }
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

            if (Installed) return;
            TickLoosePhysics();
            if (!GameRefs.IsHeld(gameObject)) return;
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

        // ---------------------------------------------------------------- falling through floors
        //
        // Three things make loose items vanish under the map in MSC: thin colliders tunnelling,
        // floors that are not loaded yet (savegame load, store LOD switched off far away), and
        // nothing to catch them when it happens. So a loose part
        //   * stays kinematic for a moment after spawning and until there is ground under it,
        //   * is parked (kinematic) while the player is far away and the world may unload,
        //   * is put back where it last rested if it drops far below that spot.

        void FreezeUntilSettled()
        {
            settleUntil = Time.time + 1.5f;
            SetFrozen(true);
        }

        void SetFrozen(bool on)
        {
            frozen = on;
            if (body == null) return;
            body.isKinematic = on;
            if (!on) body.WakeUp();
        }

        bool GroundBelow()
        {
            foreach (var h in Physics.RaycastAll(transform.position + Vector3.up * 0.05f, Vector3.down, 3f))
            {
                if (h.collider.isTrigger || h.collider.transform.IsChildOf(transform)) continue;
                return true;
            }
            return false;
        }

        void TickLoosePhysics()
        {
            if (body == null || Price > 0) return; // for sale: frozen on the counter by design

            if (GameRefs.IsHeld(gameObject))
            {
                // In hand the game moves it; the hand is a safe spot to fall back to.
                if (frozen) SetFrozen(false);
                restPos = transform.position;
                hasRest = true;
                return;
            }

            var cam = GameRefs.Camera;
            float dist = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 0f;

            if (frozen)
            {
                if (Time.time >= settleUntil && dist < WakeDistance && GroundBelow()) SetFrozen(false);
                return;
            }

            if (dist > FreezeDistance)
            {
                SetFrozen(true);
                return;
            }

            if (body.velocity.sqrMagnitude < 0.01f)
            {
                restPos = transform.position;
                hasRest = true;
            }
            else if (hasRest && transform.position.y < restPos.y - RescueDrop && body.velocity.y < -2f)
            {
                ModConsole.Log("[CSE] " + Uid + " fell through the floor, put back");
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                transform.position = restPos + Vector3.up * 0.2f;
                FreezeUntilSettled();
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
                Gui.Interaction(L.T("disconnect_wire"));
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
                Gui.Interaction(L.T("disconnect_power"));
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
