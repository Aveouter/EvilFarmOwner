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

    public static int CountCarriedSlots<T>(
        IEnumerable<T> cargo,
        Func<T, int> getQuantity,
        Func<T, int> getMaximumStack,
        Func<T, T, bool> canStack)
    {
        ArgumentNullException.ThrowIfNull(cargo);
        ArgumentNullException.ThrowIfNull(getQuantity);
        ArgumentNullException.ThrowIfNull(getMaximumStack);
        ArgumentNullException.ThrowIfNull(canStack);

        List<CarriedSlot<T>> slots = new();
        foreach (T entry in cargo)
        {
            int remaining = Math.Max(0, getQuantity(entry));
            if (remaining == 0)
                continue;

            int maximumStack = Math.Max(1, getMaximumStack(entry));
            foreach (CarriedSlot<T> slot in slots)
            {
                if (remaining == 0)
                    break;
                if (!canStack(slot.Sample, entry))
                    continue;

                int accepted = Math.Min(remaining, Math.Max(0, slot.MaximumStack - slot.Quantity));
                slot.Quantity += accepted;
                remaining -= accepted;
            }

            while (remaining > 0)
            {
                int quantity = Math.Min(remaining, maximumStack);
                slots.Add(new CarriedSlot<T>(entry, quantity, maximumStack));
                remaining -= quantity;
            }
        }

        return slots.Count;
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

    private sealed class CarriedSlot<T>
    {
        public CarriedSlot(T sample, int quantity, int maximumStack)
        {
            this.Sample = sample;
            this.Quantity = quantity;
            this.MaximumStack = maximumStack;
        }

        public T Sample { get; }
        public int Quantity { get; set; }
        public int MaximumStack { get; }
    }
}
