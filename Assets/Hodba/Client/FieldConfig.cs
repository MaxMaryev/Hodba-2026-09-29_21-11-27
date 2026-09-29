using System.Collections.Generic;
using Hodba.Sim.Walk;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hodba.Client
{
    /// <summary>
    /// Все ручки атмосферы «Поля». Меняются прямо в Play Mode — так атмосфера и настраивается.
    /// Небо и свет заданы по высоте солнца: от −20° (глубокая ночь) до 70° (полдень).
    /// </summary>
    [CreateAssetMenu(menuName = "Hodba/Field Config", fileName = "FieldConfig")]
    public sealed class FieldConfig : ScriptableObject
    {
        [Header("Мир")]
        public uint seed = 1;

        [Header("Ходьба")]
        public WalkParams walk = WalkParams.Default;

        [Header("Тело и камера")]
        public float eyeHeight = 1.65f;
        public float fov = 60f;
        [Tooltip("Покачивание вверх-вниз за шаг, м. Отключить нельзя — закон 6.")]
        public float bobVertical = 0.035f;
        [Tooltip("Раскачка в стороны за пару шагов, м.")]
        public float bobLateral = 0.025f;
        [Tooltip("Крен за пару шагов, °.")]
        public float bobRoll = 0.6f;
        [Tooltip("Случайная неровность каждого шага, доля.")]
        [Range(0f, 0.5f)] public float stepIrregularity = 0.12f;
        [Tooltip("Насколько взгляд гасит тряску: 0 — никак, 1 — точка впереди неподвижна.")]
        [Range(0f, 1f)] public float gazeStabilization = 0.7f;
        public float stabilizationDistance = 10f;
        public float breathAmplitude = 0.004f;
        public float breathRate = 0.25f;
        [Tooltip("Крен при повороте тела, ° на каждый °/с.")]
        public float turnLean = 0.03f;

        [Header("Взгляд")]
        [Tooltip("Градусов на ширину экрана при ведении пальцем.")]
        public float lookSensitivity = 110f;
        public float lookSmoothing = 0.06f;
        public float neckYawLimit = 100f;
        public float pitchMin = -70f;
        public float pitchMax = 70f;
        [Tooltip("Отклонение взгляда от курса, после которого человек может повернуть, °.")]
        public float courseFollowAngle = 12f;
        [Tooltip("Сколько смотреть в сторону, чтобы он повернул туда, с.")]
        public float courseFollowDelay = 1.2f;

        [Header("Всмотреться")]
        public float focusFov = 42f;
        [Range(0f, 1f)] public float focusSpeedFactor = 0.4f;
        public float focusTime = 0.8f;

        [Header("Время")]
        [Tooltip("Широта мира, °. Пустынный пояс.")]
        public float latitude = 28f;
        [Tooltip("Взять время отсюда, а не с часов устройства.")]
        public bool overrideTime;
        [Range(0f, 24f)] public float overrideHour = 7f;
        [Tooltip("День года (1..365), −1 — сегодняшний.")]
        public int dayOfYearOverride = -1;

        [Header("Небо и свет — по высоте солнца (−20° … 70°)")]
        public float elevationMin = -20f;
        public float elevationMax = 70f;
        public Gradient skyZenith = Palette.SkyZenith();
        public Gradient skyHorizon = Palette.SkyHorizon();
        public Gradient fogColor = Palette.Fog();
        public Gradient sunColor = Palette.Sun();
        public Gradient ambientSky = Palette.AmbientSky();
        public Gradient ambientEquator = Palette.AmbientEquator();
        public Gradient ambientGround = Palette.AmbientGround();
        [Tooltip("Сила солнца от высоты солнца, °.")]
        public AnimationCurve sunIntensity = Palette.SunIntensity();
        [Tooltip("Плотность пыльной дымки от высоты солнца, °.")]
        public AnimationCurve fogDensity = Palette.FogDensity();
        [Tooltip("Экспозиция глаза от высоты солнца, °.")]
        public AnimationCurve exposure = Palette.Exposure();
        public Color nightLightColor = new Color(0.55f, 0.64f, 0.80f);
        public float nightLightIntensity = 0.035f;
        [Range(0f, 1f)] public float shadowStrength = 0.85f;
        public float sunDiscSize = 0.6f;
        public float sunGlow = 1f;
        [Range(0.01f, 0.5f)] public float hazeHeight = 0.12f;
        [Range(0.1f, 2f)] public float horizonCurve = 0.45f;
        public float starBrightness = 1.4f;

        [Header("Ослепление и цвет")]
        public float glareExposure = 1.1f;
        public float glareBloom = 2.2f;
        public float glarePower = 6f;
        [Tooltip("Как быстро слепит, с.")]
        public float glareRise = 1.2f;
        [Tooltip("Как долго отпускает, с.")]
        public float glareFall = 5f;
        public float baseBloom = 0.35f;
        public float saturation = -12f;
        public float contrast = 8f;
        [Range(0f, 1f)] public float vignette = 0.2f;

        [Header("Земля")]
        public int chunkSize = 512;
        [Tooltip("Радиус видимой земли в чанках.")]
        public int viewChunks = 6;
        public float lod0Distance = 800f;
        public float lod1Distance = 1700f;
        public int lod0Resolution = 128;
        public int lod1Resolution = 32;
        public int lod2Resolution = 8;
        [Tooltip("Сколько чанков достраивать за кадр.")]
        public int chunksPerFrame = 2;

        [Header("Камни")]
        public List<Mesh> stoneMeshes = new List<Mesh>();
        public List<Mesh> boulderMeshes = new List<Mesh>();
        public Material stoneMaterial;
        [Tooltip("Материал валунов. Пусто — как у камней.")]
        public Material boulderMaterial;
        [Tooltip("Модели со стороны уже нужного размера (М1: 5–40 см, М2: 0,6–1,8 м) — не масштабировать под диапазон.")]
        public bool authoredStoneSizes = true;
        public float stoneCellSize = 16f;
        [Tooltip("Среднее число мелких камней на клетку.")]
        public float stonesPerCell = 1.4f;
        public Vector2 stoneSize = new Vector2(0.05f, 0.4f);
        public float stoneRadius = 120f;
        public float boulderCellSize = 128f;
        [Range(0f, 1f)] public float boulderChance = 0.15f;
        public Vector2 boulderSize = new Vector2(0.6f, 1.8f);
        public float boulderRadius = 1400f;

        [Header("Ветер и пыль")]
        [Tooltip("Куда дует преобладающий ветер, ° (90 — на восток). Рябь на пепле лежит поперёк.")]
        public float prevailingWind = 90f;
        public float windWander = 35f;
        [Range(0f, 1f)] public float windBase = 0.35f;
        [Range(0f, 1f)] public float windGust = 0.5f;
        public float gustPeriod = 9f;
        public int dustCount = 300;
        public float dustBox = 36f;
        public float driftRate = 30f;

        [Header("Звук")]
        [Range(0f, 1f)] public float masterVolume = 0.9f;
        [Range(0f, 1f)] public float windVolume = 0.7f;
        [Range(0f, 1f)] public float stepVolume = 0.55f;
        [Tooltip("Настоящие шаги, по одному шагу в клипе. Пусто — синтез.")]
        public AudioClip[] footstepClips = new AudioClip[0];
        [Tooltip("Ровный ветер (бесшовная петля). Пусто — синтез.")]
        public AudioClip windLoop;
        [Tooltip("Ветер с порывами (петля). Подмешивается, когда порыв.")]
        public AudioClip windGustLoop;
        [Tooltip("Шорох пепла (петля). Растёт с силой ветра.")]
        public AudioClip ashHissLoop;
        [Range(0f, 1f)] public float ashHissVolume = 0.4f;

        [Header("Следы")]
        public int footprintMax = 1500;
        public float footprintLifetimeMinutes = 180f;
        public Vector2 footprintSize = new Vector2(0.13f, 0.30f);
        public float footprintOffset = 0.12f;
        [Tooltip("Серая, 0.5 — нейтраль. Импорт без sRGB. Пусто — сгенерированная.")]
        public Texture2D footprintTexture;

        [Header("Материалы (создаёт Hodba ▸ Setup Field)")]
        public Material groundMaterial;
        public Material skyMaterial;
        public Material footprintMaterial;
        public Material dustMaterial;
        public Material driftMaterial;
        public VolumeProfile volumeProfile;

        [Header("Ассеты со стороны (Docs/Assets/field-assets.md)")]
        [Tooltip("Т1 albedo")] public Texture2D ashAlbedo;
        [Tooltip("Т1 normal")] public Texture2D ashNormal;
        [Tooltip("Т2 normal")] public Texture2D rippleNormal;
        [Tooltip("Т3 albedo")] public Texture2D packedAlbedo;
        [Tooltip("М3: префаб путника с Animator. Виден только его тень.")]
        public GameObject walkerPrefab;

        [Header("Путь между запусками")]
        [Tooltip("Продолжать с того же места; если шёл — досчитать, сколько прошёл без тебя.")]
        public bool continueFromSave = true;
        public float backgroundMaxHours = 168f;
    }
}
