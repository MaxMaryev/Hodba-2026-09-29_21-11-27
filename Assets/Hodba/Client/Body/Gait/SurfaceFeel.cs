using System;
using Hodba.World;
using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Как ощущается поверхность под стопой: проседание, отдача, звук, осторожность.
    /// Новая поверхность — новая строка в таблице <see cref="GaitSettings.surfaces"/>, код не трогаем.
    /// </summary>
    [Serializable]
    public struct SurfaceFeel
    {
        public SurfaceKind kind;
        [Tooltip("Насколько нога проседает при полной рыхлости, м. Провал с задержкой после удара.")]
        public float sink;
        [Tooltip("Сила удара пятки, доля от обычной. Твёрдо — упругий короткий, рыхло — глухой.")]
        public float impact;
        [Tooltip("Множитель вертикали: отталкивание. Рыхло — вяло.")]
        public float bounce;
        [Tooltip("Множитель длины шага.")]
        public float stride;
        [Tooltip("0..1 — насколько поверхность сама по себе заставляет осторожничать.")]
        public float caution;

        [Header("Звук")]
        public float soundGain;
        public float soundPitch;
        [Tooltip("Срез высоких, Гц. Рыхло — глуше.")]
        public float soundCutoff;
        [Tooltip("Вероятность шороха осыпи на шаге.")]
        [Range(0f, 1f)] public float scuff;

        public static SurfaceFeel[] Defaults() => new[]
        {
            new SurfaceFeel
            {
                kind = SurfaceKind.FineAsh, sink = 0.014f, impact = 0.45f, bounce = 0.85f, stride = 0.95f, caution = 0.3f,
                soundGain = 0.85f, soundPitch = 0.94f, soundCutoff = 3200f, scuff = 0.6f,
            },
            new SurfaceFeel
            {
                kind = SurfaceKind.PackedAsh, sink = 0.003f, impact = 1f, bounce = 1f, stride = 1f, caution = 0.03f,
                soundGain = 1f, soundPitch = 1.03f, soundCutoff = 9000f, scuff = 0.3f,
            },
            new SurfaceFeel
            {
                kind = SurfaceKind.Stone, sink = 0f, impact = 1.2f, bounce = 1.02f, stride = 0.97f, caution = 0.2f,
                soundGain = 1.05f, soundPitch = 1.1f, soundCutoff = 12000f, scuff = 0.3f,
            },
        };

        /// <summary>Строка для вида поверхности; если её нет — нейтральная.</summary>
        public static SurfaceFeel Find(SurfaceFeel[] table, SurfaceKind kind)
        {
            if (table != null)
                foreach (var f in table)
                    if (f.kind == kind) return f;
            return new SurfaceFeel
            {
                kind = kind, sink = 0f, impact = 1f, bounce = 1f, stride = 1f, caution = 0f,
                soundGain = 1f, soundPitch = 1f, soundCutoff = 9000f, scuff = 0.1f,
            };
        }
    }
}
