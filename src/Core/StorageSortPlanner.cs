namespace EvilFarmOwner;

internal enum StorageSortMatchKind
{
    ExactStack = 0,
    SameItem = 1,
    SameCategory = 2,
    SourceAnchor = 3,
    Empty = 4,
    Incompatible = 5
}

internal enum StorageSortPlanFailure
{
    None,
    InvalidSnapshot,
    InsufficientCapacity,
    NonConvergent
}

internal sealed record StorageSortStackSnapshot(
    string StackId,
    string StackingKey,
    string ItemId,
    int Category,
    int Quantity,
    int MaximumStackSize);

internal sealed record StorageSortChestSnapshot(
    GridPoint ChestTile,
    int Capacity,
    IReadOnlyList<StorageSortStackSnapshot> Stacks);

internal sealed record StorageSortTransfer(
    int Sequence,
    GridPoint SourceChest,
    GridPoint DestinationChest,
    string StackId,
    string StackingKey,
    string ItemId,
    int Category,
    int Quantity);

internal sealed record StorageSortPlan(
    StorageSortPlanFailure Failure,
    IReadOnlyList<StorageSortTransfer> Transfers,
    IReadOnlyList<StorageSortChestSnapshot> ResultChests)
{
    public bool CanExecute => this.Failure == StorageSortPlanFailure.None;
}

internal static class StorageSortPlanner
{
    public static StorageSortPlan Create(IReadOnlyList<StorageSortChestSnapshot> chests)
    {
        if (!TryCreateSimulation(chests, out List<SimulatedChest>? simulated)
            || simulated is null)
        {
            return Failure(StorageSortPlanFailure.InvalidSnapshot, chests);
        }

        List<StorageSortChestSnapshot> original = CreateSnapshots(simulated);
        List<StorageSortTransfer> transfers = new();
        HashSet<string> observedStates = new(StringComparer.Ordinal)
        {
            CreateStateKey(simulated)
        };
        int sourceStackCount = simulated.Sum(chest => chest.Stacks.Count);
        int maximumMoves = Math.Max(1, sourceStackCount * Math.Max(2, simulated.Count) * 8);

        while (true)
        {
            bool movedInPass = false;
            string[] stackIds = simulated
                .OrderBy(chest => chest.Tile.Y)
                .ThenBy(chest => chest.Tile.X)
                .SelectMany(chest => chest.Stacks.OrderBy(stack => stack.Order))
                .Select(stack => stack.StackId)
                .ToArray();

            foreach (string stackId in stackIds)
            {
                if (!TryFindStack(simulated, stackId, out SimulatedChest? source, out SimulatedStack? stack)
                    || source is null
                    || stack is null)
                {
                    continue;
                }

                SortScore sourceScore = GetSourceScore(source, stack);
                DestinationOption? destination = simulated
                    .Where(chest => !ReferenceEquals(chest, source))
                    .Select(chest => CreateDestinationOption(chest, stack))
                    .Where(option => option is not null)
                    .Select(option => option!)
                    .OrderBy(option => option.Score, SortScoreComparer.Instance)
                    .FirstOrDefault();

                if (destination is null)
                {
                    if (sourceScore.MatchKind == StorageSortMatchKind.Incompatible)
                        return Failure(StorageSortPlanFailure.InsufficientCapacity, original);
                    continue;
                }

                if (SortScoreComparer.Instance.Compare(destination.Score, sourceScore) >= 0)
                {
                    if (sourceScore.MatchKind == StorageSortMatchKind.Incompatible)
                        return Failure(StorageSortPlanFailure.InsufficientCapacity, original);
                    continue;
                }

                int quantity = stack.Quantity;
                GridPoint sourceTile = source.Tile;
                RemoveStack(source, stack);
                if (!TryInsertWholeStack(destination.Chest, stack))
                    return Failure(StorageSortPlanFailure.InsufficientCapacity, original);

                transfers.Add(new StorageSortTransfer(
                    transfers.Count + 1,
                    sourceTile,
                    destination.Chest.Tile,
                    stack.StackId,
                    stack.StackingKey,
                    stack.ItemId,
                    stack.Category,
                    quantity));
                movedInPass = true;

                if (transfers.Count > maximumMoves)
                    return Failure(StorageSortPlanFailure.NonConvergent, original);
            }

            if (!movedInPass)
                return new StorageSortPlan(StorageSortPlanFailure.None, transfers, CreateSnapshots(simulated));

            string state = CreateStateKey(simulated);
            if (!observedStates.Add(state))
                return Failure(StorageSortPlanFailure.NonConvergent, original);
        }
    }

    private static StorageSortPlan Failure(
        StorageSortPlanFailure failure,
        IReadOnlyList<StorageSortChestSnapshot>? chests)
    {
        return new StorageSortPlan(
            failure,
            Array.Empty<StorageSortTransfer>(),
            chests?.OfType<StorageSortChestSnapshot>().Select(CloneSnapshot).ToArray()
                ?? Array.Empty<StorageSortChestSnapshot>());
    }

    private static StorageSortChestSnapshot CloneSnapshot(StorageSortChestSnapshot chest)
    {
        return new StorageSortChestSnapshot(
            chest.ChestTile,
            chest.Capacity,
            chest.Stacks?.OfType<StorageSortStackSnapshot>().Select(stack => stack with { }).ToArray()
                ?? Array.Empty<StorageSortStackSnapshot>());
    }

    private static bool TryCreateSimulation(
        IReadOnlyList<StorageSortChestSnapshot>? chests,
        out List<SimulatedChest>? simulated)
    {
        simulated = null;
        if (chests is null)
            return false;

        HashSet<GridPoint> tiles = new();
        HashSet<string> stackIds = new(StringComparer.Ordinal);
        List<SimulatedChest> result = new(chests.Count);
        long order = 0;
        foreach (StorageSortChestSnapshot? chest in chests)
        {
            if (chest is null
                || chest.Capacity < 0
                || chest.Stacks is null
                || chest.Stacks.Count > chest.Capacity
                || !tiles.Add(chest.ChestTile))
            {
                return false;
            }

            List<SimulatedStack> stacks = new(chest.Stacks.Count);
            foreach (StorageSortStackSnapshot? stack in chest.Stacks)
            {
                if (stack is null
                    || string.IsNullOrWhiteSpace(stack.StackId)
                    || string.IsNullOrWhiteSpace(stack.StackingKey)
                    || string.IsNullOrWhiteSpace(stack.ItemId)
                    || stack.Quantity <= 0
                    || stack.MaximumStackSize <= 0
                    || stack.Quantity > stack.MaximumStackSize
                    || !stackIds.Add(stack.StackId))
                {
                    return false;
                }

                stacks.Add(new SimulatedStack(
                    stack.StackId,
                    stack.StackingKey,
                    stack.ItemId,
                    stack.Category,
                    stack.Quantity,
                    stack.MaximumStackSize,
                    order++));
            }

            result.Add(new SimulatedChest(
                chest.ChestTile,
                chest.Capacity,
                GetDominantCategory(stacks),
                stacks));
        }

        simulated = result;
        return true;
    }

    private static DestinationOption? CreateDestinationOption(
        SimulatedChest chest,
        SimulatedStack incoming)
    {
        int acceptableCapacity = GetAcceptableCapacity(chest, incoming);
        if (acceptableCapacity < incoming.Quantity)
            return null;

        StorageSortContents contents = GetContents(chest, incoming, excludedStackId: null);
        StorageSortMatchKind? matchKind = ClassifyDestination(chest, incoming, contents);
        if (!matchKind.HasValue)
            return null;

        return new DestinationOption(
            chest,
            CreateScore(
                chest,
                matchKind.Value,
                contents,
                acceptableCapacity - incoming.Quantity));
    }

    private static SortScore GetSourceScore(SimulatedChest source, SimulatedStack incoming)
    {
        StorageSortContents contents = GetContents(source, incoming, incoming.StackId);
        StorageSortMatchKind matchKind;
        if (source.PurposeCategory != incoming.Category)
        {
            matchKind = StorageSortMatchKind.Incompatible;
        }
        else if (contents.ExactStackSlots > 0)
        {
            matchKind = StorageSortMatchKind.ExactStack;
        }
        else if (contents.SameItemSlots > 0)
        {
            matchKind = StorageSortMatchKind.SameItem;
        }
        else if (contents.SameCategorySlots > 0)
        {
            matchKind = StorageSortMatchKind.SameCategory;
        }
        else
        {
            matchKind = StorageSortMatchKind.SourceAnchor;
        }

        int capacityWithoutIncoming = GetAcceptableCapacity(source, incoming, incoming.StackId);
        return CreateScore(
            source,
            matchKind,
            contents,
            Math.Max(0, capacityWithoutIncoming - incoming.Quantity));
    }

    private static StorageSortMatchKind? ClassifyDestination(
        SimulatedChest chest,
        SimulatedStack incoming,
        StorageSortContents contents)
    {
        if (chest.PurposeCategory.HasValue
            && chest.PurposeCategory.Value != incoming.Category)
        {
            return null;
        }
        if (contents.ExactStackSlots > 0)
            return StorageSortMatchKind.ExactStack;
        if (contents.SameItemSlots > 0)
            return StorageSortMatchKind.SameItem;
        if (contents.OccupiedSlots == 0)
            return StorageSortMatchKind.Empty;
        return chest.PurposeCategory == incoming.Category
            ? StorageSortMatchKind.SameCategory
            : null;
    }

    private static SortScore CreateScore(
        SimulatedChest chest,
        StorageSortMatchKind matchKind,
        StorageSortContents contents,
        int remainingCapacity)
    {
        return new SortScore(
            matchKind,
            contents.ExactStackSlots,
            contents.SameItemSlots,
            contents.CategoryPurityBasisPoints,
            contents.SameCategorySlots,
            remainingCapacity,
            chest.Tile);
    }

    private static StorageSortContents GetContents(
        SimulatedChest chest,
        SimulatedStack incoming,
        string? excludedStackId)
    {
        int exactStackSlots = 0;
        int sameItemSlots = 0;
        int sameCategorySlots = 0;
        int occupiedSlots = 0;
        foreach (SimulatedStack existing in chest.Stacks)
        {
            if (string.Equals(existing.StackId, excludedStackId, StringComparison.Ordinal))
                continue;

            occupiedSlots++;
            if (string.Equals(existing.StackingKey, incoming.StackingKey, StringComparison.Ordinal))
                exactStackSlots++;
            if (string.Equals(existing.ItemId, incoming.ItemId, StringComparison.Ordinal))
                sameItemSlots++;
            if (existing.Category == incoming.Category)
                sameCategorySlots++;
        }

        return new StorageSortContents(
            exactStackSlots,
            sameItemSlots,
            sameCategorySlots,
            occupiedSlots);
    }

    private static int? GetDominantCategory(IEnumerable<SimulatedStack> stacks)
    {
        return stacks
            .GroupBy(stack => stack.Category)
            .Select(group => new
            {
                Category = group.Key,
                Quantity = group.Sum(stack => (long)stack.Quantity),
                Slots = group.Count()
            })
            .OrderByDescending(group => group.Quantity)
            .ThenByDescending(group => group.Slots)
            .ThenBy(group => group.Category)
            .Select(group => group.Category)
            .Cast<int?>()
            .FirstOrDefault();
    }

    private static int GetAcceptableCapacity(
        SimulatedChest chest,
        SimulatedStack incoming,
        string? excludedStackId = null)
    {
        long capacity = 0;
        int occupiedSlots = 0;
        foreach (SimulatedStack existing in chest.Stacks)
        {
            if (string.Equals(existing.StackId, excludedStackId, StringComparison.Ordinal))
                continue;

            occupiedSlots++;
            if (string.Equals(existing.StackingKey, incoming.StackingKey, StringComparison.Ordinal))
                capacity += Math.Max(0, existing.MaximumStackSize - existing.Quantity);
        }

        int emptySlots = Math.Max(0, chest.Capacity - occupiedSlots);
        capacity += (long)emptySlots * incoming.MaximumStackSize;
        return (int)Math.Min(int.MaxValue, capacity);
    }

    private static void RemoveStack(SimulatedChest source, SimulatedStack stack)
    {
        if (!source.Stacks.Remove(stack))
            throw new InvalidOperationException("Storage sort simulation lost its source stack.");
    }

    private static bool TryInsertWholeStack(SimulatedChest destination, SimulatedStack incoming)
    {
        int remainder = incoming.Quantity;
        foreach (SimulatedStack existing in destination.Stacks
                     .Where(existing => string.Equals(
                         existing.StackingKey,
                         incoming.StackingKey,
                         StringComparison.Ordinal))
                     .OrderBy(existing => existing.Order))
        {
            int moved = Math.Min(remainder, Math.Max(0, existing.MaximumStackSize - existing.Quantity));
            existing.Quantity += moved;
            remainder -= moved;
            if (remainder == 0)
                return true;
        }

        if (remainder <= 0)
            return true;
        if (destination.Stacks.Count >= destination.Capacity
            || remainder > incoming.MaximumStackSize)
        {
            return false;
        }

        destination.PurposeCategory ??= incoming.Category;
        destination.Stacks.Add(new SimulatedStack(
            incoming.StackId,
            incoming.StackingKey,
            incoming.ItemId,
            incoming.Category,
            remainder,
            incoming.MaximumStackSize,
            incoming.Order));
        return true;
    }

    private static bool TryFindStack(
        IEnumerable<SimulatedChest> chests,
        string stackId,
        out SimulatedChest? chest,
        out SimulatedStack? stack)
    {
        foreach (SimulatedChest candidate in chests)
        {
            SimulatedStack? found = candidate.Stacks.FirstOrDefault(item =>
                string.Equals(item.StackId, stackId, StringComparison.Ordinal));
            if (found is null)
                continue;

            chest = candidate;
            stack = found;
            return true;
        }

        chest = null;
        stack = null;
        return false;
    }

    private static List<StorageSortChestSnapshot> CreateSnapshots(IEnumerable<SimulatedChest> chests)
    {
        return chests
            .OrderBy(chest => chest.Tile.Y)
            .ThenBy(chest => chest.Tile.X)
            .Select(chest => new StorageSortChestSnapshot(
                chest.Tile,
                chest.Capacity,
                chest.Stacks
                    .OrderBy(stack => stack.Order)
                    .Select(stack => new StorageSortStackSnapshot(
                        stack.StackId,
                        stack.StackingKey,
                        stack.ItemId,
                        stack.Category,
                        stack.Quantity,
                        stack.MaximumStackSize))
                    .ToArray()))
            .ToList();
    }

    private static string CreateStateKey(IEnumerable<SimulatedChest> chests)
    {
        System.Text.StringBuilder key = new();
        foreach (SimulatedChest chest in chests
                     .OrderBy(chest => chest.Tile.Y)
                     .ThenBy(chest => chest.Tile.X))
        {
            key.Append(chest.Tile.X)
                .Append(',')
                .Append(chest.Tile.Y)
                .Append(',')
                .Append(chest.PurposeCategory)
                .Append(':');
            foreach (SimulatedStack stack in chest.Stacks.OrderBy(stack => stack.Order))
            {
                AppendLengthPrefixed(key, stack.StackId);
                AppendLengthPrefixed(key, stack.StackingKey);
                AppendLengthPrefixed(key, stack.ItemId);
                key.Append(stack.Category)
                    .Append(',')
                    .Append(stack.Quantity)
                    .Append(',')
                    .Append(stack.MaximumStackSize)
                    .Append(';');
            }

            key.Append('|');
        }

        return key.ToString();
    }

    private static void AppendLengthPrefixed(System.Text.StringBuilder target, string value)
    {
        target.Append(value.Length).Append(':').Append(value).Append(',');
    }

    private readonly record struct StorageSortContents(
        int ExactStackSlots,
        int SameItemSlots,
        int SameCategorySlots,
        int OccupiedSlots)
    {
        public int CategoryPurityBasisPoints => this.OccupiedSlots <= 0
            ? 0
            : this.SameCategorySlots * 10_000 / this.OccupiedSlots;
    }

    private readonly record struct SortScore(
        StorageSortMatchKind MatchKind,
        int ExactStackSlots,
        int SameItemSlots,
        int CategoryPurityBasisPoints,
        int SameCategorySlots,
        int RemainingCapacity,
        GridPoint ChestTile);

    private sealed class SortScoreComparer : IComparer<SortScore>
    {
        public static SortScoreComparer Instance { get; } = new();

        public int Compare(SortScore left, SortScore right)
        {
            int result = left.MatchKind.CompareTo(right.MatchKind);
            if (result != 0)
                return result;
            result = right.ExactStackSlots.CompareTo(left.ExactStackSlots);
            if (result != 0)
                return result;
            result = right.SameItemSlots.CompareTo(left.SameItemSlots);
            if (result != 0)
                return result;
            result = right.CategoryPurityBasisPoints.CompareTo(left.CategoryPurityBasisPoints);
            if (result != 0)
                return result;
            result = right.SameCategorySlots.CompareTo(left.SameCategorySlots);
            if (result != 0)
                return result;
            result = right.RemainingCapacity.CompareTo(left.RemainingCapacity);
            if (result != 0)
                return result;
            result = left.ChestTile.Y.CompareTo(right.ChestTile.Y);
            return result != 0
                ? result
                : left.ChestTile.X.CompareTo(right.ChestTile.X);
        }
    }

    private sealed record DestinationOption(SimulatedChest Chest, SortScore Score);

    private sealed class SimulatedChest
    {
        public SimulatedChest(
            GridPoint tile,
            int capacity,
            int? purposeCategory,
            List<SimulatedStack> stacks)
        {
            this.Tile = tile;
            this.Capacity = capacity;
            this.PurposeCategory = purposeCategory;
            this.Stacks = stacks;
        }

        public GridPoint Tile { get; }

        public int Capacity { get; }

        public int? PurposeCategory { get; set; }

        public List<SimulatedStack> Stacks { get; }
    }

    private sealed class SimulatedStack
    {
        public SimulatedStack(
            string stackId,
            string stackingKey,
            string itemId,
            int category,
            int quantity,
            int maximumStackSize,
            long order)
        {
            this.StackId = stackId;
            this.StackingKey = stackingKey;
            this.ItemId = itemId;
            this.Category = category;
            this.Quantity = quantity;
            this.MaximumStackSize = maximumStackSize;
            this.Order = order;
        }

        public string StackId { get; }

        public string StackingKey { get; }

        public string ItemId { get; }

        public int Category { get; }

        public int Quantity { get; set; }

        public int MaximumStackSize { get; }

        public long Order { get; }
    }
}
