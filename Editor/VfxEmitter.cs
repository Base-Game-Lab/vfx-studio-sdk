using UnityEngine;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>One emitter layer as a Particle System: few particles, billboards, local simulation.</summary>
    internal sealed class VfxEmitter
    {
        readonly VfxBuilder b;
        readonly J l;
        ParticleSystem ps;

        public VfxEmitter(VfxBuilder builder, J layer) { b = builder; l = layer; }

        static ParticleSystem.MinMaxCurve Range(J r, float scale = 1)
        {
            float a = r[0].F() * scale, c = r[1].F() * scale;
            return new ParticleSystem.MinMaxCurve(Mathf.Min(a, c), Mathf.Max(a, c));
        }

        public void Build()
        {
            var id = l["id"].S();
            var go = b.Child(id, l["pos"].V2);
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Main();
            Emission();
            Shape();
            Velocity();
            Forces();
            Rotation();
            Over();
            Renderer(id);
        }

        void Main()
        {
            var sim = l["sim"];
            var start = l["start"];
            var main = ps.main;
            main.duration = b.Duration;
            main.loop = b.Looping;
            main.playOnAwake = true;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = sim["space"].S("local") == "world" ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.maxParticles = sim["maxParticles"].I(100);
            main.startLifetime = Range(start["life"]);
            main.startSpeed = start["speed"].Exists ? Range(start["speed"], b.Units(1)) : new ParticleSystem.MinMaxCurve(0);
            main.gravityModifier = 0;

            float aspect = start["aspect"].F(1);
            if (aspect != 1)
            {
                main.startSize3D = true;
                main.startSizeX = Range(start["size"], b.Units(1));
                main.startSizeY = Range(start["size"], b.Units(1) * aspect);
                main.startSizeZ = 1;
            }
            else main.startSize = Range(start["size"], b.Units(1));

            var colors = start["colors"];
            if (RampColoured) main.startColor = Color.white;
            else if (colors.Count == 1) main.startColor = VfxConvert.Hex(colors[0].S());
            else if (colors.Count > 1) main.startColor = PickOne(colors);

            var seed = sim["seed"];
            ps.useAutoRandomSeed = !seed.Exists;
            if (seed.Exists) ps.randomSeed = (uint)seed.I();
        }

        // A file from before game.light: an additive system's colour lives in its ramp.
        bool RampColoured => l["material"]["blend"].S() == "add" && !b.Light.Straight;

        // That ramp holds one colour; listed colours are close tints, so their mean stands for them.
        Color MeanColour()
        {
            var colors = l["start"]["colors"];
            if (colors.Count == 0) return Color.white;
            var sum = Color.clear;
            for (int i = 0; i < colors.Count; i++) sum += VfxConvert.Hex(colors[i].S());
            return sum / colors.Count;
        }

        float[] Alphas()
        {
            var over = l["over"]["color"];
            if (!over.Exists) return new[] { 1f };
            var g = VfxConvert.ToGradient(b.Gradient(over.S()));
            var a = new float[33];
            for (int i = 0; i < a.Length; i++) a[i] = g.Evaluate(i / 32f).a;
            return a;
        }

        // RandomColor over a stepped gradient picks exactly one of the listed colours.
        static ParticleSystem.MinMaxGradient PickOne(J colors)
        {
            int n = colors.Count;
            var keys = new GradientColorKey[n];
            for (int i = 0; i < n; i++) keys[i] = new GradientColorKey(VfxConvert.Hex(colors[i].S()), (i + 1f) / n);
            var g = new Gradient { mode = GradientMode.Fixed };
            g.SetKeys(keys, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
        }

        void Emission()
        {
            var emit = l["emit"];
            var e = ps.emission;
            e.enabled = true;
            e.rateOverTime = emit["rate"].Exists ? Rate(emit["rate"].S()) : new ParticleSystem.MinMaxCurve(0);
            var bursts = emit["bursts"];
            if (bursts.Count == 0) return;
            var list = new ParticleSystem.Burst[bursts.Count];
            for (int i = 0; i < list.Length; i++) list[i] = new ParticleSystem.Burst(bursts[i]["t"].F(), (short)bursts[i]["n"].I());
            e.SetBursts(list);
        }

        // A system's curves run on 0..1 of its duration, and a scalar multiplies them.
        ParticleSystem.MinMaxCurve Rate(string curveId)
        {
            var c = b.Curve(curveId);
            float peak = 0.0001f;
            foreach (var v in c["v"].Items) peak = Mathf.Max(peak, v.F());
            float d = b.Duration, p = peak;
            var keys = VfxConvert.ToCurve(c, null, 1, d).keys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].time /= d;
                keys[i].value /= p;
                keys[i].inTangent *= d / p;
                keys[i].outTangent *= d / p;
            }
            return new ParticleSystem.MinMaxCurve(p, new AnimationCurve(keys));
        }

        void Shape()
        {
            var s = l["emit"]["shape"];
            var shape = ps.shape;
            shape.enabled = true;
            shape.rotation = Vector3.zero;
            shape.arc = 360;
            switch (s["type"].S())
            {
                case "rect":
                    shape.shapeType = ParticleSystemShapeType.Rectangle;
                    shape.scale = new Vector3(b.Units(s["w"].F()), b.Units(s["h"].F()), 1);
                    break;
                case "ring":
                    float inner = s["r"][0].F(), outer = Mathf.Max(s["r"][1].F(), 0.0001f);
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = b.Units(outer);
                    shape.radiusThickness = (outer - inner) / outer;
                    break;
                default:
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radius = 0.0001f;
                    shape.radiusThickness = 1;
                    break;
            }
        }

        // `vel` picks a velocity per particle on plate axes, which only Velocity over Lifetime can do.
        void Velocity()
        {
            var vel = l["start"]["vel"];
            float drag = l["sim"]["drag"].F();
            if (!vel.Exists && drag <= 0) return;
            var v = ps.velocityOverLifetime;
            v.enabled = true;
            v.space = ParticleSystemSimulationSpace.Local;
            v.x = vel.Exists ? Range(vel["x"], b.Units(1)) : new ParticleSystem.MinMaxCurve(0, 0);
            v.y = vel.Exists ? Range(vel["y"], -b.Units(1)) : new ParticleSystem.MinMaxCurve(0, 0);
            v.z = new ParticleSystem.MinMaxCurve(0, 0);
            if (drag > 0) v.speedModifier = new ParticleSystem.MinMaxCurve(1, DragCurve(-drag));
        }

        // Unity's Limit Velocity drag is not exponential and fights Velocity over Lifetime (measured), so drag is a speed
        // modifier, which scales every velocity the system has. Curves run on life, so they use the mean life.
        AnimationCurve DragCurve(float rate)
        {
            var life = l["start"]["life"];
            float k = rate * (life[0].F() + life[1].F()) / 2;
            const int steps = 7;
            var keys = new Keyframe[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float u = i / (float)steps, value = Mathf.Exp(k * u), slope = k * value;
                keys[i] = new Keyframe(u, value, slope, slope);
            }
            return new AnimationCurve(keys);
        }

        void Forces()
        {
            var g = l["sim"]["gravity"];
            if (!g.Exists) return;
            var f = ps.forceOverLifetime;
            f.enabled = true;
            f.space = ParticleSystemSimulationSpace.Local;
            float x = b.Units(g[0].F()), y = -b.Units(g[1].F()), drag = l["sim"]["drag"].F();
            if (drag > 0)
            {
                // The speed modifier also scales what the force has built up. Growing the force at the same rate
                // gives the studio's path exactly: dv/dt = g - drag * v.
                var grow = DragCurve(drag);
                f.x = new ParticleSystem.MinMaxCurve(x, grow);
                f.y = new ParticleSystem.MinMaxCurve(y, grow);
                f.z = new ParticleSystem.MinMaxCurve(0, grow);
            }
            else
            {
                f.x = new ParticleSystem.MinMaxCurve(x);
                f.y = new ParticleSystem.MinMaxCurve(y);
                f.z = new ParticleSystem.MinMaxCurve(0);
            }
        }

        void Rotation()
        {
            var start = l["start"];
            var main = ps.main;
            var flutter = start["flutter"];
            var spin = start["spin"];
            // Screen clockwise is negative around Unity's z, and the rot range keeps its width either way.
            if (start["rot"].Exists && !flutter.Exists) main.startRotation = Range(start["rot"], -Mathf.Deg2Rad);
            if (flutter.Exists)
            {
                main.startRotation3D = true;
                main.startRotationX = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                main.startRotationY = 0;
                main.startRotationZ = start["rot"].Exists ? Range(start["rot"], -Mathf.Deg2Rad) : new ParticleSystem.MinMaxCurve(0);
            }
            if (!spin.Exists && !flutter.Exists) return;
            var r = ps.rotationOverLifetime;
            r.enabled = true;
            r.separateAxes = flutter.Exists;
            if (flutter.Exists)
            {
                r.x = Range(flutter, Mathf.Deg2Rad);
                r.y = new ParticleSystem.MinMaxCurve(0, 0);
                r.z = spin.Exists ? Range(spin, -Mathf.Deg2Rad) : new ParticleSystem.MinMaxCurve(0, 0);
            }
            else r.z = Range(spin, -Mathf.Deg2Rad);
        }

        void Over()
        {
            var over = l["over"];
            if (over["color"].Exists)
            {
                var c = ps.colorOverLifetime;
                c.enabled = true;
                var g = b.Gradient(over["color"].S());
                c.color = new ParticleSystem.MinMaxGradient(RampColoured ? VfxConvert.LightFade(g, b.Light) : VfxConvert.ToGradient(g));
            }
            if (over["size"].Exists)
            {
                var s = ps.sizeOverLifetime;
                s.enabled = true;
                s.size = new ParticleSystem.MinMaxCurve(1, VfxConvert.ToCurve(b.Curve(over["size"].S())));
            }
            var noise = over["noise"];
            if (noise.Exists)
            {
                var n = ps.noise;
                n.enabled = true;
                n.quality = ParticleSystemNoiseQuality.Medium;
                n.damping = false;
                n.strength = b.Units(noise["strength"].F());
                n.frequency = 1f / Mathf.Max(b.Units(noise["scale"].F()), 0.0001f);
            }
        }

        void Renderer(string id)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = b.Material(id, l["material"], l["sort"].S(), Alphas(), MeanColour());
            r.sortingLayerName = b.SortingLayer;
            r.sortingOrder = b.NextOrder(l["sort"].S());
            r.maxParticleSize = 5;
            var stretch = l["over"]["stretch"];
            if (stretch.Exists)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.lengthScale = stretch["min"].F(1);
                r.velocityScale = stretch["speed"].F() / 2;
            }
            else
            {
                r.renderMode = ParticleSystemRenderMode.Billboard;
                r.alignment = ParticleSystemRenderSpace.View;
            }
        }
    }
}
