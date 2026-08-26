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
    public string[] PreviousSelectedWorkerNames { get; set; } = Array.Empty<string>();
    public RecurringEvaluationData LastEvaluation { get; set; } = new();
}

internal sealed class RecurringEvaluationData
{
    public int TotalDays { get; set; } = -1;
    public string RunId { get; set; } = "";
    public RecurringEvaluationStatus Status { get; set; }
    public string SelectedWorkerName { get; set; } = "";
    public string[] SelectedWorkerNames { get; set; } = Array.Empty<string>();
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
    public const int SchemaVersion = 3;
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
            || template.Task != NamedFarmTask.FarmWork
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
            || template.PreviousSelectedWorkerNames is null
            || template.PreviousSelectedWorkerNames.Length > ContractSettingsPolicy.MaximumMaximumConcurrentWorkers
            || template.PreviousSelectedWorkerNames.Any(name => !WorkerEfficiencyProfiles.HasExplicitProfile(name))
            || template.PreviousSelectedWorkerNames.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != template.PreviousSelectedWorkerNames.Length
            || (template.PreviousSelectedWorkerNames.Length == 0
                ? !string.IsNullOrWhiteSpace(template.PreviousSelectedWorkerName)
                : !string.Equals(
                    template.PreviousSelectedWorkerName,
                    template.PreviousSelectedWorkerNames[0],
                    StringComparison.OrdinalIgnoreCase))
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

    public static RecurringContractSaveData Upgrade(RecurringContractSaveData state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion is < 1 or > SchemaVersion)
            return state;

        if (state.SchemaVersion == 1)
        {
            if (state.Template?.Task is NamedFarmTask.Watering or NamedFarmTask.Harvesting)
                state.Template.Task = NamedFarmTask.FarmWork;
            state.SchemaVersion = 2;
        }
        if (state.SchemaVersion == 2)
        {
            if (state.Template is { } template)
            {
                template.PreviousSelectedWorkerNames = string.IsNullOrWhiteSpace(template.PreviousSelectedWorkerName)
                    ? Array.Empty<string>()
                    : new[] { template.PreviousSelectedWorkerName };
                template.LastEvaluation.SelectedWorkerNames = string.IsNullOrWhiteSpace(
                    template.LastEvaluation.SelectedWorkerName)
                        ? Array.Empty<string>()
                        : new[] { template.LastEvaluation.SelectedWorkerName };
            }
            state.SchemaVersion = SchemaVersion;
        }
        return state;
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

        if (evaluation.SelectedWorkerNames is null
            || evaluation.SelectedWorkerNames.Length > ContractSettingsPolicy.MaximumMaximumConcurrentWorkers
            || evaluation.SelectedWorkerNames.Any(name => !WorkerEfficiencyProfiles.HasExplicitProfile(name))
            || evaluation.SelectedWorkerNames.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != evaluation.SelectedWorkerNames.Length
            || (evaluation.SelectedWorkerNames.Length > 0
                && !string.Equals(
                    evaluation.SelectedWorkerName,
                    evaluation.SelectedWorkerNames[0],
                    StringComparison.OrdinalIgnoreCase)))
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
                && evaluation.SelectedWorkerNames.Length == 0
                && string.IsNullOrWhiteSpace(evaluation.ReasonKey)
                && evaluation.AuthorizedGold == 0
                && evaluation.CompletedWork == 0
                && evaluation.ChargedGold == 0
                && evaluation.RefundedGold == 0,
            RecurringEvaluationStatus.Started => Guid.TryParseExact(evaluation.RunId, "N", out _)
                && !string.IsNullOrWhiteSpace(evaluation.SelectedWorkerName)
                && evaluation.SelectedWorkerNames.Length > 0
                && string.IsNullOrWhiteSpace(evaluation.ReasonKey)
                && evaluation.AuthorizedGold > 0
                && evaluation.CompletedWork == 0
                && evaluation.ChargedGold == 0
                && evaluation.RefundedGold == 0,
            RecurringEvaluationStatus.Completed =>
                Guid.TryParseExact(evaluation.RunId, "N", out _)
                && !string.IsNullOrWhiteSpace(evaluation.SelectedWorkerName)
                && evaluation.SelectedWorkerNames.Length > 0
                && string.IsNullOrWhiteSpace(evaluation.ReasonKey)
                && evaluation.AuthorizedGold > 0
                && evaluation.CompletedWork > 0
                && evaluation.ChargedGold > 0,
            RecurringEvaluationStatus.Stopped => Guid.TryParseExact(evaluation.RunId, "N", out _)
                && !string.IsNullOrWhiteSpace(evaluation.SelectedWorkerName)
                && evaluation.SelectedWorkerNames.Length > 0
                && !string.IsNullOrWhiteSpace(evaluation.ReasonKey)
                && evaluation.AuthorizedGold > 0,
            RecurringEvaluationStatus.Skipped => Guid.TryParseExact(evaluation.RunId, "N", out _)
                && string.IsNullOrWhiteSpace(evaluation.SelectedWorkerName)
                && evaluation.SelectedWorkerNames.Length == 0
                && !string.IsNullOrWhiteSpace(evaluation.ReasonKey)
                && evaluation.AuthorizedGold == 0
                && evaluation.CompletedWork == 0
                && evaluation.ChargedGold == 0
                && evaluation.RefundedGold == 0,
            _ => false
        };
    }
}
