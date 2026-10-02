using Hodba.Core;
using Hodba.World;
using Hodba.World.Gen;
using Hodba.Sim.Walk;
using NUnit.Framework;
using Hodba.Client;
using UnityEngine;
using System;

namespace Hodba.Tests
{
    public class GreatWallTests
    {
        [Test]
        public void Movement_CannotTunnelAcrossWallInEitherDirection()
        {
            var world = new GreatWallWorld(new FlatStub(1));
            var west = WorldPos.FromMeters(1600, 128);
            var east = WorldPos.FromMeters(2000, 128);
            Assert.That(world.ResolveMovement(west, east).XMeters, Is.LessThan(1740));
            Assert.That(world.ResolveMovement(east, west).XMeters, Is.GreaterThan(1860));
        }

        [Test]
        public void Movement_SlidesAlongFaceAndStopsAtButtress()
        {
            var world = new GreatWallWorld(new FlatStub(1));
            var slide = world.ResolveMovement(WorldPos.FromMeters(1730, 100), WorldPos.FromMeters(1750, 120));
            Assert.That(slide.XMeters, Is.LessThan(1740));
            Assert.That(slide.ZMeters, Is.EqualTo(120).Within(0.01));
            var post = world.ResolveMovement(WorldPos.FromMeters(1720, -30), WorldPos.FromMeters(1720, 30));
            Assert.That(post.ZMeters, Is.LessThan(-12));
        }

        [Test]
        public void Sim_UsesObstacleQueryAndDoesNotCountBlockedSteps()
        {
            var world = new GreatWallWorld(new FlatStub(1));
            var sim = new WalkSim(WalkParams.Default, WorldPos.FromMeters(1739.65, 128), 90);
            sim.Apply(Intent.Walk());
            for (int i = 0; i < 100; i++) sim.Step(0.1f, world);
            Assert.That(sim.Position.XMeters, Is.LessThan(1740));
            Assert.That(sim.Distance, Is.LessThan(0.1));
        }

        [Test]
        public void Wall_DoesNotChangeDesertGround()
        {
            var desert = new FlatStub(5);
            var wall = new GreatWallWorld(desert);
            Assert.That(wall.SampleHeightMm(1700000, -123000), Is.EqualTo(desert.SampleHeightMm(1700000, -123000)));
            Assert.That(wall.Info.Seed, Is.EqualTo(desert.Info.Seed));
        }

        [Test]
        public void WalkingAwayFromWall_PreservesExistingStepTimingExactly()
        {
            var desert = new FlatStub(5);
            var plain = new WalkSim(WalkParams.Default, WorldPos.FromMeters(0, 0), 30);
            var withWall = new WalkSim(WalkParams.Default, WorldPos.FromMeters(0, 0), 30);
            plain.Apply(Intent.Walk()); withWall.Apply(Intent.Walk());
            for (int i = 0; i < 1000; i++)
            {
                plain.Step(0.002f, desert);
                withWall.Step(0.002f, new GreatWallWorld(desert));
                Assert.That(withWall.Position, Is.EqualTo(plain.Position));
                Assert.That(withWall.Distance, Is.EqualTo(plain.Distance));
                Assert.That(withWall.Steps, Is.EqualTo(plain.Steps));
            }
        }

        [Test]
        public void OfflineWalking_CannotCrossTheWall()
        {
            var state = new WalkerSave.State { Position = WorldPos.FromMeters(1600, 128), Course = 90,
                Walking = true, SavedUtc = DateTime.UtcNow.AddHours(-1) };
            var pos = WalkerSave.Advance(state, 1, 1, out double distance, new GreatWallWorld(new FlatStub(1)));
            Assert.That(pos.XMeters, Is.LessThan(1740));
            Assert.That(distance, Is.LessThan(140));
        }

        [Test]
        public void BayMesh_AndOriginShift_PreserveFiveHundredMetreLandmark()
        {
            var mesh = GreatWall.CreateBayMesh();
            try
            {
                Assert.That(mesh.bounds.max.y, Is.EqualTo(500).Within(0.01));
                Assert.That(mesh.bounds.min.y, Is.LessThan(0));
                var origin = new FloatingOrigin(WorldPos.FromMeters(0, 0));
                var position = new WorldPos(GreatWallWorld.CenterXMm, -GreatWallWorld.BayLengthMm);
                var local = origin.ToLocal(position);
                origin.Shifted += delta => local += delta;
                origin.Rebase(WorldPos.FromMeters(1300, -2700));
                Assert.That(origin.ToWorld(local), Is.EqualTo(position));
                // Every face's winding agrees with its lighting normal.
                var vertices = mesh.vertices; var normals = mesh.normals; var indices = mesh.triangles;
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int a = indices[i], b = indices[i+1], c = indices[i+2];
                    Assert.That(Vector3.Dot(Vector3.Cross(vertices[b]-vertices[a], vertices[c]-vertices[a]), normals[a]), Is.GreaterThan(0));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
