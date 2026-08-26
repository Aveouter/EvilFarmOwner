using StardewValley;

namespace EvilFarmOwner;

internal static class RequesterInventoryCapacity
{
    public static bool CanAcceptCompleteStack(Farmer requester, Item item)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsRecipe
            || item.QualifiedItemId is "(O)73" or "(O)930" or "(O)102" or "(O)858" or "(O)GoldCoin")
            return true;

        int usableSlots = Math.Max(0, requester.MaxItems);
        InventoryCapacitySlot[] slots = new InventoryCapacitySlot[usableSlots];
        for (int index = 0; index < usableSlots; index++)
        {
            Item? existing = index < requester.Items.Count
                ? requester.Items[index]
                : null;
            slots[index] = existing is null
                ? new InventoryCapacitySlot(true, false, 0, 0)
                : new InventoryCapacitySlot(
                    false,
                    existing.canStackWith(item),
                    existing.Stack,
                    existing.maximumStackSize());
        }

        return InventoryCapacityPolicy.CanAcceptCompleteStack(
            item.Stack,
            item.maximumStackSize(),
            slots);
    }
}
