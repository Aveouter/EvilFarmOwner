namespace EvilFarmOwner;

internal sealed partial class WateringTargetPlanner
{
    public static bool IsEligibleDryCropState(bool hasCrop, bool isDead, bool isWatered) =>
        hasCrop && !isDead && !isWatered;
}
