using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>
    /// The way back from Send to VFX Studio: an effect file made from a capture names the slot it came from (game.slot, format 0.4). When
    /// such a file is imported, its effect goes into that slot by itself, through Use as Slot's own route: the slot made editable (Prefab
    /// Mode when the slot is in a prefab), then <see cref="VfxSlot.Use"/> as one undo step. When the project or the package differs from the
    /// capture's, or the slot is gone, nothing is placed: the file imports as usual and the Console says why, and Use as Slot still works.
    /// A file placed once is not placed again on a re-import of the same content (per machine), and an effect already in the slot is never placed twice.
    /// </summary>
    public static class VfxReturn
    {
        public sealed class Outcome
        {
            public string file;
            public bool placed;
            public string why; // when not placed
            public GameObject slot, effect;
        }

        /// <summary>What the last placement attempt did, for tests and tools.</summary>
        public static Outcome Last { get; private set; }

        sealed class Watch : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                var files = imported.Where(p => p.EndsWith(".vfxnative", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (files.Length == 0) return;
                // after the import has finished: placing changes scenes and prefabs, which an import must not
                EditorApplication.delayCall += () => { foreach (var f in files) Place(f, once: true); };
            }
        }

        internal static string Key(string path) => $"BaseGameLab.Vfx.Return.{PlayerSettings.productGUID}.{AssetDatabase.AssetPathToGUID(path)}";

        /// <summary>Puts the effect of the file at <paramref name="path"/> into the slot its capture came from. With <paramref name="once"/>,
        /// a file whose content was already handled on this machine is left alone (a re-import of the same file, a Library rebuild).</summary>
        public static Outcome Place(string path, bool once = false)
        {
            var o = new Outcome { file = path };
            if (!File.Exists(path)) return o;
            var bytes = File.ReadAllBytes(path);
            J slot;
            try { slot = Json.Parse(System.Text.Encoding.UTF8.GetString(bytes))["game"]["slot"]; }
            catch (FormatException) { return o; }
            if (!slot.Exists) return o; // not from a capture: a normal import
            var hash = Hash128.Compute(bytes).ToString();
            if (once && EditorPrefs.GetString(Key(path)) == hash) return o;
            Last = o;

            o.effect = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var name = Path.GetFileName(path);
            if (o.effect == null) return Skip(o, $"{name} did not import, so there is nothing to place.");
            var project = slot["project"].S();
            if (project != null && project != PlayerSettings.productGUID.ToString())
                return Skip(o, $"{name} was captured in another Unity project.");
            var package = slot["package"].S();
            if (package != null && MajorMinor(package) != MajorMinor(VfxCapture.PackageVersion))
                return Skip(o, $"{name} was captured with BaSe VFX {package}, and this project has {VfxCapture.PackageVersion}.");

            var found = Resolve(slot, out var where);
            if (found == null) return Skip(o, $"{where} is not there any more.");
            var editable = VfxSlot.Editable(found);
            if (editable == null) return Skip(o, $"{where} could not be opened for editing.");
            o.slot = editable;
            if (editable.transform.Cast<Transform>().Any(c => VfxSlot.IsOurs(c.gameObject) && PrefabUtility.GetCorrespondingObjectFromOriginalSource(c.gameObject) == o.effect))
            {
                o.placed = true; // already there: a newer export of the same file updates it by itself
                EditorPrefs.SetString(Key(path), hash);
                return o;
            }
            var problem = VfxSlot.Problem(editable, o.effect);
            if (problem != null) return Skip(o, problem);
            VfxSlot.Use(editable, o.effect);
            o.placed = true;
            EditorPrefs.SetString(Key(path), hash); // a skipped file is tried again on its next import
            Debug.Log($"{name} is in {where}, where it was captured. Edit > Undo or BaSe VFX > Restore Original takes it out.", editable);
            return o;
        }

        static Outcome Skip(Outcome o, string why)
        {
            o.why = why;
            Debug.LogWarning($"{why} The effect imported as usual; place it with BaSe VFX > Use as Slot.");
            return o;
        }

        static string MajorMinor(string v)
        {
            var p = v.Split('.');
            return p.Length >= 2 ? p[0] + "." + p[1] : v;
        }

        /// <summary>The slot: inside the prefab asset that holds it when there is one, else in the open scene it was captured in.</summary>
        static GameObject Resolve(J slot, out string where)
        {
            var prefab = slot["prefab"];
            if (prefab.Exists)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(prefab["guid"].S(""));
                var inside = prefab["slot"].S("");
                where = $"{inside} in {(string.IsNullOrEmpty(assetPath) ? prefab["path"].S("the card prefab") : assetPath)}";
                var root = string.IsNullOrEmpty(assetPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                var t = root != null ? root.transform.Find(inside) : null;
                return t != null ? t.gameObject : null;
            }
            var scenePath = slot["scene"].S("");
            var card = slot["card"].S("");
            var slotPath = slot["slot"].S("");
            where = $"{card}/{slotPath} in {(scenePath.Length > 0 ? scenePath : "the scene")}";
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || scene.path != scenePath) continue;
                var parts = card.Split(new[] { '/' }, 2);
                foreach (var r in scene.GetRootGameObjects().Where(g => g.name == parts[0]))
                {
                    var c = parts.Length > 1 ? r.transform.Find(parts[1]) : r.transform;
                    var t = c != null ? c.Find(slotPath) : null;
                    if (t != null) return t.gameObject;
                }
            }
            if (!SceneManager.GetSceneByPath(scenePath).isLoaded) where += " (open that scene and import the file again)";
            return null;
        }
    }
}
