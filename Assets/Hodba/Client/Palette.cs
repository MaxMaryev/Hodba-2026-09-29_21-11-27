using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Стартовая палитра пепельного мира. Ключи градиентов — высота солнца:
    /// −20° ночь · −8° сумерки · −2° предрассвет · 3° восход · 10° золото · 25° утро · 45° день · 70° полдень.
    /// Небо выцветшее, горизонт тонет в пыльной дымке, полдень почти белый.
    /// </summary>
    public static class Palette
    {
        static readonly float[] Elevations = { -20f, -8f, -2f, 3f, 10f, 25f, 45f, 70f };

        public static Gradient SkyZenith() => Make(0x03050a, 0x0d1424, 0x2a3550, 0x4d5d7a, 0x6f819a, 0x8a9db2, 0x9aabb9, 0xa7b3bb);
        public static Gradient SkyHorizon() => Make(0x080b12, 0x2a2a36, 0x8a6e66, 0xd8a483, 0xe2c7a6, 0xd9d4c9, 0xe3dfd6, 0xebe7df);
        public static Gradient Fog() => Make(0x070a10, 0x1e1f28, 0x6e5a55, 0xc49a80, 0xd4bca0, 0xcfc8bc, 0xd9d4ca, 0xe0dcd3);
        public static Gradient Sun() => Make(0x000000, 0x000000, 0xff7a40, 0xff9a55, 0xffc890, 0xfff0dc, 0xfff6e8, 0xfffaf2);
        public static Gradient AmbientSky() => Make(0x0b1220, 0x1a2336, 0x3a4460, 0x5d6a86, 0x7a8aa2, 0x8e9db0, 0x9aa7b4, 0xa3adb6);
        public static Gradient AmbientEquator() => Make(0x070a10, 0x16161d, 0x4a3c3a, 0x8a7060, 0xa8987f, 0xb9b3a8, 0xc4bfb5, 0xcdc8bf);
        public static Gradient AmbientGround() => Make(0x040506, 0x0a0a0c, 0x1c1918, 0x3a3230, 0x4e4640, 0x5e5850, 0x66605a, 0x6e6862);

        public static AnimationCurve SunIntensity() => Curve((-3f, 0f), (0f, 0.25f), (5f, 0.9f), (15f, 1.8f), (35f, 2.6f), (70f, 3.1f));
        public static AnimationCurve FogDensity() => Curve((-20f, 0.0009f), (-2f, 0.001f), (8f, 0.0011f), (30f, 0.00085f), (70f, 0.0008f));
        public static AnimationCurve Exposure() => Curve((-20f, 0.3f), (-6f, 0.3f), (0f, 0.2f), (10f, 0f), (35f, -0.1f), (70f, -0.05f));

        /// <summary>Высота солнца, °, → позиция на градиенте.</summary>
        public static float ToGradientTime(float elevation, float min, float max) =>
            Mathf.InverseLerp(min, max, elevation);

        static Gradient Make(params int[] hex)
        {
            var colors = new GradientColorKey[hex.Length];
            for (int i = 0; i < hex.Length; i++)
            {
                float t = Mathf.InverseLerp(Elevations[0], Elevations[Elevations.Length - 1], Elevations[i]);
                colors[i] = new GradientColorKey(FromHex(hex[i]), t);
            }
            var g = new Gradient { mode = GradientMode.Blend };
            g.SetKeys(colors, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        static AnimationCurve Curve(params (float t, float v)[] keys)
        {
            var c = new AnimationCurve();
            foreach (var (t, v) in keys) c.AddKey(t, v);
            for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
            return c;
        }

        static Color FromHex(int hex) =>
            new Color(((hex >> 16) & 0xff) / 255f, ((hex >> 8) & 0xff) / 255f, (hex & 0xff) / 255f, 1f);
    }
}
