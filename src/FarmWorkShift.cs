using StardewValley;

namespace EvilFarmOwner;

internal enum FarmWorkStage
{
    Harvesting,
    Watering,
    AnimalCare,
    StorageSorting,
    Complete
}

internal enum FarmWorkPass
{
    Initial,
    Reconciliation
}

internal sealed record FarmWorkShiftContext(
    Guid Id,
    string RequestId,
    Farmer Requester,
    NpcWorkLease Lease,
    WorkContractPreview BillingPreview,
    HarvestDestinationMode HarvestDestination,
    FarmWorkStageSelection EnabledStages);

internal static class FarmWorkStagePolicy
{
    private static readonly FarmWorkStage[] OrderedStages =
    {
        FarmWorkStage.Harvesting,
        FarmWorkStage.Watering,
        FarmWorkStage.AnimalCare,
        FarmWorkStage.StorageSorting
    };

    public static IReadOnlyList<FarmWorkStage> Order => OrderedStages;

    public static FarmWorkStage GetNext(FarmWorkStage? completed)
    {
        return GetNext(completed, FarmWorkStageSelection.All);
    }

    public static FarmWorkStage GetNext(
        FarmWorkStage? completed,
        FarmWorkStageSelection enabledStages)
    {
        int startIndex = 0;
        if (completed is not null)
        {
            int completedIndex = Array.IndexOf(OrderedStages, completed.Value);
            if (completedIndex < 0)
                return FarmWorkStage.Complete;
            startIndex = completedIndex + 1;
        }

        for (int index = startIndex; index < OrderedStages.Length; index++)
        {
            if (IsEnabled(OrderedStages[index], enabledStages))
                return OrderedStages[index];
        }

        return FarmWorkStage.Complete;
    }

    public static bool IsEnabled(FarmWorkStage stage, FarmWorkStageSelection enabledStages)
    {
        FarmWorkStageSelection flag = stage switch
        {
            FarmWorkStage.Harvesting => FarmWorkStageSelection.Harvesting,
            FarmWorkStage.Watering => FarmWorkStageSelection.Watering,
            FarmWorkStage.AnimalCare => FarmWorkStageSelection.AnimalCare,
            FarmWorkStage.StorageSorting => FarmWorkStageSelection.StorageSorting,
            _ => FarmWorkStageSelection.None
        };
        return flag != FarmWorkStageSelection.None && (enabledStages & flag) != 0;
    }

    public static bool IsEmptyStageFailure(FarmWorkStage stage, string? failureKey)
    {
        return stage switch
        {
            FarmWorkStage.Harvesting => failureKey == "harvest.start.no-mature-crop",
            FarmWorkStage.Watering => failureKey == "contract.start.no-dry-crop",
            FarmWorkStage.AnimalCare => failureKey == "animal-care.start.no-work",
            FarmWorkStage.StorageSorting => failureKey is "storage-sort.start.no-work"
                or "storage-sort.start.no-chest",
            _ => false
        };
    }
}

internal static class FarmWorkPassPolicy
{
    public const int MaximumPasses = 2;

    public static IReadOnlyList<(FarmWorkPass Pass, FarmWorkStage Stage)> OrderedSteps { get; } =
        new[] { FarmWorkPass.Initial, FarmWorkPass.Reconciliation }
            .SelectMany(pass => FarmWorkStagePolicy.Order.Select(stage => (pass, stage)))
            .ToArray();

    public static bool TryGetNext(FarmWorkPass current, out FarmWorkPass next)
    {
        if (current == FarmWorkPass.Initial)
        {
            next = FarmWorkPass.Reconciliation;
            return true;
        }

        next = current;
        return false;
    }

    public static string FormatRuntimePhase(
        FarmWorkStage stage,
        FarmWorkPass pass,
        string childPhase)
    {
        if (string.IsNullOrWhiteSpace(childPhase))
        {
            throw new ArgumentException(
                "Child phase is required.",
                nameof(childPhase));
        }

        return $"{stage}/{pass}/{childPhase}";
    }
}
