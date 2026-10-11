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

        public static readonly Color Black = new Color(0.08f, 0.08f, 0.08f);
        public static readonly Color DarkGrey = new Color(0.22f, 0.22f, 0.22f);
        public static readonly Color Steel = new Color(0.6f, 0.6f, 0.62f);
        public static readonly Color LedOff = new Color(0.15f, 0.05f, 0.05f);
        public static readonly Color LedRed = new Color(1f, 0.1f, 0.05f);
        public static readonly Color LedGreen = new Color(0.1f, 1f, 0.2f);
    }
}
