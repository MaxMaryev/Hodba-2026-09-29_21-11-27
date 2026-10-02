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
using UnityEngine.SceneManagement;

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
            public float Yaw;
            public float OriginX;
            public float Wind; // сила ветра, 0 — штиль
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
            // Снимки не закрывают рабочую сцену и не теряют несохранённые изменения.
            var previousScene = SceneManager.GetActiveScene();
            var previousOrigin = Shader.GetGlobalVector("_HodbaOriginMod");
            var previewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(previewScene);

            var world = new ProvingGround(config.seed);
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            // Свет, небо и дымка — как в игре, от высоты солнца; облака стоят на месте.
            var sky = new SkyController(sun, config.skyMaterial);
            DustShadows.Push(config, Vector2.zero);

            var camGo = new GameObject("Eyes");
            var cam = camGo.AddComponent<Camera>();
            cam.scene = previewScene;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 5000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = true;

            var shots = new[]
            {
                new Shot { Name = "стекло, низкое солнце", Z = 30f, SunElevation = 6f, Pitch = 28f, Yaw = 10f },
                new Shot { Name = "рябь, низкое солнце", Z = 95f, SunElevation = 6f, Pitch = 28f, Yaw = 10f },
                new Shot { Name = "рябь, полдень", Z = 95f, SunElevation = 65f, Pitch = 28f, Yaw = 10f },
                new Shot { Name = "бугры, низкое солнце", Z = 260f, SunElevation = 6f, Pitch = 28f, Yaw = 10f },
                new Shot { Name = "бугры, полдень", Z = 260f, SunElevation = 65f, Pitch = 28f, Yaw = 10f },
                new Shot { Name = "к горизонту через все кольца", Z = 260f, SunElevation = 12f, Pitch = 3f, Yaw = 10f },
                new Shot { Name = "к горизонту спиной к солнцу", Z = 400f, SunElevation = 8f, Pitch = 3f, Yaw = 200f },
                new Shot { Name = "бархан против солнца, рассвет", Z = 300f, SunElevation = 4f, Pitch = 2f, Yaw = 20f },
                new Shot { Name = "позёмка в порыв, рябь, низкое солнце", Z = 95f, SunElevation = 8f, Pitch = 18f, Yaw = 60f, Wind = 1f },
                new Shot { Name = "позёмка на бархане против ветра", Z = 340f, SunElevation = 10f, Pitch = 6f, Yaw = 270f, Wind = 1f },
                new Shot { Name = "корка, вдаль", Z = 30f, SunElevation = 12f, Pitch = 8f, Yaw = 10f },
                new Shot { Name = "рябь вдоль ветра, пологий ракурс", Z = 95f, SunElevation = 6f, Pitch = 6f, Yaw = 90f },
                new Shot { Name = "перенос центра: 512 м", Z = 95f, SunElevation = 6f, Pitch = 28f, Yaw = 90f, OriginX = 512f },
                new Shot { Name = "перенос центра: 4096 + 512 м", Z = 95f, SunElevation = 6f, Pitch = 28f, Yaw = 90f, OriginX = 4608f },
                new Shot { Name = "взвесь, ветер 0.3, низкое солнце", Z = 95f, SunElevation = 8f, Pitch = 8f, Yaw = 40f, Wind = 0.3f },
                new Shot { Name = "взвесь, ветер 0.8, против солнца", Z = 340f, SunElevation = 6f, Pitch = 5f, Yaw = 200f, Wind = 0.8f },
            };

            int rows = (shots.Length + 1) / 2;
            var sheet = new Texture2D(W * 2, H * rows, TextureFormat.RGB24, false);
            var frame = new Texture2D(W, H, TextureFormat.RGB24, false);
            Color32[] rebaseReference = null;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            try
            {
                for (int i = 0; i < shots.Length; i++)
                {
                    var s = shots[i];
                    var focus = WorldPos.FromMeters(0, s.Z);
                    // Меняется только центр координат; мировая точка и геометрия остаются теми же.
                    var origin = new FloatingOrigin(s.OriginX == 0f ? focus : WorldPos.FromMeters(s.OriginX, 0));
                    using (var ground = new ClipmapTerrain(world, origin, config, config.groundMaterial, config.sandVeilMaterial))
                    {
                        // Солнце сбоку-спереди: низкое солнце вытягивает тени от каждого бугорка.
                        float h = world.SampleHeightMm(focus) / 1000f;
                        var sunDirection = -(Quaternion.Euler(s.SunElevation, 200f, 0f) * Vector3.forward);
                        sky.Apply(s.SunElevation, sunDirection, config, h, 0f);
                        camGo.transform.SetPositionAndRotation(origin.ToLocal(focus, h + config.eyeHeight), Quaternion.Euler(s.Pitch, s.Yaw, 0f));
                        // Ветер и песок застыли в кадре: ветер на восток, вокруг глаз; бег струй и языков виден только в Play.
                        var eye = camGo.transform.position;
                        Wind.Push(new Vector2(1f, 0f), Mathf.Lerp(1.5f, 9f, s.Wind), s.Wind, new Vector2(eye.x, eye.z), Vector2.zero,
                            Wind.FrontTile(config), config.windGust);
                        float veil = SandDrift.VeilResponse(s.Wind, config);
                        SandDrift.Push(config, veil, SandDrift.Response(s.Wind, config.saltationThreshold) * config.saltationStrength,
                            Vector4.zero, SandDrift.StillLayers());

                        ground.Tick(focus, veil);
                        cam.targetTexture = rt;
                        RenderPipeline.SubmitRenderRequest(cam, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                        var old = RenderTexture.active;
                        RenderTexture.active = rt;
                        try
                        {
                            frame.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                            frame.Apply();
                            var pixels = frame.GetPixels32();
                            sheet.SetPixels32((i % 2) * W, (rows - 1 - i / 2) * H, W, H, pixels);
                            if (s.OriginX == 512f) rebaseReference = pixels;
                            if (s.OriginX == 4608f && rebaseReference != null)
                            {
                                int maxDifference = 0;
                                int changedPixels = 0;
                                int maxPixel = 0;
                                long totalDifference = 0;
                                for (int p = 0; p < pixels.Length; p++)
                                {
                                    var a = rebaseReference[p];
                                    var b = pixels[p];
                                    int dr = System.Math.Abs(a.r - b.r), dg = System.Math.Abs(a.g - b.g), db = System.Math.Abs(a.b - b.b);
                                    int difference = System.Math.Max(dr, System.Math.Max(dg, db));
                                    if (difference > maxDifference) { maxDifference = difference; maxPixel = p; }
                                    if (difference > 2) changedPixels++;
                                    totalDifference += dr + dg + db;
                                }
                                Debug.Log($"[GroundPreview] Rebase: max={maxDifference}/255 at ({maxPixel % W},{maxPixel / W}), mean={totalDifference / (pixels.Length * 3.0):F6}/255, pixels > 2/255: {changedPixels}/{pixels.Length}");
                            }
                        }
                        finally
                        {
                            RenderTexture.active = old;
                            cam.targetTexture = null;
                        }
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
                Object.DestroyImmediate(frame);
                Shader.SetGlobalVector("_HodbaOriginMod", previousOrigin);
                SceneManager.SetActiveScene(previousScene);
                EditorSceneManager.CloseScene(previewScene, true);
            }
        }
    }
}
