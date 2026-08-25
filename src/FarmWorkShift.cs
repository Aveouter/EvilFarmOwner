using StardewValley;

namespace EvilFarmOwner;

internal enum FarmWorkStage
{
    Harvesting,
    Watering,
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
    HarvestDestinationMode HarvestDestination);

internal static class FarmWorkStagePolicy
{
    private static readonly FarmWorkStage[] OrderedStages =
    {
        FarmWorkStage.Harvesting,
        FarmWorkStage.Watering,
        FarmWorkStage.StorageSorting
    };

    public static IReadOnlyList<FarmWorkStage> Order => OrderedStages;

    public static FarmWorkStage GetNext(FarmWorkStage? completed)
    {
        if (completed is null)
            return OrderedStages[0];

        int index = Array.IndexOf(OrderedStages, completed.Value);
        return index < 0 || index + 1 >= OrderedStages.Length
            ? FarmWorkStage.Complete
            : OrderedStages[index + 1];
    }

    public static bool IsEmptyStageFailure(FarmWorkStage stage, string? failureKey)
    {
        return stage switch
        {
            FarmWorkStage.Harvesting => failureKey == "harvest.start.no-mature-crop",
            FarmWorkStage.Watering => failureKey == "contract.start.no-dry-crop",
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
