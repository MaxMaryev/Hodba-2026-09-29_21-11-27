using System.Collections.Generic;
using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>Что это за цель — ключ привыкания. Новая встреча — новый вид; привыкание общее для всех источников.</summary>
    public enum GazeKind
    {
        /// <summary>Ничего: горизонт, пустой участок.</summary>
        Nothing,
        Ground,
        Stone,
        Boulder,
        /// <summary>Прочь от раздражителя.</summary>
        Away,
    }

    public enum GazeSpace
    {
        /// <summary>Точка в мире: рыск по миру, взгляд держит её, пока человек идёт и голова качается.</summary>
        World,
        /// <summary>Точка у тела (например, дорога в двух шагах): рыск от курса.</summary>
        Body,
        /// <summary>Смещение от головы, без привязки к миру.</summary>
        Head,
    }

    /// <summary>Куда можно посмотреть. Тангаж — по миру (плюс вниз), рыск — по пространству.</summary>
    public readonly struct GazeCandidate
    {
        public readonly GazeKind Kind;
        public readonly GazeSpace Space;
        public readonly float Yaw;
        public readonly float Pitch;
        public readonly float Weight;
        /// <summary>Как далеко, м — для гашения тряски взглядом.</summary>
        public readonly float Distance;
        /// <summary>Медиана задержки, с.</summary>
        public readonly float Dwell;

        public GazeCandidate(GazeKind kind, GazeSpace space, float yaw, float pitch, float weight, float distance, float dwell)
        {
            Kind = kind;
            Space = space;
            Yaw = yaw;
            Pitch = pitch;
            Weight = weight;
            Distance = distance;
            Dwell = dwell;
        }
    }

    /// <summary>Что внимание знает о теле, кроме контекста кадра.</summary>
    public readonly struct AttentionInputs
    {
        public readonly float Caution;
        public readonly float GroundChange;
        public readonly float EyeHeight;
        /// <summary>0..1 — неровность под ногами: на буграх чаще смотрят под ноги.</summary>
        public readonly float Roughness;
        /// <summary>0..1 — насколько подгоняет себя: взгляд под ноги, по сторонам почти не смотрит.</summary>
        public readonly float Haste;

        public AttentionInputs(float caution, float groundChange, float eyeHeight, float roughness = 0f, float haste = 0f)
        {
            Caution = caution;
            GroundChange = groundChange;
            EyeHeight = eyeHeight;
            Roughness = roughness;
            Haste = haste;
        }

        /// <summary>Сколько веса остаётся у всего, что не под ногами: спешка — потеря внимания (закон 8).</summary>
        public float Wander(in EyeWanderSettings s) => 1f - Mathf.Clamp01(Haste * s.hasteNarrow);
    }

    /// <summary>
    /// Источник целей для взгляда. Горизонт, земля, камни, уклонение от солнца — каждый сам по себе.
    /// Встречи, дым, чужой силуэт потом — новый источник, взгляд не меняется.
    /// </summary>
    public interface IGazeTargetSource
    {
        void Collect(in BodyContext ctx, in AttentionInputs inputs, in EyeWanderSettings s, Habituation habituation, Rng rng,
            List<GazeCandidate> into);
    }

    /// <summary>
    /// Привыкание: знакомое теряет вес. Растёт от фиксаций и — медленно — просто от присутствия
    /// однотипного вокруг: неделю идущему среди камней не нужно сперва осмотреть каждый.
    /// Забывается часами.
    /// </summary>
    public sealed class Habituation
    {
        readonly float[] _familiar = new float[System.Enum.GetValues(typeof(GazeKind)).Length];
        readonly int[] _seen = new int[System.Enum.GetValues(typeof(GazeKind)).Length];

        public float Familiarity(GazeKind kind) => _familiar[(int)kind];

        /// <summary>Сколько однотипного в поле зрения прямо сейчас (источники отмечают при сборе).</summary>
        public void Seen(GazeKind kind) => _seen[(int)kind]++;

        public void Fixate(GazeKind kind, in EyeWanderSettings s)
        {
            float gain = s.fixationGain * Resistance(kind, s);
            ref float f = ref _familiar[(int)kind];
            f += gain * (1f - f);
        }

        /// <summary>Прошло dt секунд с прошлого сбора: присутствие и забывание.</summary>
        public void Tick(float dt, in EyeWanderSettings s)
        {
            float forget = Mathf.Exp(-dt * 0.6931f / Mathf.Max(0.01f, s.habituationHours * 3600f));
            for (int k = 0; k < _familiar.Length; k++)
            {
                float crowd = Mathf.Clamp01(_seen[k] / 10f);
                float gain = s.presenceGain * dt * crowd * Resistance((GazeKind)k, s);
                _familiar[k] = Mathf.Clamp01((_familiar[k] + gain * (1f - _familiar[k])) * forget);
                _seen[k] = 0;
            }
        }

        static float Resistance(GazeKind kind, in EyeWanderSettings s) =>
            kind == GazeKind.Boulder ? s.boulderHabituation : 1f;
    }
}
