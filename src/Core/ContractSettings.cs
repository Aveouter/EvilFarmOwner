namespace EvilFarmOwner;

[Flags]
internal enum FarmWorkStageSelection
{
    None = 0,
    Harvesting = 1 << 0,
    Watering = 1 << 1,
    AnimalCare = 1 << 2,
    StorageSorting = 1 << 3,
    All = Harvesting | Watering | AnimalCare | StorageSorting
}

internal enum RestDayRule
{
    NpcSchedule = 0,
    Weekend = 1,
    Disabled = 2
}

internal sealed record ContractSettingsSnapshot(
    int BaseHourlyWage,
    int FriendshipWageImpactPercent,
    decimal RestDayMultiplier,
    HarvestDestinationMode DefaultHarvestDestination,
    FarmWorkStageSelection EnabledStages,
    int MaximumConcurrentWorkers = ContractSettingsPolicy.DefaultMaximumConcurrentWorkers,
    int WorkerEfficiencyImpactPercent = ContractSettingsPolicy.DefaultWorkerEfficiencyImpactPercent,
    RestDayRule RestDayRule = RestDayRule.NpcSchedule,
    FarmWorkScopeSelection WorkScope = FarmWorkScopeSelection.All)
{
    public static ContractSettingsSnapshot Default { get; } = new(
        ContractPreviewService.BaseHourlyWage,
        20,
        ContractPreviewService.RestDayMultiplier,
        HarvestDestinationMode.ClassifiedChests,
        FarmWorkStageSelection.All);

    public bool IsValid => ContractSettingsPolicy.IsValid(this);
}

internal static class ContractSettingsPolicy
{
    public const int MinimumBaseHourlyWage = 50;
    public const int MaximumBaseHourlyWage = 500;
    public const int BaseHourlyWageStep = 10;
    public const int MinimumFriendshipImpactPercent = 0;
    public const int MaximumFriendshipImpactPercent = 40;
    public const int FriendshipImpactStep = 5;
    public const decimal MinimumRestDayMultiplier = 1.0m;
    public const decimal MaximumRestDayMultiplier = 5.0m;
    public const decimal RestDayMultiplierStep = 0.5m;
    public const int MinimumMaximumConcurrentWorkers = 1;
    public const int MaximumMaximumConcurrentWorkers = 4;
    public const int DefaultMaximumConcurrentWorkers = 1;
    public const int MinimumWorkerEfficiencyImpactPercent = 0;
    public const int MaximumWorkerEfficiencyImpactPercent = 200;
    public const int WorkerEfficiencyImpactStep = 25;
    public const int DefaultWorkerEfficiencyImpactPercent = 150;

    public static bool IsValid(ContractSettingsSnapshot settings)
    {
        return settings.BaseHourlyWage is >= MinimumBaseHourlyWage and <= MaximumBaseHourlyWage
            && settings.BaseHourlyWage % BaseHourlyWageStep == 0
            && settings.FriendshipWageImpactPercent is >= MinimumFriendshipImpactPercent
                and <= MaximumFriendshipImpactPercent
            && settings.FriendshipWageImpactPercent % FriendshipImpactStep == 0
            && settings.RestDayMultiplier is >= MinimumRestDayMultiplier
                and <= MaximumRestDayMultiplier
            && IsStepAligned(settings.RestDayMultiplier, RestDayMultiplierStep)
            && Enum.IsDefined(settings.DefaultHarvestDestination)
            && settings.EnabledStages != FarmWorkStageSelection.None
            && (settings.EnabledStages & ~FarmWorkStageSelection.All) == 0
            && settings.MaximumConcurrentWorkers is >= MinimumMaximumConcurrentWorkers
                and <= MaximumMaximumConcurrentWorkers
            && settings.WorkerEfficiencyImpactPercent is >= MinimumWorkerEfficiencyImpactPercent
                and <= MaximumWorkerEfficiencyImpactPercent
            && settings.WorkerEfficiencyImpactPercent % WorkerEfficiencyImpactStep == 0
            && Enum.IsDefined(settings.RestDayRule)
            && settings.WorkScope == FarmWorkLocationPolicy.Normalize(settings.WorkScope);
    }

    public static int NormalizeBaseHourlyWage(int value) => NormalizeStep(
        value,
        MinimumBaseHourlyWage,
        MaximumBaseHourlyWage,
        BaseHourlyWageStep);

    public static int NormalizeFriendshipImpactPercent(int value) => NormalizeStep(
        value,
        MinimumFriendshipImpactPercent,
        MaximumFriendshipImpactPercent,
        FriendshipImpactStep);

    public static decimal NormalizeRestDayMultiplier(decimal value)
    {
        decimal clamped = Math.Clamp(value, MinimumRestDayMultiplier, MaximumRestDayMultiplier);
        decimal steps = decimal.Round(
            (clamped - MinimumRestDayMultiplier) / RestDayMultiplierStep,
            0,
            MidpointRounding.AwayFromZero);
        return MinimumRestDayMultiplier + steps * RestDayMultiplierStep;
    }

    public static FarmWorkStageSelection NormalizeEnabledStages(FarmWorkStageSelection value)
    {
        return value != FarmWorkStageSelection.None
            && (value & ~FarmWorkStageSelection.All) == 0
                ? value
                : FarmWorkStageSelection.All;
    }

    public static int NormalizeMaximumConcurrentWorkers(int value) => Math.Clamp(
        value <= 0 ? DefaultMaximumConcurrentWorkers : value,
        MinimumMaximumConcurrentWorkers,
        MaximumMaximumConcurrentWorkers);

    public static int NormalizeWorkerEfficiencyImpactPercent(int value) => NormalizeStep(
        value,
        MinimumWorkerEfficiencyImpactPercent,
        MaximumWorkerEfficiencyImpactPercent,
        WorkerEfficiencyImpactStep);

    public static decimal ApplyWorkerEfficiencyImpact(decimal profileMultiplier, int impactPercent)
    {
        decimal normalizedImpact = NormalizeWorkerEfficiencyImpactPercent(impactPercent) / 100m;
        decimal adjusted = 1m + (profileMultiplier - 1m) * normalizedImpact;
        return Math.Clamp(
            adjusted,
            WorkerEfficiencyProfiles.MinimumSupportedMultiplier,
            WorkerEfficiencyProfiles.MaximumSupportedMultiplier);
    }

    private static int NormalizeStep(int value, int minimum, int maximum, int step)
    {
        int clamped = Math.Clamp(value, minimum, maximum);
        int steps = (int)Math.Round(
            (clamped - minimum) / (double)step,
            MidpointRounding.AwayFromZero);
        return minimum + steps * step;
    }

    private static bool IsStepAligned(decimal value, decimal step)
    {
        return (value - MinimumRestDayMultiplier) % step == 0;
    }
}
