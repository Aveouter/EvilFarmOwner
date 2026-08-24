using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.TerrainFeatures;

namespace EvilFarmOwner;

internal enum HarvestPlanFailure
{
    None,
    UnsupportedFarmMap,
    NoSafeArrivalTile,
    NoMatureCrop,
    NoReachableCrop
}

internal sealed record HarvestTargetPlan(
    Point TargetTile,
    Point InteractionTile,
    int FacingDirection,
    Stack<Point> Path);

internal sealed record HarvestWorkPlan(
    Point ArrivalTile,
    HarvestTargetPlan FirstTarget);

internal sealed record HarvestTargetSearchResult(
    HarvestTargetPlan? Target,
    HarvestPlanFailure Failure,
    int CandidateTargetCount)
{
    public bool IsSuccess => this.Target is not null && this.Failure == HarvestPlanFailure.None;
}

internal sealed record HarvestPlanResult(
    HarvestWorkPlan? Plan,
    HarvestPlanFailure Failure)
{
    public bool IsSuccess => this.Plan is not null && this.Failure == HarvestPlanFailure.None;
}

internal sealed class HarvestTargetPlanner
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

    public HarvestTargetPlanner(IMonitor monitor)
    {
        this.Monitor = monitor;
    }

    public HarvestPlanResult TryCreate(Farm farm, NPC worker)
    {
        int width = farm.Map.Layers[0].LayerWidth;
        int height = farm.Map.Layers[0].LayerHeight;
        if (width > MaximumSupportedMapDimension || height > MaximumSupportedMapDimension)
            return new HarvestPlanResult(null, HarvestPlanFailure.UnsupportedFarmMap);

        bool foundSafeArrival = false;
        int checkedPaths = 0;
        HarvestPlanFailure lastFailure = HarvestPlanFailure.NoReachableCrop;
        foreach (GridPoint candidate in FarmEntranceSelection.OrderBoundaryArrivalCandidates(
                     width,
                     height,
                     farm.warps.Select(warp => new GridPoint(warp.X, warp.Y)),
                     requiredSide: FarmEntranceSelection.FixedWorkerEntranceSide))
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
            HarvestTargetSearchResult firstTarget = this.TryFindNext(
                farm,
                worker,
                arrivalTile,
                arrivalTile,
                new HashSet<Point>(),
                new HashSet<FarmTaskRouteEdge>());
            if (firstTarget.IsSuccess && firstTarget.Target is not null)
            {
                return new HarvestPlanResult(
                    new HarvestWorkPlan(arrivalTile, firstTarget.Target),
                    HarvestPlanFailure.None);
            }

            lastFailure = firstTarget.Failure;
            if (lastFailure == HarvestPlanFailure.NoMatureCrop)
                break;
        }

        return new HarvestPlanResult(
            null,
            foundSafeArrival ? lastFailure : HarvestPlanFailure.NoSafeArrivalTile);
    }

    public HarvestTargetSearchResult TryFindNext(
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
            return new HarvestTargetSearchResult(
                null,
                HarvestPlanFailure.NoReachableCrop,
                CountRemainingMatureCrops(farm, completedTargets));
        }

        List<FarmTaskRouteOption> reachable = new();
        HashSet<Point> candidateTargets = new();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2 target = new(x, y);
                Point targetPoint = new(x, y);
                if (completedTargets.Contains(targetPoint) || !IsMatureSupportedCrop(farm, target))
                    continue;

                candidateTargets.Add(targetPoint);
                foreach (Point offset in InteractionOffsets)
                {
                    Point interaction = new(x + offset.X, y + offset.Y);
                    FarmTaskRouteEdge edge = WateringTargetPlanner.ToEdge(targetPoint, interaction);
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
            return new HarvestTargetSearchResult(null, HarvestPlanFailure.NoMatureCrop, 0);

        IReadOnlyList<FarmTaskRouteOption> ordered = FarmTaskRouteSelection.Order(
            reachable);
        if (ordered.Count == 0)
        {
            return new HarvestTargetSearchResult(
                null,
                HarvestPlanFailure.NoReachableCrop,
                candidateTargets.Count);
        }

        FarmTaskRouteOption best = ordered[0];
        if (!routes.TryGetPath(best.Interaction, out IReadOnlyList<GridPoint> gridPath))
        {
            return new HarvestTargetSearchResult(
                null,
                HarvestPlanFailure.NoReachableCrop,
                candidateTargets.Count);
        }
        Stack<Point> bestPath = FarmNavigationMap.ToPath(gridPath);
        Point bestTarget = new(best.Target.X, best.Target.Y);
        Point bestInteraction = new(best.Interaction.X, best.Interaction.Y);
        return new HarvestTargetSearchResult(
            new HarvestTargetPlan(
                bestTarget,
                bestInteraction,
                GetFacingDirection(bestInteraction, bestTarget),
                bestPath),
            HarvestPlanFailure.None,
            candidateTargets.Count);
    }

    public static bool IsMatureSupportedCrop(GameLocation location, Vector2 tile)
    {
        return location.terrainFeatures.TryGetValue(tile, out TerrainFeature? feature)
            && feature is HoeDirt dirt
            && dirt.crop is { } crop
            && !crop.forageCrop.Value
            && !crop.dead.Value
            && !string.IsNullOrWhiteSpace(crop.indexOfHarvest.Value)
            && dirt.readyForHarvest();
    }

    public static int CountRemainingMatureCrops(
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
                    && IsMatureSupportedCrop(farm, new Vector2(x, y)))
                    count++;
            }
        }
        return count;
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
