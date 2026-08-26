namespace EvilFarmOwner;

internal enum TravelInterruptionKind
{
    Timeout,
    ProgressStall,
    ControllerEnded,
    ControllerReplaced
}

internal static class DeliveryRouteExclusionPolicy
{
    public static bool TrySelectFailedTile(
        GridPoint currentTile,
        GridPoint? nextWaypoint,
        out GridPoint failedTile)
    {
        failedTile = default;
        if (!nextWaypoint.HasValue)
            return false;

        GridPoint next = nextWaypoint.Value;
        int distance = Math.Abs(next.X - currentTile.X) + Math.Abs(next.Y - currentTile.Y);
        if (distance != 1)
            return false;

        failedTile = next;
        return true;
    }
}
