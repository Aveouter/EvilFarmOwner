namespace EvilFarmOwner;

internal enum HarvestDestinationMode
{
    ClassifiedChests = 0,
    RequesterInventory = 1
}

internal enum HarvestDestinationAction
{
    RouteToClassifiedChest,
    DeliverToRequester,
    StopUnavailable
}

internal static class HarvestDestinationPolicy
{
    public static HarvestDestinationMode DefaultManualMode => HarvestDestinationMode.ClassifiedChests;

    public static HarvestDestinationMode AutomaticMode => HarvestDestinationMode.ClassifiedChests;

    public static bool IsValidForTask(NamedFarmTask task, HarvestDestinationMode mode)
    {
        if (!Enum.IsDefined(mode))
            return false;

        return task == NamedFarmTask.Harvesting
            || mode == HarvestDestinationMode.ClassifiedChests;
    }

    public static HarvestDestinationAction SelectAction(
        HarvestDestinationMode mode,
        bool requesterIsOnline,
        bool requesterIsOnMainFarm,
        bool requesterCanAcceptCompleteStack)
    {
        if (mode == HarvestDestinationMode.ClassifiedChests)
            return HarvestDestinationAction.RouteToClassifiedChest;

        return mode == HarvestDestinationMode.RequesterInventory
            && requesterIsOnline
            && requesterIsOnMainFarm
            && requesterCanAcceptCompleteStack
                ? HarvestDestinationAction.DeliverToRequester
                : HarvestDestinationAction.StopUnavailable;
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
