using System;
using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Как игрок подгоняет человека: тапы в такт ногам. Окно попадания — по фазе шага, без интерфейса:
    /// метроном — сам звук шагов. Числа стартовые, подбираются на телефоне.
    /// </summary>
    [Serializable]
    public struct RhythmSettings
    {
        [Tooltip("С какой фазы шага тап маховой ноги уже в такт: чуть раньше удара пятки.")]
        [Range(0f, 1f)] public float early;
        [Tooltip("До какой фазы тап ещё в такт, если нога только что встала.")]
        [Range(0f, 0.5f)] public float late;
        [Tooltip("Сколько тапов подряд в такт до спешки в полную силу.")]
        [Range(1f, 8f)] public float buildTaps;
        [Tooltip("Сколько шагов без тапа в такт спешка ещё держится.")]
        [Range(0.5f, 4f)] public float graceSteps;
        [Tooltip("За сколько шагов спешка гаснет, когда тапы прекратились.")]
        [Range(0.2f, 4f)] public float fallSteps;
        [Tooltip("Сколько спешки остаётся после тапа мимо такта, доля.")]
        [Range(0f, 1f)] public float missKeep;

        public static RhythmSettings Default => new RhythmSettings
        {
            early = 0.45f,
            late = 0.15f,
            buildTaps = 3f,
            graceSteps = 1.6f,
            fallSteps = 1f,
            missKeep = 0.4f,
        };
    }
}
