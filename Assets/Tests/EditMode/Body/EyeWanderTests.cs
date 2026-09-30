using System;
using System.Collections.Generic;
using System.Linq;
using Hodba.Client;
using Hodba.Client.Body;
using Hodba.Core;
using Hodba.Sim.Walk;
using NUnit.Framework;
using UnityEngine;

namespace Hodba.Tests
{
    public class EyeWanderTests
    {
        /// <summary>Игра в миниатюре: ввод → глаза уступают → голова → шаг → тело. Как в Bootstrap.</summary>
        sealed class Walker : IDisposable
        {
            public readonly FieldConfig Config = ScriptableObject.CreateInstance<FieldConfig>();
            public readonly FlatWorld World = new FlatWorld();
            public readonly WalkSim Sim;
            public readonly GazeController Gaze = new GazeController(0f);
            public readonly WalkerBody Body;
            bool _looking;

            public Walker()
            {
                Sim = new WalkSim(Config.walk, new WorldPos(0, 0), 0f);
                Body = new WalkerBody(Config, World);
            }

            /// <summary>Куда сейчас смотрит картинка (без покачивания), °.</summary>
            public Vector2 View => new Vector2(Gaze.Yaw + Body.Eyes.Yaw, Gaze.Pitch + Body.Eyes.Pitch);

            public void Tick(float dt, Vector2 look = default, bool looking = false, bool focus = false)
            {
                if (looking && !_looking) Body.YieldEyes(Gaze);
                _looking = looking;
                Gaze.Tick(look, looking, false, focus, Sim, Config, dt);
                Sim.Step(dt, World);
                Body.Tick(new BodyContext(dt, Body.Time + dt, Sim, World, Vector3.zero, 0f, 0f,
                    new Vector3(0f, 0.5f, -1f).normalized, 20f, Gaze.Yaw, Gaze.Pitch, looking, Gaze.FocusBlend));
            }

            public void Dispose() => UnityEngine.Object.DestroyImmediate(Config);
        }

        [Test]
        public void Wander_StaysSmallSmoothAndAlive()
        {
            using (var w = new Walker())
            {
                w.Sim.Apply(Intent.Walk());
                var s = w.Config.eyes;
                const float dt = 1f / 30f;
                var yaws = new List<float>();
                float maxSpeed = 0f;
                int slow = 0, down = 0, n = 0;
                var prev = new Vector2(w.Body.Eyes.Yaw, w.Body.Eyes.Pitch);
                for (int i = 0; i < 30 * 60 * 10; i++)
                {
                    w.Tick(dt);
                    var e = new Vector2(w.Body.Eyes.Yaw, w.Body.Eyes.Pitch);
                    if (i > 30 * 10)
                    {
                        float speed = (e - prev).magnitude / dt;
                        maxSpeed = Mathf.Max(maxSpeed, speed);
                        if (speed < 0.3f) slow++;
                        if (e.y > 10f) down++;
                        yaws.Add(e.x);
                        n++;
                        Assert.That(Mathf.Abs(e.x), Is.LessThanOrEqualTo(s.maxYaw + 1e-3f));
                        Assert.That(e.y, Is.InRange(-s.maxUp - 1e-3f, s.maxDown + 1e-3f));
                    }
                    prev = e;
                }
                float mean = yaws.Average();
                float sd = Mathf.Sqrt(yaws.Select(y => (y - mean) * (y - mean)).Average());
                TestContext.WriteLine($"рыск σ {sd:0.00}°, быстрее всего {maxSpeed:0.0}°/с, почти стоит {100f * slow / n:0}% времени, " +
                                      $"под ногами {100f * down / n:0.0}% времени, камни знакомы на {w.Body.Habituation.Familiarity(GazeKind.Stone):0.00}");
                Assert.That(sd, Is.GreaterThan(0.8f), "живой");
                Assert.That(maxSpeed, Is.LessThan(45f), "без рывков");
                Assert.That(slow, Is.GreaterThan(n / 50), "иногда задерживается");
                Assert.That(down, Is.GreaterThan(0), "бывает, смотрит под ноги");
                Assert.That(down, Is.LessThan(n / 4), "но не всё время");
            }
        }

        /// <summary>Главное требование: передача взгляда игроку не двигает картинку и не меняет курс.</summary>
        [Test]
        public void HandOver_KeepsTheImage_AndNeverTurnsTheCourse()
        {
            using (var w = new Walker())
            {
                w.Sim.Apply(Intent.Walk());
                const float dt = 1f / 30f;
                w.Tick(dt);
                float worstJump = 0f;
                float absorbed = 0f;
                for (int touch = 0; touch < 60; touch++)
                {
                    for (int i = 0; i < 90; i++) w.Tick(dt); // 3 с блуждает
                    var before = w.View;
                    absorbed = Mathf.Max(absorbed, Mathf.Abs(w.Body.Eyes.Yaw));
                    w.Tick(dt, Vector2.zero, true); // коснулся, не ведя
                    worstJump = Mathf.Max(worstJump, Mathf.Abs(Mathf.DeltaAngle(before.x, w.View.x)), Mathf.Abs(before.y - w.View.y));
                    for (int i = 0; i < 6; i++) w.Tick(dt, Vector2.zero, true);
                    Assert.AreEqual(0f, w.Gaze.Dwell, 1e-4f, "перенос не копит «смотрю в сторону»");
                }
                TestContext.WriteLine($"худший скачок картинки при передаче {worstJump:0.000}°, глаза отдавали до {absorbed:0.0}°, " +
                                      $"намерение {w.Gaze.IntentYaw:0.0}°, голова {w.Gaze.Yaw:0.0}°");
                Assert.That(worstJump, Is.LessThan(0.2f), "картинка не прыгает");
                Assert.AreEqual(0f, Mathf.DeltaAngle(0f, w.Sim.TargetCourse), 1e-3f, "курс не ушёл за блуждающими глазами");
                Assert.AreEqual(0f, Mathf.DeltaAngle(0f, w.Gaze.IntentYaw), 1e-3f);
            }
        }

        [Test]
        public void RealDrag_BecomesIntent_AndTurnsTheCourse()
        {
            using (var w = new Walker())
            {
                w.Sim.Apply(Intent.Walk());
                const float dt = 1f / 30f;
                for (int i = 0; i < 90; i++) w.Tick(dt);
                for (int i = 0; i < 10; i++) w.Tick(dt, new Vector2(3f, 0f), true); // увёл на 30°
                for (int i = 0; i < 30 * 4; i++) w.Tick(dt, Vector2.zero, true); // держит
                Assert.That(Mathf.DeltaAngle(0f, w.Sim.TargetCourse), Is.GreaterThan(20f), "долгий взгляд — поворот");
            }
        }

        [Test]
        public void EyesYield_ThenComeBackGently()
        {
            using (var w = new Walker())
            {
                const float dt = 1f / 30f;
                for (int i = 0; i < 300; i++) w.Tick(dt);
                w.Tick(dt, Vector2.zero, true);
                for (int i = 0; i < 30; i++)
                {
                    w.Tick(dt, new Vector2(0.1f, 0f), true);
                    Assert.AreEqual(0f, w.Body.Eyes.Gain, "пока ведёт игрок — глаза молчат");
                }
                float worst = 0f;
                var prev = new Vector2(w.Body.Eyes.Yaw, w.Body.Eyes.Pitch);
                for (int i = 0; i < 30 * 15; i++)
                {
                    w.Tick(dt);
                    var e = new Vector2(w.Body.Eyes.Yaw, w.Body.Eyes.Pitch);
                    worst = Mathf.Max(worst, (e - prev).magnitude);
                    prev = e;
                }
                Assert.That(w.Body.Eyes.Gain, Is.EqualTo(1f).Within(1e-3f), "вернулись");
                Assert.That(worst, Is.LessThan(1f), "без рывка, ° за кадр");
            }
        }

        [Test]
        public void Focus_FreezesTheEyes()
        {
            using (var w = new Walker())
            {
                const float dt = 1f / 30f;
                for (int i = 0; i < 300; i++) w.Tick(dt);
                for (int i = 0; i < 60; i++) w.Tick(dt, default, false, true); // всмотрелся
                var held = new Vector2(w.Body.Eyes.Yaw, w.Body.Eyes.Pitch);
                for (int i = 0; i < 90; i++) w.Tick(dt, default, false, true);
                var after = new Vector2(w.Body.Eyes.Yaw, w.Body.Eyes.Pitch);
                Assert.That((after - held).magnitude, Is.LessThan(0.3f));
            }
        }

        /// <summary>Тысяча похожих камней не даёт тысячи одинаковых фиксаций.</summary>
        [Test]
        public void Stones_LoseTheirPull_OverHours()
        {
            var s = EyeWanderSettings.Default;
            var world = new FlatWorld();
            var sim = new WalkSim(WalkParams.Default, new WorldPos(0, 0), 0f);
            var eyes = new EyeWander(3, null);
            var dense = new StoneLayout(8f, 3f, 0f, false, new Vector2(0.1f, 0.4f));
            eyes.Add(new HorizonSource());
            eyes.Add(new StoneSource(7, dense, new StoneLayout(128f, 1f, 0f, true, new Vector2(0.6f, 1.8f))));
            sim.Apply(Intent.Walk());

            const float dt = 0.1f;
            double t = 0;
            int Count(float minutes)
            {
                int fixations = 0;
                var last = GazeKind.Nothing;
                for (int i = 0; i < minutes * 600; i++)
                {
                    sim.Step(dt, world);
                    t += dt;
                    eyes.Tick(new BodyContext(dt, t, sim, world, Vector3.zero, 0f, 0f, Vector3.down, -20f, sim.Course, 4f, false, 0f),
                        new AttentionInputs(0f, 0f, 1.65f), s, 0f);
                    var kind = eyes.State.Kind;
                    if (kind == GazeKind.Stone && last != GazeKind.Stone) fixations++;
                    last = kind;
                }
                return fixations;
            }

            int early = Count(15f);
            Count(180f);
            int late = Count(15f);
            TestContext.WriteLine($"фиксаций на камнях за 15 минут: сначала {early}, через три часа {late}, переводов внимания {eyes.Shifts}; " +
                                  $"знакомость {eyes.Habituation.Familiarity(GazeKind.Stone):0.00}");
            Assert.That(early, Is.GreaterThan(5));
            Assert.That(late, Is.LessThan(early * 0.6f));
            Assert.That(late, Is.GreaterThan(0), "совсем не пропадают");
        }
    }
}
