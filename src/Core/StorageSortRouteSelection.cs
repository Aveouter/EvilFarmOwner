namespace EvilFarmOwner;

internal sealed record StorageSortInteractionOption(
    GridPoint InteractionTile,
    int Distance,
    int OffsetPriority);

internal static class StorageSortRouteSelection
{
    public static IEnumerable<StorageSortInteractionOption> Order(
        IEnumerable<StorageSortInteractionOption> options) =>
        options
            .OrderBy(option => option.Distance)
            .ThenBy(option => option.OffsetPriority)
            .ThenBy(option => option.InteractionTile.Y)
            .ThenBy(option => option.InteractionTile.X);
}
