using System.Collections.Generic;
using System.Linq;
using Hodba.Client;
using Hodba.Client.Body;
using Hodba.Core;
using Hodba.Sim.Walk;
using Hodba.World;
using Hodba.World.Gen;
using NUnit.Framework;
using UnityEngine;

namespace Hodba.Tests
{
    /// <summary>Тело целиком, как его собирает игра, на полигоне: твёрдо → рыхло → подъём → плато.</summary>
    public class WalkerBodyTests
    {
        [Test]
        public void ProvingGround_WholeRoute()
        {
            var config = ScriptableObject.CreateInstance<FieldConfig>();
            try
            {
                var world = new ProvingGround(config.seed);
                var sim = new WalkSim(config.walk, new WorldPos(0, 0), 0f);
                var body = new WalkerBody(config, world);
                var steps = new List<StepEvent>();
                body.Events.Step += steps.Add;

                sim.Apply(Intent.Walk());
                float loadOnHard = 0f, loadOnTop = 0f;
                double hardSum = 0, looseSum = 0;
                int hardN = 0, looseN = 0;
                const float dt = 1f / 30f;
                for (int i = 0; i < 30 * 60 * 5 && sim.Position.Z < 240_000; i++)
                {
                    sim.Step(dt, world);
                    body.Tick(BodyContext.Walk(dt, body.Time + dt, sim, world));
                    float z = sim.Position.Z / 1000f;
                    if (z > 20f && z < 55f) { loadOnHard = body.Exertion.Load; hardSum += sim.Speed; hardN++; }
                    // Средняя по двум целым волнам: под горку по Тоблеру идут быстрее, в гору медленнее.
                    if (z > 80f && z < 120f) { looseSum += sim.Speed; looseN++; }
                    if (z > 205f) loadOnTop = Mathf.Max(loadOnTop, body.Exertion.Load);
                }
                float speedHard = (float)(hardSum / hardN), speedLoose = (float)(looseSum / looseN);

                TestContext.WriteLine($"твёрдо {speedHard:0.00} м/с, рыхло {speedLoose:0.00} м/с; " +
                                      $"одышка на ровном {loadOnHard:0.00}, после подъёма {loadOnTop:0.00}; шагов {steps.Count}");
                Assert.That(sim.Position.Z, Is.GreaterThan(215_000), "дошёл до плато");
                Assert.That(speedLoose, Is.LessThan(speedHard * 0.95f), "в рыхлом медленнее");
                Assert.That(loadOnTop, Is.GreaterThan(loadOnHard + 0.2f), "подъём даёт одышку");

                var loose = steps.Where(s => s.Felt && s.Contact.Z > 65_000 && s.Contact.Z < 125_000).ToList();
                var hard = steps.Where(s => s.Felt && s.Contact.Z > 5_000 && s.Contact.Z < 55_000).ToList();
                Assert.IsTrue(loose.All(s => s.Surface == SurfaceKind.FineAsh));
                Assert.IsTrue(hard.All(s => s.Surface == SurfaceKind.PackedAsh));
                Assert.IsFalse(float.IsNaN(body.Pose.Up));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void ProvingGround_ClimbIsSmooth()
        {
            var world = new ProvingGround(1);
            long prev = world.SampleHeightMm(0, 100_000);
            for (long z = 100_000; z < 240_000; z += 250)
            {
                long h = world.SampleHeightMm(0, z);
                Assert.That(h - prev, Is.InRange(-60, 60), $"без ступенек у {z / 1000} м");
                prev = h;
            }
            Assert.That(world.SampleHeightMm(0, 230_000) / 1000f, Is.EqualTo(80f * 0.12f).Within(0.05f));
        }
    }
}
