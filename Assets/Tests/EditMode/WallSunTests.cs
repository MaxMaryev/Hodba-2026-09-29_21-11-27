using Hodba.Core;
using Hodba.World.Gen;
using Hodba.Client.Body;
using Hodba.Sim.Walk;
using NUnit.Framework;
using UnityEngine;

namespace Hodba.Tests
{
    public class WallSunTests
    {
        [Test]
        public void Wall_BlocksLowSunButNotSunAboveCrownOrBehindWalker()
        {
            var world = new GreatWallWorld(new FlatStub(1));
            var pos = WorldPos.FromMeters(0,128);
            Assert.That(SunOcclusion.Visibility(world,pos,2,new Vector3(1,0.2f,0)),Is.Zero);
            Assert.That(SunOcclusion.Visibility(world,pos,2,new Vector3(1,2,0)),Is.EqualTo(1));
            Assert.That(SunOcclusion.Visibility(world,pos,2,new Vector3(-1,0.2f,0)),Is.EqualTo(1));
            Assert.That(SunOcclusion.Visibility(world,WorldPos.FromMeters(2000,128),2,new Vector3(-1,0.2f,0)),Is.Zero);
        }

        [Test]
        public void CrownAndButtresses_AreOccludersAtTheirActualHeights()
        {
            var world = new GreatWallWorld(new FlatStub(1));
            Assert.That(SunOcclusion.Visibility(world,WorldPos.FromMeters(1600,128),490,Vector3.right),Is.EqualTo(1));
            Assert.That(SunOcclusion.Visibility(world,WorldPos.FromMeters(1600,0),490,Vector3.right),Is.Zero);
            Assert.That(SunOcclusion.Visibility(world,WorldPos.FromMeters(1720,-30),2,new Vector3(0,0.1f,1)),Is.Zero);
        }

        [Test]
        public void HiddenSun_DoesNotCauseSquintBlinkOrDirectGlare()
        {
            var world = new GreatWallWorld(new FlatStub(1));
            var sim = new WalkSim(WalkParams.Default,WorldPos.FromMeters(0,128),90);
            var sun = new Vector3(1,0.2f,0).normalized;
            var ctx = new BodyContext(0.1f,1,sim,world,Vector3.zero,0,0,sun,12,90,-12,false,0,0);
            var irritant = new SunIrritant();
            var sensed = irritant.Sense(ctx,sun,EyelidSettings.Default);
            Assert.That(irritant.Stimulus,Is.Zero);
            Assert.That(sensed.Squint,Is.Zero);
            Assert.That(sensed.ExtraBlinkRate,Is.Zero);
            Assert.That(Glare.Stimulus(sun,sun,12,4,0),Is.Zero);
            Assert.That(Glare.Stimulus(sun,sun,12,4,1),Is.EqualTo(1).Within(0.001));
        }

        [Test]
        public void PlainDesert_KeepsSunVisible()
        {
            Assert.That(SunOcclusion.Visibility(new FlatStub(1),WorldPos.FromMeters(0,0),2,Vector3.right),Is.EqualTo(1));
        }
    }
}
