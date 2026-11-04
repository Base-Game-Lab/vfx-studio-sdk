using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>Imports a .vfxnative file as a prefab. The prefab and everything it uses are read-only sub-assets of that file.</summary>
    // Raise the version whenever imported output changes: Unity re-imports on it, not on a code change.
    [ScriptedImporter(16, "vfxnative", 0)] // default queue, before prefabs: a card prefab nesting this effect logs a missing-nested-prefab error on a clean Library otherwise. The shader is a declared dependency, so it still imports first.
    public sealed class VfxImporter : ScriptedImporter
    {
        internal const string NoteId = "notes";

        // Adjustments. They live in this file's .meta, so replacing the .vfxnative with a new version under the same name keeps them.
        [Tooltip("Plays the whole effect this many times faster: clips, particle lifetimes, emission and velocities. 2 takes half the time.")]
        [Min(0.05f)] public float speed = 1;
        [Tooltip("Scales every layer's size and its distance from the card. Screen-wide dims and the card's own motion stay as they are.")]
        [Min(0.05f)] public float size = 1;
        [Tooltip("Multiplies every colour in the effect. White changes nothing; black stays black.")]
        [ColorUsage(false)] public Color tint = Color.white;
        [Tooltip("Multiplies the light each layer adds. Dims are left alone. Above 1 only the glowing layers get brighter.")]
        [Range(0, 2)] public float intensity = 1;

        internal VfxKnobs Knobs => new VfxKnobs { speed = speed > 0 ? speed : 1, size = size > 0 ? size : 1, tint = tint, intensity = Mathf.Max(0, intensity) };

        public override void OnImportAsset(AssetImportContext ctx)
        {
            if (File.Exists(VfxNativeSettings.AssetPath)) ctx.DependsOnSourceAsset(VfxNativeSettings.AssetPath);
            ctx.DependsOnCustomDependency(VfxProjectWatch.DependencyName);

            var fileName = Path.GetFileName(ctx.assetPath);
            var report = new VfxReport(Path.GetFileNameWithoutExtension(ctx.assetPath));

            J doc;
            try { doc = Json.Parse(File.ReadAllText(ctx.assetPath)); }
            catch (Exception e) when (e is FormatException || e is IOException)
            {
                Fail(ctx, report, $"{fileName} could not be read: {e.Message}.");
                return;
            }

            var versionProblem = VfxVersion.Problem(doc, fileName);
            if (versionProblem != null)
            {
                Fail(ctx, report, versionProblem);
                return;
            }

            var settings = VfxNativeSettings.For(doc);
            var shader = VfxShaders.Find(settings.shader);
            if (shader == null && VfxShaders.PathOf(settings.shader) != null)
            {
                // The first pass of a clean Library: the file is there but Unity cannot hand it out yet. A second pass follows.
                report.Attention.Add($"Waiting for {settings.shader} to finish importing.");
                shader = Shader.Find(VfxMaterials.Fallback);
            }
            else if (shader == null)
            {
                var plain = $"AllIn1Vfx is not in this project: no shader \"{settings.shader}\". Install AllIn1Vfx Toolkit and {fileName} rebuilds itself. Until then its materials use {VfxMaterials.Fallback}.";
                ctx.LogImportError(plain);
                report.Attention.Add(plain);
                shader = Shader.Find(VfxMaterials.Fallback);
            }

            try
            {
                new VfxBuilder(ctx, doc, settings, shader, report, Knobs).Build();
            }
            catch (FormatException e)
            {
                Fail(ctx, report, $"{fileName} could not be built: {e.Message}");
                return;
            }

            if (report.Dropped.Count > 0)
                ctx.LogImportWarning($"{report.Effect}: {report.Dropped.Count} thing(s) did not map to Unity. The list is in its inspector.");
            ctx.AddObjectToAsset(NoteId, new TextAsset(report.ToText()) { name = report.Effect + " notes" });
        }

        // Imports these first, so a clean Library never reads a half-imported asset or misses a shader that is there.
        static string[] GatherDependenciesFromSourceFile(string path)
        {
            var deps = new List<string>();
            if (File.Exists(VfxNativeSettings.AssetPath)) deps.Add(VfxNativeSettings.AssetPath);
            var shader = VfxShaders.PathOf(ShaderNameOnDisk());
            if (shader != null) deps.Add(shader);
            return deps.ToArray();
        }

        // Assets cannot load during dependency gathering, so this reads the settings file as text.
        static string ShaderNameOnDisk()
        {
            if (File.Exists(VfxNativeSettings.AssetPath))
                foreach (var line in File.ReadLines(VfxNativeSettings.AssetPath))
                    if (line.StartsWith("  shader: ")) return line.Substring(10).Trim();
            return VfxMaterials.DefaultShader;
        }

        static void Fail(AssetImportContext ctx, VfxReport report, string message)
        {
            ctx.LogImportError(message);
            report.Attention.Add(message);
            var note = new TextAsset(report.ToText()) { name = report.Effect + " notes" };
            ctx.AddObjectToAsset(NoteId, note);
            ctx.SetMainObject(note);
        }
    }

    /// <summary>Finds the shader by its file, because on a clean Library Shader.Find misses one that has only just imported.</summary>
    internal static class VfxShaders
    {
        static readonly Dictionary<string, string> Found = new Dictionary<string, string>();

        public static void Forget() => Found.Clear();

        public static string PathOf(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName)) return null;
            if (Found.TryGetValue(shaderName, out var known)) return known;
            var leaf = shaderName.Substring(shaderName.LastIndexOf('/') + 1) + ".shader";
            var hits = Directory.GetFiles("Assets", leaf, SearchOption.AllDirectories);
            var path = hits.Length > 0 ? hits[0].Replace('\\', '/') : null;
            Found[shaderName] = path;
            return path;
        }

        public static Shader Find(string shaderName)
        {
            var path = PathOf(shaderName);
            var shader = path != null ? AssetDatabase.LoadAssetAtPath<Shader>(path) : null;
            return shader != null ? shader : Shader.Find(shaderName);
        }
    }

    /// <summary>Re-imports effects when AllIn1Vfx or the settings asset appears or goes, so neither needs an extra step.</summary>
    internal sealed class VfxProjectWatch : AssetPostprocessor
    {
        public const string DependencyName = "basegamelab.vfx/project";

        [InitializeOnLoadMethod]
        static void Register() => EditorApplication.delayCall += Update;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (Touches(imported) || Touches(deleted)) EditorApplication.delayCall += Update;
        }

        static bool Touches(string[] paths)
        {
            foreach (var p in paths)
            {
                if (p == VfxNativeSettings.AssetPath) return true;
                if (p.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) && p.IndexOf("AllIn1Vfx", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        static void Update()
        {
            VfxShaders.Forget();
            var state = $"{VfxShaders.Find(VfxNativeSettings.Load().shader) != null}/{File.Exists(VfxNativeSettings.AssetPath)}/{PlayerSettings.colorSpace}";
            AssetDatabase.RegisterCustomDependency(DependencyName, Hash128.Compute(state));
            // The dependency only takes effect on a refresh. Kept in Library so an unchanged project never refreshes at startup.
            const string file = "Library/BaseGameLabVfxState.txt";
            if (File.Exists(file) && File.ReadAllText(file) == state) return;
            File.WriteAllText(file, state);
            AssetDatabase.Refresh();
        }
    }
}
