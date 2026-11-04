using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>
    /// Puts an effect into a slot the card already has, such as DefaultOnRevealVFX, so the game's own code starts it. The slot object
    /// stays as it is: its name, its components and every reference to it. Its children move under one inactive child, _Original,
    /// and the effect is nested as an active child of the slot, so turning the slot on plays ours. One Undo reverses it, and
    /// <see cref="Restore"/> puts the original children back later.
    /// </summary>
    public static class VfxSlot
    {
        /// <summary>The inactive child that holds the slot's own children while one of our effects is in it.</summary>
        public const string Parked = "_Original";
        /// <summary>The card node whose children are the slots.</summary>
        public const string VfxNode = "VFX";

        /// <summary>The card's VFX node: the picked object if it is one, the nearest one above it, else the shallowest one below.</summary>
        public static Transform FindVfxNode(GameObject picked)
        {
            if (picked == null) return null;
            for (var t = picked.transform; t != null; t = t.parent)
                if (t.name == VfxNode) return t;
            var queue = new Queue<Transform>();
            queue.Enqueue(picked.transform);
            while (queue.Count > 0)
            {
                var t = queue.Dequeue();
                if (t.name == VfxNode) return t;
                foreach (Transform c in t) queue.Enqueue(c);
            }
            return null;
        }

        /// <summary>The slots of the card the picked object belongs to: every child of its VFX node except our own effects.</summary>
        public static GameObject[] Slots(GameObject picked)
        {
            var vfx = FindVfxNode(picked);
            if (vfx == null) return Array.Empty<GameObject>();
            return vfx.Cast<Transform>().Select(t => t.gameObject).Where(g => !IsOurs(g) && g.name != Parked).ToArray();
        }

        /// <summary>A prefab imported from a .vfxnative file.</summary>
        public static bool IsEffect(GameObject prefab) =>
            prefab != null && EditorUtility.IsPersistent(prefab) && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(prefab)) is VfxImporter;

        /// <summary>The root of a placed copy of one of our effects.</summary>
        public static bool IsOurs(GameObject go)
        {
            if (go == null || PrefabUtility.GetNearestPrefabInstanceRoot(go) != go) return false;
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
            return source != null && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(source)) is VfxImporter;
        }

        /// <summary>The effect prefab behind a selected file, prefab or placed copy, or null.</summary>
        public static GameObject EffectOf(Object o)
        {
            if (o == null) return null;
            var path = AssetDatabase.GetAssetPath(o);
            if (!string.IsNullOrEmpty(path))
                return AssetImporter.GetAtPath(path) is VfxImporter ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
            if (o is GameObject go)
            {
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(go);
                if (root != null && IsOurs(root)) return (GameObject)PrefabUtility.GetCorrespondingObjectFromOriginalSource(root);
            }
            return null;
        }

        /// <summary>True when one of our effects is in the slot and its original children are parked.</summary>
        public static bool Holds(GameObject slot) => slot != null && slot.transform.Find(Parked) != null;

        /// <summary>Null when the effect can go into the slot here, otherwise why not, in one sentence.</summary>
        public static string Problem(GameObject slot, GameObject effect)
        {
            if (slot == null) return "Pick a card, or one of its slots.";
            if (!IsEffect(effect)) return "Pick an effect: a .vfxnative file from VFX Studio.";
            if (IsOurs(slot)) return $"{slot.name} is one of our effects, not a slot.";
            if (slot.name == Parked) return $"{Parked} holds a slot's original children; pick the slot above it.";
            if (EditorUtility.IsPersistent(slot)) return $"{slot.name} is inside a prefab file; open the prefab to change it.";
            var locked = Locked(slot);
            if (locked != null) return $"{slot.name}'s children belong to the prefab {locked}, and a prefab's own children cannot move in a copy of it. Open {System.IO.Path.GetFileName(locked)} and do it there.";
            return null;
        }

        // The prefab file that defines the slot's children, when the slot is a copy of that prefab and so they cannot move here.
        static string Locked(GameObject slot)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(slot)) return null;
            foreach (Transform c in slot.transform)
                if (PrefabUtility.IsPartOfPrefabInstance(c.gameObject) && !PrefabUtility.IsAddedGameObjectOverride(c.gameObject) && !IsOurs(c.gameObject))
                    return PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(slot);
            return null;
        }

        /// <summary>Puts the effect into the slot as one undo step. Running it on a slot that already holds one of ours swaps the effect and
        /// keeps the one parked original. An effect with card motion is bound to the card's Animator when Timeline is in the project.</summary>
        public static GameObject Use(GameObject slot, GameObject effect, bool undo = true)
        {
            var problem = Problem(slot, effect);
            if (problem != null) throw new InvalidOperationException(problem);
            var name = $"Use {effect.name} as {slot.name}";
            int group = Begin(name, undo);
            var t = slot.transform;
            var parked = t.Find(Parked);
            if (parked == null)
            {
                var children = t.Cast<Transform>().ToList();
                parked = NewParked(t, undo, name);
                foreach (var c in children) Move(c, parked, undo, name);
            }
            else
            {
                foreach (var old in t.Cast<Transform>().Where(c => IsOurs(c.gameObject)).ToList()) Destroy(old.gameObject, undo);
            }

            var placed = (GameObject)PrefabUtility.InstantiatePrefab(effect, t);
            if (undo) Undo.RegisterCreatedObjectUndo(placed, name);
            AtCardCentre(placed.transform, t);
            placed.SetActive(true);
            BindCardMotion(placed);
            if (undo) Undo.CollapseUndoOperations(group);
            return placed;
        }

        /// <summary>Takes our effect out of the slot and puts its original children back where they were, as one undo step.</summary>
        public static void Restore(GameObject slot, bool undo = true)
        {
            if (!Holds(slot)) throw new InvalidOperationException($"{(slot != null ? slot.name : "Nothing")} holds no parked original to restore.");
            var name = $"Restore {slot.name}";
            int group = Begin(name, undo);
            var t = slot.transform;
            var parked = t.Find(Parked);
            foreach (var ours in t.Cast<Transform>().Where(c => IsOurs(c.gameObject)).ToList()) Destroy(ours.gameObject, undo);
            foreach (var c in parked.Cast<Transform>().ToList()) Move(c, t, undo, name);
            Destroy(parked.gameObject, undo);
            if (undo) Undo.CollapseUndoOperations(group);
        }

        /// <summary>The same change inside a prefab file, saved, with no undo: for scripts and batch use. Restore reverses it.</summary>
        public static void UseInPrefab(string prefabPath, string slotPath, GameObject effect) => InPrefab(prefabPath, slotPath, slot => Use(slot, effect, false));

        public static void RestoreInPrefab(string prefabPath, string slotPath) => InPrefab(prefabPath, slotPath, slot => Restore(slot, false));

        static void InPrefab(string prefabPath, string slotPath, Action<GameObject> change)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var slot = string.IsNullOrEmpty(slotPath) ? root.transform : root.transform.Find(slotPath);
                if (slot == null) throw new ArgumentException($"{prefabPath} has no {slotPath}.");
                change(slot.gameObject);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        /// <summary>The slot where it can be changed with Undo: a slot inside a prefab file, or one whose children belong to a prefab, is opened
        /// in Prefab Mode, the way you would edit it yourself. Saving follows Prefab Mode's own Auto Save.</summary>
        public static GameObject Editable(GameObject slot)
        {
            for (int hops = 0; slot != null && hops < 8; hops++)
            {
                string asset = null;
                Transform root = null;
                if (EditorUtility.IsPersistent(slot))
                {
                    asset = AssetDatabase.GetAssetPath(slot);
                    root = slot.transform.root;
                }
                else if (Locked(slot) is string locked)
                {
                    asset = locked;
                    root = PrefabUtility.GetNearestPrefabInstanceRoot(slot).transform;
                }
                if (asset == null) return slot;
                var path = AnimationUtility.CalculateTransformPath(slot.transform, root);
                var stage = PrefabStageUtility.OpenPrefab(asset);
                if (stage == null) return null;
                var found = path.Length == 0 ? stage.prefabContentsRoot.transform : stage.prefabContentsRoot.transform.Find(path);
                slot = found != null ? found.gameObject : null;
            }
            return slot;
        }

        // ---- the steps, with or without Undo ----

        static int Begin(string name, bool undo)
        {
            if (!undo) return 0;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return Undo.GetCurrentGroup();
        }

        static Transform NewParked(Transform slot, bool undo, string name)
        {
            var go = new GameObject(Parked) { layer = slot.gameObject.layer };
            if (go.scene != slot.gameObject.scene) SceneManager.MoveGameObjectToScene(go, slot.gameObject.scene);
            if (slot is RectTransform)
            {
                // Children laid out against the slot's rect keep that layout: the holder fills it exactly.
                var r = go.AddComponent<RectTransform>();
                r.SetParent(slot, false);
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
                r.offsetMin = r.offsetMax = Vector2.zero;
                r.pivot = ((RectTransform)slot).pivot;
            }
            else go.transform.SetParent(slot, false);
            go.SetActive(false);
            if (undo) Undo.RegisterCreatedObjectUndo(go, name);
            return go.transform;
        }

        // Keeps the local values exactly as they were: the holder sits at the slot's origin, so nothing moves on screen.
        static void Move(Transform c, Transform parent, bool undo, string name)
        {
            var p = c.localPosition;
            var r = c.localRotation;
            var s = c.localScale;
            if (undo)
            {
                Undo.SetTransformParent(c, parent, name);
                Undo.RecordObject(c, name);
            }
            else c.SetParent(parent, false);
            c.localPosition = p;
            c.localRotation = r;
            c.localScale = s;
        }

        static void Destroy(GameObject go, bool undo)
        {
            if (undo) Undo.DestroyObjectImmediate(go);
            else Object.DestroyImmediate(go);
        }

        // The effect is made for the card's centre and size, where the VFX node is. A moved or scaled slot is undone for it.
        static void AtCardCentre(Transform placed, Transform slot)
        {
            var vfx = slot.parent;
            if (vfx == null || (slot.localPosition == Vector3.zero && slot.localRotation == Quaternion.identity && slot.localScale == Vector3.one)) return;
            placed.SetPositionAndRotation(vfx.position, vfx.rotation);
            var a = vfx.lossyScale;
            var b = slot.lossyScale;
            float Ratio(float x, float y) => Mathf.Approximately(y, 0) ? 1 : x / y;
            placed.localScale = new Vector3(Ratio(a.x, b.x), Ratio(a.y, b.y), Ratio(a.z, b.z));
        }

        // Card motion plays on activation, as Bind Card Motion does, so placing an effect stays one step. Needs Timeline.
        static void BindCardMotion(GameObject placed)
        {
            var binder = Type.GetType("BaseGameLab.Vfx.Editor.VfxCardMotion, BaseGameLab.Vfx.Editor.Timeline");
            if (binder == null) return;
            if (binder.GetMethod("Problem").Invoke(null, new object[] { placed }) != null) return;
            binder.GetMethod("Bind").Invoke(null, new object[] { placed });
        }
    }

    /// <summary>BaSe VFX > Use as Slot: lists the slots of the selected card. Unity's menus are fixed when scripts compile, so the slot
    /// names live in this small window rather than in the menu itself.</summary>
    internal sealed class VfxSlotWindow : EditorWindow
    {
        GameObject card, effect;
        Vector2 scroll;

        [MenuItem("GameObject/BaSe VFX/Use as Slot...", false, 10)]
        [MenuItem("Assets/BaSe VFX/Use as Slot...", false, 20)]
        static void Open()
        {
            var w = GetWindow<VfxSlotWindow>(true, "Use as Slot", true);
            w.minSize = new Vector2(320, 180);
            w.FromSelection();
        }

        const string RestoreMenu = "GameObject/BaSe VFX/Restore Original";

        // The selected slot, or the slot above a selected placed effect.
        static GameObject SelectedFilledSlot()
        {
            var go = Selection.activeGameObject;
            if (go != null && !VfxSlot.Holds(go) && VfxSlot.IsOurs(go) && go.transform.parent != null) go = go.transform.parent.gameObject;
            return VfxSlot.Holds(go) ? go : null;
        }

        [MenuItem(RestoreMenu, true)]
        static bool CanRestore() => SelectedFilledSlot() != null;

        [MenuItem(RestoreMenu, false, 11)]
        static void RestoreSelected()
        {
            var slot = VfxSlot.Editable(SelectedFilledSlot());
            if (slot != null) VfxSlot.Restore(slot);
        }

        void FromSelection()
        {
            foreach (var o in Selection.objects)
            {
                var e = VfxSlot.EffectOf(o);
                if (e != null) effect = e;
                // A placed effect also names its card; an effect file does not.
                if (o is GameObject go && (e == null || !EditorUtility.IsPersistent(go)) && VfxSlot.FindVfxNode(go) != null) card = go;
            }
        }

        void OnSelectionChange()
        {
            FromSelection();
            Repaint();
        }

        void OnGUI()
        {
            card = (GameObject)EditorGUILayout.ObjectField("Card", card, typeof(GameObject), true);
            effect = (GameObject)EditorGUILayout.ObjectField("Effect", effect, typeof(GameObject), false);
            if (effect != null && !VfxSlot.IsEffect(effect)) EditorGUILayout.HelpBox("Pick an effect: a .vfxnative file from VFX Studio.", MessageType.Warning);
            if (card == null)
            {
                EditorGUILayout.HelpBox("Select a card, in the scene or as a prefab, and an effect file. The slots under its VFX node appear here.", MessageType.Info);
                return;
            }
            var slots = VfxSlot.Slots(card);
            if (slots.Length == 0)
            {
                EditorGUILayout.HelpBox($"{card.name} has no {VfxSlot.VfxNode} node with slots under it.", MessageType.Warning);
                return;
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var slot in slots)
            {
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(!VfxSlot.IsEffect(effect)))
                    if (GUILayout.Button($"Use as {slot.name}")) Do(slot, s => Selection.activeGameObject = VfxSlot.Use(s, effect));
                if (VfxSlot.Holds(slot) && GUILayout.Button("Restore original", GUILayout.Width(120))) Do(slot, s => VfxSlot.Restore(s));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        // After OnGUI, because opening Prefab Mode and creating objects inside a GUI pass breaks its layout.
        void Do(GameObject slot, Action<GameObject> change)
        {
            EditorApplication.delayCall += () =>
            {
                var editable = VfxSlot.Editable(slot);
                if (editable == null) return;
                try { change(editable); }
                catch (InvalidOperationException e) { EditorUtility.DisplayDialog("Use as Slot", e.Message, "OK"); }
                card = editable;
                Repaint();
            };
        }
    }
}
