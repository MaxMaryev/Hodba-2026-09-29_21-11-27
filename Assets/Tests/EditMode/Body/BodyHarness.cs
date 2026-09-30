using System.Collections.Generic;
using Hodba.Client.Body;
using Hodba.Core;
using Hodba.Sim.Walk;
using Hodba.World;

namespace Hodba.Tests
{
    sealed class FlatWorld : IWorldQuery
    {
        readonly int _looseness;
        public FlatWorld(int looseness = 10_000) { _looseness = looseness; }
        public WorldInfo Info => new WorldInfo(7, 0, "flat");
        public long SampleHeightMm(long xMm, long zMm) => 0;
        public SurfaceSample SampleSurface(long xMm, long zMm) => new SurfaceSample(SurfaceKind.PackedAsh, _looseness);
    }

    sealed class HillWorld : IWorldQuery
    {
        public WorldInfo Info => new WorldInfo(7, 0, "hill");
        public long SampleHeightMm(long xMm, long zMm) => zMm < 0 ? 0 : zMm * 15 / 100; // 15% в гору на север
        public SurfaceSample SampleSurface(long xMm, long zMm) => new SurfaceSample(SurfaceKind.PackedAsh, 10_000);
    }

    /// <summary>Тело целиком, как в игре, но без Unity-сцены: модули, бюджет, голова.</summary>
    sealed class BodyHarness
    {
        public readonly WalkSim Sim;
        public readonly IWorldQuery World;
        public readonly BodyEvents Events = new BodyEvents();
        public readonly Exertion Exertion;
        public readonly Gait Gait;
        public readonly BreathLayer Breath = new BreathLayer();
        public readonly PostureLayer Posture;
        public readonly HeadSpring Head = new HeadSpring();
        public readonly MotionBudget Budget = new MotionBudget();
        public GaitSettings GaitSettings = GaitSettings.Default;
        public ExertionSettings ExertionSettings = ExertionSettings.Default;
        public PoseSettings PoseSettings = PoseSettings.Default;
        public readonly List<StepEvent> Steps = new List<StepEvent>();
        public readonly List<double> StepTimes = new List<double>();
        public readonly List<BodyEvent> BodyEvents = new List<BodyEvent>();
        public PoseDelta Pose;
        public double Time;

        public BodyHarness(IWorldQuery world, IObstacleQuery obstacles = null, uint seed = 1, float course = 0f)
        {
            World = world;
            Sim = new WalkSim(WalkParams.Default, new WorldPos(0, 0), course);
            Exertion = new Exertion(seed);
            Gait = new Gait(seed, Events, obstacles);
            Posture = new PostureLayer(seed);
            Budget.Add(Gait);
            Budget.Add(Breath);
            Budget.Add(Posture);
            Events.Step += e =>
            {
                Steps.Add(e);
                if (e.Felt) StepTimes.Add(Time);
                Exertion.OnStep(e, ExertionSettings);
            };
            Events.Body += e =>
            {
                BodyEvents.Add(e);
                Exertion.OnBodyEvent(e, ExertionSettings);
            };
        }

        public void Tick(float dt)
        {
            Sim.Step(dt, World);
            Time += dt;
            var ctx = BodyContext.Walk(dt, Time, Sim, World);
            Exertion.Tick(ctx, ExertionSettings, Gait.State);
            Gait.Tick(ctx, GaitSettings, Exertion.State);
            Breath.Tick(Exertion.State, ExertionSettings);
            Posture.Tick(ctx, PoseSettings, Gait.State.Blend);
            var mixed = Budget.Mix(dt, PoseSettings);
            Pose = Budget.Limit(Head.Filter(mixed, dt, PoseSettings), dt, PoseSettings);
        }

        public void Run(float seconds, float dt)
        {
            int n = (int)System.Math.Round(seconds / dt);
            for (int i = 0; i < n; i++) Tick(dt);
        }
    }
}
