using System;
using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Характер походки. Все числа — стартовые настройки для подбора на телефоне, а не норма:
    /// ощущение важнее цифр. Покачивание отключить нельзя — закон 6.
    /// </summary>
    [Serializable]
    public struct GaitSettings
    {
        [Header("Ритм")]
        [Tooltip("Вверх-вниз за шаг, м (размах).")]
        public float bobVertical;
        [Tooltip("Раскачка в стороны за пару шагов, м (размах).")]
        public float bobLateral;
        [Tooltip("Крен за пару шагов, °.")]
        public float bobRoll;
        [Tooltip("Кивок за шаг, °.")]
        public float bobPitch;
        [Tooltip("Рывок вперёд-назад за шаг, м: тело то догоняет ногу, то притормаживает.")]
        public float bobSurge;
        [Tooltip("Крен при повороте тела, ° на каждый °/с.")]
        public float turnLean;

        [Header("Живость: ни один шаг не копия другого")]
        [Tooltip("Разброс каждого шага, доля (белый).")]
        [Range(0f, 0.3f)] public float stepJitter;
        [Tooltip("Медленный дрейф характера шага, доля (1/f): то пружинистее, то ровнее.")]
        [Range(0f, 0.4f)] public float drift;
        [Tooltip("Разброс длины шага, доля.")]
        [Range(0f, 0.1f)] public float strideJitter;
        [Tooltip("Насколько гуляет момент верхней точки, доля шага.")]
        [Range(0f, 0.25f)] public float peakSkew;
        [Tooltip("Самая медленная октава дрейфа, Гц.")]
        public float driftLowestHz;

        [Header("Опора")]
        [Tooltip("Удар пятки на твёрдом, м/с толчка вниз.")]
        public float impactKick;
        [Tooltip("Кивок от удара, °/с толчка.")]
        public float impactNod;
        [Tooltip("Наклон корпуса на единицу уклона, °. Вверх — вперёд, вниз — назад.")]
        public float slopeLean;
        [Tooltip("Куда смотрят глаза на склоне, ° на единицу уклона: на спуске — вниз по склону, на подъёме — вверх.")]
        public float slopeGaze;
        [Tooltip("Наклон от разгона и торможения, ° на м/с².")]
        public float accelLean;
        [Tooltip("Боковое смещение стопы от оси, м.")]
        public float footOffset;
        [Tooltip("Насколько стопа при ударе впереди тела, доля шага.")]
        [Range(0f, 0.8f)] public float footReach;
        [Tooltip("Насколько корпус кренится от разницы высот левой и правой стопы, доля (тело гасит остальное).")]
        [Range(0f, 1f)] public float footRoll;
        [Tooltip("Насколько наклоняется от разницы высот опорной и прошлой стопы, доля.")]
        [Range(0f, 1f)] public float footPitch;
        [Tooltip("Как быстро вес переходит на новую стопу, Гц.")]
        public float supportHz;
        [Tooltip("Куда тело заглядывает заранее, м.")]
        public float lookAhead;
        [Tooltip("Насколько шаг укорачивается перед переменой земли, доля.")]
        [Range(0f, 0.3f)] public float anticipation;
        public SurfaceFeel[] surfaces;

        [Header("Неровная земля")]
        [Tooltip("Сколько осторожности дают бугры в полную силу.")]
        [Range(0f, 1f)] public float roughCaution;
        [Tooltip("Насколько короче шаг на буграх, доля.")]
        [Range(0f, 0.4f)] public float roughStride;
        [Tooltip("Насколько чаще шаркает на буграх.")]
        [Range(0f, 1f)] public float roughScuff;

        [Header("Камни на пути")]
        [Tooltip("Полоса стопы: камень ближе этого к точке постановки — на пути, м.")]
        public float footRadius;
        [Tooltip("Вероятность подстроить шаг под камень (растёт с осторожностью).")]
        [Range(0f, 1f)] public float avoidChance;
        [Tooltip("Камень крупнее этого обходят всегда, м.")]
        public float alwaysAvoidSize;
        [Tooltip("Вероятность споткнуться о неучтённый камень при нулевой осторожности.")]
        [Range(0f, 1f)] public float stumbleChance;
        [Tooltip("Удар при спотыкании, м/с толчка вниз.")]
        public float stumbleKick;
        [Tooltip("Кивок при спотыкании, °/с толчка.")]
        public float stumbleNod;

        [Header("Переходы")]
        [Tooltip("Длина первого шага из стойки, доля.")]
        [Range(0.3f, 1f)] public float firstStep;
        [Tooltip("Наклон при трогании, °/с толчка.")]
        public float startLean;
        [Tooltip("Корпус догоняет при остановке, °/с толчка.")]
        public float stopSettle;
        [Tooltip("Снаряжение догоняет через, с.")]
        public float gearLag;
        [Tooltip("Кивок от снаряжения, °/с толчка.")]
        public float gearNod;
        [Tooltip("Разворот стоя: переступание каждые столько °.")]
        public float shuffleAngle;

        [Header("Пружины тела")]
        [Tooltip("Удар пятки гаснет с этой частотой, Гц: выше — короче и суше.")]
        public float impactHz;
        [Tooltip("Спотыкание, камень, снаряжение, Гц.")]
        public float eventHz;
        [Tooltip("Наклон корпуса, Гц: медленный, с перелётом при остановке.")]
        public float leanHz;

        public static GaitSettings Default => new GaitSettings
        {
            bobVertical = 0.024f,
            bobLateral = 0.025f,
            bobRoll = 0.6f,
            bobPitch = 0.3f,
            bobSurge = 0.008f,
            turnLean = 0.03f,

            stepJitter = 0.07f,
            drift = 0.12f,
            strideJitter = 0.025f,
            peakSkew = 0.1f,
            driftLowestHz = 0.004f,

            impactKick = 0.04f,
            impactNod = 8f,
            slopeLean = 12f,
            slopeGaze = 6f,
            accelLean = 1.2f,
            footOffset = 0.12f,
            footReach = 0.35f,
            footRoll = 0.25f,
            footPitch = 0.1f,
            supportHz = 3f,
            lookAhead = 2.5f,
            anticipation = 0.08f,
            surfaces = SurfaceFeel.Defaults(),

            roughCaution = 0.5f,
            roughStride = 0.1f,
            roughScuff = 0.3f,

            footRadius = 0.16f,
            avoidChance = 0.75f,
            alwaysAvoidSize = 0.28f,
            stumbleChance = 0.12f,
            stumbleKick = 0.3f,
            stumbleNod = 45f,

            firstStep = 0.65f,
            startLean = 6f,
            stopSettle = 7f,
            gearLag = 0.28f,
            gearNod = 6f,
            shuffleAngle = 35f,

            impactHz = 7f,
            eventHz = 2.8f,
            leanHz = 1.2f,
        };
    }
}
