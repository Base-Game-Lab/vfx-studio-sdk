using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>
    /// Makes an effect under a card move the card when activated, with no script. An Animator cannot reach above itself, so
    /// the effect gets Unity's own PlayableDirector, whose Timeline drives the Animator above it. The binding is a reference,
    /// so it holds for an effect placed in the card prefab, not for one instantiated from code.
    /// </summary>
    public static class VfxCardMotion
    {
        const string Menu = "GameObject/BaSe VFX/Bind Card Motion";

        [MenuItem(Menu, true)]
        static bool Can() => Problem(Selection.activeGameObject) == null;

        [MenuItem(Menu)]
        static void Run()
        {
            var effect = Selection.activeGameObject;
            Bind(effect);
            Debug.Log($"{effect.name} now moves the card when it is activated.", effect);
        }

        static string SourcePath(GameObject effect)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(effect);
            return source != null ? AssetDatabase.GetAssetPath(source) : "";
        }

        static AnimationClip MotionClip(string path)
        {
            var parts = path.EndsWith(".prefab")
                ? AssetDatabase.FindAssets("t:AnimationClip", new[] { Path.GetDirectoryName(path) })
                    .Select(g => AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g))).ToArray()
                : AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().ToArray();
            return parts.FirstOrDefault(c => c.name.EndsWith(" Motion"));
        }

        /// <summary>Null when the object can be bound, otherwise why not.</summary>
        public static string Problem(GameObject effect)
        {
            if (effect == null) return "Nothing selected.";
            if (effect.transform.parent == null || effect.transform.parent.GetComponentInParent<Animator>() == null)
                return "The effect needs an Animator above it, on the card.";
            return MotionClip(SourcePath(effect)) == null ? "This effect has no card motion." : null;
        }

        /// <summary>The Animator the motion clip's paths start from: the nearest one above the effect that holds every path. A slot or
        /// part of the card can carry its own Animator; that one is skipped. With none holding the paths, the nearest Animator.</summary>
        public static Animator CardAnimator(GameObject effect, AnimationClip clip)
        {
            var paths = AnimationUtility.GetCurveBindings(clip).Select(b => b.path).Distinct().ToArray();
            for (var t = effect.transform.parent; t != null; t = t.parent)
            {
                var animator = t.GetComponent<Animator>();
                if (animator != null && paths.All(p => p.Length == 0 || t.Find(p) != null)) return animator;
            }
            return effect.transform.parent.GetComponentInParent<Animator>();
        }

        /// <summary>Adds the director to a placed effect and binds it to the Animator above. Safe to run again.</summary>
        public static PlayableDirector Bind(GameObject effect)
        {
            var problem = Problem(effect);
            if (problem != null) throw new System.InvalidOperationException(problem);

            var path = SourcePath(effect);
            var clip = MotionClip(path);
            var timelinePath = $"{Path.GetDirectoryName(path)}/{clip.name}.playable".Replace('\\', '/');
            var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(timelinePath);
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<TimelineAsset>();
                AssetDatabase.CreateAsset(timeline, timelinePath);
                var made = timeline.CreateTrack<AnimationTrack>(null, "Card");
                made.CreateClip(clip);
                AssetDatabase.SaveAssets();
            }

            var director = effect.GetComponent<PlayableDirector>();
            if (director == null) director = Undo.AddComponent<PlayableDirector>(effect);
            director.playableAsset = timeline;
            director.playOnAwake = true;
            director.extrapolationMode = clip.isLooping ? DirectorWrapMode.Loop : DirectorWrapMode.None; // a looping effect keeps the card moving with it
            var track = timeline.GetOutputTracks().OfType<AnimationTrack>().First();
            director.SetGenericBinding(track, CardAnimator(effect, clip));
            EditorUtility.SetDirty(director);
            return director;
        }
    }
}
