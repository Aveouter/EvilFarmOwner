namespace EvilFarmOwner;

internal static class HarvestEmergencyDropSelection
{
    public static GridPoint? FindNearest(
        int mapWidth,
        int mapHeight,
        GridPoint anchor,
        Func<GridPoint, bool> isEligible,
        int maximumRadius = 8)
    {
        ArgumentNullException.ThrowIfNull(isEligible);
        if (mapWidth <= 0 || mapHeight <= 0 || maximumRadius < 0)
            return null;

        int minimumX = Math.Max(0, anchor.X - maximumRadius);
        int maximumX = Math.Min(mapWidth - 1, anchor.X + maximumRadius);
        int minimumY = Math.Max(0, anchor.Y - maximumRadius);
        int maximumY = Math.Min(mapHeight - 1, anchor.Y + maximumRadius);

        IEnumerable<GridPoint> ordered = Enumerable.Range(minimumX, maximumX - minimumX + 1)
            .SelectMany(x => Enumerable.Range(minimumY, maximumY - minimumY + 1)
                .Select(y => new GridPoint(x, y)))
            .Where(tile => Math.Abs(tile.X - anchor.X) + Math.Abs(tile.Y - anchor.Y) <= maximumRadius)
            .OrderBy(tile => Math.Abs(tile.X - anchor.X) + Math.Abs(tile.Y - anchor.Y))
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X);
        foreach (GridPoint tile in ordered)
        {
            if (isEligible(tile))
                return tile;
        }

        return null;
    }
}
