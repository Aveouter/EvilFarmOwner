namespace EvilFarmOwner;

internal enum RequesterInventoryDeliveryDecision
{
    FallBackToChest,
    DeliverCompleteStack
}

internal static class RequesterInventoryDeliveryPolicy
{
    public static RequesterInventoryDeliveryDecision Select(
        bool requesterIsOnline,
        bool requesterIsOnFarm,
        bool inventoryCanAcceptCompleteStack)
    {
        return requesterIsOnline
            && requesterIsOnFarm
            && inventoryCanAcceptCompleteStack
                ? RequesterInventoryDeliveryDecision.DeliverCompleteStack
                : RequesterInventoryDeliveryDecision.FallBackToChest;
    }

    public static int GetRetainedCount(
        int compatibleQuantityBefore,
        int compatibleQuantityAfter,
        int requestedStack)
    {
        return Math.Clamp(
            compatibleQuantityAfter - compatibleQuantityBefore,
            0,
            Math.Max(0, requestedStack));
    }
}
