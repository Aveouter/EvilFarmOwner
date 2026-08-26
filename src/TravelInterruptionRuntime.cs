using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;

namespace EvilFarmOwner;

internal static class TravelInterruptionRuntime
{
    public static TravelInterruptionSnapshot Capture(
        GameLocation location,
        NPC worker,
        PathFindController? expectedController,
        Point? destination,
        TravelInterruptionKind kind,
        GridPoint? previousProgressTile,
        Stack<Point>? explicitPath = null,
        string? explicitCollisionProbe = null)
    {
        Stack<Point>? remainingPath = explicitPath ?? expectedController?.pathToEndPoint;
        Point? nextPoint = remainingPath is { Count: > 0 } ? remainingPath.Peek() : null;
        string collisionProbe = explicitCollisionProbe ?? "no remaining waypoint";
        if (explicitCollisionProbe is null && nextPoint.HasValue)
        {
            Stack<Point> probePath = new();
            probePath.Push(nextPoint.Value);
            collisionProbe = FarmNavigationMap.CanBeginPath(
                location,
                worker,
                worker.TilePoint,
                probePath,
                out string failure)
                ? "pass"
                : failure;
        }

        TravelControllerState controllerState = worker.controller is null
            ? TravelControllerState.None
            : ReferenceEquals(worker.controller, expectedController)
                ? TravelControllerState.Attached
                : TravelControllerState.Replaced;
        return new TravelInterruptionSnapshot(
            kind,
            location.NameOrUniqueName,
            new GridPoint(worker.TilePoint.X, worker.TilePoint.Y),
            worker.Position.X,
            worker.Position.Y,
            destination.HasValue
                ? new GridPoint(destination.Value.X, destination.Value.Y)
                : null,
            remainingPath?.Count ?? 0,
            nextPoint.HasValue
                ? new GridPoint(nextPoint.Value.X, nextPoint.Value.Y)
                : null,
            previousProgressTile,
            controllerState,
            collisionProbe);
    }
}
