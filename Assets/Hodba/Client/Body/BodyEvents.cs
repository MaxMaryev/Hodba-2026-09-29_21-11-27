using System;
using Hodba.Core;
using Hodba.World;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Шаг — единственный источник для следа, звука, дыхания и движения тела.
    /// Кто его создал, не знает, кто его слушает.
    /// </summary>
    public readonly struct StepEvent
    {
        public readonly bool Left;
        /// <summary>Точка контакта этой стопы, а не положение человека.</summary>
        public readonly WorldPos Contact;
        /// <summary>Курс, ° — куда смотрит носок.</summary>
        public readonly float Course;
        /// <summary>Сила опоры: 1 — обычный шаг в полную силу, меньше — переступил, больше — тяжело.</summary>
        public readonly float Force;
        /// <summary>Поверхность под этой стопой.</summary>
        public readonly SurfaceKind Surface;
        /// <summary>0..1 под этой стопой.</summary>
        public readonly float Looseness;
        public readonly bool OnStone;
        public readonly bool Stumble;
        /// <summary>Нога шаркнула: осыпь, шорох.</summary>
        public readonly bool Scuff;
        /// <summary>Сколько длится этот шаг, с.</summary>
        public readonly float Duration;
        /// <summary>
        /// Шаг прочувствован телом и слышен. Если за длинный кадр прошло несколько шагов, прочувствован
        /// только последний, остальные — только следы: без пачки звуков и рывков.
        /// </summary>
        public readonly bool Felt;

        public StepEvent(bool left, WorldPos contact, float course, float force, SurfaceKind surface, float looseness,
            bool onStone, bool stumble, bool scuff, float duration, bool felt)
        {
            Left = left;
            Contact = contact;
            Course = course;
            Force = force;
            Surface = surface;
            Looseness = looseness;
            OnStone = onStone;
            Stumble = stumble;
            Scuff = scuff;
            Duration = duration;
            Felt = felt;
        }
    }

    public enum BodyEventKind
    {
        /// <summary>Тронулся с места.</summary>
        Start,
        /// <summary>Решил остановиться.</summary>
        Stop,
        /// <summary>Встал: корпус оседает, снаряжение догоняет.</summary>
        Settled,
        /// <summary>Переступил через камень или обошёл его шагом.</summary>
        StoneAvoided,
        Stumble,
        /// <summary>Взгляд крупно перевёл внимание — часто вместе с морганием.</summary>
        GazeShift,
        /// <summary>Сбился с ритма: подгонял себя не в такт шагу.</summary>
        Misstep,
    }

    public readonly struct BodyEvent
    {
        public readonly BodyEventKind Kind;
        /// <summary>0..1.</summary>
        public readonly float Strength;

        public BodyEvent(BodyEventKind kind, float strength)
        {
            Kind = kind;
            Strength = strength;
        }
    }

    /// <summary>Шина событий тела. Структуры по значению — без аллокаций.</summary>
    public sealed class BodyEvents
    {
        public event Action<StepEvent> Step;
        public event Action<BodyEvent> Body;

        public void Emit(in StepEvent e) => Step?.Invoke(e);
        public void Emit(in BodyEvent e) => Body?.Invoke(e);
    }
}
