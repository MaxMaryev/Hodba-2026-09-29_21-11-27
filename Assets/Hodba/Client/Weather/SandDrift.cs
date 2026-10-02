using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Пустыня отвечает ветру: взвесь над землёй, позёмка по земле, песчинки у ног. Ветер о песке ничего не знает.
    /// Слои песка живут в кадре ветра (<see cref="Wind"/>): каждый прибавляет шаг поля за кадр и отстаёт от воздуха
    /// у глаз по своему — у земли песок медленнее, выше быстрее (логарифмический профиль). Своего центра нет —
    /// струи и языки не расходятся с фронтами порывов. Рисуют шейдеры земли и взвеси.
    /// </summary>
    public sealed class SandDrift
    {
        public const float CoarseScale = 2.6f;
        /// <summary>Высоты оболочек взвеси над землёй, м.</summary>
        public static readonly float[] VeilHeights = { 0.08f, 0.3f, 0.7f };

        const float Roughness = 0.02f, EyeHeight = 1.5f;

        static readonly int ParamsId = Shader.PropertyToID("_SandDrift");
        static readonly int OffsetId = Shader.PropertyToID("_SandDriftOffset");
        static readonly int TilesId = Shader.PropertyToID("_SandDriftTiles");
        static readonly int ColorId = Shader.PropertyToID("_SandDriftColor");
        static readonly int LayersId = Shader.PropertyToID("_SandDriftLayers");
        static readonly int TexId = Shader.PropertyToID("_SandDriftTex");

        double _fineX, _fineY, _coarseX, _coarseY;
        readonly double[] _layerX = new double[3], _layerY = new double[3];
        readonly Vector4[] _layers = new Vector4[3];

        /// <summary>Сила взвеси, 0..1: сколько песка и пыли ветер держит в воздухе.</summary>
        public float Veil { get; private set; }
        /// <summary>Сила позёмки, 0..1+.</summary>
        public float Saltation { get; private set; }
        /// <summary>Сила песчинок у ног, 0..1.</summary>
        public float Grain { get; private set; }

        public void Tick(Wind wind, FieldConfig config, float dt)
        {
            Veil = VeilResponse(wind.Strength, config);
            Saltation = Response(wind.Strength, config.saltationThreshold) * config.saltationStrength;
            Grain = Response(wind.Strength, config.grainThreshold);

            float speed = wind.Velocity.magnitude;
            var step = wind.Step;
            // Шаг поля уже несёт полный ветер (−speed·dt); слой возвращает себе то, на что он медленнее воздуха у глаз.
            double fine = step.x + speed * (1f - config.saltationSpeed) * dt;
            double coarse = step.x + speed * (1f - config.saltationSpeed * 0.7f) * dt;
            float fineTile = FineTile(config), coarseTile = fineTile * CoarseScale;
            _fineX = Wrap(_fineX + fine, fineTile); _fineY = Wrap(_fineY + step.y, fineTile);
            _coarseX = Wrap(_coarseX + coarse, coarseTile); _coarseY = Wrap(_coarseY + step.y, coarseTile);

            float veilPeriod = VeilPeriod(config);
            for (int i = 0; i < 3; i++)
            {
                float k = Profile(VeilHeights[i]);
                _layerX[i] = Wrap(_layerX[i] + step.x + speed * (1f - k) * dt, veilPeriod);
                _layerY[i] = Wrap(_layerY[i] + step.y, veilPeriod);
                _layers[i] = new Vector4((float)_layerX[i], (float)_layerY[i], VeilHeights[i], k);
            }

            Push(config, Veil, Saltation,
                new Vector4((float)_fineX, (float)_fineY, (float)_coarseX, (float)_coarseY), _layers);
        }

        /// <summary>Поставить глобальные значения для шейдеров земли и взвеси.</summary>
        public static void Push(FieldConfig config, float veil, float saltation, Vector4 groundOffset, Vector4[] layers)
        {
            bool on = config.saltationTexture != null;
            if (on) Shader.SetGlobalTexture(TexId, config.saltationTexture);
            float fine = FineTile(config);
            Shader.SetGlobalVector(ParamsId, new Vector4(on ? veil : 0f, on ? saltation : 0f, config.saltationDistance, config.saltationOpacity));
            Shader.SetGlobalVector(OffsetId, groundOffset);
            Shader.SetGlobalVector(TilesId, new Vector4(1f / fine, 1f / (fine * CoarseScale), 1f / VeilTile(config), config.veilDensity));
            Shader.SetGlobalColor(ColorId, config.saltationColor);
            Shader.SetGlobalVectorArray(LayersId, layers);
        }

        /// <summary>Слои для превью: неподвижные, на своих высотах.</summary>
        public static Vector4[] StillLayers()
        {
            var layers = new Vector4[3];
            for (int i = 0; i < 3; i++) layers[i] = new Vector4(0f, 0f, VeilHeights[i], Profile(VeilHeights[i]));
            return layers;
        }

        /// <summary>Взвесь видна уже при лёгком ветре: тонкие языки, а не ничего.</summary>
        public static float VeilResponse(float strength, FieldConfig config) =>
            Mathf.Pow(Response(strength, config.veilThreshold), 0.6f);

        public static float Response(float strength, float threshold) =>
            Mathf.Clamp01((strength - threshold) / Mathf.Max(0.05f, 1f - threshold));

        /// <summary>Доля скорости воздуха у глаз на высоте h: ln(h/z0) / ln(hГлаз/z0).</summary>
        static float Profile(float height) =>
            Mathf.Clamp01(Mathf.Log(height / Roughness) / Mathf.Log(EyeHeight / Roughness));

        static float FineTile(FieldConfig config) => Mathf.Max(0.5f, config.saltationTile);
        static float VeilTile(FieldConfig config) => Mathf.Max(1f, config.veilTile);
        /// <summary>Шейдер берёт языки на тайле и изгиб на тройном — сдвиг сворачивается по общему периоду.</summary>
        static float VeilPeriod(FieldConfig config) => VeilTile(config) * 3f;

        static double Wrap(double v, double period)
        {
            v %= period;
            return v < 0 ? v + period : v;
        }
    }
}
