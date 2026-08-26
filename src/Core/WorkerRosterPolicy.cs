namespace EvilFarmOwner;

internal enum WorkerAvailabilityState
{
    EligibleForPreview,
    TemporarilyUnavailable,
    Ineligible,
    Unknown
}

internal static class WorkerRosterPolicy
{
    public static bool ShouldDisplay(WorkerAvailabilityState state) =>
        state == WorkerAvailabilityState.EligibleForPreview;
}
