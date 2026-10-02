using System;

namespace Hodba.Sim.Walk
{
    /// <summary>
    /// Свой темп, запас сил и спешка. Запас сил — минуты: тратится спешкой и подъёмом, возвращается на ровном,
    /// на спуске и стоя. Дневная усталость (часы пути) — потом, в Hodba.Sim.Body. Все числа стартовые, подбираются.
    /// </summary>
    [Serializable]
    public struct PaceParams
    {
        /// <summary>Усилие ровного шага в полную силу, 0..1. Растёт как квадрат скорости: спешка дорогая.</summary>
        public float BaseEffort;
        /// <summary>Сколько добавляет подъём, на единицу уклона. Спуск не добавляет ничего.</summary>
        public float UphillEffort;
        /// <summary>Сколько добавляет рыхлость при полной рыхлости.</summary>
        public float LooseEffort;
        /// <summary>Сколько добавляют бугры и наносы в полную силу.</summary>
        public float RoughEffort;
        /// <summary>Сколько добавляет разгон, на м/с².</summary>
        public float AccelEffort;

        /// <summary>Усилие, которое тело держит без убыли: ниже него запас восстанавливается.</summary>
        public float Sustainable;
        /// <summary>Как быстро тратится запас: за столько секунд уходит единица при превышении усилия на 1.</summary>
        public float SpendTime;
        /// <summary>За столько секунд стоя запас возвращается целиком.</summary>
        public float RecoverTime;
        /// <summary>С какой усталости человек идёт медленнее обычного (откат).</summary>
        public float DebtFrom;
        /// <summary>Насколько медленнее при полной усталости, доля.</summary>
        public float DebtSlow;

        /// <summary>Насколько спешка в полную силу прибавляет к скорости, доля.</summary>
        public float HurryGain;
        /// <summary>С какой усталости спешка начинает слабеть.</summary>
        public float HurryFadeFrom;
        /// <summary>С какой усталости спешки нет совсем: сколько ни подгоняй, быстрее не идёт.</summary>
        public float HurryFadeTo;
        /// <summary>Как быстро тело откликается на спешку, с.</summary>
        public float HasteRise;
        /// <summary>Как быстро отпускает, с.</summary>
        public float HasteFall;

        /// <summary>Свой темп плавает по пути на столько, доля (±).</summary>
        public float DriftAmount;
        /// <summary>Размер самой крупной волны дрейфа, м пути.</summary>
        public float DriftCellM;

        public static PaceParams Default => new PaceParams
        {
            BaseEffort = 0.3f,
            UphillEffort = 5f,
            LooseEffort = 0.35f,
            RoughEffort = 0.2f,
            AccelEffort = 0.25f,

            Sustainable = 0.42f,
            SpendTime = 120f,
            RecoverTime = 240f,
            DebtFrom = 0.45f,
            DebtSlow = 0.35f,

            HurryGain = 0.6f,
            HurryFadeFrom = 0.5f,
            HurryFadeTo = 0.85f,
            HasteRise = 1.5f,
            HasteFall = 2f,

            DriftAmount = 0.04f,
            DriftCellM = 60f,
        };
    }
}
