using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>
    /// Hierarchy > right-click a slot > BaSe VFX > Import from Studio: an effect into the slot already chosen. Either one of the effects
    /// already in the project (newest first, the ones captured from this slot marked) or a .vfxnative from disk, copied into
    /// Assets/BaSe VFX first. Placing is Use as Slot's own route (<see cref="VfxSlot.UseHere"/>): Prefab Mode for a card prefab, the
    /// original children parked under _Original, one Undo, card motion bound. A file placed here is marked as chosen
    /// (<see cref="VfxReturn.Choose"/>), so its import never also puts it back into the slot it was captured from.
    /// </summary>
    public static class VfxImport
    {
        /// <summary>Where From file... copies an effect file.</summary>
        public const string Folder = "Assets/BaSe VFX";

        /// <summary>One effect file in the project, as the picker lists it.</summary>
        public sealed class Entry
        {
            public string path;
            public GameObject effect;
            /// <summary>The file's last write, UTC: the picker lists newest first.</summary>
            public DateTime time;
            /// <summary>Made from a capture of this very slot (the file's game.slot names it).</summary>
            public bool capturedHere;
            /// <summary>For a capture's effect, the card and slot it was captured from, else null.</summary>
            public string capturedFrom;
            /// <summary>A copy of it is in the slot now.</summary>
            public bool inSlot;
        }

        /// <summary>The slot for a picked object: the object itself, or the slot above one of our placed effects.</summary>
        public static GameObject SlotFor(GameObject picked)
        {
            if (picked != null && VfxSlot.IsOurs(picked) && picked.transform.parent != null) return picked.transform.parent.gameObject;
            return picked;
        }

        /// <summary>Null when an effect can be imported into this slot, otherwise why not, in one sentence.</summary>
        public static string Problem(GameObject slot)
        {
            if (slot == null) return "Right-click a slot under a card's VFX node in the Hierarchy, such as DefaultOnRevealVFX.";
            if (slot.name == VfxSlot.Parked) return $"{VfxSlot.Parked} holds a slot's original children; pick the slot above it.";
            var parent = slot.transform.parent;
            if (parent == null || parent.name != VfxSlot.VfxNode) return $"{slot.name} is not a slot: pick a child of a card's {VfxSlot.VfxNode} node.";
            return null;
        }

        /// <summary>The effect files in the project, newest first, each marked when it was captured from <paramref name="slot"/>.</summary>
        public static List<Entry> Effects(GameObject slot)
        {
            var list = new List<Entry>();
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.EndsWith(".vfxnative", StringComparison.OrdinalIgnoreCase) || !(AssetImporter.GetAtPath(path) is VfxImporter)) continue;
                var effect = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (effect == null || !File.Exists(path)) continue;
                var from = VfxReturn.CapturedSlot(path);
                list.Add(new Entry
                {
                    path = path,
                    effect = effect,
                    time = File.GetLastWriteTimeUtc(path),
                    capturedHere = CapturedFrom(from, slot),
                    capturedFrom = Where(from),
                    inSlot = slot != null && slot.transform.Cast<Transform>().Any(c => VfxSlot.IsOurs(c.gameObject) && PrefabUtility.GetCorrespondingObjectFromOriginalSource(c.gameObject) == effect),
                });
            }
            return list.OrderByDescending(e => e.time).ThenBy(e => e.path, StringComparer.Ordinal).ToList();
        }

        /// <summary>True when a capture's slot (an effect file's game.slot, as <see cref="VfxCapture"/> writes it) is this slot: the same
        /// slot in the same card prefab when the capture was of a prefab, else the same card and slot in the same scene.</summary>
        internal static bool CapturedFrom(J from, GameObject slot)
        {
            if (!from.Exists || slot == null) return false;
            var project = from["project"].S();
            if (project != null && project != PlayerSettings.productGUID.ToString()) return false;
            var prefab = from["prefab"];
            if (prefab.Exists)
            {
                var (guid, inside) = InPrefab(slot);
                return guid != null && guid == prefab["guid"].S("") && inside == prefab["slot"].S("");
            }
            var card = VfxCapture.CardOf(slot);
            return card != null && slot.scene.IsValid() && slot.scene.path == from["scene"].S("") && PathOf(card) == from["card"].S("")
                && AnimationUtility.CalculateTransformPath(slot.transform, card) == from["slot"].S("");
        }

        // The card prefab that holds the slot and the slot's path in it, the way a capture records it; nulls when no prefab holds it.
        static (string guid, string inside) InPrefab(GameObject slot)
        {
            var stage = PrefabStageUtility.GetPrefabStage(slot);
            if (stage != null)
                return (AssetDatabase.AssetPathToGUID(stage.assetPath), AnimationUtility.CalculateTransformPath(slot.transform, stage.prefabContentsRoot.transform));
            if (EditorUtility.IsPersistent(slot))
                return (AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(slot)), AnimationUtility.CalculateTransformPath(slot.transform, slot.transform.root));
            if (PrefabUtility.IsPartOfPrefabInstance(slot))
            {
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(slot);
                var asset = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(slot);
                return (AssetDatabase.AssetPathToGUID(asset), AnimationUtility.CalculateTransformPath(slot.transform, root.transform));
            }
            return (null, null);
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        // "Card/Reveal": the card (or card prefab) and slot a capture's effect came from.
        static string Where(J from)
        {
            if (!from.Exists) return null;
            var prefab = from["prefab"];
            if (prefab.Exists)
            {
                var path = AssetDatabase.GUIDToAssetPath(prefab["guid"].S(""));
                if (string.IsNullOrEmpty(path)) path = prefab["path"].S("");
                var inside = prefab["slot"].S("");
                return $"{Path.GetFileNameWithoutExtension(path)}/{inside.Split('/').Last()}";
            }
            var card = from["card"].S("");
            return $"{card.Split('/').Last()}/{from["slot"].S("").Split('/').Last()}";
        }

        /// <summary>Puts an effect already in the project into the slot: Use as Slot's route, with Undo. Returns the placed effect, or null
        /// when the card prefab could not be opened.</summary>
        public static GameObject Use(GameObject slot, GameObject effect)
        {
            var problem = Problem(slot) ?? (VfxSlot.IsEffect(effect) ? null : "Pick an effect: a .vfxnative file from VFX Studio.");
            if (problem != null) throw new InvalidOperationException(problem);
            var path = AssetDatabase.GetAssetPath(effect);
            var placed = VfxSlot.UseHere(slot, effect);
            if (placed != null) VfxReturn.Choose(path);
            return placed;
        }

        /// <summary>From file...: copies a .vfxnative into <see cref="Folder"/> (made when missing; a free name when the file's is taken),
        /// imports it and puts it into the slot. A file already under Assets is used where it is. The file is marked as chosen before
        /// the import's own placement runs, so it lands only in this slot. Returns the placed effect; <paramref name="path"/> is the
        /// file's place in the project.</summary>
        public static GameObject FromFile(string file, GameObject slot, out string path)
        {
            path = null;
            var problem = Problem(slot);
            if (problem != null) throw new InvalidOperationException(problem);
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) throw new InvalidOperationException($"{file} is not there.");
            if (!file.EndsWith(".vfxnative", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{Path.GetFileName(file)} is not an effect: pick a .vfxnative file from VFX Studio's Unity export.");
            path = CopyIn(file);
            VfxReturn.Choose(path); // before the import's delayed placement runs, which then leaves it alone
            var effect = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!VfxSlot.IsEffect(effect)) throw new InvalidOperationException($"{Path.GetFileName(path)} did not import as an effect; the Console says why.");
            return VfxSlot.UseHere(slot, effect);
        }

        /// <summary>The file in the project: a file already under Assets as it is, else a copy in <see cref="Folder"/>, imported.</summary>
        public static string CopyIn(string file)
        {
            var full = Path.GetFullPath(file).Replace('\\', '/');
            var assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
            if (full.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase))
            {
                var inProject = "Assets" + full.Substring(assets.Length);
                if (!(AssetImporter.GetAtPath(inProject) is VfxImporter)) AssetDatabase.ImportAsset(inProject, ImportAssetOptions.ForceSynchronousImport);
                return inProject;
            }
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            var path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{Path.GetFileName(file)}");
            File.Copy(file, path);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow); // newest in the project, so it heads the list
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        /// <summary>Where From file... starts: the Downloads folder, where a browser saves the studio's export, else the home folder.</summary>
        public static string Downloads()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var downloads = Path.Combine(home, "Downloads");
            return Directory.Exists(downloads) ? downloads : home;
        }
    }

    /// <summary>The Import from Studio picker for one slot: the project's effects with a Use button each, and From file....</summary>
    internal sealed class VfxImportWindow : EditorWindow
    {
        internal const string Title = "Import from Studio";
        const string Menu = "GameObject/BaSe VFX/Import from Studio...";

        [SerializeField] GameObject slot;
        List<VfxImport.Entry> effects = new List<VfxImport.Entry>();
        Vector2 scroll;

        // always on, as Send to VFX Studio is: with the wrong thing selected, it says why
        [MenuItem(Menu, false, 11)]
        static void Open()
        {
            var slot = VfxImport.SlotFor(Selection.activeGameObject);
            var problem = VfxImport.Problem(slot);
            if (problem != null) { EditorUtility.DisplayDialog(Title, problem, "OK"); return; }
            var w = GetWindow<VfxImportWindow>(true, Title, true);
            w.minSize = new Vector2(380, 220);
            w.slot = slot;
            w.Refresh();
        }

        void OnEnable()
        {
            EditorApplication.projectChanged += Refresh;
            Refresh();
        }

        void OnDisable() => EditorApplication.projectChanged -= Refresh;

        void Refresh()
        {
            effects = slot != null ? VfxImport.Effects(slot) : new List<VfxImport.Entry>();
            Repaint();
        }

        void OnGUI()
        {
            if (slot == null)
            {
                EditorGUILayout.HelpBox("The slot is gone. Right-click a slot in the Hierarchy and choose BaSe VFX > Import from Studio... again.", MessageType.Info);
                return;
            }
            var card = VfxCapture.CardOf(slot);
            EditorGUILayout.LabelField("Slot", card != null ? $"{card.name} / {slot.name}" : slot.name, EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (GUILayout.Button(new GUIContent("From file...", "A .vfxnative from VFX Studio's Unity export. It is copied into " + VfxImport.Folder + " and put in this slot."), GUILayout.Height(24)))
                Later(() =>
                {
                    var file = EditorUtility.OpenFilePanel(Title, VfxImport.Downloads(), "vfxnative");
                    return string.IsNullOrEmpty(file) ? null : VfxImport.FromFile(file, slot, out _);
                });
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Already in this project, newest first", EditorStyles.miniBoldLabel);
            if (effects.Count == 0)
            {
                EditorGUILayout.HelpBox("No effects in this project yet. From file... brings one in.", MessageType.None);
                return;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var e in effects)
            {
                if (e.effect == null) continue;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent(e.effect.name, e.path), e.capturedHere ? EditorStyles.boldLabel : EditorStyles.label, GUILayout.MinWidth(80)))
                    EditorGUIUtility.PingObject(e.effect);
                GUILayout.FlexibleSpace();
                var note = e.inSlot ? "in this slot now" : e.capturedHere ? "captured from this slot" : e.capturedFrom != null ? $"from {e.capturedFrom}" : "";
                GUILayout.Label(note, EditorStyles.miniLabel);
                GUILayout.Label(e.time.ToLocalTime().ToString("d MMM HH:mm"), EditorStyles.miniLabel, GUILayout.Width(70));
                var effect = e.effect;
                if (GUILayout.Button("Use", GUILayout.Width(50))) Later(() => VfxImport.Use(slot, effect));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        // After OnGUI, because a file dialog, Prefab Mode and new objects inside a GUI pass break its layout. A card prefab's slot is then
        // the one in Prefab Mode, which the window follows.
        void Later(Func<GameObject> change)
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    var placed = change();
                    if (placed != null)
                    {
                        Selection.activeGameObject = placed;
                        slot = placed.transform.parent.gameObject;
                    }
                }
                catch (InvalidOperationException e) { EditorUtility.DisplayDialog(Title, e.Message, "OK"); }
                Refresh();
            };
        }
    }
}
