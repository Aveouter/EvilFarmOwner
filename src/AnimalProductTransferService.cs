using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.FarmAnimals;
using StardewValley.Locations;
using StardewValley.Objects;

namespace EvilFarmOwner;

internal enum AnimalProductTransferFailure
{
    None,
    SourceChanged,
    DestinationChanged,
    InsufficientCapacity,
    CommitFailed
}

internal static class AnimalProductCommitPolicy
{
    public static AnimalProductTransferFailure EvaluatePreflight(
        bool sourceUnchanged,
        bool destinationEligible,
        int acceptableCapacity,
        int requestedStack)
    {
        if (requestedStack <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedStack));
        if (!sourceUnchanged)
            return AnimalProductTransferFailure.SourceChanged;
        if (!destinationEligible)
            return AnimalProductTransferFailure.DestinationChanged;
        return acceptableCapacity >= requestedStack
            ? AnimalProductTransferFailure.None
            : AnimalProductTransferFailure.InsufficientCapacity;
    }
}

internal sealed record AnimalProductChestDestination(
    Chest Chest,
    Point Tile,
    HarvestChestMatchKind MatchKind,
    int AcceptableCapacity);

internal static class AnimalProductDestinationPlanner
{
    public static AnimalProductChestDestination? FindBestChest(Farm farm, Item item)
    {
        ArgumentNullException.ThrowIfNull(farm);
        ArgumentNullException.ThrowIfNull(item);

        List<(HarvestChestOption Option, Chest Chest)> candidates = new();
        foreach (KeyValuePair<Vector2, StardewValley.Object> pair in farm.objects.Pairs)
        {
            if (pair.Value is not Chest chest || !HarvestChestRouter.IsEligibleChest(chest))
                continue;

            int capacity = HarvestChestRouter.GetAcceptableCapacity(chest, item);
            if (capacity < item.Stack)
                continue;

            HarvestChestContents contents = HarvestChestRouter.GetContents(chest, item);
            HarvestChestMatchKind? match = HarvestChestClassification.Classify(contents);
            if (!match.HasValue)
                continue;

            GridPoint tile = new((int)pair.Key.X, (int)pair.Key.Y);
            candidates.Add((
                new HarvestChestOption(
                    tile,
                    tile,
                    match.Value,
                    capacity,
                    item.Stack,
                    TravelDistance: 0,
                    contents),
                chest));
        }

        HarvestChestOption? best = HarvestChestRanking.Order(
            candidates.Select(candidate => candidate.Option)).FirstOrDefault();
        if (best is null)
            return null;

        (HarvestChestOption Option, Chest Chest) selected =
            candidates.First(candidate => candidate.Option == best);
        return new AnimalProductChestDestination(
            selected.Chest,
            new Point(best.ChestTile.X, best.ChestTile.Y),
            best.MatchKind,
            best.AcceptableCapacity);
    }
}

internal sealed class AnimalProductTransferService
{
    public AnimalProductTransferFailure TryCommitToChest(
        AnimalHouse house,
        AnimalProductTargetPlan target,
        Chest chest,
        Farmer requester,
        out Item? delivered)
    {
        delivered = null;
        bool sourceUnchanged = TryCreateCurrentProduct(
            house, target, out Item? product, out FarmAnimal? animal);
        if (!sourceUnchanged || product is null)
            return AnimalProductTransferFailure.SourceChanged;
        bool destinationEligible = HarvestChestRouter.IsEligibleChest(chest)
            && HarvestChestClassification.Classify(
                HarvestChestRouter.GetContents(chest, product)).HasValue;
        AnimalProductTransferFailure preflight = AnimalProductCommitPolicy.EvaluatePreflight(
            sourceUnchanged,
            destinationEligible,
            HarvestChestRouter.GetAcceptableCapacity(chest, product),
            product.Stack);
        if (preflight != AnimalProductTransferFailure.None)
            return preflight;

        InventorySnapshot snapshot = InventorySnapshot.Capture(chest.Items);
        try
        {
            Item? remainder = chest.addItem(product);
            if (remainder is not null)
            {
                snapshot.Restore(chest.Items);
                return AnimalProductTransferFailure.InsufficientCapacity;
            }

            product.Stack = target.Stack;
            if (!TryCommitSource(house, target, animal, product, requester))
            {
                snapshot.Restore(chest.Items);
                return AnimalProductTransferFailure.SourceChanged;
            }

            delivered = product.getOne();
            delivered.Stack = target.Stack;
            delivered.Quality = target.Quality;
            return AnimalProductTransferFailure.None;
        }
        catch
        {
            snapshot.Restore(chest.Items);
            return AnimalProductTransferFailure.CommitFailed;
        }
    }

    public AnimalProductTransferFailure TryCommitToRequester(
        AnimalHouse house,
        AnimalProductTargetPlan target,
        Farmer requester,
        out Item? delivered)
    {
        delivered = null;
        if (!TryCreateCurrentProduct(house, target, out Item? product, out FarmAnimal? animal)
            || product is null)
            return AnimalProductTransferFailure.SourceChanged;
        if (!CanInventoryAcceptCompleteStack(requester, product))
            return AnimalProductTransferFailure.InsufficientCapacity;

        InventorySnapshot snapshot = InventorySnapshot.Capture(requester.Items);
        try
        {
            Item? remainder = requester.addItemToInventory(product);
            if (remainder is not null)
            {
                snapshot.Restore(requester.Items);
                return AnimalProductTransferFailure.InsufficientCapacity;
            }

            product.Stack = target.Stack;
            if (!TryCommitSource(house, target, animal, product, requester))
            {
                snapshot.Restore(requester.Items);
                return AnimalProductTransferFailure.SourceChanged;
            }

            delivered = product.getOne();
            delivered.Stack = target.Stack;
            delivered.Quality = target.Quality;
            return AnimalProductTransferFailure.None;
        }
        catch
        {
            snapshot.Restore(requester.Items);
            return AnimalProductTransferFailure.CommitFailed;
        }
    }

    public static bool CanInventoryAcceptCompleteStack(Farmer requester, Item item)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsRecipe
            || item.QualifiedItemId is "(O)73" or "(O)930" or "(O)102" or "(O)858" or "(O)GoldCoin")
            return true;

        for (int index = 0; index < requester.MaxItems && index < requester.Items.Count; index++)
        {
            Item? existing = requester.Items[index];
            if (existing is null)
                return true;
            if (existing.canStackWith(item)
                && existing.Stack + item.Stack <= existing.maximumStackSize())
                return true;
        }

        return false;
    }

    private static bool TryCreateCurrentProduct(
        AnimalHouse house,
        AnimalProductTargetPlan target,
        out Item? product,
        out FarmAnimal? animal)
    {
        product = null;
        animal = null;
        if (target.Kind == AnimalProductTargetKind.LooseOvernightProduct)
        {
            Vector2 tile = target.TargetTile.ToVector2();
            if (!house.objects.TryGetValue(tile, out StardewValley.Object? source)
                || source.QualifiedItemId != target.QualifiedItemId
                || source.Stack != target.Stack
                || source.Quality != target.Quality)
                return false;

            product = source.getOne();
            product.Stack = source.Stack;
            product.Quality = source.Quality;
            return true;
        }

        if (!target.AnimalId.HasValue
            || !house.animals.TryGetValue(target.AnimalId.Value, out animal)
            || !ReferenceEquals(animal.currentLocation, house))
            return false;

        FarmAnimalData? data = animal.GetAnimalData();
        AnimalCareSkipReason reason = AnimalProducePolicy.TryCreateToolHarvestPlan(
            Context.IsMainPlayer,
            animal.isAdult(),
            animal.currentProduce.Value,
            data?.HarvestType == FarmAnimalHarvestType.HarvestWithTool,
            data?.HarvestTool,
            animal.hasEatenAnimalCracker.Value,
            animal.produceQuality.Value,
            autoGrabberOwnsProduce: false,
            out AnimalProducePlan? plan);
        if (reason != AnimalCareSkipReason.None
            || plan is null
            || plan.QualifiedItemId != target.QualifiedItemId
            || plan.Stack != target.Stack
            || plan.Quality != target.Quality
            || plan.RequiredTool != target.RequiredTool)
            return false;

        StardewValley.Object created = ItemRegistry.Create<StardewValley.Object>(plan.QualifiedItemId);
        created.CanBeSetDown = false;
        created.Stack = plan.Stack;
        created.Quality = plan.Quality;
        product = created;
        return true;
    }

    private static bool TryCommitSource(
        AnimalHouse house,
        AnimalProductTargetPlan target,
        FarmAnimal? animal,
        Item product,
        Farmer requester)
    {
        if (target.Kind == AnimalProductTargetKind.LooseOvernightProduct)
        {
            Vector2 tile = target.TargetTile.ToVector2();
            if (!house.objects.TryGetValue(tile, out StardewValley.Object? source)
                || source.QualifiedItemId != target.QualifiedItemId
                || source.Stack != target.Stack
                || source.Quality != target.Quality)
                return false;
            return ReferenceEquals(house.removeObject(tile, showDestroyedObject: false), source);
        }

        if (animal is null
            || animal.currentProduce.Value is null
            || !ReferenceEquals(animal.currentLocation, house))
            return false;

        string previousProduce = animal.currentProduce.Value;
        int previousFriendship = animal.friendshipTowardFarmer.Value;
        try
        {
            animal.currentProduce.Value = null;
            animal.friendshipTowardFarmer.Value = Math.Min(1000, previousFriendship + 5);
            animal.ReloadTextureIfNeeded();
            animal.HandleStatsOnProduceCollected(product, (uint)product.Stack);
            requester.gainExperience(0, 5);
            return true;
        }
        catch
        {
            animal.currentProduce.Value = previousProduce;
            animal.friendshipTowardFarmer.Value = previousFriendship;
            animal.ReloadTextureIfNeeded();
            throw;
        }
    }

    private sealed class InventorySnapshot
    {
        private readonly Item?[] Items;
        private readonly int[] Stacks;

        private InventorySnapshot(Item?[] items, int[] stacks)
        {
            this.Items = items;
            this.Stacks = stacks;
        }

        public static InventorySnapshot Capture(IList<Item> inventory)
        {
            Item?[] items = inventory.Cast<Item?>().ToArray();
            return new InventorySnapshot(
                items,
                items.Select(item => item?.Stack ?? 0).ToArray());
        }

        public void Restore(IList<Item> inventory)
        {
            while (inventory.Count > this.Items.Length)
                inventory.RemoveAt(inventory.Count - 1);
            while (inventory.Count < this.Items.Length)
                inventory.Add(null!);

            for (int index = 0; index < this.Items.Length; index++)
            {
                Item? item = this.Items[index];
                if (item is not null)
                    item.Stack = this.Stacks[index];
                inventory[index] = item!;
            }
        }
    }
}
