namespace Hodba.Client.Body
{
    /// <summary>
    /// Голова — масса на шее поверх таза. Таз задаёт движение, голова догоняет с запаздыванием
    /// и лёгким перелётом. Это вторичное движение само по себе ломает идеальную периодичность
    /// и делает остановку и спотыкание телесными: голова «доезжает».
    /// </summary>
    public sealed class HeadSpring
    {
        readonly Spring _up = new Spring(), _side = new Spring(), _fwd = new Spring();
        readonly Spring _pitch = new Spring(), _yaw = new Spring(), _roll = new Spring();

        public PoseDelta Filter(in PoseDelta target, float dt, in PoseSettings s)
        {
            _up.Step(target.Up, dt, s.headHz, s.headDamping);
            _side.Step(target.Side, dt, s.headHz, s.headDamping);
            _fwd.Step(target.Forward, dt, s.headHz, s.headDamping);
            _pitch.Step(target.Pitch, dt, s.headRotHz, s.headRotDamping);
            _yaw.Step(target.Yaw, dt, s.headRotHz, s.headRotDamping);
            _roll.Step(target.Roll, dt, s.headRotHz, s.headRotDamping);
            return new PoseDelta(_up.Value, _side.Value, _fwd.Value, _pitch.Value, _yaw.Value, _roll.Value);
        }

        public void Reset(in PoseDelta p)
        {
            _up.Reset(p.Up);
            _side.Reset(p.Side);
            _fwd.Reset(p.Forward);
            _pitch.Reset(p.Pitch);
            _yaw.Reset(p.Yaw);
            _roll.Reset(p.Roll);
        }
    }
}
