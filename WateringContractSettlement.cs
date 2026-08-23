namespace EvilFarmOwner;

internal sealed record WateringContractSettlement(
    int ReservedGold,
    int ChargedGold,
    int RefundedGold,
    int BillableHours)
{
    public static WateringContractSettlement Create(
        WateringContractPreview preview,
        bool dispatched,
        int startTime,
        int endTime)
    {
        int reserved = Math.Max(0, preview.MaximumAuthorizedWage);
        int billableHours = dispatched
            ? GameClockMath.GetStartedHours(startTime, endTime, preview.RegularShiftHours)
            : 0;
        int charged = dispatched
            ? Math.Clamp(preview.MinimumCalloutWage * billableHours, 0, reserved)
            : 0;

        return new WateringContractSettlement(
            reserved,
            charged,
            reserved - charged,
            billableHours);
    }
}

internal static class GameClockMath
{
    public static int GetStartedHours(int startTime, int endTime, int maximumHours)
    {
        int startMinutes = ToMinutes(startTime);
        int endMinutes = Math.Max(startMinutes, ToMinutes(endTime));
        int elapsedMinutes = endMinutes - startMinutes;
        int startedHours = Math.Max(1, (int)Math.Ceiling(elapsedMinutes / 60m));
        return Math.Clamp(startedHours, 1, Math.Max(1, maximumHours));
    }

    private static int ToMinutes(int time)
    {
        int normalized = Math.Max(0, time);
        return normalized / 100 * 60 + normalized % 100;
    }
}
