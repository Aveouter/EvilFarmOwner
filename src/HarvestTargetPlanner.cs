using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Machines;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace EvilFarmOwner;

internal enum HarvestPlanFailure
{
    None,
    UnsupportedFarmMap,
    NoSafeArrivalTile,
    NoHarvestTarget,
    NoReachableTarget
}

internal enum HarvestTargetKind
{
    Crop,
    Tapper,
    FruitTree,
    Machine,
    CrabPot,
    FishPond,
    Bush
}

internal sealed record HarvestTargetPlan(
    HarvestTargetKind Kind,
    Point TargetTile,
    Point InteractionTile,
    int FacingDirection,
    Stack<Point> Path);

internal sealed record HarvestWorkPlan(
    Point ArrivalTile,
    FarmBoundarySide ArrivalSide,
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
    private const int MaximumArrivalPathChecksPerSide = 8;
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

    public HarvestPlanResult TryCreate(
        Farm farm,
        NPC worker,
        IReadOnlySet<FarmBoundarySide>? excludedArrivalSides = null)
    {
        int width = farm.Map.Layers[0].LayerWidth;
        int height = farm.Map.Layers[0].LayerHeight;
        if (width > MaximumSupportedMapDimension || height > MaximumSupportedMapDimension)
            return new HarvestPlanResult(null, HarvestPlanFailure.UnsupportedFarmMap);

        bool foundSafeArrival = false;
        Dictionary<FarmBoundarySide, int> checkedPathsBySide = new();
        HarvestPlanFailure lastFailure = HarvestPlanFailure.NoReachableTarget;
        foreach (GridPoint candidate in FarmEntranceSelection.OrderBoundaryArrivalCandidates(
                     width,
                     height,
                     farm.warps.Select(warp => new GridPoint(warp.X, warp.Y)),
                     excludedSides: excludedArrivalSides))
        {
            FarmBoundarySide arrivalSide = FarmEntranceSelection.GetNearestBoundarySide(width, height, candidate);
            int checkedOnSide = checkedPathsBySide.GetValueOrDefault(arrivalSide);
            if (checkedOnSide >= MaximumArrivalPathChecksPerSide)
                continue;

            Vector2 candidateTile = new(candidate.X, candidate.Y);
            if (farm.warps.Any(warp => warp.X == candidate.X && warp.Y == candidate.Y)
                || farm.doors.ContainsKey(new Point(candidate.X, candidate.Y))
                || !farm.CanSpawnCharacterHere(candidateTile))
                continue;

            foundSafeArrival = true;
            checkedPathsBySide[arrivalSide] = checkedOnSide + 1;

            Point arrivalTile = new(candidate.X, candidate.Y);
            HarvestTargetSearchResult firstTarget = this.TryFindNext(
                farm,
                worker,
                arrivalTile,
                arrivalTile,
                new HashSet<Point>(),
                new HashSet<FarmTaskRouteEdge>());
            if (firstTarget.IsSuccess && firstTarget.Target is { } firstPlan)
            {
                if (FarmNavigationMap.CanBeginPath(
                        farm,
                        worker,
                        arrivalTile,
                        firstPlan.Path,
                        out string firstStepFailure))
                {
                    return new HarvestPlanResult(
                        new HarvestWorkPlan(arrivalTile, arrivalSide, firstPlan),
                        HarvestPlanFailure.None);
                }

                this.Monitor.Log(
                    $"Rejected harvest arrival {arrivalTile} on {arrivalSide}: {firstStepFailure}.",
                    LogLevel.Trace);
                lastFailure = HarvestPlanFailure.NoSafeArrivalTile;
                continue;
            }

            lastFailure = firstTarget.Failure;
            if (lastFailure == HarvestPlanFailure.NoHarvestTarget)
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
        IReadOnlySet<FarmTaskRouteEdge> failedEdges,
        TravelObstacleLedger? obstacles = null)
    {
        int width = farm.Map.Layers[0].LayerWidth;
        int height = farm.Map.Layers[0].LayerHeight;
        bool builtRoutes = obstacles is null
            ? FarmNavigationMap.TryBuild(farm, worker, startTile, this.Monitor, out GridRouteMap? routes)
            : FarmNavigationMap.TryBuild(
                farm,
                worker,
                startTile,
                this.Monitor,
                farm.NameOrUniqueName,
                obstacles,
                out routes);
        if (!builtRoutes
            || routes is null
            || !routes.IsReachable(new GridPoint(arrivalTile.X, arrivalTile.Y)))
        {
            return new HarvestTargetSearchResult(
                null,
                HarvestPlanFailure.NoReachableTarget,
                CountRemainingHarvestTargets(farm, completedTargets));
        }

        List<FarmTaskRouteOption> reachable = new();
        Dictionary<Point, HarvestTargetKind> candidateTargets = new();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2 target = new(x, y);
                Point targetPoint = new(x, y);
                HarvestTargetKind? targetKind = GetSupportedTargetKind(farm, target);
                if (completedTargets.Contains(targetPoint) || targetKind is null)
                    continue;

                candidateTargets[targetPoint] = targetKind.Value;
                foreach (Point offset in InteractionOffsets)
                {
                    Point interaction = new(x + offset.X, y + offset.Y);
                    FarmTaskRouteEdge edge = WateringTargetPlanner.ToEdge(targetPoint, interaction);
                    GridPoint interactionGrid = new(interaction.X, interaction.Y);
                    if (failedEdges.Contains(edge)
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
            return new HarvestTargetSearchResult(null, HarvestPlanFailure.NoHarvestTarget, 0);

        IReadOnlyList<FarmTaskRouteOption> ordered = FarmTaskRouteSelection.Order(
            reachable);
        if (ordered.Count == 0)
        {
            return new HarvestTargetSearchResult(
                null,
                HarvestPlanFailure.NoReachableTarget,
                candidateTargets.Count);
        }

        FarmTaskRouteOption best = ordered[0];
        if (!routes.TryGetPath(best.Interaction, out IReadOnlyList<GridPoint> gridPath))
        {
            return new HarvestTargetSearchResult(
                null,
                HarvestPlanFailure.NoReachableTarget,
                candidateTargets.Count);
        }
        Stack<Point> bestPath = FarmNavigationMap.ToPath(gridPath);
        Point bestTarget = new(best.Target.X, best.Target.Y);
        Point bestInteraction = new(best.Interaction.X, best.Interaction.Y);
        return new HarvestTargetSearchResult(
            new HarvestTargetPlan(
                candidateTargets[bestTarget],
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

    public static bool IsReadySupportedTapper(GameLocation location, Vector2 tile)
    {
        bool attachedToTree = location.terrainFeatures.TryGetValue(tile, out TerrainFeature? feature)
            && feature is Tree;
        bool isTapper = location.objects.TryGetValue(tile, out StardewValley.Object? tapper)
            && tapper.IsTapper();
        bool hasOutput = tapper?.heldObject.Value is not null;
        bool readyForHarvest = tapper?.readyForHarvest.Value == true;
        return TapperHarvestSemantics.IsReadyTarget(
            isTapper,
            attachedToTree,
            hasOutput,
            readyForHarvest);
    }

    public static bool IsReadySupportedFruitTree(GameLocation location, Vector2 tile)
    {
        if (!location.terrainFeatures.TryGetValue(tile, out TerrainFeature? feature)
            || feature is not FruitTree tree)
            return false;

        int fruitSlots = tree.fruit.Count(item => item is not null);
        return FruitTreeHarvestSemantics.IsReadyTarget(
            tree.growthStage.Value,
            tree.stump.Value,
            fruitSlots);
    }

    public static bool IsReadySupportedMachine(GameLocation location, Vector2 tile)
    {
        if (!location.objects.TryGetValue(tile, out StardewValley.Object? machine))
            return false;

        MachineData? data = machine.GetMachineData();
        bool hasRecalculateOnCollectRule = data?.OutputRules?.Any(
            rule => rule.RecalculateOnCollect) == true;
        bool hasOutputCollectedRule = data?.OutputRules?.Any(rule =>
            rule.Triggers?.Any(trigger =>
                trigger.Trigger.HasFlag(MachineOutputTrigger.OutputCollected)) == true) == true;
        return MachineHarvestSemantics.IsReadyTarget(
            machine.GetType() == typeof(StardewValley.Object),
            int.TryParse(machine.ItemId, out _),
            machine.bigCraftable.Value,
            machine.readyForHarvest.Value,
            machine.heldObject.Value is StardewValley.Object
                and not StardewValley.Objects.Chest,
            data is not null,
            data?.IsIncubator == true,
            machine.IsTapper(),
            hasRecalculateOnCollectRule,
            hasOutputCollectedRule);
    }

    public static bool IsReadySupportedCrabPot(GameLocation location, Vector2 tile)
    {
        return location.objects.TryGetValue(tile, out StardewValley.Object? value)
            && value is CrabPot pot
            && CrabPotHarvestSemantics.IsReadyTarget(
                pot.tileIndexToShow == 714,
                pot.readyForHarvest.Value,
                pot.heldObject.Value is not null);
    }

    public static bool IsReadySupportedFishPond(GameLocation location, Vector2 tile)
    {
        if (location is not Farm farm)
            return false;

        Point point = new((int)tile.X, (int)tile.Y);
        FishPond? pond = farm.buildings.OfType<FishPond>()
            .FirstOrDefault(candidate => candidate.GetItemBucketTile().ToPoint() == point);
        return pond is not null
            && FishPondHarvestSemantics.IsReadyTarget(
                pond.daysOfConstructionLeft.Value <= 0,
                pond.daysUntilUpgrade.Value <= 0,
                pond.output.Value is not null);
    }

    public static bool IsReadySupportedBush(GameLocation location, Vector2 tile)
    {
        Bush? bush = location.largeTerrainFeatures.OfType<Bush>()
            .FirstOrDefault(candidate => candidate.Tile == tile);
        return bush is not null
            && BushHarvestSemantics.IsReadyTarget(
                location is Farm,
                bush.townBush.Value,
                bush.size.Value,
                bush.readyForHarvest(),
                bush.inBloom(),
                bush.GetShakeOffItem() is not null);
    }

    public static HarvestTargetKind? GetSupportedTargetKind(GameLocation location, Vector2 tile)
    {
        if (IsMatureSupportedCrop(location, tile))
            return HarvestTargetKind.Crop;
        if (IsReadySupportedTapper(location, tile))
            return HarvestTargetKind.Tapper;
        if (IsReadySupportedFruitTree(location, tile))
            return HarvestTargetKind.FruitTree;
        if (IsReadySupportedCrabPot(location, tile))
            return HarvestTargetKind.CrabPot;
        if (IsReadySupportedFishPond(location, tile))
            return HarvestTargetKind.FishPond;
        if (IsReadySupportedBush(location, tile))
            return HarvestTargetKind.Bush;
        if (IsReadySupportedMachine(location, tile))
            return HarvestTargetKind.Machine;
        return null;
    }

    public static int CountRemainingHarvestTargets(
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
                    && GetSupportedTargetKind(farm, new Vector2(x, y)) is not null)
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
