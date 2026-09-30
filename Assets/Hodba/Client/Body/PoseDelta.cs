namespace Hodba.Client.Body
{
    /// <summary>
    /// Отклонение головы от «идеального» положения: смещение в осях тела (м) и повороты (°).
    /// Оси тела: вверх, вправо от курса, вперёд по курсу. Тангаж плюс — вниз, как в Unity.
    /// </summary>
    public struct PoseDelta
    {
        public float Up, Side, Forward;
        public float Pitch, Yaw, Roll;

        public PoseDelta(float up, float side, float forward, float pitch, float yaw, float roll)
        {
            Up = up;
            Side = side;
            Forward = forward;
            Pitch = pitch;
            Yaw = yaw;
            Roll = roll;
        }

        public static readonly PoseDelta Zero = default;

        public static PoseDelta operator +(PoseDelta a, PoseDelta b) =>
            new PoseDelta(a.Up + b.Up, a.Side + b.Side, a.Forward + b.Forward, a.Pitch + b.Pitch, a.Yaw + b.Yaw, a.Roll + b.Roll);

        public static PoseDelta operator *(PoseDelta a, float k) =>
            new PoseDelta(a.Up * k, a.Side * k, a.Forward * k, a.Pitch * k, a.Yaw * k, a.Roll * k);

        public override string ToString() =>
            $"up {Up * 1000f:0.0} мм, side {Side * 1000f:0.0} мм, fwd {Forward * 1000f:0.0} мм, " +
            $"pitch {Pitch:0.00}°, yaw {Yaw:0.00}°, roll {Roll:0.00}°";
    }
}
