using System.Collections.Generic;
using Hodba.Sim.Walk;
using UnityEngine;

namespace Hodba.Client.Body
{
    /// <summary>Ничем не примечательная точка горизонта. Самая частая цель: в пустыне упустить нечего.</summary>
    public sealed class HorizonSource : IGazeTargetSource
    {
        public void Collect(in BodyContext ctx, in AttentionInputs inputs, in EyeWanderSettings s, Habituation habituation, Rng rng,
            List<GazeCandidate> into)
        {
            float yaw = ctx.HeadYaw + rng.Range(-s.horizonYaw, s.horizonYaw);
            float pitch = rng.Range(0.3f, 2.5f); // чуть ниже линии горизонта
            into.Add(new GazeCandidate(GazeKind.Nothing, GazeSpace.World, yaw, pitch, s.horizonWeight, 60f, s.horizonDwell));
        }
    }

    /// <summary>Дорога в паре шагов. Чаще, когда тело насторожено или земля впереди меняется.</summary>
    public sealed class GroundSource : IGazeTargetSource
    {
        public void Collect(in BodyContext ctx, in AttentionInputs inputs, in EyeWanderSettings s, Habituation habituation, Rng rng,
            List<GazeCandidate> into)
        {
            float d = rng.Range(1.8f, 4f);
            float pitch = Mathf.Atan2(inputs.EyeHeight, d) * Mathf.Rad2Deg;
            // Если голова смотрит далеко в сторону, дорога под ногами вне поля глаз.
            float along = Mathf.Clamp01(Mathf.Cos(WalkSim.DeltaAngle(ctx.HeadYaw, ctx.Sim.Course) * Mathf.Deg2Rad));
            float weight = (s.groundBase + inputs.Caution * s.groundCaution + inputs.GroundChange * s.groundChange
                            + inputs.Roughness * s.groundRough) * along;
            into.Add(new GazeCandidate(GazeKind.Ground, GazeSpace.Body, rng.Range(-4f, 4f), pitch, weight, d, s.groundDwell));
        }
    }

    /// <summary>
    /// Камни и валуны в поле глаз. Тянут слегка и тем слабее, чем привычнее; валуны привыкают медленнее.
    /// Берутся из той же раскладки, что рисует рендер.
    /// </summary>
    public sealed class StoneSource : IGazeTargetSource
    {
        const int Best = 3;

        readonly uint _seed;
        readonly StoneLayout _stones, _boulders;
        readonly List<GazeCandidate> _found = new List<GazeCandidate>(64);

        public StoneSource(uint worldSeed, in StoneLayout stones, in StoneLayout boulders)
        {
            _seed = worldSeed;
            _stones = stones;
            _boulders = boulders;
        }

        public void Collect(in BodyContext ctx, in AttentionInputs inputs, in EyeWanderSettings s, Habituation habituation, Rng rng,
            List<GazeCandidate> into)
        {
            _found.Clear();
            Scan(ctx, inputs, s, habituation, _stones, GazeKind.Stone, s.stoneRadius, s.stoneWeight);
            Scan(ctx, inputs, s, habituation, _boulders, GazeKind.Boulder, s.boulderRadius, s.boulderWeight);
            _found.Sort((a, b) => b.Weight.CompareTo(a.Weight));
            for (int i = 0; i < _found.Count && i < Best; i++) into.Add(_found[i]);
        }

        void Scan(in BodyContext ctx, in AttentionInputs inputs, in EyeWanderSettings s, Habituation habituation,
            in StoneLayout layout, GazeKind kind, float radius, float weight)
        {
            long cellMm = layout.CellMm;
            if (cellMm <= 0 || weight <= 0f) return;
            var p = ctx.Sim.Position;
            var world = ctx.World;
            float eyeY = world.SampleHeightMm(p.X, p.Z) / 1000f + inputs.EyeHeight;
            long reach = (long)(radius * 1000f);
            long x0 = Hodba.Core.WorldPos.FloorDiv(p.X - reach, cellMm), x1 = Hodba.Core.WorldPos.FloorDiv(p.X + reach, cellMm);
            long z0 = Hodba.Core.WorldPos.FloorDiv(p.Z - reach, cellMm), z1 = Hodba.Core.WorldPos.FloorDiv(p.Z + reach, cellMm);
            float fresh = 1f - habituation.Familiarity(kind);

            for (long cz = z0; cz <= z1; cz++)
            for (long cx = x0; cx <= x1; cx++)
            {
                uint h = StoneField.CellHash(cx, cz, _seed, layout);
                int n = StoneField.Count(h, layout);
                for (int k = 0; k < n; k++)
                {
                    var stone = StoneField.Place(cx, cz, h, k, layout);
                    float dx = (stone.X - p.X) / 1000f, dz = (stone.Z - p.Z) / 1000f;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist < 2.5f || dist > radius) continue;

                    float yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                    if (Mathf.Abs(WalkSim.DeltaAngle(ctx.HeadYaw, yaw)) > s.maxYaw * 1.5f) continue;

                    float size = StoneField.NominalSize(stone, layout);
                    float top = world.SampleHeightMm(stone.X, stone.Z) / 1000f + size * 0.3f;
                    float pitch = Mathf.Atan2(eyeY - top, dist) * Mathf.Rad2Deg;
                    habituation.Seen(kind);

                    // Заметность — по видимому размеру: мелкий камень у ног и валун вдали цепляют похоже.
                    float angular = size / dist * Mathf.Rad2Deg;
                    float salience = Mathf.Sqrt(Mathf.Clamp01(angular / 1.5f));
                    _found.Add(new GazeCandidate(kind, GazeSpace.World, yaw, pitch, weight * fresh * salience, dist,
                        s.stoneDwell * (1f + fresh)));
                }
            }
        }
    }

    /// <summary>Прочь от раздражителя: солнце в лицо уводит взгляд в сторону и вниз, встречный ветер — вниз.</summary>
    public sealed class AversionSource : IGazeTargetSource
    {
        public void Collect(in BodyContext ctx, in AttentionInputs inputs, in EyeWanderSettings s, Habituation habituation, Rng rng,
            List<GazeCandidate> into)
        {
            var head = ctx.HeadForward;
            float sun = Glare.Stimulus(head, ctx.SunDirection, ctx.SunElevation, 4f);
            if (sun > 0.05f)
            {
                float sunYaw = Mathf.Atan2(ctx.SunDirection.x, ctx.SunDirection.z) * Mathf.Rad2Deg;
                float side = Mathf.Sign(WalkSim.DeltaAngle(sunYaw, ctx.HeadYaw));
                if (side == 0f) side = rng.Chance(0.5f) ? 1f : -1f;
                into.Add(new GazeCandidate(GazeKind.Away, GazeSpace.World,
                    ctx.HeadYaw + side * rng.Range(6f, 10f), ctx.HeadPitch + rng.Range(3f, 6f),
                    sun * s.sunAversion, 20f, 2f));
            }

            var wind = new Vector3(ctx.Wind.x, 0f, ctx.Wind.z);
            var flat = new Vector3(head.x, 0f, head.z);
            if (wind.sqrMagnitude > 1e-4f && flat.sqrMagnitude > 1e-4f)
            {
                float face = Mathf.Clamp01(Vector3.Dot(-wind.normalized, flat.normalized));
                float push = face * ctx.WindStrength * ctx.WindStrength;
                if (push > 0.05f)
                    into.Add(new GazeCandidate(GazeKind.Away, GazeSpace.World, ctx.HeadYaw + rng.Range(-3f, 3f),
                        ctx.HeadPitch + s.windDown * (1f + push), push, 15f, 2.5f));
            }
        }
    }
}
