using Hodba.Core;

namespace Hodba.Client.Body
{
    /// <summary>
    /// Подпись человека: постоянные, по которым его походку узнаёшь. У каждого своя — из seed,
    /// поэтому два путника ходят по-разному, а один и тот же — всегда узнаваемо сам собой.
    /// Разбросы маленькие: это не хромота, а характер.
    /// </summary>
    public readonly struct Signature
    {
        /// <summary>−1..1: правая нога чуть длиннее и сильнее (плюс) или левая (минус).</summary>
        public readonly float LegAsymmetry;
        /// <summary>Привычный завал головы, °.</summary>
        public readonly float HeadTilt;
        /// <summary>Множитель вертикали: «прыгучая» или «стелющаяся» походка.</summary>
        public readonly float Bounce;
        /// <summary>Множитель раскачки в стороны.</summary>
        public readonly float Sway;
        /// <summary>Множитель длины шага.</summary>
        public readonly float Stride;
        /// <summary>Где в шаге верхняя точка: −1 раньше, 1 позже.</summary>
        public readonly float PeakBias;
        /// <summary>Множитель темпа дыхания.</summary>
        public readonly float BreathRate;
        /// <summary>Множитель темпа моргания.</summary>
        public readonly float BlinkRate;
        /// <summary>0..1: насколько беспокойный взгляд.</summary>
        public readonly float Restlessness;

        Signature(uint seed)
        {
            uint h = Hash.Next(Hash.Mix(seed), Salts.Signature);
            float U(uint k) => Hash.Unit(Hash.Next(h, k));
            float S(uint k) => U(k) * 2f - 1f;

            LegAsymmetry = S(1);
            HeadTilt = S(2) * 0.6f;
            Bounce = 1f + S(3) * 0.15f;
            Sway = 1f + S(4) * 0.2f;
            Stride = 1f + S(5) * 0.04f;
            PeakBias = S(6);
            BreathRate = 1f + S(7) * 0.12f;
            BlinkRate = 1f + S(8) * 0.25f;
            Restlessness = U(9);
        }

        public static Signature From(uint seed) => new Signature(seed);
    }
}
