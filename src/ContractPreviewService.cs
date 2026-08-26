namespace EvilFarmOwner;

internal static class ContractPreviewService
{
    internal const int BaseHourlyWage = 100;
    internal const int RegularShiftHours = 6;
    internal const int MaximumOvertimeHours = 2;
    internal const decimal RestDayMultiplier = 3.00m;
    internal const decimal OvertimeMultiplier = 1.50m;
    internal const decimal BaselineEfficiencyMultiplier = WorkerEfficiencyProfiles.BaselineMultiplier;

    public static WorkContractPreview Create(int friendshipHearts, int dayOfMonth)
    {
        return Create(
            friendshipHearts,
            dayOfMonth,
            BaselineEfficiencyMultiplier,
            WorkerEfficiencyBackground.Baseline,
            ContractSettingsSnapshot.Default);
    }

    public static WorkContractPreview Create(
        int friendshipHearts,
        int dayOfMonth,
        ContractSettingsSnapshot settings)
    {
        return Create(
            friendshipHearts,
            dayOfMonth,
            BaselineEfficiencyMultiplier,
            WorkerEfficiencyBackground.Baseline,
            settings);
    }

    public static WorkContractPreview Create(
        int friendshipHearts,
        int dayOfMonth,
        string workerName,
        NamedFarmTask task,
        ContractSettingsSnapshot? settings = null)
    {
        WorkerEfficiencyProfile profile = WorkerEfficiencyProfiles.GetProfile(workerName);
        return Create(
            friendshipHearts,
            dayOfMonth,
            profile.GetMultiplier(task),
            task == NamedFarmTask.StorageSorting
                ? WorkerEfficiencyBackground.Baseline
                : profile.Background,
            settings ?? ContractSettingsSnapshot.Default);
    }

    private static WorkContractPreview Create(
        int friendshipHearts,
        int dayOfMonth,
        decimal efficiencyMultiplier,
        WorkerEfficiencyBackground efficiencyBackground,
        ContractSettingsSnapshot settings)
    {
        if (!settings.IsValid)
            settings = ContractSettingsSnapshot.Default;

        int normalizedHearts = Math.Max(0, friendshipHearts);
        (FriendshipWageBand band, decimal friendshipMultiplier) = GetFriendshipBand(
            normalizedHearts,
            settings.FriendshipWageImpactPercent);

        int normalizedDay = Math.Max(1, dayOfMonth);
        int zeroBasedDayOfWeek = (normalizedDay - 1) % 7;
        bool isRestDay = zeroBasedDayOfWeek >= 5;
        ContractDayKind dayKind = isRestDay ? ContractDayKind.RestDay : ContractDayKind.RegularWorkday;
        decimal dayMultiplier = isRestDay ? settings.RestDayMultiplier : 1.00m;

        int estimatedWage = RoundWage(
            settings.BaseHourlyWage * RegularShiftHours * friendshipMultiplier * dayMultiplier);
        int minimumCalloutWage = RoundWage(
            settings.BaseHourlyWage * friendshipMultiplier * dayMultiplier);

        return new WorkContractPreview(
            normalizedHearts,
            band,
            friendshipMultiplier,
            dayKind,
            dayMultiplier,
            settings.BaseHourlyWage,
            RegularShiftHours,
            efficiencyMultiplier,
            efficiencyBackground,
            OvertimeEnabled: false,
            OvertimeMultiplier,
            MaximumOvertimeHours,
            estimatedWage,
            MaximumAuthorizedWage: estimatedWage,
            MinimumCalloutWage: minimumCalloutWage);
    }

    private static (FriendshipWageBand Band, decimal Multiplier) GetFriendshipBand(
        int hearts,
        int friendshipImpactPercent)
    {
        decimal impact = ContractSettingsPolicy.NormalizeFriendshipImpactPercent(
            friendshipImpactPercent) / 100m;
        if (hearts <= 1)
            return (FriendshipWageBand.HighRisk, 1.00m + impact);

        if (hearts <= 3)
            return (FriendshipWageBand.ElevatedRisk, 1.00m + impact / 2m);

        if (hearts <= 7)
            return (FriendshipWageBand.Standard, 1.00m);

        return (FriendshipWageBand.Trusted, 1.00m - impact / 2m);
    }

    private static int RoundWage(decimal wage)
    {
        return (int)Math.Ceiling(wage);
    }
}
