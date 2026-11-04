using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace BaseGameLab.Vfx.Editor
{
    [CustomEditor(typeof(VfxImporter))]
    internal sealed class VfxImporterEditor : ScriptedImporterEditor
    {
        public override void OnInspectorGUI()
        {
            var path = ((AssetImporter)target).assetPath;
            var note = Note(path);
            if (note != null)
            {
                var style = new GUIStyle(EditorStyles.helpBox) { wordWrap = true, fontSize = 12 };
                EditorGUILayout.LabelField(note.text, style);
            }
            serializedObject.Update();
            EditorGUILayout.LabelField("Adjust", EditorStyles.boldLabel);
            foreach (var name in new[] { "speed", "size", "tint", "intensity" })
                EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space();
            // Delayed: creating assets inside an inspector's GUI pass breaks its layout.
            if (GUILayout.Button("Make Editable"))
                EditorApplication.delayCall += () => VfxMakeEditable.Export(path);
            if (GUILayout.Button("Project Settings"))
                EditorApplication.delayCall += VfxNativeSettings.OpenOrCreate;
            ApplyRevertGUI();
        }

        static TextAsset Note(string path)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is TextAsset t) return t;
            return null;
        }
    }
}
