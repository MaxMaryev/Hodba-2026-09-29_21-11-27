using Hodba.Client.Body;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Hodba.Client
{
    /// <summary>
    /// Глаз. Полдень выбелен; взгляд на низкое солнце слепит быстро, а отпускает медленно —
    /// ослепление держится (Docs/Design/04-camera-presence.md). Прищур снимает часть ослепления,
    /// но щурятся на стимул, а не на воспринятое — поэтому глаз не качается (<see cref="Glare"/>).
    /// </summary>
    public sealed class ExposureController
    {
        readonly ColorAdjustments _color;
        readonly Bloom _bloom;
        readonly Vignette _vignette;
        readonly GlareAdaptation _adaptation = new GlareAdaptation();

        /// <summary>0..1 — насколько солнце бьёт в глаз, до век.</summary>
        public float GlareStimulus { get; private set; }
        /// <summary>0..1 — что осталось после век и медленно отпускает.</summary>
        public float PerceivedGlare => _adaptation.Perceived;

        public ExposureController(Volume volume)
        {
            var profile = volume.profile; // своя копия, ассет не трогаем
            profile.TryGet(out _color);
            profile.TryGet(out _bloom);
            profile.TryGet(out _vignette);
        }

        /// <param name="squint">0..1 — прищур из век.</param>
        public void Tick(Camera camera, SkyClock clock, FieldConfig config, float dt, float squint)
        {
            float el = clock.Elevation;
            var lids = config.eyelids;
            GlareStimulus = Glare.Stimulus(camera.transform.forward, clock.SunDirection, el, lids.sunPower);
            float glare = _adaptation.Tick(GlareStimulus, squint, lids.squintGlareRelief, config.glareRise, config.glareFall, dt);

            if (_color != null)
            {
                _color.postExposure.overrideState = true;
                _color.postExposure.value = config.exposure.Evaluate(el) + glare * config.glareExposure;
                _color.saturation.overrideState = true;
                _color.saturation.value = config.saturation;
                _color.contrast.overrideState = true;
                _color.contrast.value = config.contrast;
            }

            if (_bloom != null)
            {
                _bloom.intensity.overrideState = true;
                _bloom.intensity.value = config.baseBloom + glare * config.glareBloom;
            }

            if (_vignette != null)
            {
                _vignette.intensity.overrideState = true;
                _vignette.intensity.value = config.vignette;
            }
        }
    }
}
