using CSE.Core;
using CSE.Parts;
using MSCLoader;
using UnityEngine;

namespace CSE.Systems
{
    // What the keyring looks like in the player's hand and in the ignition, with the remote
    // swinging from the ring (see Swing).
    public static class KeyRingView
    {
        // Keys held in the lower right of the view, blade forward, ring towards the player.
        static readonly Vector3 HandPos = new Vector3(0.14f, -0.12f, 0.32f);
        static readonly Vector3 HandEuler = new Vector3(8f, 170f, 15f);

        static GameObject hand;
        static bool handHasFob;
        static Swing handSwing;

        static GameObject ignition;
        static Swing ignitionSwing;
        static Transform keyMesh;

        static float blinkUntil;

        // ---- HUD mode icon (top right), the same indicator the game uses for its tools
        static Material modeIcon;
        static Texture savedIcon;
        static Texture2D keysIcon;

        public static void Init()
        {
            keyMesh = GameRefs.FindInCar("Dashboard/Steering/steering_column2/Ignition/Keys/Key");
            hand = ignition = null;
            handSwing = ignitionSwing = null;
            var mode = GameObject.Find("GUI/Indicators/Mode");
            var r = mode != null ? mode.GetComponent<Renderer>() : null;
            modeIcon = r != null ? r.sharedMaterial : null;
            savedIcon = null;
            if (modeIcon == null) ModConsole.Warning("[CSE] HUD mode indicator not found, no keys icon");
        }

        // ---------------------------------------------------------------- building

        /// Key, bow and ring. Ring centre at the local origin, key along -Z.
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
            BuildKey(hand.transform);
            handHasFob = ring.Fob;
            if (ring.Fob) handSwing = Swing.Create(hand.transform, Swing.Feel.Hand);
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
            handSwing = null;
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

        /// The bow end of the game's key: the end of its mesh farthest from the lock cylinder
        /// (the parent "Keys" pivot the key turns around).
        static bool FindBow(out Vector3 localPoint)
        {
            localPoint = Vector3.zero;
            var mf = keyMesh != null ? keyMesh.GetComponentInChildren<MeshFilter>() : null;
            if (mf == null || mf.sharedMesh == null) return false;
            var b = mf.sharedMesh.bounds;
            var meshT = mf.transform;
            Vector3 e = b.extents;
            Vector3 axis = e.x >= e.y && e.x >= e.z ? Vector3.right * e.x : e.y >= e.z ? Vector3.up * e.y : Vector3.forward * e.z;
            Vector3 endA = meshT.TransformPoint(b.center + axis);
            Vector3 endB = meshT.TransformPoint(b.center - axis);
            Vector3 lockPivot = keyMesh.parent != null ? keyMesh.parent.position : keyMesh.position;
            Vector3 bow = Vector3.Distance(endA, lockPivot) > Vector3.Distance(endB, lockPivot) ? endA : endB;
            localPoint = keyMesh.InverseTransformPoint(bow);
            return true;
        }

        static void BuildIgnition()
        {
            DestroyIgnition();
            Vector3 bow;
            if (!FindBow(out bow)) bow = Vector3.zero;
            ignition = new GameObject("cse_keyring_in_ignition");
            ignition.transform.parent = keyMesh;
            ignition.transform.localPosition = bow;
            ignition.transform.localRotation = Quaternion.identity;
            // a ring standing in the plane of the key bow
            Shapes.Add(ignition.transform, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.024f, 0.0015f, 0.024f), Shapes.Steel, new Vector3(0, 0, 90), "ring");
        }

        static void DestroyIgnition()
        {
            if (ignition != null) Object.Destroy(ignition);
            ignition = null;
            ignitionSwing = null;
        }

        // ---------------------------------------------------------------- frame

        public static void Tick(KeyRing inHand, KeyRing inIgnition)
        {
            // Rebuild if a remote was put on or taken off while showing.
            if (inHand != null && hand != null && handHasFob != inHand.Fob) ShowInHand(inHand);

            bool wantIgnition = inIgnition != null && keyMesh != null && keyMesh.gameObject.activeInHierarchy;
            if (wantIgnition && ignition == null) BuildIgnition();
            if (!wantIgnition && ignition != null) DestroyIgnition();
            if (ignition != null && inIgnition.Fob != (ignitionSwing != null))
            {
                if (ignitionSwing != null) Object.Destroy(ignitionSwing.gameObject);
                ignitionSwing = inIgnition.Fob ? Swing.Create(ignition.transform, Swing.Feel.Car) : null;
            }

            if (handSwing != null) handSwing.SetLed(Time.time < blinkUntil, inHand != null ? inHand.Battery : 0f);
        }
    }

    // The remote hanging from the keyring as a pendulum, simulated here rather than with a
    // physics joint: the hand and the car move outside the physics step (interpolated, camera
    // driven), so a joint lags and jitters. Every rendered frame the pivot's real acceleration
    // is measured and the remote swings under gravity minus that acceleration, with damping.
    public class Swing : MonoBehaviour
    {
        // Keyring hole of the remote, in remote space (back end, see PartCatalog.Fob).
        static readonly Vector3 Hole = new Vector3(0f, 0f, -0.03f);
        const float Length = 0.032f;   // pivot to the remote's centre of mass
        const float MaxAccel = 60f;    // m/s^2, ignore anything wilder (teleports, first frame)
        const int Substeps = 4;

        /// How it feels. In the car the full acceleration swings it; in the hand the head bob and
        /// mouse look would make it twitch, and a real hand absorbs most of that anyway.
        public struct Feel
        {
            public float Response;  // share of the pivot's acceleration that reaches the remote
            public float Smoothing; // 0..1 per frame, lower = smoother acceleration
            public float Damping;   // 1/s, how fast a swing dies down

            public static readonly Feel Car = new Feel { Response = 1f, Smoothing = 0.5f, Damping = 3f };
            public static readonly Feel Hand = new Feel { Response = 0.3f, Smoothing = 0.12f, Damping = 6f };
        }

        Feel feel;
        Transform pivot;
        Vector3 dir = Vector3.down;    // pivot -> remote, world space
        Vector3 omega;                 // rad/s, world space
        Vector3 lastPos, lastVel, accel;
        int frames;
        Material led;

        public static Swing Create(Transform pivot, Feel feel)
        {
            var go = new GameObject("cse_fob_swinging");
            go.transform.parent = pivot;
            go.layer = pivot.gameObject.layer;
            PartCatalog.Fob.Build(go.transform);
            var s = go.AddComponent<Swing>();
            s.pivot = pivot;
            s.feel = feel;
            var ledT = go.transform.Find("led");
            s.led = ledT != null ? ledT.GetComponent<Renderer>().material : null;
            s.Place();
            return s;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (pivot == null || dt <= 0f) return;

            Vector3 p = pivot.position;
            Vector3 v = (p - lastPos) / dt;
            Vector3 a = (v - lastVel) / dt;
            lastPos = p;
            lastVel = v;
            if (++frames < 3 || a.sqrMagnitude > 25f * MaxAccel * MaxAccel) a = Vector3.zero;
            accel = Vector3.Lerp(accel, Vector3.ClampMagnitude(a, MaxAccel) * feel.Response, feel.Smoothing);

            // In the pivot's frame the remote feels gravity minus the pivot's acceleration.
            Vector3 g = Physics.gravity - accel;
            float h = dt / Substeps;
            for (int i = 0; i < Substeps; i++)
            {
                Vector3 alpha = Vector3.Cross(dir, g) / Length - omega * feel.Damping;
                omega += alpha * h;
                omega -= Vector3.Project(omega, dir); // no spin about its own hanging axis
                float speed = omega.magnitude;
                if (speed > 1e-5f) dir = Quaternion.AngleAxis(speed * h * Mathf.Rad2Deg, omega / speed) * dir;
                dir.Normalize();
            }
            Place();
        }

        void Place()
        {
            if (pivot == null) return;
            Vector3 up = Vector3.ProjectOnPlane(pivot.forward, dir);
            if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(pivot.up, dir);
            transform.rotation = Quaternion.LookRotation(dir, up);
            transform.position = pivot.position - transform.rotation * Hole;
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
