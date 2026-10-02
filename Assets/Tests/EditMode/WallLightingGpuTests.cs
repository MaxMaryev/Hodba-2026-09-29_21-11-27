using NUnit.Framework;
using UnityEngine;

namespace Hodba.Tests
{
    public class WallLightingGpuTests
    {
        Material _probe;
        RenderTexture _target, _previous;
        Texture2D _pixel;
        Vector4 _wall, _details, _sun, _origin, _fog;

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
            Shader.SetGlobalVector("_HodbaWall",new Vector4(1800,60,482,1));
            Shader.SetGlobalVector("_HodbaWallDetails",new Vector4(32,12,256,500));
            Shader.SetGlobalVector("_HodbaSunDir",new Vector3(1,0.2f,0).normalized);
            Shader.SetGlobalVector("_HodbaOriginMod",Vector4.zero);
            Shader.SetGlobalVector("_HodbaFog",new Vector4(0.001f,0.04f,0.45f,0));
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
            for(int mode=0;mode<3;mode++) Assert.That(Sample(new Vector3(0,2,128),mode),Is.GreaterThan(0.99));
        }

        [TearDown]
        public void Cleanup()
        {
            Shader.SetGlobalVector("_HodbaWall",_wall); Shader.SetGlobalVector("_HodbaWallDetails",_details);
            Shader.SetGlobalVector("_HodbaSunDir",_sun); Shader.SetGlobalVector("_HodbaOriginMod",_origin);
            Shader.SetGlobalVector("_HodbaFog",_fog); RenderTexture.active=_previous;
            Object.DestroyImmediate(_probe); Object.DestroyImmediate(_target); Object.DestroyImmediate(_pixel);
        }
    }
}
