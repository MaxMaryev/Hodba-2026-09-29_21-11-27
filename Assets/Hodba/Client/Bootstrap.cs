using Hodba.Client.Body;
using Hodba.Core;
using Hodba.Sim.Walk;
using Hodba.World;
using Hodba.World.Gen;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Hodba.Client
{
    /// <summary>
    /// Единственный объект сцены «Поле». Собирает мир при запуске и ведёт все системы в строгом порядке.
    /// Сцену и ассеты создаёт меню Hodba ▸ Setup Field.
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        public FieldConfig config;

        IWorldQuery _world;
        WalkSim _sim;
        FloatingOrigin _origin;
        InputReader _input;
        GazeController _gaze;
        WalkerBody _walker;
        FirstPersonRig _rig;
        ShadowBody _body;
        SkyClock _clock;
        SkyController _sky;
        ExposureController _exposure;
        Wind _wind;
        Dust _dust;
        DustShadows _dustShadows;
        Saltation _saltation;
        ClipmapTerrain _ground;
        StoneScatter _stones;
        Footprints _footprints;
        WindSynth _windAudio;
        FootstepSynth _stepAudio;
        StepDetailSynth _stepDetailAudio;
        BreathSynth _breathAudio;
        GearSynth _gearAudio;
        BodyDebugOverlay _debug;
        EyeRender _eyeRender;
        bool _looking;
        float _saveTimer;

        bool Proving => config.worldKind == WorldKind.ProvingGround;

        void Awake()
        {
            if (config == null)
            {
                Debug.LogError("Hodba: у Bootstrap нет FieldConfig. Запусти Hodba ▸ Setup Field.");
                enabled = false;
                return;
            }

            Application.targetFrameRate = 30;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.AutoRotation;

            _world = Proving ? new ProvingGround(config.seed) : new FlatStub(config.seed);

            var start = WorldPos.FromMeters(0, 0);
            float course = Proving ? 0f : 30f;
            bool walking = false;
            // Полигон всегда с начала маршрута: сохранение ему только мешает.
            if (!Proving && config.continueFromSave && WalkerSave.TryLoad(out var saved))
            {
                start = WalkerSave.Advance(saved, config.walk.BaseSpeed, config.backgroundMaxHours, out _);
                course = saved.Course;
                walking = saved.Walking;
            }

            _sim = new WalkSim(config.walk, start, course);
            if (walking) _sim.Teleport(start, course, true);
            _origin = new FloatingOrigin(start);

            var camera = CreateCamera();
            var sun = CreateSun();
            var volume = CreateVolume();

            _input = new InputReader();
            _gaze = new GazeController(course);
            _walker = new WalkerBody(config, _world);
            _rig = new FirstPersonRig(camera);
            _body = new ShadowBody(config, config.stoneMaterial);
            _clock = new SkyClock();
            _sky = new SkyController(sun, config.skyMaterial);
            _exposure = new ExposureController(volume);
            _eyeRender = new EyeRender(camera, config.eyeShader != null ? config.eyeShader : Shader.Find("Hidden/Hodba/Eye"));
            _wind = new Wind();
            _dust = new Dust(config, _origin);
            _dustShadows = new DustShadows();
            _saltation = new Saltation(_origin);

            ApplyExternalTextures();
            _ground = new ClipmapTerrain(_world, _origin, config, config.groundMaterial);
            _ground.Update(_sim.Position);
            _stones = new StoneScatter(_world, _origin, config, config.stoneMaterial, config.boulderMaterial);
            _footprints = new Footprints(_world, _origin, config, config.footprintMaterial);

            _windAudio = WindSynth.Create(camera.transform, config.windLoop, config.windGustLoop, config.ashHissLoop);
            _stepAudio = FootstepSynth.Create(camera.transform, config.footstepClips);
            _stepDetailAudio = StepDetailSynth.Create(camera.transform);
            _breathAudio = BreathSynth.Create(camera.transform);
            _gearAudio = GearSynth.Create(camera.transform);

            // Один шаг — след, звук опоры, подробности, снаряжение. Каждый потребитель берёт своё.
            _walker.Events.Step += e =>
            {
                _footprints.Add(e.Left, e.Contact, e.Course);
                if (!e.Felt) return;
                var feel = SurfaceFeel.Find(config.gait.surfaces, e.Surface);
                _stepAudio.Trigger(e, feel, config.stepVolume * config.masterVolume);
                _stepDetailAudio.OnStep(e, config.stepDetailVolume * config.masterVolume);
                _gearAudio.OnStep(e, config.gearVolume * config.masterVolume);
            };
            _walker.Events.Body += e => _gearAudio.OnBodyEvent(e, config.gearVolume * config.masterVolume);

            if (Debug.isDebugBuild)
            {
                _debug = BodyDebugOverlay.Attach(gameObject, _walker, _sim);
                _debug.SetEye(_eyeRender);
            }

            Tick(0f);
        }

        void Update() => Tick(Mathf.Min(Time.unscaledDeltaTime, 0.1f));

        void Tick(float dt)
        {
            _sim.Params = config.walk;

            _input.Tick(config);
            if (_input.Back) Minimize();
            if (_input.CycleTime) _clock.CyclePreset();

            // Игрок взял взгляд: глаза отдают свой взгляд голове до того, как ввод её поведёт, — без скачка.
            if (_input.Looking && !_looking) _walker.YieldEyes(_gaze);
            _looking = _input.Looking;

            _gaze.Tick(_input, _sim, config, dt);
            if (_input.ToggleWalk) _sim.Apply(Intent.Toggle());
            _sim.Step(dt, _world);

            _origin.Tick(_sim.Position);
            _ground.Tick(_sim.Position);
            _stones.Tick(_sim.Position);

            // Ветер раньше тела: тело (а потом и веки) чувствует его в этом же кадре.
            _wind.Tick(config, Time.time, _sim.Course);

            _walker.Tick(_walker.Context(dt, _sim, _world, _wind, _clock, _gaze, _looking));
            _rig.Apply(_walker.Pose, _walker.Eyes, _walker.Gait.SupportHeight, _sim, _gaze, _origin, config);
            _body.Tick(_sim, _walker.Gait.SupportHeight, _origin, _walker.Pose.Up, _walker.Gait.Lean);
            _breathAudio.Set(_walker.Exertion, config.breathVolume * config.masterVolume);
            if (_debug != null) _debug.Sample(dt);

            float ground = _world.SampleHeightMm(_sim.Position) / 1000f;
            _clock.Tick(config);
            _sky.SetRaisedDust(_wind.Strength * _wind.Gust);
            _sky.Tick(_clock, config, ground, dt);
            _dustShadows.Tick(_wind, config, dt);
            _saltation.Tick(_wind, config, _rig.Camera.transform.position, dt);
            _exposure.Tick(_rig.Camera, _clock, config, dt, _walker.Eyelids.Squint);
            _eyeRender.Apply(_walker.Eyelids, _exposure.GlareStimulus, _walker.Periphery);

            float looseness = _world.SampleSurface(_sim.Position.X, _sim.Position.Z).Looseness / 65536f;
            _dust.Tick(_rig.Camera, _wind, config, _origin.ToLocal(_sim.Position, ground).y, looseness);
            _footprints.Tick(dt, _wind.Strength);

            float side = Mathf.Sin((_wind.Direction + 180f - _gaze.Yaw) * Mathf.Deg2Rad);
            _windAudio.Set(_wind.Strength, _wind.Gust, side, config.windVolume * config.masterVolume, config.ashHissVolume);

            _saveTimer -= dt;
            if (_saveTimer <= 0f)
            {
                _saveTimer = 10f;
                Save();
            }
        }

        /// <summary>Полигон не пишет в сохранение: настоящий путь путника он не трогает.</summary>
        void Save()
        {
            if (_sim != null && !Proving) WalkerSave.Save(_sim);
        }

        void OnApplicationPause(bool paused)
        {
            if (_sim == null || Proving) return;
            if (paused)
            {
                Save();
                return;
            }

            // Вернулся: сколько он прошёл без тебя.
            if (!WalkerSave.TryLoad(out var saved) || !saved.Walking) return;
            var pos = WalkerSave.Advance(saved, config.walk.BaseSpeed, config.backgroundMaxHours, out double meters);
            if (meters < 1.0) return;
            _sim.Teleport(pos, saved.Course, true);
            _walker.Reset(_sim);
            _origin.Rebase(pos);
            if (meters > 300.0) _footprints.Clear();
            _ground.Update(pos);
        }

        void OnApplicationQuit() => Save();

        void OnDestroy()
        {
            _input?.Dispose();
            _eyeRender?.Dispose();
            _ground?.Dispose();
        }

        Camera CreateCamera()
        {
            var go = new GameObject("Eyes");
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 5000f;
            cam.fieldOfView = config.fov;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = true;
            cam.allowMSAA = true;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.renderShadows = true;
            go.AddComponent<AudioListener>();
            go.tag = "MainCamera";
            return cam;
        }

        static Light CreateSun()
        {
            var go = new GameObject("Sun");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            // Смещения теней берутся из HodbaURP.asset (Hodba ▸ Setup Field).
            return light;
        }

        Volume CreateVolume()
        {
            var go = new GameObject("Eye (post)");
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = 10;
            v.sharedProfile = config.volumeProfile;
            return v;
        }

        void ApplyExternalTextures()
        {
            var m = config.groundMaterial;
            if (m == null) return;
            // Готовый цвет (Т1+Т3) — только когда есть оба, иначе заглушка второго стала бы тёмно-серой.
            if (config.ashAlbedo != null && config.packedAlbedo != null)
            {
                m.SetTexture("_AshAlbedo", config.ashAlbedo);
                m.SetTexture("_PackedAlbedo", config.packedAlbedo);
                m.SetFloat("_AlbedoMode", 1f);
                m.SetColor("_AshColor", Color.white);
                m.SetColor("_PackedColor", Color.white);
            }
            if (config.ashNormal != null) m.SetTexture("_AshNormal", config.ashNormal);
            if (config.rippleNormal != null) m.SetTexture("_RippleNormal", config.rippleNormal);
            if (config.footprintTexture != null && config.footprintMaterial != null)
                config.footprintMaterial.SetTexture("_MainTex", config.footprintTexture);
        }

        static void Minimize()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                activity.Call<bool>("moveTaskToBack", true);
#endif
        }
    }
}
