using MSCLoader;
using UnityEngine;

namespace CSE.Core
{
    // Placeholder geometry built from Unity primitives. Will be replaced by real models later.
    public static class Shapes
    {
        static Shader shader;

        static Shader Shader
        {
            get
            {
                if (shader != null) return shader;
                shader = Shader.Find("Diffuse") ?? Shader.Find("Legacy Shaders/Diffuse");
                if (shader == null)
                {
                    // Fall back to whatever the car body uses.
                    var r = GameRefs.Car != null ? GameRefs.Car.GetComponentInChildren<MeshRenderer>() : null;
                    if (r != null) shader = r.sharedMaterial.shader;
                }
                ModConsole.Log("[CSE] Shapes shader: " + (shader != null ? shader.name : "<none>"));
                return shader;
            }
        }

        public static Material Material(Color color)
        {
            var m = new Material(Shader);
            m.color = color;
            return m;
        }

        /// Adds a visual-only primitive child (its collider is removed).
        public static GameObject Add(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 size, Color color,
            Vector3 euler = default(Vector3), string name = "mesh")
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.parent = parent;
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().material = Material(color);
            return go;
        }

        /// A flat picture facing the part's +Y, with the image's top towards +Z
        /// (a cover lying on a table). Falls back to a plain coloured quad without the texture.
        public static GameObject AddPicture(Transform parent, Vector3 localPos, Vector2 size, Texture2D texture, Color fallback)
        {
            var go = Add(parent, PrimitiveType.Quad, localPos, new Vector3(size.x, size.y, 1f), fallback,
                new Vector3(90, 0, 0), "picture");
            if (texture != null)
            {
                var m = go.GetComponent<Renderer>().material;
                m.color = Color.white;
                m.mainTexture = texture;
            }
            return go;
        }

        static Shader glowShader;
        static bool glowLooked;

        /// A self-lit LED lens named "led" (the alarm code looks it up by name). Uses the
        /// legacy self-illumin shader so it glows in the dark; plain diffuse if missing.
        public static GameObject AddLed(Transform parent, Vector3 localPos, float size)
        {
            var go = Add(parent, PrimitiveType.Sphere, localPos, Vector3.one * size, LedOff, default(Vector3), "led");
            if (!glowLooked)
            {
                glowLooked = true;
                glowShader = Shader.Find("Self-Illumin/Diffuse") ?? Shader.Find("Legacy Shaders/Self-Illumin/Diffuse");
                ModConsole.Log("[CSE] LED shader: " + (glowShader != null ? glowShader.name : "<none, diffuse>"));
            }
            if (glowShader != null)
            {
                var r = go.GetComponent<Renderer>();
                r.material.shader = glowShader;
                r.material.color = LedOff;
            }
            return go;
        }

        public static readonly Color Black = new Color(0.08f, 0.08f, 0.08f);
        public static readonly Color DarkGrey = new Color(0.22f, 0.22f, 0.22f);
        public static readonly Color Steel = new Color(0.6f, 0.6f, 0.62f);
        public static readonly Color LedOff = new Color(0.15f, 0.05f, 0.05f);
        public static readonly Color LedRed = new Color(1f, 0.1f, 0.05f);
        public static readonly Color LedGreen = new Color(0.1f, 1f, 0.2f);
    }
}
