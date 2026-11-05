using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>One per project, at <see cref="AssetPath"/>. Says how exported effects fit this game. Editor only.</summary>
    public sealed class VfxNativeSettings : ScriptableObject
    {
        public const string AssetPath = "Assets/VfxNativeSettings.asset";

        [Serializable]
        public struct TargetBinding
        {
            [Tooltip("Target id in the effect file, for example card.")]
            public string id;
            [Tooltip("Path from the Animator that plays the motion clip to the object it moves.")]
            public string path;
        }

        [Tooltip("Width of the effect's anchor target in the local units of the object the prefab sits under.")]
        public float anchorWidth = 5.56f;
        [Tooltip("Visible screen in those same units, for fullscreen layers. Leave at zero to use the effect's plate.")]
        public Vector2 screenSize = Vector2.zero;
        [Tooltip("Sorting layer for every renderer in the prefab.")]
        public string sortingLayer = "Default";
        [Tooltip("Order of the layer closest behind the anchor. Layers further back count down from here.")]
        public int orderBehind = -100;
        [Tooltip("Order of the rearmost front layer. Layers further forward count up from here.")]
        public int orderFront = 100;
        [Tooltip("Render queue of layers behind the anchor. In a game that sorts cards by render queue, set it below the card's parts.")]
        public int queueBehind = 3000;
        [Tooltip("Render queue of layers in front of the anchor.")]
        public int queueFront = 3000;
        [Tooltip("Only for effects exported before format 0.3: in a Linear project, the colour they usually land on, as shown on screen.")]
        public Color backdrop = new Color(0.2f, 0.2f, 0.2f);
        [Tooltip("Shader name of the AllIn1Vfx variant materials use.")]
        public string shader = VfxMaterials.DefaultShader;
        [Tooltip("Where each moved target lives.")]
        public TargetBinding[] targets = Array.Empty<TargetBinding>();

        /// <summary>The one place the importer reads, so the menu never lets it land anywhere else.</summary>
        [MenuItem("Assets/BaSe VFX/Project Settings")]
        public static void OpenOrCreate() => OpenOrCreate(null);

        /// <summary>A new settings file starts from what an effect carries for its game (seed, else the newest effect in the project),
        /// so creating it changes no effect; only what is then edited in it does.</summary>
        public static void OpenOrCreate(string seed)
        {
            if (AssetDatabase.LoadAssetAtPath<VfxNativeSettings>(AssetPath) == null)
            {
                var s = CreateInstance<VfxNativeSettings>();
                var from = seed ?? NewestEffect();
                if (from != null)
                {
                    try { Apply(s, Json.Parse(File.ReadAllText(from))["game"]); }
                    catch (Exception e) when (e is FormatException || e is IOException) { }
                }
                AssetDatabase.CreateAsset(s, AssetPath);
            }
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<VfxNativeSettings>(AssetPath);
        }

        static string NewestEffect()
        {
            string newest = null;
            var when = DateTime.MinValue;
            foreach (var guid in AssetDatabase.FindAssets("", new[] { "Assets" }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!p.EndsWith(".vfxnative", StringComparison.OrdinalIgnoreCase)) continue;
                var t = File.GetLastWriteTimeUtc(p);
                if (t > when) { when = t; newest = p; }
            }
            return newest;
        }

        /// <summary>The project's settings, or defaults when it has none.</summary>
        public static VfxNativeSettings Load()
        {
            var s = AssetDatabase.LoadAssetAtPath<VfxNativeSettings>(AssetPath);
            return s != null ? s : CreateInstance<VfxNativeSettings>();
        }

        /// <summary>What an effect imports with: the project's settings if it has them, else the values the file carries for its game.</summary>
        internal static VfxNativeSettings For(J doc)
        {
            var s = Load();
            var g = doc["game"];
            if (File.Exists(AssetPath) || !g.Exists) return s;
            Apply(s, g);
            return s;
        }

        static void Apply(VfxNativeSettings s, J g)
        {
            if (!g.Exists) return;
            s.anchorWidth = g["anchorWidth"].F(s.anchorWidth);
            if (g["screenSize"].Exists) s.screenSize = g["screenSize"].V2;
            s.sortingLayer = g["sortingLayer"].S(s.sortingLayer);
            s.orderBehind = g["orderBehind"].I(s.orderBehind);
            s.orderFront = g["orderFront"].I(s.orderFront);
            s.queueBehind = g["queueBehind"].I(s.queueBehind);
            s.queueFront = g["queueFront"].I(s.queueFront);
            s.shader = g["shader"].S(s.shader);
            var b = g["backdrop"];
            if (b.Exists) s.backdrop = new Color(b[0].F(), b[1].F(), b[2].F());
            var targets = new System.Collections.Generic.List<TargetBinding>();
            foreach (var t in g["targets"].Pairs) targets.Add(new TargetBinding { id = t.Key, path = t.Value.S() });
            s.targets = targets.ToArray();
        }

        /// <summary>Where the project's settings file differs from what this effect carries for its game, in words; empty when there
        /// is no settings file, the effect carries nothing, or they agree. The file still wins: this only keeps it from being silent.</summary>
        internal static System.Collections.Generic.List<string> Overrides(J doc, VfxNativeSettings s)
        {
            var list = new System.Collections.Generic.List<string>();
            var g = doc["game"];
            if (!File.Exists(AssetPath) || !g.Exists) return list;
            void Num(string what, float mine, J theirs, float tolerance = 1e-3f)
            {
                if (theirs.Exists && Mathf.Abs(mine - theirs.F()) > tolerance) list.Add($"{what} {mine:0.####} (the effect: {theirs.F():0.####})");
            }
            Num("anchor width", s.anchorWidth, g["anchorWidth"]);
            Num("render queue behind", s.queueBehind, g["queueBehind"], 0.5f);
            Num("render queue in front", s.queueFront, g["queueFront"], 0.5f);
            Num("sorting order behind", s.orderBehind, g["orderBehind"], 0.5f);
            Num("sorting order in front", s.orderFront, g["orderFront"], 0.5f);
            if (g["sortingLayer"].Exists && g["sortingLayer"].S() != s.sortingLayer) list.Add($"sorting layer {s.sortingLayer} (the effect: {g["sortingLayer"].S()})");
            if (g["shader"].Exists && g["shader"].S() != s.shader) list.Add($"shader {s.shader} (the effect: {g["shader"].S()})");
            return list;
        }

        public string PathOf(string id)
        {
            foreach (var t in targets)
                if (t.id == id) return t.path;
            return null;
        }
    }
}
