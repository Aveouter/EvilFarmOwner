namespace EvilFarmOwner;

internal static class FarmEntranceSelection
{
    private const int DefaultSearchDepth = 8;

    /// <summary>
    /// Enumerate visible farm-edge arrival candidates without treating interior map warps
    /// (farmhouse, greenhouse, cave, and similar doors) as entrances.
    /// </summary>
    public static IReadOnlyList<GridPoint> OrderLeftBoundaryCandidates(
        int mapWidth,
        int mapHeight,
        int searchDepth = DefaultSearchDepth)
    {
        if (mapWidth <= 0 || mapHeight <= 0 || searchDepth < 0)
            return Array.Empty<GridPoint>();

        int firstInwardX = mapWidth > 1 ? 1 : 0;
        int lastInwardX = Math.Min(mapWidth - 1, firstInwardX + searchDepth);
        int verticalCenter = mapHeight / 2;

        return Enumerable.Range(firstInwardX, lastInwardX - firstInwardX + 1)
            .SelectMany(x => Enumerable.Range(0, mapHeight).Select(y => new GridPoint(x, y)))
            .OrderBy(tile => tile.X)
            .ThenBy(tile => Math.Abs(tile.Y - verticalCenter))
            .ThenBy(tile => tile.Y)
            .ToArray();
    }
}
