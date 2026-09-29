using UnityEngine;
using UnityEngine.Rendering;

namespace Hodba.Client
{
    /// <summary>
    /// Небо, солнце, дымка и рассеянный свет — всё от высоты солнца.
    /// Дымка у горизонта неба того же цвета, что туман над землёй: горизонт растворяется без шва.
    /// Ночь — настоящая темнота: звёзды и тусклый холодный свет, без подсветки.
    /// </summary>
    public sealed class SkyController
    {
        static readonly int ZenithId = Shader.PropertyToID("_ZenithColor");
        static readonly int HorizonId = Shader.PropertyToID("_HorizonColor");
        static readonly int HazeId = Shader.PropertyToID("_HazeColor");
        static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        static readonly int SunSizeId = Shader.PropertyToID("_SunSize");
        static readonly int SunGlowId = Shader.PropertyToID("_SunGlow");
        static readonly int HazeHeightId = Shader.PropertyToID("_HazeHeight");
        static readonly int HorizonCurveId = Shader.PropertyToID("_HorizonCurve");
        static readonly int StarsId = Shader.PropertyToID("_StarIntensity");

        static readonly int GlobalSunDir = Shader.PropertyToID("_HodbaSunDir");
        static readonly int GlobalSunColor = Shader.PropertyToID("_HodbaSunColor");
        static readonly int GlobalAmbient = Shader.PropertyToID("_HodbaAmbient");

        static readonly Quaternion NightLight = Quaternion.Euler(62f, 200f, 0f);

        public readonly Light Sun;
        readonly Material _sky;

        public SkyController(Light sun, Material sky)
        {
            Sun = sun;
            _sky = sky;
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.ambientMode = AmbientMode.Trilight;
        }

        public void Tick(SkyClock clock, FieldConfig config)
        {
            float el = clock.Elevation;
            float t = Palette.ToGradientTime(el, config.elevationMin, config.elevationMax);

            Color zenith = config.skyZenith.Evaluate(t);
            Color horizon = config.skyHorizon.Evaluate(t);
            Color fog = config.fogColor.Evaluate(t);
            Color sunColor = config.sunColor.Evaluate(t);
            Color ambSky = config.ambientSky.Evaluate(t);
            Color ambEq = config.ambientEquator.Evaluate(t);
            Color ambGround = config.ambientGround.Evaluate(t);

            // Солнце днём, звёздный свет ночью. Переключаемся, когда солнечный свет уже погас — скачка не видно.
            float sunI = Mathf.Max(0f, config.sunIntensity.Evaluate(el));
            if (el > -3f)
            {
                Sun.transform.rotation = Quaternion.LookRotation(-clock.SunDirection);
                Sun.color = sunColor;
                Sun.intensity = sunI;
                Sun.shadows = sunI > 0.02f ? LightShadows.Soft : LightShadows.None;
            }
            else
            {
                Sun.transform.rotation = NightLight;
                Sun.color = config.nightLightColor;
                Sun.intensity = config.nightLightIntensity * Mathf.Clamp01((-3f - el) / 6f);
                Sun.shadows = LightShadows.None;
            }
            Sun.shadowStrength = config.shadowStrength;

            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = Mathf.Max(0f, config.fogDensity.Evaluate(el));
            RenderSettings.ambientSkyColor = ambSky;
            RenderSettings.ambientEquatorColor = ambEq;
            RenderSettings.ambientGroundColor = ambGround;
            RenderSettings.ambientProbe = Hemisphere(ambSky, ambEq, ambGround);

            if (_sky != null)
            {
                _sky.SetColor(ZenithId, zenith);
                _sky.SetColor(HorizonId, horizon);
                _sky.SetColor(HazeId, fog);
                _sky.SetColor(SunColorId, sunColor * Mathf.Clamp01((el + 2f) / 4f));
                _sky.SetVector(SunDirId, clock.SunDirection);
                _sky.SetFloat(SunSizeId, config.sunDiscSize);
                _sky.SetFloat(SunGlowId, config.sunGlow);
                _sky.SetFloat(HazeHeightId, config.hazeHeight);
                _sky.SetFloat(HorizonCurveId, config.horizonCurve);
                _sky.SetFloat(StarsId, config.starBrightness * Mathf.Clamp01((-5f - el) / 8f));
            }

            Shader.SetGlobalVector(GlobalSunDir, clock.SunDirection);
            Shader.SetGlobalColor(GlobalSunColor, el > -3f ? sunColor * sunI : config.nightLightColor * Sun.intensity);
            Shader.SetGlobalColor(GlobalAmbient, ambEq);
        }

        /// <summary>Рассеянный свет: небо сверху, горизонт по кругу, земля снизу.</summary>
        static SphericalHarmonicsL2 Hemisphere(Color sky, Color equator, Color ground)
        {
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(equator.linear);
            sh.AddDirectionalLight(Vector3.up, Delta(sky, equator), 0.8f);
            sh.AddDirectionalLight(Vector3.down, Delta(ground, equator), 0.8f);
            return sh;
        }

        // Может быть отрицательным: земля темнее горизонта — снизу света меньше.
        static Color Delta(Color a, Color b) => a.linear - b.linear;
    }
}
