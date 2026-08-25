using StardewValley;

namespace EvilFarmOwner;

internal enum FarmWorkStage
{
    Harvesting,
    Watering,
    StorageSorting,
    Complete
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
