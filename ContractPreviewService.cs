namespace EvilFarmOwner;

internal static class ContractPreviewService
{
    internal const int BaseHourlyWage = 100;
    internal const int RegularShiftHours = 6;
    internal const int MaximumOvertimeHours = 2;
    internal const decimal RestDayMultiplier = 3.00m;
    internal const decimal OvertimeMultiplier = 1.50m;
    internal const decimal BaselineEfficiencyMultiplier = 1.00m;

    public static WateringContractPreview Create(int friendshipHearts, int dayOfMonth)
    {
        int normalizedHearts = Math.Max(0, friendshipHearts);
        (FriendshipWageBand band, decimal friendshipMultiplier) = GetFriendshipBand(normalizedHearts);

        int normalizedDay = Math.Max(1, dayOfMonth);
        int zeroBasedDayOfWeek = (normalizedDay - 1) % 7;
        bool isRestDay = zeroBasedDayOfWeek >= 5;
        ContractDayKind dayKind = isRestDay ? ContractDayKind.RestDay : ContractDayKind.RegularWorkday;
        decimal dayMultiplier = isRestDay ? RestDayMultiplier : 1.00m;

        int estimatedWage = RoundWage(
            BaseHourlyWage * RegularShiftHours * friendshipMultiplier * dayMultiplier);

        return new WateringContractPreview(
            normalizedHearts,
            band,
            friendshipMultiplier,
            dayKind,
            dayMultiplier,
            BaseHourlyWage,
            RegularShiftHours,
            BaselineEfficiencyMultiplier,
            OvertimeEnabled: false,
            OvertimeMultiplier,
            MaximumOvertimeHours,
            estimatedWage,
            MaximumAuthorizedWage: estimatedWage);
    }

    private static (FriendshipWageBand Band, decimal Multiplier) GetFriendshipBand(int hearts)
    {
        if (hearts <= 1)
            return (FriendshipWageBand.HighRisk, 1.20m);

        if (hearts <= 3)
            return (FriendshipWageBand.ElevatedRisk, 1.10m);

        if (hearts <= 7)
            return (FriendshipWageBand.Standard, 1.00m);

        return (FriendshipWageBand.Trusted, 0.90m);
    }

    private static int RoundWage(decimal wage)
    {
        return (int)Math.Ceiling(wage);
    }
}
