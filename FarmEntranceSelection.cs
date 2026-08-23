namespace EvilFarmOwner;

internal static class FarmEntranceSelection
{
    public static GridPoint SelectLeftEntrance(
        int mapWidth,
        int mapHeight,
        IEnumerable<GridPoint> warpTiles)
    {
        GridPoint boundary = warpTiles
            .OrderBy(tile => tile.X)
            .ThenBy(tile => Math.Abs(tile.Y - mapHeight / 2))
            .ThenBy(tile => tile.Y)
            .FirstOrDefault(new GridPoint(0, mapHeight / 2));

        int inwardX = boundary.X <= mapWidth / 2
            ? Math.Min(mapWidth - 1, boundary.X + 1)
            : Math.Max(0, boundary.X - 1);

        int inwardY = Math.Clamp(boundary.Y, 0, Math.Max(0, mapHeight - 1));
        return new GridPoint(inwardX, inwardY);
    }
}
