using CSE.Core;
using CSE.Parts;
using MSCLoader;
using UnityEngine;

namespace CSE.Systems
{
    // What the keyring looks like in the player's hand and in the ignition, with the remote
    // hanging from the ring on a real physics joint.
    public static class KeyRingView
    {
        // Where the keyring sits in the right hand, relative to the camera pivot (next to where
        // the game holds its tools).
        static readonly Vector3 HandPos = new Vector3(0.11f, -0.13f, 0.24f);
        static readonly Vector3 HandEuler = new Vector3(15f, -20f, 0f);

        static Transform handParent;
        static int playerLayer;

        static GameObject hand;
        static Dangler handFob;
        static bool handHasFob;

        static GameObject ignition;
        static Dangler ignitionFob;
        static Transform keyMesh;
        static float blinkUntil;

        public static void Init()
        {
            var p = GameObject.Find("PLAYER/Pivot/AnimPivot/Camera/FPSCamera");
            handParent = p != null ? p.transform : null;
            playerLayer = LayerMask.NameToLayer("Player");
            keyMesh = GameRefs.FindInCar("Dashboard/Steering/steering_column2/Ignition/Keys/Key");
            hand = ignition = null;
            handFob = ignitionFob = null;
            if (handParent == null) ModConsole.Warning("[CSE] Camera pivot not found, keys cannot be shown in hand");
        }

        // ---------------------------------------------------------------- building

        /// Key, bow and ring. Ring centre at the local origin, key hanging along -Z.
        public static void BuildKey(Transform t)
        {
            Shapes.Add(t, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.026f, 0.0015f, 0.026f), Shapes.Steel, Vector3.zero, "ring");
            Shapes.Add(t, PrimitiveType.Cube, new Vector3(0f, 0f, -0.022f), new Vector3(0.022f, 0.004f, 0.02f), Shapes.Black, Vector3.zero, "bow");
            Shapes.Add(t, PrimitiveType.Cube, new Vector3(0f, 0f, -0.05f), new Vector3(0.008f, 0.002f, 0.04f), Shapes.Steel, Vector3.zero, "blade");
        }

        /// A loose keyring in the world shows its remote rigidly attached.
        public static void Decorate(Part keys, KeyRing ring)
        {
            var old = keys.transform.Find("fob");
            if (old != null) Object.Destroy(old.gameObject);
            if (!ring.Fob) return;
            var fob = new GameObject("fob").transform;
            fob.parent = keys.transform;
            fob.localPosition = new Vector3(0.018f, 0f, 0.028f);
            fob.localRotation = Quaternion.Euler(0f, 30f, 0f);
            PartCatalog.Fob.Build(fob);
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        // ---------------------------------------------------------------- hand

        public static void ShowInHand(KeyRing ring)
        {
            HideHand();
            if (handParent == null) return;
            hand = new GameObject("cse_keys_in_hand");
            hand.transform.parent = handParent;
            hand.transform.localPosition = HandPos;
            hand.transform.localRotation = Quaternion.Euler(HandEuler);
            BuildKey(hand.transform);
            SetLayer(hand.transform, playerLayer);
            handHasFob = ring.Fob;
            if (ring.Fob) handFob = Dangler.Create(hand.transform, null, playerLayer);
        }

        public static void HideHand()
        {
            if (handFob != null) Object.Destroy(handFob.gameObject);
            if (hand != null) Object.Destroy(hand);
            handFob = null;
            hand = null;
        }

        /// Remote LED flash when a button is pressed.
        public static void Blink(float battery)
        {
            blinkUntil = Time.time + (battery < 30f ? 0.08f : 0.2f);
        }

        // ---------------------------------------------------------------- ignition

        static void BuildIgnition()
        {
            DestroyIgnition();
            Transform parent;
            Vector3 pos;
            Quaternion rot;
            if (!PartManager.ResolveMount(PartCatalog.KeyringAnchor.Mounts[0], null, out parent, out pos, out rot)) return;
            ignition = new GameObject("cse_keyring_in_ignition");
            ignition.transform.parent = parent;
            ignition.transform.localPosition = pos;
            ignition.transform.localRotation = rot;
            Shapes.Add(ignition.transform, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.026f, 0.0015f, 0.026f), Shapes.Steel, new Vector3(90, 0, 0), "ring");
        }

        static void DestroyIgnition()
        {
            if (ignitionFob != null) Object.Destroy(ignitionFob.gameObject);
            if (ignition != null) Object.Destroy(ignition);
            ignitionFob = null;
            ignition = null;
        }

        // ---------------------------------------------------------------- frame

        public static void Tick(KeyRing inHand, KeyRing inIgnition)
        {
            // Rebuild if a remote was put on or taken off while showing.
            if (inHand != null && hand != null && handHasFob != inHand.Fob) ShowInHand(inHand);

            bool wantIgnition = inIgnition != null && keyMesh != null && keyMesh.gameObject.activeInHierarchy;
            if (wantIgnition && ignition == null) BuildIgnition();
            if (!wantIgnition && ignition != null) DestroyIgnition();
            if (wantIgnition && ignition != null && inIgnition.Fob != (ignitionFob != null))
            {
                if (ignitionFob != null) Object.Destroy(ignitionFob.gameObject);
                ignitionFob = inIgnition.Fob
                    ? Dangler.Create(ignition.transform, GameRefs.Car.GetComponent<Rigidbody>(), 0)
                    : null;
            }

            if (handFob != null) handFob.SetLed(Time.time < blinkUntil, inHand != null ? inHand.Battery : 0f);
        }

        /// Mount editor: re-place the ring after its anchor moved.
        public static void Reseat()
        {
            if (ignition != null) BuildIgnition();
        }
    }

    // The remote hanging from the keyring. A free rigidbody held at its keyring hole by a
    // ConfigurableJoint. In the ignition the joint is tied to the car body so it swings with the
    // car's own motion (like the Key mod does); in the hand it is tied to a world point that
    // follows the hand. The anchor point is moved every physics step so it also follows the key
    // turning in the lock.
    public class Dangler : MonoBehaviour
    {
        // Keyring hole of the remote, in remote space (back end, see PartCatalog.Fob).
        static readonly Vector3 Hole = new Vector3(0f, 0f, -0.032f);

        Transform ring;
        Rigidbody carBody;
        ConfigurableJoint joint;
        Material led;

        public static Dangler Create(Transform ring, Rigidbody carBody, int layer)
        {
            var go = new GameObject("cse_fob_dangling");
            go.layer = layer;
            PartCatalog.Fob.Build(go.transform);
            foreach (Transform c in go.transform) c.gameObject.layer = layer;

            // hang straight down from the ring
            go.transform.rotation = Quaternion.LookRotation(Vector3.down, ring.forward);
            go.transform.position = ring.position - go.transform.TransformVector(Hole);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.04f;
            rb.drag = 0.6f;
            rb.angularDrag = 1.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            // No collider (it must not snag on the dash or the player), so Unity cannot work out
            // the inertia; give it that of a 32x12x56 mm, 40 g box or it swings like it is in syrup.
            rb.inertiaTensor = new Vector3(1.1e-5f, 1.4e-5f, 0.4e-5f);
            rb.inertiaTensorRotation = Quaternion.identity;

            var j = go.AddComponent<ConfigurableJoint>();
            j.autoConfigureConnectedAnchor = false;
            j.anchor = Hole;
            j.connectedBody = carBody;
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Limited;
            j.lowAngularXLimit = new SoftJointLimit { limit = -80f };
            j.highAngularXLimit = new SoftJointLimit { limit = 80f };
            j.angularYLimit = new SoftJointLimit { limit = 80f };
            j.angularZLimit = new SoftJointLimit { limit = 45f };

            var d = go.AddComponent<Dangler>();
            d.ring = ring;
            d.carBody = carBody;
            d.joint = j;
            var ledT = go.transform.Find("led");
            d.led = ledT != null ? ledT.GetComponent<Renderer>().material : null;
            d.UpdateAnchor();
            return d;
        }

        void UpdateAnchor()
        {
            if (ring == null || joint == null) return;
            joint.connectedAnchor = carBody != null ? carBody.transform.InverseTransformPoint(ring.position) : ring.position;
        }

        void FixedUpdate()
        {
            if (ring == null)
            {
                Destroy(gameObject);
                return;
            }
            UpdateAnchor();
        }

        public void SetLed(bool on, float battery)
        {
            if (led == null) return;
            // a weak battery gives a dim, short flash
            Color lit = battery < 30f ? Color.Lerp(Shapes.LedOff, Shapes.LedRed, 0.35f) : Shapes.LedRed;
            led.color = on ? lit : Shapes.LedOff;
        }
    }
}
