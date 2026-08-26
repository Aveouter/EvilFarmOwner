namespace EvilFarmOwner;

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
}
