namespace EvilFarmOwner;

internal enum RecurringWorkerMode
{
    FixedWorkerOnly,
    PreferredWithApprovedSubstitutes
}

internal enum RecurringEvaluationStatus
{
    None,
    Started,
    Completed,
    Stopped,
    Skipped
}

internal enum RecurringBudgetFailure
{
    None,
    ExceedsAuthorizedCap,
    InsufficientFunds
}

internal sealed class RecurringContractSaveData
{
    public int SchemaVersion { get; set; }
    public RecurringContractTemplateData? Template { get; set; }
}

internal sealed class RecurringContractTemplateData
{
    public bool Enabled { get; set; }
    public NamedFarmTask Task { get; set; }
    public string PreferredWorkerName { get; set; } = "";
    public RecurringWorkerMode WorkerMode { get; set; }
    public string[] ApprovedSubstituteNames { get; set; } = Array.Empty<string>();
    public int MaximumRegularDayGold { get; set; }
    public bool AllowRestDays { get; set; }
    public int MaximumRestDayGold { get; set; }
    public int LastProcessedTotalDays { get; set; } = -1;
    public string LastRunId { get; set; } = "";
    public string PreviousSelectedWorkerName { get; set; } = "";
    public RecurringEvaluationData LastEvaluation { get; set; } = new();
}

internal sealed class RecurringEvaluationData
{
    public int TotalDays { get; set; } = -1;
    public string RunId { get; set; } = "";
    public RecurringEvaluationStatus Status { get; set; }
    public string SelectedWorkerName { get; set; } = "";
    public string ReasonKey { get; set; } = "";
    public int AuthorizedGold { get; set; }
    public int CompletedWork { get; set; }
    public int ChargedGold { get; set; }
    public int RefundedGold { get; set; }
    public RecurringCandidateRejectionData[] Rejections { get; set; } =
        Array.Empty<RecurringCandidateRejectionData>();
}

internal sealed class RecurringCandidateRejectionData
{
    public string WorkerName { get; set; } = "";
    public string ReasonKey { get; set; } = "";
}

internal sealed record RecurringWorkerCandidate(
    string WorkerName,
    bool IsPreferred,
    decimal EfficiencyMultiplier,
    int MaximumAuthorizedWage,
    int FriendshipHearts,
    bool WorkedPreviousRun);

internal static class RecurringContractPolicy
{
    public const int SchemaVersion = 1;
    public const int MaximumStoredWorkers = 27;
    public const int MaximumStoredGold = 1_000_000;
    public const int EvaluationWindowStart = 610;
    public const int EvaluationWindowEnd = 1600;

    public static bool IsValid(RecurringContractSaveData? state)
    {
        if (state is null || state.SchemaVersion != SchemaVersion)
            return false;

        return state.Template is null || IsValid(state.Template);
    }

    public static bool IsValid(RecurringContractTemplateData template)
    {
        if (!Enum.IsDefined(template.Task)
            || template.Task == NamedFarmTask.StorageSorting
            || !Enum.IsDefined(template.WorkerMode)
            || !WorkerEfficiencyProfiles.HasExplicitProfile(template.PreferredWorkerName)
            || template.ApprovedSubstituteNames is null
            || template.ApprovedSubstituteNames.Length > MaximumStoredWorkers - 1
            || template.MaximumRegularDayGold <= 0
            || template.MaximumRegularDayGold > MaximumStoredGold
            || (template.AllowRestDays
                ? template.MaximumRestDayGold < template.MaximumRegularDayGold
                    || template.MaximumRestDayGold > MaximumStoredGold
                : template.MaximumRestDayGold != 0)
            || template.LastProcessedTotalDays < -1
            || (!string.IsNullOrWhiteSpace(template.LastRunId)
                && !Guid.TryParseExact(template.LastRunId, "N", out _))
            || (!string.IsNullOrWhiteSpace(template.PreviousSelectedWorkerName)
                && !WorkerEfficiencyProfiles.HasExplicitProfile(template.PreviousSelectedWorkerName))
            || !IsValid(template.LastEvaluation))
            return false;

        if (template.LastEvaluation.Status == RecurringEvaluationStatus.None
            ? !string.IsNullOrWhiteSpace(template.LastRunId)
            : !string.Equals(template.LastRunId, template.LastEvaluation.RunId, StringComparison.Ordinal))
            return false;

        HashSet<string> substitutes = new(StringComparer.OrdinalIgnoreCase);
        foreach (string workerName in template.ApprovedSubstituteNames)
        {
            if (!WorkerEfficiencyProfiles.HasExplicitProfile(workerName)
                || string.Equals(workerName, template.PreferredWorkerName, StringComparison.OrdinalIgnoreCase)
                || !substitutes.Add(workerName))
                return false;
        }

        return template.WorkerMode == RecurringWorkerMode.PreferredWithApprovedSubstitutes
            || substitutes.Count == 0;
    }

    public static RecurringWorkerCandidate? SelectCandidate(
        IEnumerable<RecurringWorkerCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates
            .OrderByDescending(candidate => candidate.IsPreferred)
            .ThenByDescending(candidate => candidate.EfficiencyMultiplier)
            .ThenBy(candidate => candidate.MaximumAuthorizedWage)
            .ThenByDescending(candidate => candidate.FriendshipHearts)
            .ThenByDescending(candidate => candidate.WorkedPreviousRun)
            .ThenBy(candidate => candidate.WorkerName, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static IReadOnlyList<string> GetAuthorizedWorkerNames(
        RecurringContractTemplateData template)
    {
        ArgumentNullException.ThrowIfNull(template);
        IEnumerable<string> names = new[] { template.PreferredWorkerName };
        if (template.WorkerMode == RecurringWorkerMode.PreferredWithApprovedSubstitutes)
            names = names.Concat(template.ApprovedSubstituteNames);
        return names.ToArray();
    }

    public static RecurringBudgetFailure CheckBudget(
        int maximumAuthorizedWage,
        int savedCap,
        int availableFunds)
    {
        if (maximumAuthorizedWage <= 0 || savedCap <= 0 || maximumAuthorizedWage > savedCap)
            return RecurringBudgetFailure.ExceedsAuthorizedCap;
        if (availableFunds < maximumAuthorizedWage)
            return RecurringBudgetFailure.InsufficientFunds;
        return RecurringBudgetFailure.None;
    }

    public static bool CanWaitForEvaluation(
        bool enabled,
        int currentTotalDays,
        int lastProcessedTotalDays,
        int timeOfDay)
    {
        return enabled
            && currentTotalDays != lastProcessedTotalDays
            && timeOfDay >= EvaluationWindowStart
            && timeOfDay <= EvaluationWindowEnd;
    }

    private static bool IsValid(RecurringEvaluationData? evaluation)
    {
        if (evaluation is null
            || !Enum.IsDefined(evaluation.Status)
            || evaluation.TotalDays < -1
            || evaluation.AuthorizedGold < 0
            || evaluation.AuthorizedGold > MaximumStoredGold
            || evaluation.CompletedWork < 0
            || evaluation.ChargedGold < 0
            || evaluation.ChargedGold > MaximumStoredGold
            || evaluation.RefundedGold < 0
            || evaluation.RefundedGold > MaximumStoredGold
            || evaluation.ChargedGold + evaluation.RefundedGold > evaluation.AuthorizedGold
            || evaluation.ReasonKey is null
            || evaluation.ReasonKey.Length > 200
            || evaluation.Rejections is null
            || evaluation.Rejections.Length > MaximumStoredWorkers)
            return false;

        if (!string.IsNullOrWhiteSpace(evaluation.RunId)
            && !Guid.TryParseExact(evaluation.RunId, "N", out _))
            return false;
        if (!string.IsNullOrWhiteSpace(evaluation.SelectedWorkerName)
            && !WorkerEfficiencyProfiles.HasExplicitProfile(evaluation.SelectedWorkerName))
            return false;

        foreach (RecurringCandidateRejectionData rejection in evaluation.Rejections)
        {
            if (rejection is null
                || !WorkerEfficiencyProfiles.HasExplicitProfile(rejection.WorkerName)
                || string.IsNullOrWhiteSpace(rejection.ReasonKey)
                || rejection.ReasonKey.Length > 200)
                return false;
        }

        return evaluation.Status switch
        {
            RecurringEvaluationStatus.None => string.IsNullOrWhiteSpace(evaluation.RunId)
                && string.IsNullOrWhiteSpace(evaluation.SelectedWorkerName)
                && string.IsNullOrWhiteSpace(evaluation.ReasonKey)
                && evaluation.AuthorizedGold == 0
                && evaluation.CompletedWork == 0
                && evaluation.ChargedGold == 0
                && evaluation.RefundedGold == 0,
            RecurringEvaluationStatus.Started => Guid.TryParseExact(evaluation.RunId, "N", out _)
                && !string.IsNullOrWhiteSpace(evaluation.SelectedWorkerName)
                && string.IsNullOrWhiteSpace(evaluation.ReasonKey)
                && evaluation.AuthorizedGold > 0
                && evaluation.CompletedWork == 0
                && evaluation.ChargedGold == 0
                && evaluation.RefundedGold == 0,
            RecurringEvaluationStatus.Completed =>
                Guid.TryParseExact(evaluation.RunId, "N", out _)
                && !string.IsNullOrWhiteSpace(evaluation.SelectedWorkerName)
                && string.IsNullOrWhiteSpace(evaluation.ReasonKey)
                && evaluation.AuthorizedGold > 0
                && evaluation.CompletedWork > 0
                && evaluation.ChargedGold > 0,
            RecurringEvaluationStatus.Stopped => Guid.TryParseExact(evaluation.RunId, "N", out _)
                && !string.IsNullOrWhiteSpace(evaluation.SelectedWorkerName)
                && !string.IsNullOrWhiteSpace(evaluation.ReasonKey)
                && evaluation.AuthorizedGold > 0,
            RecurringEvaluationStatus.Skipped => Guid.TryParseExact(evaluation.RunId, "N", out _)
                && string.IsNullOrWhiteSpace(evaluation.SelectedWorkerName)
                && !string.IsNullOrWhiteSpace(evaluation.ReasonKey)
                && evaluation.AuthorizedGold == 0
                && evaluation.CompletedWork == 0
                && evaluation.ChargedGold == 0
                && evaluation.RefundedGold == 0,
            _ => false
        };
    }
}
