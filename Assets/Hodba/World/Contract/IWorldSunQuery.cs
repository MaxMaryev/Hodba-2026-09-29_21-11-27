using Hodba.Core;
namespace Hodba.World
{
    /// <summary>Sun-ray occlusion independent of rendered LODs and floating origin.</summary>
    public interface IWorldSunQuery
    {
        bool IsSunOccluded(WorldPos position, double heightMeters, double dx, double dy, double dz);
    }
}
