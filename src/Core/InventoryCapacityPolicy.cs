namespace EvilFarmOwner;

internal readonly record struct InventoryCapacitySlot(
    bool IsEmpty,
    bool IsCompatible,
    int CurrentStack,
    int MaximumStack);

internal static class InventoryCapacityPolicy
{
    public static bool CanAcceptCompleteStack(
        int requestedStack,
        int incomingMaximumStack,
        IEnumerable<InventoryCapacitySlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (requestedStack <= 0)
            return true;

        long capacity = 0;
        foreach (InventoryCapacitySlot slot in slots)
        {
            if (slot.IsEmpty)
                capacity += Math.Max(1, incomingMaximumStack);
            else if (slot.IsCompatible)
                capacity += Math.Max(0, slot.MaximumStack - slot.CurrentStack);
            if (capacity >= requestedStack)
                return true;
        }
        return false;
    }
}
