using System;
using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Веки. Художественное средство: силу подбираем на телефоне, все числа — стартовые.
    /// </summary>
    [Serializable]
    public struct EyelidSettings
    {
        [Header("Моргание")]
        [Tooltip("Медиана паузы между морганиями, с (у человека 3–4 с, но на экране каждое моргание заметнее — реже).")]
        public float blinkMedian;
        [Tooltip("Разброс пауз (σ логарифма): больше — чаще «залипания» без моргания.")]
        [Range(0f, 1.5f)] public float blinkSpread;
        [Tooltip("Вероятность двойного моргания.")]
        [Range(0f, 0.5f)] public float doubleBlink;
        [Tooltip("Веко закрывается, с.")]
        public float closeTime;
        [Tooltip("Сомкнуто, с.")]
        public float holdTime;
        [Tooltip("Открывается, с — медленнее, чем закрывается.")]
        public float openTime;
        [Tooltip("Насколько смыкается при обычном моргании: 1 — полностью темно. Полная тьма — только от сонливости.")]
        [Range(0.5f, 1f)] public float blinkDepth;
        [Tooltip("Во сколько раз реже моргаешь, когда всматриваешься.")]
        [Range(0.1f, 1f)] public float focusBlinkRate;
        [Tooltip("Вероятность моргнуть от спотыкания.")]
        [Range(0f, 1f)] public float stumbleBlink;
        [Tooltip("Вероятность моргнуть при крупном переводе взгляда.")]
        [Range(0f, 1f)] public float gazeShiftBlink;

        [Header("Прищур")]
        [Tooltip("Верхнее веко при полном прищуре, доля щели.")]
        [Range(0f, 0.8f)] public float squintUpper;
        [Tooltip("Нижнее веко при полном прищуре, доля щели.")]
        [Range(0f, 0.8f)] public float squintLower;
        [Tooltip("Как быстро щуришься, с.")]
        public float squintRise;
        [Tooltip("Как долго отпускает, с.")]
        public float squintFall;
        [Tooltip("Прищур дрожит, а не застывает: ±доля.")]
        [Range(0f, 0.2f)] public float squintFlutter;
        [Tooltip("Сколько ослепления снимает полный прищур.")]
        [Range(0f, 1f)] public float squintGlareRelief;

        [Header("Солнце")]
        [Tooltip("Острота конуса ослепления.")]
        public float sunPower;
        [Tooltip("Слабее этого стимула солнце не заставляет щуриться.")]
        [Range(0f, 1f)] public float sunThreshold;
        [Tooltip("Прищур в полдень, даже не глядя на солнце: пустыня выбелена.")]
        [Range(0f, 1f)] public float middaySquint;
        [Tooltip("Сколько добавляет к частоте моргания полное ослепление.")]
        public float sunBlink;

        [Header("Ветер в лицо")]
        [Tooltip("Прищур от сильного ветра прямо в лицо.")]
        [Range(0f, 1f)] public float windSquint;
        [Tooltip("Сколько добавляет к частоте моргания сильный ветер в лицо (порывы — больше).")]
        public float windBlink;

        public static EyelidSettings Default => new EyelidSettings
        {
            blinkMedian = 6f,
            blinkSpread = 0.6f,
            doubleBlink = 0.04f,
            closeTime = 0.08f,
            holdTime = 0.03f,
            openTime = 0.18f,
            blinkDepth = 0.9f,
            focusBlinkRate = 0.5f,
            stumbleBlink = 0.8f,
            gazeShiftBlink = 0.3f,

            squintUpper = 0.45f,
            squintLower = 0.4f,
            squintRise = 0.35f,
            squintFall = 1.5f,
            squintFlutter = 0.06f,
            squintGlareRelief = 0.55f,

            sunPower = 6f,
            sunThreshold = 0.15f,
            middaySquint = 0.2f,
            sunBlink = 0.4f,

            windSquint = 0.8f,
            windBlink = 1f,
        };
    }
}
