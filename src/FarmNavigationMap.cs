using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;

namespace EvilFarmOwner;

/// <summary>A deterministic single-source shortest-path map for a four-direction tile grid.</summary>
internal sealed class GridRouteMap
{
    private static readonly GridPoint[] NeighborOffsets =
    {
        new(-1, 0),
        new(1, 0),
        new(0, 1),
        new(0, -1)
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
        int maximumVisitedTiles = int.MaxValue)
    {
        return Build(
            width,
            height,
            start,
            isPassable,
            canTraverse: null,
            maximumVisitedTiles);
    }

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
                if (next.X < 0
                    || next.Y < 0
                    || next.X >= width
                    || next.Y >= height
                    || distances.ContainsKey(next)
                    || !isPassable(next)
                    || canTraverse?.Invoke(current, next) == false)
                    continue;

                previous[next] = current;
                distances[next] = distances[current] + 1;
                open.Enqueue(next);
            }
        }

        return new GridRouteMap(start, previous, distances);
    }

    public bool IsReachable(GridPoint tile)
    {
        return this.Distances.ContainsKey(tile);
    }

    public bool TryGetDistance(GridPoint tile, out int distance)
    {
        return this.Distances.TryGetValue(tile, out distance);
    }

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

internal static class FarmNavigationMap
{
    private const int MaximumVisitedTiles = 65535;
    private const int VanillaNpcWalkingPixelsPerTick = 2;

    public static bool TryBuild(
        GameLocation farm,
        NPC worker,
        Point startTile,
        IMonitor monitor,
        out GridRouteMap? routes)
    {
        return TryBuild(
            farm,
            worker,
            startTile,
            monitor,
            excludedTiles: null,
            out routes);
    }

    public static bool TryBuild(
        GameLocation farm,
        NPC worker,
        Point startTile,
        IMonitor monitor,
        IReadOnlySet<GridPoint>? excludedTiles,
        out GridRouteMap? routes)
    {
        routes = null;
        int width = farm.Map.Layers[0].LayerWidth;
        int height = farm.Map.Layers[0].LayerHeight;
        try
        {
            GridPoint start = new(startTile.X, startTile.Y);
            routes = GridRouteMap.Build(
                width,
                height,
                start,
                tile => (tile == start || excludedTiles?.Contains(tile) != true)
                    && IsPassable(farm, worker, tile),
                MaximumVisitedTiles);
            return true;
        }
        catch (Exception ex)
        {
            monitor.Log(
                $"Farm navigation scan failed closed for worker '{worker.Name}' from {startTile}: {ex.Message}",
                LogLevel.Warn);
            return false;
        }
    }

    public static bool TryBuild(
        GameLocation farm,
        NPC worker,
        Point startTile,
        IMonitor monitor,
        string locationKey,
        TravelObstacleLedger obstacles,
        out GridRouteMap? routes)
    {
        if (string.IsNullOrWhiteSpace(locationKey))
            throw new ArgumentException("A location key is required.", nameof(locationKey));
        ArgumentNullException.ThrowIfNull(obstacles);
        routes = null;
        int width = farm.Map.Layers[0].LayerWidth;
        int height = farm.Map.Layers[0].LayerHeight;
        try
        {
            GridPoint start = new(startTile.X, startTile.Y);
            routes = GridRouteMap.Build(
                width,
                height,
                start,
                tile => (tile == start || !obstacles.IsTileBlocked(locationKey, tile))
                    && IsPassable(farm, worker, tile),
                (from, to) => !obstacles.IsEdgeBlocked(locationKey, from, to),
                MaximumVisitedTiles);
            return true;
        }
        catch (Exception ex)
        {
            monitor.Log(
                $"Farm navigation scan failed closed for worker '{worker.Name}' from {startTile}: {ex.Message}",
                LogLevel.Warn);
            return false;
        }
    }

    public static Stack<Point> ToPath(IReadOnlyList<GridPoint> gridPath)
    {
        IReadOnlyList<GridPoint> steps = ToControllerSteps(gridPath);
        Stack<Point> path = new();
        for (int index = steps.Count - 1; index >= 0; index--)
            path.Push(new Point(steps[index].X, steps[index].Y));
        return path;
    }

    public static IReadOnlyList<GridPoint> ToControllerSteps(IReadOnlyList<GridPoint> gridPath)
    {
        // PathFindController tries to center a character within its first waypoint.
        // A freshly warped NPC is already on gridPath[0], and that centering step can
        // collide with an adjacent edge tile before the NPC ever starts walking.
        return gridPath.Skip(1).ToArray();
    }

    public static Vector2 GetAlignedCharacterPosition(Point tile)
    {
        GridPoint pixel = GetAlignedCharacterPixel(new GridPoint(tile.X, tile.Y), Game1.tileSize);
        return new Vector2(pixel.X, pixel.Y);
    }

    public static GridPoint GetAlignedCharacterPixel(GridPoint tile, int tileSize)
    {
        if (tileSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(tileSize));
        return new GridPoint(tile.X * tileSize, tile.Y * tileSize);
    }

    public static bool CanBeginPath(
        GameLocation farm,
        NPC worker,
        Point startTile,
        Stack<Point> path,
        out string failure)
    {
        failure = string.Empty;
        if (path.Count == 0)
            return true;

        Point firstWaypoint = path.Peek();
        if (!TryGetFirstStepOffset(
                new GridPoint(startTile.X, startTile.Y),
                new GridPoint(firstWaypoint.X, firstWaypoint.Y),
                VanillaNpcWalkingPixelsPerTick,
                out GridPoint offset))
        {
            failure = $"first waypoint {firstWaypoint} is not cardinally adjacent";
            return false;
        }

        try
        {
            Rectangle currentBounds = worker.GetBoundingBox();
            if (currentBounds.IsEmpty)
            {
                failure = "worker has no collision bounds";
                return false;
            }

            Vector2 alignedPosition = GetAlignedCharacterPosition(startTile);
            Rectangle startBounds = new(
                (int)alignedPosition.X + currentBounds.X - (int)worker.Position.X,
                (int)alignedPosition.Y + currentBounds.Y - (int)worker.Position.Y,
                currentBounds.Width,
                currentBounds.Height);
            if (farm.isCollidingPosition(
                    startBounds,
                    Game1.viewport,
                    isFarmer: false,
                    damagesFarmer: 0,
                    glider: false,
                    worker,
                    pathfinding: true))
            {
                failure = $"worker collision bounds cannot occupy {startTile}";
                return false;
            }

            Rectangle nextBounds = startBounds;
            nextBounds.Offset(offset.X, offset.Y);
            if (!farm.isCollidingPosition(
                    nextBounds,
                    Game1.viewport,
                    isFarmer: false,
                    damagesFarmer: 0,
                    glider: false,
                    worker,
                    pathfinding: true))
            {
                return true;
            }

            Vector2 firstWaypointVector = new(firstWaypoint.X, firstWaypoint.Y);
            if (farm.objects.TryGetValue(firstWaypointVector, out StardewValley.Object? placedObject)
                && placedObject is Fence { isGate.Value: true }
                && farm.isTilePassable(firstWaypointVector))
            {
                return true;
            }

            failure = $"worker collision bounds cannot take the first step toward {firstWaypoint}";
            return false;
        }
        catch (Exception ex)
        {
            failure = $"first-step collision probe failed: {ex.Message}";
            return false;
        }
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

        offset = new GridPoint(
            Math.Sign(deltaX) * movementPixels,
            Math.Sign(deltaY) * movementPixels);
        return true;
    }

    private static bool IsPassable(GameLocation farm, NPC worker, GridPoint tile)
    {
        if (farm.warps.Any(warp => warp.X == tile.X && warp.Y == tile.Y)
            || farm.doors.ContainsKey(new Point(tile.X, tile.Y)))
            return false;

        Vector2 tileVector = new(tile.X, tile.Y);
        if (farm.objects.TryGetValue(tileVector, out StardewValley.Object? placedObject)
            && placedObject is Fence { isGate.Value: true })
        {
            // PathFindController.nonDestructivePathing opens a gate immediately before
            // entering it. Plan against the underlying map tile without opening it early.
            return farm.isTilePassable(tileVector);
        }

        Rectangle bounds = new(
            tile.X * Game1.tileSize + 1,
            tile.Y * Game1.tileSize + 1,
            Game1.tileSize - 2,
            Game1.tileSize - 2);
        return !farm.isCollidingPosition(
            bounds,
            Game1.viewport,
            isFarmer: false,
            damagesFarmer: 0,
            glider: false,
            worker,
            pathfinding: true);
    }
}
