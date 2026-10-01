using Hodba.Sim.Walk;
using Hodba.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hodba.Client
{
    /// <summary>
    /// Тело, которого не видно — только его тень (модель М3). На рассвете она тянется на десятки метров вперёд.
    /// </summary>
    public sealed class ShadowBody
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");

        readonly Transform _root;
        readonly Animator _animator;
        readonly bool _hasSpeed;

        public ShadowBody(FieldConfig config)
        {
            if (config.walkerPrefab == null)
            {
                // Модель М3 лежит в репозитории: её отсутствие — сломанная сборка ассетов, а не повод для замены.
                Debug.LogError("Hodba: в FieldConfig нет префаба путника — запусти Hodba ▸ Refresh Field Assets.");
                _root = new GameObject("Walker (shadow)").transform;
                return;
            }

            var go = Object.Instantiate(config.walkerPrefab);
            go.name = "Walker (shadow)";
            _root = go.transform;
            _animator = go.GetComponentInChildren<Animator>();
            if (_animator != null)
                foreach (var p in _animator.parameters)
                    if (p.nameHash == SpeedId && p.type == AnimatorControllerParameterType.Float) _hasSpeed = true;

            foreach (var r in _root.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                r.receiveShadows = false;
            }
        }

        /// <param name="bob">Вертикаль головы, м.</param>
        /// <param name="lean">Наклон корпуса, ° (плюс — вперёд).</param>
        /// <param name="support">Высота опоры, м.</param>
        public void Tick(WalkSim sim, float support, FloatingOrigin origin, float bob, float lean)
        {
            // Корпус клонится вслед за телом: вперёд на подъёме и при трогании, назад на спуске.
            _root.SetPositionAndRotation(
                origin.ToLocal(sim.Position, support + bob * 0.6f),
                Quaternion.Euler(0f, sim.Course, 0f) * Quaternion.Euler(lean, 0f, 0f));

            if (_animator == null) return;
            if (_hasSpeed) _animator.SetFloat(SpeedId, sim.Speed);
            // Шаг анимации под настоящую скорость: клип ходьбы записан на ~1,3 м/с.
            float walk = sim.Params.BaseSpeed > 0f ? sim.Speed / sim.Params.BaseSpeed : 0f;
            _animator.speed = walk > 0.1f ? Mathf.Max(0.3f, walk) : 1f;
        }
    }
}
