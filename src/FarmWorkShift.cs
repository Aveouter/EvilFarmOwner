using StardewValley;

namespace EvilFarmOwner;

internal sealed record FarmWorkShiftContext(
    Guid Id,
    string RequestId,
    Farmer Requester,
    NpcWorkLease Lease,
    WorkContractPreview BillingPreview,
    HarvestDestinationMode HarvestDestination,
    FarmWorkStageSelection EnabledStages);
