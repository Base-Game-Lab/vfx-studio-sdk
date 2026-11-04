using System;
using System.Collections.Generic;
using UnityEngine;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>The four adjustments on an effect's import settings. Defaults change nothing at all.</summary>
    [Serializable]
    internal struct VfxKnobs
    {
        public float speed, size, intensity;
        public Color tint;

        public static VfxKnobs Default => new VfxKnobs { speed = 1, size = 1, intensity = 1, tint = Color.white };

        public bool IsDefault => speed == 1 && size == 1 && intensity == 1 && TintIsWhite;
        public bool TintIsWhite => tint.r == 1 && tint.g == 1 && tint.b == 1;

        public string Describe()
        {
            var parts = new List<string>();
            if (speed != 1) parts.Add($"speed {speed:0.###}x");
            if (size != 1) parts.Add($"size {size:0.###}x");
            if (!TintIsWhite) parts.Add($"tint #{ColorUtility.ToHtmlStringRGB(tint)}");
            if (intensity != 1) parts.Add($"intensity {intensity:0.###}x");
            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// Speed and tint rewrite the parsed file before anything is built, so every part that reads a time or a colour sees the
    /// adjusted one and nothing downstream needs to know. Size and intensity live in the builder: size in its px-to-units
    /// scale, intensity in each material's alpha.
    /// </summary>
    internal static class VfxAdjust
    {
        /// <summary>Plays the effect s times faster: every time divides by s, every rate and velocity multiplies by it, every
        /// acceleration by s squared, so each particle takes the same path in less time.</summary>
        public static void Speed(J doc, float s)
        {
            if (s == 1 || s <= 0) return;
            var root = (Dictionary<string, object>)doc.Raw;
            var curves = Dict(root, "curves");
            var effect = Dict(root, "effect");
            if (effect != null && effect.TryGetValue("duration", out var d)) effect["duration"] = (double)d / s;

            // Which curves run on effect time (tracks), which are emission rates (per second, on effect time), and which run on
            // particle life (0..1, untouched). One id used two ways is split, so each use is scaled once and only its own way.
            var uses = new Dictionary<string, HashSet<string>>();
            var refs = new List<(Dictionary<string, object> holder, string key, string kind)>();
            void Use(Dictionary<string, object> holder, string key, string kind)
            {
                if (holder == null || !(holder.TryGetValue(key, out var v) && v is string id)) return;
                if (!uses.TryGetValue(id, out var set)) uses[id] = set = new HashSet<string>();
                set.Add(kind);
                refs.Add((holder, key, kind));
            }

            foreach (var layer in List(root, "layers"))
            {
                if (!(layer is Dictionary<string, object> l)) continue;
                var tracks = Dict(l, "tracks");
                if (tracks != null)
                    foreach (var k in new List<string>(tracks.Keys)) Use(tracks, k, "time");
                var emit = Dict(l, "emit");
                Use(emit, "rate", "rate");
                foreach (var b in List(emit, "bursts"))
                    if (b is Dictionary<string, object> burst && burst.TryGetValue("t", out var bt)) burst["t"] = (double)bt / s;
                var over = Dict(l, "over");
                Use(over, "size", "life");
                var start = Dict(l, "start");
                Scale(start, "life", 1 / s);
                Scale(start, "speed", s);
                Scale(start, "spin", s);
                Scale(start, "flutter", s);
                var vel = Dict(start, "vel");
                Scale(vel, "x", s);
                Scale(vel, "y", s);
                var sim = Dict(l, "sim");
                Scale(sim, "drag", s);
                Scale(sim, "gravity", s * s);
                Scale(Dict(over, "noise"), "strength", s);
                // Stretch length is velocity times this, and velocity has just grown by s.
                Scale(Dict(over, "stretch"), "speed", 1 / s);
            }

            foreach (var r in refs)
            {
                if (r.kind == "life") continue;
                var id = (string)r.holder[r.key];
                if (!curves.TryGetValue(id, out var c) || !(c is Dictionary<string, object> curve)) continue;
                var own = id;
                if (uses[id].Count > 1)
                {
                    own = $"{id}~{r.kind}";
                    if (!curves.ContainsKey(own)) curves[own] = Copy(curve);
                    r.holder[r.key] = own;
                }
            }
            var done = new HashSet<string>();
            foreach (var r in refs)
            {
                if (r.kind == "life") continue;
                var id = (string)r.holder[r.key];
                if (!done.Add(id) || !(curves.TryGetValue(id, out var c) && c is Dictionary<string, object> curve)) continue;
                bool rate = r.kind == "rate";
                Scale(curve, "t", 1 / s);
                // Slopes are per second of effect time; a rate's values are per second as well.
                Scale(curve, "in", rate ? s * s : s);
                Scale(curve, "out", rate ? s * s : s);
                if (rate) Scale(curve, "v", s);
            }

            foreach (var item in List(root, "dropped"))
                if (item is Dictionary<string, object> dropped) Scale(dropped, "t", 1 / s);
        }

        /// <summary>Multiplies every colour in the file by the tint, in light, the way a tint multiplies in a Linear project. Black stays black.</summary>
        public static void Tint(J doc, Color tint)
        {
            if (tint.r == 1 && tint.g == 1 && tint.b == 1) return;
            var root = (Dictionary<string, object>)doc.Raw;
            var gradients = Dict(root, "gradients");
            if (gradients != null)
                foreach (var g in gradients.Values)
                    if (g is Dictionary<string, object> gradient && gradient.TryGetValue("c", out var c) && c is List<object> colours) TintAll(colours, tint);
            foreach (var layer in List(root, "layers"))
            {
                if (!(layer is Dictionary<string, object> l)) continue;
                if (l.TryGetValue("color", out var col) && col is string hex) l["color"] = TintHex(hex, tint);
                if (Dict(l, "start") is Dictionary<string, object> start && start.TryGetValue("colors", out var cs) && cs is List<object> list) TintAll(list, tint);
            }
        }

        static void TintAll(List<object> colours, Color tint)
        {
            for (int i = 0; i < colours.Count; i++)
                if (colours[i] is string hex) colours[i] = TintHex(hex, tint);
        }

        internal static string TintHex(string hex, Color tint)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out var c)) return hex;
            float Mul(float a, float b) => Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(a) * Mathf.GammaToLinearSpace(Mathf.Clamp01(b)));
            var t = new Color(Mul(c.r, tint.r), Mul(c.g, tint.g), Mul(c.b, tint.b), c.a);
            bool alpha = hex.Length == 9;
            return "#" + (alpha ? ColorUtility.ToHtmlStringRGBA(t) : ColorUtility.ToHtmlStringRGB(t)).ToLowerInvariant();
        }

        // ---- raw JSON helpers ----

        static Dictionary<string, object> Dict(Dictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        static IEnumerable<object> List(Dictionary<string, object> d, string key)
        {
            if (d != null && d.TryGetValue(key, out var v) && v is List<object> l) return l;
            return Array.Empty<object>();
        }

        /// <summary>Multiplies a number, or every number in a list, held at key.</summary>
        static void Scale(Dictionary<string, object> d, string key, float by)
        {
            if (d == null || !d.TryGetValue(key, out var v)) return;
            if (v is double x) d[key] = x * by;
            else if (v is List<object> l)
                for (int i = 0; i < l.Count; i++)
                    if (l[i] is double y) l[i] = y * by;
        }

        static Dictionary<string, object> Copy(Dictionary<string, object> d)
        {
            var c = new Dictionary<string, object>();
            foreach (var kv in d) c[kv.Key] = kv.Value is List<object> l ? new List<object>(l) : kv.Value;
            return c;
        }
    }
}
