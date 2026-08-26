namespace EvilFarmOwner;

internal enum StorageSortSnapshotFailure
{
    None,
    NoEligibleChest,
    BusyChest,
    InvalidChest,
    InvalidItem,
    NoTransfers,
    InsufficientCapacity,
    NonConvergent,
    FarmChanged,
    ChestChanged
}

internal sealed record StorageSortStackBinding(
    string StackId,
    GridPoint ChestTile,
    int Slot,
    StorageSortItemFingerprint Fingerprint);

internal sealed partial record StorageSortItemFingerprint(
    string QualifiedItemId,
    string RuntimeType,
    string RuntimeAssembly,
    int Category,
    int Quality,
    int Quantity,
    int MaximumStackSize,
    string SerializedXml);

internal sealed record StorageSortChestFingerprint(
    GridPoint ChestTile,
    int Capacity,
    IReadOnlyList<StorageSortStackBinding> Stacks);

internal static class StorageSortSnapshotValidation
{
    public static bool HasSameChestSet(
        IEnumerable<GridPoint> expected,
        IEnumerable<GridPoint> current) =>
        expected.ToHashSet().SetEquals(current.ToHashSet());

    public static bool IsChestUnchanged(
        StorageSortChestFingerprint expected,
        StorageSortChestFingerprint current) =>
        expected.ChestTile == current.ChestTile
            && expected.Capacity == current.Capacity
            && expected.Stacks.Count == current.Stacks.Count
            && expected.Stacks.SequenceEqual(current.Stacks);
}
