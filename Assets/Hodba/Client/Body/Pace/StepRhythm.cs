using UnityEngine;

namespace Hodba.Client.Body
{
    public enum TapResult
    {
        /// <summary>Стоит или только трогается: подгонять некого.</summary>
        Ignored,
        OnBeat,
        /// <summary>Не та нога, середина шага или второй тап на тот же шаг.</summary>
        Miss,
    }

    /// <summary>
    /// Спешка в такт ногам. Ведёт игрок, тело подстраивается: какая нога сейчас, без интерфейса не видно,
    /// поэтому первый тап серии любой рукой — в такт, если нога вот-вот встанет или только что встала, и он
    /// же связывает руку с этой ногой. Дальше — чередовать руки в такт шагам: две подряд одной рукой,
    /// тап посреди шага или второй на тот же шаг — мимо. Несколько тапов подряд разгоняют, перестал — за шаг-другой
    /// человек возвращается к своему темпу, и следующая серия связывает руки заново. Тап мимо такта сбивает шаг.
    /// Сколько из этого тело правда выдаст, решает запас сил в симуляции: здесь только просьба.
    /// </summary>
    public sealed class StepRhythm
    {
        float _drive, _sinceBeat = float.MaxValue;
        long _landings, _claimed = -1;
        bool _stanceLeft, _hasStance;
        /// <summary>Рука в этой серии ведёт противоположную ногу (левая рука — правая нога).</summary>
        bool _crossed;

        /// <summary>0..1 — сколько просит ритм.</summary>
        public float Drive => _drive;

        public void Tick(float dt, in GaitState g, bool walking, in RhythmSettings s)
        {
            if (!_hasStance || g.StanceLeft != _stanceLeft) _landings++;
            _stanceLeft = g.StanceLeft;
            _hasStance = true;

            if (!walking)
            {
                _drive = 0f;
                _sinceBeat = float.MaxValue;
                return;
            }

            float steps = dt * Mathf.Max(0.5f, g.StepFrequency);
            if (_sinceBeat < float.MaxValue) _sinceBeat += steps;
            if (_sinceBeat > s.graceSteps) _drive = Mathf.Max(0f, _drive - steps / Mathf.Max(0.05f, s.fallSteps));
        }

        public TapResult Tap(bool left, in GaitState g, bool walking, in RhythmSettings s)
        {
            if (!walking || g.Blend < 0.5f) return TapResult.Ignored;

            // Новая серия: какая нога в такт — та и становится ногой этой руки.
            if (_sinceBeat > s.graceSteps)
            {
                if (g.Phase >= s.early) _crossed = left == g.StanceLeft; // встанет маховая
                else if (g.Phase <= s.late) _crossed = left != g.StanceLeft; // встала опорная
            }

            bool foot = left != _crossed;
            long landing = -1;
            if (foot != g.StanceLeft && g.Phase >= s.early) landing = _landings + 1; // эта нога вот-вот встанет
            else if (foot == g.StanceLeft && g.Phase <= s.late) landing = _landings; // только что встала

            if (landing < 0 || landing == _claimed)
            {
                _drive *= s.missKeep;
                return TapResult.Miss;
            }

            _claimed = landing;
            _sinceBeat = 0f;
            _drive = Mathf.Min(1f, _drive + 1f / Mathf.Max(1f, s.buildTaps));
            return TapResult.OnBeat;
        }
    }
}
