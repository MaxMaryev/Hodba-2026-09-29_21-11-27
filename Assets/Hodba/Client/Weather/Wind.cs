using System;
using Hodba.Core;
using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Ветер — поле над пустыней. Направление бродит вокруг преобладающего, погода сменяет затишья ветреными часами,
    /// а фронты порывов бегут по миру со скоростью ветра. Кадр ветра вокруг путника: q = R(ветер)·(xz − pivot) + сдвиг.
    /// Сдвиг копится в double: путник идёт — сдвиг догоняет его, и поле стоит в мире; ветер дует — фронты бегут.
    /// Порыв у путника — выборка того же поля, что видят шейдеры, поэтому видимый фронт, звук и прищур приходят вместе.
    /// </summary>
    public sealed class Wind
    {
        /// <summary>Текселей по стороне текстуры фронтов.</summary>
        public const int FrontSize = 128;

        static readonly int ParamsId = Shader.PropertyToID("_HodbaWind");
        static readonly int FrameId = Shader.PropertyToID("_HodbaWindFrame");
        static readonly int FrontId = Shader.PropertyToID("_HodbaWindFront");
        static readonly int FrontTexId = Shader.PropertyToID("_HodbaWindFrontTex");

        static Texture2D _frontTexture;
        static float[] _front;

        double _offsetX, _offsetY;
        Vector2 _pivot;
        bool _hasPivot;

        /// <summary>Куда дует, ° от севера по часовой.</summary>
        public float Direction { get; private set; }
        /// <summary>0..1.</summary>
        public float Strength { get; private set; }
        /// <summary>0..1 — порыв в точке путника.</summary>
        public float Gust { get; private set; }
        /// <summary>Скорость воздуха у глаз, м/с.</summary>
        public Vector3 Velocity { get; private set; }
        /// <summary>Куда дует, единичный вектор в xz.</summary>
        public Vector2 Axis { get; private set; }
        /// <summary>Путник, локальные xz: центр кадра ветра.</summary>
        public Vector2 Pivot => _pivot;
        /// <summary>Сдвиг поля за этот кадр в координатах ветра (x — по ветру, y — поперёк), м. Слои песка прибавляют его к своим.</summary>
        public Vector2 Step { get; private set; }

        public Wind(FloatingOrigin origin)
        {
            // Перенос центра — не шаг путника: pivot сдвигается вместе со всем миром.
            origin.Shifted += d => _pivot += new Vector2(d.x, d.z);
        }

        /// <param name="walker">Путник, локальные координаты.</param>
        /// <param name="course">Курс путника, °: нужен только отладочному встречному ветру.</param>
        public void Tick(FieldConfig config, float time, Vector3 walker, float dt, float course = 0f)
        {
            float wander = (Mathf.PerlinNoise(time * 0.011f, 0.37f) - 0.5f) * 2f * config.windWander;
            Direction = config.debugHeadwind ? course + 180f + wander * 0.2f : config.prevailingWind + wander;
            float rad = Direction * Mathf.Deg2Rad;
            Axis = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));

            float tile = FrontTile(config);
            var pivot = new Vector2(walker.x, walker.z);
            var step = Vector2.zero;
            if (_hasPivot)
            {
                var moved = pivot - _pivot;
                step = new Vector2(Vector2.Dot(moved, Axis), Vector2.Dot(moved, new Vector2(-Axis.y, Axis.x)));
            }
            _pivot = pivot;
            _hasPivot = true;

            Gust = GustAt((float)_offsetX, (float)_offsetY, tile);
            float slow = Mathf.PerlinNoise(time * 0.004f, 9.3f); // долгие затишья и ветреные часы
            Strength = Mathf.Clamp01(config.windBase * Mathf.Lerp(0.4f, 1.3f, slow) + config.windGust * Gust);
            if (config.debugHeadwind) Strength = Mathf.Clamp01(0.75f + 0.25f * Gust);
            if (config.windOverride >= 0f) Strength = Mathf.Clamp01(config.windOverride);

            float speed = Mathf.Lerp(1.5f, 9f, Strength);
            Velocity = new Vector3(Axis.x, 0f, Axis.y) * speed;

            step.x -= speed * dt;
            Step = step;
            _offsetX = Wrap(_offsetX + step.x, tile);
            _offsetY = Wrap(_offsetY + step.y, tile);

            Push(Axis, speed, Strength, _pivot, new Vector2((float)_offsetX, (float)_offsetY), tile, config.windGust);
        }

        /// <summary>Поставить глобальные значения для шейдеров. axis — куда дует (xz, единичный), offset — сдвиг фронтов, м.</summary>
        public static void Push(Vector2 axis, float speed, float strength, Vector2 pivot, Vector2 offset, float tile, float gustShare)
        {
            Shader.SetGlobalTexture(FrontTexId, FrontTexture);
            Shader.SetGlobalVector(ParamsId, new Vector4(axis.x, axis.y, speed, strength));
            Shader.SetGlobalVector(FrameId, new Vector4(pivot.x, pivot.y, 0f, 0f));
            Shader.SetGlobalVector(FrontId, new Vector4(offset.x, offset.y, 1f / tile, gustShare));
        }

        public static float FrontTile(FieldConfig config) => Mathf.Max(64f, config.gustFrontScale);

        /// <summary>Порыв 0..1 в точке q (координаты ветра со сдвигом), как его видит шейдер.</summary>
        public static float GustAt(float qx, float qy, float tile)
        {
            float n = SampleFront(qx / tile, qy / tile);
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.85f, n));
        }

        /// <summary>Фронты порывов: R8, бесшовная, одна на процесс. Шейдеры и CPU читают одни и те же байты.</summary>
        public static Texture2D FrontTexture
        {
            get
            {
                if (_frontTexture == null) BuildFront();
                return _frontTexture;
            }
        }

        /// <summary>Билинейно от центров текселов, как GPU с Repeat.</summary>
        static float SampleFront(float u, float v)
        {
            if (_front == null) BuildFront();
            float x = u * FrontSize - 0.5f, y = v * FrontSize - 0.5f;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float a = Texel(x0, y0), b = Texel(x0 + 1, y0), c = Texel(x0, y0 + 1), d = Texel(x0 + 1, y0 + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Texel(int x, int y) => _front[(y & (FrontSize - 1)) * FrontSize + (x & (FrontSize - 1))];

        /// <summary>
        /// Три октавы шума значений; U — по ветру, V — поперёк, клеток поперёк вдвое меньше: фронт — полоса, а не круг.
        /// Потом выравнивание по гистограмме: значения равномерны на 0..1, и сильные порывы бывают, а не тонут в середине.
        /// </summary>
        static void BuildFront()
        {
            const int n = FrontSize * FrontSize;
            var raw = new float[n];
            for (int y = 0; y < FrontSize; y++)
            for (int x = 0; x < FrontSize; x++)
            {
                float u = (x + 0.5f) / FrontSize, v = (y + 0.5f) / FrontSize;
                raw[y * FrontSize + x] = Octave(u, v, 16, 8, 0x51u) * 0.55f
                    + Octave(u, v, 32, 16, 0x52u) * 0.3f
                    + Octave(u, v, 64, 32, 0x53u) * 0.15f;
            }

            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort((float[])raw.Clone(), order);
            var bytes = new byte[n];
            _front = new float[n];
            for (int rank = 0; rank < n; rank++)
            {
                byte b = (byte)Mathf.RoundToInt(rank * 255f / (n - 1));
                bytes[order[rank]] = b;
                _front[order[rank]] = b / 255f;
            }

            if (_frontTexture != null) return;
            _frontTexture = new Texture2D(FrontSize, FrontSize, TextureFormat.R8, false, true)
            {
                name = "Wind Fronts",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _frontTexture.LoadRawTextureData(bytes);
            _frontTexture.Apply(false, true);
        }

        static float Octave(float u, float v, int periodU, int periodV, uint seed)
        {
            float x = u * periodU, y = v * periodV;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float Cell(int cx, int cy) => Hash.Unit(Hash.Cell((cx % periodU + periodU) % periodU, (cy % periodV + periodV) % periodV, seed));
            return Mathf.Lerp(Mathf.Lerp(Cell(x0, y0), Cell(x0 + 1, y0), fx), Mathf.Lerp(Cell(x0, y0 + 1), Cell(x0 + 1, y0 + 1), fx), fy);
        }

        static double Wrap(double v, double period)
        {
            v %= period;
            return v < 0 ? v + period : v;
        }
    }
}
