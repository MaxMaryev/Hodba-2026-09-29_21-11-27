using System;
using Hodba.Core;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Шум 1/f: октавы сглаженного шума значений с равной мощностью на октаву.
    /// У живой походки интервалы шагов коррелированы на долгих временах (Хаусдорф) — белый шум
    /// на каждый шаг выглядит как механизм с люфтом, а розовый — как человек, у которого «то так, то эдак».
    /// Выход — примерно N(0, 0.45²): редко выходит за ±1.
    /// </summary>
    public sealed class PinkNoise
    {
        readonly uint _seed;
        readonly double _lowest;
        readonly int _octaves;
        readonly float _norm;

        /// <param name="lowestHz">Самая медленная октава, Гц. Каждая следующая вдвое быстрее.</param>
        public PinkNoise(uint seed, uint salt, float lowestHz, int octaves)
        {
            _seed = Hash.Next(Hash.Mix(seed), salt);
            _lowest = lowestHz;
            _octaves = Math.Max(1, octaves);
            _norm = 1f / (float)Math.Sqrt(_octaves);
        }

        public float Sample(double t)
        {
            float sum = 0f;
            double f = _lowest;
            for (int i = 0; i < _octaves; i++)
            {
                sum += Value(t * f, (uint)i);
                f *= 2.0;
            }
            return sum * _norm;
        }

        /// <summary>Шум значений в одной октаве, −1..1, гладкий до второй производной.</summary>
        float Value(double x, uint octave)
        {
            double cell = Math.Floor(x);
            float frac = (float)(x - cell);
            long c = (long)cell;
            float a = Hash.Unit(Hash.Cell(c, octave, _seed)) * 2f - 1f;
            float b = Hash.Unit(Hash.Cell(c + 1, octave, _seed)) * 2f - 1f;
            float s = frac * frac * frac * (frac * (frac * 6f - 15f) + 10f);
            return a + (b - a) * s;
        }
    }
}
