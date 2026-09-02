namespace EvilFarmOwner;

internal enum HarvestChestMatchKind
{
    ExactStack = 0,
    SameItem = 1,
    SameCategory = 2,
    Empty = 3,
    MixedFreeSlot = 4
}

internal readonly record struct HarvestChestContents(
    int ExactStackSlots,
    int SameItemSlots,
    int SameCategorySlots,
    int OccupiedSlots,
    int DistinctCategoryCount = 0)
{
    public int CategoryPurityBasisPoints => this.OccupiedSlots <= 0
        ? 0
        : this.SameCategorySlots * 10_000 / this.OccupiedSlots;

    public bool IsMixed => this.DistinctCategoryCount > 1;
}

internal static class HarvestChestClassification
{
    // A chest that holds only foreign items no longer becomes invisible to delivery.
    // It falls back to MixedFreeSlot so single-chest farms and mixed-category cargo
    // remain deliverable; misplaced stacks are re-sorted by the storage-sort stage.
    public static HarvestChestMatchKind Classify(HarvestChestContents contents)
    {
        if (contents.ExactStackSlots > 0)
            return HarvestChestMatchKind.ExactStack;
        if (contents.SameItemSlots > 0)
            return HarvestChestMatchKind.SameItem;
        if (contents.SameCategorySlots > 0)
            return HarvestChestMatchKind.SameCategory;
        return contents.OccupiedSlots == 0
            ? HarvestChestMatchKind.Empty
            : HarvestChestMatchKind.MixedFreeSlot;
    }
}

internal sealed record HarvestChestOption(
    GridPoint ChestTile,
    GridPoint InteractionTile,
    HarvestChestMatchKind MatchKind,
    int AcceptableCapacity,
    int RequestedStack,
    int TravelDistance,
    HarvestChestContents Contents = default)
{
    public bool CanFullyAccept => this.AcceptableCapacity >= this.RequestedStack;
}

internal readonly record struct HarvestChestRouteKey(
    GridPoint ChestTile,
    GridPoint InteractionTile);

internal static class HarvestChestRouteAttemptPolicy
{
    public static bool IsExcluded(
        GridPoint chestTile,
        GridPoint interactionTile,
        IReadOnlySet<GridPoint> attemptedChestTiles,
        IReadOnlySet<HarvestChestRouteKey> attemptedRoutes)
    {
        return attemptedChestTiles.Contains(chestTile)
            || attemptedRoutes.Contains(new HarvestChestRouteKey(chestTile, interactionTile));
    }
}

internal static class HarvestChestRanking
{
    public static IReadOnlyList<HarvestChestOption> Order(IEnumerable<HarvestChestOption> options)
    {
        return options
            .Where(option => option.CanFullyAccept)
            .OrderBy(option => option.MatchKind)
            .ThenByDescending(option => option.Contents.ExactStackSlots)
            .ThenByDescending(option => option.Contents.SameItemSlots)
            .ThenByDescending(option => option.Contents.CategoryPurityBasisPoints)
            .ThenByDescending(option => option.Contents.SameCategorySlots)
            .ThenByDescending(option => option.MatchKind == HarvestChestMatchKind.MixedFreeSlot
                && option.Contents.IsMixed
                ? 1
                : 0)
            .ThenByDescending(option => option.MatchKind is HarvestChestMatchKind.Empty
                    or HarvestChestMatchKind.MixedFreeSlot
                ? option.AcceptableCapacity
                : 0)
            .ThenBy(option => option.ChestTile.Y)
            .ThenBy(option => option.ChestTile.X)
            .ThenBy(option => option.TravelDistance)
            .ThenBy(option => option.InteractionTile.Y)
            .ThenBy(option => option.InteractionTile.X)
            .ToArray();
    }
}

internal sealed class HarvestTransferLedger
{
    private readonly HashSet<string> CompletedTransferIds = new(StringComparer.Ordinal);

    public bool TryApply(string transferId, Action transfer)
    {
        if (this.CompletedTransferIds.Contains(transferId))
            return false;

        transfer();
        this.CompletedTransferIds.Add(transferId);
        return true;
    }

    public IReadOnlyList<string> GetCompletedTransferIds()
    {
        return this.CompletedTransferIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }
}

internal static class HarvestTransferMath
{
    public static int GetDeliveredCount(int requestedStack, int remainingStack)
    {
        return Math.Max(0, requestedStack - Math.Clamp(remainingStack, 0, Math.Max(0, requestedStack)));
    }
}

internal static class HarvestPlacementAudit
{
    public static bool IsBalanced(
        int harvested,
        int playerInventory,
        int chest,
        int overflow,
        int quarantine,
        int dropped,
        int unresolved)
    {
        if (harvested < 0
            || playerInventory < 0
            || chest < 0
            || overflow < 0
            || quarantine < 0
            || dropped < 0
            || unresolved < 0)
            return false;
        return harvested == (long)playerInventory + chest + overflow + quarantine + dropped + unresolved;
    }
}
