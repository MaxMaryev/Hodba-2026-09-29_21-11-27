using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Hodba.Client
{
    /// <summary>
    /// Единственный объект сцены «Заставка» — первой сцены игры. Показывает логотип и тем временем
    /// догружает «Поле» вторым слоем: пока надпись проступает, мир собирается и прогоняется под ней
    /// (Bootstrap.Holding — рисуется, но время стоит). Когда мир готов и надпись выстояла своё,
    /// заставка разлетается по ветру, ветер мира нарастает из тишины, и сцена выгружается.
    /// Сцену создаёт меню Hodba ▸ Setup Title.
    /// </summary>
    public sealed class Title : MonoBehaviour
    {
        public Shader shader;
        public string fieldScene = "Field";

        /// <summary>Раньше этого момента заставка не отпускает мир, даже если он давно готов.</summary>
        const float HoldUntil = 5.4f;
        /// <summary>Кадров, которые готовый мир рисуется под заставкой до выхода: шейдеры и буферы успевают прогреться.</summary>
        const int WarmFrames = 24;
        const float BlowSeconds = 2.8f;
        const float AppearFrom = 0.6f;
        const float AppearTo = 4.4f;
        const float SoundFrom = 0.4f;
        const float SoundSeconds = 5f;
        /// <summary>Шаг кадра в анимации не больше этого: тяжёлый кадр загрузки не съедает кусок логотипа.</summary>
        const float MaxStep = 1f / 20f;

        static readonly int ParamsId = Shader.PropertyToID("_Params");
        static readonly int SdfId = Shader.PropertyToID("_Sdf");
        static readonly int FrameId = Shader.PropertyToID("_Frame");
        static readonly int GlyphsId = Shader.PropertyToID("_Glyphs");

        Material _material;
        Texture2D _glyphs;
        float _time;

        void Start()
        {
            if (shader == null)
            {
                Debug.LogError("Hodba: у Title нет шейдера. Запусти Hodba ▸ Setup Title.");
                enabled = false;
                return;
            }

            Application.targetFrameRate = 30;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            CreateCamera();
            _glyphs = TitleGlyphs.Build();
            _material = new Material(shader) { name = "Title" };
            _material.SetTexture(GlyphsId, _glyphs);
            _material.SetVector(SdfId, new Vector4(
                TitleGlyphs.MaxDistancePx / TitleGlyphs.Width, TitleGlyphs.StrokeHalfWidth, 0.012f, 0f));
            _material.SetVector(FrameId, new Vector4(TitleGlyphs.Width / (float)TitleGlyphs.Height, 0f, 0f, 0f));
            CreateCanvas();
            Draw(0f, 0f, 1f);

            // Мир молчит, пока заставка не начала дышать: слушатель появится вместе с полем.
            AudioListener.volume = 0f;
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            Bootstrap.Holding = true;
            // Два кадра заставки на экране до тяжёлой загрузки.
            yield return null;
            yield return null;

            var load = SceneManager.LoadSceneAsync(fieldScene, LoadSceneMode.Additive);
            Bootstrap boot = null;
            bool searched = false;
            int warm = 0;

            while (true)
            {
                _time += Mathf.Min(Time.unscaledDeltaTime, MaxStep);
                Draw(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(AppearFrom, AppearTo, _time)), 0f, 1f);
                AudioListener.volume = Mathf.SmoothStep(0f, 1f, (_time - SoundFrom) / SoundSeconds);

                if (load.isDone && !searched)
                {
                    searched = true;
                    SceneManager.SetActiveScene(SceneManager.GetSceneByName(fieldScene));
                    boot = FindAnyObjectByType<Bootstrap>();
                    if (boot == null || !boot.isActiveAndEnabled)
                        Debug.LogError("Hodba: в сцене «Поле» нет рабочего Bootstrap, заставка не ждёт мир.");
                }

                bool worldReady = searched && (boot == null || !boot.isActiveAndEnabled || boot.Ready);
                if (worldReady) warm++;
                if (worldReady && warm >= WarmFrames && _time >= HoldUntil) break;
                yield return null;
            }

            Bootstrap.Holding = false;
            AudioListener.volume = 1f;

            float blowTime = 0f;
            while (blowTime < BlowSeconds)
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, MaxStep);
                _time += dt;
                blowTime += dt;
                float u = Mathf.Clamp01(blowTime / BlowSeconds);
                Draw(1f, u * u * (3f - 2f * u), 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, u)));
                yield return null;
            }

            SceneManager.UnloadSceneAsync(gameObject.scene);
        }

        void Draw(float appear, float blow, float fade)
        {
            _material.SetVector(ParamsId, new Vector4(appear, blow, fade, _time));
        }

        void CreateCamera()
        {
            var go = new GameObject("Title camera");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            // Под камерой поля: она появится позже и закроет эту, а заставка рисуется поверх обеих.
            cam.depth = -100f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        }

        void CreateCanvas()
        {
            var canvasGo = new GameObject("Title canvas", typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var go = new GameObject("Logo", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var image = go.GetComponent<RawImage>();
            image.raycastTarget = false;
            image.material = _material;
        }

        void OnDestroy()
        {
            Bootstrap.Holding = false;
            if (_material != null) Destroy(_material);
            if (_glyphs != null) Destroy(_glyphs);
        }
    }
}
