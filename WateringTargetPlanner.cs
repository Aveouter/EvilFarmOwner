using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Pathfinding;
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
    int FacingDirection);

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
    private const int ArrivalSearchRadius = 8;
    private const int MaximumPathSearchNodes = 10000;

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

        GridPoint entrance = FarmEntranceSelection.SelectLeftEntrance(
            width,
            height,
            farm.warps.Select(warp => new GridPoint(warp.X, warp.Y)));
        Point? arrivalTile = this.FindArrivalTile(farm, new Point(entrance.X, entrance.Y));
        if (arrivalTile is null)
            return new WateringPlanResult(null, WateringPlanFailure.NoSafeArrivalTile);

        WateringTargetSearchResult firstTarget = this.TryFindNext(
            farm,
            worker,
            arrivalTile.Value,
            arrivalTile.Value,
            new HashSet<Point>());
        if (!firstTarget.IsSuccess || firstTarget.Target is null)
            return new WateringPlanResult(null, firstTarget.Failure);

        return new WateringPlanResult(
            new WateringWorkPlan(arrivalTile.Value, firstTarget.Target),
            WateringPlanFailure.None);
    }

    public WateringTargetSearchResult TryFindNext(
        Farm farm,
        NPC worker,
        Point startTile,
        Point arrivalTile,
        IReadOnlySet<Point> attemptedTargets)
    {
        int width = farm.Map.Layers[0].LayerWidth;
        int height = farm.Map.Layers[0].LayerHeight;

        List<WateringTargetOption> options = new();
        HashSet<Point> candidateTargets = new();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2 target = new(x, y);
                Point targetPoint = new(x, y);
                if (attemptedTargets.Contains(targetPoint) || !IsDryCrop(farm, target))
                    continue;

                candidateTargets.Add(targetPoint);
                foreach (Point offset in InteractionOffsets)
                {
                    Vector2 interaction = new(x + offset.X, y + offset.Y);
                    if (!farm.CanSpawnCharacterHere(interaction))
                        continue;

                    options.Add(new WateringTargetOption(
                        new GridPoint(x, y),
                        new GridPoint((int)interaction.X, (int)interaction.Y)));
                }
            }
        }

        if (candidateTargets.Count == 0)
            return new WateringTargetSearchResult(null, WateringPlanFailure.NoDryCrop, 0);

        GridPoint start = new(startTile.X, startTile.Y);
        foreach (WateringTargetOption option in WateringTargetSelection.Order(start, options))
        {
            Point interaction = new(option.Interaction.X, option.Interaction.Y);
            if (!this.HasPath(farm, worker, startTile, interaction)
                || !this.HasPath(farm, worker, interaction, arrivalTile))
                continue;

            Point target = new(option.Target.X, option.Target.Y);
            return new WateringTargetSearchResult(
                new WateringTargetPlan(
                    target,
                    interaction,
                    GetFacingDirection(interaction, target)),
                WateringPlanFailure.None,
                candidateTargets.Count);
        }

        return new WateringTargetSearchResult(
            null,
            WateringPlanFailure.NoReachableCrop,
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
        IReadOnlySet<Point> attemptedTargets)
    {
        int width = farm.Map.Layers[0].LayerWidth;
        int height = farm.Map.Layers[0].LayerHeight;
        int count = 0;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Point target = new(x, y);
                if (!attemptedTargets.Contains(target)
                    && IsDryCrop(farm, new Vector2(x, y)))
                    count++;
            }
        }

        return count;
    }

    private Point? FindArrivalTile(Farm farm, Point center)
    {
        for (int distance = 0; distance <= ArrivalSearchRadius; distance++)
        {
            for (int yOffset = -distance; yOffset <= distance; yOffset++)
            {
                int xOffsetMagnitude = distance - Math.Abs(yOffset);
                int[] xOffsets = xOffsetMagnitude == 0
                    ? new[] { 0 }
                    : new[] { -xOffsetMagnitude, xOffsetMagnitude };

                foreach (int xOffset in xOffsets)
                {
                    Vector2 tile = new(center.X + xOffset, center.Y + yOffset);
                    if (farm.CanSpawnCharacterHere(tile))
                        return new Point((int)tile.X, (int)tile.Y);
                }
            }
        }

        return null;
    }

    private bool HasPath(Farm farm, NPC worker, Point start, Point end)
    {
        try
        {
            Stack<Point>? path = PathFindController.findPath(
                start,
                end,
                PathFindController.isAtEndPoint,
                farm,
                worker,
                MaximumPathSearchNodes);

            return path is { Count: > 0 };
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Path preflight failed closed for worker '{worker.Name}' from {start} to {end}: {ex.Message}",
                LogLevel.Warn);
            return false;
        }
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
