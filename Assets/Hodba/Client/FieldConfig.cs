using System.Collections.Generic;
using Hodba.Client.Body;
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
        [Tooltip("Field — обычное поле. ProvingGround — полигон для настройки тела: твёрдо → рыхло → подъём → плато, на север от (0,0).")]
        public WorldKind worldKind = WorldKind.Field;
        [Tooltip("Отладка: сильный встречный ветер (сцена «остановка на ветру»).")]
        public bool debugHeadwind;

        [Header("Ходьба")]
        public WalkParams walk = WalkParams.Default;

        [Header("Камера")]
        public float eyeHeight = 1.65f;
        public float fov = 60f;
        [Tooltip("Насколько взгляд гасит тряску: 0 — никак, 1 — точка впереди неподвижна.")]
        [Range(0f, 1f)] public float gazeStabilization = 0.7f;
        public float stabilizationDistance = 10f;

        // Тело — из кирпичиков: у каждого модуля свои настройки. Все числа стартовые, подбираются на телефоне.
        [Header("Тело: походка и опора")]
        public GaitSettings gait = GaitSettings.Default;
        [Header("Тело: усилие, дыхание, осторожность")]
        public ExertionSettings exertion = ExertionSettings.Default;
        [Header("Тело: как слои складываются в голову")]
        public PoseSettings pose = PoseSettings.Default;
        [Header("Глаза: веки, моргание, прищур")]
        public EyelidSettings eyelids = EyelidSettings.Default;
        [Header("Глаза: блуждающий взгляд")]
        public EyeWanderSettings eyes = EyeWanderSettings.Default;
        [Header("Глаза: периферия и туннель")]
        public PeripherySettings periphery = PeripherySettings.Default;

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

        [Header("Дымка и тени пыльных облаков")]
        [Tooltip("Насколько дымка светлее и теплее в сторону солнца (и в небе у горизонта, и над землёй).")]
        [Range(0f, 1f)] public float hazeForward = 0.35f;
        [Tooltip("Насколько дымка темнее в сторону от солнца.")]
        [Range(0f, 0.5f)] public float hazeAway = 0.12f;
        [Tooltip("Толщина приземного слоя дымки, м: в низинах гуще, на возвышенностях реже.")]
        public float fogLayerHeight = 25f;
        [Tooltip("Доля дымки, не зависящая от высоты.")]
        [Range(0f, 1f)] public float fogLayerBase = 0.45f;
        [Tooltip("Во сколько раз в низине гуще, не больше.")]
        public float fogValleyBoost = 2.5f;
        [Tooltip("За сколько секунд уровень слоя догоняет землю под путником: поднялся на бархан — низина осталась в дымке.")]
        public float fogLevelLag = 45f;
        [Tooltip("Шум облаков (создаёт Hodba ▸ Setup Field). Пусто — теней нет.")]
        public Texture2D dustShadowTexture;
        [Range(0f, 1f)] public float dustShadowStrength = 0.35f;
        [Tooltip("Доля неба в облаках.")]
        [Range(0f, 1f)] public float dustShadowCoverage = 0.4f;
        [Range(0.01f, 0.5f)] public float dustShadowSoftness = 0.12f;
        [Tooltip("Высота облаков, м: на низком солнце тень ложится далеко от облака.")]
        public float dustShadowHeight = 400f;
        [Tooltip("Тайл шума, м; округляется до степени двойки, чтобы делить 4096.")]
        public float dustShadowTile = 2048f;
        [Tooltip("Скорость облаков относительно ветра у земли.")]
        public float dustShadowSpeed = 1.6f;

        [Header("Ослепление и цвет")]
        public float glareExposure = 1.1f;
        public float glareBloom = 2.2f;
        [Tooltip("Как быстро слепит, с. Острота конуса ослепления — в «Глаза: веки» (sunPower).")]
        public float glareRise = 1.2f;
        [Tooltip("Как долго отпускает, с.")]
        public float glareFall = 5f;
        public float baseBloom = 0.35f;
        public float saturation = -12f;
        public float contrast = 8f;
        [Range(0f, 1f)] public float vignette = 0.2f;

        [Header("Земля: кольца вокруг путника")]
        [Tooltip("Сколько колец. Каждое вдвое крупнее предыдущего; 9 колец от 0,25 м — это ~4 км, дальше дымка.")]
        public int clipLevels = 9;
        [Tooltip("Шаг сетки у ног, м. Стартовое значение, проверить на телефоне.")]
        public float clipSpacing = 0.25f;
        [Tooltip("Ширина перетекания в следующее кольцо, клеток.")]
        public float clipMorph = 12f;
        [Tooltip("Сколько ближних колец отбрасывают тени (рябь и бугры на рассвете).")]
        public int clipShadowLevels = 2;

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
        [Tooltip("Насколько гуще дымка в сильный порыв: ветер поднимает пыль.")]
        [Range(0f, 2f)] public float dustRaise = 0.6f;

        [Header("Позёмка: песок бежит по земле")]
        [Tooltip("Струи и фронты порыва (создаёт Hodba ▸ Setup Field). Пусто — позёмки нет.")]
        public Texture2D saltationTexture;
        [Tooltip("Сила ветра (0..1), с которой песок начинает бежать.")]
        [Range(0f, 1f)] public float saltationThreshold = 0.35f;
        [Range(0f, 2f)] public float saltationStrength = 1f;
        [Tooltip("Насколько струя закрывает землю.")]
        [Range(0f, 1f)] public float saltationOpacity = 0.35f;
        public Color saltationColor = new Color(0.84f, 0.81f, 0.76f);
        [Tooltip("Тайл мелких струй, м; крупные — в 2,6 раза больше.")]
        public float saltationTile = 6f;
        [Tooltip("Скорость струй относительно ветра.")]
        public float saltationSpeed = 0.8f;
        [Tooltip("Дальше этого, м, позёмку не видно.")]
        public float saltationDistance = 60f;
        [Tooltip("Размер фронтов порыва, м: пятна, которые бегут по пустыне со скоростью ветра.")]
        public float gustFrontScale = 80f;
        [Tooltip("Песчинок в секунду у ног в самый сильный порыв.")]
        public float sprayRate = 240f;

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
        [Tooltip("Дыхание: на ровном почти не слышно, после подъёма — да.")]
        [Range(0f, 1f)] public float breathVolume = 0.35f;
        [Tooltip("Ткань, лямки, снаряжение.")]
        [Range(0f, 1f)] public float gearVolume = 0.3f;
        [Tooltip("Подробности шага: перекат подошвы, осыпь, камень.")]
        [Range(0f, 1f)] public float stepDetailVolume = 0.5f;

        [Header("Следы")]
        public int footprintMax = 1500;
        public float footprintLifetimeMinutes = 180f;
        public Vector2 footprintSize = new Vector2(0.13f, 0.30f);
        [Tooltip("Серая, 0.5 — нейтраль. Импорт без sRGB. Пусто — сгенерированная.")]
        public Texture2D footprintTexture;

        [Header("Материалы (создаёт Hodba ▸ Setup Field)")]
        public Material groundMaterial;
        public Material skyMaterial;
        public Material footprintMaterial;
        public Material dustMaterial;
        public Material driftMaterial;
        public VolumeProfile volumeProfile;
        [Tooltip("Hidden/Hodba/Eye — веки поверх картинки.")]
        public Shader eyeShader;

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

        public StoneLayout StoneLayout => new StoneLayout(stoneCellSize, stonesPerCell, boulderChance, false, stoneSize);
        public StoneLayout BoulderLayout => new StoneLayout(boulderCellSize, stonesPerCell, boulderChance, true, boulderSize);
    }

    public enum WorldKind
    {
        Field,
        ProvingGround,
    }
}
