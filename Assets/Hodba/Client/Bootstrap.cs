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
        FirstPersonRig _rig;
        ShadowBody _body;
        SkyClock _clock;
        SkyController _sky;
        ExposureController _exposure;
        Wind _wind;
        Dust _dust;
        TerrainStreamer _terrain;
        StoneScatter _stones;
        Footprints _footprints;
        WindSynth _windAudio;
        FootstepSynth _stepAudio;
        float _saveTimer;

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

            _world = new FlatStub(config.seed);

            var start = WorldPos.FromMeters(0, 0);
            float course = 30f;
            bool walking = false;
            if (config.continueFromSave && WalkerSave.TryLoad(out var saved))
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
            _rig = new FirstPersonRig(camera, _sim);
            _body = new ShadowBody(config, config.stoneMaterial);
            _clock = new SkyClock();
            _sky = new SkyController(sun, config.skyMaterial);
            _exposure = new ExposureController(volume);
            _wind = new Wind();
            _dust = new Dust(config, _origin);

            ApplyExternalTextures();
            _terrain = new TerrainStreamer(_world, _origin, config, config.groundMaterial);
            _terrain.BuildAll(_sim.Position);
            _stones = new StoneScatter(_world, _origin, config, config.stoneMaterial);
            _footprints = new Footprints(_world, _origin, config, config.footprintMaterial);

            _windAudio = WindSynth.Create(camera.transform, config.windLoop, config.windGustLoop, config.ashHissLoop);
            _stepAudio = FootstepSynth.Create(camera.transform, config.footstepClips);
            _rig.Step += (left, pos) =>
            {
                _footprints.Add(left, pos, _sim.Course);
                float intensity = Mathf.Clamp01(_sim.Speed / Mathf.Max(0.1f, _sim.Params.BaseSpeed));
                _stepAudio.Trigger(left, config.stepVolume * config.masterVolume * Mathf.Lerp(0.4f, 1f, intensity));
            };

            Tick(0f);
        }

        void Update() => Tick(Mathf.Min(Time.unscaledDeltaTime, 0.1f));

        void Tick(float dt)
        {
            _sim.Params = config.walk;

            _input.Tick(config);
            if (_input.Back) Minimize();
            if (_input.CycleTime) _clock.CyclePreset();

            _gaze.Tick(_input, _sim, config, dt);
            if (_input.ToggleWalk) _sim.Apply(Intent.Toggle());
            _sim.Step(dt, _world);

            _origin.Tick(_sim.Position);
            _terrain.Tick(_sim.Position);
            _stones.Tick(_sim.Position);

            _rig.Tick(_sim, _gaze, _world, _origin, config, dt);
            _body.Tick(_sim, _world, _origin, _rig.Bob);

            _clock.Tick(config);
            _sky.Tick(_clock, config);
            _exposure.Tick(_rig.Camera, _clock, config, dt);

            _wind.Tick(config, Time.time);
            float ground = _world.SampleHeightMm(_sim.Position) / 1000f;
            _dust.Tick(_rig.Camera, _wind, config, _origin.ToLocal(_sim.Position, ground).y);
            _footprints.Tick(dt, _wind.Strength);

            float side = Mathf.Sin((_wind.Direction + 180f - _gaze.Yaw) * Mathf.Deg2Rad);
            _windAudio.Set(_wind.Strength, _wind.Gust, side, config.windVolume * config.masterVolume, config.ashHissVolume);

            _saveTimer -= dt;
            if (_saveTimer <= 0f)
            {
                _saveTimer = 10f;
                WalkerSave.Save(_sim);
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (_sim == null) return;
            if (paused)
            {
                WalkerSave.Save(_sim);
                return;
            }

            // Вернулся: сколько он прошёл без тебя.
            if (!WalkerSave.TryLoad(out var saved) || !saved.Walking) return;
            var pos = WalkerSave.Advance(saved, config.walk.BaseSpeed, config.backgroundMaxHours, out double meters);
            if (meters < 1.0) return;
            _sim.Teleport(pos, saved.Course, true);
            _rig.ResetSteps(_sim);
            _origin.Rebase(pos);
            if (meters > 300.0) _footprints.Clear();
            _terrain.BuildAll(pos);
        }

        void OnApplicationQuit()
        {
            if (_sim != null) WalkerSave.Save(_sim);
        }

        void OnDestroy() => _input?.Dispose();

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
            light.shadowBias = 0.05f;
            light.shadowNormalBias = 0.4f;
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
            if (config.ashAlbedo != null)
            {
                m.SetTexture("_AshAlbedo", config.ashAlbedo);
                m.SetFloat("_AlbedoMode", 1f);
            }
            if (config.packedAlbedo != null) m.SetTexture("_PackedAlbedo", config.packedAlbedo);
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
