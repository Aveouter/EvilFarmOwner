namespace EvilFarmOwner;

internal enum HarvestChestMatchKind
{
    ExactStack = 0,
    SameItem = 1,
    SameGroup = 2,
    AvailableCapacity = 3
}

internal sealed record HarvestChestOption(
    GridPoint ChestTile,
    GridPoint InteractionTile,
    HarvestChestMatchKind MatchKind,
    int AcceptableCapacity,
    int RequestedStack,
    int TravelDistance)
{
    public bool CanFullyAccept => this.AcceptableCapacity >= this.RequestedStack;
}

internal static class HarvestChestRanking
{
    public static IReadOnlyList<HarvestChestOption> Order(IEnumerable<HarvestChestOption> options)
    {
        return options
            .OrderBy(option => option.MatchKind)
            .ThenByDescending(option => option.CanFullyAccept)
            .ThenByDescending(option => option.AcceptableCapacity)
            .ThenBy(option => option.TravelDistance)
            .ThenBy(option => option.ChestTile.Y)
            .ThenBy(option => option.ChestTile.X)
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

internal enum HarvestFallbackDestination
{
    Chest,
    PersistentOverflow,
    VisibleGroundDrop
}

internal static class HarvestDeliveryFallback
{
    public static HarvestFallbackDestination Select(
        bool hasEligibleChest,
        bool persistentOverflowAvailable)
    {
        if (hasEligibleChest)
            return HarvestFallbackDestination.Chest;
        return persistentOverflowAvailable
            ? HarvestFallbackDestination.PersistentOverflow
            : HarvestFallbackDestination.VisibleGroundDrop;
    }
}
