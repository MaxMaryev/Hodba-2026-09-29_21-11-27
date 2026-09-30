namespace Hodba.Client.Body
{
    /// <summary>
    /// Человек и стоя не статуя: едва заметно покачивается, переносит вес, чуть клонит голову.
    /// Фоновый слой — глохнет, пока идёт событие.
    /// </summary>
    public sealed class PostureLayer : IMotionLayer
    {
        readonly PinkNoise _side, _fwd, _pitch, _roll;

        public MotionRole Role => MotionRole.Background;
        public PoseDelta Pose { get; private set; }
        public float EventStrength => 0f;

        public PostureLayer(uint seed)
        {
            _side = new PinkNoise(seed, Salts.Posture + 1, 0.04f, 5);
            _fwd = new PinkNoise(seed, Salts.Posture + 2, 0.04f, 5);
            _pitch = new PinkNoise(seed, Salts.Posture + 3, 0.03f, 5);
            _roll = new PinkNoise(seed, Salts.Posture + 4, 0.03f, 5);
        }

        public void Tick(in BodyContext ctx, in PoseSettings s, float gaitBlend)
        {
            float k = 1f - (1f - s.postureWhileWalking) * gaitBlend;
            double t = ctx.Time;
            Pose = new PoseDelta(
                0f,
                _side.Sample(t) * s.postureSway * k,
                _fwd.Sample(t) * s.postureSway * k,
                _pitch.Sample(t) * s.posturePitch * k,
                0f,
                _roll.Sample(t) * s.postureRoll * k);
        }
    }
}
