using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>Turns one parsed effect into sub-assets of the imported file: prefab, materials, masks, clips, controller.</summary>
    internal sealed class VfxBuilder
    {
        readonly AssetImportContext ctx;
        readonly J doc;
        readonly VfxNativeSettings settings;
        readonly VfxReport report;
        readonly Shader shader;

        readonly Dictionary<string, Texture2D> masks = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        readonly Dictionary<string, Texture2D> ramps = new Dictionary<string, Texture2D>();
        readonly VfxLight light;
        readonly VfxKnobs knobs;

        string effectName;
        float duration, unitsPerPx;
        Vector2 origin, plate;
        GameObject root;
        AnimationClip clip, motionClip;
        int orderBehindNext, orderFrontNext;

        public VfxBuilder(AssetImportContext ctx, J doc, VfxNativeSettings settings, Shader shader, VfxReport report, VfxKnobs knobs)
        {
            this.ctx = ctx;
            this.knobs = knobs;
            // Speed and tint are applied to the parsed file itself, before anything reads a time or a colour.
            VfxAdjust.Speed(doc, knobs.speed);
            VfxAdjust.Tint(doc, knobs.tint);
            this.doc = doc;
            this.settings = settings;
            this.shader = shader;
            this.report = report;
            light = new VfxLight(settings, doc);
            if (light.Straight && PlayerSettings.colorSpace != ColorSpace.Linear)
                report.Attention.Add("This project is in Gamma colour space and the effect was made for Linear, so its soft glows read weaker and its dims darker than in the studio.");
        }

        public void Build()
        {
            ReadSpace();
            foreach (var m in doc["assets"]["masks"].Pairs) AddMask(m.Key, m.Value);
            foreach (var l in doc["layers"].Items)
                if (l["material"]["blend"].S() != "add" && l["material"]["ramp"].Exists && !ramps.ContainsKey(l["material"]["ramp"].S()))
                    AddRamp(l["material"]["ramp"].S(), doc["gradients"][l["material"]["ramp"].S()]);

            root = new GameObject(effectName);
            clip = NewClip(effectName, "clip");
            OrderLayers();
            int emitters = 0, quads = 0;
            foreach (var layer in doc["layers"].Items)
            {
                switch (layer["kind"].S())
                {
                    case "emitter": new VfxEmitter(this, layer).Build(); emitters++; break;
                    case "quad": BuildQuad(layer); quads++; break;
                    case "fullscreen": BuildFullscreen(layer); quads++; break;
                    case "motion": BuildMotion(layer); break;
                    default:
                        report.Attention.Add($"Layer \"{layer["id"].S()}\" is a \"{layer["kind"].S()}\", which this package does not know. It was skipped.");
                        break;
                }
            }
            FinishAnimator();
            foreach (var d in doc["dropped"].Items)
            {
                var t = d["t"].Exists ? $" ({d["t"][0].F():0.##}s to {d["t"][1].F():0.##}s)" : "";
                report.Dropped.Add($"{d["what"].S()}{t}: {d["why"].S()}");
            }
            report.Built.Add($"{emitters} particle systems, {quads} sprites, plays when activated, {duration:0.##}s at 60 fps.");
            if (!knobs.IsDefault) report.Built.Add($"Adjusted in its import settings: {knobs.Describe()}.");
            ctx.AddObjectToAsset("prefab", root);
            ctx.SetMainObject(root);
        }

        void ReadSpace()
        {
            var e = doc["effect"];
            effectName = e["name"].S(report.Effect);
            duration = e["duration"].F(1);
            plate = doc["space"]["plate"].V2;
            var anchorId = doc["space"]["anchor"].S();
            foreach (var t in doc["targets"].Items)
            {
                if (t["id"].S() != anchorId) continue;
                var box = t["frame"].Exists ? t["frame"] : t["box"]; // the visible card, when box is a cut-out with margins
                origin = new Vector2(box[0].F() + box[2].F() / 2, box[1].F() + box[3].F() / 2);
                unitsPerPx = settings.anchorWidth / box[2].F(1);
                return;
            }
            throw new FormatException($"The anchor target \"{anchorId}\" is not in the file.");
        }

        // ---- shared pieces ------------------------------------------------

        /// <summary>Plate px to the prefab's local units, with the Size adjustment. Screen-wide layers and card motion use <see cref="ScreenUnits"/>.</summary>
        public float Units(float px) => px * unitsPerPx * knobs.size;
        /// <summary>Without the Size adjustment: a dim still covers the screen and the card still moves as authored.</summary>
        float ScreenUnits(float px) => px * unitsPerPx;
        public float Duration => duration;
        public bool Looping => doc["effect"]["loop"].B();
        public J Curve(string id) => doc["curves"][id];
        public J Gradient(string id) => doc["gradients"][id];
        public string SortingLayer => settings.sortingLayer;
        public VfxLight Light => light;
        public AnimationClip Clip => clip;
        public Texture2D Ramp(string id) => ramps[id];

        /// <summary>Plate px to prefab space: origin at the anchor's centre, y up.</summary>
        public Vector3 Place(Vector2 px) => new Vector3(Units(px.x - origin.x), -Units(px.y - origin.y), 0);

        public GameObject Child(string id, Vector2 platePos) => Child(id, Place(platePos));

        GameObject Child(string id, Vector3 local)
        {
            var go = new GameObject(id);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = local;
            return go;
        }

        /// <summary>Intensity scales the light a layer adds: the shader multiplies its final alpha by _Alpha, and an additive layer adds
        /// colour x alpha. An alpha-blended layer is at most fully opaque, so above 1 only additive layers get brighter.</summary>
        void Intensity(Material mat, string blend)
        {
            if (knobs.intensity == 1 || !mat.HasProperty("_Alpha")) return;
            mat.SetFloat("_Alpha", blend == "add" ? knobs.intensity : Mathf.Min(knobs.intensity, 1));
        }

        int Queue(string sort) => sort == "behind" ? settings.queueBehind : settings.queueFront;

        /// <summary>alphas is the layer's alpha through its life, flat the colour of an additive layer that has no ramp.</summary>
        public Material Material(string layerId, J m, string sort, float[] alphas, Color? flat)
        {
            // In a straight file an emitter's colour stays in its particles, as the studio draws it: a ramp would read that colour as
            // strength. With no ramp the shader does not clamp, so glow goes into the mask.
            bool coloured = light.Straight && flat != null;
            var mat = VfxMaterials.Create("mat_" + layerId, shader, m["blend"].S(), coloured ? GainedMask(m) : Mask(m["mask"].S()), Queue(sort));
            if (m["blend"].S() == "add" && !coloured) LightRamp(layerId, mat, m["ramp"].Exists ? VfxConvert.ToGradient(Gradient(m["ramp"].S())) : VfxConvert.Flat(flat ?? Color.white), alphas);
            else if (m["blend"].S() != "add" && m["ramp"].Exists) VfxMaterials.AddRamp(mat, ramps[m["ramp"].S()]);
            if (m["clip"].Exists) VfxMaterials.AddClip(mat, Mask(m["clip"].S()));
            if (m["glow"].F() > 0 && !coloured) VfxMaterials.AddGlow(mat, m["glow"].F());
            Intensity(mat, m["blend"].S());
            ctx.AddObjectToAsset("mat_" + layerId, mat);
            return mat;
        }

        void LightRamp(string layerId, Material mat, Gradient g, float[] alphas)
        {
            var t = VfxMaterials.LightRamp(g, light, alphas);
            ctx.AddObjectToAsset("ramp_" + layerId, t);
            VfxMaterials.AddRamp(mat, t);
        }

        // The alpha of a layer through its life, evenly in time.
        float[] Alphas(J track)
        {
            if (!track.Exists) return new[] { 1f };
            var c = VfxConvert.ToCurve(Curve(track.S()), null, 1, duration);
            var a = new float[121];
            for (int i = 0; i < a.Length; i++) a[i] = Mathf.Clamp01(c.Evaluate(duration * i / 120f));
            return a;
        }

        public int NextOrder(string sort) => sort == "behind" ? orderBehindNext++ : orderFrontNext++;

        void OrderLayers()
        {
            int behind = 0;
            foreach (var l in doc["layers"].Items)
                if (l["kind"].S() != "motion" && l["sort"].S() == "behind") behind++;
            orderBehindNext = settings.orderBehind - behind + 1;
            orderFrontNext = settings.orderFront;
        }

        Texture2D Mask(string id)
        {
            if (masks.TryGetValue(id, out var t)) return t;
            throw new FormatException($"The mask \"{id}\" is not in the file.");
        }

        Texture2D GainedMask(J material)
        {
            var id = material["mask"].S();
            float gain = 1 + material["glow"].F();
            if (gain <= 1) return Mask(id);
            var key = $"{id}_gain{gain:0.###}";
            if (masks.TryGetValue(key, out var t)) return t;
            t = VfxMasks.Decode(key, doc["assets"]["masks"][id], gain);
            VfxMasks.CompressFor(t, ctx.selectedBuildTarget);
            ctx.AddObjectToAsset("mask_" + key, t);
            masks[key] = t;
            return t;
        }

        void AddMask(string id, J m)
        {
            var t = VfxMasks.Decode(id, m);
            VfxMasks.CompressFor(t, ctx.selectedBuildTarget);
            ctx.AddObjectToAsset("mask_" + id, t);
            masks[id] = t;
        }

        void AddRamp(string id, J g)
        {
            var t = VfxMaterials.Ramp(id, VfxConvert.ToGradient(g));
            ctx.AddObjectToAsset("ramp_" + id, t);
            ramps[id] = t;
        }

        Sprite SpriteOf(string maskId, Texture2D t)
        {
            if (sprites.TryGetValue(maskId, out var s)) return s;
            s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), t.width, 0, SpriteMeshType.FullRect);
            s.name = "sprite_" + maskId;
            ctx.AddObjectToAsset("sprite_" + maskId, s);
            sprites[maskId] = s;
            return s;
        }

        Texture2D Fill()
        {
            const string id = "_fill";
            if (masks.TryGetValue(id, out var t)) return t;
            t = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "mask_fill", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            t.SetPixels32(px);
            t.Apply(false, false);
            ctx.AddObjectToAsset("mask_fill", t);
            masks[id] = t;
            return t;
        }

        // ---- quads --------------------------------------------------------

        void BuildQuad(J l)
        {
            var id = l["id"].S();
            var m = l["material"];
            var mask = Mask(m["mask"].S());
            var size = l["size"].V2;
            var go = Child(id, l["pos"].V2);
            var sr = AddSprite(go, SpriteOf(m["mask"].S(), mask), Material(id, m, l["sort"].S(), Alphas(l["tracks"]["alpha"]), null), l["sort"].S());
            // The sprite is 1 unit wide, and as tall as its texture's aspect.
            var scale = new Vector3(Units(size.x), Units(size.y) / ((float)mask.height / mask.width), 1);
            go.transform.localScale = scale;
            var tracks = l["tracks"];
            var rest = go.transform.localPosition;
            if (tracks["x"].Exists) Key(id, typeof(Transform), "m_LocalPosition.x", ToClip(tracks["x"], v => rest.x + Units(v), Units(1)));
            if (tracks["y"].Exists) Key(id, typeof(Transform), "m_LocalPosition.y", ToClip(tracks["y"], v => rest.y - Units(v), -Units(1)));
            if (tracks["scale"].Exists)
            {
                Key(id, typeof(Transform), "m_LocalScale.x", ToClip(tracks["scale"], v => v * scale.x, scale.x));
                Key(id, typeof(Transform), "m_LocalScale.y", ToClip(tracks["scale"], v => v * scale.y, scale.y));
            }
            if (tracks["rz"].Exists) Key(id, typeof(Transform), "localEulerAnglesRaw.z", ToClip(tracks["rz"], v => -v, -1));
            if (m["blend"].S() == "add" && !light.Straight) LightTrack(id, sr, tracks["alpha"]);
            else AlphaTrack(id, sr, tracks["alpha"], null);
            if (tracks["scrollU"].Exists)
            {
                Key(id, typeof(SpriteRenderer), "material._MainTex_ST.z", ToClip(tracks["scrollU"], v => -v, -1));
                // An animated vector replaces the whole property, so tiling must be keyed too or the mask collapses to a point.
                Key(id, typeof(SpriteRenderer), "material._MainTex_ST.x", AnimationCurve.Constant(0, duration, 1));
                Key(id, typeof(SpriteRenderer), "material._MainTex_ST.y", AnimationCurve.Constant(0, duration, 1));
                Key(id, typeof(SpriteRenderer), "material._MainTex_ST.w", AnimationCurve.Constant(0, duration, 0));
            }
        }

        void BuildFullscreen(J l)
        {
            var id = l["id"].S();
            var hasMask = l["mask"].Exists;
            var tex = hasMask ? Mask(l["mask"].S()) : Fill();
            var centre = plate / 2 - origin;
            var go = Child(id, new Vector3(ScreenUnits(centre.x), -ScreenUnits(centre.y), 0));
            var mat = VfxMaterials.Create("mat_" + id, shader, l["blend"].S(), tex, Queue(l["sort"].S()));
            bool add = l["blend"].S() == "add";
            if (add) LightRamp(id, mat, VfxConvert.Flat(VfxConvert.Hex(l["color"].S("#ffffff"))), Alphas(l["tracks"]["alpha"]));
            // A dim takes light away rather than adding it, so intensity leaves black layers alone.
            if (add || VfxConvert.Hex(l["color"].S("#000000")).maxColorComponent > 0) Intensity(mat, l["blend"].S());
            ctx.AddObjectToAsset("mat_" + id, mat);
            var sr = AddSprite(go, SpriteOf(hasMask ? l["mask"].S() : "_fill", tex), mat, l["sort"].S());
            sr.color = add ? Color.white : VfxConvert.Hex(l["color"].S("#000000"));
            // A flat colour runs far past the screen wherever the card is. A shaped mask is exactly the plate: any stretch moves its
            // shape, and 1.15 left a vignette at a third of its darkness at the plate's edge.
            float over = hasMask ? 1f : 4f;
            // Flat colour only: a shaped mask belongs to the frame the effect was made in, and its edges would land off screen.
            var screen = !hasMask && settings.screenSize.x > 0 ? settings.screenSize : new Vector2(ScreenUnits(plate.x), ScreenUnits(plate.y));
            go.transform.localScale = new Vector3(screen.x * over, screen.y * over, 1);
            if (add && !light.Straight) LightTrack(id, sr, l["tracks"]["alpha"]);
            else if (add || sr.color.maxColorComponent > 0) AlphaTrack(id, sr, l["tracks"]["alpha"], null);
            else
            {
                // The backdrop is the board as shown under the dims, so a dim behind the card darkens something brighter than that.
                float peak = Alphas(l["tracks"]["alpha"]).Max();
                float start = l["sort"].S() == "behind" ? 1 / Mathf.Max(1 - peak, 0.01f) : 1;
                AlphaTrack(id, sr, l["tracks"]["alpha"], a => light.Dark(a, start));
            }
        }

        SpriteRenderer AddSprite(GameObject go, Sprite sprite, Material mat, string sort)
        {
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sharedMaterial = mat;
            sr.sortingLayerName = settings.sortingLayer;
            sr.sortingOrder = NextOrder(sort);
            return sr;
        }

        void AlphaTrack(string id, SpriteRenderer sr, J track, Func<float, float> convert)
        {
            if (!track.Exists) return;
            var c = VfxConvert.ToCurve(Curve(track.S()), convert, 1, duration);
            Key(id, typeof(SpriteRenderer), "m_Color.a", c);
            var col = sr.color;
            col.a = c.Evaluate(0);
            sr.color = col;
        }

        // Alpha in both colour and alpha: see VfxLight.VertexChannel. A layer with no track stays white.
        void LightTrack(string id, SpriteRenderer sr, J track)
        {
            if (!track.Exists) return;
            AlphaTrack(id, sr, track, null);
            var rgb = VfxConvert.ToCurve(Curve(track.S()), light.VertexChannel, 1, duration);
            foreach (var channel in new[] { "m_Color.r", "m_Color.g", "m_Color.b" }) Key(id, typeof(SpriteRenderer), channel, rgb);
            float v = rgb.Evaluate(0);
            sr.color = new Color(v, v, v, sr.color.a);
        }

        // ---- motion -------------------------------------------------------

        void BuildMotion(J l)
        {
            var target = l["target"].S();
            var path = settings.PathOf(target);
            if (path == null)
            {
                path = target;
                report.Attention.Add($"Target \"{target}\" has no path in {VfxNativeSettings.AssetPath}, so its motion clip binds to \"{target}\". Add the path there.");
            }
            if (motionClip == null) motionClip = NewClip(effectName + " Motion", "motion");
            var t = l["tracks"];
            void Add(string property, AnimationCurve c) =>
                AnimationUtility.SetEditorCurve(motionClip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), c);

            if (t["x"].Exists) Add("m_LocalPosition.x", ToClip(t["x"], ScreenUnits, ScreenUnits(1)));
            if (t["y"].Exists) Add("m_LocalPosition.y", ToClip(t["y"], v => -ScreenUnits(v), -ScreenUnits(1)));
            if (t["scale"].Exists)
                foreach (var axis in new[] { "x", "y", "z" })
                    Add("m_LocalScale." + axis, ToClip(t["scale"]));
            if (t["rz"].Exists) Add("localEulerAnglesRaw.z", ToClip(t["rz"], v => -v, -1));
            if (t["rx"].Exists) Add("localEulerAnglesRaw.x", ToClip(t["rx"]));
            if (t["ry"].Exists) Add("localEulerAnglesRaw.y", ToClip(t["ry"]));
            report.Built.Add($"Motion clip \"{motionClip.name}\" moves \"{path}\". To play it on activation, select the effect under the card and use GameObject > BaSe VFX > Bind Card Motion.");
        }

        // ---- animation ----------------------------------------------------

        AnimationCurve ToClip(J track, Func<float, float> value = null, float slope = 1) =>
            VfxConvert.ToCurve(Curve(track.S()), value, slope, duration);

        void Key(string path, Type type, string property, AnimationCurve c) =>
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), c);

        AnimationClip NewClip(string name, string assetId)
        {
            var c = new AnimationClip { name = name, frameRate = 60 };
            var s = AnimationUtility.GetAnimationClipSettings(c);
            s.loopTime = Looping;
            s.stopTime = duration;
            AnimationUtility.SetAnimationClipSettings(c, s);
            ctx.AddObjectToAsset(assetId, c);
            return c;
        }

        void FinishAnimator()
        {
            var controller = new AnimatorController { name = effectName };
            controller.AddLayer("Base Layer");
            var machine = controller.layers[0].stateMachine;
            machine.name = "Base Layer";
            var state = machine.AddState("Play");
            state.motion = clip;
            machine.defaultState = state;
            ctx.AddObjectToAsset("controller", controller);
            ctx.AddObjectToAsset("machine", machine);
            ctx.AddObjectToAsset("state", state);

            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
        }
    }
}
