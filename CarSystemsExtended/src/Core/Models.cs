using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MSCLoader;
using UnityEngine;

namespace CSE.Core
{
    // A part model made in Blender (tools/blender), embedded in the DLL as Assets/models/<name>.obj
    // + .mtl. The exporter already writes the mod's part space (Unity, left-handed winding), so
    // loading is a straight read. Named points come in as "#marker <name> x y z" lines.
    public class Model
    {
        public Mesh Mesh;
        public Material[] Materials;
        public Bounds Bounds;
        readonly Dictionary<string, Vector3> markers = new Dictionary<string, Vector3>();

        public bool HasMarker(string name)
        {
            return markers.ContainsKey(name);
        }

        public Vector3 Marker(string name, Vector3 fallback)
        {
            Vector3 v;
            return markers.TryGetValue(name, out v) ? v : fallback;
        }

        internal void SetMarker(string name, Vector3 v)
        {
            markers[name] = v;
        }

        /// The model as a child object (no collider).
        public GameObject Instantiate(Transform parent, string name = "model")
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.layer = parent.gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = Mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = Materials;
            return go;
        }
    }

    public static class Models
    {
        static readonly Dictionary<string, Model> cache = new Dictionary<string, Model>();
        static readonly HashSet<string> missing = new HashSet<string>();

        /// The embedded model, or null (callers fall back to placeholder shapes).
        public static Model Get(string name)
        {
            Model m;
            // Runtime meshes and materials can be unloaded with the scene (menu and back), so a
            // cached model whose assets are gone is parsed again.
            if (cache.TryGetValue(name, out m) && m.Mesh != null && Array.TrueForAll(m.Materials, x => x != null)) return m;
            if (missing.Contains(name)) return null;
            try
            {
                string obj = Read("models." + name + ".obj");
                if (obj == null)
                {
                    missing.Add(name);
                    return null;
                }
                m = Parse(name, obj, Read("models." + name + ".mtl"));
                cache[name] = m;
                return m;
            }
            catch (Exception e)
            {
                ModConsole.Error("[CSE] Model " + name + " failed to load: " + e.Message);
                missing.Add(name);
                return null;
            }
        }

        /// Builds the model under t, or the placeholder when there is no model. Returns the model.
        public static Model BuildOr(Transform t, string name, Action<Transform> placeholder)
        {
            var m = Get(name);
            if (m != null) m.Instantiate(t);
            else if (placeholder != null) placeholder(t);
            return m;
        }

        static string Read(string resource)
        {
            using (var s = typeof(Models).Assembly.GetManifestResourceStream("CSE." + resource))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s)) return r.ReadToEnd();
            }
        }

        static float F(string s)
        {
            return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static Model Parse(string name, string obj, string mtl)
        {
            var mats = ParseMtl(mtl);
            var pos = new List<Vector3>();
            var nrm = new List<Vector3>();
            var tex = new List<Vector2>();
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var subs = new List<KeyValuePair<string, List<int>>>();
            List<int> current = null;
            var corners = new Dictionary<long, int>();
            int triangles = 0;
            var model = new Model();

            foreach (var raw in obj.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                switch (p[0])
                {
                    case "#marker":
                        model.SetMarker(p[1], new Vector3(F(p[2]), F(p[3]), F(p[4])));
                        break;
                    case "v":
                        pos.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
                        break;
                    case "vn":
                        nrm.Add(new Vector3(F(p[1]), F(p[2]), F(p[3])));
                        break;
                    case "vt":
                        tex.Add(new Vector2(F(p[1]), F(p[2])));
                        break;
                    case "usemtl":
                        current = new List<int>();
                        subs.Add(new KeyValuePair<string, List<int>>(p[1], current));
                        break;
                    case "f":
                        if (current == null)
                        {
                            current = new List<int>();
                            subs.Add(new KeyValuePair<string, List<int>>("default", current));
                        }
                        for (int k = 1; k <= 3; k++)
                        {
                            var idx = p[k].Split('/');
                            int vi = int.Parse(idx[0], CultureInfo.InvariantCulture) - 1;
                            int ti = idx.Length > 1 && idx[1].Length > 0 ? int.Parse(idx[1], CultureInfo.InvariantCulture) - 1 : -1;
                            int ni = idx.Length > 2 && idx[2].Length > 0 ? int.Parse(idx[2], CultureInfo.InvariantCulture) - 1 : -1;
                            // one Unity vertex per distinct position + uv + normal (21 bits each)
                            long key = ((long)vi << 42) | ((long)(ti + 1) << 21) | (long)(ni + 1);
                            int unity;
                            if (!corners.TryGetValue(key, out unity))
                            {
                                unity = verts.Count;
                                corners[key] = unity;
                                verts.Add(pos[vi]);
                                normals.Add(ni >= 0 ? nrm[ni] : Vector3.up);
                                uvs.Add(ti >= 0 ? tex[ti] : Vector2.zero);
                            }
                            current.Add(unity);
                        }
                        triangles++;
                        break;
                }
            }

            var mesh = new Mesh { name = "cse_" + name };
            mesh.vertices = verts.ToArray();
            mesh.normals = normals.ToArray();
            if (tex.Count > 0) mesh.uv = uvs.ToArray();
            mesh.subMeshCount = subs.Count;
            var materials = new Material[subs.Count];
            for (int i = 0; i < subs.Count; i++)
            {
                mesh.SetTriangles(subs[i].Value.ToArray(), i);
                Material m;
                materials[i] = mats.TryGetValue(subs[i].Key, out m) ? m : Shapes.Material(Color.grey);
            }
            mesh.RecalculateBounds();
            model.Mesh = mesh;
            model.Materials = materials;
            model.Bounds = mesh.bounds;
            ModConsole.Log("[CSE] Model " + name + ": " + triangles + " triangles, " + verts.Count + " vertices, " + subs.Count + " materials");
            return model;
        }

        static float ToGamma(float linear)
        {
            return Mathf.Pow(Mathf.Clamp01(linear), 1f / 2.2f);
        }

        static Shader specular;

        /// Kd colour, or a baked texture (map_Kd, a PNG embedded next to the model). Shiny or
        /// metallic materials get the legacy specular shader; on a textured one the texture's
        /// alpha is the gloss mask (metal shiny, plastic dull), as that shader reads it.
        static Dictionary<string, Material> ParseMtl(string mtl)
        {
            var result = new Dictionary<string, Material>();
            if (mtl == null) return result;
            string name = null;
            Color kd = Color.grey;
            float ns = 0f, pm = 0f;
            string map = null;

            Action flush = () =>
            {
                if (name == null) return;
                var m = Shapes.Material(kd);
                var texture = map != null ? Textures.Embedded("models." + map) : null;
                if (texture != null)
                {
                    m.color = Color.white;
                    m.mainTexture = texture;
                    if (specular == null) specular = Shader.Find("Specular") ?? Shader.Find("Legacy Shaders/Specular");
                    if (specular != null)
                    {
                        m.shader = specular;
                        m.mainTexture = texture;
                        m.SetColor("_SpecColor", new Color(0.55f, 0.55f, 0.55f));
                        m.SetFloat("_Shininess", Mathf.Lerp(0.5f, 0.05f, Mathf.Clamp01(ns / 100f)));
                    }
                }
                else if (pm > 0.5f || ns > 60f)
                {
                    if (specular == null) specular = Shader.Find("Specular") ?? Shader.Find("Legacy Shaders/Specular");
                    if (specular != null)
                    {
                        m.shader = specular;
                        m.color = kd;
                        m.SetColor("_SpecColor", pm > 0.5f ? Color.Lerp(kd, Color.white, 0.4f) : new Color(0.3f, 0.3f, 0.3f));
                        m.SetFloat("_Shininess", Mathf.Lerp(0.5f, 0.05f, Mathf.Clamp01(ns / 100f)));
                    }
                }
                m.name = "cse_" + name;
                result[name] = m;
            };

            foreach (var raw in mtl.Split('\n'))
            {
                var p = raw.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                switch (p[0])
                {
                    case "newmtl":
                        flush();
                        name = p[1];
                        kd = Color.grey;
                        ns = pm = 0f;
                        map = null;
                        break;
                    case "Kd":
                        // Blender stores linear colours; the game renders in gamma space.
                        kd = new Color(ToGamma(F(p[1])), ToGamma(F(p[2])), ToGamma(F(p[3])));
                        break;
                    case "Ns":
                        ns = F(p[1]);
                        break;
                    case "Pm":
                        pm = F(p[1]);
                        break;
                    case "map_Kd":
                        map = p[p.Length - 1];
                        break;
                }
            }
            flush();
            return result;
        }
    }
}
