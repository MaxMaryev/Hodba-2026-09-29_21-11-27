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
        readonly IWorldQuery _world;
        readonly Exertion _exertion;
        readonly Gait _gait;
        readonly BreathLayer _breath = new BreathLayer();
        readonly PostureLayer _posture;
        readonly HeadSpring _head = new HeadSpring();
        readonly MotionBudget _budget = new MotionBudget();
        readonly EyeWander _eyes;
        readonly Eyelids _eyelids;
        readonly SunIrritant _sun = new SunIrritant();
        double _time;

        /// <summary>Итоговое отклонение головы в этом кадре.</summary>
        public PoseDelta Pose { get; private set; }
        public GaitState Gait => _gait.State;
        public ExertionState Exertion => _exertion.State;
        /// <summary>Куда глаза смотрят сами, относительно головы.</summary>
        public EyeState Eyes => _eyes.State;
        public EyelidState Eyelids => _eyelids.State;
        /// <summary>Периферия: мыло к краям, туннель от одышки.</summary>
        public PeripheryState Periphery => Hodba.Client.Body.Periphery.From(_exertion.State, _config.periphery);
        public Habituation Habituation => _eyes.Habituation;
        /// <summary>0..1 — насколько солнце бьёт в глаз (до век).</summary>
        public float SunStimulus => _sun.Stimulus;
        /// <summary>0..1 — насколько приглушён фон ради события.</summary>
        public float Duck => _budget.Duck;
        public double Time => _time;

        public WalkerBody(FieldConfig config, IWorldQuery world)
        {
            _config = config;
            _world = world;
            uint seed = config.seed;
            var obstacles = new StoneObstacles(world.Info.Seed, config.StoneLayout);

            _exertion = new Exertion(seed);
            _gait = new Gait(seed, Events, obstacles);
            _posture = new PostureLayer(seed);

            _budget.Add(_gait);
            _budget.Add(_breath);
            _budget.Add(_posture);

            _eyes = new EyeWander(seed, Events);
            _eyes.Add(new HorizonSource());
            _eyes.Add(new GroundSource());
            _eyes.Add(new StoneSource(world.Info.Seed, config.StoneLayout, config.BoulderLayout));
            _eyes.Add(new AversionSource());

            _eyelids = new Eyelids(seed);
            _eyelids.Add(_sun);
            _eyelids.Add(new WindIrritant());

            Events.Step += e => _exertion.OnStep(e, _config.exertion);
            Events.Body += e => _exertion.OnBodyEvent(e, _config.exertion);
            Events.Body += e => _eyes.OnBodyEvent(e, _config.eyes);
            Events.Body += e => _eyelids.OnBodyEvent(e, _config.eyelids);
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

            var attention = new AttentionInputs(_exertion.State.Caution, _gait.State.AheadChange, c.eyeHeight, ctx.Sim.Roughness);
            _eyes.Tick(ctx, attention, c.eyes, _budget.Duck);

            var eyes = _eyes.State;
            var view = BodyContext.Direction(ctx.HeadYaw + eyes.Yaw, ctx.HeadPitch + eyes.Pitch);
            _eyelids.Tick(ctx, view, c.eyelids);
        }

        /// <summary>
        /// Игрок взял взгляд: глаза отдают свой взгляд голове (картинка не двигается) и замолкают.
        /// Намерение и курс это не трогает (<see cref="GazeController.Absorb"/>).
        /// </summary>
        public void YieldEyes(GazeController gaze)
        {
            var offset = _eyes.YieldToPlayer();
            gaze.Absorb(offset.x, offset.y, _config);
        }

        /// <summary>После телепорта: шаги с нуля, без шквала следов.</summary>
        public void Reset(WalkSim sim) =>
            _gait.Reset(sim.Distance, sim.Position, _world.SampleHeightMm(sim.Position.X, sim.Position.Z) / 1000f);
    }
}
