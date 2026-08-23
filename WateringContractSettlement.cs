namespace EvilFarmOwner;

internal sealed record WateringContractSettlement(
    int ReservedGold,
    int ChargedGold,
    int RefundedGold)
{
    public static WateringContractSettlement Create(WateringContractPreview preview, bool dispatched)
    {
        int reserved = Math.Max(0, preview.MaximumAuthorizedWage);
        int charged = dispatched
            ? Math.Clamp(preview.MinimumCalloutWage, 0, reserved)
            : 0;

        return new WateringContractSettlement(
            reserved,
            charged,
            reserved - charged);
    }
}
