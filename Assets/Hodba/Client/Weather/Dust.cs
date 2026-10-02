using UnityEngine;
using UnityEngine.Rendering;
using Hodba.Client.Body;
using Hodba.World;

namespace Hodba.Client
{
    /// <summary>
    /// Мелкие зёрна несёт ветер, песчинки прыгают у ног, а с фронтом порыва
    /// в лицо бьёт короткий налёт песка — вместе со звуком порыва и прищуром. Против низкого солнца частицы светятся
    /// (шейдер Hodba/Dust); вплотную к глазу гаснут: пылинка в 20 см от глаза была бы огромным мягким пятном.
    /// </summary>
    public sealed class Dust
    {
        /// <summary>Самая крупная частица на экране, доля его высоты.</summary>
        const float MaxScreenSize = 0.0015f;
        const float MoteLifetime = 2.4f;
        /// <summary>Насколько порыв у путника должен вырасти над недавним затишьем, чтобы песок ударил в лицо.</summary>
        const float BurstRise = 0.25f;
        const int BurstCount = 60;
        /// <summary>Песок в лицо: частиц в секунду при полном ветре в глаза; начинается чуть раньше прищура.</summary>
        const float FaceRate = 220f, FaceThreshold = 0.1f;
        const float FaceScreenSize = 0.005f;

        readonly ParticleSystem _motes;
        readonly ParticleSystem _spray;
        readonly ParticleSystem _burst;
        readonly ParticleSystem _face;
        readonly FloatingOrigin _origin;
        readonly IWorldQuery _world;
        readonly long _drawnFootprintMm;
        ParticleSystem.Particle[] _buffer = new ParticleSystem.Particle[0];
        float _gustFloor;
        float _sprayCarry;
        float _faceCarry;

        public Dust(FieldConfig config, FloatingOrigin origin, IWorldQuery world)
        {
            _origin = origin;
            _world = world;
            _drawnFootprintMm = System.Math.Max(10, (long)System.Math.Round(config.clipSpacing * 1000f));
            _motes = CreateMotes(config);
            _spray = CreateSpray(config);
            _burst = CreateBurst(config);
            _face = CreateFace(config);
            origin.Shifted += d => { Shift(_motes, d); Shift(_spray, d); Shift(_burst, d); Shift(_face, d); };
        }

        /// <param name="looseness">Рыхлость земли под путником, 0..1.</param>
        public void Tick(Camera camera, Wind wind, SandDrift sand, FieldConfig config, float groundY, float looseness, float dt)
        {
            var eye = camera.transform.position;
            var flow = wind.Velocity;
            var upwind = flow.sqrMagnitude > 1e-4f ? -flow.normalized : Vector3.zero;

            // Пылинки рождаются с наветренной стороны и пролетают мимо глаз со скоростью ветра.
            _motes.transform.position = eye + upwind * (config.dustBox * 0.3f);
            var mv = _motes.velocityOverLifetime;
            // Все три кривые обязаны быть в одном режиме — поэтому везде «между двумя».
            mv.x = new ParticleSystem.MinMaxCurve(flow.x * 0.8f, flow.x * 1.1f);
            mv.z = new ParticleSystem.MinMaxCurve(flow.z * 0.8f, flow.z * 1.1f);
            mv.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.15f);
            var mm = _motes.main;
            mm.startColor = new Color(1f, 1f, 1f, Mathf.Lerp(0.08f, 0.3f, sand.Veil));
            var me = _motes.emission;
            me.rateOverTime = config.dustCount / MoteLifetime * Mathf.Lerp(0.15f, 1f, sand.Veil);

            // Каждый скачок начинается на настоящем грунте, а не на плоскости под камерой.
            EmitSpray(eye + upwind * 2f, flow, config.sprayRate * sand.Grain
                * Mathf.Lerp(0.4f, 1f, wind.Gust), dt);
            CullLandedGrains();

            EmitFace(eye, flow, WindIrritant.Stimulus(flow, camera.transform.forward, wind.Strength, wind.Gust), dt);

            // Фронт порыва дошёл до путника: короткий налёт песчинок вокруг глаз.
            _gustFloor = Mathf.Min(_gustFloor + dt * 0.08f, wind.Gust);
            if (wind.Gust - _gustFloor > BurstRise)
            {
                _gustFloor = wind.Gust;
                float amount = Mathf.Max(sand.Grain, sand.Veil * 0.5f) * Mathf.Lerp(0.4f, 1f, looseness);
                Burst(eye, groundY, flow, Mathf.RoundToInt(BurstCount * amount));
            }
        }

        void EmitSpray(Vector3 center, Vector3 flow, float rate, float dt)
        {
            _sprayCarry += Mathf.Max(0f, rate) * dt;
            int count = Mathf.Min(Mathf.FloorToInt(_sprayCarry), 180);
            _sprayCarry -= Mathf.Floor(_sprayCarry);
            for (int i = 0; i < count; i++)
            {
                var offset = Random.insideUnitCircle * 7f;
                var p = center + new Vector3(offset.x, 0f, offset.y);
                var world = _origin.ToWorld(p);
                float loose = _world.SampleSurface(world.X, world.Z).Looseness / 65535f;
                if (Random.value > Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.7f, loose))) continue;
                p.y = _world.SampleHeightMm(world.X, world.Z, _drawnFootprintMm) / 1000f + 0.015f;
                _spray.Emit(new ParticleSystem.EmitParams
                {
                    position = p,
                    velocity = flow * Random.Range(0.4f, 0.75f) + Vector3.up * Random.Range(0.35f, 0.9f),
                }, 1);
            }
        }

        /// <summary>
        /// Песок прямо в глаза: штрихи по ветру вылетают из точки впереди и расходятся к краям кадра.
        /// Столько же ветра заставляет щуриться (<see cref="WindIrritant.Stimulus"/>), поэтому прищур выглядит оправданным.
        /// </summary>
        void EmitFace(Vector3 eye, Vector3 flow, float stimulus, float dt)
        {
            float speed = flow.magnitude;
            if (speed < 0.1f) return;
            _faceCarry += FaceRate * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FaceThreshold, 1f, stimulus)) * dt;
            int count = Mathf.Min(Mathf.FloorToInt(_faceCarry), 80);
            _faceCarry -= Mathf.Floor(_faceCarry);
            var dir = flow / speed;
            var side = Vector3.Cross(Vector3.up, dir);
            for (int i = 0; i < count; i++)
            {
                // Наветренный конус вокруг глаз: частицы, нацеленные в точку возле лица, пролетают у самого века.
                var p = eye - dir * Random.Range(2f, 5f) + side * Random.Range(-1.4f, 1.4f) + Vector3.up * Random.Range(-0.9f, 0.7f);
                var aim = eye + side * Random.Range(-0.25f, 0.25f) + Vector3.up * Random.Range(-0.2f, 0.2f);
                var v = Vector3.Lerp(dir, (aim - p).normalized, 0.5f).normalized * speed * Random.Range(1.1f, 1.5f);
                _face.Emit(new ParticleSystem.EmitParams { position = p, velocity = v }, 1);
            }
        }

        void CullLandedGrains()
        {
            int count = _spray.particleCount;
            if (_buffer.Length < count) _buffer = new ParticleSystem.Particle[count];
            count = _spray.GetParticles(_buffer);
            for (int i = 0; i < count; i++)
            {
                var world = _origin.ToWorld(_buffer[i].position);
                float ground = _world.SampleHeightMm(world.X, world.Z, _drawnFootprintMm) / 1000f;
                if (_buffer[i].position.y < ground) _buffer[i].remainingLifetime = 0f;
            }
            _spray.SetParticles(_buffer, count);
        }

        void Burst(Vector3 eye, float groundY, Vector3 flow, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var around = Random.insideUnitCircle.normalized * Random.Range(2f, 6f);
                var p = new ParticleSystem.EmitParams
                {
                    position = new Vector3(eye.x + around.x, groundY + Random.Range(0.3f, 1.5f), eye.z + around.y),
                    velocity = flow * Random.Range(0.9f, 1.25f) + Vector3.up * Random.Range(-0.2f, 0.4f),
                };
                _burst.Emit(p, 1);
            }
        }

        ParticleSystem CreateMotes(FieldConfig config)
        {
            var ps = NewSystem("Dust Motes", config.dustMaterial, ParticleSystemRenderMode.Billboard);

            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(MoteLifetime * 0.7f, MoteLifetime * 1.3f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.002f, 0.006f);
            main.startColor = new Color(1f, 1f, 1f, 0.35f);
            main.maxParticles = Mathf.Max(10, config.dustCount);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = ps.emission;
            em.rateOverTime = config.dustCount / MoteLifetime;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(config.dustBox, 3f, config.dustBox);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.3f;
            noise.scrollSpeed = 0.3f;

            FadeInOut(ps, 0.1f, 0.85f);
            ps.Play();
            return ps;
        }

        /// <summary>
        /// Короткие низкие скачки мелких зёрен. Рождение и приземление следуют рельефу.
        /// </summary>
        ParticleSystem CreateSpray(FieldConfig config)
        {
            var ps = NewSystem("Sand Spray", config.sprayMaterial,
                ParticleSystemRenderMode.Billboard);

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.4f);
            main.startSpeed = 0f;
            main.gravityModifier = 0.75f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.003f, 0.008f);
            main.startColor = new Color(1f, 1f, 1f, 0.45f);
            main.maxParticles = 1600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = ps.emission;
            em.enabled = false;

            var shape = ps.shape;
            shape.enabled = false;

            FadeInOut(ps, 0.1f, 0.75f);
            ps.Play();
            return ps;
        }

        /// <summary>Налёт порыва: песок в лицо на высоте глаз, полсекунды — секунда, штрихами по ветру.</summary>
        ParticleSystem CreateBurst(FieldConfig config)
        {
            var ps = NewSystem("Gust Sand", config.sprayMaterial, ParticleSystemRenderMode.Stretch);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.velocityScale = 0.002f;
            r.lengthScale = 1f;
            r.maxParticleSize = MaxScreenSize;

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.002f, 0.006f);
            main.startColor = new Color(1f, 1f, 1f, 0.35f);
            main.maxParticles = BurstCount * 3;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.2f;

            var em = ps.emission;
            em.enabled = false;

            FadeInOut(ps, 0.1f, 0.7f);
            ps.Play();
            return ps;
        }

        /// <summary>Песок в лицо: быстрые штрихи; гаснут только вплотную к глазу, чтобы долетать до века.</summary>
        ParticleSystem CreateFace(FieldConfig config)
        {
            var material = new Material(config.sprayMaterial) { name = "Sand Face" };
            material.SetVector("_NearFade", new Vector4(0.06f, 0.35f, 0f, 0f));
            var ps = NewSystem("Face Sand", material, ParticleSystemRenderMode.Stretch);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.velocityScale = 0.012f;
            r.lengthScale = 1f;
            r.maxParticleSize = FaceScreenSize;

            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.012f);
            main.startColor = new Color(1f, 1f, 1f, 0.55f);
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = ps.emission;
            em.enabled = false;

            FadeInOut(ps, 0.1f, 0.85f);
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
