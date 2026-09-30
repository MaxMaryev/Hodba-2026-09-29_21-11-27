namespace Hodba.Client.Body
{
    /// <summary>Что походка отдаёт наружу: другим модулям, тени, отладке.</summary>
    public readonly struct GaitState
    {
        /// <summary>0..1 — насколько идёт в полную силу (плавно, не скачком).</summary>
        public readonly float Blend;
        /// <summary>0..1 внутри текущего шага.</summary>
        public readonly float Phase;
        /// <summary>На какой ноге стоит в этом шаге.</summary>
        public readonly bool StanceLeft;
        /// <summary>Шагов в секунду.</summary>
        public readonly float StepFrequency;
        /// <summary>0..1 — сколько осторожности просит земля под последней стопой.</summary>
        public readonly float GroundCaution;
        /// <summary>0..1 — насколько земля впереди другая.</summary>
        public readonly float AheadChange;
        /// <summary>Наклон корпуса, ° (плюс — вперёд).</summary>
        public readonly float Lean;
        /// <summary>0..1 — сила текущего события (камень, спотыкание, остановка).</summary>
        public readonly float EventStrength;

        public GaitState(float blend, float phase, bool stanceLeft, float stepFrequency, float groundCaution,
            float aheadChange, float lean, float eventStrength)
        {
            Blend = blend;
            Phase = phase;
            StanceLeft = stanceLeft;
            StepFrequency = stepFrequency;
            GroundCaution = groundCaution;
            AheadChange = aheadChange;
            Lean = lean;
            EventStrength = eventStrength;
        }
    }
}
