using System;
using CSE.Core;
using CSE.Parts;
using MSCLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CSE.Systems
{
    // What the keyring looks like in the player's hand and in the ignition. A chain like the real
    // thing: the key is held (or sits in the lock), the split ring hangs from the hole in its bow
    // and swings, and the remote hangs from the bottom of the ring and swings from that.
    public static class KeyRingView
    {
        // Keys held in the lower right of the view, blade forward, ring towards the player.
        static readonly Vector3 HandPos = new Vector3(0.14f, -0.12f, 0.32f);
        static readonly Vector3 HandEuler = new Vector3(8f, 170f, 15f);

        static GameObject hand;
        static bool handHasFob;
        static Swing handFob;

        static GameObject ignition;
        static bool ignitionHasFob;
        static Transform keyMesh;
        static Renderer hiddenGameKey;

        static float blinkUntil;

        // ---- HUD mode icon (top right), the same indicator the game uses for its tools
        static Material modeIcon;
        static Texture savedIcon;
        static Texture2D keysIcon;

        public static void Init()
        {
            keyMesh = GameRefs.FindInCar("Dashboard/Steering/steering_column2/Ignition/Keys/Key");
            hand = ignition = null;
            handFob = null;
            hiddenGameKey = null;
            var mode = GameObject.Find("GUI/Indicators/Mode");
            var r = mode != null ? mode.GetComponent<Renderer>() : null;
            modeIcon = r != null ? r.sharedMaterial : null;
            savedIcon = null;
            if (modeIcon == null) ModConsole.Warning("[CSE] HUD mode indicator not found, no keys icon");
        }

        // ---------------------------------------------------------------- pieces

        // keyring model: origin where it passes the key's hole, hanging along +Z
        static Vector3 RingFobPoint
        {
            get
            {
                var m = Models.Get("keyring");
                return m != null ? m.Marker("fob", new Vector3(0f, 0f, 0.024f)) : new Vector3(0f, 0f, 0.024f);
            }
        }

        static float RingLength
        {
            get
            {
                var m = Models.Get("keyring");
                return m != null ? m.Marker("center", new Vector3(0f, 0f, 0.012f)).magnitude : 0.012f;
            }
        }

        static Vector3 KeyRingHole(Model key)
        {
            return key != null ? key.Marker("ring", Vector3.zero) : Vector3.zero;
        }

        static void BuildRing(Transform t)
        {
            Models.BuildOr(t, "keyring", r =>
                Shapes.Add(r, PrimitiveType.Cylinder, new Vector3(0f, 0f, 0.012f), new Vector3(0.024f, 0.0015f, 0.024f),
                    Shapes.Steel, new Vector3(0, 0, 90), "ring"));
        }

        static void KeyPlaceholder(Transform t)
        {
            Shapes.Add(t, PrimitiveType.Cube, new Vector3(0f, 0f, -0.01f), new Vector3(0.022f, 0.004f, 0.02f), Shapes.Black, Vector3.zero, "bow");
            Shapes.Add(t, PrimitiveType.Cube, new Vector3(0f, 0f, -0.04f), new Vector3(0.008f, 0.002f, 0.04f), Shapes.Steel, Vector3.zero, "blade");
        }

        static Transform Child(Transform parent, string name, Vector3 localPos)
        {
            var c = new GameObject(name).transform;
            c.parent = parent;
            c.localPosition = localPos;
            c.localRotation = Quaternion.identity;
            c.localScale = Vector3.one;
            return c;
        }

        /// Static keyring (a loose item in the world, the mount ghost): key, ring hanging straight
        /// back from the bow, and a "fob_pivot" at the bottom of the ring.
        public static void BuildKey(Transform t)
        {
            var key = Models.BuildOr(t, "key", KeyPlaceholder);
            var ring = Child(t, "keyring", KeyRingHole(key));
            BuildRing(ring);
            Child(ring, "fob_pivot", RingFobPoint);
        }

        /// Key held rigidly at t, with the ring and (optionally) the remote swinging below it.
        /// Returns the remote's swing (for its LED) or null.
        static Swing BuildSwingingChain(Transform t, bool withKey, bool withFob, Swing.Feel feel)
        {
            var key = withKey ? Models.BuildOr(t, "key", KeyPlaceholder) : null;
            var ringPivot = Child(t, "ring_pivot", KeyRingHole(key));
            var ring = Swing.Create(ringPivot, feel, BuildRing, Vector3.zero, RingLength, "cse_keyring_swinging");
            if (!withFob) return null;
            var fobPivot = Child(ring.transform, "fob_pivot", RingFobPoint);
            var fob = Swing.Create(fobPivot, feel, PartCatalog.Fob.Build, PartCatalog.FobRingHole, 0.032f, "cse_fob_swinging");
            ring.Child = fob; // stepped right after the ring, so the chain moves as one
            return fob;
        }

        /// A loose keyring in the world shows its remote rigidly attached below the ring.
        public static void Decorate(Part keys, KeyRing ring)
        {
            var old = keys.transform.Find("fob");
            if (old != null) Object.Destroy(old.gameObject);
            if (!ring.Fob) return;
            var pivot = keys.transform.Find("keyring/fob_pivot");
            var fob = new GameObject("fob").transform;
            fob.parent = keys.transform;
            fob.localRotation = Quaternion.Euler(0f, 12f, 0f);
            Vector3 at = pivot != null ? keys.transform.InverseTransformPoint(pivot.position) : Vector3.zero;
            fob.localPosition = at - fob.localRotation * PartCatalog.FobRingHole;
            PartCatalog.Fob.Build(fob);
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        /// A layer the main camera actually draws (Default unless culled).
        static int VisibleLayer(Camera cam)
        {
            if ((cam.cullingMask & 1) != 0) return 0;
            for (int i = 0; i < 32; i++) if ((cam.cullingMask & (1 << i)) != 0) return i;
            return 0;
        }

        // ---------------------------------------------------------------- hand

        public static void ShowInHand(KeyRing ring)
        {
            HideHand();
            var cam = GameRefs.Camera;
            if (cam == null) return;

            // Child of the rendering camera itself, so it follows every look and step.
            hand = new GameObject("cse_keys_in_hand");
            hand.transform.parent = cam.transform;
            hand.transform.localPosition = HandPos;
            hand.transform.localRotation = Quaternion.Euler(HandEuler);
            handHasFob = ring.Fob;
            handFob = BuildSwingingChain(hand.transform, true, ring.Fob, Swing.Feel.Hand);
            SetLayer(hand.transform, VisibleLayer(cam));
            ShowIcon(true);
        }

        public static void HideHand()
        {
            if (hand != null)
            {
                Object.Destroy(hand);
                ShowIcon(false);
            }
            hand = null;
            handFob = null;
        }

        /// Remote LED flash when a button is pressed.
        public static void Blink(float battery)
        {
            blinkUntil = Time.time + (battery < 30f ? 0.08f : 0.2f);
        }

        // ---------------------------------------------------------------- HUD icon

        static void ShowIcon(bool on)
        {
            if (modeIcon == null) return;
            if (on)
            {
                if (modeIcon.mainTexture != KeysIcon) savedIcon = modeIcon.mainTexture;
                modeIcon.mainTexture = KeysIcon;
            }
            else if (modeIcon.mainTexture == KeysIcon)
            {
                // the game puts its own icon back when it is the one switching (keys 1/2)
                modeIcon.mainTexture = savedIcon;
            }
        }

        /// White key silhouette on transparent, in the style of the game's mode icons.
        static Texture2D KeysIcon
        {
            get
            {
                if (keysIcon != null) return keysIcon;
                const int S = 64;
                var t = new Texture2D(S, S, TextureFormat.ARGB32, false);
                t.wrapMode = TextureWrapMode.Clamp;
                var px = new Color[S * S];
                for (int y = 0; y < S; y++)
                {
                    for (int x = 0; x < S; x++)
                    {
                        // bow: ring with a hole
                        float dx = x - 18f, dy = y - 32f;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(12.5f - r) * Mathf.Clamp01(r - 5.5f);
                        // blade
                        if (x >= 28 && x <= 58 && y >= 29 && y <= 35) a = 1f;
                        // two teeth
                        if ((x >= 44 && x <= 48 || x >= 51 && x <= 55) && y >= 22 && y < 29) a = 1f;
                        px[y * S + x] = new Color(1f, 1f, 1f, a);
                    }
                }
                t.SetPixels(px);
                t.Apply();
                keysIcon = t;
                return t;
            }
        }

        // ---------------------------------------------------------------- ignition

        // The game's key, measured from its mesh bounds (world space): the bow end (farthest from
        // the lock cylinder, the "Keys" pivot it turns around), the tip in the lock, and the
        // normal of its flat side (the thinnest bounds axis).
        struct GameKey
        {
            public Vector3 Bow, Tip, Flat;
            public Renderer Renderer;
        }

        static bool MeasureGameKey(out GameKey k)
        {
            k = default(GameKey);
            var mf = keyMesh != null ? keyMesh.GetComponentInChildren<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) return false;
            var b = mf.sharedMesh.bounds;
            var t = mf.transform;
            Vector3[] axes = { t.TransformVector(new Vector3(b.extents.x, 0, 0)),
                               t.TransformVector(new Vector3(0, b.extents.y, 0)),
                               t.TransformVector(new Vector3(0, 0, b.extents.z)) };
            int longest = 0, thinnest = 0;
            for (int i = 1; i < 3; i++)
            {
                if (axes[i].sqrMagnitude > axes[longest].sqrMagnitude) longest = i;
                if (axes[i].sqrMagnitude < axes[thinnest].sqrMagnitude) thinnest = i;
            }
            Vector3 c = t.TransformPoint(b.center);
            Vector3 endA = c + axes[longest], endB = c - axes[longest];
            Vector3 lockPivot = keyMesh.parent != null ? keyMesh.parent.position : keyMesh.position;
            bool aIsBow = Vector3.Distance(endA, lockPivot) > Vector3.Distance(endB, lockPivot);
            k.Bow = aIsBow ? endA : endB;
            k.Tip = aIsBow ? endB : endA;
            k.Flat = axes[thinnest].normalized;
            k.Renderer = mf.GetComponent<Renderer>();
            return true;
        }

        /// Our key model takes the place of the game's key (hidden, but the object stays active:
        /// its active state is how we know the key is in the lock), with the ring and remote
        /// swinging from it. Without the model only the ring is hung at the game key's bow.
        static void BuildIgnition(KeyRing ring)
        {
            DestroyIgnition();
            GameKey k;
            bool measured = MeasureGameKey(out k);
            var model = Models.Get("key");

            ignition = new GameObject("cse_keys_in_ignition");
            // Under the turning "Keys" pivot rather than the key mesh: the mesh may be scaled
            // non-uniformly, which would skew a rotated child.
            ignition.transform.parent = keyMesh.parent ?? keyMesh;
            float parentScale = ignition.transform.parent.lossyScale.x;
            ignition.transform.localScale = Vector3.one / (Mathf.Abs(parentScale) > 1e-4f ? parentScale : 1f);
            ignitionHasFob = ring.Fob;

            bool useModel = model != null && measured;
            if (useModel)
            {
                // Model space: bow hole at the origin, blade running to -Z, flat side facing +Y.
                ignition.transform.rotation = Quaternion.LookRotation((k.Bow - k.Tip).normalized, k.Flat);
                ignition.transform.position = k.Tip - ignition.transform.rotation * new Vector3(0f, 0f, model.Bounds.min.z);
                if (k.Renderer != null)
                {
                    k.Renderer.enabled = false;
                    hiddenGameKey = k.Renderer;
                }
            }
            else
            {
                ignition.transform.position = measured ? k.Bow : keyMesh.position;
                ignition.transform.rotation = keyMesh.rotation;
            }
            BuildSwingingChain(ignition.transform, useModel, ring.Fob, Swing.Feel.Car);
        }

        static void DestroyIgnition()
        {
            if (hiddenGameKey != null) hiddenGameKey.enabled = true;
            hiddenGameKey = null;
            if (ignition != null) Object.Destroy(ignition);
            ignition = null;
        }

        // ---------------------------------------------------------------- frame

        public static void Tick(KeyRing inHand, KeyRing inIgnition)
        {
            // Rebuild if a remote was put on or taken off while showing.
            if (inHand != null && hand != null && handHasFob != inHand.Fob) ShowInHand(inHand);

            bool wantIgnition = inIgnition != null && keyMesh != null && keyMesh.gameObject.activeInHierarchy;
            if (wantIgnition && (ignition == null || ignitionHasFob != inIgnition.Fob)) BuildIgnition(inIgnition);
            if (!wantIgnition && ignition != null) DestroyIgnition();

            if (handFob != null) handFob.SetLed(Time.time < blinkUntil, inHand != null ? inHand.Battery : 0f);
        }
    }

    // Something hanging from a pivot - the keyring, the remote - simulated as a pendulum rather
    // than with a physics joint: the hand and the car move outside the physics step (camera
    // driven, interpolated), so a joint lags and jitters. Every rendered frame the pivot's real
    // acceleration is measured and the piece swings under gravity minus that acceleration.
    public class Swing : MonoBehaviour
    {
        const float MaxAccel = 60f;    // m/s^2, ignore anything wilder (teleports, first frame)
        const int Substeps = 4;

        /// How it feels. In the car the full acceleration swings it; in the hand the head bob and
        /// mouse look would make it twitch, and a real hand absorbs most of that anyway.
        public struct Feel
        {
            public float Response;  // share of the pivot's acceleration that reaches the piece
            public float Smoothing; // 0..1 per frame, lower = smoother acceleration
            public float Damping;   // 1/s, how fast a swing dies down

            public static readonly Feel Car = new Feel { Response = 1f, Smoothing = 0.5f, Damping = 3f };
            public static readonly Feel Hand = new Feel { Response = 0.3f, Smoothing = 0.12f, Damping = 6f };
        }

        /// Hangs below this one and is stepped right after it (the remote on the ring).
        public Swing Child;

        Feel feel;
        Transform pivot;
        Vector3 hole;                  // hanging point in the piece's own space
        float length;                  // pivot to centre of mass
        bool driven;                   // stepped by a parent Swing instead of LateUpdate
        Vector3 dir = Vector3.down;    // pivot -> piece, world space
        Vector3 omega;                 // rad/s, world space
        Vector3 lastPos, lastVel, accel;
        int frames;
        Material led;

        /// The piece (built by `build`, hanging from `hole` along its +Z) swinging below `pivot`.
        public static Swing Create(Transform pivot, Feel feel, Action<Transform> build, Vector3 hole, float length, string name)
        {
            var go = new GameObject(name);
            go.transform.parent = pivot;
            go.layer = pivot.gameObject.layer;
            build(go.transform);
            var s = go.AddComponent<Swing>();
            s.pivot = pivot;
            s.feel = feel;
            s.hole = hole;
            s.length = Mathf.Max(0.005f, length);
            s.lastPos = pivot.position;
            var ledT = go.transform.Find("led");
            s.led = ledT != null ? ledT.GetComponent<Renderer>().material : null;
            s.Place();
            return s;
        }

        void LateUpdate()
        {
            if (!driven) Step(Time.deltaTime);
        }

        void Step(float dt)
        {
            if (pivot == null || dt <= 0f) return;

            Vector3 p = pivot.position;
            Vector3 v = (p - lastPos) / dt;
            Vector3 a = (v - lastVel) / dt;
            lastPos = p;
            lastVel = v;
            if (++frames < 3 || a.sqrMagnitude > 25f * MaxAccel * MaxAccel) a = Vector3.zero;
            accel = Vector3.Lerp(accel, Vector3.ClampMagnitude(a, MaxAccel) * feel.Response, feel.Smoothing);

            // In the pivot's frame the piece feels gravity minus the pivot's acceleration.
            Vector3 g = Physics.gravity - accel;
            float h = dt / Substeps;
            for (int i = 0; i < Substeps; i++)
            {
                Vector3 alpha = Vector3.Cross(dir, g) / length - omega * feel.Damping;
                omega += alpha * h;
                omega -= Vector3.Project(omega, dir); // no spin about its own hanging axis
                float speed = omega.magnitude;
                if (speed > 1e-5f) dir = Quaternion.AngleAxis(speed * h * Mathf.Rad2Deg, omega / speed) * dir;
                dir.Normalize();
            }
            Place();

            if (Child != null)
            {
                Child.driven = true;
                Child.Step(dt);
            }
        }

        void Place()
        {
            if (pivot == null) return;
            Vector3 up = Vector3.ProjectOnPlane(pivot.forward, dir);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(pivot.up, dir);
            transform.rotation = Quaternion.LookRotation(dir, up);
            transform.position = pivot.position - transform.rotation * hole;
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
