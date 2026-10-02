using System.IO;
using UnityEditor;
using UnityEngine;

namespace Hodba.Editor
{
    /// <summary>
    /// Текстуры-данные, которые генерирует сама игра: поля шума для вариаций земли, облаков и позёмки, формы частиц.
    /// Арт (земля, камни, путник, следы) приходит из арт-пака, здесь его нет. Все линейные; тайлящиеся — бесшовные.
    /// </summary>
    public static class TextureGen
    {
        /// <summary>Крупные пятна земли: тайл 512 м.</summary>
        public static Texture2D Macro(string path) => Write(path, 256, 256, (u, v) =>
        {
            float n = Fbm(u, v, 4, 4, 31);
            return new Color(n, n, n, 1f);
        });

        const float RippleTileM = 64f, RippleWarpM = 0.12f, RippleWarpSlope = 0.6f; // как в HodbaRipple.hlsl

        /// <summary>
        /// Мелкая рябь: тайл 64 м, U — по ветру (мировая x). R — изгиб гребней, ±0,12 м (клетка 2 м);
        /// G, B — его наклон по x и z, ±0,6: шейдер берёт градиент фазы готовым, билинейка наклона непрерывна —
        /// гребни не ломаются на текселях; A — сила ряби по месту (пятна ~4 м). Обрывы гребней — хэш их номеров в шейдере.
        /// Без сжатия: блоки сжатия ломали бы гребни сеткой.
        /// </summary>
        public static Texture2D RippleNoise(string path) => Write(path, 512, 512, (u, v) =>
        {
            const float e = 0.5f / 512f;
            float dx = (RippleWarp(u + e, v) - RippleWarp(u - e, v)) / (2f * e * RippleTileM);
            float dz = (RippleWarp(u, v + e) - RippleWarp(u, v - e)) / (2f * e * RippleTileM);
            float strength = Mathf.Lerp(0.4f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.7f, Fbm(u, v, 16, 2, 821))));
            return new Color(
                0.5f + 0.5f * Mathf.Clamp(RippleWarp(u, v) / RippleWarpM, -1f, 1f),
                0.5f + 0.5f * Mathf.Clamp(dx / RippleWarpSlope, -1f, 1f),
                0.5f + 0.5f * Mathf.Clamp(dz / RippleWarpSlope, -1f, 1f),
                strength);
        }, TextureWrapMode.Repeat, TextureImporterCompression.Uncompressed);

        /// <summary>Изгиб гребней мелкой ряби, м.</summary>
        static float RippleWarp(float u, float v) => (Fbm(u, v, 32, 2, 811) - 0.5f) * 0.4f;

        /// <summary>Три независимых поля на тайл 128 м: варианты (4 м), оттенок (32–8 м), детали (16 м).</summary>
        public static Texture2D Variation(string path) => Write(path, 512, 512, (u, v) =>
            new Color(Fbm(u, v, 32, 2, 301), Fbm(u, v, 4, 3, 401), Fbm(u, v, 8, 2, 501), 1f));

        /// <summary>
        /// Пыльные облака для теней на земле: тайл ~2 км, пятна от полукилометра до десятков метров.
        /// Контраст растянут, чтобы порог покрытия в шейдере означал примерно долю неба в облаках.
        /// </summary>
        public static Texture2D DustShadow(string path) => Write(path, 256, 256, (u, v) =>
        {
            float n = Mathf.Clamp01((Fbm(u, v, 4, 5, 601) - 0.5f) * 2.6f + 0.5f);
            return new Color(n, n, n, 1f);
        });

        /// <summary>
        /// Песок на ветру, координаты по ветру (U — вдоль ветра).
        /// R — мелкая зернистая позёмка; G — где поток гуще, а где его рвёт;
        /// A — языки взвеси: мягкий шум, вытянутый вдоль ветра 4:1, без гребней. Фронты порыва — у ветра, не здесь.
        /// </summary>
        public static Texture2D Saltation(string path) => Write(path, 256, 256, (u, v) =>
        {
            // Короткие разрозненные пятна: движение задаёт сдвиг по ветру, а не длинные полосы в текстуре.
            float specks = TileNoise(u, v, 96, 128, 711);
            float threads = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.9f, specks));
            float breakup = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Fbm(u, v, 6, 3, 721) - 0.38f) / 0.3f));
            float tongues = Mathf.Clamp01((StretchedFbm(u, v, 4, 16, 4, 741) - 0.5f) * 2.2f + 0.5f);
            return new Color(threads, breakup, 0f, tongues);
        });

        /// <summary>Пылинка: мягкая точка.</summary>
        public static Texture2D SoftDot(string path) => Write(path, 64, 64, (u, v) =>
        {
            float r = new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f;
            float a = Mathf.Pow(Mathf.Clamp01(1f - r), 2f);
            return new Color(1f, 1f, 1f, a);
        }, TextureWrapMode.Clamp);

        /// <summary>Компактное зерно с узким мягким краем, без длинного хвоста.</summary>
        public static Texture2D Grain(string path) => Write(path, 32, 32, (u, v) =>
        {
            float r = new Vector2((u - 0.5f) * 2f, (v - 0.5f) * 2.4f).magnitude;
            return new Color(1f, 1f, 1f, 1f - Mathf.SmoothStep(0.45f, 0.95f, r));
        }, TextureWrapMode.Clamp);

        // ——— общее ———

        delegate Color Pixel(float u, float v);

        /// <summary>Линейная текстура с мипами. Repeat — поле шума, Clamp — спрайт частицы (альфа — прозрачность).</summary>
        static Texture2D Write(string path, int w, int h, Pixel f, TextureWrapMode wrap = TextureWrapMode.Repeat,
            TextureImporterCompression compression = TextureImporterCompression.CompressedHQ)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, true);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = f((x + 0.5f) / w, (y + 0.5f) / h);
            tex.SetPixels(px);
            tex.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = false;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = wrap == TextureWrapMode.Clamp;
            imp.wrapMode = wrap;
            imp.mipmapEnabled = true;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 4;
            imp.textureCompression = compression;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static float Hash(float x, float y, int seed)
        {
            uint h = (uint)(Mathf.FloorToInt(x) * 73856093) ^ (uint)(Mathf.FloorToInt(y) * 19349663) ^ (uint)(seed * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xffffff) / 16777216f;
        }

        /// <summary>Периодический шум значений: periodU × periodV клеток на тайл (разные — вытянутые пятна).</summary>
        static float TileNoise(float u, float v, int periodU, int periodV, int seed)
        {
            float x = u * periodU, y = v * periodV;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float Cell(int cx, int cy) => Hash((cx % periodU + periodU) % periodU, (cy % periodV + periodV) % periodV, seed);
            float a = Cell(x0, y0), b = Cell(x0 + 1, y0), c = Cell(x0, y0 + 1), d = Cell(x0 + 1, y0 + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int period, int octaves, int seed) =>
            StretchedFbm(u, v, period, period, octaves, seed);

        /// <summary>Fbm с разным числом клеток по осям: пятна вытянуты вдоль оси с меньшим числом клеток.</summary>
        static float StretchedFbm(float u, float v, int periodU, int periodV, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += TileNoise(u, v, periodU, periodV, seed + i * 17) * amp;
                norm += amp;
                amp *= 0.5f;
                periodU *= 2;
                periodV *= 2;
            }
            return sum / norm;
        }
    }
}
