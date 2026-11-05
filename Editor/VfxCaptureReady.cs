using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>
    /// Shown when a capture is saved. Unlike a dialog it stays open, so the studio, the file's folder and the path can all be opened
    /// from it, one after another, while the file is dragged from the folder onto the studio page.
    /// </summary>
    public sealed class VfxCaptureReady : EditorWindow
    {
        [SerializeField] string path, card, slot, url;
        [SerializeField] int frames, parts;
        [SerializeField] float sizeMb;
        [SerializeField] List<string> notes = new List<string>();

        static string Folder => Application.platform == RuntimePlatform.OSXEditor ? "Finder" : "Explorer";

        internal static VfxCaptureReady Show(string path, string card, string slot, int frames, int parts, float sizeMb, List<string> notes, string url)
        {
            var w = GetWindow<VfxCaptureReady>(true, "Capture ready", true);
            w.path = path; w.card = card; w.slot = slot; w.frames = frames; w.parts = parts; w.sizeMb = sizeMb; w.url = url;
            w.notes = new List<string>(notes);
            w.minSize = new Vector2(420, 220);
            w.Show();
            w.Focus();
            return w;
        }

        void OnGUI()
        {
            var wrap = new GUIStyle(EditorStyles.label) { wordWrap = true };
            EditorGUILayout.LabelField($"{card} / {slot}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"{Path.GetFileName(path)}: {frames} frames, {parts} parts, {sizeMb:0.0} MB", wrap);
            EditorGUILayout.SelectableLabel(path, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (notes.Count > 0)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(notes.Count == 1 ? "The studio will note:" : $"The studio will note {notes.Count} things:", EditorStyles.miniBoldLabel);
                foreach (var n in notes) EditorGUILayout.LabelField("• " + n, wrap);
            }
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox($"Drag the file from {Folder} onto the VFX Studio page. The card opens there with effects already playing on it.", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open VFX Studio")) Application.OpenURL(url);
                using (new EditorGUI.DisabledScope(!File.Exists(path)))
                    if (GUILayout.Button($"Show in {Folder}")) EditorUtility.RevealInFinder(path);
                if (GUILayout.Button("Copy path")) EditorGUIUtility.systemCopyBuffer = path;
            }
            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Close", GUILayout.Width(80))) Close();
            }
        }
    }
}
