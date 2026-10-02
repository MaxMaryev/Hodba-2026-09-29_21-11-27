using NUnit.Framework;
using UnityEngine;

namespace Hodba.Tests
{
    public class WallLightingGpuTests
    {
        Material _probe;
        RenderTexture _target, _previous;
        Texture2D _pixel;
        Vector4 _wall, _details, _sun, _origin, _fog, _haze;

        [SetUp]
        public void Setup()
        {
            var shader=Shader.Find("Hidden/Hodba/WallLightingProbe");
            Assert.That(shader,Is.Not.Null);
            _probe=new Material(shader);
            _target=new RenderTexture(1,1,0,RenderTextureFormat.ARGBFloat);
            _pixel=new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            _previous=RenderTexture.active;
            _wall=Shader.GetGlobalVector("_HodbaWall"); _details=Shader.GetGlobalVector("_HodbaWallDetails");
            _sun=Shader.GetGlobalVector("_HodbaSunDir"); _origin=Shader.GetGlobalVector("_HodbaOriginMod");
            _fog=Shader.GetGlobalVector("_HodbaFog");
            _haze=Shader.GetGlobalVector("_HodbaHaze");
            Shader.SetGlobalVector("_HodbaWall",new Vector4(1800,60,482,1));
            Shader.SetGlobalVector("_HodbaWallDetails",new Vector4(32,12,256,500));
            Shader.SetGlobalVector("_HodbaSunDir",new Vector3(1,0.2f,0).normalized);
            Shader.SetGlobalVector("_HodbaOriginMod",Vector4.zero);
            Shader.SetGlobalVector("_HodbaFog",new Vector4(0.001f,0.04f,0.45f,0));
            Shader.SetGlobalVector("_HodbaHaze",new Vector4(1,1,2,0));
        }

        float Sample(Vector3 position,int mode=0,Vector3 eye=default)
        {
            _probe.SetVector("_ProbePosition",position); _probe.SetVector("_ProbeNormal",Vector3.up);
            _probe.SetVector("_ProbeEye",eye); _probe.SetFloat("_ProbeMode",mode);
            Graphics.Blit(null,_target,_probe);
            RenderTexture.active=_target;
            _pixel.ReadPixels(new Rect(0,0,1,1),0,0); _pixel.Apply();
            return _pixel.GetPixel(0,0).r;
        }

        [Test]
        public void Sunlight_IsBlockedOnWestAndVisibleOnEastAndAboveWall()
        {
            Assert.That(Sample(new Vector3(0,2,128)),Is.LessThan(0.01));
            Assert.That(Sample(new Vector3(2000,2,128)),Is.GreaterThan(0.99));
            Assert.That(Sample(new Vector3(0,600,128)),Is.GreaterThan(0.99));
        }

        [Test]
        public void SkyOcclusion_IsStrongerCloseToWall()
        {
            float near=Sample(new Vector3(1700,2,128),1);
            float far=Sample(new Vector3(-10000,2,128),1);
            Assert.That(near,Is.LessThan(0.8));
            Assert.That(far,Is.GreaterThan(near+0.15));
        }

        [Test]
        public void DustAlongRay_ReceivesLessDirectLightInWallShadow()
        {
            Assert.That(Sample(new Vector3(1600,2,128),2,new Vector3(0,2,128)),Is.LessThan(0.01));
            Assert.That(Sample(new Vector3(2600,2,128),2,new Vector3(2000,2,128)),Is.GreaterThan(0.99));
        }

        [Test]
        public void DisabledWall_DoesNotChangeLighting()
        {
            Shader.SetGlobalVector("_HodbaWall",Vector4.zero);
            for(int mode=0;mode<4;mode++) Assert.That(Sample(new Vector3(0,2,128),mode),Is.GreaterThan(0.99));
        }

        [Test]
        public void FogShadow_IntegratesTheWholeHorizontalRay()
        {
            // At y=2, sunlight clears the 482 m panel at x=-660.
            // Exactly 340 of this 2000 m view ray is illuminated.
            Assert.That(Sample(new Vector3(1000,2,128),2,new Vector3(-1000,2,128)),
                Is.EqualTo(0.17f).Within(0.002f));
        }

        [Test]
        public void FogShadow_HasNoFourSampleBandsOnTheWallFace()
        {
            Shader.SetGlobalVector("_HodbaSunDir",new Vector3(1,0.25f,0).normalized);
            var eye=new Vector3(-292,2,128);
            float last=Sample(new Vector3(1739.8f,260,128),2,eye);
            for(int y=261;y<=481;y++)
            {
                float next=Sample(new Vector3(1739.8f,y,128),2,eye);
                // The exact fraction gets steeper near the crest (about 0.03/m),
                // but never makes the old approximately 0.25 sample-count jump.
                Assert.That(Mathf.Abs(next-last),Is.LessThan(0.04f),"Height "+y);
                last=next;
            }
        }

        [Test]
        public void SunDisc_IsHalfVisibleAtThePanelEdge()
        {
            Shader.SetGlobalVector("_HodbaSunDir",new Vector3(1,0.25f,0).normalized);
            Assert.That(Sample(new Vector3(740,232,128)),Is.EqualTo(0.5f).Within(0.025f));
        }

        [Test]
        public void Penumbra_WidensWithDistanceFromThePanel()
        {
            Shader.SetGlobalVector("_HodbaSunDir",new Vector3(1,0.25f,0).normalized);
            float near=Sample(new Vector3(1640,456,128));
            float far=Sample(new Vector3(740,231,128));
            Assert.That(near,Is.LessThan(0.05f));
            Assert.That(far,Is.InRange(0.2f,0.49f));
        }

        [Test]
        public void DistantNarrowPier_CannotFullyBlockTheSunDisc()
        {
            Shader.SetGlobalVector("_HodbaSunDir",new Vector3(1,Mathf.Tan(5*Mathf.Deg2Rad),0).normalized);
            float pier=Sample(new Vector3(-3900,2,0));
            float gap=Sample(new Vector3(-3900,2,128));
            Assert.That(pier,Is.InRange(0.1f,0.95f));
            // The main panel's penumbra also reaches here, so the gap is not
            // fully illuminated; isolate the additional narrow pier shadow.
            Assert.That(gap,Is.GreaterThan(pier+0.1f));
        }

        [Test]
        public void AmbientBounce_FollowsTheSoftShadowWithoutAnExtraStep()
        {
            Shader.SetGlobalVector("_HodbaSunDir",new Vector3(1,0.25f,0).normalized);
            float low=Sample(new Vector3(740,231.8f,128),3);
            float high=Sample(new Vector3(740,232.2f,128),3);
            Assert.That(low,Is.InRange(0.7f,0.9f));
            Assert.That(high-low,Is.InRange(0f,0.05f));
        }

        [TestCase(10f,450f)]
        [TestCase(-40f,300f)]
        public void FogShadow_MatchesDenseReferenceIncludingTheDensityFloor(float eyeY,float endY)
        {
            var eye=new Vector3(-1000,eyeY,128);
            var end=new Vector3(1700,endY,128);
            double total=0,illuminated=0;
            const int slices=8192;
            for(int i=0;i<slices;i++)
            {
                var p=Vector3.Lerp(eye,end,(i+0.5f)/slices);
                double density=0.45+0.55*System.Math.Min(2,System.Math.Exp(-p.y*0.04));
                bool shaded=p.y+0.2*(1740-p.x)<=482;
                total+=density;
                if(!shaded) illuminated+=density;
            }
            Assert.That(Sample(end,2,eye),Is.EqualTo(illuminated/total).Within(0.002));
        }

        [Test]
        public void FogShadow_HandlesAxisParallelSunRays()
        {
            var eye=new Vector3(0,2,128);
            var end=new Vector3(1600,2,128);
            Shader.SetGlobalVector("_HodbaSunDir",Vector3.up);
            Assert.That(Sample(end,2,eye),Is.GreaterThan(0.99f));
            Shader.SetGlobalVector("_HodbaSunDir",Vector3.right);
            Assert.That(Sample(end,2,eye),Is.LessThan(0.01f));
        }

        [Test]
        public void SoftShadow_IsSymmetricOnBothSidesOfTheWall()
        {
            Shader.SetGlobalVector("_HodbaSunDir",new Vector3(1,0.25f,0).normalized);
            float west=Sample(new Vector3(740,232,128));
            Shader.SetGlobalVector("_HodbaSunDir",new Vector3(-1,0.25f,0).normalized);
            float east=Sample(new Vector3(2860,232,128));
            Assert.That(east,Is.EqualTo(west).Within(0.002f));
        }

        [Test]
        public void PierPenumbra_IsStableAcrossFloatingOriginShifts()
        {
            Shader.SetGlobalVector("_HodbaSunDir",new Vector3(1,Mathf.Tan(5*Mathf.Deg2Rad),0).normalized);
            float before=Sample(new Vector3(-3900,2,0));
            Shader.SetGlobalVector("_HodbaWall",new Vector4(1800-4096,60,482,1));
            Shader.SetGlobalVector("_HodbaOriginMod",new Vector4(0,4096,0,0));
            float after=Sample(new Vector3(-3900-4096,2,-4096));
            Assert.That(after,Is.EqualTo(before).Within(0.002f));
        }

        [TearDown]
        public void Cleanup()
        {
            Shader.SetGlobalVector("_HodbaWall",_wall); Shader.SetGlobalVector("_HodbaWallDetails",_details);
            Shader.SetGlobalVector("_HodbaSunDir",_sun); Shader.SetGlobalVector("_HodbaOriginMod",_origin);
            Shader.SetGlobalVector("_HodbaFog",_fog); RenderTexture.active=_previous;
            Shader.SetGlobalVector("_HodbaHaze",_haze);
            Object.DestroyImmediate(_probe); Object.DestroyImmediate(_target); Object.DestroyImmediate(_pixel);
        }
    }
}
