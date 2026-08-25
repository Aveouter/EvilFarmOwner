namespace EvilFarmOwner;

internal enum NpcLeaseRecoveryAction
{
    Complete,
    Retry,
    Relinquish
}

internal static class NpcLeaseRecoveryPolicy
{
    public const int MaximumDeferredTicks = 300;

    public static NpcLeaseRecoveryAction Select(
        NpcLeaseRestoreResult restoreResult,
        int deferredTicks,
        bool mustFinalizeNow)
    {
        if (restoreResult != NpcLeaseRestoreResult.ConflictingController)
            return NpcLeaseRecoveryAction.Complete;

        return mustFinalizeNow || deferredTicks >= MaximumDeferredTicks
            ? NpcLeaseRecoveryAction.Relinquish
            : NpcLeaseRecoveryAction.Retry;
    }
}
