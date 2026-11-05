using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>
    /// BaSe VFX > Send to VFX Studio: one card moment as a .vfxcapture file (format/CAPTURE.md) that VFX Studio opens as a ready project.
    /// Everything happens in the editor, in a hidden preview scene: the card is cloned, its clip sampled frame by frame, its particles
    /// simulated with fixed seeds, and each image rendered twice, over black and over white, so its transparency is exact for any shader.
    /// Nothing in the open scene or prefab changes.
    /// </summary>
    public static class VfxCapture
    {
        public const string Extension = "vfxcapture";
        public const string Version = "0.1";
        const string Menu = "GameObject/BaSe VFX/Send to VFX Studio...";
        const float FrameShare = 0.6f; // the card's frame is this share of the plate's width

        public sealed class Options
        {
            public int width = 720, height = 1280;
            public int fps; // 0: 30, or 60 when the clip has keys closer than 1/30 s
            public int frameScale = 2; // the moving frames at 1/frameScale of the plate; the before and after stills stay full size
            public float maxSeconds = 4;
            public bool background = true; // from an open scene, what its camera sees behind the card
            /// <summary>Told what the capture is doing and how far along it is (0..1): the board, each part, each frame.</summary>
            public Action<string, float> progress;
        }

        // ---- menu ----

        const string FolderPref = "BaseGameLab.Vfx.CaptureFolder";
        /// <summary>Where Open VFX Studio goes; a team on its own studio sets this EditorPrefs key.</summary>
        public const string StudioPref = "BaseGameLab.Vfx.StudioUrl";
        const string StudioDefault = "https://portal.basegamelab.com/tools/vfx-studio/";

        // always on: with the wrong thing selected, the item says why instead of greying out with no reason
        [MenuItem(Menu, false, 12)]
        static void Send()
        {
            var slot = Selection.activeGameObject;
            var problem = Problem(slot);
            if (problem != null) { EditorUtility.DisplayDialog("Send to VFX Studio", problem, "OK"); return; }
            var card = CardOf(slot);
            var name = $"{Safe(card.name)}_{Safe(slot.name)}.{Extension}";
            var folder = EditorPrefs.GetString(FolderPref, "");
            if (!Directory.Exists(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var path = EditorUtility.SaveFilePanel("Send to VFX Studio", folder, name, Extension);
            if (string.IsNullOrEmpty(path)) return;
            EditorPrefs.SetString(FolderPref, Path.GetDirectoryName(path));
            Result m;
            var title = $"Send to VFX Studio: {card.name}";
            try
            {
                EditorUtility.DisplayProgressBar(title, "Starting", 0);
                m = Capture(slot, path, new Options { progress = (what, at) => EditorUtility.DisplayProgressBar(title, what, at) });
            }
            catch (InvalidOperationException e) { EditorUtility.ClearProgressBar(); EditorUtility.DisplayDialog("Send to VFX Studio", e.Message, "OK"); return; }
            finally { EditorUtility.ClearProgressBar(); }
            var notes = m.unseen.Count == 0 ? "" : m.unseen.Count == 1 ? " The studio shows 1 note about it." : $" The studio shows {m.unseen.Count} notes about it.";
            var size = new FileInfo(path).Length / (1024f * 1024f);
            Debug.Log($"Captured {slot.name}: {m.frames} frames, {m.parts} parts, {size:0.0} MB.{notes} Drop {Path.GetFileName(path)} on VFX Studio.");
            var url = EditorPrefs.GetString(StudioPref, StudioDefault);
            var choice = EditorUtility.DisplayDialogComplex("Captured " + card.name,
                $"{Path.GetFileName(path)} is ready: {m.frames} frames, {m.parts} parts, {size:0.0} MB.{notes}\n\nDrop it on VFX Studio to choose an effect for this slot.",
                "Open VFX Studio", "Done", Application.platform == RuntimePlatform.OSXEditor ? "Show in Finder" : "Show in Explorer");
            if (choice == 0) { EditorUtility.RevealInFinder(path); Application.OpenURL(url); }
            else if (choice == 2) EditorUtility.RevealInFinder(path);
        }

        static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());

        /// <summary>This package's version, as Package Manager reports it.</summary>
        public static string PackageVersion => UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VfxCapture).Assembly)?.version ?? "unknown";

        /// <summary>Null when the object is a slot that can be captured, otherwise why not, in one sentence.</summary>
        public static string Problem(GameObject slot)
        {
            if (slot == null) return "Select a slot under a card's VFX node, such as DefaultOnRevealVFX.";
            if (EditorUtility.IsPersistent(slot)) return "Open the card prefab, or use a copy of it in a scene, and pick the slot there.";
            var parent = slot.transform.parent;
            if (parent == null || parent.name != VfxSlot.VfxNode) return $"{slot.name} is not a slot: pick a child of a card's {VfxSlot.VfxNode} node.";
            if (slot.name == VfxSlot.Parked) return $"{VfxSlot.Parked} holds a slot's original children; pick the slot above it.";
            if (CardOf(slot) == null) return $"{slot.name} is not inside a card.";
            return null;
        }

        /// <summary>The card a slot belongs to: the nearest object above its VFX node with an Animator, else with a SortingGroup, else the
        /// VFX node's grandparent.</summary>
        public static Transform CardOf(GameObject slot)
        {
            var vfx = slot != null ? slot.transform.parent : null;
            if (vfx == null || vfx.parent == null) return null;
            for (var t = vfx.parent; t != null; t = t.parent) if (t.GetComponent<Animator>() != null) return t;
            for (var t = vfx.parent; t != null; t = t.parent) if (t.GetComponent<SortingGroup>() != null) return t;
            return vfx.parent.parent != null ? vfx.parent.parent : vfx.parent;
        }

        // ---- the capture ----

        public sealed class Result
        {
            public int frames, parts;
            public bool background;
            public List<string> unseen = new List<string>();
            public string manifest;
        }

        sealed class Image
        {
            public string file;
            public byte[] png;
        }

        /// <summary>Captures the slot's moment and writes the .vfxcapture file. Throws InvalidOperationException with a sentence for the user.</summary>
        public static Result Capture(GameObject slot, string outPath, Options o = null)
        {
            o ??= new Options();
            var problem = Problem(slot);
            if (problem != null) throw new InvalidOperationException(problem);
            var c = new Capturer(slot, o);
            var (manifest, images) = c.Run();
            WriteZip(outPath, manifest, images);
            return new Result { frames = c.FrameCount, parts = c.PartCount, background = c.HasBackground, unseen = c.Unseen.Select(u => u.what).ToList(), manifest = manifest };
        }

        /// <summary>The card as the open scene's camera draws it, through the capture's crop, as an opaque PNG. A check: the capture's
        /// background with parts/card.png over it should give this image.</summary>
        public static byte[] RenderInScene(GameObject slot, Options o = null)
        {
            var problem = Problem(slot);
            if (problem != null) throw new InvalidOperationException(problem);
            return new Capturer(slot, o ?? new Options()).InScene();
        }

        static void WriteZip(string path, string manifest, List<Image> images)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var stamp = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero); // fixed, so two captures of the same card are the same bytes
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
            void Add(string name, byte[] bytes, CompressionLevel level)
            {
                var e = zip.CreateEntry(name, level);
                e.LastWriteTime = stamp;
                using var s = e.Open();
                s.Write(bytes, 0, bytes.Length);
            }
            Add("manifest.json", Encoding.UTF8.GetBytes(manifest), CompressionLevel.Optimal);
            foreach (var im in images) Add(im.file, im.png, CompressionLevel.NoCompression); // PNG is compressed already
        }

        sealed class Gap { public string what, why; }

        sealed class Capturer
        {
            readonly GameObject slot;
            readonly Options o;
            readonly Transform card, vfx, space, slotT;
            readonly string slotPath; // from the card
            public int FrameCount, PartCount;
            public bool HasBackground;
            public readonly List<Gap> Unseen = new List<Gap>();
            readonly bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Matrix4x4 proj, view;
            public int Unique;
            readonly List<string> frameFiles = new List<string>();
            Vector3 camPos;
            Quaternion camRot;
            bool ortho;
            float orthoSize;
            Camera sceneCam;
            Rect viewport;
            RenderTexture rt;
            Texture2D read;

            public Capturer(GameObject slot, Options o)
            {
                this.slot = slot;
                this.o = o;
                slotT = slot.transform;
                vfx = slotT.parent;
                space = vfx.parent;
                card = CardOf(slot);
                slotPath = AnimationUtility.CalculateTransformPath(slotT, card);
            }

            public (string, List<Image>) Run()
            {
                var images = new List<Image>();
                var stage = PrefabStageUtility.GetPrefabStage(slot);
                bool inPrefab = stage != null;
                var mainParts = FindChild(card, "MainComponents");
                var parts = mainParts != null ? mainParts.Cast<Transform>().Where(t => t.gameObject.activeSelf).ToList() : new List<Transform>();
                var frame = FramePart(parts);

                // geometry at rest, on the original card: nothing is changed on it
                var cardBounds = BoundsOf(card, skip: vfx);
                var frameBounds = frame != null ? BoundsOf(frame, null) : cardBounds;
                if (frameBounds.size.x <= 0) throw new InvalidOperationException($"{card.name} draws nothing that the capture can frame: its parts have no renderers.");
                float anchorWidth = WidthIn(space, frameBounds);

                sceneCam = inPrefab ? null : SceneCamera(card.gameObject.scene);
                Frame(frameBounds);

                rt = new RenderTexture(o.width, o.height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
                rt.Create();
                read = new Texture2D(o.width, o.height, TextureFormat.RGBAHalf, false, true);
                var preview = EditorSceneManager.NewPreviewScene();
                var made = new List<Object>();
                try
                {
                    // background: the scene itself, with this card hidden by a flag that is never saved
                    if (sceneCam != null && o.background)
                    {
                        o.progress?.Invoke("The board behind the card", 0.02f);
                        images.Add(new Image { file = "background.png", png = RenderBackground() });
                        HasBackground = true;
                    }

                    var clip = FindClip(out var candidates);
                    float slotOn = SlotOn(clip);
                    float duration = Duration(clip, slotOn);
                    if (o.fps <= 0) o.fps = clip != null && KeySpacing(clip) < 1f / 30 - 1e-4f ? 60 : 30;
                    int frames = Mathf.Max(1, Mathf.RoundToInt(duration * o.fps));
                    FrameCount = frames;
                    int iPre = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(0, slotOn - 1f / o.fps) * o.fps), 0, frames - 1), iPost = frames - 1;

                    // parts at rest, one at a time, then the whole card with its VFX node off
                    var restClone = Clone(preview, made);
                    var cvfx = restClone.Find(Rel(vfx));
                    cvfx.gameObject.SetActive(false);
                    var partInfo = new List<string>();
                    var cparts = parts.Select(p => restClone.Find(Rel(p))).ToList();
                    for (int i = 0; i < parts.Count; i++)
                    {
                        o.progress?.Invoke($"Part {i + 1} of {parts.Count}: {parts[i].name}", 0.05f + 0.15f * i / parts.Count);
                        for (int j = 0; j < cparts.Count; j++) cparts[j].gameObject.SetActive(j == i);
                        var cparts0 = mainParts != null ? restClone.Find(Rel(mainParts)) : null;
                        var restore = Hide(restClone, t => (cparts0 == null || !t.IsChildOf(cparts0)) && !t.IsChildOf(cvfx));
                        var (png, box) = RenderMatte(preview);
                        restore();
                        var file = $"parts/{i:D2}_{Safe(parts[i].name)}.png";
                        images.Add(new Image { file = file, png = png });
                        var rs = parts[i].GetComponentsInChildren<Renderer>(false);
                        partInfo.Add(Jw.O(("name", Jw.S(parts[i].name)), ("path", Jw.S(Rel(parts[i]))), ("file", Jw.S(file)), ("box", Jw.Box(box)),
                            ("order", rs.Length > 0 ? Jw.A(rs.Min(r => r.sortingOrder), rs.Max(r => r.sortingOrder)) : "null"),
                            ("queue", rs.Length > 0 ? Jw.A(rs.Min(QueueOf), rs.Max(QueueOf)) : "null")));
                    }
                    PartCount = parts.Count;
                    foreach (var cp in cparts) cp.gameObject.SetActive(true);
                    var (cardPng, cardBox) = RenderMatte(preview);
                    images.Add(new Image { file = "parts/card.png", png = cardPng });
                    Object.DestroyImmediate(restClone.gameObject);

                    // the moment, on a fixed clock
                    var clone = Clone(preview, made);
                    var cslot = clone.Find(slotPath);
                    var systems = clone.GetComponentsInChildren<ParticleSystem>(true).Where(ps => ps.transform.parent == null || ps.transform.parent.GetComponentInParent<ParticleSystem>(true) == null).ToList();
                    foreach (var ps in clone.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        ps.useAutoRandomSeed = false;
                        ps.randomSeed = (uint)StableHash(AnimationUtility.CalculateTransformPath(ps.transform, clone));
                    }
                    var animators = cslot.GetComponentsInChildren<Animator>(true).Select(a => (a, clip: DefaultClip(a))).Where(x => x.clip != null).ToList();
                    var directors = cslot.GetComponentsInChildren<PlayableDirector>(true).Where(d => d.playableAsset != null).ToList();
                    bool slotStartsOff = !slotT.gameObject.activeSelf;
                    var started = new HashSet<ParticleSystem>();
                    float dt = 1f / o.fps;
                    byte[] lastPng = null;
                    for (int i = 0; i < frames; i++)
                    {
                        o.progress?.Invoke($"Frame {i + 1} of {frames}", 0.2f + 0.78f * i / frames);
                        float t = i * dt;
                        if (clip != null) clip.SampleAnimation(clone.gameObject, Mathf.Min(t, clip.length));
                        if (t + 1e-5f >= slotOn) cslot.gameObject.SetActive(true);
                        else if (slotStartsOff && clip == null) cslot.gameObject.SetActive(false);
                        float local = t - slotOn;
                        foreach (var (a, ac) in animators)
                            if (local >= 0 && a.gameObject.activeInHierarchy) ac.SampleAnimation(a.gameObject, ac.isLooping ? Mathf.Repeat(local, ac.length) : Mathf.Min(local, ac.length));
                        foreach (var d in directors)
                            if (local >= 0 && d.gameObject.activeInHierarchy) { d.time = Math.Min(local, d.duration); d.Evaluate(); }
                        foreach (var ps in systems)
                        {
                            if (!ps.gameObject.activeInHierarchy) { started.Remove(ps); continue; }
                            if (started.Add(ps)) ps.Simulate(0, true, true, false); // its own start: the first frame it is on
                            else ps.Simulate(dt, true, false, false);
                        }
                        var (px, _) = Matte(preview);
                        if (i == iPre) images.Add(new Image { file = "stills/pre.png", png = Png(o.width, o.height, px) });
                        if (i == iPost) images.Add(new Image { file = "stills/post.png", png = Png(o.width, o.height, px) });
                        var png = Png(o.width / o.frameScale, o.height / o.frameScale, Shrink(px, o.frameScale));
                        // a frame the same as the one before is stored once: the clock points at the first copy
                        if (lastPng != null && png.AsSpan().SequenceEqual(lastPng)) { frameFiles.Add(frameFiles[frameFiles.Count - 1]); continue; }
                        lastPng = png;
                        frameFiles.Add($"frames/{i:D4}.png");
                        images.Add(new Image { file = frameFiles[frameFiles.Count - 1], png = png });
                    }
                    Unique = frameFiles.Distinct().Count();
                    Object.DestroyImmediate(clone.gameObject);

                    Notice(candidates, clip, parts);
                    if (frames > 1 && Unique == 1)
                        Unseen.Insert(0, new Gap { what = "no motion captured", why = "every frame is the same: nothing the capture ran moved or drew anything new, so this is a still, not a moment" });
                    var manifest = Manifest(stage, clip, slotOn, duration, frames, iPre, anchorWidth, frame, frameBounds, cardBox, partInfo);
                    return (manifest, images);
                }
                finally
                {
                    foreach (var m in made) if (m != null) Object.DestroyImmediate(m);
                    EditorSceneManager.ClosePreviewScene(preview);
                    rt.Release();
                    Object.DestroyImmediate(rt);
                    Object.DestroyImmediate(read);
                }
            }

            public byte[] InScene()
            {
                var mainParts = FindChild(card, "MainComponents");
                var parts = mainParts != null ? mainParts.Cast<Transform>().Where(t => t.gameObject.activeSelf).ToList() : new List<Transform>();
                var frame = FramePart(parts);
                var frameBounds = frame != null ? BoundsOf(frame, null) : BoundsOf(card, vfx);
                sceneCam = SceneCamera(card.gameObject.scene);
                if (sceneCam == null) throw new InvalidOperationException("The card's scene has no camera.");
                Frame(frameBounds);
                rt = new RenderTexture(o.width, o.height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
                rt.Create();
                read = new Texture2D(o.width, o.height, TextureFormat.RGBAHalf, false, true);
                var cam = NewCamera(default, null);
                Action restoreVfx = null;
                try
                {
                    restoreVfx = Hide(vfx, _ => true);
                    cam.clearFlags = sceneCam.clearFlags;
                    cam.backgroundColor = sceneCam.backgroundColor;
                    var px = Render(cam);
                    var out32 = px.Select(c => new Color32(Enc(c.r), Enc(c.g), Enc(c.b), 255)).ToArray();
                    return Png(new Texture2D(o.width, o.height, TextureFormat.RGBA32, false, false), out32);
                }
                finally
                {
                    restoreVfx?.Invoke();
                    Object.DestroyImmediate(cam.gameObject);
                    rt.Release();
                    Object.DestroyImmediate(rt);
                    Object.DestroyImmediate(read);
                }
            }

            // ---- camera ----

            static Camera SceneCamera(Scene scene)
            {
                if (!scene.IsValid()) return null;
                return scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(false))
                    .Where(cam => cam.enabled && cam.targetTexture == null).OrderByDescending(cam => cam.depth).FirstOrDefault();
            }

            /// <summary>The scene camera cropped around the card's frame, or an orthographic camera on the card.</summary>
            void Frame(Bounds frame)
            {
                float aspect = o.width / (float)o.height;
                if (sceneCam != null)
                {
                    camPos = sceneCam.transform.position;
                    camRot = sceneCam.transform.rotation;
                    var corners = Corners(frame).Select(p => sceneCam.WorldToViewportPoint(p)).ToArray();
                    if (corners.Any(p => p.z <= 0)) { sceneCam = null; Frame(frame); return; }
                    float x0 = corners.Min(p => p.x), x1 = corners.Max(p => p.x), y0 = corners.Min(p => p.y), y1 = corners.Max(p => p.y);
                    float vw = (x1 - x0) / FrameShare;
                    float vh = vw * sceneCam.aspect / aspect;
                    viewport = new Rect((x0 + x1) / 2 - vw / 2, (y0 + y1) / 2 - vh / 2, vw, vh);
                    var P = sceneCam.projectionMatrix;
                    // the crop as a projection: NDC of the crop's rectangle becomes -1..1
                    float sx = viewport.width, sy = viewport.height, cx = 2 * viewport.center.x - 1, cy = 2 * viewport.center.y - 1;
                    var crop = Matrix4x4.identity;
                    crop.m00 = 1 / sx; crop.m03 = -cx / sx;
                    crop.m11 = 1 / sy; crop.m13 = -cy / sy;
                    proj = crop * P;
                    ortho = sceneCam.orthographic;
                }
                else
                {
                    var up = space.up;
                    var fwd = space.forward;
                    camRot = Quaternion.LookRotation(fwd, up);
                    camPos = frame.center - fwd * 20;
                    float worldW = WidthIn(null, frame) / FrameShare;
                    orthoSize = worldW / aspect / 2;
                    proj = Matrix4x4.Ortho(-worldW / 2, worldW / 2, -orthoSize, orthoSize, 0.01f, 100);
                    ortho = true;
                }
                view = Matrix4x4.TRS(camPos, camRot, Vector3.one).inverse;
                view.m20 *= -1; view.m21 *= -1; view.m22 *= -1; view.m23 *= -1; // Unity's view space looks down -z
            }

            Camera NewCamera(Scene scene, List<Object> made)
            {
                var go = EditorUtility.CreateGameObjectWithHideFlags("VfxCapture Camera", HideFlags.HideAndDontSave, typeof(Camera));
                if (scene.IsValid()) SceneManager.MoveGameObjectToScene(go, scene);
                made?.Add(go);
                var cam = go.GetComponent<Camera>();
                if (sceneCam != null) cam.CopyFrom(sceneCam);
                cam.enabled = false;
                cam.transform.SetPositionAndRotation(camPos, camRot);
                cam.orthographic = ortho;
                if (ortho && sceneCam == null) cam.orthographicSize = orthoSize;
                cam.nearClipPlane = sceneCam != null ? sceneCam.nearClipPlane : 0.01f;
                cam.farClipPlane = sceneCam != null ? sceneCam.farClipPlane : 100;
                cam.projectionMatrix = proj;
                cam.allowHDR = true;
                cam.allowMSAA = false;
                cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor;
                if (scene.IsValid()) cam.scene = scene;
                return cam;
            }

            byte[] RenderBackground()
            {
                Action restoreCard = null;
                var cam = NewCamera(default, null);
                try
                {
                    restoreCard = Hide(card, _ => true); // the whole card: sprites, meshes, particles and its canvases' UI
                    var others = OtherCardsOver(card);
                    BackgroundHidden.AddRange(others.Select(t => PathInScene(t)));
                    var restoreOthers = others.Select(t => Hide(t, _ => true)).ToList();
                    restoreCard += () => { foreach (var r in restoreOthers) r(); };
                    cam.clearFlags = sceneCam.clearFlags;
                    cam.backgroundColor = sceneCam.backgroundColor;
                    var px = Render(cam);
                    var tex = new Texture2D(o.width, o.height, TextureFormat.RGBA32, false, false);
                    var out32 = new Color32[px.Length];
                    for (int i = 0; i < px.Length; i++)
                    {
                        var c = px[i];
                        out32[i] = new Color32(Enc(c.r), Enc(c.g), Enc(c.b), 255);
                    }
                    return Png(tex, out32);
                }
                finally
                {
                    restoreCard?.Invoke();
                    Object.DestroyImmediate(cam.gameObject);
                }
            }

            Color[] Render(Camera cam)
            {
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, o.width, o.height), 0, 0, false);
                read.Apply(false);
                RenderTexture.active = prev;
                return read.GetPixels();
            }

            /// <summary>The preview scene rendered over black and over white: cover is 1 - (white - black), light is the black render.</summary>
            (byte[] png, RectInt box) RenderMatte(Scene preview)
            {
                var (px, box) = Matte(preview);
                return (Png(o.width, o.height, px), box);
            }

            (Color32[] px, RectInt box) Matte(Scene preview)
            {
                var cam = NewCamera(preview, null);
                try
                {
                    cam.backgroundColor = new Color(0, 0, 0, 0);
                    var black = Render(cam);
                    cam.backgroundColor = Color.white;
                    var white = Render(cam);
                    var px = new Color32[black.Length];
                    int x0 = o.width, y0 = o.height, x1 = -1, y1 = -1;
                    for (int i = 0; i < px.Length; i++)
                    {
                        Color b = black[i], w = white[i];
                        float cover = 1 - Mathf.Clamp01(((w.r - b.r) + (w.g - b.g) + (w.b - b.b)) / 3);
                        var c = new Color32(Enc(b.r), Enc(b.g), Enc(b.b), (byte)Mathf.RoundToInt(cover * 255));
                        px[i] = c;
                        if (c.a > 0 || c.r > 0 || c.g > 0 || c.b > 0)
                        {
                            int x = i % o.width, y = o.height - 1 - i / o.width; // texture rows run up, plate rows down
                            if (x < x0) x0 = x;
                            if (x > x1) x1 = x;
                            if (y < y0) y0 = y;
                            if (y > y1) y1 = y;
                        }
                    }
                    var box = x1 < 0 ? new RectInt(0, 0, 0, 0) : new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
                    return (px, box);
                }
                finally { Object.DestroyImmediate(cam.gameObject); }
            }

            byte[] Png(int w, int h, Color32[] px) => Png(new Texture2D(w, h, TextureFormat.RGBA32, false, false), px);

            // a box filter on premultiplied pixels, k x k into one
            Color32[] Shrink(Color32[] px, int k)
            {
                if (k <= 1) return px;
                int w = o.width / k, h = o.height / k, n = k * k;
                var outPx = new Color32[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int r = 0, g = 0, b = 0, a = 0;
                        for (int dy = 0; dy < k; dy++)
                            for (int dx = 0; dx < k; dx++)
                            {
                                var c = px[(y * k + dy) * o.width + x * k + dx];
                                r += c.r; g += c.g; b += c.b; a += c.a;
                            }
                        outPx[y * w + x] = new Color32((byte)((r + n / 2) / n), (byte)((g + n / 2) / n), (byte)((b + n / 2) / n), (byte)((a + n / 2) / n));
                    }
                return outPx;
            }

            byte[] Png(Texture2D tex, Color32[] px)
            {
                try
                {
                    tex.SetPixels32(px);
                    tex.Apply(false);
                    return tex.EncodeToPNG();
                }
                finally { Object.DestroyImmediate(tex); }
            }

            public readonly List<string> BackgroundHidden = new List<string>();

            /// <summary>Other cards in the scene drawn where this one is, such as a copy saved in a board slot under it: hidden for the
            /// background only, so the empty plate has no card in it. A card is anything shaped like this one: an object holding the same
            /// path to MainComponents. Listed in the manifest (plate.backgroundHidden).</summary>
            List<Transform> OtherCardsOver(Transform me)
            {
                var parts = FindChild(me, "MainComponents");
                if (parts == null) return new List<Transform>();
                var rel = AnimationUtility.CalculateTransformPath(parts, me);
                int depth = rel.Split('/').Length;
                var mine = BoundsOf(me, vfx);
                var found = new List<Transform>();
                foreach (var root in me.gameObject.scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "MainComponents"))
                    {
                        var other = t;
                        for (int i = 0; i < depth && other != null; i++) other = other.parent;
                        if (other == null || other == me || me.IsChildOf(other) || other.IsChildOf(me) || other.Find(rel) != t) continue;
                        if (!other.gameObject.activeInHierarchy) continue;
                        var b = BoundsOf(other, null);
                        bool overlap = b.size != Vector3.zero && b.min.x < mine.max.x && b.max.x > mine.min.x && b.min.y < mine.max.y && b.max.y > mine.min.y;
                        if (overlap && !found.Any(f => other.IsChildOf(f))) found.Add(other);
                    }
                return found.OrderBy(t => PathInScene(t), StringComparer.Ordinal).ToList();
            }

            // light to stored value: sRGB in a Linear project, as stored in a Gamma one
            byte Enc(float v)
            {
                v = Mathf.Clamp01(v);
                if (linear) v = Mathf.LinearToGammaSpace(v);
                return (byte)Mathf.RoundToInt(v * 255);
            }

            // ---- the card ----

            Transform Clone(Scene preview, List<Object> made)
            {
                var copy = Object.Instantiate(card.gameObject);
                copy.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(copy, preview);
                copy.transform.SetPositionAndRotation(card.position, card.rotation);
                copy.transform.localScale = card.lossyScale;
                made.Add(copy);
                return copy.transform;
            }

            string Rel(Transform t) => AnimationUtility.CalculateTransformPath(t, card);

            /// <summary>Hides what draws under root and passes the filter, with flags Unity never saves: Renderer.forceRenderingOff for
            /// sprites, meshes and particles, CanvasRenderer.cull for uGUI and TextMeshPro on a canvas. Returns what puts it back.</summary>
            static Action Hide(Transform root, Func<Transform, bool> which)
            {
                var rs = root.GetComponentsInChildren<Renderer>(true).Where(r => !r.forceRenderingOff && which(r.transform)).ToList();
                var cs = root.GetComponentsInChildren<CanvasRenderer>(true).Where(c => !c.cull && which(c.transform)).ToList();
                foreach (var r in rs) r.forceRenderingOff = true;
                foreach (var c in cs) c.cull = true;
                return () =>
                {
                    foreach (var r in rs) if (r != null) r.forceRenderingOff = false;
                    foreach (var c in cs) if (c != null) c.cull = false;
                };
            }


            static Transform FindChild(Transform root, string name)
            {
                var q = new Queue<Transform>();
                q.Enqueue(root);
                while (q.Count > 0)
                {
                    var t = q.Dequeue();
                    if (t != root && t.name == name) return t;
                    foreach (Transform c in t) q.Enqueue(c);
                }
                return null;
            }

            static Transform FramePart(List<Transform> parts)
            {
                var named = parts.FirstOrDefault(p => p.name.IndexOf("Frame", StringComparison.OrdinalIgnoreCase) >= 0 && BoundsOf(p, null).size.x > 0);
                return named != null ? named : parts.Where(p => BoundsOf(p, null).size.x > 0).OrderByDescending(p => { var s = BoundsOf(p, null).size; return s.x * s.y; }).FirstOrDefault();
            }

            static Bounds BoundsOf(Transform root, Transform skip)
            {
                var rs = root.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && (skip == null || !r.transform.IsChildOf(skip)) && !(r is ParticleSystemRenderer)).ToList();
                if (rs.Count == 0) return new Bounds(root.position, Vector3.zero);
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                return b;
            }

            static IEnumerable<Vector3> Corners(Bounds b)
            {
                for (int i = 0; i < 8; i++)
                    yield return b.center + Vector3.Scale(b.extents, new Vector3((i & 1) * 2 - 1, (i >> 1 & 1) * 2 - 1, (i >> 2 & 1) * 2 - 1));
            }

            // a world box's width along the space's x axis, in that space's units (world units when space is null)
            float WidthIn(Transform s, Bounds b)
            {
                var axis = space.right;
                var xs = Corners(b).Select(p => Vector3.Dot(p, axis)).ToArray();
                float w = xs.Max() - xs.Min();
                return s == null ? w : w / Mathf.Max(1e-6f, s.lossyScale.x);
            }

            static int QueueOf(Renderer r) => r.sharedMaterial != null ? r.sharedMaterial.renderQueue : 0;

            static int StableHash(string s)
            {
                unchecked
                {
                    int h = 23;
                    foreach (char ch in s) h = h * 31 + ch;
                    return h & 0x7fffffff;
                }
            }

            // ---- timing ----

            /// <summary>The clip that plays the moment: one that turns the slot on or animates inside it, from the card's Animator. Its
            /// own Animator controller first, then every clip under Assets, in path order.</summary>
            AnimationClip FindClip(out List<string> candidates)
            {
                bool Binds(AnimationClip c) =>
                    AnimationUtility.GetCurveBindings(c).Concat(AnimationUtility.GetObjectReferenceCurveBindings(c))
                        .Any(b => b.path == slotPath || b.path.StartsWith(slotPath + "/", StringComparison.Ordinal));
                var found = new List<AnimationClip>();
                var animator = card.GetComponent<Animator>();
                if (animator != null && animator.runtimeAnimatorController != null)
                    found.AddRange(animator.runtimeAnimatorController.animationClips.Where(Binds).Distinct().OrderBy(c => c.name, StringComparer.Ordinal));
                if (found.Count == 0)
                    foreach (var path in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p, StringComparer.Ordinal))
                        foreach (var c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).OrderBy(c => c.name, StringComparer.Ordinal))
                            if (Binds(c)) found.Add(c);
                candidates = found.Select(c => $"{c.name} ({AssetDatabase.GetAssetPath(c)})").ToList();
                return found.FirstOrDefault();
            }

            static float KeySpacing(AnimationClip clip)
            {
                var keys = AnimationUtility.GetCurveBindings(clip).SelectMany(b => AnimationUtility.GetEditorCurve(clip, b).keys.Select(k => k.time)).Distinct().OrderBy(t => t).ToArray();
                float min = float.MaxValue;
                for (int i = 1; i < keys.Length; i++) min = Mathf.Min(min, keys[i] - keys[i - 1]);
                return min;
            }

            float SlotOn(AnimationClip clip)
            {
                if (clip == null) return 0;
                var b = AnimationUtility.GetCurveBindings(clip).FirstOrDefault(x => x.path == slotPath && x.propertyName == "m_IsActive");
                if (b.propertyName == null) return 0;
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                var on = curve.keys.FirstOrDefault(k => k.value > 0.5f);
                return curve.keys.Any(k => k.value > 0.5f) ? on.time : 0;
            }

            float Duration(AnimationClip clip, float slotOn)
            {
                float own = 0;
                // a looping system counts one loop; a child switched off inside the slot never plays with it
                foreach (var ps in slotT.GetComponentsInChildren<ParticleSystem>(true).Where(p => OnInSlot(p.transform)))
                    own = Mathf.Max(own, ps.main.duration + (ps.main.loop ? 0 : ps.main.startLifetime.constantMax));
                foreach (var a in slotT.GetComponentsInChildren<Animator>(true))
                {
                    var c = DefaultClip(a);
                    if (c != null) own = Mathf.Max(own, c.length);
                }
                foreach (var d in slotT.GetComponentsInChildren<PlayableDirector>(true)) if (d.playableAsset != null) own = Mathf.Max(own, (float)d.duration);
                float total = Mathf.Max(clip != null ? clip.length : 0, slotOn + own);
                if (total <= 0) total = 2;
                return Mathf.Min(total, o.maxSeconds);
            }

            bool OnInSlot(Transform t)
            {
                for (; t != null && t != slotT; t = t.parent) if (!t.gameObject.activeSelf) return false;
                return true;
            }

            static AnimationClip DefaultClip(Animator a)
            {
                if (a.runtimeAnimatorController is AnimatorController ac && ac.layers.Length > 0)
                {
                    var state = ac.layers[0].stateMachine.defaultState;
                    if (state != null && state.motion is AnimationClip c) return c;
                }
                return a.runtimeAnimatorController != null ? a.runtimeAnimatorController.animationClips.FirstOrDefault() : null;
            }

            // ---- what it could not see ----

            void Notice(List<string> candidates, AnimationClip clip, List<Transform> parts)
            {
                if (clip == null) Unseen.Add(new Gap { what = $"the clip that plays {slot.name}", why = "no AnimationClip under Assets turns the slot on or animates it; the capture runs the slot's own particles and animators from the start" });
                if (candidates.Count > 1) Unseen.Add(new Gap { what = $"{candidates.Count} clips play {slot.name}", why = $"the capture used the first, {candidates[0]}; the others: {string.Join(", ", candidates.Skip(1).Take(4))}" });
                if (parts.Count == 0) Unseen.Add(new Gap { what = "the card's parts", why = "the card has no MainComponents node, so the studio gets the whole card as one cut-out" });
                int missing = card.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
                if (missing > 0) Unseen.Add(new Gap { what = $"{missing} missing script{(missing == 1 ? "" : "s")} on the card", why = "Unity cannot load them in this project, so whatever they draw or start is not in the capture" });
                var scripts = card.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m != null && !(m.GetType().Namespace ?? "").StartsWith("UnityEngine", StringComparison.Ordinal) && !(m.GetType().Namespace ?? "").StartsWith("TMPro", StringComparison.Ordinal))
                    .GroupBy(m => m.GetType().FullName).OrderBy(g => g.Key, StringComparer.Ordinal);
                foreach (var g in scripts)
                {
                    var first = AnimationUtility.CalculateTransformPath(g.First().transform, card);
                    bool inSlot = g.Any(m => m.transform.IsChildOf(slotT));
                    Unseen.Add(new Gap { what = $"{g.Key} on {(first.Length == 0 ? card.name : first)}{(g.Count() > 1 ? $" and {g.Count() - 1} more" : "")}", why = inSlot ? "a script in the slot: anything it spawns or plays at run time is not in the capture" : "a script; scripts do not run in a capture" });
                }
                foreach (var ps in slotT.GetComponentsInChildren<ParticleSystem>(true).Where(p => !p.main.playOnAwake))
                    Unseen.Add(new Gap { what = $"particle system {AnimationUtility.CalculateTransformPath(ps.transform, card)} does not play on awake", why = "something starts it at run time; the capture starts it with the slot" });
                foreach (var cv in card.GetComponentsInChildren<Canvas>(true).Where(x => x.isRootCanvas && x.renderMode == RenderMode.ScreenSpaceOverlay))
                    Unseen.Add(new Gap { what = $"overlay canvas {cv.name}", why = "a Screen Space Overlay canvas draws outside every camera" });
            }

            // ---- the manifest ----

            string Manifest(PrefabStage stage, AnimationClip clip, float slotOn, float duration, int frames, int iPre, float anchorWidth, Transform frame, Bounds frameBounds, RectInt cardBox, List<string> partInfo)
            {
                Vector2 Px(Vector3 world)
                {
                    var p = proj * view * new Vector4(world.x, world.y, world.z, 1);
                    var ndc = new Vector2(p.x / p.w, p.y / p.w);
                    return new Vector2((ndc.x + 1) / 2 * o.width, (1 - ndc.y) / 2 * o.height);
                }
                var fc = Corners(frameBounds).Select(Px).ToArray();
                var fbox = new Rect(fc.Min(p => p.x), fc.Min(p => p.y), fc.Max(p => p.x) - fc.Min(p => p.x), fc.Max(p => p.y) - fc.Min(p => p.y));
                float ppu = fbox.width / Mathf.Max(1e-6f, anchorWidth);
                var frameLocal = new Vector2(anchorWidth, WidthAlongUp(frameBounds) / Mathf.Max(1e-6f, space.lossyScale.y));

                var points = new List<string>();
                var tp = FindChild(card, "VFXTargetPoints");
                if (tp != null)
                    foreach (Transform p in tp)
                    {
                        var local = space.InverseTransformPoint(p.position);
                        points.Add(Jw.O(("name", Jw.S(p.name)), ("local", Jw.A(local.x, local.y)), ("px", Jw.A(Px(p.position).x, Px(p.position).y))));
                    }
                var slotLocal = space.InverseTransformPoint(slotT.position);

                // sorting and queues: the parts' range, and where the card's own effects sit below and above it
                var partRs = (FindChild(card, "MainComponents") ?? card).GetComponentsInChildren<Renderer>(true).Where(r => !r.transform.IsChildOf(vfx)).ToList();
                var cardRs = card.GetComponentsInChildren<Renderer>(true).Where(r => !r.transform.IsChildOf(vfx)).ToList();
                var fxRs = vfx.GetComponentsInChildren<Renderer>(true).ToList();
                int oMin = partRs.Count > 0 ? partRs.Min(r => r.sortingOrder) : 0, oMax = partRs.Count > 0 ? partRs.Max(r => r.sortingOrder) : 0;
                // behind goes under the card's own effects that sit below its parts (for example ShapeTrail -2, so -3)
                int allMin = Math.Min(cardRs.Count > 0 ? cardRs.Min(r => r.sortingOrder) : oMin, fxRs.Where(r => r.sortingOrder < oMin).Select(r => r.sortingOrder).DefaultIfEmpty(oMin).Min());
                int qMin = partRs.Count > 0 ? partRs.Min(QueueOf) : 3000, qMax = partRs.Count > 0 ? partRs.Max(QueueOf) : 3000;
                var fxBelow = fxRs.Where(r => QueueOf(r) <= qMax && QueueOf(r) > qMin).Select(QueueOf).ToList();
                var fxAbove = fxRs.Where(r => QueueOf(r) > qMax).Select(QueueOf).ToList();
                int qBehind = fxBelow.Count > 0 ? fxBelow.Min() : qMin + 1, qFront = fxAbove.Count > 0 ? fxAbove.Min() : qMax + 1;
                string layer = partRs.Count > 0 ? partRs.GroupBy(r => r.sortingLayerName).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key : "Default";
                var shader = fxRs.Where(r => r.sharedMaterial != null && r.sharedMaterial.shader != null).GroupBy(r => r.sharedMaterial.shader.name)
                    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key).FirstOrDefault();
                var queueParts = Jw.O(partRs.GroupBy(r => r.transform.IsChildOf(card) ? TopPart(r.transform) : r.name).OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => (g.Key, Jw.I(g.Min(QueueOf)))).ToArray());

                string screen = "null", screenFrom = "null";
                if (sceneCam != null)
                {
                    float d = Vector3.Dot(frameBounds.center - sceneCam.transform.position, sceneCam.transform.forward);
                    float h = sceneCam.orthographic ? 2 * sceneCam.orthographicSize : 2 * d * Mathf.Tan(sceneCam.fieldOfView * Mathf.Deg2Rad / 2);
                    float s = space.lossyScale.y;
                    var (aspect, aspectFrom) = TargetAspect();
                    screen = Jw.A(h * aspect / s, h / s);
                    screenFrom = Jw.S(aspectFrom);
                }

                // identity: the prefab that holds the slot, and where it is in the open scene or stage
                string prefab = "null";
                string stageName = stage != null ? "prefab" : "scene";
                if (stage != null)
                {
                    var root = stage.prefabContentsRoot.transform;
                    prefab = Jw.O(("guid", Jw.S(AssetDatabase.AssetPathToGUID(stage.assetPath))), ("path", Jw.S(stage.assetPath)), ("slot", Jw.S(AnimationUtility.CalculateTransformPath(slotT, root))));
                }
                else if (PrefabUtility.IsPartOfPrefabInstance(slot))
                {
                    var root = PrefabUtility.GetNearestPrefabInstanceRoot(slot);
                    var asset = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(slot);
                    prefab = Jw.O(("guid", Jw.S(AssetDatabase.AssetPathToGUID(asset))), ("path", Jw.S(asset)), ("slot", Jw.S(AnimationUtility.CalculateTransformPath(slotT, root.transform))));
                }
                string cardPath = PathInScene(card);

                string clipJson = "null";
                if (clip != null)
                {
                    var keys = AnimationUtility.GetCurveBindings(clip).SelectMany(b => AnimationUtility.GetEditorCurve(clip, b).keys.Select(k => k.time))
                        .Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip).SelectMany(b => AnimationUtility.GetObjectReferenceCurve(clip, b).Select(k => k.time)))
                        .Select(t => (float)Math.Round(t, 4)).Distinct().OrderBy(t => t).ToArray();
                    clipJson = Jw.O(("name", Jw.S(clip.name)), ("asset", Jw.S(AssetDatabase.GetAssetPath(clip))), ("length", Jw.F(clip.length)), ("keys", Jw.A(keys)));
                }

                var camera = sceneCam != null
                    ? Jw.O(("kind", Jw.S("scene")), ("from", Jw.S(PathInScene(sceneCam.transform))), ("viewport", Jw.A(viewport.x, viewport.y, viewport.width, viewport.height)))
                    : Jw.O(("kind", Jw.S("card")), ("from", "null"), ("viewport", "null"));
                var center = Px(space.position);
                
                var pipeline = GraphicsSettings.currentRenderPipeline;

                return Jw.O(
                    ("format", Jw.S("vfx-capture")), ("version", Jw.S(Version)),
                    ("made", Jw.O(("package", Jw.S(PackageVersion)), ("unity", Jw.S(Application.unityVersion)),
                        ("pipeline", Jw.S(pipeline != null ? pipeline.GetType().Name : "builtin")), ("renderer", Jw.S(RendererName(pipeline))))),
                    ("colour", Jw.O(("space", Jw.S(QualitySettings.activeColorSpace.ToString())), ("light", Jw.S(linear ? "linear" : "gamma")), ("alpha", Jw.S("premultiplied")))),
                    ("source", Jw.O(("scene", stage != null ? "null" : Jw.S(card.gameObject.scene.path)), ("card", Jw.S(cardPath)), ("slot", Jw.S(slotPath)), ("prefab", prefab), ("stage", Jw.S(stageName)), ("project", Jw.S(PlayerSettings.productGUID.ToString())))),
                    ("plate", Jw.O(("size", Jw.A(o.width, o.height)), ("pixelsPerUnit", Jw.F(ppu)), ("background", HasBackground ? Jw.S("background.png") : "null"),
                        ("backgroundHidden", "[" + string.Join(", ", BackgroundHidden.Select(Jw.S)) + "]"), ("camera", camera))),
                    ("clock", Jw.O(("fps", Jw.I(o.fps)), ("frames", Jw.I(frames)), ("duration", Jw.F(frames / (float)o.fps)), ("size", Jw.A(o.width / o.frameScale, o.height / o.frameScale)),
                        ("unique", Jw.I(Unique)), ("images", "[" + string.Join(", ", frameFiles.Select(Jw.S)) + "]"))),
                    ("timing", Jw.O(("tPre", Jw.F(iPre / (float)o.fps)), ("tPost", Jw.F((frames - 1) / (float)o.fps)), ("slotOn", Jw.F(slotOn)), ("clip", clipJson),
                        ("stills", Jw.O(("pre", Jw.S("stills/pre.png")), ("post", Jw.S("stills/post.png")))))),
                    ("layout", Jw.O(
                        ("space", Jw.S(space.name)), ("anchorWidth", Jw.F(anchorWidth)), ("center", Jw.A(center.x, center.y)),
                        ("card", Jw.O(("box", Jw.Box(cardBox)), ("file", Jw.S("parts/card.png")))),
                        ("frame", Jw.O(("box", Jw.A(fbox.x, fbox.y, fbox.width, fbox.height)), ("size", Jw.A(frameLocal.x, frameLocal.y)), ("part", frame != null ? Jw.S(frame.name) : "null"))),
                        ("slot", Jw.O(("local", Jw.A(slotLocal.x, slotLocal.y)), ("px", Jw.A(Px(slotT.position).x, Px(slotT.position).y)))),
                        ("points", Jw.L(points)),
                        ("screenSize", screen), ("screenFrom", screenFrom),
                        ("sorting", Jw.O(("layer", Jw.S(layer)), ("parts", Jw.A(oMin, oMax)), ("behind", Jw.I(Mathf.Min(oMin, allMin) - 1)), ("front", Jw.I(oMax + 1)), ("from", Jw.S("one step under every card renderer and own effect below the parts; one step over the parts in front")))),
                        ("queues", Jw.O(("parts", queueParts), ("behind", Jw.I(qBehind)), ("front", Jw.I(qFront)),
                            ("from", Jw.S(fxBelow.Count > 0 && fxAbove.Count > 0 ? "the card's own effects" : "one step past the parts' range on a side with no effect of the card's own")))),
                        ("shader", shader != null ? Jw.S(shader) : "null"))),
                    ("parts", Jw.L(partInfo)),
                    ("unseen", Jw.L(Unseen.Select(u => Jw.O(("what", Jw.S(u.what)), ("why", Jw.S(u.why))))))
                ) + "\n";
            }

            string TopPart(Transform t)
            {
                var parts = FindChild(card, "MainComponents");
                if (parts == null || !t.IsChildOf(parts) || t == parts) return t.name;
                while (t.parent != parts) t = t.parent;
                return t.name;
            }

            float WidthAlongUp(Bounds b)
            {
                var axis = space.up;
                var ys = Corners(b).Select(p => Vector3.Dot(p, axis)).ToArray();
                return ys.Max() - ys.Min();
            }

            /// <summary>The screen the game runs on: the Game view's chosen size when one is open, else the default orientation with a
            /// portrait phone's 9:16. Never the scene camera's own aspect, which in batch mode is 4:3.</summary>
            static (float, string) TargetAspect()
            {
                var gameViews = Resources.FindObjectsOfTypeAll<EditorWindow>().Where(w => w.GetType().Name == "GameView").ToArray();
                if (gameViews.Length > 0)
                {
                    var size = Handles.GetMainGameViewSize();
                    if (size.x > 0 && size.y > 0) return (size.x / size.y, $"the Game view, {size.x:0}x{size.y:0}");
                }
                var landscape = PlayerSettings.defaultInterfaceOrientation == UIOrientation.LandscapeLeft || PlayerSettings.defaultInterfaceOrientation == UIOrientation.LandscapeRight;
                return landscape ? (16f / 9, "Player Settings: landscape, 16:9") : (9f / 16, $"Player Settings: {PlayerSettings.defaultInterfaceOrientation}, a portrait phone's 9:16");
            }

            static string PathInScene(Transform t) => t.parent == null ? t.name : PathInScene(t.parent) + "/" + t.name;

            static string RendererName(RenderPipelineAsset asset)
            {
                if (asset == null) return "builtin";
                try
                {
                    var so = new SerializedObject(asset);
                    var list = so.FindProperty("m_RendererDataList");
                    int index = so.FindProperty("m_DefaultRendererIndex")?.intValue ?? 0;
                    if (list != null && index < list.arraySize && list.GetArrayElementAtIndex(index).objectReferenceValue is Object r) return r.GetType().Name;
                }
                catch (Exception) { /* not URP */ }
                return asset.GetType().Name;
            }
        }

        /// <summary>A tiny JSON writer with a fixed key order and fixed number formatting, so the manifest is the same bytes every time.</summary>
        static class Jw
        {
            public static string S(string s)
            {
                var b = new StringBuilder("\"");
                foreach (char ch in s)
                {
                    if (ch == '"' || ch == '\\') b.Append('\\').Append(ch);
                    else if (ch < 0x20) b.Append("\\u").Append(((int)ch).ToString("x4"));
                    else b.Append(ch);
                }
                return b.Append('"').ToString();
            }
            public static string F(float v) => Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);
            public static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
            public static string A(params float[] v) => "[" + string.Join(", ", v.Select(F)) + "]";
            public static string A(params int[] v) => "[" + string.Join(", ", v.Select(I)) + "]";
            public static string Box(RectInt r) => A(r.x, r.y, r.width, r.height);
            public static string L(IEnumerable<string> items)
            {
                var list = items.ToList();
                return list.Count == 0 ? "[]" : "[\n" + string.Join(",\n", list.Select(x => Indent(x))) + "\n]";
            }
            public static string O(params (string key, string value)[] pairs) =>
                pairs.Length == 0 ? "{}" : "{\n" + string.Join(",\n", pairs.Select(p => Indent($"{S(p.key)}: {p.value}"))) + "\n}";
            static string Indent(string s) => " " + s.Replace("\n", "\n ");
        }
    }
}
