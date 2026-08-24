using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using SObject = StardewValley.Object;

namespace EvilFarmOwner;

internal sealed record HarvestChestRoute(
    Chest Chest,
    Point ChestTile,
    Point InteractionTile,
    HarvestChestMatchKind MatchKind,
    int AcceptableCapacity,
    Stack<Point> Path);

internal sealed class HarvestChestRouter
{
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
        Item item,
        IReadOnlySet<Point> attemptedChestTiles,
        IReadOnlySet<HarvestChestRouteKey> attemptedRoutes)
    {
        if (!FarmNavigationMap.TryBuild(farm, worker, startTile, this.Monitor, out GridRouteMap? routes)
            || routes is null)
            return null;

        HashSet<GridPoint> attemptedChestGrids = attemptedChestTiles
            .Select(tile => new GridPoint(tile.X, tile.Y))
            .ToHashSet();
        List<(HarvestChestOption Option, Chest Chest, Stack<Point> Path)> candidates = new();
        foreach (KeyValuePair<Vector2, SObject> pair in farm.objects.Pairs)
        {
            if (pair.Value is not Chest chest || !IsEligibleChest(chest))
                continue;

            Point chestTile = new((int)pair.Key.X, (int)pair.Key.Y);
            GridPoint chestGrid = new(chestTile.X, chestTile.Y);

            int acceptableCapacity = GetAcceptableCapacity(chest, item);
            if (acceptableCapacity < item.Stack)
                continue;

            HarvestChestContents contents = GetContents(chest, item);
            HarvestChestMatchKind? matchKind = HarvestChestClassification.Classify(contents);
            if (!matchKind.HasValue)
                continue;

            foreach (Point offset in InteractionOffsets)
            {
                Point interaction = new(chestTile.X + offset.X, chestTile.Y + offset.Y);
                GridPoint interactionGrid = new(interaction.X, interaction.Y);
                if (HarvestChestRouteAttemptPolicy.IsExcluded(
                        chestGrid,
                        interactionGrid,
                        attemptedChestGrids,
                        attemptedRoutes)
                    || !routes.TryGetDistance(interactionGrid, out int distance)
                    || !routes.TryGetPath(interactionGrid, out IReadOnlyList<GridPoint> gridPath))
                    continue;

                candidates.Add((
                    new HarvestChestOption(
                        chestGrid,
                        new GridPoint(interaction.X, interaction.Y),
                        matchKind.Value,
                        acceptableCapacity,
                        item.Stack,
                        distance,
                        contents),
                    chest,
                    FarmNavigationMap.ToPath(gridPath)));
            }
        }

        HarvestChestOption? best = HarvestChestRanking.Order(
            candidates.Select(candidate => candidate.Option)).FirstOrDefault();
        if (best is null)
            return null;

        (HarvestChestOption Option, Chest Chest, Stack<Point> Path) selected =
            candidates.First(candidate => candidate.Option == best);
        return new HarvestChestRoute(
            selected.Chest,
            new Point(best.ChestTile.X, best.ChestTile.Y),
            new Point(best.InteractionTile.X, best.InteractionTile.Y),
            best.MatchKind,
            best.AcceptableCapacity,
            selected.Path);
    }

    public static bool IsEligibleChest(Chest chest)
    {
        return chest.GetType() == typeof(Chest)
            && chest.playerChest.Value
            && string.IsNullOrWhiteSpace(chest.GlobalInventoryId)
            && chest.SpecialChestType is Chest.SpecialChestTypes.None or Chest.SpecialChestTypes.BigChest;
    }

    public static bool HasEligibleChest(Farm farm)
    {
        return farm.objects.Values.Any(value => value is Chest chest && IsEligibleChest(chest));
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

    public static HarvestChestContents GetContents(Chest chest, Item incoming)
    {
        int exactStackSlots = 0;
        int sameItemSlots = 0;
        int sameCategorySlots = 0;
        int occupiedSlots = 0;
        foreach (Item? existing in chest.Items)
        {
            if (existing is null)
                continue;

            occupiedSlots++;
            if (existing.canStackWith(incoming))
                exactStackSlots++;
            if (existing.QualifiedItemId == incoming.QualifiedItemId)
                sameItemSlots++;
            if (existing.Category == incoming.Category)
                sameCategorySlots++;
        }

        return new HarvestChestContents(
            exactStackSlots,
            sameItemSlots,
            sameCategorySlots,
            occupiedSlots);
    }

}
