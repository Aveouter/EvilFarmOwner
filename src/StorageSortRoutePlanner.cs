using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

namespace EvilFarmOwner;

internal sealed record StorageSortInteractionOption(
    GridPoint InteractionTile,
    int Distance,
    int OffsetPriority);

internal static class StorageSortRouteSelection
{
    public static IEnumerable<StorageSortInteractionOption> Order(
        IEnumerable<StorageSortInteractionOption> options)
    {
        return options
            .OrderBy(option => option.Distance)
            .ThenBy(option => option.OffsetPriority)
            .ThenBy(option => option.InteractionTile.Y)
            .ThenBy(option => option.InteractionTile.X);
    }
}

internal sealed record StorageSortRouteStep(
    StorageSortTransfer Transfer,
    Point SourceInteractionTile,
    Stack<Point> SourcePath,
    Point DestinationInteractionTile,
    Stack<Point> DestinationPath);

internal sealed record StorageSortRoutePlan(
    Point ArrivalTile,
    FarmBoundarySide ArrivalSide,
    IReadOnlyList<StorageSortRouteStep> Steps,
    Stack<Point> ReturnPath);

internal enum StorageSortRouteFailure
{
    None,
    InvalidPlan,
    UnsupportedFarmMap,
    NoBoundaryEntrance,
    NoSafeArrival,
    NoSafeRoundTrip
}

internal sealed record StorageSortRoutePlanResult(
    StorageSortRouteFailure Failure,
    StorageSortRoutePlan? Plan)
{
    public bool IsSuccess => this.Failure == StorageSortRouteFailure.None
        && this.Plan is not null;
}

internal sealed class StorageSortRoutePlanner
{
    private const int MaximumSupportedMapDimension = 255;
    private const int MaximumArrivalPathChecksPerSide = 8;
    private static readonly Point[] InteractionOffsets =
    {
        new(0, 1),
        new(-1, 0),
        new(1, 0),
        new(0, -1)
    };

    private readonly IMonitor Monitor;

    public StorageSortRoutePlanner(IMonitor monitor)
    {
        this.Monitor = monitor;
    }

    public StorageSortRoutePlanResult TryCreate(
        Farm farm,
        NPC worker,
        StorageSortRuntimePlan runtimePlan)
    {
        if (runtimePlan.Plan.Transfers.Count == 0)
        {
            return new StorageSortRoutePlanResult(
                StorageSortRouteFailure.InvalidPlan,
                Plan: null);
        }

        int mapWidth = farm.Map.Layers[0].LayerWidth;
        int mapHeight = farm.Map.Layers[0].LayerHeight;
        if (mapWidth > MaximumSupportedMapDimension || mapHeight > MaximumSupportedMapDimension)
        {
            return new StorageSortRoutePlanResult(
                StorageSortRouteFailure.UnsupportedFarmMap,
                Plan: null);
        }

        IReadOnlyList<GridPoint> arrivals = FarmEntranceSelection.OrderBoundaryArrivalCandidates(
            mapWidth,
            mapHeight,
            farm.warps.Select(warp => new GridPoint(warp.X, warp.Y)));
        if (arrivals.Count == 0)
        {
            return new StorageSortRoutePlanResult(
                StorageSortRouteFailure.NoBoundaryEntrance,
                Plan: null);
        }

        bool foundSafeArrival = false;
        Dictionary<FarmBoundarySide, int> checkedPathsBySide = new();
        Dictionary<GridPoint, GridRouteMap> routeCache = new();
        foreach (GridPoint arrival in arrivals)
        {
            FarmBoundarySide arrivalSide = FarmEntranceSelection.GetNearestBoundarySide(
                mapWidth,
                mapHeight,
                arrival);
            int checkedOnSide = checkedPathsBySide.GetValueOrDefault(arrivalSide);
            if (checkedOnSide >= MaximumArrivalPathChecksPerSide)
                continue;

            Vector2 arrivalVector = new(arrival.X, arrival.Y);
            if (farm.warps.Any(warp => warp.X == arrival.X && warp.Y == arrival.Y)
                || farm.doors.ContainsKey(new Point(arrival.X, arrival.Y))
                || !farm.CanSpawnCharacterHere(arrivalVector))
            {
                continue;
            }

            foundSafeArrival = true;
            checkedPathsBySide[arrivalSide] = checkedOnSide + 1;
            if (!this.TryCreateFromArrival(
                    farm,
                    worker,
                    runtimePlan.Plan.Transfers,
                    arrival,
                    routeCache,
                    out StorageSortRoutePlan? routePlan)
                || routePlan is null)
            {
                continue;
            }

            return new StorageSortRoutePlanResult(StorageSortRouteFailure.None, routePlan);
        }

        return new StorageSortRoutePlanResult(
            foundSafeArrival
                ? StorageSortRouteFailure.NoSafeRoundTrip
                : StorageSortRouteFailure.NoSafeArrival,
            Plan: null);
    }

    public bool TryCreateChestRoute(
        Farm farm,
        NPC worker,
        Point start,
        GridPoint chestTile,
        TravelObstacleLedger obstacles,
        out Point interaction,
        out Stack<Point>? path)
    {
        Dictionary<GridPoint, GridRouteMap> routeCache = new();
        bool found = this.TryCreateChestRoute(
            farm,
            worker,
            new GridPoint(start.X, start.Y),
            chestTile,
            routeCache,
            obstacles,
            out GridPoint interactionTile,
            out path);
        interaction = new Point(interactionTile.X, interactionTile.Y);
        return found;
    }

    public bool TryCreateDirectRoute(
        Farm farm,
        NPC worker,
        Point start,
        Point destination,
        TravelObstacleLedger obstacles,
        out Stack<Point>? path)
    {
        return this.TryCreateDirectRoute(
            farm,
            worker,
            new GridPoint(start.X, start.Y),
            new GridPoint(destination.X, destination.Y),
            new Dictionary<GridPoint, GridRouteMap>(),
            obstacles,
            out path);
    }

    private bool TryCreateFromArrival(
        Farm farm,
        NPC worker,
        IReadOnlyList<StorageSortTransfer> transfers,
        GridPoint arrival,
        Dictionary<GridPoint, GridRouteMap> routeCache,
        out StorageSortRoutePlan? plan)
    {
        plan = null;
        GridPoint current = arrival;
        List<StorageSortRouteStep> steps = new(transfers.Count);
        foreach (StorageSortTransfer transfer in transfers)
        {
            if (!this.TryCreateChestRoute(
                    farm,
                    worker,
                    current,
                    transfer.SourceChest,
                    routeCache,
                    obstacles: null,
                    out GridPoint sourceInteraction,
                    out Stack<Point>? sourcePath)
                || sourcePath is null
                || !this.TryCreateChestRoute(
                    farm,
                    worker,
                    sourceInteraction,
                    transfer.DestinationChest,
                    routeCache,
                    obstacles: null,
                    out GridPoint destinationInteraction,
                    out Stack<Point>? destinationPath)
                || destinationPath is null)
            {
                return false;
            }

            steps.Add(new StorageSortRouteStep(
                transfer,
                new Point(sourceInteraction.X, sourceInteraction.Y),
                sourcePath,
                new Point(destinationInteraction.X, destinationInteraction.Y),
                destinationPath));
            current = destinationInteraction;
        }

        if (!this.TryCreateDirectRoute(
                farm,
                worker,
                current,
                arrival,
                routeCache,
                obstacles: null,
                out Stack<Point>? returnPath)
            || returnPath is null)
        {
            return false;
        }

        plan = new StorageSortRoutePlan(
            new Point(arrival.X, arrival.Y),
            FarmEntranceSelection.GetNearestBoundarySide(
                farm.Map.Layers[0].LayerWidth,
                farm.Map.Layers[0].LayerHeight,
                arrival),
            steps,
            returnPath);
        return true;
    }

    private bool TryCreateChestRoute(
        Farm farm,
        NPC worker,
        GridPoint start,
        GridPoint chestTile,
        Dictionary<GridPoint, GridRouteMap> routeCache,
        TravelObstacleLedger? obstacles,
        out GridPoint interaction,
        out Stack<Point>? path)
    {
        interaction = default;
        path = null;
        if (!this.TryGetRoutes(farm, worker, start, routeCache, obstacles, out GridRouteMap? routes)
            || routes is null)
        {
            return false;
        }

        List<StorageSortInteractionOption> options = new();
        for (int index = 0; index < InteractionOffsets.Length; index++)
        {
            Point offset = InteractionOffsets[index];
            GridPoint candidate = new(chestTile.X + offset.X, chestTile.Y + offset.Y);
            if (routes.TryGetDistance(candidate, out int distance))
                options.Add(new StorageSortInteractionOption(candidate, distance, index));
        }

        foreach (StorageSortInteractionOption option in StorageSortRouteSelection.Order(options))
        {
            if (!routes.TryGetPath(option.InteractionTile, out IReadOnlyList<GridPoint> gridPath))
                continue;

            Stack<Point> candidatePath = FarmNavigationMap.ToPath(gridPath);
            if (!FarmNavigationMap.CanBeginPath(
                    farm,
                    worker,
                    new Point(start.X, start.Y),
                    candidatePath,
                    out _))
            {
                continue;
            }

            interaction = option.InteractionTile;
            path = candidatePath;
            return true;
        }

        return false;
    }

    private bool TryCreateDirectRoute(
        Farm farm,
        NPC worker,
        GridPoint start,
        GridPoint destination,
        Dictionary<GridPoint, GridRouteMap> routeCache,
        TravelObstacleLedger? obstacles,
        out Stack<Point>? path)
    {
        path = null;
        if (!this.TryGetRoutes(farm, worker, start, routeCache, obstacles, out GridRouteMap? routes)
            || routes is null
            || !routes.TryGetPath(destination, out IReadOnlyList<GridPoint> gridPath))
        {
            return false;
        }

        Stack<Point> candidatePath = FarmNavigationMap.ToPath(gridPath);
        if (!FarmNavigationMap.CanBeginPath(
                farm,
                worker,
                new Point(start.X, start.Y),
                candidatePath,
                out _))
        {
            return false;
        }

        path = candidatePath;
        return true;
    }

    private bool TryGetRoutes(
        Farm farm,
        NPC worker,
        GridPoint start,
        Dictionary<GridPoint, GridRouteMap> routeCache,
        TravelObstacleLedger? obstacles,
        out GridRouteMap? routes)
    {
        if (routeCache.TryGetValue(start, out routes))
            return true;
        bool built = obstacles is null
            ? FarmNavigationMap.TryBuild(
                farm,
                worker,
                new Point(start.X, start.Y),
                this.Monitor,
                out routes)
            : FarmNavigationMap.TryBuild(
                farm,
                worker,
                new Point(start.X, start.Y),
                this.Monitor,
                farm.NameOrUniqueName,
                obstacles,
                out routes);
        if (!built
            || routes is null)
        {
            return false;
        }

        routeCache[start] = routes;
        return true;
    }
}
