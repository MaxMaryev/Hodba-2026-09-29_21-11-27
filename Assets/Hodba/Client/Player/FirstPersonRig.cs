using Hodba.Client.Body;
using Hodba.Sim.Walk;
using Hodba.World;
using UnityEngine;

namespace Hodba.Client
{
    /// <summary>
    /// Глаза человека. Только применяет к камере то, что собрало тело (<see cref="WalkerBody"/>):
    /// голова идёт с телом, а взгляд гасит тряску так, как это делает живой человек —
    /// голова качается, а точка в десяти метрах впереди почти стоит. Так реалистичнее и меньше укачивает.
    /// </summary>
    public sealed class FirstPersonRig
    {
        public readonly Camera Camera;

        public FirstPersonRig(Camera camera)
        {
            Camera = camera;
        }

        /// <param name="eyes">Куда глаза смотрят сами. Камера — это глаз: их взгляд поворачивает саму картинку.</param>
        /// <param name="support">Высота опоры, м: земля под стопой, на которой стоит тело, а не под центром.</param>
        public void Apply(in PoseDelta pose, in EyeState eyes, float support, WalkSim sim, GazeController gaze, FloatingOrigin origin, FieldConfig config)
        {
            float courseRad = sim.Course * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(courseRad), 0f, Mathf.Cos(courseRad));
            var right = new Vector3(forward.z, 0f, -forward.x);

            var pos = origin.ToLocal(sim.Position, support + config.eyeHeight + pose.Up)
                      + right * pose.Side + forward * pose.Forward;

            // Гашение тряски взглядом: глаз держит точку, на которую смотрит, — дорогу у ног сильнее, горизонт слабее.
            float stab = config.gazeStabilization;
            float focus = Mathf.Clamp(eyes.FocusDistance, 2f, 30f);
            float dist = Mathf.Max(1f, Mathf.Lerp(config.stabilizationDistance, focus, eyes.Gain));
            float pitch = gaze.Pitch + eyes.Pitch + pose.Pitch + Mathf.Atan2(pose.Up, dist) * Mathf.Rad2Deg * stab;
            float yaw = gaze.Yaw + eyes.Yaw + pose.Yaw - Mathf.Atan2(pose.Side, dist) * Mathf.Rad2Deg * stab;

            Camera.transform.SetPositionAndRotation(pos, Quaternion.Euler(pitch, yaw, pose.Roll));
            Camera.fieldOfView = gaze.Fov(config);
        }
    }
}
