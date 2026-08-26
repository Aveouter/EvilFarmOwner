namespace EvilFarmOwner;

internal sealed record WorkforceStageOutcome(
    string WorkerId,
    FarmWorkStageSelection AssignedStages,
    bool Succeeded,
    bool NoWork);

internal sealed record WorkforceRecoveryDecision(
    FarmWorkStageSelection FailedStages,
    string? RecoveryWorkerId)
{
    public bool IsRequired => this.FailedStages != FarmWorkStageSelection.None;
}

internal static class WorkforceRecoveryPolicy
{
    public static WorkforceRecoveryDecision Select(
        IEnumerable<WorkforceStageOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        WorkforceStageOutcome[] snapshot = outcomes.ToArray();
        if (snapshot.Any(outcome => string.IsNullOrWhiteSpace(outcome.WorkerId))
            || snapshot.Select(outcome => outcome.WorkerId)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Length)
            throw new ArgumentException("Worker outcomes require unique worker IDs.", nameof(outcomes));

        FarmWorkStageSelection failedStages = snapshot
            .Where(outcome => !outcome.Succeeded && !outcome.NoWork)
            .Aggregate(
                FarmWorkStageSelection.None,
                (stages, outcome) => stages | outcome.AssignedStages);
        string? recoveryWorker = failedStages == FarmWorkStageSelection.None
            ? null
            : snapshot
                .Where(outcome => outcome.Succeeded || outcome.NoWork)
                .OrderBy(outcome => outcome.NoWork ? 0 : 1)
                .ThenBy(outcome => outcome.WorkerId, StringComparer.Ordinal)
                .Select(outcome => outcome.WorkerId)
                .FirstOrDefault();
        return new WorkforceRecoveryDecision(failedStages, recoveryWorker);
    }

    public static bool AreInitialOutcomesSuccessful(
        IEnumerable<WorkforceStageOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        return outcomes.All(outcome => outcome.Succeeded || outcome.NoWork);
    }
}
