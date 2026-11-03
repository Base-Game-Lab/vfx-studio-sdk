using System;
using UnityEditor;
using UnityEngine;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>Grey masks from the file as textures shaped the way AllIn1Vfx reads them.</summary>
    internal static class VfxMasks
    {
        public const string FillName = "mask_fill";

        /// <summary>White, with brightness as alpha: reads right for additive, alpha blend and the colour ramp alike. Readable, uncompressed. gain is material.glow where the shader cannot clamp it.</summary>
        public static Texture2D Decode(string id, J m, float gain = 1)
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!ImageConversion.LoadImage(src, Convert.FromBase64String(m["data"].S("")), false))
                throw new FormatException($"The mask \"{id}\" is not a readable PNG.");
            var px = src.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                float grey = (0.3f * px[i].r + 0.59f * px[i].g + 0.11f * px[i].b) * (px[i].a / 255f);
                px[i] = new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt(grey * gain), 0, 255));
            }
            var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true, true)
            {
                name = "mask_" + id,
                wrapMode = m["wrap"].S("clamp") == "repeat" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            t.SetPixels32(px);
            t.Apply(true, false);
            UnityEngine.Object.DestroyImmediate(src);
            return t;
        }

        /// <summary>Phones get ASTC, the way an artist's texture would ship. The Editor's own target stays uncompressed.</summary>
        public static void CompressFor(Texture2D t, BuildTarget target)
        {
            if (target == BuildTarget.iOS || target == BuildTarget.Android)
                EditorUtility.CompressTexture(t, TextureFormat.ASTC_6x6, TextureCompressionQuality.Normal);
        }
    }
}
