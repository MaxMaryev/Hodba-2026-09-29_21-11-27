using System.IO;
using System.Linq;
using Hodba.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Hodba.Editor
{
    /// <summary>
    /// Hodba ▸ Setup Field — собирает веху «Поле» с нуля: 3D-пайплайн, пост-обработку, материалы,
    /// заглушки текстур, конфиг и сцену. Можно запускать повторно: настройки в FieldConfig не теряются.
    /// </summary>
    public static class FieldSetup
    {
        const string Root = "Assets/Hodba";
        const string Settings = Root + "/Settings";
        const string Generated = Root + "/Generated";
        const string ScenePath = Root + "/Scenes/Field.unity";
        const string ConfigPath = Settings + "/FieldConfig.asset";

        [MenuItem("Hodba/Setup Field", priority = 0)]
        public static void Setup()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            try
            {
                EditorUtility.DisplayProgressBar("Hodba", "Пайплайн", 0.1f);
                Directory.CreateDirectory(Settings);
                Directory.CreateDirectory(Generated);
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));

                var pipeline = SetupPipeline();

                EditorUtility.DisplayProgressBar("Hodba", "Текстуры-заглушки", 0.3f);
                var ashDetail = TextureGen.AshDetail(Generated + "/T_AshDetail.png");
                var packedDetail = TextureGen.PackedDetail(Generated + "/T_PackedDetail.png");
                var macro = TextureGen.Macro(Generated + "/T_Macro.png");
                var ashNormal = TextureGen.AshNormal(Generated + "/T_AshNormal.png");
                var ripples = TextureGen.Ripples(Generated + "/T_Ripples.png");
                var footprint = TextureGen.Footprint(Generated + "/T_Footprint.png");
                var dot = TextureGen.SoftDot(Generated + "/T_Dot.png");
                var streak = TextureGen.Streak(Generated + "/T_Streak.png");

                EditorUtility.DisplayProgressBar("Hodba", "Материалы", 0.6f);
                var ground = Mat("M_Ground", "Hodba/Ground", m =>
                {
                    m.SetTexture("_AshAlbedo", ashDetail);
                    m.SetTexture("_PackedAlbedo", packedDetail);
                    m.SetTexture("_MacroTex", macro);
                    m.SetTexture("_AshNormal", ashNormal);
                    m.SetTexture("_RippleNormal", ripples);
                });
                var sky = Mat("M_Sky", "Hodba/Sky", null);
                var print = Mat("M_Footprint", "Hodba/Footprint", m => m.SetTexture("_MainTex", footprint));
                var dust = Mat("M_Dust", "Hodba/Dust", m => m.SetTexture("_MainTex", dot));
                var drift = Mat("M_Drift", "Hodba/Dust", m =>
                {
                    m.SetTexture("_MainTex", streak);
                    m.SetFloat("_Scatter", 1.5f);
                });
                var stone = Mat("M_Stone", "Universal Render Pipeline/Lit", m =>
                {
                    m.SetColor("_BaseColor", new Color(0.25f, 0.235f, 0.22f));
                    m.SetFloat("_Smoothness", 0.12f);
                    m.enableInstancing = true;
                });

                var volume = SetupVolume();

                EditorUtility.DisplayProgressBar("Hodba", "Конфиг", 0.8f);
                var config = AssetDatabase.LoadAssetAtPath<FieldConfig>(ConfigPath);
                if (config == null)
                {
                    config = ScriptableObject.CreateInstance<FieldConfig>();
                    AssetDatabase.CreateAsset(config, ConfigPath);
                }
                config.groundMaterial = ground;
                config.skyMaterial = sky;
                config.footprintMaterial = print;
                config.dustMaterial = dust;
                config.driftMaterial = drift;
                if (config.stoneMaterial == null) config.stoneMaterial = stone;
                config.volumeProfile = volume;
                AssignAudio(config);
                EditorUtility.DisplayProgressBar("Hodba", "Модели и текстуры", 0.85f);
                FieldArt.Assign(config, stone);
                EditorUtility.SetDirty(config);

                SetupPlayer();

                EditorUtility.DisplayProgressBar("Hodba", "Сцена", 0.9f);
                SetupScene(config, sky);

                AssetDatabase.SaveAssets();
                Debug.Log($"Hodba: «Поле» готово. Пайплайн {AssetDatabase.GetAssetPath(pipeline)}, сцена {ScenePath}. Жми Play.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("Hodba/Reset Walker (start from zero)", priority = 20)]
        public static void ResetWalker()
        {
            WalkerSave.Clear();
            Debug.Log("Hodba: путь сброшен, следующий запуск — с начала.");
        }

        /// <summary>Записи из ArtSource/Field/audio.py. Подставляются только в пустые поля.</summary>
        static void AssignAudio(FieldConfig config)
        {
            const string audio = "Assets/Art/Field/Audio";
            if (!AssetDatabase.IsValidFolder(audio)) return;
            AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{audio}/{name}.wav");

            if (config.footstepClips == null || config.footstepClips.Length == 0)
                config.footstepClips = AssetDatabase.FindAssets("Step_ t:AudioClip", new[] { audio })
                    .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p)
                    .Select(AssetDatabase.LoadAssetAtPath<AudioClip>).ToArray();
            if (config.windLoop == null) config.windLoop = Clip("Wind_Calm_Loop");
            if (config.windGustLoop == null) config.windGustLoop = Clip("Wind_Gusts_Loop");
            if (config.ashHissLoop == null) config.ashHissLoop = Clip("Ash_Hiss_Loop");
        }

        static UniversalRenderPipelineAsset SetupPipeline()
        {
            string rendererPath = Settings + "/HodbaURP_Renderer.asset";
            string assetPath = Settings + "/HodbaURP.asset";

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
            }
            if (renderer.postProcessData == null)
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            EditorUtility.SetDirty(renderer);

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, assetPath);
            }

            asset.supportsHDR = true;
            asset.msaaSampleCount = 2;
            asset.renderScale = 0.85f;
            // Тени — главная картинка рассвета: длинная тень путника и тени внутри следов.
            // Первый каскад (~10 м) — чёткие следы и камни под ногами, второй — хвост тени на низком солнце.
            asset.shadowDistance = 35f;
            asset.shadowCascadeCount = 2;
            asset.cascade2Split = 0.3f;
            asset.mainLightShadowmapResolution = 2048;
            asset.shadowDepthBias = 0.5f;
            asset.shadowNormalBias = 0.3f; // больше — «съедает» ноги и край плаща

            var so = new SerializedObject(asset);
            Set(so, "m_MainLightShadowsSupported", true);
            Set(so, "m_SoftShadowsSupported", true);
            SetInt(so, "m_ColorGradingMode", 1);              // HDR
            SetInt(so, "m_AdditionalLightsRenderingMode", 0); // одно солнце, больше ничего
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);

            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
            return asset;
        }

        static VolumeProfile SetupVolume()
        {
            string path = Settings + "/VP_Field.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            var tone = Get<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.ACES);

            var bloom = Get<Bloom>(profile);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.7f);

            var color = Get<ColorAdjustments>(profile);
            color.postExposure.Override(0f);
            color.saturation.Override(-12f);
            color.contrast.Override(8f);

            var vignette = Get<Vignette>(profile);
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.5f);

            EditorUtility.SetDirty(profile);
            return profile;
        }

        static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T c)) return c;
            c = profile.Add<T>(true);
            c.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(c, profile);
            return c;
        }

        static Material Mat(string name, string shaderName, System.Action<Material> fill)
        {
            string path = Settings + "/" + name + ".mat";
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"Hodba: не найден шейдер {shaderName}");
                return null;
            }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else m.shader = shader;
            fill?.Invoke(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static void SetupPlayer()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
        }

        static void SetupScene(FieldConfig config, Material sky)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Туман и небо в сохранённой сцене — чтобы сборка не выкинула варианты шейдеров с туманом.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0009f;
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;

            var go = new GameObject("Bootstrap");
            go.AddComponent<Bootstrap>().config = config;

            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void Set(SerializedObject so, string prop, bool value)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.boolValue = value;
            else Debug.LogWarning($"Hodba: в URP-ассете нет поля {prop}");
        }

        static void SetInt(SerializedObject so, string prop, int value)
        {
            var p = so.FindProperty(prop);
            if (p != null) p.intValue = value;
            else Debug.LogWarning($"Hodba: в URP-ассете нет поля {prop}");
        }
    }
}
