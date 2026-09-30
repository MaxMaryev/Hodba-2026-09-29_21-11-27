namespace Hodba.Client.Body
{
    /// <summary>
    /// Будильник для редких событий: моргание, смена точки внимания, поправка лямки.
    /// Интервал задаёт владелец (обычно логнормальный из <see cref="Rng"/>), часы только отсчитывают.
    /// Даже в длинном кадре срабатывают не больше одного раза — пачки событий не бывает.
    /// </summary>
    public sealed class EventClock
    {
        float _left;

        public float Remaining => _left;

        public void Schedule(float seconds) => _left = seconds;

        /// <param name="rate">Во сколько раз быстрее идёт время (ветер учащает моргание).</param>
        public bool Tick(float dt, float rate = 1f)
        {
            _left -= dt * (rate < 0f ? 0f : rate);
            if (_left > 0f) return false;
            _left = float.MaxValue; // владелец обязан перезавести
            return true;
        }
    }
}
