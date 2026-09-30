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

        public void Apply(in PoseDelta pose, WalkSim sim, GazeController gaze, IWorldQuery world, FloatingOrigin origin, FieldConfig config)
        {
            float ground = world.SampleHeightMm(sim.Position) / 1000f;
            float courseRad = sim.Course * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(courseRad), 0f, Mathf.Cos(courseRad));
            var right = new Vector3(forward.z, 0f, -forward.x);

            var pos = origin.ToLocal(sim.Position, ground + config.eyeHeight + pose.Up)
                      + right * pose.Side + forward * pose.Forward;

            // Гашение тряски взглядом: глаз доворачивается на точку впереди.
            float stab = config.gazeStabilization;
            float dist = Mathf.Max(1f, config.stabilizationDistance);
            float pitch = gaze.Pitch + pose.Pitch + Mathf.Atan2(pose.Up, dist) * Mathf.Rad2Deg * stab;
            float yaw = gaze.Yaw + pose.Yaw - Mathf.Atan2(pose.Side, dist) * Mathf.Rad2Deg * stab;

            Camera.transform.SetPositionAndRotation(pos, Quaternion.Euler(pitch, yaw, pose.Roll));
            Camera.fieldOfView = gaze.Fov(config);
        }
    }
}
