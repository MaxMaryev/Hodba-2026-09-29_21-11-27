using System;
using Hodba.Core;
using Hodba.Sim.Walk;
using Hodba.World;
using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Глаза человека. Покачивание не отключается (закон 6), но взгляд его гасит так, как это делает живой человек:
    /// голова качается, а точка в десяти метрах впереди почти стоит. Так реалистичнее и меньше укачивает.
    /// </summary>
    public sealed class FirstPersonRig
    {
        public readonly Camera Camera;

        /// <summary>Шаг: левая ли нога, где ступня.</summary>
        public event Action<bool, WorldPos> Step;

        /// <summary>Вертикальное покачивание в этом кадре, м (для тени).</summary>
        public float Bob { get; private set; }

        long _lastStep;
        float _gait; // 0 — стоит, 1 — идёт в полную силу
        float _stepScale = 1f;

        public FirstPersonRig(Camera camera, WalkSim sim)
        {
            Camera = camera;
            _lastStep = sim.Steps;
        }

        public void ResetSteps(WalkSim sim) => _lastStep = sim.Steps;

        public void Tick(WalkSim sim, GazeController gaze, IWorldQuery world, FloatingOrigin origin, FieldConfig config, float dt)
        {
            float speedNorm = sim.Params.BaseSpeed > 0f ? Mathf.Clamp01(sim.Speed / sim.Params.BaseSpeed) : 0f;
            _gait = Mathf.MoveTowards(_gait, speedNorm, dt * 2f);

            // Фаза: один шаг — полпериода, пара шагов — полный.
            double phase = sim.Distance / Math.Max(0.01, sim.Params.StepLength) * Math.PI;
            float s = (float)(phase % (Math.PI * 2.0));

            long steps = sim.Steps;
            if (steps != _lastStep)
            {
                _lastStep = steps;
                _stepScale = 1f + (Hash.Unit(Hash.Cell(steps, 0, 77u)) - 0.5f) * 2f * config.stepIrregularity;
                bool left = (steps & 1) == 0;
                Step?.Invoke(left, FootPosition(sim, left, config));
            }

            float vertical = -config.bobVertical * 0.5f * Mathf.Cos(2f * s) * _gait * _stepScale;
            float lateral = config.bobLateral * 0.5f * Mathf.Sin(s) * _gait;
            float roll = config.bobRoll * Mathf.Sin(s) * _gait - sim.TurnRate * config.turnLean;

            float breath = config.breathAmplitude * Mathf.Sin(Time.time * Mathf.PI * 2f * config.breathRate) * (1f - _gait);
            vertical += breath;
            Bob = vertical;

            float ground = world.SampleHeightMm(sim.Position) / 1000f;
            float courseRad = sim.Course * Mathf.Deg2Rad;
            var right = new Vector3(Mathf.Cos(courseRad), 0f, -Mathf.Sin(courseRad));

            var pos = origin.ToLocal(sim.Position, ground + config.eyeHeight + vertical) + right * lateral;

            // Гашение тряски взглядом.
            float stab = config.gazeStabilization;
            float dist = Mathf.Max(1f, config.stabilizationDistance);
            float pitch = gaze.Pitch + Mathf.Atan2(vertical, dist) * Mathf.Rad2Deg * stab;
            float yaw = gaze.Yaw - Mathf.Atan2(lateral, dist) * Mathf.Rad2Deg * stab;

            var t = Camera.transform;
            t.SetPositionAndRotation(pos, Quaternion.Euler(pitch, yaw, roll));
            Camera.fieldOfView = gaze.Fov(config);
        }

        static WorldPos FootPosition(WalkSim sim, bool left, FieldConfig config)
        {
            double rad = sim.Course * Math.PI / 180.0;
            double side = (left ? -1 : 1) * config.footprintOffset;
            long dx = (long)(Math.Cos(rad) * side * 1000.0);
            long dz = (long)(-Math.Sin(rad) * side * 1000.0);
            return sim.Position.Offset(dx, dz);
        }
    }
}
