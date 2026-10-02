using System;
using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Память усилия: как тело копит и отпускает нагрузку. Числа стартовые, подбираются по ощущению.
    /// </summary>
    [Serializable]
    public struct ExertionSettings
    {
        [Header("Одышка (усилие и усталость считает симуляция: WalkParams.Pace)")]
        [Tooltip("Как быстро копится одышка, с.")]
        public float loadRise;
        [Tooltip("Как долго отпускает, с. После подъёма дыхание восстанавливается постепенно.")]
        public float loadFall;

        [Header("Дыхание")]
        [Tooltip("Вдохов в секунду в покое.")]
        public float breathRest;
        [Tooltip("Вдохов в секунду на пределе.")]
        public float breathHard;
        public float depthRest;
        public float depthHard;
        [Tooltip("Грудь поднимает глаза на столько при глубине 1, м.")]
        public float breathAmplitude;
        [Tooltip("Голова чуть запрокидывается на вдохе, °.")]
        public float breathPitch;
        [Tooltip("Шагов на вдох спокойно (у людей обычно 4).")]
        public float stepsPerBreathCalm;
        [Tooltip("Шагов на вдох тяжело (обычно 2).")]
        public float stepsPerBreathHard;
        [Tooltip("0..1 — насколько дыхание подстраивается под шаг. Не 1: связь должна соскальзывать.")]
        [Range(0f, 1f)] public float coupling;
        [Tooltip("Насколько каждый шаг подтягивает фазу дыхания, доля.")]
        [Range(0f, 0.5f)] public float phaseNudge;

        [Header("Осторожность")]
        [Tooltip("Как быстро тело настораживается, с.")]
        public float cautionRise;
        [Tooltip("Как долго осторожность держится после трудного места, с.")]
        public float cautionFall;
        [Tooltip("Сколько осторожности даёт спуск, на единицу уклона.")]
        public float downhillCaution;
        [Tooltip("С какого спуска (уклон) тело начинает осторожничать: пологий спуск идут легко.")]
        public float downhillFrom;
        [Tooltip("Сколько даёт перемена земли впереди.")]
        public float aheadCaution;
        [Tooltip("Сколько добавляет спотыкание.")]
        public float stumbleCaution;

        public static ExertionSettings Default => new ExertionSettings
        {
            loadRise = 10f,
            loadFall = 45f,

            breathRest = 0.23f,
            breathHard = 0.62f,
            depthRest = 0.6f,
            depthHard = 1.4f,
            breathAmplitude = 0.004f,
            breathPitch = 0.12f,
            stepsPerBreathCalm = 4f,
            stepsPerBreathHard = 2f,
            coupling = 0.5f,
            phaseNudge = 0.15f,

            cautionRise = 2.5f,
            cautionFall = 40f,
            downhillCaution = 1.5f,
            downhillFrom = 0.12f,
            aheadCaution = 0.5f,
            stumbleCaution = 0.6f,
        };
    }
}
