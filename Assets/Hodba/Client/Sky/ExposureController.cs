using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Hodba.Client
{
    /// <summary>
    /// Глаз. Полдень выбелен; взгляд на низкое солнце слепит быстро, а отпускает медленно —
    /// ослепление держится (Docs/Design/04-camera-presence.md).
    /// </summary>
    public sealed class ExposureController
    {
        readonly ColorAdjustments _color;
        readonly Bloom _bloom;
        readonly Vignette _vignette;
        float _glare;

        public ExposureController(Volume volume)
        {
            var profile = volume.profile; // своя копия, ассет не трогаем
            profile.TryGet(out _color);
            profile.TryGet(out _bloom);
            profile.TryGet(out _vignette);
        }

        public void Tick(Camera camera, SkyClock clock, FieldConfig config, float dt)
        {
            float el = clock.Elevation;
            float facing = Mathf.Clamp01(Vector3.Dot(camera.transform.forward, clock.SunDirection));
            float visible = Mathf.Clamp01((el + 1f) / 4f);
            float target = Mathf.Pow(facing, config.glarePower) * visible;

            float time = target > _glare ? config.glareRise : config.glareFall;
            _glare = Mathf.Lerp(_glare, target, 1f - Mathf.Exp(-dt / Mathf.Max(0.05f, time)));

            if (_color != null)
            {
                _color.postExposure.overrideState = true;
                _color.postExposure.value = config.exposure.Evaluate(el) + _glare * config.glareExposure;
                _color.saturation.overrideState = true;
                _color.saturation.value = config.saturation;
                _color.contrast.overrideState = true;
                _color.contrast.value = config.contrast;
            }

            if (_bloom != null)
            {
                _bloom.intensity.overrideState = true;
                _bloom.intensity.value = config.baseBloom + _glare * config.glareBloom;
            }

            if (_vignette != null)
            {
                _vignette.intensity.overrideState = true;
                _vignette.intensity.value = config.vignette;
            }
        }
    }
}
