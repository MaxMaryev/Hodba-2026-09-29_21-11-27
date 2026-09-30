using System;
using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Периферия. Человек видит чётко только в центре. Игрок и так смотрит своими глазами по экрану,
    /// но в пустыне мало мелких деталей, и слабое мыло не видно вовсе — поэтому оно заметное;
    /// с одышкой (потом — жаждой, жарой, усталостью) туннель сужается и темнеет.
    /// Числа стартовые, подбираются на телефоне.
    /// </summary>
    [Serializable]
    public struct PeripherySettings
    {
        [Tooltip("Откуда начинается мыло: 0 — от центра, 1 — только в углах.")]
        [Range(0f, 1f)] public float start;
        [Tooltip("Сколько мыла в углах в покое, 0..1.")]
        [Range(0f, 1f)] public float blur;
        [Tooltip("Сколько цвета уходит с краёв в покое, 0..1.")]
        [Range(0f, 1f)] public float desaturate;
        [Tooltip("Ширина размытия, в пикселях уменьшенной картинки.")]
        [Range(0.5f, 4f)] public float radius;

        [Header("Туннель")]
        [Tooltip("С какой одышки начинается туннель, 0..1.")]
        [Range(0f, 1f)] public float tunnelFrom;
        [Tooltip("Насколько туннель подбирается к центру.")]
        [Range(0f, 0.6f)] public float tunnelInward;
        [Tooltip("Сколько мыла добавляет туннель.")]
        [Range(0f, 1f)] public float tunnelBlur;
        [Tooltip("Насколько темнеют края в туннеле.")]
        [Range(0f, 1f)] public float tunnelDarken;

        public static PeripherySettings Default => new PeripherySettings
        {
            start = 0.4f,
            blur = 0.8f,
            desaturate = 0.25f,
            radius = 3f,
            tunnelFrom = 0.5f,
            tunnelInward = 0.25f,
            tunnelBlur = 0.2f,
            tunnelDarken = 0.35f,
        };
    }

    public readonly struct PeripheryState
    {
        /// <summary>Откуда начинается мыло: 0 — центр, 1 — углы.</summary>
        public readonly float Start;
        public readonly float Blur;
        public readonly float Desaturate;
        public readonly float Darken;
        public readonly float Radius;

        public PeripheryState(float start, float blur, float desaturate, float darken, float radius)
        {
            Start = start;
            Blur = blur;
            Desaturate = desaturate;
            Darken = darken;
            Radius = radius;
        }

        public bool Visible => Blur > 0.001f || Desaturate > 0.001f || Darken > 0.001f;
    }

    public static class Periphery
    {
        /// <summary>Периферия по состоянию тела. Сейчас туннель даёт одышка; потом — Hodba.Sim.Body.</summary>
        public static PeripheryState From(in ExertionState ex, in PeripherySettings s)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(s.tunnelFrom, 1f, ex.Load));
            return new PeripheryState(
                Mathf.Clamp01(s.start - s.tunnelInward * t),
                Mathf.Clamp01(s.blur + s.tunnelBlur * t),
                Mathf.Clamp01(s.desaturate + 0.2f * t),
                s.tunnelDarken * t,
                s.radius);
        }
    }
}
