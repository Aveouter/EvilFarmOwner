using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.TerrainFeatures;

namespace EvilFarmOwner;

internal enum WateringPlanFailure
{
    None,
    UnsupportedFarmMap,
    NoSafeArrivalTile,
    NoDryCrop,
    NoReachableCrop
}

internal sealed record WateringTargetPlan(
    Point TargetTile,
    Point InteractionTile,
    int FacingDirection,
    Stack<Point> Path);

internal sealed record WateringWorkPlan(
    Point ArrivalTile,
    WateringTargetPlan FirstTarget);

internal sealed record WateringTargetSearchResult(
    WateringTargetPlan? Target,
    WateringPlanFailure Failure,
    int CandidateTargetCount)
{
    public bool IsSuccess => this.Target is not null && this.Failure == WateringPlanFailure.None;
}

internal sealed record WateringPlanResult(
    WateringWorkPlan? Plan,
    WateringPlanFailure Failure)
{
    public bool IsSuccess => this.Plan is not null && this.Failure == WateringPlanFailure.None;
}

internal sealed class WateringTargetPlanner
{
    private const int MaximumSupportedMapDimension = 255;
    private const int MaximumArrivalPathChecks = 32;
    private static readonly Point[] InteractionOffsets =
    {
        new(0, 1),
        new(-1, 0),
        new(1, 0),
        new(0, -1)
    };

    private readonly IMonitor Monitor;

    public WateringTargetPlanner(IMonitor monitor)
    {
        this.Monitor = monitor;
    }

    public WateringPlanResult TryCreate(Farm farm, NPC worker)
    {
        int width = farm.Map.Layers[0].LayerWidth;
        int height = farm.Map.Layers[0].LayerHeight;
        if (width > MaximumSupportedMapDimension || height > MaximumSupportedMapDimension)
            return new WateringPlanResult(null, WateringPlanFailure.UnsupportedFarmMap);

        bool foundSafeArrival = false;
        int checkedPaths = 0;
        WateringPlanFailure lastFailure = WateringPlanFailure.NoReachableCrop;
        foreach (GridPoint candidate in FarmEntranceSelection.OrderLeftBoundaryCandidates(width, height))
        {
            Vector2 candidateTile = new(candidate.X, candidate.Y);
            if (farm.warps.Any(warp => warp.X == candidate.X && warp.Y == candidate.Y)
                || farm.doors.ContainsKey(new Point(candidate.X, candidate.Y))
                || !farm.CanSpawnCharacterHere(candidateTile))
                continue;

            foundSafeArrival = true;
            if (++checkedPaths > MaximumArrivalPathChecks)
                break;

            Point arrivalTile = new(candidate.X, candidate.Y);
            WateringTargetSearchResult firstTarget = this.TryFindNext(
                farm,
                worker,
                arrivalTile,
                arrivalTile,
                new HashSet<Point>(),
                new HashSet<FarmTaskRouteEdge>());
            if (firstTarget.IsSuccess && firstTarget.Target is not null)
            {
                return new WateringPlanResult(
                    new WateringWorkPlan(arrivalTile, firstTarget.Target),
                    WateringPlanFailure.None);
            }

            lastFailure = firstTarget.Failure;
            if (lastFailure == WateringPlanFailure.NoDryCrop)
                break;
        }

        return new WateringPlanResult(
            null,
            foundSafeArrival ? lastFailure : WateringPlanFailure.NoSafeArrivalTile);
    }

    public WateringTargetSearchResult TryFindNext(
        Farm farm,
        NPC worker,
        Point startTile,
        Point arrivalTile,
        IReadOnlySet<Point> completedTargets,
        IReadOnlySet<FarmTaskRouteEdge> failedEdges)
    {
        int width = farm.Map.Layers[0].LayerWidth;
        int height = farm.Map.Layers[0].LayerHeight;

        if (!FarmNavigationMap.TryBuild(farm, worker, startTile, this.Monitor, out GridRouteMap? routes)
            || routes is null
            || !routes.IsReachable(new GridPoint(arrivalTile.X, arrivalTile.Y)))
        {
            return new WateringTargetSearchResult(
                null,
                WateringPlanFailure.NoReachableCrop,
                CountRemainingDryCrops(farm, completedTargets));
        }

        List<FarmTaskRouteOption> reachable = new();
        HashSet<Point> candidateTargets = new();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2 target = new(x, y);
                Point targetPoint = new(x, y);
                if (completedTargets.Contains(targetPoint) || !IsDryCrop(farm, target))
                    continue;

                candidateTargets.Add(targetPoint);
                foreach (Point offset in InteractionOffsets)
                {
                    Point interaction = new(x + offset.X, y + offset.Y);
                    FarmTaskRouteEdge edge = ToEdge(targetPoint, interaction);
                    GridPoint interactionGrid = new(interaction.X, interaction.Y);
                    if (failedEdges.Contains(edge)
                        || !farm.CanSpawnCharacterHere(new Vector2(interaction.X, interaction.Y))
                        || !routes.TryGetDistance(interactionGrid, out int distance))
                        continue;

                    reachable.Add(new FarmTaskRouteOption(
                        new GridPoint(x, y),
                        interactionGrid,
                        distance));
                }
            }
        }

        if (candidateTargets.Count == 0)
            return new WateringTargetSearchResult(null, WateringPlanFailure.NoDryCrop, 0);

        IReadOnlyList<FarmTaskRouteOption> ordered = FarmTaskRouteSelection.Order(
            reachable);
        if (ordered.Count == 0)
        {
            return new WateringTargetSearchResult(
                null,
                WateringPlanFailure.NoReachableCrop,
                candidateTargets.Count);
        }

        FarmTaskRouteOption best = ordered[0];
        if (!routes.TryGetPath(best.Interaction, out IReadOnlyList<GridPoint> gridPath))
        {
            return new WateringTargetSearchResult(
                null,
                WateringPlanFailure.NoReachableCrop,
                candidateTargets.Count);
        }
        Stack<Point> bestPath = FarmNavigationMap.ToPath(gridPath);
        Point bestTarget = new(best.Target.X, best.Target.Y);
        Point bestInteraction = new(best.Interaction.X, best.Interaction.Y);
        return new WateringTargetSearchResult(
            new WateringTargetPlan(
                bestTarget,
                bestInteraction,
                GetFacingDirection(bestInteraction, bestTarget),
                bestPath),
            WateringPlanFailure.None,
            candidateTargets.Count);
    }

    public static bool IsDryCrop(GameLocation location, Vector2 tile)
    {
        return location.terrainFeatures.TryGetValue(tile, out TerrainFeature? feature)
            && feature is HoeDirt dirt
            && dirt.crop is not null
            && dirt.state.Value != HoeDirt.watered;
    }

    public static int CountRemainingDryCrops(
        Farm farm,
        IReadOnlySet<Point> completedTargets)
    {
        int width = farm.Map.Layers[0].LayerWidth;
        int height = farm.Map.Layers[0].LayerHeight;
        int count = 0;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Point target = new(x, y);
                if (!completedTargets.Contains(target)
                    && IsDryCrop(farm, new Vector2(x, y)))
                    count++;
            }
        }

        return count;
    }

    public static FarmTaskRouteEdge ToEdge(Point target, Point interaction)
    {
        return new FarmTaskRouteEdge(
            new GridPoint(target.X, target.Y),
            new GridPoint(interaction.X, interaction.Y));
    }

    private static int GetFacingDirection(Point interaction, Point target)
    {
        if (target.X > interaction.X)
            return Game1.right;
        if (target.X < interaction.X)
            return Game1.left;
        if (target.Y > interaction.Y)
            return Game1.down;
        return Game1.up;
    }
}
