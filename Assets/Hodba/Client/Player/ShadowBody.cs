using Hodba.Sim.Walk;
using Hodba.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hodba.Client
{
    /// <summary>
    /// Тело, которого не видно — только его тень. На рассвете она тянется на десятки метров вперёд.
    /// Пока нет модели М3, тень отбрасывает простой силуэт: плащ, голова, рюкзак.
    /// </summary>
    public sealed class ShadowBody
    {
        static readonly int SpeedId = Animator.StringToHash("Speed");

        readonly Transform _root;
        readonly Animator _animator;
        readonly bool _hasSpeed;

        public ShadowBody(FieldConfig config, Material fallbackMaterial)
        {
            if (config.walkerPrefab != null)
            {
                var go = Object.Instantiate(config.walkerPrefab);
                go.name = "Walker (shadow)";
                _root = go.transform;
                _animator = go.GetComponentInChildren<Animator>();
                if (_animator != null)
                    foreach (var p in _animator.parameters)
                        if (p.nameHash == SpeedId && p.type == AnimatorControllerParameterType.Float) _hasSpeed = true;
            }
            else
            {
                _root = new GameObject("Walker (shadow)").transform;
                Part(PrimitiveType.Capsule, new Vector3(0f, 0.78f, 0f), new Vector3(0.5f, 0.78f, 0.36f), fallbackMaterial); // плащ
                Part(PrimitiveType.Sphere, new Vector3(0f, 1.6f, 0.03f), new Vector3(0.24f, 0.28f, 0.26f), fallbackMaterial); // капюшон
                Part(PrimitiveType.Cube, new Vector3(0f, 1.15f, -0.24f), new Vector3(0.36f, 0.5f, 0.22f), fallbackMaterial);  // рюкзак
            }

            foreach (var r in _root.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                r.receiveShadows = false;
            }
        }

        public void Tick(WalkSim sim, IWorldQuery world, FloatingOrigin origin, float bob)
        {
            float ground = world.SampleHeightMm(sim.Position) / 1000f;
            _root.SetPositionAndRotation(
                origin.ToLocal(sim.Position, ground + bob * 0.6f),
                Quaternion.Euler(0f, sim.Course, 0f));

            if (_animator == null) return;
            if (_hasSpeed) _animator.SetFloat(SpeedId, sim.Speed);
            // Шаг анимации под настоящую скорость: клип ходьбы записан на ~1,3 м/с.
            float walk = sim.Params.BaseSpeed > 0f ? sim.Speed / sim.Params.BaseSpeed : 0f;
            _animator.speed = walk > 0.1f ? Mathf.Max(0.3f, walk) : 1f;
        }

        void Part(PrimitiveType type, Vector3 pos, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(_root, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var mr = go.GetComponent<MeshRenderer>();
            if (material != null) mr.sharedMaterial = material;
        }
    }
}
