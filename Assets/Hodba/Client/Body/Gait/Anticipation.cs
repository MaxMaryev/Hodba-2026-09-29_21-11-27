using Hodba.Core;
using Hodba.World;
using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>Что впереди по курсу: тело чувствует землю заранее, а не узнаёт о ней пяткой.</summary>
    public readonly struct GroundAhead
    {
        public readonly float LoosenessHere;
        public readonly float LoosenessAhead;
        public readonly float SlopeAhead;

        public GroundAhead(float here, float ahead, float slopeAhead)
        {
            LoosenessHere = here;
            LoosenessAhead = ahead;
            SlopeAhead = slopeAhead;
        }

        /// <summary>0..1 — насколько земля впереди другая, чем под ногами.</summary>
        public float Change(float slopeHere) =>
            Mathf.Clamp01(Mathf.Abs(LoosenessAhead - LoosenessHere) * 1.5f + Mathf.Abs(SlopeAhead - slopeHere) * 4f);
    }

    public static class Anticipation
    {
        public static GroundAhead Look(in BodyContext ctx, float distance)
        {
            var sim = ctx.Sim;
            var world = ctx.World;
            var fwd = ctx.Forward;
            long ax = (long)(fwd.x * distance * 1000f), az = (long)(fwd.z * distance * 1000f);
            var p = sim.Position;
            var ahead = p.Offset(ax, az);
            var further = p.Offset(ax * 3 / 2, az * 3 / 2);

            float here = Loose(world, p);
            float there = Loose(world, ahead);
            float dh = (world.SampleHeightMm(further) - world.SampleHeightMm(ahead)) / 1000f;
            float slopeAhead = distance > 0f ? dh / (distance * 0.5f) : 0f;
            return new GroundAhead(here, there, slopeAhead);
        }

        static float Loose(IWorldQuery world, WorldPos p) => world.SampleSurface(p.X, p.Z).Looseness / 65535f;
    }
}
