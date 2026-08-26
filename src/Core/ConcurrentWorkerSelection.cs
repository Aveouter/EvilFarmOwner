namespace EvilFarmOwner;

internal sealed record ConcurrentWorkerCandidate(
    string WorkerName,
    decimal EfficiencyMultiplier,
    int MaximumAuthorizedWage,
    int FriendshipHearts);

internal static class ConcurrentWorkerSelectionPolicy
{
    public static IReadOnlyList<string> NormalizeManualSelection(
        IEnumerable<string> workerNames)
    {
        ArgumentNullException.ThrowIfNull(workerNames);
        return workerNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<ConcurrentWorkerCandidate> Select(
        IEnumerable<ConcurrentWorkerCandidate> candidates,
        int maximumWorkers,
        int availableGold)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        int limit = ContractSettingsPolicy.NormalizeMaximumConcurrentWorkers(maximumWorkers);
        int remainingGold = Math.Max(0, availableGold);
        List<ConcurrentWorkerCandidate> selected = new(limit);
        HashSet<string> seenNames = new(StringComparer.OrdinalIgnoreCase);

        foreach (ConcurrentWorkerCandidate candidate in candidates
                     .Where(IsValid)
                     .OrderByDescending(candidate => candidate.EfficiencyMultiplier)
                     .ThenBy(candidate => candidate.MaximumAuthorizedWage)
                     .ThenByDescending(candidate => candidate.FriendshipHearts)
                     .ThenBy(candidate => candidate.WorkerName, StringComparer.Ordinal))
        {
            if (selected.Count >= limit)
                break;
            if (!seenNames.Add(candidate.WorkerName)
                || candidate.MaximumAuthorizedWage > remainingGold)
                continue;

            selected.Add(candidate);
            remainingGold -= candidate.MaximumAuthorizedWage;
        }

        return selected;
    }

    private static bool IsValid(ConcurrentWorkerCandidate candidate)
    {
        return !string.IsNullOrWhiteSpace(candidate.WorkerName)
            && WorkerEfficiencyProfiles.IsValidMultiplier(candidate.EfficiencyMultiplier)
            && candidate.MaximumAuthorizedWage > 0
            && candidate.FriendshipHearts is >= 0 and <= 14;
    }
}
