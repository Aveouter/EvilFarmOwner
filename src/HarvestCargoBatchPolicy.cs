namespace EvilFarmOwner;

internal readonly record struct HarvestCargoDestination(
    string TransferId,
    GridPoint ChestTile);

internal static class HarvestCargoBatchPolicy
{
    public const int DeliveryStackThreshold = 12;

    public static bool ShouldDeliver(
        int carriedStacks,
        bool acquisitionClosed,
        bool noRemainingTarget)
    {
        return carriedStacks > 0
            && (carriedStacks >= DeliveryStackThreshold
                || acquisitionClosed
                || noRemainingTarget);
    }

    public static IReadOnlyList<string> SelectForChest(
        IEnumerable<HarvestCargoDestination> destinations,
        GridPoint chestTile)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        return destinations
            .Where(destination => destination.ChestTile == chestTile)
            .Select(destination => destination.TransferId)
            .ToArray();
    }
}
