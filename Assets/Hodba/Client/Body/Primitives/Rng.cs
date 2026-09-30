using System;
using Hodba.Core;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Случайность тела. Своя у каждого модуля (соль из <see cref="Salts"/>), поэтому модули не сбивают
    /// друг другу последовательность. Распределения — не только равномерное: живое чаще всего
    /// логнормально (обычно коротко, изредка очень долго) или пуассоновски.
    /// </summary>
    public sealed class Rng
    {
        uint _s;
        float _spare;
        bool _hasSpare;

        public Rng(uint seed, uint salt)
        {
            _s = Hash.Next(Hash.Mix(seed), salt) | 1u;
        }

        /// <summary>0..1, без единицы.</summary>
        public float Next01()
        {
            _s ^= _s << 13;
            _s ^= _s >> 17;
            _s ^= _s << 5;
            return (_s >> 8) * (1f / 16777216f);
        }

        public float Range(float a, float b) => a + (b - a) * Next01();

        /// <summary>−1..1.</summary>
        public float Signed() => Next01() * 2f - 1f;

        public bool Chance(float p) => Next01() < p;

        /// <summary>Нормальное N(0,1), Бокс — Мюллер.</summary>
        public float Gaussian()
        {
            if (_hasSpare)
            {
                _hasSpare = false;
                return _spare;
            }
            float u = Math.Max(1e-7f, Next01());
            float v = Next01();
            float r = (float)Math.Sqrt(-2.0 * Math.Log(u));
            float a = 2f * (float)Math.PI * v;
            _spare = r * (float)Math.Sin(a);
            _hasSpare = true;
            return r * (float)Math.Cos(a);
        }

        /// <summary>Логнормальное: медиана и разброс σ в логарифме. Тяжёлый хвост — отсюда «залипания».</summary>
        public float LogNormal(float median, float sigma) => median * (float)Math.Exp(sigma * Gaussian());

        /// <summary>Экспоненциальное: интервал между независимыми событиями со средним mean.</summary>
        public float Exponential(float mean) => -mean * (float)Math.Log(Math.Max(1e-7f, 1f - Next01()));
    }
}
