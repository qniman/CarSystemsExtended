using CSE.Core;
using UnityEngine;

namespace CSE.Electrics
{
    // A visible wire between two points, sagging slightly. World-space so it follows parts
    // wherever they are; positions are refreshed every frame while visible.
    public class Wire
    {
        const int Segments = 10;

        readonly LineRenderer line;
        Transform a, b;

        public Wire(string name, Color color, float width = 0.006f)
        {
            var go = new GameObject("cse_wire_" + name);
            line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.SetWidth(width, width);
            line.SetVertexCount(Segments + 1);
            line.material = Shapes.Material(color);
            go.SetActive(false);
        }

        public void Connect(Transform from, Transform to)
        {
            a = from;
            b = to;
            line.gameObject.SetActive(a != null && b != null);
            Update();
        }

        public void Hide()
        {
            Connect(null, null);
        }

        public void Update()
        {
            if (a == null || b == null) return;
            Vector3 p0 = a.position, p2 = b.position;
            float sag = Mathf.Min(0.15f, Vector3.Distance(p0, p2) * 0.15f);
            Vector3 p1 = (p0 + p2) * 0.5f + Vector3.down * sag;
            for (int i = 0; i <= Segments; i++)
            {
                float t = (float)i / Segments;
                Vector3 p = (1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * p1 + t * t * p2;
                line.SetPosition(i, p);
            }
        }
    }
}
