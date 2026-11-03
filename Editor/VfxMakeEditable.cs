using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>Copies an effect's read-only parts into plain project assets artists can change. A later re-import never touches the copy.</summary>
    internal static class VfxMakeEditable
    {
        const string Menu = "Assets/BaSe VFX/Make Editable";

        [MenuItem(Menu, true)]
        static bool CanExport() => Selected() != null;

        [MenuItem(Menu)]
        static void ExportSelected() => Export(Selected());

        static string Selected()
        {
            var p = AssetDatabase.GetAssetPath(Selection.activeObject);
            return AssetImporter.GetAtPath(p) is VfxImporter ? p : null;
        }

        /// <summary>Writes the copy into a new folder beside the file and returns the folder.</summary>
        public static string Export(string assetPath)
        {
            var parts = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
            {
                Debug.LogError($"{Path.GetFileName(assetPath)} did not import, so there is nothing to copy. See its inspector.");
                return null;
            }

            var dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(assetPath);
            var folder = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{name} Editable");
            AssetDatabase.CreateFolder(dir, Path.GetFileName(folder));

            var map = new Dictionary<Object, Object>();
            AssetDatabase.StartAssetEditing();
            try
            {
                // From the file, not the imported texture, which may be compressed for a mobile target.
                var doc = Json.Parse(File.ReadAllText(assetPath));
                foreach (var m in doc["assets"]["masks"].Pairs)
                {
                    var t = VfxMasks.Decode(m.Key, m.Value);
                    File.WriteAllBytes($"{folder}/{t.name}.png", ImageConversion.EncodeToPNG(t));
                    Object.DestroyImmediate(t);
                }
                foreach (var o in parts)
                    if (o is Texture2D fill && fill.name == VfxMasks.FillName) File.WriteAllBytes($"{folder}/{fill.name}.png", ImageConversion.EncodeToPNG(fill));
                foreach (var o in parts)
                    if (o is TextAsset n) File.WriteAllText($"{folder}/{name} notes.txt", n.text);
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();

            foreach (var o in parts)
                if (o is Texture2D t && !t.name.StartsWith("z_")) map[t] = ConfigureSprite($"{folder}/{t.name}.png", t);

            foreach (var o in parts)
                if (o is Sprite s) map[s] = AssetDatabase.LoadAssetAtPath<Sprite>($"{folder}/{s.texture.name}.png");

            var clips = new List<AnimationClip>();
            foreach (var o in parts)
            {
                if (!(o is AnimationClip c)) continue;
                var copy = Object.Instantiate(c);
                copy.name = c.name;
                AssetDatabase.CreateAsset(copy, $"{folder}/{c.name}.anim");
                map[c] = copy;
                clips.Add(copy);
            }

            foreach (var o in parts)
            {
                if (!(o is Material m)) continue;
                var copy = new Material(m) { name = m.name };
                AssetDatabase.CreateAsset(copy, $"{folder}/{m.name}.mat");
                foreach (var prop in copy.GetTexturePropertyNames())
                {
                    var tex = copy.GetTexture(prop) as Texture2D;
                    if (tex == null) continue;
                    if (tex.name.StartsWith("z_"))
                    {
                        // Ramps stay inside their material, which is where AllIn1Vfx keeps them.
                        var ramp = Object.Instantiate(tex);
                        ramp.name = tex.name;
                        AssetDatabase.AddObjectToAsset(ramp, copy);
                        copy.SetTexture(prop, ramp);
                    }
                    else if (map.TryGetValue(tex, out var mapped)) copy.SetTexture(prop, (Texture2D)mapped);
                }
                map[m] = copy;
            }

            var source = (AnimatorController)prefab.GetComponent<Animator>().runtimeAnimatorController;
            var main = (AnimationClip)map[source.layers[0].stateMachine.defaultState.motion];
            var controller = AnimatorController.CreateAnimatorControllerAtPathWithClip($"{folder}/{main.name}.controller", main);

            var instance = Object.Instantiate(prefab);
            instance.name = main.name;
            try
            {
                foreach (var sr in instance.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    sr.sprite = (Sprite)map[sr.sprite];
                    sr.sharedMaterial = (Material)map[sr.sharedMaterial];
                }
                foreach (var pr in instance.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    pr.sharedMaterial = (Material)map[pr.sharedMaterial];
                instance.GetComponent<Animator>().runtimeAnimatorController = controller;
                PrefabUtility.SaveAsPrefabAsset(instance, $"{folder}/{main.name}.prefab");
            }
            finally { Object.DestroyImmediate(instance); }

            AssetDatabase.SaveAssets();
            Debug.Log($"Editable copy of {name} is in {folder}. Re-importing {Path.GetFileName(assetPath)} will not change it.");
            return folder;
        }

        static Object ConfigureSprite(string path, Texture2D source)
        {
            var i = (TextureImporter)AssetImporter.GetAtPath(path);
            i.textureType = TextureImporterType.Sprite;
            i.spriteImportMode = SpriteImportMode.Single;
            i.spritePixelsPerUnit = source.width;
            i.sRGBTexture = false;
            i.alphaIsTransparency = true;
            i.wrapMode = source.wrapMode;
            var s = new TextureImporterSettings();
            i.ReadTextureSettings(s);
            s.spriteMeshType = SpriteMeshType.FullRect;
            i.SetTextureSettings(s);
            i.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
