using System.IO;
using Hodba.Client;
using Hodba.Core;
using Hodba.World;
using Hodba.World.Gen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Hodba.Editor
{
    /// <summary>
    /// Земля на полигоне: стекло, рябь, бугры — при низком и высоком солнце, и вид к горизонту через все кольца.
    /// Чтобы подбирать рельеф глазами и видеть переходы колец. Запуск из меню или -executeMethod Hodba.Editor.GroundPreview.Render.
    /// </summary>
    public static class GroundPreview
    {
        const int W = 800, H = 450;
        const string ConfigPath = "Assets/Hodba/Settings/FieldConfig.asset";

        struct Shot
        {
            public string Name;
            public float Z;
            public float SunElevation;
            public float Pitch;
        }

        [MenuItem("Hodba/Debug/Ground Preview", priority = 201)]
        public static void Render()
        {
            string output = System.Environment.GetEnvironmentVariable("HODBA_GROUND_PREVIEW") ?? "Logs/GroundPreview.png";
            var config = AssetDatabase.LoadAssetAtPath<FieldConfig>(ConfigPath);
            if (config == null)
            {
                Debug.LogError("[GroundPreview] нет FieldConfig — запусти Hodba ▸ Setup Field.");
                return;
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var world = new ProvingGround(config.seed);
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.color = new Color(1f, 0.85f, 0.7f);
            if (config.skyMaterial != null) RenderSettings.skybox = config.skyMaterial;
            RenderSettings.ambientLight = new Color(0.45f, 0.42f, 0.4f);

            var camGo = new GameObject("Eyes");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 5000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = true;

            var shots = new[]
            {
                new Shot { Name = "стекло, низкое солнце", Z = 30f, SunElevation = 6f, Pitch = 28f },
                new Shot { Name = "рябь, низкое солнце", Z = 95f, SunElevation = 6f, Pitch = 28f },
                new Shot { Name = "рябь, полдень", Z = 95f, SunElevation = 65f, Pitch = 28f },
                new Shot { Name = "бугры, низкое солнце", Z = 260f, SunElevation = 6f, Pitch = 28f },
                new Shot { Name = "бугры, полдень", Z = 260f, SunElevation = 65f, Pitch = 28f },
                new Shot { Name = "к горизонту через все кольца", Z = 260f, SunElevation = 12f, Pitch = 3f },
            };

            var sheet = new Texture2D(W * 2, H * 3, TextureFormat.RGB24, false);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            try
            {
                for (int i = 0; i < shots.Length; i++)
                {
                    var s = shots[i];
                    var focus = WorldPos.FromMeters(0, s.Z);
                    var origin = new FloatingOrigin(focus);
                    using (var ground = new ClipmapTerrain(world, origin, config, config.groundMaterial))
                    {
                        // Солнце сбоку-спереди: низкое солнце вытягивает тени от каждого бугорка.
                        sunGo.transform.rotation = Quaternion.Euler(s.SunElevation, 200f, 0f);
                        float h = world.SampleHeightMm(focus) / 1000f;
                        camGo.transform.SetPositionAndRotation(origin.ToLocal(focus, h + config.eyeHeight), Quaternion.Euler(s.Pitch, 10f, 0f));

                        ground.Tick(focus);
                        cam.targetTexture = rt;
                        RenderPipeline.SubmitRenderRequest(cam, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                        var old = RenderTexture.active;
                        RenderTexture.active = rt;
                        sheet.ReadPixels(new Rect(0, 0, W, H), (i % 2) * W, (2 - i / 2) * H);
                        RenderTexture.active = old;
                        cam.targetTexture = null;
                    }
                    Debug.Log($"[GroundPreview] {i + 1}. {s.Name}");
                }
                sheet.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                File.WriteAllBytes(output, sheet.EncodeToPNG());
                Debug.Log($"[GroundPreview] {Path.GetFullPath(output)}");
            }
            finally
            {
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(sheet);
            }
        }
    }
}
