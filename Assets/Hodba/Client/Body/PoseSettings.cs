using System;
using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Как слои движения складываются в голову. Все числа — стартовые, подбираются на телефоне.
    /// </summary>
    [Serializable]
    public struct PoseSettings
    {
        [Header("Голова на шее")]
        [Tooltip("Собственная частота головы по смещению, Гц. Ниже — голова «болтается», выше — прибита к тазу.")]
        public float headHz;
        [Tooltip("Затухание по смещению: 1 — без перелёта, меньше — с перелётом.")]
        public float headDamping;
        [Tooltip("Собственная частота по поворотам, Гц.")]
        public float headRotHz;
        public float headRotDamping;

        [Header("Стоя не статуя")]
        [Tooltip("Покачивание корпуса, м (≈σ).")]
        public float postureSway;
        [Tooltip("Покачивание головы по тангажу, ° (≈σ).")]
        public float posturePitch;
        [Tooltip("Покачивание по крену, ° (≈σ).")]
        public float postureRoll;
        [Tooltip("Сколько его остаётся на ходу, доля.")]
        [Range(0f, 1f)] public float postureWhileWalking;

        [Header("Общий предел выраженности")]
        [Tooltip("Мягкий потолок смещений (вверх, вбок, вперёд), м. Совпавшие пики всех слоёв в него упираются, а не кивают.")]
        public Vector3 offsetLimit;
        [Tooltip("Мягкий потолок поворотов (тангаж, рыск, крен), °.")]
        public Vector3 angleLimit;
        [Tooltip("Мягкий потолок скорости смещений, м/с.")]
        public float offsetRate;
        [Tooltip("Мягкий потолок скорости поворотов, °/с.")]
        public float angleRate;

        [Header("Фон уступает событию")]
        [Tooltip("Насколько глохнет фон, пока идёт выраженное событие: 0 — никак, 1 — полностью.")]
        [Range(0f, 1f)] public float duckDepth;
        [Tooltip("Как быстро фон глохнет, с.")]
        public float duckRise;
        [Tooltip("Как долго фон возвращается, с.")]
        public float duckFall;

        public static PoseSettings Default => new PoseSettings
        {
            headHz = 5f,
            headDamping = 0.55f,
            headRotHz = 4f,
            headRotDamping = 0.6f,
            postureSway = 0.004f,
            posturePitch = 0.25f,
            postureRoll = 0.12f,
            postureWhileWalking = 0.5f,
            offsetLimit = new Vector3(0.06f, 0.05f, 0.05f),
            angleLimit = new Vector3(5f, 3f, 2.5f),
            offsetRate = 0.6f,
            angleRate = 45f,
            duckDepth = 0.7f,
            duckRise = 0.08f,
            duckFall = 1.2f,
        };
    }
}
