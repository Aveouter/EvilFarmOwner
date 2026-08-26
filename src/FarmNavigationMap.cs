using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;

namespace EvilFarmOwner;

/// <summary>A deterministic single-source shortest-path map for a four-direction tile grid.</summary>
internal static partial class FarmNavigationMap
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

    public static Vector2 GetAlignedCharacterPosition(Point tile)
    {
        GridPoint pixel = GetAlignedCharacterPixel(new GridPoint(tile.X, tile.Y), Game1.tileSize);
        return new Vector2(pixel.X, pixel.Y);
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

    private static bool IsPassable(GameLocation farm, NPC worker, GridPoint tile)
    {
        if (IsOccupiedByOtherLeasedWorker(farm, worker, tile))
            return false;
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

    public static bool IsOccupiedByOtherLeasedWorker(
        GameLocation location,
        NPC worker,
        GridPoint tile)
    {
        return location.characters.Any(candidate =>
            !ReferenceEquals(candidate, worker)
            && NpcWorkLease.IsLeasedWorker(candidate)
            && candidate.TilePoint.X == tile.X
            && candidate.TilePoint.Y == tile.Y);
    }
}
