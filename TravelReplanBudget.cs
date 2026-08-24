namespace EvilFarmOwner;

internal enum TravelRoutePurpose
{
    Target,
    Delivery
}

internal readonly record struct TravelReplanDecision(
    int FailureCount,
    int MaximumFailures)
{
    public bool CanReplan => this.FailureCount < this.MaximumFailures;
}

/// <summary>
/// Bounds consecutive route failures which share a purpose and origin tile.
/// Fine-grained target/chest ledgers still exclude the exact failed destination;
/// this budget prevents many distinct destinations from retrying through the same
/// dynamically blocked departure tile.
/// </summary>
internal sealed class TravelReplanBudget
{
    internal const int DefaultMaximumFailures = 3;

    private readonly int MaximumFailures;
    private readonly Dictionary<TravelRoutePurpose, FailureState> Failures = new();

    public TravelReplanBudget(int maximumFailures = DefaultMaximumFailures)
    {
        if (maximumFailures <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumFailures));

        this.MaximumFailures = maximumFailures;
    }

    public TravelReplanDecision RecordFailure(TravelRoutePurpose purpose, GridPoint origin)
    {
        if (!this.Failures.TryGetValue(purpose, out FailureState state)
            || state.Origin != origin)
        {
            state = new FailureState(origin, 0);
        }

        state = state with { Count = state.Count + 1 };
        this.Failures[purpose] = state;
        return new TravelReplanDecision(state.Count, this.MaximumFailures);
    }

    public void Reset(TravelRoutePurpose purpose)
    {
        this.Failures.Remove(purpose);
    }

    private readonly record struct FailureState(GridPoint Origin, int Count);
}
