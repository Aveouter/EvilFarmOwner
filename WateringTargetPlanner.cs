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

internal sealed record WateringWorkPlan(
    Point ArrivalTile,
    Point TargetTile,
    Point InteractionTile,
    int FacingDirection);

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

        Point? arrivalTile = this.FindArrivalTile(farm, farm.GetMainFarmHouseEntry());
        if (arrivalTile is null)
            return new WateringPlanResult(null, WateringPlanFailure.NoSafeArrivalTile);

        List<WateringTargetOption> options = new();
        bool foundDryCrop = false;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2 target = new(x, y);
                if (!IsDryCrop(farm, target))
                    continue;

                foundDryCrop = true;
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

        if (!foundDryCrop)
            return new WateringPlanResult(null, WateringPlanFailure.NoDryCrop);

        GridPoint start = new(arrivalTile.Value.X, arrivalTile.Value.Y);
        foreach (WateringTargetOption option in WateringTargetSelection.Order(start, options))
        {
            Point interaction = new(option.Interaction.X, option.Interaction.Y);
            if (!this.HasPath(farm, worker, arrivalTile.Value, interaction)
                || !this.HasPath(farm, worker, interaction, arrivalTile.Value))
                continue;

            Point target = new(option.Target.X, option.Target.Y);
            return new WateringPlanResult(
                new WateringWorkPlan(
                    arrivalTile.Value,
                    target,
                    interaction,
                    GetFacingDirection(interaction, target)),
                WateringPlanFailure.None);
        }

        return new WateringPlanResult(null, WateringPlanFailure.NoReachableCrop);
    }

    public static bool IsDryCrop(GameLocation location, Vector2 tile)
    {
        return location.terrainFeatures.TryGetValue(tile, out TerrainFeature? feature)
            && feature is HoeDirt dirt
            && dirt.crop is not null
            && dirt.state.Value != HoeDirt.watered;
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
