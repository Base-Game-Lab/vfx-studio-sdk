using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>AllIn1Vfx materials set up the way a VFX artist would: additive, grey mask, colour ramp. References the shader, never ships it.</summary>
    internal static class VfxMaterials
    {
        public const string DefaultShader = "AllIn1Vfx/AllIn1VfxSRPBatch";
        public const string Fallback = "Sprites/Default";

        public static Material Create(string name, Shader shader, string blend, Texture2D mask, int queue)
        {
            var m = new Material(shader) { name = name };
            bool additive = blend == "add";
            if (additive) m.EnableKeyword("ADDITIVECONFIG_ON");
            Set(m, "_RenderingMode", additive ? 1 : 0);
            Set(m, "_SrcMode", additive ? 1 : 5);
            Set(m, "_DstMode", additive ? 1 : 10);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", mask);
            m.renderQueue = queue;
            return m;
        }

        public static void AddRamp(Material m, Texture2D ramp)
        {
            m.EnableKeyword("COLORRAMP_ON");
            m.EnableKeyword("COLORRAMPGRAD_ON");
            Set(m, "_ColorRampBlend", 1);
            if (m.HasProperty("_ColorRampTexGradient")) m.SetTexture("_ColorRampTexGradient", ramp);
        }

        public static void AddClip(Material m, Texture2D clip)
        {
            m.EnableKeyword("MASK_ON");
            Set(m, "_MaskPow", 1);
            if (m.HasProperty("_MaskTex")) m.SetTexture("_MaskTex", clip);
        }

        /// <summary>Glow is a gain on the mask: the shape colour's alpha scales it before the ramp reads it.</summary>
        public static void AddGlow(Material m, float glow)
        {
            if (m.HasProperty("_ShapeColor")) m.SetColor("_ShapeColor", new Color(1, 1, 1, 1 + glow));
        }

        static void Set(Material m, string prop, float v)
        {
            if (m.HasProperty(prop)) m.SetFloat(prop, v);
        }

        /// <summary>A 64x1 ramp named the way AllIn1Vfx's own gradient drawer names one, so it stays editable in an exported copy.</summary>
        public static Texture2D Ramp(string id, Gradient g) => Ramp(g, (u, c) => c);

        /// <summary>An additive layer's ramp: the lift of the file's colour at each strength. The shader reads it at alpha x mask, while the
        /// file's colour goes by mask alone, so each strength holds the colour of every moment it can occur at, weighted by how bright the
        /// layer is then. The brightest strengths only occur at the peak, so they are exact there.</summary>
        public static Texture2D LightRamp(Gradient g, VfxLight light, float[] alphas)
        {
            if (light.Straight) return Ramp(g, (u, c) => c);
            Color ColourAt(float u)
            {
                var sum = Color.clear;
                float weight = 0;
                foreach (var a in alphas)
                {
                    if (a < u || a <= 0) continue;
                    sum += g.Evaluate(u / a) * a;
                    weight += a;
                }
                return weight > 0 ? sum / weight : g.Evaluate(1);
            }
            return Ramp(g, (u, c) => light.RampAt(ColourAt(u), u, c.a));
        }

        static Texture2D Ramp(Gradient g, Func<float, Color, Color> at)
        {
            const int w = 256;
            var t = new Texture2D(w, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[w];
            for (int i = 0; i < w; i++)
            {
                float u = i / (w - 1f);
                px[i] = at(u, g.Evaluate(u));
            }
            t.SetPixels(px);
            t.Apply(false, false);
            t.name = "z_ColorRampTexGradientTex" + GradientJson(g);
            return t;
        }

        static string GradientJson(Gradient g)
        {
            string N(float f) => f.ToString("R", CultureInfo.InvariantCulture);
            var b = new StringBuilder("{\"mode\":0,\"colorKeys\":[");
            for (int i = 0; i < g.colorKeys.Length; i++)
            {
                var k = g.colorKeys[i];
                if (i > 0) b.Append(',');
                b.Append($"{{\"color\":{{\"r\":{N(k.color.r)},\"g\":{N(k.color.g)},\"b\":{N(k.color.b)},\"a\":1.0}},\"time\":{N(k.time)}}}");
            }
            b.Append("],\"alphaKeys\":[");
            for (int i = 0; i < g.alphaKeys.Length; i++)
            {
                if (i > 0) b.Append(',');
                b.Append($"{{\"alpha\":{N(g.alphaKeys[i].alpha)},\"time\":{N(g.alphaKeys[i].time)}}}");
            }
            b.Append("]}");
            return b.ToString();
        }
    }

    /// <summary>Plate colours and curves into Unity's types.</summary>
    internal static class VfxConvert
    {
        public static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
        }

        public static Gradient Flat(Color c)
        {
            var r = new Gradient();
            r.SetKeys(new[] { new GradientColorKey(c, 0), new GradientColorKey(c, 1) }, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return r;
        }

        /// <summary>The file's fade, eight evenly spaced keys of which is Unity's limit, carried in colour as well as alpha.</summary>
        public static Gradient LightFade(J g, VfxLight light)
        {
            var src = ToGradient(g);
            var colours = new GradientColorKey[8];
            var alphas = new GradientAlphaKey[8];
            for (int i = 0; i < 8; i++)
            {
                float k = i / 7f, a = src.Evaluate(k).a, v = light.VertexChannel(a);
                colours[i] = new GradientColorKey(new Color(v, v, v), k);
                alphas[i] = new GradientAlphaKey(a, k);
            }
            var r = new Gradient();
            r.SetKeys(colours, alphas);
            return r;
        }

        public static Gradient ToGradient(J g)
        {
            var colors = new List<GradientColorKey>();
            var alphas = new List<GradientAlphaKey>();
            var t = g["t"];
            var c = g["c"];
            for (int i = 0; i < t.Count; i++)
            {
                var col = Hex(c[i].S("#ffffff"));
                colors.Add(new GradientColorKey(col, t[i].F()));
                alphas.Add(new GradientAlphaKey(col.a, t[i].F()));
            }
            var r = new Gradient();
            r.SetKeys(colors.ToArray(), alphas.ToArray());
            return r;
        }

        /// <summary>A format curve as an AnimationCurve. Slopes come from the file, or make it linear when it has none.</summary>
        public static AnimationCurve ToCurve(J c, Func<float, float> value = null, float slopeScale = 1, float extendTo = -1)
        {
            var t = c["t"].Floats();
            var v = c["v"].Floats();
            var keys = new List<Keyframe>(t.Length + 1);
            bool hermite = c["in"].Exists;
            for (int i = 0; i < t.Length; i++)
            {
                float vi = value != null ? value(v[i]) : v[i];
                float a, b;
                if (hermite)
                {
                    a = c["in"][i].F() * slopeScale;
                    b = c["out"][i].F() * slopeScale;
                }
                else
                {
                    float Slope(int p, int q) => (Vi(v, value, q) - Vi(v, value, p)) / (t[q] - t[p]);
                    a = i > 0 ? Slope(i - 1, i) : 0;
                    b = i < t.Length - 1 ? Slope(i, i + 1) : 0;
                }
                keys.Add(new Keyframe(t[i], vi, a, b));
            }
            if (extendTo > t[t.Length - 1])
            {
                var last = keys[keys.Count - 1];
                last.outTangent = 0;
                keys[keys.Count - 1] = last;
                keys.Add(new Keyframe(extendTo, last.value, 0, 0));
            }
            return new AnimationCurve(keys.ToArray());
        }

        static float Vi(float[] v, Func<float, float> f, int i) => f != null ? f(v[i]) : v[i];
    }
}
