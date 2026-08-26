namespace EvilFarmOwner;

internal sealed record StorageSortCompletedTransfer(
    int Sequence,
    string ItemId,
    string DisplayName,
    int Category,
    int Quality,
    int Quantity,
    GridPoint SourceChest,
    GridPoint DestinationChest);

internal static class StorageSortContractAudit
{
    public static bool IsReportBalanced(
        int plannedTransfers,
        IReadOnlyList<StorageSortCompletedTransfer> completed,
        IReadOnlyList<StorageSortCompletedTransfer> skipped,
        int movedItems,
        int persistedRecoveryItems)
    {
        if (plannedTransfers < 0 || movedItems < 0 || persistedRecoveryItems < 0
            || completed.Count + skipped.Count != plannedTransfers
            || completed.Sum(transfer => (long)transfer.Quantity) != movedItems
            || skipped.Sum(transfer => (long)transfer.Quantity) < persistedRecoveryItems)
            return false;
        int[] sequences = completed.Concat(skipped)
            .Select(transfer => transfer.Sequence).OrderBy(sequence => sequence).ToArray();
        return sequences.SequenceEqual(Enumerable.Range(1, plannedTransfers));
    }
}

internal static class StorageSortSaveBoundaryPolicy
{
    public static bool CanForceQuarantine(
        bool hasUnresolvedItem,
        bool unresolvedItemDetached,
        Guid transferId) =>
        hasUnresolvedItem && unresolvedItemDetached && transferId != Guid.Empty;
}

internal enum StorageSortRouteFailureDisposition
{
    AbortBeforeDetach,
    ResolveDetachedCargo
}

internal static class StorageSortRouteFailurePolicy
{
    public static StorageSortRouteFailureDisposition Decide(
        bool hasUnresolvedItem,
        bool unresolvedItemDetached) =>
        hasUnresolvedItem && unresolvedItemDetached
            ? StorageSortRouteFailureDisposition.ResolveDetachedCargo
            : StorageSortRouteFailureDisposition.AbortBeforeDetach;
}
