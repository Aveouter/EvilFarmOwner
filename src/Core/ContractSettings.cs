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

internal sealed record ContractSettingsSnapshot(
    int BaseHourlyWage,
    int FriendshipWageImpactPercent,
    decimal RestDayMultiplier,
    HarvestDestinationMode DefaultHarvestDestination,
    FarmWorkStageSelection EnabledStages)
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
            && (settings.EnabledStages & ~FarmWorkStageSelection.All) == 0;
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
