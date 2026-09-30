using System.IO;
using Hodba.Client;
using Hodba.Client.Body;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Hodba.Editor
{
    /// <summary>
    /// Лист состояний век на одной картинке: открыто, моргание, прищур, против солнца.
    /// Чтобы подбирать веки глазами, а не по цифрам. Запуск из меню или -executeMethod Hodba.Editor.EyePreview.Render.
    /// </summary>
    public static class EyePreview
    {
        const int W = 800, H = 450;
        const string ConfigPath = "Assets/Hodba/Settings/FieldConfig.asset";

        struct Shot
        {
            public string Name;
            public EyelidState Lids;
            public float Sun;
            public bool FaceSun;
            public PeripheryState Periphery;
        }

        [MenuItem("Hodba/Debug/Eye Preview", priority = 200)]
        public static void Render()
        {
            string output = System.Environment.GetEnvironmentVariable("HODBA_EYE_PREVIEW") ?? "Logs/EyePreview.png";
            var config = AssetDatabase.LoadAssetAtPath<FieldConfig>(ConfigPath);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.color = new Color(1f, 0.85f, 0.65f);
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -0.12f, -1f));
            if (config != null && config.skyMaterial != null) RenderSettings.skybox = config.skyMaterial;
            RenderSettings.ambientLight = new Color(0.55f, 0.5f, 0.45f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = Vector3.one * 200f;
            if (config != null && config.groundMaterial != null) ground.GetComponent<Renderer>().sharedMaterial = config.groundMaterial;
            for (int i = 0; i < 48; i++)
            {
                var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rock.transform.position = new Vector3((i * 37 % 23 - 11) * 1.4f, 0.05f, 3f + i * 13 % 31 * 1.3f);
                rock.transform.localScale = new Vector3(0.5f, 0.25f, 0.4f) * (0.6f + (i % 3) * 0.4f);
                if (config != null && config.stoneMaterial != null) rock.GetComponent<Renderer>().sharedMaterial = config.stoneMaterial;
            }

            var camGo = new GameObject("Eyes");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 3000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;

            var shader = config != null && config.eyeShader != null ? config.eyeShader : Shader.Find("Hidden/Hodba/Eye");
            var eye = new EyeRender(cam, shader);

            var ps = config != null ? config.periphery : PeripherySettings.Default;
            var calm = Periphery.From(new ExertionState(0f, 0f, 0f, 0.23f, 0.6f, 0f, 0.5f), ps);
            var tunnel = Periphery.From(new ExertionState(1f, 1f, 0f, 0.6f, 1.4f, 0f, 0.5f), ps);
            var none = new PeripheryState(1f, 0f, 0f, 0f, ps.radius);
            var open = new EyelidState(0f, 0f, 0f, 0f, 0.04f);

            var shots = new[]
            {
                new Shot { Name = "без периферии (для сравнения)", Lids = open, Periphery = none },
                new Shot { Name = "периферия в покое", Lids = open, Periphery = calm },
                new Shot { Name = "туннель от одышки", Lids = open, Periphery = tunnel },
                new Shot { Name = "моргание, середина", Lids = new EyelidState(0.5f, 0.06f, 0f, 0.5f, 0.04f), Periphery = calm },
                new Shot { Name = "прищур на солнце", Lids = new EyelidState(0.45f, 0.4f, 1f, 0f, 1f), Sun = 1f, FaceSun = true, Periphery = calm },
                new Shot { Name = "сомкнуто против солнца", Lids = new EyelidState(0.97f, 0.12f, 0f, 1f, 1f), Sun = 1f, FaceSun = true, Periphery = calm },
            };

            var sheet = new Texture2D(W * 2, H * 3, TextureFormat.RGB24, false);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            try
            {
                for (int i = 0; i < shots.Length; i++)
                {
                    var s = shots[i];
                    camGo.transform.SetPositionAndRotation(new Vector3(0f, 1.65f, 0f),
                        Quaternion.LookRotation(s.FaceSun ? -sunGo.transform.forward : new Vector3(0.3f, -0.12f, 1f)));
                    eye.Apply(s.Lids, s.Sun, s.Periphery);
                    cam.targetTexture = rt;
                    RenderPipeline.SubmitRenderRequest(cam, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                    var old = RenderTexture.active;
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, W, H), (i % 2) * W, (2 - i / 2) * H);
                    RenderTexture.active = old;
                    cam.targetTexture = null;
                    Debug.Log($"[EyePreview] {i + 1}. {s.Name}");
                }
                sheet.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
                File.WriteAllBytes(output, sheet.EncodeToPNG());
                Debug.Log($"[EyePreview] {Path.GetFullPath(output)}");
            }
            finally
            {
                eye.Dispose();
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(sheet);
            }
        }
    }
}
