using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>
    /// The shader variants an effect draws with, as a ShaderVariantCollection sub-asset ("&lt;effect&gt; warm-up"). It is plain
    /// Unity data, no code of ours: add it to Project Settings > Graphics > Preloaded Shaders, or call WarmUp() on it from the
    /// game's own loading step, and the first play no longer compiles shaders mid-game (BGL-263).
    /// </summary>
    internal static class VfxWarmup
    {
        internal const string Id = "warmup";

        // Tried in order; the first pass type the shader has is used (AllIn1Vfx's untagged URP pass is not DefaultUnlit to Unity).
        internal static readonly PassType[] Passes = { PassType.ScriptableRenderPipelineDefaultUnlit, PassType.ScriptableRenderPipeline, PassType.Normal };

        public static ShaderVariantCollection Collect(AssetImportContext ctx, string effect)
        {
            var objects = new List<Object>();
            ctx.GetObjects(objects);
            var collection = new ShaderVariantCollection { name = effect + " warm-up" };
            foreach (var m in objects.OfType<Material>())
            {
                if (m.shader == null) continue;
                var keywords = m.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal).ToArray();
                foreach (var pass in Passes)
                {
                    try
                    {
                        collection.Add(new ShaderVariantCollection.ShaderVariant(m.shader, pass, keywords));
                        break;
                    }
                    catch (ArgumentException) { } // the shader has no such pass or variant: try the next pass type
                }
            }
            return collection.variantCount > 0 ? collection : null;
        }
    }
}
