using System.Collections.Generic;
using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Надпись «ХОДЬБА» штрихами, без шрифта: на телефоне нет нужного, а свой тянуть ради шести букв незачем.
    /// Буквы — тонкие линии в клетке 1×1 (y вверх), широко расставленные, как на камне.
    /// Из них собирается поле расстояний до штриха (R8): шейдер из него делает и линию, и ореол, и распад на пыль.
    /// </summary>
    public static class TitleGlyphs
    {
        public const int Width = 1024;
        public const int Height = 192;
        /// <summary>Дальше этого расстояния (в пикселях текстуры) поле не хранится.</summary>
        public const float MaxDistancePx = 24f;
        /// <summary>Полутолщина штриха в долях ширины текстуры.</summary>
        public const float StrokeHalfWidth = 0.0030f;

        const float Gap = 0.55f;
        const float MarginX = 0.4f;

        struct Seg { public Vector2 A, B; }

        public static Texture2D Build()
        {
            var letters = Letters();

            float totalW = 2f * MarginX + Gap * (letters.Count - 1);
            foreach (var l in letters) totalW += l.width;
            float scale = Width / totalW;
            float baseline = (Height / scale - 1f) * 0.5f;

            var segs = new List<Seg>();
            float x = MarginX;
            foreach (var l in letters)
            {
                foreach (var s in l.segs)
                    segs.Add(new Seg { A = ToPx(s.A, x, baseline, scale), B = ToPx(s.B, x, baseline, scale) });
                x += l.width + Gap;
            }

            var data = new byte[Width * Height];
            for (int i = 0; i < data.Length; i++) data[i] = 255;

            int r = Mathf.CeilToInt(MaxDistancePx);
            foreach (var s in segs)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(s.A.x, s.B.x)) - r);
                int x1 = Mathf.Min(Width - 1, Mathf.CeilToInt(Mathf.Max(s.A.x, s.B.x)) + r);
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(s.A.y, s.B.y)) - r);
                int y1 = Mathf.Min(Height - 1, Mathf.CeilToInt(Mathf.Max(s.A.y, s.B.y)) + r);
                var ab = s.B - s.A;
                float len2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
                for (int y = y0; y <= y1; y++)
                for (int px = x0; px <= x1; px++)
                {
                    var p = new Vector2(px + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - s.A, ab) / len2);
                    float d = (p - (s.A + ab * t)).magnitude;
                    byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(d / MaxDistancePx) * 255f);
                    int i = y * Width + px;
                    if (v < data[i]) data[i] = v;
                }
            }

            var tex = new Texture2D(Width, Height, TextureFormat.R8, false, true)
            {
                name = "TitleGlyphs",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixelData(data, 0);
            tex.Apply(false, true);
            return tex;
        }

        static Vector2 ToPx(Vector2 p, float x, float baseline, float scale) =>
            new Vector2((x + p.x) * scale, (baseline + p.y) * scale);

        static List<(float width, List<Seg> segs)> Letters()
        {
            return new List<(float, List<Seg>)>
            {
                // Х
                (0.9f, Lines(P(0, 1), P(0.9f, 0)).Add(P(0, 0), P(0.9f, 1)).Done()),
                // О
                (0.95f, Ellipse(0.475f, 0.5f, 0.475f, 0.5f)),
                // Д: крыша, две ноги, основание с засечками
                (1.05f, Lines(P(0.3f, 1), P(0.95f, 1)).Add(P(0.3f, 1), P(0.13f, 0.2f)).Add(P(0.95f, 1), P(0.95f, 0.2f))
                    .Add(P(0, 0.2f), P(1.05f, 0.2f)).Add(P(0, 0.2f), P(0, 0)).Add(P(1.05f, 0.2f), P(1.05f, 0)).Done()),
                // Ь: стебель, засечка, чаша
                (0.8f, Lines(P(0, 1), P(0, 0)).Add(P(0, 1), P(0.3f, 1))
                    .Poly(P(0, 0.52f), P(0.34f, 0.55f), P(0.6f, 0.5f), P(0.78f, 0.36f), P(0.78f, 0.2f), P(0.6f, 0.05f), P(0.34f, 0), P(0, 0)).Done()),
                // Б
                (0.9f, Lines(P(0, 1), P(0, 0)).Add(P(0, 1), P(0.85f, 1))
                    .Poly(P(0, 0.58f), P(0.4f, 0.62f), P(0.7f, 0.56f), P(0.88f, 0.4f), P(0.88f, 0.22f), P(0.7f, 0.06f), P(0.4f, 0), P(0, 0)).Done()),
                // А
                (1f, Lines(P(0, 0), P(0.5f, 1)).Add(P(0.5f, 1), P(1, 0)).Add(P(0.22f, 0.35f), P(0.78f, 0.35f)).Done()),
            };
        }

        static Vector2 P(float x, float y) => new Vector2(x, y);

        static Builder Lines(Vector2 a, Vector2 b) => new Builder().Add(a, b);

        static List<Seg> Ellipse(float cx, float cy, float rx, float ry)
        {
            const int n = 44;
            var list = new List<Seg>(n);
            var prev = P(cx + rx, cy);
            for (int i = 1; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var next = P(cx + Mathf.Cos(a) * rx, cy + Mathf.Sin(a) * ry);
                list.Add(new Seg { A = prev, B = next });
                prev = next;
            }
            return list;
        }

        sealed class Builder
        {
            readonly List<Seg> _segs = new List<Seg>();

            public Builder Add(Vector2 a, Vector2 b)
            {
                _segs.Add(new Seg { A = a, B = b });
                return this;
            }

            /// <summary>Гладкая ломаная: между опорными точками — сплайн Катмулла–Рома, чтобы чаши Ь и Б не были гранёными.</summary>
            public Builder Poly(params Vector2[] pts)
            {
                const int sub = 6;
                var prev = pts[0];
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    var p0 = pts[Mathf.Max(i - 1, 0)];
                    var p1 = pts[i];
                    var p2 = pts[i + 1];
                    var p3 = pts[Mathf.Min(i + 2, pts.Length - 1)];
                    for (int k = 1; k <= sub; k++)
                    {
                        float t = k / (float)sub;
                        var q = 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t
                                        + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
                        _segs.Add(new Seg { A = prev, B = q });
                        prev = q;
                    }
                }
                return this;
            }

            public List<Seg> Done() => _segs;
        }
    }
}
