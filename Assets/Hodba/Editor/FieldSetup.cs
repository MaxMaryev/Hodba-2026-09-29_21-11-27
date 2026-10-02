using System.IO;
using System.Linq;
using Hodba.Client;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Hodba.Editor
{
    /// <summary>
    /// Сборка вехи «Поле».
    /// Hodba ▸ Refresh Field Assets — генерируемые текстуры, материалы, арт-пак и конфиг; сцену и пайплайн не трогает.
    /// Hodba ▸ Setup Field — пайплайн, пост-обработка и сцена с нуля, плюс Refresh.
    /// Конвейер владеет полями ассетов в FieldConfig и выставляет их одинаково при каждом запуске; настройки поведения не трогает.
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
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var pipeline = SetupPipeline();

                var config = Refresh();
                SetupPlayer();

                EditorUtility.DisplayProgressBar("Hodba", "Сцена", 0.9f);
                SetupScene(config);

                AssetDatabase.SaveAssets();
                Debug.Log($"Hodba: «Поле» готово. Пайплайн {AssetDatabase.GetAssetPath(pipeline)}, сцена {ScenePath}. Жми Play.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("Hodba/Refresh Field Assets", priority = 1)]
        public static void RefreshAssets()
        {
            try
            {
                Refresh();
                AssetDatabase.SaveAssets();
                Debug.Log("Hodba: ассеты поля обновлены.");
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

        static FieldConfig Refresh()
        {
            Directory.CreateDirectory(Settings);
            Directory.CreateDirectory(Generated);

            EditorUtility.DisplayProgressBar("Hodba", "Генерируемые текстуры", 0.3f);
            var macro = TextureGen.Macro(Generated + "/T_Macro.png");
            var variation = TextureGen.Variation(Generated + "/T_Variation.png");
            var dot = TextureGen.SoftDot(Generated + "/T_Dot.png");
            var grain = TextureGen.Grain(Generated + "/T_Grain.png");
            var dustShadow = TextureGen.DustShadow(Generated + "/T_DustShadow.png");
            var saltation = TextureGen.Saltation(Generated + "/T_Saltation.png");
            var rippleNoise = TextureGen.RippleNoise(Generated + "/T_RippleNoise.png");

            EditorUtility.DisplayProgressBar("Hodba", "Материалы", 0.5f);
            var ground = Mat("M_Ground", "Hodba/Ground", m =>
            {
                m.SetTexture("_MacroTex", macro);
                m.SetTexture("_VariationTex", variation);
                m.SetTexture("_RippleNoise", rippleNoise);
            });
            var sky = Mat("M_Sky", "Hodba/Sky", null);
            var wall = Mat("M_GreatWall", "Hodba/GreatWall", null);
            // Пылинки и песок в лицо гаснут только вплотную к глазу: налёт порыва летит в 2–6 м, а не за полтора метра.
            var nearFade = new Vector4(0.1f, 0.2f, 0f, 0f);
            var dust = Mat("M_Dust", "Hodba/Dust", m =>
            {
                m.SetTexture("_MainTex", dot);
                m.SetFloat("_Scatter", 0.8f);
                m.SetVector("_NearFade", nearFade);
            });
            // Песчинки у ног: компактные зёрна с умеренным рассеянием света.
            var spray = Mat("M_SandSpray", "Hodba/Dust", m =>
            {
                m.SetTexture("_MainTex", grain);
                m.SetFloat("_Scatter", 0.6f);
                m.SetVector("_NearFade", nearFade);
            });
            var veil = Mat("M_SandVeil", "Hodba/SandVeil", null);
            var volume = SetupVolume();

            EditorUtility.DisplayProgressBar("Hodba", "Конфиг и арт-пак", 0.7f);
            var config = AssetDatabase.LoadAssetAtPath<FieldConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<FieldConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            config.groundMaterial = ground;
            config.skyMaterial = sky;
            config.greatWallMaterial = wall;
            config.dustMaterial = dust;
            config.sprayMaterial = spray;
            config.sandVeilMaterial = veil;
            config.dustShadowTexture = dustShadow;
            config.saltationTexture = saltation;
            config.volumeProfile = volume;
            // Шейдер век грузится кодом — ссылка из конфига не даёт сборке его выбросить.
            config.eyeShader = Shader.Find("Hidden/Hodba/Eye");
            AssignAudio(config);
            FieldArt.Assign(config);
            EditorUtility.SetDirty(config);
            return config;
        }

        /// <summary>Записи из ArtSource/Field/audio.py.</summary>
        static void AssignAudio(FieldConfig config)
        {
            const string audio = "Assets/Art/Field/Audio";
            AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{audio}/{name}.wav")
                ?? throw new System.InvalidOperationException($"Нет звука {audio}/{name}.wav.");

            config.footstepClips = AssetDatabase.FindAssets("Step_ t:AudioClip", new[] { audio })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<AudioClip>).ToArray();
            config.windLoop = Clip("Wind_Calm_Loop");
            config.windGustLoop = Clip("Wind_Gusts_Loop");
            config.ashHissLoop = Clip("Ash_Hiss_Loop");
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
            var shader = Shader.Find(shaderName) ?? throw new System.InvalidOperationException($"Не найден шейдер {shaderName}.");
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

        static void SetupScene(FieldConfig config)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Небо и туман в сохранённой сцене — такие же, как их ставит SkyController, с первого кадра.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0009f;
            RenderSettings.skybox = config.skyMaterial;
            RenderSettings.ambientMode = AmbientMode.Trilight;

            var go = new GameObject("Bootstrap");
            go.AddComponent<Bootstrap>().config = config;

            EditorSceneManager.SaveScene(scene, ScenePath);
            // В сборке одна сцена — поле.
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
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
