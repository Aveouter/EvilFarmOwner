using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.Pathfinding;
using SObject = StardewValley.Object;

namespace EvilFarmOwner;

internal sealed record HarvestChestRoute(
    Chest Chest,
    Point ChestTile,
    Point InteractionTile,
    HarvestChestMatchKind MatchKind);

internal sealed class HarvestChestRouter
{
    private const int MaximumPathSearchNodes = 10000;

    private static readonly Point[] InteractionOffsets =
    {
        new(0, 1),
        new(-1, 0),
        new(1, 0),
        new(0, -1)
    };

    private readonly IMonitor Monitor;

    public HarvestChestRouter(IMonitor monitor)
    {
        this.Monitor = monitor;
    }

    public HarvestChestRoute? FindBestRoute(
        Farm farm,
        NPC worker,
        Point startTile,
        Point returnTile,
        Item item,
        IReadOnlySet<Point> attemptedChestTiles)
    {
        List<(HarvestChestOption Option, Chest Chest)> candidates = new();
        foreach (KeyValuePair<Vector2, SObject> pair in farm.objects.Pairs)
        {
            if (pair.Value is not Chest chest || !IsEligibleChest(chest))
                continue;

            Point chestTile = new((int)pair.Key.X, (int)pair.Key.Y);
            if (attemptedChestTiles.Contains(chestTile)
                || (chest.GetMutex().IsLocked() && !chest.GetMutex().IsLockHeld()))
                continue;

            int acceptableCapacity = GetAcceptableCapacity(chest, item);
            if (acceptableCapacity <= 0)
                continue;

            HarvestChestMatchKind matchKind = GetMatchKind(chest, item);
            foreach (Point offset in InteractionOffsets)
            {
                Point interaction = new(chestTile.X + offset.X, chestTile.Y + offset.Y);
                bool alreadyStandingThere = interaction == startTile;
                if ((!alreadyStandingThere && !farm.CanSpawnCharacterHere(new Vector2(interaction.X, interaction.Y)))
                    || (!alreadyStandingThere && !this.HasPath(farm, worker, startTile, interaction))
                    || (interaction != returnTile && !this.HasPath(farm, worker, interaction, returnTile)))
                    continue;

                int distance = Math.Abs(startTile.X - interaction.X) + Math.Abs(startTile.Y - interaction.Y);
                candidates.Add((
                    new HarvestChestOption(
                        new GridPoint(chestTile.X, chestTile.Y),
                        new GridPoint(interaction.X, interaction.Y),
                        matchKind,
                        acceptableCapacity,
                        item.Stack,
                        distance),
                    chest));
            }
        }

        HarvestChestOption? best = HarvestChestRanking.Order(candidates.Select(candidate => candidate.Option)).FirstOrDefault();
        if (best is null)
            return null;

        Chest selectedChest = candidates.First(candidate => candidate.Option == best).Chest;
        return new HarvestChestRoute(
            selectedChest,
            new Point(best.ChestTile.X, best.ChestTile.Y),
            new Point(best.InteractionTile.X, best.InteractionTile.Y),
            best.MatchKind);
    }

    public static bool IsEligibleChest(Chest chest)
    {
        return chest.GetType() == typeof(Chest)
            && chest.playerChest.Value
            && string.IsNullOrWhiteSpace(chest.GlobalInventoryId)
            && chest.SpecialChestType is Chest.SpecialChestTypes.None or Chest.SpecialChestTypes.BigChest;
    }

    public static int GetAcceptableCapacity(Chest chest, Item incoming)
    {
        long capacity = 0;
        int occupiedSlots = 0;
        foreach (Item? existing in chest.Items)
        {
            if (existing is null)
                continue;

            occupiedSlots++;
            if (existing.canStackWith(incoming))
                capacity += Math.Max(0, existing.maximumStackSize() - existing.Stack);
        }

        int emptySlots = Math.Max(0, chest.GetActualCapacity() - occupiedSlots);
        capacity += (long)emptySlots * Math.Max(1, incoming.maximumStackSize());
        return (int)Math.Min(int.MaxValue, capacity);
    }

    public static HarvestChestMatchKind GetMatchKind(Chest chest, Item incoming)
    {
        if (chest.Items.Any(existing => existing is not null && existing.canStackWith(incoming)))
            return HarvestChestMatchKind.ExactStack;

        if (chest.Items.Any(existing => existing?.QualifiedItemId == incoming.QualifiedItemId))
            return HarvestChestMatchKind.SameItem;

        if (chest.Items.Any(existing => existing is not null && existing.Category == incoming.Category))
            return HarvestChestMatchKind.SameGroup;

        return HarvestChestMatchKind.AvailableCapacity;
    }

    private bool HasPath(Farm farm, NPC worker, Point start, Point end)
    {
        if (start == end)
            return true;

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
                $"Harvest delivery path failed closed for worker '{worker.Name}' from {start} to {end}: {ex.Message}",
                LogLevel.Warn);
            return false;
        }
    }
}
