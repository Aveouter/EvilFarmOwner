namespace EvilFarmOwner;

internal readonly record struct StorageSortLockPair(GridPoint First, GridPoint Second);

internal static class StorageSortTransferPolicy
{
    public static StorageSortLockPair GetLockOrder(GridPoint source, GridPoint destination) =>
        CompareTiles(source, destination) <= 0
            ? new StorageSortLockPair(source, destination)
            : new StorageSortLockPair(destination, source);

    public static bool IsExpectedTransfer(
        IReadOnlyList<StorageSortTransfer> transfers,
        int nextSequence,
        StorageSortTransfer candidate) =>
        nextSequence > 0 && nextSequence <= transfers.Count
            && candidate.Sequence == nextSequence
            && transfers[nextSequence - 1] == candidate;

    private static int CompareTiles(GridPoint left, GridPoint right)
    {
        int result = left.Y.CompareTo(right.Y);
        return result != 0 ? result : left.X.CompareTo(right.X);
    }
}

internal static class StorageSortTransferAudit
{
    public static bool IsConserved(
        int expected,
        int destination,
        int restoredSource,
        int quarantine,
        int unresolved)
    {
        if (expected < 0 || destination < 0 || restoredSource < 0
            || quarantine < 0 || unresolved < 0)
            return false;
        return expected == (long)destination + restoredSource + quarantine + unresolved;
    }
}

internal static class StorageSortRecoveryValidation
{
    public static bool IsSourceWithoutTransfer(
        string removedStackId,
        StorageSortChestFingerprint expected,
        StorageSortChestFingerprint actual)
    {
        if (string.IsNullOrWhiteSpace(removedStackId)
            || expected.ChestTile != actual.ChestTile
            || expected.Capacity != actual.Capacity
            || expected.Stacks.Count(binding => string.Equals(
                binding.StackId, removedStackId, StringComparison.Ordinal)) != 1)
            return false;
        StorageSortItemFingerprint[] expectedItems = expected.Stacks
            .Where(binding => !string.Equals(
                binding.StackId, removedStackId, StringComparison.Ordinal))
            .Select(binding => binding.Fingerprint).ToArray();
        StorageSortItemFingerprint[] actualItems = actual.Stacks
            .Select(binding => binding.Fingerprint).ToArray();
        return expectedItems.SequenceEqual(actualItems);
    }
}
