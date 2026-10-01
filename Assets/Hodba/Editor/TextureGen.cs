using System.IO;
using UnityEditor;
using UnityEngine;

namespace Hodba.Editor
{
    /// <summary>
    /// Заглушки текстур, пока не пришли настоящие (Docs/Assets/field-assets.md).
    /// Все бесшовные, построены на периодическом шуме.
    /// </summary>
    public static class TextureGen
    {
        public static Texture2D AshDetail(string path) => Write(path, 512, 512, (u, v) =>
        {
            float n = Fbm(u, v, 16, 5, 11);
            float g = 0.5f + (n - 0.5f) * 0.28f;
            float speck = Hash(u * 512f, v * 512f, 3);
            if (speck > 0.985f) g *= 0.55f;          // крупинки угля
            else if (speck < 0.01f) g *= 1.12f;      // светлые зёрна
            return new Color(g, g * 0.995f, g * 0.985f, 1f);
        }, true, false);

        public static Texture2D PackedDetail(string path) => Write(path, 512, 512, (u, v) =>
        {
            float n = Fbm(u, v, 8, 4, 21);
            float crack = Worley(u, v, 7, 5);
            float line = 1f - Mathf.SmoothStep(0f, 0.035f, crack);
            float g = 0.5f + (n - 0.5f) * 0.22f - line * 0.16f;
            return new Color(g, g, g * 0.99f, 1f);
        }, true, false);

        public static Texture2D Macro(string path) => Write(path, 256, 256, (u, v) =>
        {
            float n = Fbm(u, v, 4, 4, 31);
            return new Color(n, n, n, 1f);
        }, false, false);

        /// <summary>Три независимых поля на тайл 128 м: варианты (4 м), оттенок (32–8 м), детали (16 м).</summary>
        public static Texture2D Variation(string path) => Write(path, 512, 512, (u, v) =>
            new Color(Fbm(u, v, 32, 2, 301), Fbm(u, v, 4, 3, 401), Fbm(u, v, 8, 2, 501), 1f), false, false);

        /// <summary>
        /// Пыльные облака для теней на земле: тайл ~2 км, пятна от полукилометра до десятков метров.
        /// Контраст растянут, чтобы порог покрытия в шейдере означал примерно долю неба в облаках.
        /// </summary>
        public static Texture2D DustShadow(string path) => Write(path, 256, 256, (u, v) =>
        {
            float n = Mathf.Clamp01((Fbm(u, v, 4, 5, 601) - 0.5f) * 2.6f + 0.5f);
            return new Color(n, n, n, 1f);
        }, false, false);

        /// <summary>Мелкая неровность пепла: ~2 мм на тайл 2 м.</summary>
        public static Texture2D AshNormal(string path)
        {
            const int size = 512;
            return NormalFromHeight(path, size, 2f, (u, v) => Fbm(u, v, 32, 4, 41) * 0.002f);
        }

        /// <summary>
        /// Рябь от ветра: гребни поперёк оси U (ветер дует вдоль X), шаг ~10 см на тайл 4 м,
        /// пологий наветренный склон и крутой подветренный, с разрывами и слияниями.
        /// </summary>
        public static Texture2D Ripples(string path)
        {
            const int size = 512;
            return NormalFromHeight(path, size, 4f, (u, v) =>
            {
                float warp = (Fbm(u, v, 4, 3, 51) - 0.5f) * 2.2f + (Fbm(u, v, 12, 2, 53) - 0.5f) * 0.5f;
                float phase = u * 40f + warp;
                float p = phase - Mathf.Floor(phase);
                float profile = p < 0.72f ? Mathf.SmoothStep(0f, 1f, p / 0.72f) : 1f - Mathf.SmoothStep(0f, 1f, (p - 0.72f) / 0.28f);
                float amp = Mathf.Lerp(0.25f, 1f, Mathf.SmoothStep(0.3f, 0.7f, Fbm(u, v, 6, 2, 57)));
                return profile * amp * 0.012f;
            });
        }

        /// <summary>Отпечаток левого сапога: 0.5 — нейтраль, вмятина темнее, выдавленный край светлее.</summary>
        public static Texture2D Footprint(string path)
        {
            const int w = 128, h = 256;
            var depth = new float[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                float fore = Ellipse(u, v, 0.52f, 0.68f, 0.36f, 0.25f);
                float heel = Ellipse(u, v, 0.47f, 0.22f, 0.29f, 0.17f);
                float arch = Ellipse(u, v, 0.5f, 0.45f, 0.26f, 0.2f) * 0.8f;
                float m = Mathf.Max(fore, Mathf.Max(heel, arch));
                float tread = 0.5f + 0.5f * Mathf.Sin(v * 90f);
                float grain = Hash(x, y, 7) * 0.15f;
                depth[y * w + x] = m * (0.85f + tread * 0.1f + grain);
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, true);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float d = depth[y * w + x];
                float dx = Sample(depth, w, h, x + 1, y) - Sample(depth, w, h, x - 1, y);
                float dy = Sample(depth, w, h, x, y + 1) - Sample(depth, w, h, x, y - 1);
                float shade = (dx + dy) * 1.2f;                  // свет сверху-слева
                float rim = Mathf.Clamp01(Blur(depth, w, h, x, y, 6) * 1.6f - d * 1.6f);
                float g = 0.5f - d * 0.2f + shade * 0.5f + rim * 0.1f;
                if (x == 0 || y == 0 || x == w - 1 || y == h - 1) g = 0.5f;
                px[y * w + x] = new Color(g, g, g, 1f);
            }
            tex.SetPixels(px);
            tex.Apply();
            return Save(path, tex, false, false, TextureWrapMode.Clamp);
        }

        public static Texture2D SoftDot(string path) => Write(path, 64, 64, (u, v) =>
        {
            float r = new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f;
            float a = Mathf.Pow(Mathf.Clamp01(1f - r), 2f);
            return new Color(1f, 1f, 1f, a);
        }, false, false, TextureWrapMode.Clamp);

        /// <summary>
        /// Позёмка, координаты по ветру (U — вдоль ветра). Бесшовная, линейная.
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
        }, false, false);

        /// <summary>Песчинка в полёте: тонкий штрих с чётким краем и сужением к хвосту, без гауссова «облака».</summary>
        public static Texture2D Grain(string path) => Write(path, 64, 16, (u, v) =>
        {
            float across = Mathf.Abs(v - 0.5f) * 2f;
            float body = 1f - Mathf.SmoothStep(0.45f, 0.75f, across);
            float taper = Mathf.SmoothStep(0f, 0.25f, u) * (1f - Mathf.SmoothStep(0.6f, 1f, u));
            return new Color(1f, 1f, 1f, body * taper);
        }, false, false, TextureWrapMode.Clamp);

        // ——— общее ———

        delegate Color Pixel(float u, float v);
        delegate float Height(float u, float v);

        static Texture2D Write(string path, int w, int h, Pixel f, bool srgb, bool normal, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, !srgb);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = f((x + 0.5f) / w, (y + 0.5f) / h);
            tex.SetPixels(px);
            tex.Apply();
            return Save(path, tex, srgb, normal, wrap);
        }

        static Texture2D NormalFromHeight(string path, int size, float tileMeters, Height height)
        {
            var hmap = new float[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                hmap[y * size + x] = height((x + 0.5f) / size, (y + 0.5f) / size);

            float pixel = tileMeters / size;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (Wrap(hmap, size, x + 1, y) - Wrap(hmap, size, x - 1, y)) / (2f * pixel);
                float dy = (Wrap(hmap, size, x, y + 1) - Wrap(hmap, size, x, y - 1)) / (2f * pixel);
                var n = new Vector3(-dx, -dy, 1f).normalized;
                px[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
            tex.SetPixels(px);
            tex.Apply();
            return Save(path, tex, false, true, TextureWrapMode.Repeat);
        }

        static Texture2D Save(string path, Texture2D tex, bool srgb, bool normal, TextureWrapMode wrap)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = srgb;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = !normal && !srgb && wrap == TextureWrapMode.Clamp;
            imp.wrapMode = wrap;
            imp.mipmapEnabled = true;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 4;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static float Wrap(float[] a, int size, int x, int y)
        {
            x = (x % size + size) % size;
            y = (y % size + size) % size;
            return a[y * size + x];
        }

        static float Sample(float[] a, int w, int h, int x, int y) =>
            a[Mathf.Clamp(y, 0, h - 1) * w + Mathf.Clamp(x, 0, w - 1)];

        static float Blur(float[] a, int w, int h, int x, int y, int r)
        {
            float s = 0f;
            int n = 0;
            for (int dy = -r; dy <= r; dy += 2)
            for (int dx = -r; dx <= r; dx += 2)
            {
                s += Sample(a, w, h, x + dx, y + dy);
                n++;
            }
            return s / n;
        }

        static float Ellipse(float u, float v, float cx, float cy, float rx, float ry)
        {
            float x = (u - cx) / rx, y = (v - cy) / ry;
            float r = Mathf.Sqrt(x * x + y * y);
            return 1f - Mathf.SmoothStep(0.75f, 1f, r);
        }

        static float Hash(float x, float y, int seed)
        {
            uint h = (uint)(Mathf.FloorToInt(x) * 73856093) ^ (uint)(Mathf.FloorToInt(y) * 19349663) ^ (uint)(seed * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xffffff) / 16777216f;
        }

        /// <summary>Периодический шум значений с разным числом клеток по осям: вытянутые пятна.</summary>
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

        /// <summary>Периодический шум значений: period клеток на тайл.</summary>
        static float TileNoise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Lattice(x0, y0, period, seed), b = Lattice(x0 + 1, y0, period, seed);
            float c = Lattice(x0, y0 + 1, period, seed), d = Lattice(x0 + 1, y0 + 1, period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Lattice(int x, int y, int period, int seed)
        {
            x = (x % period + period) % period;
            y = (y % period + period) % period;
            return Hash(x, y, seed);
        }

        static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += TileNoise(u, v, period, seed + i * 17) * amp;
                norm += amp;
                amp *= 0.5f;
                period *= 2;
            }
            return sum / norm;
        }

        /// <summary>Периодический клеточный шум: F2 − F1 (линии между клетками — трещины корки).</summary>
        static float Worley(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            float f1 = 9f, f2 = 9f;
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int gx = cx + i, gy = cy + j;
                int wx = (gx % period + period) % period, wy = (gy % period + period) % period;
                float px = gx + Hash(wx, wy, seed), py = gy + Hash(wx, wy, seed + 1);
                float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < f1) { f2 = f1; f1 = d; }
                else if (d < f2) f2 = d;
            }
            return f2 - f1;
        }
    }
}
