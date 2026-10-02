using System;
using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Спокойный, немного рассеянный взгляд человека, которому ещё очень долго идти.
    /// Все числа — стартовые настройки для подбора на телефоне, а не норма.
    /// </summary>
    [Serializable]
    public struct EyeWanderSettings
    {
        [Header("Дрейф: маленький и непрерывный")]
        [Tooltip("Размах дрейфа по горизонтали, ° (≈σ).")]
        public float driftYaw;
        [Tooltip("Размах дрейфа по вертикали, ° (≈σ).")]
        public float driftPitch;
        [Tooltip("Самая медленная октава дрейфа, Гц.")]
        public float driftLowestHz;
        [Tooltip("Во сколько раз замедляется дрейф, когда взгляд задержался.")]
        [Range(0f, 1f)] public float dwellSpeed;
        [Tooltip("Медиана «плывёт», с.")]
        public float moveMedian;
        [Tooltip("Медиана «задержался», с.")]
        public float dwellMedian;
        [Tooltip("Разброс длительностей (σ логарифма).")]
        [Range(0f, 1.5f)] public float dwellSpread;

        [Header("Перевод внимания: реже, крупнее, всегда плавно")]
        [Tooltip("Медиана между переводами внимания, с.")]
        public float shiftMedian;
        [Range(0f, 1.5f)] public float shiftSpread;
        [Tooltip("Как быстро центр внимания переезжает, Гц.")]
        public float centerHz;
        [Tooltip("1 — без перелёта.")]
        public float centerDamping;
        [Tooltip("Мягкий потолок скорости переезда, °/с.")]
        public float centerMaxSpeed;
        [Tooltip("Дальше этого в сторону глаза не уходят, °: дальше — дело головы.")]
        public float maxYaw;
        [Tooltip("Вниз — к дороге и опоре, °.")]
        public float maxDown;
        [Tooltip("Вверх, °.")]
        public float maxUp;
        [Tooltip("Такой перевод, °, часто сопровождается морганием.")]
        public float shiftBlinkAngle;

        [Header("Куда тянет")]
        [Tooltip("Точка на горизонте, ничем не примечательная.")]
        public float horizonWeight;
        [Tooltip("Остаться там, где есть: пустота — тоже состояние внимания.")]
        public float stayWeight;
        [Tooltip("Насколько в сторону может уйти точка горизонта, °.")]
        public float horizonYaw;
        public float horizonDwell;
        [Tooltip("Под ноги, просто так.")]
        public float groundBase;
        [Tooltip("Под ноги от осторожности.")]
        public float groundCaution;
        [Tooltip("Под ноги, когда земля впереди меняется.")]
        public float groundChange;
        [Tooltip("Под ноги на буграх и наносах.")]
        public float groundRough;
        [Tooltip("Под ноги при спешке в полную силу.")]
        public float groundHaste;
        [Tooltip("Насколько спешка гасит горизонт и камни вокруг, доля: торопясь, по сторонам не смотрят.")]
        [Range(0f, 1f)] public float hasteNarrow;
        [Tooltip("Вероятность глянуть под ноги, когда тело подстраивает шаг под камень. Не 1: часто тело справляется само.")]
        [Range(0f, 1f)] public float obstacleLook;
        public float groundDwell;
        [Tooltip("Притяжение мелкого камня.")]
        public float stoneWeight;
        [Tooltip("Притяжение валуна.")]
        public float boulderWeight;
        [Tooltip("Дальше этого мелкие камни не цепляют, м.")]
        public float stoneRadius;
        [Tooltip("Дальше этого валуны не цепляют, м.")]
        public float boulderRadius;
        public float stoneDwell;
        [Tooltip("Насколько предмет рядом притягивает и замедляет дрейф.")]
        [Range(0f, 1f)] public float attraction;

        [Header("Привыкание: знакомое теряет вес")]
        [Tooltip("Сколько знакомости добавляет одна фиксация.")]
        [Range(0f, 0.5f)] public float fixationGain;
        [Tooltip("Сколько знакомости в секунду даёт само присутствие однотипных вещей вокруг.")]
        public float presenceGain;
        [Tooltip("За сколько часов знакомость забывается (вдвое).")]
        public float habituationHours;
        [Tooltip("Валуны и необычное привыкают слабее, доля.")]
        [Range(0f, 1f)] public float boulderHabituation;

        [Header("Раздражители")]
        [Tooltip("Насколько солнце в лицо уводит взгляд.")]
        public float sunAversion;
        [Tooltip("Встречный ветер опускает взгляд, °.")]
        public float windDown;

        [Header("Игрок главнее")]
        [Tooltip("Медиана паузы, прежде чем глаза снова начнут блуждать после ввода, с.")]
        public float resumeMedian;
        [Range(0f, 1.5f)] public float resumeSpread;
        [Tooltip("Как долго блуждание набирает силу, с.")]
        public float resumeRamp;

        public static EyeWanderSettings Default => new EyeWanderSettings
        {
            driftYaw = 1.5f,
            driftPitch = 0.8f,
            driftLowestHz = 0.025f,
            dwellSpeed = 0.2f,
            moveMedian = 3f,
            dwellMedian = 1f,
            dwellSpread = 0.7f,

            shiftMedian = 5f,
            shiftSpread = 0.7f,
            centerHz = 0.3f,
            centerDamping = 0.95f,
            centerMaxSpeed = 14f,
            maxYaw = 8f,
            maxDown = 25f,
            maxUp = 5f,
            shiftBlinkAngle = 8f,

            horizonWeight = 1f,
            stayWeight = 0.8f,
            horizonYaw = 4f,
            horizonDwell = 4f,
            groundBase = 0.08f,
            groundCaution = 0.6f,
            groundChange = 0.4f,
            groundRough = 0.5f,
            groundHaste = 1f,
            hasteNarrow = 0.6f,
            obstacleLook = 0.5f,
            groundDwell = 1.2f,
            stoneWeight = 0.5f,
            boulderWeight = 1.2f,
            stoneRadius = 40f,
            boulderRadius = 400f,
            stoneDwell = 1.5f,
            attraction = 0.35f,

            fixationGain = 0.05f,
            presenceGain = 0.0002f,
            habituationHours = 6f,
            boulderHabituation = 0.4f,

            sunAversion = 1.5f,
            windDown = 3f,

            resumeMedian = 3.5f,
            resumeSpread = 0.5f,
            resumeRamp = 2f,
        };
    }
}
