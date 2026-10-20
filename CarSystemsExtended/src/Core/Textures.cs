using System.Collections.Generic;
using MSCLoader;
using UnityEngine;

namespace CSE.Core
{
    // Textures embedded in the DLL (Assets/*, see the csproj), so the mod stays a single file.
    public static class Textures
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        /// Loads a PNG/JPG embedded as "CSE.&lt;file&gt;". Null if missing or unreadable.
        public static Texture2D Embedded(string file)
        {
            Texture2D tex;
            if (cache.TryGetValue(file, out tex) && tex != null) return tex;

            using (var s = typeof(Textures).Assembly.GetManifestResourceStream("CSE." + file))
            {
                if (s == null)
                {
                    ModConsole.Warning("[CSE] Missing embedded texture " + file);
                    return null;
                }
                var bytes = new byte[s.Length];
                s.Read(bytes, 0, bytes.Length);
                tex = new Texture2D(2, 2, TextureFormat.ARGB32, true); // PNG alpha = gloss mask
                if (!tex.LoadImage(bytes))
                {
                    ModConsole.Warning("[CSE] Could not decode texture " + file);
                    return null;
                }
            }
            tex.name = "cse_" + file;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4; // read at a slant on a table
            cache[file] = tex;
            return tex;
        }
    }
}
