using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;

namespace EvilFarmOwner;

internal enum StorageSortLockedTransferFailure
{
    None,
    InvalidSequence,
    MissingChest,
    SourceLockNotHeld,
    DestinationLockNotHeld,
    SourceChanged,
    DestinationChanged,
    SourceItemMissing,
    InsufficientCapacity,
    CommitFailed,
    RollbackFailed
}

internal sealed record StorageSortLockedTransferResult(
    StorageSortLockedTransferFailure Failure,
    int MovedItems,
    bool RequiresPersistentRecovery)
{
    public bool IsSuccess => this.Failure == StorageSortLockedTransferFailure.None;
}

internal readonly record struct StorageSortLockPair(GridPoint First, GridPoint Second);

internal static class StorageSortTransferPolicy
{
    public static StorageSortLockPair GetLockOrder(GridPoint source, GridPoint destination)
    {
        return CompareTiles(source, destination) <= 0
            ? new StorageSortLockPair(source, destination)
            : new StorageSortLockPair(destination, source);
    }

    public static bool IsExpectedTransfer(
        IReadOnlyList<StorageSortTransfer> transfers,
        int nextSequence,
        StorageSortTransfer candidate)
    {
        return nextSequence > 0
            && nextSequence <= transfers.Count
            && candidate.Sequence == nextSequence
            && transfers[nextSequence - 1] == candidate;
    }

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
        if (expected < 0
            || destination < 0
            || restoredSource < 0
            || quarantine < 0
            || unresolved < 0)
        {
            return false;
        }

        return expected == (long)destination + restoredSource + quarantine + unresolved;
    }
}

internal sealed class StorageSortExecutionSession
{
    private readonly StorageSortRuntimePlan RuntimePlan;
    private readonly Dictionary<GridPoint, StorageSortChestFingerprint> ExpectedChestFingerprints;
    private readonly Dictionary<string, Item> StackItems;

    private StorageSortExecutionSession(
        StorageSortRuntimePlan runtimePlan,
        Dictionary<GridPoint, StorageSortChestFingerprint> expectedChestFingerprints,
        Dictionary<string, Item> stackItems)
    {
        this.RuntimePlan = runtimePlan;
        this.ExpectedChestFingerprints = expectedChestFingerprints;
        this.StackItems = stackItems;
    }

    public int NextSequence { get; private set; } = 1;

    public static bool TryCreate(
        Farm farm,
        StorageSortRuntimePlan runtimePlan,
        out StorageSortExecutionSession? session,
        out StorageSortSnapshotFailure failure)
    {
        session = null;
        if (!runtimePlan.TryValidateUnchanged(farm, out failure))
            return false;

        Dictionary<string, Item> stackItems = new(StringComparer.Ordinal);
        foreach ((string stackId, StorageSortStackBinding binding) in runtimePlan.StackBindings)
        {
            if (!runtimePlan.RuntimeChests.TryGetValue(binding.ChestTile, out Chest? chest)
                || binding.Slot < 0
                || binding.Slot >= chest.Items.Count)
            {
                failure = StorageSortSnapshotFailure.ChestChanged;
                return false;
            }

            Item? item = chest.Items[binding.Slot];
            if (item is null || !binding.Fingerprint.Matches(item))
            {
                failure = StorageSortSnapshotFailure.ChestChanged;
                return false;
            }

            stackItems.Add(stackId, item);
        }

        session = new StorageSortExecutionSession(
            runtimePlan,
            runtimePlan.InitialChestFingerprints.ToDictionary(
                pair => pair.Key,
                pair => pair.Value),
            stackItems);
        failure = StorageSortSnapshotFailure.None;
        return true;
    }

    public StorageSortLockedTransferResult TryExecuteLocked(StorageSortTransfer transfer)
    {
        if (!StorageSortTransferPolicy.IsExpectedTransfer(
                this.RuntimePlan.Plan.Transfers,
                this.NextSequence,
                transfer))
        {
            return Failure(StorageSortLockedTransferFailure.InvalidSequence);
        }

        if (!this.RuntimePlan.RuntimeChests.TryGetValue(transfer.SourceChest, out Chest? source)
            || !this.RuntimePlan.RuntimeChests.TryGetValue(
                transfer.DestinationChest,
                out Chest? destination)
            || ReferenceEquals(source, destination))
        {
            return Failure(StorageSortLockedTransferFailure.MissingChest);
        }

        if (!source.GetMutex().IsLockHeld())
            return Failure(StorageSortLockedTransferFailure.SourceLockNotHeld);
        if (!destination.GetMutex().IsLockHeld())
            return Failure(StorageSortLockedTransferFailure.DestinationLockNotHeld);

        if (!this.IsChestExpected(transfer.SourceChest, source))
            return Failure(StorageSortLockedTransferFailure.SourceChanged);
        if (!this.IsChestExpected(transfer.DestinationChest, destination))
            return Failure(StorageSortLockedTransferFailure.DestinationChanged);

        if (!this.StackItems.TryGetValue(transfer.StackId, out Item? sourceItem)
            || sourceItem.Stack != transfer.Quantity
            || sourceItem.QualifiedItemId != transfer.ItemId
            || sourceItem.Category != transfer.Category)
        {
            return Failure(StorageSortLockedTransferFailure.SourceItemMissing);
        }

        int sourceSlot = source.Items.IndexOf(sourceItem);
        if (sourceSlot < 0)
            return Failure(StorageSortLockedTransferFailure.SourceItemMissing);
        if (GetSymmetricAcceptableCapacity(destination, sourceItem) < sourceItem.Stack)
            return Failure(StorageSortLockedTransferFailure.InsufficientCapacity);

        StorageSortChestFingerprint expectedSource = this.ExpectedChestFingerprints[transfer.SourceChest];
        StorageSortChestFingerprint expectedDestination =
            this.ExpectedChestFingerprints[transfer.DestinationChest];
        List<(Item Item, int Stack)> destinationStacks = destination.Items
            .Where(item => item is not null
                && item.canStackWith(sourceItem)
                && sourceItem.canStackWith(item))
            .Select(item => (item!, item!.Stack))
            .ToList();
        int originalQuantity = sourceItem.Stack;
        bool removedFromSource = false;
        bool addedToDestination = false;

        try
        {
            source.Items.RemoveAt(sourceSlot);
            removedFromSource = true;

            int remainder = originalQuantity;
            foreach ((Item existing, _) in destinationStacks)
            {
                int moved = Math.Min(
                    remainder,
                    Math.Max(0, existing.maximumStackSize() - existing.Stack));
                existing.Stack += moved;
                remainder -= moved;
                if (remainder == 0)
                    break;
            }

            if (remainder > 0)
            {
                if (destination.Items.Count >= destination.GetActualCapacity())
                    throw new InvalidOperationException("Destination lost the preflight empty slot.");

                sourceItem.Stack = remainder;
                destination.Items.Add(sourceItem);
                addedToDestination = true;
            }

            int destinationBefore = destinationStacks.Sum(entry => entry.Stack);
            int destinationAfter = destinationStacks.Sum(entry => entry.Item.Stack)
                + (addedToDestination ? sourceItem.Stack : 0);
            if (destinationAfter - destinationBefore != originalQuantity
                || source.Items.Contains(sourceItem)
                || (addedToDestination && !destination.Items.Contains(sourceItem)))
            {
                throw new InvalidOperationException("Locked transfer failed its immediate conservation audit.");
            }

            if (!TryCreateFingerprint(
                    transfer.SourceChest,
                    source,
                    out StorageSortChestFingerprint? committedSource)
                || !TryCreateFingerprint(
                    transfer.DestinationChest,
                    destination,
                    out StorageSortChestFingerprint? committedDestination)
                || committedSource is null
                || committedDestination is null)
            {
                throw new InvalidOperationException("Locked transfer could not fingerprint its committed state.");
            }

            this.ExpectedChestFingerprints[transfer.SourceChest] = committedSource;
            this.ExpectedChestFingerprints[transfer.DestinationChest] = committedDestination;
            if (!addedToDestination)
                this.StackItems.Remove(transfer.StackId);
            this.NextSequence++;
            return new StorageSortLockedTransferResult(
                StorageSortLockedTransferFailure.None,
                originalQuantity,
                RequiresPersistentRecovery: false);
        }
        catch
        {
            bool rolledBack = this.TryRollback(
                source,
                destination,
                sourceItem,
                sourceSlot,
                originalQuantity,
                destinationStacks,
                removedFromSource,
                addedToDestination,
                expectedSource,
                expectedDestination);
            return rolledBack
                ? Failure(StorageSortLockedTransferFailure.CommitFailed)
                : new StorageSortLockedTransferResult(
                    StorageSortLockedTransferFailure.RollbackFailed,
                    MovedItems: 0,
                    RequiresPersistentRecovery: true);
        }
    }

    private bool IsChestExpected(GridPoint tile, Chest chest)
    {
        return StorageSortSnapshotService.TryCreateChestFingerprint(
                tile,
                chest,
                out StorageSortChestFingerprint? current,
                out _)
            && current is not null
            && StorageSortSnapshotValidation.IsChestUnchanged(
                this.ExpectedChestFingerprints[tile],
                current);
    }

    private static bool TryCreateFingerprint(
        GridPoint tile,
        Chest chest,
        out StorageSortChestFingerprint? fingerprint)
    {
        return StorageSortSnapshotService.TryCreateChestFingerprint(
                tile,
                chest,
                out fingerprint,
                out _)
            && fingerprint is not null;
    }

    private static int GetSymmetricAcceptableCapacity(Chest chest, Item incoming)
    {
        long capacity = 0;
        int occupiedSlots = 0;
        foreach (Item? existing in chest.Items)
        {
            if (existing is null)
                continue;

            occupiedSlots++;
            if (existing.canStackWith(incoming) && incoming.canStackWith(existing))
                capacity += Math.Max(0, existing.maximumStackSize() - existing.Stack);
        }

        int emptySlots = Math.Max(0, chest.GetActualCapacity() - occupiedSlots);
        capacity += (long)emptySlots * Math.Max(1, incoming.maximumStackSize());
        return (int)Math.Min(int.MaxValue, capacity);
    }

    private bool TryRollback(
        Chest source,
        Chest destination,
        Item sourceItem,
        int sourceSlot,
        int originalQuantity,
        IReadOnlyList<(Item Item, int Stack)> destinationStacks,
        bool removedFromSource,
        bool addedToDestination,
        StorageSortChestFingerprint expectedSource,
        StorageSortChestFingerprint expectedDestination)
    {
        try
        {
            if (addedToDestination)
                destination.Items.Remove(sourceItem);
            foreach ((Item item, int stack) in destinationStacks)
                item.Stack = stack;

            sourceItem.Stack = originalQuantity;
            if (removedFromSource && !source.Items.Contains(sourceItem))
            {
                if (sourceSlot <= source.Items.Count)
                    source.Items.Insert(sourceSlot, sourceItem);
                else
                    source.Items.Add(sourceItem);
            }

            bool sourceRestored = StorageSortSnapshotService.TryCreateChestFingerprint(
                    expectedSource.ChestTile,
                    source,
                    out StorageSortChestFingerprint? actualSource,
                    out _)
                && actualSource is not null
                && StorageSortSnapshotValidation.IsChestUnchanged(expectedSource, actualSource);
            bool destinationRestored = StorageSortSnapshotService.TryCreateChestFingerprint(
                    expectedDestination.ChestTile,
                    destination,
                    out StorageSortChestFingerprint? actualDestination,
                    out _)
                && actualDestination is not null
                && StorageSortSnapshotValidation.IsChestUnchanged(
                    expectedDestination,
                    actualDestination);
            return sourceRestored && destinationRestored;
        }
        catch
        {
            return false;
        }
    }

    private static StorageSortLockedTransferResult Failure(
        StorageSortLockedTransferFailure failure)
    {
        return new StorageSortLockedTransferResult(
            failure,
            MovedItems: 0,
            RequiresPersistentRecovery: false);
    }
}
