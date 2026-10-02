using Hodba.Core;
using Hodba.World;
using UnityEngine;
namespace Hodba.Client.Body
{
    public static class SunOcclusion
    {
        public static float Visibility(IWorldQuery world, WorldPos position, float eyeY, Vector3 direction) =>
            world is IWorldSunQuery sun && sun.IsSunOccluded(position, eyeY, direction.x, direction.y, direction.z) ? 0f : 1f;
    }
}
