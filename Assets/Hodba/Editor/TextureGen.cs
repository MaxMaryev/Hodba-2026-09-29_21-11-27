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
        /// Позёмка, координаты по ветру (U — вдоль ветра).
        /// R — нити: тонкие гребни шума, вытянутые вдоль ветра ~8:1; G — где струи есть, а где их рвёт;
        /// B — крупные пятна для фронтов порыва (шейдер берёт их на своём, гораздо большем тайле).
        /// </summary>
        public static Texture2D Saltation(string path) => Write(path, 256, 256, (u, v) =>
        {
            // Небольшое искривление поперёк — нити сплетаются, а не идут по линейке.
            float bend = (Fbm(u, v, 4, 2, 701) - 0.5f) * 0.08f;
            float n1 = TileNoise(u, v + bend, 4, 32, 711), n2 = TileNoise(u, v + bend * 1.7f, 8, 64, 713);
            float ridge = Mathf.Pow(1f - Mathf.Abs(2f * n1 - 1f), 6f) * 0.7f + Mathf.Pow(1f - Mathf.Abs(2f * n2 - 1f), 6f) * 0.45f;
            float threads = Mathf.Clamp01(ridge * (0.6f + 0.8f * TileNoise(u, v, 16, 8, 717)));
            float breakup = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Fbm(u, v, 6, 3, 721) - 0.38f) / 0.3f));
            float front = Mathf.Clamp01((Fbm(u, v, 4, 3, 731) - 0.5f) * 2.4f + 0.5f);
            return new Color(threads, breakup, front, 1f);
        });

        /// <summary>Пылинка: мягкая точка.</summary>
        public static Texture2D SoftDot(string path) => Write(path, 64, 64, (u, v) =>
        {
            float r = new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f;
            float a = Mathf.Pow(Mathf.Clamp01(1f - r), 2f);
            return new Color(1f, 1f, 1f, a);
        }, TextureWrapMode.Clamp);

        /// <summary>Песчинка в полёте: тонкий штрих с чётким краем и сужением к хвосту, без гауссова «облака».</summary>
        public static Texture2D Grain(string path) => Write(path, 64, 16, (u, v) =>
        {
            float across = Mathf.Abs(v - 0.5f) * 2f;
            float body = 1f - Mathf.SmoothStep(0.45f, 0.75f, across);
            float taper = Mathf.SmoothStep(0f, 0.25f, u) * (1f - Mathf.SmoothStep(0.6f, 1f, u));
            return new Color(1f, 1f, 1f, body * taper);
        }, TextureWrapMode.Clamp);

        // ——— общее ———

        delegate Color Pixel(float u, float v);

        /// <summary>Линейная текстура с мипами. Repeat — поле шума, Clamp — спрайт частицы (альфа — прозрачность).</summary>
        static Texture2D Write(string path, int w, int h, Pixel f, TextureWrapMode wrap = TextureWrapMode.Repeat)
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
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
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

        static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += TileNoise(u, v, period, period, seed + i * 17) * amp;
                norm += amp;
                amp *= 0.5f;
                period *= 2;
            }
            return sum / norm;
        }
    }
}
