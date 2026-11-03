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
        public static void OpenOrCreate()
        {
            if (AssetDatabase.LoadAssetAtPath<VfxNativeSettings>(AssetPath) == null)
                AssetDatabase.CreateAsset(CreateInstance<VfxNativeSettings>(), AssetPath);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<VfxNativeSettings>(AssetPath);
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
            return s;
        }

        public string PathOf(string id)
        {
            foreach (var t in targets)
                if (t.id == id) return t.path;
            return null;
        }
    }
}
