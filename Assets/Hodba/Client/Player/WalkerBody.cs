using Hodba.Client.Body;
using Hodba.Sim.Walk;
using Hodba.World;

namespace Hodba.Client
{
    /// <summary>
    /// Тело путника: собирает модули в порядке и раздаёт их состояния потребителям (камера, тень, звук, отладка).
    /// Модули друг друга не знают — связь только через контекст, состояния и шину событий.
    /// Новый слой тела добавляется здесь одной строкой; Bootstrap от этого не растёт.
    /// </summary>
    public sealed class WalkerBody
    {
        public readonly BodyEvents Events = new BodyEvents();

        readonly FieldConfig _config;
        readonly Exertion _exertion;
        readonly Gait _gait;
        readonly BreathLayer _breath = new BreathLayer();
        readonly PostureLayer _posture;
        readonly HeadSpring _head = new HeadSpring();
        readonly MotionBudget _budget = new MotionBudget();
        double _time;

        /// <summary>Итоговое отклонение головы в этом кадре.</summary>
        public PoseDelta Pose { get; private set; }
        public GaitState Gait => _gait.State;
        public ExertionState Exertion => _exertion.State;
        /// <summary>0..1 — насколько приглушён фон ради события.</summary>
        public float Duck => _budget.Duck;
        public double Time => _time;

        public WalkerBody(FieldConfig config, IWorldQuery world)
        {
            _config = config;
            uint seed = config.seed;
            var obstacles = new StoneObstacles(world.Info.Seed, config.StoneLayout);

            _exertion = new Exertion(seed);
            _gait = new Gait(seed, Events, obstacles);
            _posture = new PostureLayer(seed);

            _budget.Add(_gait);
            _budget.Add(_breath);
            _budget.Add(_posture);

            Events.Step += e => _exertion.OnStep(e, _config.exertion);
            Events.Body += e => _exertion.OnBodyEvent(e, _config.exertion);
        }

        public BodyContext Context(float dt, WalkSim sim, IWorldQuery world, Wind wind, SkyClock clock, GazeController gaze, bool lookInput) =>
            new BodyContext(dt, _time + dt, sim, world,
                wind != null ? wind.Velocity : default, wind?.Strength ?? 0f, wind?.Gust ?? 0f,
                clock != null ? clock.SunDirection : default, clock?.Elevation ?? 45f,
                gaze.Yaw, gaze.Pitch, lookInput, gaze.FocusBlend);

        public void Tick(in BodyContext ctx)
        {
            _time = ctx.Time;
            var c = _config;

            _exertion.Tick(ctx, c.exertion, _gait.State);
            _gait.Tick(ctx, c.gait, _exertion.State);
            _breath.Tick(_exertion.State, c.exertion);
            _posture.Tick(ctx, c.pose, _gait.State.Blend);

            var mixed = _budget.Mix(ctx.Dt, c.pose);
            var head = _head.Filter(mixed, ctx.Dt, c.pose);
            Pose = _budget.Limit(head, ctx.Dt, c.pose);
        }

        /// <summary>После телепорта: шаги с нуля, без шквала следов.</summary>
        public void Reset(WalkSim sim) => _gait.Reset(sim.Distance);
    }
}
