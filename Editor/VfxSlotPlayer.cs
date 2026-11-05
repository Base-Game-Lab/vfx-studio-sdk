using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>
    /// A window with one Fire button per slot of each card in the open scenes, to see effects play in Play mode without the game's code.
    /// Fire does what the game is likely to do to a slot: turns it off and on again (which starts our effects and anything that plays
    /// on awake) and then plays its particle systems (which starts slots the game drives with Play()). It works in Play mode only, so
    /// it never changes the saved scene.
    /// </summary>
    public sealed class VfxSlotPlayer : EditorWindow
    {
        const string Menu = "Window/BaSe VFX/Slot Player";
        Vector2 scroll;
        string filter = "";

        [MenuItem(Menu)]
        [MenuItem("GameObject/BaSe VFX/Slot Player", false, 30)]
        public static void Open() => GetWindow<VfxSlotPlayer>("Slot Player").Show();

        void OnEnable() => EditorApplication.playModeStateChanged += Repaint;
        void OnDisable() => EditorApplication.playModeStateChanged -= Repaint;
        void Repaint(PlayModeStateChange _) => Repaint();
        void OnInspectorUpdate() { if (Application.isPlaying) Repaint(); }

        /// <summary>Every card's VFX node in the loaded scenes, inactive ones included.</summary>
        internal static List<Transform> VfxNodes()
        {
            var nodes = new List<Transform>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (t.name == VfxSlot.VfxNode && t.childCount > 0) nodes.Add(t);
            }
            return nodes;
        }

        /// <summary>Starts a slot the way the game would: off and on again, then its particle systems played.</summary>
        internal static void Fire(GameObject slot)
        {
            slot.SetActive(false);
            slot.SetActive(true);
            foreach (var ps in slot.GetComponentsInChildren<ParticleSystem>(false))
            {
                if (UnderAnother(ps, slot.transform)) continue; // Play(true) on the outer one starts it
                ps.Clear(true);
                ps.Play(true);
            }
        }

        static bool UnderAnother(ParticleSystem ps, Transform slot)
        {
            if (ps.transform == slot) return false;
            for (var p = ps.transform.parent; p != null; p = p.parent)
            {
                if (p.GetComponent<ParticleSystem>() != null) return true;
                if (p == slot) break;
            }
            return false;
        }

        static string Note(Transform slot)
        {
            var ours = slot.Cast<Transform>().FirstOrDefault(c => VfxSlot.IsOurs(c.gameObject));
            var state = slot.gameObject.activeSelf ? "on" : "off";
            return ours != null ? $"{state}, ours: {ours.name}" : state;
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(Application.isPlaying
                ? "Fire turns a slot off and on and plays its particles, as the game would. A slot can still show nothing when the game also sets its colours or turns on its parts from code."
                : "Enter Play mode to fire slots. Firing in Edit mode would change your scene.", MessageType.None);
            if (!Application.isPlaying && GUILayout.Button("Enter Play mode")) EditorApplication.EnterPlaymode();
            filter = EditorGUILayout.TextField("Card filter", filter);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var vfx in VfxNodes())
            {
                var card = VfxCapture.CardOf(vfx.GetChild(0).gameObject);
                var label = card != null ? card.name : vfx.parent != null ? vfx.parent.name : vfx.name;
                if (filter.Length > 0 && label.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                EditorGUILayout.Space(4);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
                    if (GUILayout.Button("Select", GUILayout.Width(60))) { Selection.activeTransform = card != null ? card : vfx; EditorGUIUtility.PingObject(Selection.activeObject); }
                }
                foreach (Transform slot in vfx)
                {
                    if (slot.name == VfxSlot.Parked) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(slot.name, Note(slot));
                        using (new EditorGUI.DisabledScope(!Application.isPlaying))
                        {
                            if (GUILayout.Button("Fire", GUILayout.Width(60))) Fire(slot.gameObject);
                            if (GUILayout.Button("Off", GUILayout.Width(40))) slot.gameObject.SetActive(false);
                        }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
