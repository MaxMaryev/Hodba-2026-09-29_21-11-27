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
        public readonly float RoughnessHere;
        public readonly float RoughnessAhead;

        public GroundAhead(float here, float ahead, float slopeAhead, float roughHere, float roughAhead)
        {
            LoosenessHere = here;
            LoosenessAhead = ahead;
            SlopeAhead = slopeAhead;
            RoughnessHere = roughHere;
            RoughnessAhead = roughAhead;
        }

        /// <summary>0..1 — насколько земля впереди другая, чем под ногами.</summary>
        public float Change(float slopeHere) =>
            Mathf.Clamp01(Mathf.Abs(LoosenessAhead - LoosenessHere) * 1.5f + Mathf.Abs(SlopeAhead - slopeHere) * 4f
                          + Mathf.Max(0f, RoughnessAhead - RoughnessHere));
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

            var sHere = world.SampleSurface(p.X, p.Z);
            var sThere = world.SampleSurface(ahead.X, ahead.Z);
            // Уклон впереди — по разнесённым точкам, чтобы рябь под ногами не казалась склоном.
            var far = p.Offset(ax * 2, az * 2);
            float dh = (world.SampleHeightMm(far) + world.SampleHeightMm(further) - 2 * world.SampleHeightMm(p)) / 1000f;
            float slopeAhead = distance > 0f ? dh / (distance * 3.5f) : 0f;
            return new GroundAhead(sHere.Looseness / 65535f, sThere.Looseness / 65535f, slopeAhead,
                sHere.Roughness / 65536f, sThere.Roughness / 65536f);
        }
    }
}
