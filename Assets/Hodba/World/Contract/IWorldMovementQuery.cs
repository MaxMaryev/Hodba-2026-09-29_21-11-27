using Hodba.Core;

namespace Hodba.World
{
    /// <summary>Optional solid obstacles, shared by foreground and background walking.</summary>
    public interface IWorldMovementQuery
    {
        WorldPos ResolveMovement(WorldPos from, WorldPos to);
    }
}
