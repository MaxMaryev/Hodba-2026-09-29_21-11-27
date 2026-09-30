using Hodba.Sim.Walk;
using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>Что случилось с ходом в этом кадре.</summary>
    public readonly struct TransitionFlags
    {
        /// <summary>Тронулся из стойки.</summary>
        public readonly bool Started;
        /// <summary>Решил остановиться (ещё идёт по инерции пару шагов).</summary>
        public readonly bool StopRequested;
        /// <summary>Встал окончательно.</summary>
        public readonly bool Settled;
        /// <summary>Стоя довернул корпус настолько, что пора переступить.</summary>
        public readonly bool Shuffle;

        public TransitionFlags(bool started, bool stopRequested, bool settled, bool shuffle)
        {
            Started = started;
            StopRequested = stopRequested;
            Settled = settled;
            Shuffle = shuffle;
        }
    }

    /// <summary>
    /// Начало и конец движения — отдельные маленькие сцены, а не включение и выключение синусоиды.
    /// Здесь только распознавание; как на них отвечает тело, решает походка.
    /// </summary>
    public sealed class Transitions
    {
        const float MovingSpeed = 0.05f;

        bool _wanted, _moving, _init;
        float _lastCourse, _turned;

        public bool Moving => _moving;

        public TransitionFlags Tick(WalkSim sim, float shuffleAngle)
        {
            bool moving = sim.Speed > MovingSpeed;
            if (!_init)
            {
                _init = true;
                _wanted = sim.WantsWalk;
                _moving = moving;
                _lastCourse = sim.Course;
            }

            bool started = sim.WantsWalk && !_wanted && !moving;
            bool stop = !sim.WantsWalk && _wanted && moving;
            bool settled = _moving && !moving;

            // Разворот стоя — переступаниями, а не вращением на месте.
            bool shuffle = false;
            if (!moving)
            {
                _turned += Mathf.Abs(WalkSim.DeltaAngle(_lastCourse, sim.Course));
                if (_turned >= shuffleAngle)
                {
                    shuffle = true;
                    _turned = 0f;
                }
            }
            else _turned = 0f;

            _lastCourse = sim.Course;
            _wanted = sim.WantsWalk;
            _moving = moving;
            return new TransitionFlags(started, stop, settled, shuffle);
        }
    }
}
