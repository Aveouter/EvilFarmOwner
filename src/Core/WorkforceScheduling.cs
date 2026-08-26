namespace EvilFarmOwner;

internal sealed record WorkTargetIdentity(
    string Domain,
    string Location,
    string TargetId)
{
    public string StableKey => $"{this.Domain}\u001f{this.Location}\u001f{this.TargetId}";
}

internal sealed record SchedulableWorkTarget(
    WorkTargetIdentity Identity,
    int EstimatedCost);

internal sealed record SchedulableWorker(
    string WorkerId,
    decimal EfficiencyMultiplier);

internal sealed record WorkerTargetAssignment(
    string WorkerId,
    IReadOnlyList<WorkTargetIdentity> Targets,
    decimal EstimatedLoad);

internal static class DeterministicWorkforceScheduler
{
    public static IReadOnlyList<WorkerTargetAssignment> Partition(
        IEnumerable<SchedulableWorker> workers,
        IEnumerable<SchedulableWorkTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(workers);
        ArgumentNullException.ThrowIfNull(targets);

        SchedulableWorker[] orderedWorkers = workers
            .OrderBy(worker => worker.WorkerId, StringComparer.Ordinal)
            .ToArray();
        SchedulableWorkTarget[] orderedTargets = targets
            .OrderBy(target => target.Identity.StableKey, StringComparer.Ordinal)
            .ToArray();

        if (orderedWorkers.Length == 0 && orderedTargets.Length > 0)
            throw new InvalidOperationException("Work targets require at least one worker.");
        if (orderedWorkers.Any(worker => string.IsNullOrWhiteSpace(worker.WorkerId)
            || worker.EfficiencyMultiplier <= 0m))
            throw new ArgumentException("Every worker requires an ID and positive efficiency.", nameof(workers));
        if (orderedWorkers.Select(worker => worker.WorkerId).Distinct(StringComparer.Ordinal).Count()
            != orderedWorkers.Length)
            throw new ArgumentException("Worker IDs must be unique.", nameof(workers));
        if (orderedTargets.Any(target => target.EstimatedCost <= 0
            || string.IsNullOrWhiteSpace(target.Identity.Domain)
            || string.IsNullOrWhiteSpace(target.Identity.Location)
            || string.IsNullOrWhiteSpace(target.Identity.TargetId)))
            throw new ArgumentException("Every target requires a stable identity and positive cost.", nameof(targets));
        if (orderedTargets.Select(target => target.Identity.StableKey).Distinct(StringComparer.Ordinal).Count()
            != orderedTargets.Length)
            throw new ArgumentException("Target identities must be unique.", nameof(targets));

        Dictionary<string, List<WorkTargetIdentity>> assignments = orderedWorkers
            .ToDictionary(worker => worker.WorkerId, _ => new List<WorkTargetIdentity>(), StringComparer.Ordinal);
        Dictionary<string, decimal> loads = orderedWorkers
            .ToDictionary(worker => worker.WorkerId, _ => 0m, StringComparer.Ordinal);

        foreach (SchedulableWorkTarget target in orderedTargets)
        {
            SchedulableWorker selected = orderedWorkers
                .OrderBy(worker => loads[worker.WorkerId]
                    + target.EstimatedCost / worker.EfficiencyMultiplier)
                .ThenBy(worker => worker.WorkerId, StringComparer.Ordinal)
                .First();
            decimal adjustedCost = target.EstimatedCost / selected.EfficiencyMultiplier;
            assignments[selected.WorkerId].Add(target.Identity);
            loads[selected.WorkerId] += adjustedCost;
        }

        return orderedWorkers
            .Select(worker => new WorkerTargetAssignment(
                worker.WorkerId,
                assignments[worker.WorkerId].ToArray(),
                loads[worker.WorkerId]))
            .ToArray();
    }
}

internal enum WorkClaimState
{
    Claimed,
    Committed
}

internal sealed record WorkClaimSnapshot(
    WorkTargetIdentity Target,
    string WorkerId,
    WorkClaimState State);

internal sealed class DeterministicWorkClaimLedger
{
    private readonly Dictionary<string, WorkClaimSnapshot> Claims = new(StringComparer.Ordinal);

    public bool IsClaimed(WorkTargetIdentity target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return this.Claims.ContainsKey(target.StableKey);
    }

    public bool TryClaim(WorkTargetIdentity target, string workerId)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Worker ID is required.", nameof(workerId));

        if (this.Claims.ContainsKey(target.StableKey))
            return false;

        this.Claims[target.StableKey] = new(target, workerId, WorkClaimState.Claimed);
        return true;
    }

    public bool TryCommit(WorkTargetIdentity target, string workerId)
    {
        if (!this.Claims.TryGetValue(target.StableKey, out WorkClaimSnapshot? claim)
            || claim.WorkerId != workerId
            || claim.State != WorkClaimState.Claimed)
            return false;

        this.Claims[target.StableKey] = claim with { State = WorkClaimState.Committed };
        return true;
    }

    public int ReleaseUncommitted(string workerId)
    {
        string[] releasable = this.Claims.Values
            .Where(claim => claim.WorkerId == workerId && claim.State == WorkClaimState.Claimed)
            .Select(claim => claim.Target.StableKey)
            .ToArray();
        foreach (string key in releasable)
            this.Claims.Remove(key);
        return releasable.Length;
    }

    public bool IsUncommittedOwnedBy(WorkTargetIdentity target, string workerId)
    {
        return this.Claims.TryGetValue(target.StableKey, out WorkClaimSnapshot? claim)
            && claim.State == WorkClaimState.Claimed
            && string.Equals(claim.WorkerId, workerId, StringComparison.Ordinal);
    }

    public bool Release(WorkTargetIdentity target, string workerId)
    {
        if (!this.Claims.TryGetValue(target.StableKey, out WorkClaimSnapshot? claim)
            || claim.State != WorkClaimState.Claimed
            || !string.Equals(claim.WorkerId, workerId, StringComparison.Ordinal))
            return false;
        return this.Claims.Remove(target.StableKey);
    }

    public IReadOnlyList<WorkClaimSnapshot> Snapshot()
    {
        return this.Claims.Values
            .OrderBy(claim => claim.Target.StableKey, StringComparer.Ordinal)
            .ToArray();
    }
}

internal sealed class RuntimeWorkClaimCoordinator
{
    private readonly DeterministicWorkClaimLedger Ledger = new();

    public bool IsAvailable(string location, int x, int y, string workerId)
    {
        WorkTargetIdentity target = CreateTarget(location, x, y);
        return !this.Ledger.IsClaimed(target)
            || this.Ledger.IsUncommittedOwnedBy(target, workerId);
    }

    public bool TryClaim(string location, int x, int y, string workerId)
    {
        WorkTargetIdentity target = CreateTarget(location, x, y);
        return this.Ledger.IsUncommittedOwnedBy(target, workerId)
            || this.Ledger.TryClaim(target, workerId);
    }

    public bool TryCommit(string location, int x, int y, string workerId) =>
        this.Ledger.TryCommit(CreateTarget(location, x, y), workerId);

    public bool Release(string location, int x, int y, string workerId) =>
        this.Ledger.Release(CreateTarget(location, x, y), workerId);

    public int ReleaseWorker(string workerId) => this.Ledger.ReleaseUncommitted(workerId);

    private static WorkTargetIdentity CreateTarget(string location, int x, int y) =>
        new("farm-resource", location, $"{x},{y}");
}

internal sealed record WorkerWageSettlement(string WorkerId, int AuthorizedGold, int ChargedGold);

internal static class WorkforceSettlementPolicy
{
    public static int GetAggregateCharge(IEnumerable<WorkerWageSettlement> settlements)
    {
        ArgumentNullException.ThrowIfNull(settlements);
        int total = 0;
        HashSet<string> workers = new(StringComparer.Ordinal);
        foreach (WorkerWageSettlement settlement in settlements)
        {
            if (string.IsNullOrWhiteSpace(settlement.WorkerId)
                || !workers.Add(settlement.WorkerId)
                || settlement.AuthorizedGold < 0
                || settlement.ChargedGold < 0
                || settlement.ChargedGold > settlement.AuthorizedGold)
                throw new ArgumentException("Invalid per-worker settlement.", nameof(settlements));
            total = checked(total + settlement.ChargedGold);
        }
        return total;
    }
}
