using System.Collections.Generic;
using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Единственное место, где слои движения складываются. По отдельности шаг, дыхание, дрейф
    /// и реакция на камень тонкие, а совпав пиками, дали бы неожиданный кивок. Поэтому:
    /// фон глохнет, пока идёт событие (событие читается), а итог мягко упирается в предел
    /// по величине и по скорости изменения.
    /// </summary>
    public sealed class MotionBudget
    {
        readonly List<IMotionLayer> _layers = new List<IMotionLayer>();
        PoseDelta _last;
        bool _hasLast;

        /// <summary>0..1 — насколько сейчас приглушён фон.</summary>
        public float Duck { get; private set; }

        /// <summary>Сила самого выраженного события среди слоёв.</summary>
        public float EventStrength { get; private set; }

        public void Add(IMotionLayer layer) => _layers.Add(layer);

        /// <summary>Сумма слоёв; фоновые приглушены по силе текущего события.</summary>
        public PoseDelta Mix(float dt, in PoseSettings s)
        {
            float ev = 0f;
            foreach (var l in _layers) ev = Mathf.Max(ev, l.EventStrength);
            EventStrength = ev;

            float tau = ev > Duck ? s.duckRise : s.duckFall;
            Duck = Mathf.Lerp(Duck, ev, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, tau)));

            float background = 1f - s.duckDepth * Duck;
            var sum = PoseDelta.Zero;
            foreach (var l in _layers)
                sum += l.Role == MotionRole.Background ? l.Pose * background : l.Pose;
            return sum;
        }

        /// <summary>Мягкий предел по величине и по скорости.</summary>
        public PoseDelta Limit(PoseDelta p, float dt, in PoseSettings s)
        {
            var o = s.offsetLimit;
            var a = s.angleLimit;
            var limited = new PoseDelta(
                SoftLimit.Apply(p.Up, o.x),
                SoftLimit.Apply(p.Side, o.y),
                SoftLimit.Apply(p.Forward, o.z),
                SoftLimit.Apply(p.Pitch, a.x),
                SoftLimit.Apply(p.Yaw, a.y),
                SoftLimit.Apply(p.Roll, a.z));

            if (!_hasLast || dt <= 0f)
            {
                _last = limited;
                _hasLast = true;
                return limited;
            }

            _last = new PoseDelta(
                SoftLimit.Rate(_last.Up, limited.Up, s.offsetRate, dt),
                SoftLimit.Rate(_last.Side, limited.Side, s.offsetRate, dt),
                SoftLimit.Rate(_last.Forward, limited.Forward, s.offsetRate, dt),
                SoftLimit.Rate(_last.Pitch, limited.Pitch, s.angleRate, dt),
                SoftLimit.Rate(_last.Yaw, limited.Yaw, s.angleRate, dt),
                SoftLimit.Rate(_last.Roll, limited.Roll, s.angleRate, dt));
            return _last;
        }
    }
}
