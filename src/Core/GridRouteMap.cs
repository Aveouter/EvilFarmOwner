namespace EvilFarmOwner;

internal sealed class GridRouteMap
{
    private static readonly GridPoint[] NeighborOffsets =
    {
        new(-1, 0), new(1, 0), new(0, 1), new(0, -1)
    };

    private readonly Dictionary<GridPoint, GridPoint> Previous;
    private readonly Dictionary<GridPoint, int> Distances;

    private GridRouteMap(
        GridPoint start,
        Dictionary<GridPoint, GridPoint> previous,
        Dictionary<GridPoint, int> distances)
    {
        this.Start = start;
        this.Previous = previous;
        this.Distances = distances;
    }

    public GridPoint Start { get; }

    public static GridRouteMap Build(
        int width,
        int height,
        GridPoint start,
        Func<GridPoint, bool> isPassable,
        int maximumVisitedTiles = int.MaxValue) =>
        Build(width, height, start, isPassable, canTraverse: null, maximumVisitedTiles);

    public static GridRouteMap Build(
        int width,
        int height,
        GridPoint start,
        Func<GridPoint, bool> isPassable,
        Func<GridPoint, GridPoint, bool>? canTraverse,
        int maximumVisitedTiles = int.MaxValue)
    {
        Dictionary<GridPoint, GridPoint> previous = new();
        Dictionary<GridPoint, int> distances = new() { [start] = 0 };
        Queue<GridPoint> open = new();
        open.Enqueue(start);
        while (open.Count > 0 && distances.Count < maximumVisitedTiles)
        {
            GridPoint current = open.Dequeue();
            foreach (GridPoint offset in NeighborOffsets)
            {
                GridPoint next = new(current.X + offset.X, current.Y + offset.Y);
                if (next.X < 0 || next.Y < 0 || next.X >= width || next.Y >= height
                    || distances.ContainsKey(next) || !isPassable(next)
                    || canTraverse?.Invoke(current, next) == false)
                    continue;
                previous[next] = current;
                distances[next] = distances[current] + 1;
                open.Enqueue(next);
            }
        }
        return new GridRouteMap(start, previous, distances);
    }

    public bool IsReachable(GridPoint tile) => this.Distances.ContainsKey(tile);

    public bool TryGetDistance(GridPoint tile, out int distance) =>
        this.Distances.TryGetValue(tile, out distance);

    public bool TryGetPath(GridPoint end, out IReadOnlyList<GridPoint> path)
    {
        path = Array.Empty<GridPoint>();
        if (!this.Distances.ContainsKey(end))
            return false;
        List<GridPoint> reversed = new() { end };
        GridPoint current = end;
        while (current != this.Start)
        {
            if (!this.Previous.TryGetValue(current, out current))
                return false;
            reversed.Add(current);
        }
        reversed.Reverse();
        path = reversed;
        return true;
    }
}

internal static partial class FarmNavigationMap
{
    public static IReadOnlyList<GridPoint> ToControllerSteps(IReadOnlyList<GridPoint> gridPath) =>
        gridPath.Skip(1).ToArray();

    public static GridPoint GetAlignedCharacterPixel(GridPoint tile, int tileSize)
    {
        if (tileSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(tileSize));
        return new GridPoint(tile.X * tileSize, tile.Y * tileSize);
    }

    public static bool TryGetFirstStepOffset(
        GridPoint start,
        GridPoint firstWaypoint,
        int movementPixels,
        out GridPoint offset)
    {
        offset = default;
        if (movementPixels <= 0)
            return false;
        int deltaX = firstWaypoint.X - start.X;
        int deltaY = firstWaypoint.Y - start.Y;
        if (Math.Abs(deltaX) + Math.Abs(deltaY) != 1)
            return false;
        offset = new GridPoint(Math.Sign(deltaX) * movementPixels, Math.Sign(deltaY) * movementPixels);
        return true;
    }
}
