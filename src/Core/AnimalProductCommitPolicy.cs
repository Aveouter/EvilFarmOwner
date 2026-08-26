namespace EvilFarmOwner;

internal enum AnimalProductTransferFailure
{
    None,
    SourceChanged,
    DestinationChanged,
    InsufficientCapacity,
    CommitFailed
}

internal static class AnimalProductCommitPolicy
{
    public static AnimalProductTransferFailure EvaluatePreflight(
        bool sourceUnchanged,
        bool destinationEligible,
        int acceptableCapacity,
        int requestedStack)
    {
        if (requestedStack <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedStack));
        if (!sourceUnchanged)
            return AnimalProductTransferFailure.SourceChanged;
        if (!destinationEligible)
            return AnimalProductTransferFailure.DestinationChanged;
        return acceptableCapacity >= requestedStack
            ? AnimalProductTransferFailure.None
            : AnimalProductTransferFailure.InsufficientCapacity;
    }
}
