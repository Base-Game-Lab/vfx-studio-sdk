using UnityEditor;
using UnityEngine;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>
    /// A file with game.light "linear" (format 0.3) was composited in linear light, as URP does: its colours and alphas are used as
    /// they are, and <see cref="Straight"/> is true.
    /// Older files were composited in display space, and everything below is for them. A Linear project blends in light, where soft alpha reads far stronger for additive
    /// layers and far weaker for black ones. What a display-space add does to a pixel is known exactly once the colour under it
    /// is: it lifts light by lin(backdrop + s) - lin(backdrop). So an additive layer's ramp holds that lift, and a black layer's alpha
    /// is the share of light left after it. A Gamma project blends like the studio and takes the numbers as they are. Sprites cannot
    /// see what is under each pixel, so where additive layers overlap the sum is a little short: rays over a glow, about a third.
    /// </summary>
    internal sealed class VfxLight
    {
        readonly bool linear;
        readonly Color backdrop;

        /// <summary>The file holds what Unity should draw: nothing here changes a number.</summary>
        public bool Straight { get; }

        public VfxLight(VfxNativeSettings settings, J doc)
        {
            linear = PlayerSettings.colorSpace == ColorSpace.Linear;
            backdrop = settings.backdrop;
            Straight = doc["game"]["light"].S() == "linear";
        }

        static float Lin(float c) => Mathf.GammaToLinearSpace(Mathf.Clamp01(c));

        float Lift(float under, float s) => linear ? Lin(under + s) - Lin(under) : s;

        /// <summary>What a ramp holds at u, so that ramp x u lands as colour x u added on screen over the backdrop.</summary>
        public Color RampAt(Color colour, float u, float alpha)
        {
            u = Mathf.Max(u, 1 / 512f);
            float Held(float b, float c)
            {
                float v = Lift(b, c * u) / u;
                return linear ? Mathf.LinearToGammaSpace(Mathf.Clamp01(v)) : Mathf.Clamp01(v);
            }
            return new Color(Held(backdrop.r, colour.r), Held(backdrop.g, colour.g), Held(backdrop.b, colour.b), alpha);
        }

        /// <summary>
        /// The shader reads the ramp at vertex colour x mask, before vertex alpha scales the result. So a layer's alpha a goes in both:
        /// the ramp is read at a x mask, the exact strength in the file, and alpha a undoes the a that leaves in mask = u / a.
        /// Unity decodes vertex colour to light in a Linear project, so it is written encoded.
        /// </summary>
        public float VertexChannel(float a) => linear ? Mathf.LinearToGammaSpace(Mathf.Clamp01(a)) : Mathf.Clamp01(a);

        /// <summary>Alpha of a black layer: what is left of the light after darkening on screen by a. start is how much brighter than the
        /// backdrop the pixels under this layer are.</summary>
        public float Dark(float a, float start)
        {
            if (!linear || Straight) return a;
            float under = Mathf.Max(backdrop.r, backdrop.g, backdrop.b) * start;
            return 1 - Lin(under * (1 - Mathf.Clamp01(a))) / Lin(under);
        }
    }
}
