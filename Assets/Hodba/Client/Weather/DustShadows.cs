using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Тени высоких пыльных облаков на земле, камнях, следах и пыли: шум плывёт по ветру, шейдеры проецируют его вдоль солнца.
    /// Сдвиг считается в double и сворачивается по тайлу — часы игры не съедают точность.
    /// </summary>
    public sealed class DustShadows
    {
        static readonly int ParamsId = Shader.PropertyToID("_HodbaDustShadow");
        static readonly int ShapeId = Shader.PropertyToID("_HodbaDustShadowShape");
        static readonly int TexId = Shader.PropertyToID("_HodbaDustShadowTex");

        double _x, _z;

        public void Tick(Wind wind, FieldConfig config, float dt)
        {
            float tile = Tile(config);
            _x = Wrap(_x - wind.Velocity.x * config.dustShadowSpeed * dt, tile);
            _z = Wrap(_z - wind.Velocity.z * config.dustShadowSpeed * dt, tile);
            Push(config, new Vector2((float)_x, (float)_z));
        }

        /// <summary>Поставить глобальные значения для шейдеров; offset — сдвиг облаков по ветру, м.</summary>
        public static void Push(FieldConfig config, Vector2 offset)
        {
            bool on = config.dustShadowTexture != null && config.dustShadowStrength > 0f;
            if (on) Shader.SetGlobalTexture(TexId, config.dustShadowTexture);
            Shader.SetGlobalVector(ParamsId, new Vector4(offset.x, offset.y, 1f / Tile(config), on ? config.dustShadowStrength : 0f));
            Shader.SetGlobalVector(ShapeId, new Vector4(config.dustShadowHeight, config.dustShadowCoverage, config.dustShadowSoftness, 0f));
        }

        /// <summary>Степень двойки от 64 до 4096 м — делит 4096, тени не прыгают при переносе центра.</summary>
        static float Tile(FieldConfig config) =>
            Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(1, Mathf.RoundToInt(config.dustShadowTile))), 64, 4096);

        static double Wrap(double v, double period)
        {
            v %= period;
            return v < 0 ? v + period : v;
        }
    }
}
