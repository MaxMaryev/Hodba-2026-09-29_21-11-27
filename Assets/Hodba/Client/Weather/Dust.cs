using UnityEngine;
using UnityEngine.Rendering;

namespace Hodba.Client
{
    /// <summary>
    /// Пылинки в воздухе вокруг глаз и пепельные полосы, стелющиеся у ног в порыв.
    /// Против низкого солнца пылинки светятся — шейдер Hodba/Dust.
    /// </summary>
    public sealed class Dust
    {
        readonly ParticleSystem _motes;
        readonly ParticleSystem _drift;
        ParticleSystem.Particle[] _buffer = new ParticleSystem.Particle[0];

        public Dust(FieldConfig config, FloatingOrigin origin)
        {
            _motes = CreateMotes(config);
            _drift = CreateDrift(config);
            origin.Shifted += d => { Shift(_motes, d); Shift(_drift, d); };
        }

        public void Tick(Camera camera, Wind wind, FieldConfig config, float groundY)
        {
            var eye = camera.transform.position;

            _motes.transform.position = eye;
            var mv = _motes.velocityOverLifetime;
            // Все три кривые обязаны быть в одном режиме — поэтому везде «между двумя».
            mv.x = new ParticleSystem.MinMaxCurve(wind.Velocity.x * 0.3f, wind.Velocity.x * 0.4f);
            mv.z = new ParticleSystem.MinMaxCurve(wind.Velocity.z * 0.3f, wind.Velocity.z * 0.4f);
            mv.y = new ParticleSystem.MinMaxCurve(-0.02f, 0.05f);

            // Полосы рождаются с наветренной стороны и проносятся мимо.
            var upwind = -wind.Velocity.normalized * 8f;
            _drift.transform.position = new Vector3(eye.x + upwind.x, groundY + 0.05f, eye.z + upwind.z);
            var dv = _drift.velocityOverLifetime;
            dv.x = new ParticleSystem.MinMaxCurve(wind.Velocity.x * 0.9f, wind.Velocity.x * 1.2f);
            dv.z = new ParticleSystem.MinMaxCurve(wind.Velocity.z * 0.9f, wind.Velocity.z * 1.2f);
            dv.y = new ParticleSystem.MinMaxCurve(0f, 0.12f);
            var de = _drift.emission;
            de.rateOverTime = config.driftRate * wind.Gust * wind.Gust * wind.Strength;
        }

        ParticleSystem CreateMotes(FieldConfig config)
        {
            var ps = NewSystem("Dust Motes", config.dustMaterial, ParticleSystemRenderMode.Billboard);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.045f);
            main.startColor = new Color(1f, 1f, 1f, 0.35f);
            main.maxParticles = Mathf.Max(10, config.dustCount);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = ps.emission;
            em.rateOverTime = config.dustCount / 13f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(config.dustBox, 10f, config.dustBox);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.3f;
            noise.scrollSpeed = 0.2f;

            FadeInOut(ps, 0.15f, 0.8f);
            ps.Play();
            return ps;
        }

        ParticleSystem CreateDrift(FieldConfig config)
        {
            var ps = NewSystem("Ash Drift", config.driftMaterial != null ? config.driftMaterial : config.dustMaterial,
                ParticleSystemRenderMode.Stretch);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.velocityScale = 0.18f;
            r.lengthScale = 2.5f;

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.startColor = new Color(1f, 1f, 1f, 0.12f);
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = ps.emission;
            em.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(28f, 0.15f, 28f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;

            FadeInOut(ps, 0.2f, 0.7f);
            ps.Play();
            return ps;
        }

        static ParticleSystem NewSystem(string name, Material material, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = mode;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.None;
            return ps;
        }

        static void FadeInOut(ParticleSystem ps, float fadeIn, float fadeOut)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(1f, fadeOut), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        void Shift(ParticleSystem ps, Vector3 delta)
        {
            int n = ps.particleCount;
            if (_buffer.Length < n) _buffer = new ParticleSystem.Particle[n];
            n = ps.GetParticles(_buffer, n);
            for (int i = 0; i < n; i++) _buffer[i].position += delta;
            ps.SetParticles(_buffer, n);
        }
    }
}
