using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Позёмка: песок бежит по земле струями вдоль ветра, порывы приходят фронтами. Рисует шейдер земли.
    /// Рисунок живёт в координатах ветра вокруг путника: q = R(ветер)·(xz − pivot) + сдвиг. Сдвиг копится здесь в double:
    /// путник идёт — сдвиг догоняет его, и рисунок стоит в мире; ветер дует — струи бегут.
    /// Перенос плавающего центра сдвигает xz и pivot одинаково — рисунок не прыгает.
    /// </summary>
    public sealed class Saltation
    {
        public const float CoarseScale = 2.6f;

        static readonly int ParamsId = Shader.PropertyToID("_HodbaSaltation");
        static readonly int FrameId = Shader.PropertyToID("_HodbaSaltationFrame");
        static readonly int OffsetId = Shader.PropertyToID("_HodbaSaltationOffset");
        static readonly int FrontId = Shader.PropertyToID("_HodbaSaltationFront");
        static readonly int TilesId = Shader.PropertyToID("_HodbaSaltationTiles");
        static readonly int ColorId = Shader.PropertyToID("_HodbaSaltationColor");
        static readonly int TexId = Shader.PropertyToID("_HodbaSaltationTex");

        double _fineX, _fineY, _coarseX, _coarseY, _frontX, _frontY;
        Vector2 _pivot;
        bool _hasPivot;

        public Saltation(FloatingOrigin origin)
        {
            // Перенос центра — не шаг путника: pivot сдвигается вместе со всем миром.
            origin.Shifted += d => _pivot += new Vector2(d.x, d.z);
        }

        /// <summary>Сколько песка бежит, 0..1: ниже порога силы ветра — ничего.</summary>
        public static float Intensity(Wind wind, FieldConfig config)
        {
            float t = Mathf.Clamp01((wind.Strength - config.saltationThreshold) / Mathf.Max(0.05f, 1f - config.saltationThreshold));
            return t * config.saltationStrength;
        }

        public void Tick(Wind wind, FieldConfig config, Vector3 cameraLocal, float dt)
        {
            var dir = Direction(wind);
            var across = new Vector2(-dir.y, dir.x);
            var pivot = new Vector2(cameraLocal.x, cameraLocal.z);
            if (_hasPivot)
            {
                var step = pivot - _pivot;
                double a = Vector2.Dot(step, dir), c = Vector2.Dot(step, across);
                _fineX += a; _fineY += c;
                _coarseX += a; _coarseY += c;
                _frontX += a; _frontY += c;
            }
            _pivot = pivot;
            _hasPivot = true;

            double speed = wind.Velocity.magnitude;
            _fineX -= speed * config.saltationSpeed * dt;
            _coarseX -= speed * config.saltationSpeed * 0.65 * dt;
            _frontX -= speed * dt;

            float fine = FineTile(config), coarse = fine * CoarseScale, front = FrontTile(config);
            _fineX = Wrap(_fineX, fine); _fineY = Wrap(_fineY, fine);
            _coarseX = Wrap(_coarseX, coarse); _coarseY = Wrap(_coarseY, coarse);
            _frontX = Wrap(_frontX, front); _frontY = Wrap(_frontY, front);

            Push(config, Intensity(wind, config), wind.Gust, dir, pivot,
                new Vector4((float)_fineX, (float)_fineY, (float)_coarseX, (float)_coarseY),
                new Vector2((float)_frontX, (float)_frontY));
        }

        /// <summary>Поставить глобальные значения для шейдера земли. dir — куда дует (xz, единичный).</summary>
        public static void Push(FieldConfig config, float intensity, float gust, Vector2 dir, Vector2 pivot, Vector4 offset, Vector2 frontOffset)
        {
            bool on = config.saltationTexture != null && intensity > 0f;
            if (on) Shader.SetGlobalTexture(TexId, config.saltationTexture);
            float fine = FineTile(config);
            Shader.SetGlobalVector(ParamsId, new Vector4(on ? intensity : 0f, gust, config.saltationDistance, config.saltationOpacity));
            Shader.SetGlobalVector(FrameId, new Vector4(dir.x, dir.y, pivot.x, pivot.y));
            Shader.SetGlobalVector(OffsetId, offset);
            Shader.SetGlobalVector(FrontId, new Vector4(frontOffset.x, frontOffset.y, 0f, 0f));
            Shader.SetGlobalVector(TilesId, new Vector4(1f / fine, 1f / (fine * CoarseScale), 1f / FrontTile(config), 0f));
            Shader.SetGlobalColor(ColorId, config.saltationColor);
        }

        static Vector2 Direction(Wind wind)
        {
            float rad = wind.Direction * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        }

        static float FineTile(FieldConfig config) => Mathf.Max(0.5f, config.saltationTile);
        static float FrontTile(FieldConfig config) => Mathf.Max(5f, config.gustFrontScale);

        static double Wrap(double v, double period)
        {
            v %= period;
            return v < 0 ? v + period : v;
        }
    }
}
