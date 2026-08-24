namespace EvilFarmOwner;

internal enum FarmBoundarySide
{
    North,
    East,
    South,
    West
}

internal static class FarmEntranceSelection
{
    private const int DefaultSearchRadius = 8;

    /// <summary>
    /// Enumerate visible arrival candidates around genuine map-boundary warps without
    /// treating interior transfers (farmhouse, greenhouse, cave, and similar doors) as entrances.
    /// </summary>
    public static IReadOnlyList<GridPoint> OrderBoundaryArrivalCandidates(
        int mapWidth,
        int mapHeight,
        IEnumerable<GridPoint> warpTiles,
        int searchRadius = DefaultSearchRadius)
    {
        if (mapWidth <= 0 || mapHeight <= 0 || searchRadius < 0)
            return Array.Empty<GridPoint>();

        GridPoint[] anchors = warpTiles
            .Where(tile => IsBoundaryWarp(tile, mapWidth, mapHeight))
            .Select(tile => new GridPoint(
                Math.Clamp(tile.X, 0, mapWidth - 1),
                Math.Clamp(tile.Y, 0, mapHeight - 1)))
            .Distinct()
            .ToArray();
        if (anchors.Length == 0)
            return Array.Empty<GridPoint>();

        List<GridPoint> ordered = new();
        HashSet<GridPoint> seen = new();
        for (int distance = 0; distance <= searchRadius; distance++)
        {
            foreach (GridPoint anchor in anchors)
            {
                foreach (GridPoint candidate in EnumerateRing(anchor, distance))
                {
                    if (candidate.X < 0
                        || candidate.Y < 0
                        || candidate.X >= mapWidth
                        || candidate.Y >= mapHeight
                        || !seen.Add(candidate))
                        continue;

                    ordered.Add(candidate);
                }
            }
        }

        return ordered;
    }

    public static FarmBoundarySide GetNearestBoundarySide(
        int mapWidth,
        int mapHeight,
        GridPoint tile)
    {
        if (mapWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(mapWidth), "Map width must be positive.");
        if (mapHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(mapHeight), "Map height must be positive.");

        (FarmBoundarySide Side, int Distance)[] options =
        {
            (FarmBoundarySide.North, Math.Max(0, tile.Y)),
            (FarmBoundarySide.East, Math.Max(0, mapWidth - 1 - tile.X)),
            (FarmBoundarySide.South, Math.Max(0, mapHeight - 1 - tile.Y)),
            (FarmBoundarySide.West, Math.Max(0, tile.X))
        };

        return options
            .OrderBy(option => option.Distance)
            .ThenBy(option => option.Side)
            .First()
            .Side;
    }

    private static bool IsBoundaryWarp(GridPoint tile, int mapWidth, int mapHeight)
    {
        return tile.X <= 0
            || tile.Y <= 0
            || tile.X >= mapWidth - 1
            || tile.Y >= mapHeight - 1;
    }

    private static IEnumerable<GridPoint> EnumerateRing(GridPoint center, int distance)
    {
        for (int yOffset = -distance; yOffset <= distance; yOffset++)
        {
            int xMagnitude = distance - Math.Abs(yOffset);
            yield return new GridPoint(center.X - xMagnitude, center.Y + yOffset);
            if (xMagnitude > 0)
                yield return new GridPoint(center.X + xMagnitude, center.Y + yOffset);
        }
    }
}
