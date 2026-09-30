using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Точка, которая догоняет цель на плоскости и сохраняет скорость, когда цель сменилась.
    /// Поэтому путь идёт дугами, а не «от остановки к остановке». Скорость мягко ограничена сверху.
    /// </summary>
    public sealed class Follower
    {
        readonly Spring _x = new Spring();
        readonly Spring _y = new Spring();

        public Vector2 Value => new Vector2(_x.Value, _y.Value);
        public Vector2 Velocity => new Vector2(_x.Velocity, _y.Velocity);

        public void Step(Vector2 target, float dt, float hz, float damping, float maxSpeed)
        {
            _x.Step(target.x, dt, hz, damping);
            _y.Step(target.y, dt, hz, damping);

            if (maxSpeed <= 0f) return;
            var v = Velocity;
            float speed = v.magnitude;
            if (speed <= 1e-5f) return;
            float limited = SoftLimit.Apply(speed, maxSpeed);
            if (limited >= speed) return;
            float k = limited / speed;
            _x.Velocity *= k;
            _y.Velocity *= k;
        }

        public void Reset(Vector2 value)
        {
            _x.Reset(value.x);
            _y.Reset(value.y);
        }

        /// <summary>Сдвинуть точку, не трогая скорость (перенос смещения в другую систему координат).</summary>
        public void Shift(Vector2 delta)
        {
            _x.Value += delta.x;
            _y.Value += delta.y;
        }
    }
}
