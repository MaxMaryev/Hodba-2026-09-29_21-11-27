using UnityEngine;
using UnityEngine.Rendering;

namespace Hodba.Client
{
    /// <summary>
    /// Пылинки в воздухе вокруг глаз и песчинки, прыгающие у ног, когда песок бежит (позёмка рисуется на земле, см. Saltation).
    /// Против низкого солнца пылинки светятся — шейдер Hodba/Dust. Вплотную к глазу частицы гаснут:
    /// пылинка в 20 см от глаза была бы огромным мягким пятном.
    /// </summary>
    public sealed class Dust
    {
        /// <summary>Самая крупная частица на экране, доля его высоты.</summary>
        const float MaxScreenSize = 0.004f;

        readonly ParticleSystem _motes;
        readonly ParticleSystem _spray;
        ParticleSystem.Particle[] _buffer = new ParticleSystem.Particle[0];

        public Dust(FieldConfig config, FloatingOrigin origin)
        {
            _motes = CreateMotes(config);
            _spray = CreateSpray(config);
            origin.Shifted += d => { Shift(_motes, d); Shift(_spray, d); };
        }

        /// <param name="looseness">Рыхлость земли под путником, 0..1.</param>
        public void Tick(Camera camera, Wind wind, FieldConfig config, float groundY, float looseness)
        {
            var eye = camera.transform.position;

            _motes.transform.position = eye;
            var mv = _motes.velocityOverLifetime;
            // Все три кривые обязаны быть в одном режиме — поэтому везде «между двумя».
            mv.x = new ParticleSystem.MinMaxCurve(wind.Velocity.x * 0.3f, wind.Velocity.x * 0.4f);
            mv.z = new ParticleSystem.MinMaxCurve(wind.Velocity.z * 0.3f, wind.Velocity.z * 0.4f);
            mv.y = new ParticleSystem.MinMaxCurve(-0.02f, 0.05f);

            // Песчинки рождаются чуть с наветренной стороны и прыгают по ветру — столько, сколько песка бежит по земле.
            var upwind = -wind.Velocity.normalized * 3f;
            _spray.transform.position = new Vector3(eye.x + upwind.x, groundY + 0.01f, eye.z + upwind.z);
            var sv = _spray.velocityOverLifetime;
            sv.x = new ParticleSystem.MinMaxCurve(wind.Velocity.x * 1.0f, wind.Velocity.x * 1.4f);
            sv.z = new ParticleSystem.MinMaxCurve(wind.Velocity.z * 1.0f, wind.Velocity.z * 1.4f);
            sv.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            var se = _spray.emission;
            // По корке песок не бежит — как и позёмка в шейдере земли.
            float loose = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.7f, looseness));
            se.rateOverTime = config.sprayRate * Saltation.Intensity(wind, config) * Mathf.Lerp(0.25f, 1f, wind.Gust) * loose;
        }

        ParticleSystem CreateMotes(FieldConfig config)
        {
            var ps = NewSystem("Dust Motes", config.dustMaterial, ParticleSystemRenderMode.Billboard);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.002f, 0.008f);
            main.startColor = new Color(1f, 1f, 1f, 0.35f);
            main.maxParticles = Mathf.Max(10, config.dustCount);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = ps.emission;
            em.rateOverTime = config.dustCount / 13f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(config.dustBox, 4f, config.dustBox);

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

        /// <summary>
        /// Сальтация: песчинка подскакивает на 5–25 см, летит по ветру и падает. Штрих 1–3 см толщиной,
        /// вытянутый скоростью до ~0,5 м, — как песчинку видит глаз на лету.
        /// </summary>
        ParticleSystem CreateSpray(FieldConfig config)
        {
            var ps = NewSystem("Sand Spray", config.driftMaterial != null ? config.driftMaterial : config.dustMaterial,
                ParticleSystemRenderMode.Stretch);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.velocityScale = 0.05f;
            r.lengthScale = 1f;
            r.maxParticleSize = MaxScreenSize * 4f; // штрих длинный: ограничиваем ширину, а не длину

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.4f); // вверх: форма повёрнута
            main.gravityModifier = 0.35f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.022f);
            main.startColor = new Color(1f, 1f, 1f, 0.55f);
            main.maxParticles = 800;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = ps.emission;
            em.rateOverTime = 0f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.rotation = new Vector3(-90f, 0f, 0f); // коробка испускает вдоль своей Z — разворачиваем вверх
            shape.scale = new Vector3(20f, 20f, 0.02f);
            shape.randomDirectionAmount = 0.15f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;

            FadeInOut(ps, 0.1f, 0.75f);
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
            r.maxParticleSize = MaxScreenSize;
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
