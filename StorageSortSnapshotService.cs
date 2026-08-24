using System.Globalization;
using System.Xml.Serialization;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using SObject = StardewValley.Object;

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

internal sealed record StorageSortItemFingerprint(
    string QualifiedItemId,
    string RuntimeType,
    string RuntimeAssembly,
    int Category,
    int Quality,
    int Quantity,
    int MaximumStackSize,
    string SerializedXml)
{
    public static bool TryCreate(Item item, out StorageSortItemFingerprint? fingerprint)
    {
        fingerprint = null;
        try
        {
            Type runtimeType = item.GetType();
            string qualifiedItemId = item.QualifiedItemId;
            string? typeName = runtimeType.FullName;
            string? assemblyName = runtimeType.Assembly.GetName().Name;
            int maximumStackSize = item.maximumStackSize();
            if (string.IsNullOrWhiteSpace(qualifiedItemId)
                || string.IsNullOrWhiteSpace(typeName)
                || string.IsNullOrWhiteSpace(assemblyName)
                || item.Stack <= 0
                || maximumStackSize <= 0
                || item.Stack > maximumStackSize)
            {
                return false;
            }

            fingerprint = new StorageSortItemFingerprint(
                qualifiedItemId,
                typeName,
                assemblyName,
                item.Category,
                item.Quality,
                item.Stack,
                maximumStackSize,
                Serialize(item, runtimeType));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Matches(Item item)
    {
        return TryCreate(item, out StorageSortItemFingerprint? current)
            && current == this;
    }

    private static string Serialize(Item item, Type runtimeType)
    {
        XmlSerializer serializer = new(runtimeType);
        XmlSerializerNamespaces namespaces = new();
        namespaces.Add("", "");
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        serializer.Serialize(writer, item, namespaces);
        return writer.ToString();
    }
}

internal sealed record StorageSortChestFingerprint(
    GridPoint ChestTile,
    int Capacity,
    IReadOnlyList<StorageSortStackBinding> Stacks);

internal static class StorageSortSnapshotValidation
{
    public static bool HasSameChestSet(
        IEnumerable<GridPoint> expected,
        IEnumerable<GridPoint> current)
    {
        HashSet<GridPoint> expectedSet = expected.ToHashSet();
        HashSet<GridPoint> currentSet = current.ToHashSet();
        return expectedSet.SetEquals(currentSet);
    }

    public static bool IsChestUnchanged(
        StorageSortChestFingerprint expected,
        StorageSortChestFingerprint current)
    {
        return expected.ChestTile == current.ChestTile
            && expected.Capacity == current.Capacity
            && expected.Stacks.Count == current.Stacks.Count
            && expected.Stacks.SequenceEqual(current.Stacks);
    }
}

internal sealed class StorageSortRuntimePlan
{
    private readonly IReadOnlyDictionary<GridPoint, Chest> Chests;
    private readonly IReadOnlyDictionary<GridPoint, StorageSortChestFingerprint> ChestFingerprints;

    public StorageSortRuntimePlan(
        StorageSortPlan plan,
        IReadOnlyDictionary<GridPoint, Chest> chests,
        IReadOnlyDictionary<GridPoint, StorageSortChestFingerprint> chestFingerprints,
        IReadOnlyDictionary<string, StorageSortStackBinding> stackBindings)
    {
        this.Plan = plan;
        this.Chests = chests;
        this.ChestFingerprints = chestFingerprints;
        this.StackBindings = stackBindings;
    }

    public StorageSortPlan Plan { get; }

    public IReadOnlyDictionary<string, StorageSortStackBinding> StackBindings { get; }

    internal IReadOnlyDictionary<GridPoint, Chest> RuntimeChests => this.Chests;

    internal IReadOnlyDictionary<GridPoint, StorageSortChestFingerprint> InitialChestFingerprints =>
        this.ChestFingerprints;

    public bool TryValidateUnchanged(Farm farm, out StorageSortSnapshotFailure failure)
    {
        failure = StorageSortSnapshotFailure.None;
        Dictionary<GridPoint, Chest> currentChests = StorageSortSnapshotService.GetEligibleChests(farm);
        if (!StorageSortSnapshotValidation.HasSameChestSet(
                this.Chests.Keys,
                currentChests.Keys))
        {
            failure = StorageSortSnapshotFailure.FarmChanged;
            return false;
        }

        foreach ((GridPoint tile, Chest expectedChest) in this.Chests
                     .OrderBy(pair => pair.Key.Y)
                     .ThenBy(pair => pair.Key.X))
        {
            if (!currentChests.TryGetValue(tile, out Chest? currentChest)
                || !ReferenceEquals(currentChest, expectedChest)
                || !HarvestChestRouter.IsEligibleChest(currentChest))
            {
                failure = StorageSortSnapshotFailure.ChestChanged;
                return false;
            }

            if (StorageSortSnapshotService.IsLockedByAnotherPeer(currentChest))
            {
                failure = StorageSortSnapshotFailure.BusyChest;
                return false;
            }

            if (!StorageSortSnapshotService.TryCreateChestFingerprint(
                    tile,
                    currentChest,
                    out StorageSortChestFingerprint? currentFingerprint,
                    out failure)
                || currentFingerprint is null)
            {
                return false;
            }

            StorageSortChestFingerprint expected = this.ChestFingerprints[tile];
            if (!StorageSortSnapshotValidation.IsChestUnchanged(expected, currentFingerprint))
            {
                failure = StorageSortSnapshotFailure.ChestChanged;
                return false;
            }
        }

        return true;
    }
}

internal sealed record StorageSortRuntimePlanResult(
    StorageSortSnapshotFailure Failure,
    StorageSortRuntimePlan? RuntimePlan)
{
    public bool IsSuccess => this.Failure == StorageSortSnapshotFailure.None
        && this.RuntimePlan is not null;
}

internal static class StorageSortSnapshotService
{
    public static StorageSortRuntimePlanResult TryCreate(Farm farm)
    {
        return TryCreate(GetEligibleChests(farm));
    }

    internal static StorageSortRuntimePlanResult TryCreate(
        IReadOnlyDictionary<GridPoint, Chest> sourceChests)
    {
        Dictionary<GridPoint, Chest> chests = sourceChests.ToDictionary(
            pair => pair.Key,
            pair => pair.Value);
        if (chests.Count == 0)
        {
            return new StorageSortRuntimePlanResult(
                StorageSortSnapshotFailure.NoEligibleChest,
                RuntimePlan: null);
        }

        List<StorageSortChestSnapshot> snapshots = new(chests.Count);
        Dictionary<GridPoint, StorageSortChestFingerprint> fingerprints = new();
        Dictionary<string, StorageSortStackBinding> bindings = new(StringComparer.Ordinal);
        List<StackingClass> stackingClasses = new();
        foreach ((GridPoint tile, Chest chest) in chests
                     .OrderBy(pair => pair.Key.Y)
                     .ThenBy(pair => pair.Key.X))
        {
            if (IsLockedByAnotherPeer(chest))
            {
                return new StorageSortRuntimePlanResult(
                    StorageSortSnapshotFailure.BusyChest,
                    RuntimePlan: null);
            }

            if (!TryCreateChestSnapshot(
                    tile,
                    chest,
                    stackingClasses,
                    out StorageSortChestSnapshot? snapshot,
                    out StorageSortChestFingerprint? fingerprint,
                    out StorageSortSnapshotFailure failure)
                || snapshot is null
                || fingerprint is null)
            {
                return new StorageSortRuntimePlanResult(failure, RuntimePlan: null);
            }

            snapshots.Add(snapshot);
            fingerprints.Add(tile, fingerprint);
            foreach (StorageSortStackBinding binding in fingerprint.Stacks)
                bindings.Add(binding.StackId, binding);
        }

        StorageSortPlan plan = StorageSortPlanner.Create(snapshots);
        StorageSortSnapshotFailure planFailure = plan.Failure switch
        {
            StorageSortPlanFailure.None when plan.Transfers.Count == 0 =>
                StorageSortSnapshotFailure.NoTransfers,
            StorageSortPlanFailure.None => StorageSortSnapshotFailure.None,
            StorageSortPlanFailure.InsufficientCapacity =>
                StorageSortSnapshotFailure.InsufficientCapacity,
            StorageSortPlanFailure.NonConvergent => StorageSortSnapshotFailure.NonConvergent,
            _ => StorageSortSnapshotFailure.InvalidChest
        };
        if (planFailure != StorageSortSnapshotFailure.None)
            return new StorageSortRuntimePlanResult(planFailure, RuntimePlan: null);

        return new StorageSortRuntimePlanResult(
            StorageSortSnapshotFailure.None,
            new StorageSortRuntimePlan(plan, chests, fingerprints, bindings));
    }

    internal static Dictionary<GridPoint, Chest> GetEligibleChests(Farm farm)
    {
        Dictionary<GridPoint, Chest> chests = new();
        foreach (KeyValuePair<Vector2, SObject> pair in farm.objects.Pairs)
        {
            if (pair.Value is not Chest chest || !HarvestChestRouter.IsEligibleChest(chest))
                continue;

            GridPoint tile = new((int)pair.Key.X, (int)pair.Key.Y);
            chests.Add(tile, chest);
        }

        return chests;
    }

    internal static bool IsLockedByAnotherPeer(Chest chest)
    {
        return chest.GetMutex().IsLocked() && !chest.GetMutex().IsLockHeld();
    }

    internal static bool TryCreateChestFingerprint(
        GridPoint tile,
        Chest chest,
        out StorageSortChestFingerprint? fingerprint,
        out StorageSortSnapshotFailure failure)
    {
        return TryCreateChestSnapshot(
            tile,
            chest,
            new List<StackingClass>(),
            out _,
            out fingerprint,
            out failure);
    }

    private static bool TryCreateChestSnapshot(
        GridPoint tile,
        Chest chest,
        List<StackingClass> stackingClasses,
        out StorageSortChestSnapshot? snapshot,
        out StorageSortChestFingerprint? fingerprint,
        out StorageSortSnapshotFailure failure)
    {
        snapshot = null;
        fingerprint = null;
        failure = StorageSortSnapshotFailure.None;
        if (!HarvestChestRouter.IsEligibleChest(chest))
        {
            failure = StorageSortSnapshotFailure.InvalidChest;
            return false;
        }

        try
        {
            int capacity = chest.GetActualCapacity();
            List<StorageSortStackSnapshot> stacks = new();
            List<StorageSortStackBinding> bindings = new();
            for (int slot = 0; slot < chest.Items.Count; slot++)
            {
                Item? item = chest.Items[slot];
                if (item is null)
                    continue;

                if (!StorageSortItemFingerprint.TryCreate(
                        item,
                        out StorageSortItemFingerprint? itemFingerprint)
                    || itemFingerprint is null
                    || !TryGetStackingKey(item, stackingClasses, out string? stackingKey)
                    || stackingKey is null)
                {
                    failure = StorageSortSnapshotFailure.InvalidItem;
                    return false;
                }

                string stackId = $"{tile.X}:{tile.Y}:{slot}";
                stacks.Add(new StorageSortStackSnapshot(
                    stackId,
                    stackingKey,
                    itemFingerprint.QualifiedItemId,
                    itemFingerprint.Category,
                    itemFingerprint.Quantity,
                    itemFingerprint.MaximumStackSize));
                bindings.Add(new StorageSortStackBinding(
                    stackId,
                    tile,
                    slot,
                    itemFingerprint));
            }

            if (capacity < 0 || stacks.Count > capacity)
            {
                failure = StorageSortSnapshotFailure.InvalidChest;
                return false;
            }

            snapshot = new StorageSortChestSnapshot(tile, capacity, stacks);
            fingerprint = new StorageSortChestFingerprint(tile, capacity, bindings);
            return true;
        }
        catch
        {
            failure = StorageSortSnapshotFailure.InvalidChest;
            return false;
        }
    }

    private static bool TryGetStackingKey(
        Item item,
        List<StackingClass> stackingClasses,
        out string? stackingKey)
    {
        stackingKey = null;
        try
        {
            foreach (StackingClass stackingClass in stackingClasses)
            {
                bool compatibleWithEveryMember = stackingClass.Members.All(member =>
                    item.canStackWith(member) && member.canStackWith(item));
                if (compatibleWithEveryMember)
                {
                    stackingClass.Members.Add(item);
                    stackingKey = stackingClass.Key;
                    return true;
                }
            }

            stackingKey = $"stack-class-{stackingClasses.Count:D4}";
            stackingClasses.Add(new StackingClass(stackingKey, new List<Item> { item }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record StackingClass(string Key, List<Item> Members);
}
