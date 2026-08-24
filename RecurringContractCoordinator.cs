using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

namespace EvilFarmOwner;

internal sealed class RecurringContractCoordinator
{
    internal const string SaveDataKey = "recurring-contract";

    private readonly IModHelper Helper;
    private readonly ITranslationHelper Translation;
    private readonly IMonitor Monitor;
    private readonly WorkerRosterService WorkerRoster;
    private readonly MultiplayerContractCoordinator MultiplayerContracts;
    private RecurringContractSaveData State = CreateEmptyState();
    private bool PersistenceHealthy = true;

    public RecurringContractCoordinator(
        IModHelper helper,
        ITranslationHelper translation,
        IMonitor monitor,
        WorkerRosterService workerRoster,
        MultiplayerContractCoordinator multiplayerContracts)
    {
        this.Helper = helper;
        this.Translation = translation;
        this.Monitor = monitor;
        this.WorkerRoster = workerRoster;
        this.MultiplayerContracts = multiplayerContracts;
    }

    public RecurringContractTemplateData? Template => this.State.Template;

    public bool IsPersistenceHealthy => this.PersistenceHealthy;

    public void OnSaveLoaded()
    {
        this.State = CreateEmptyState();
        this.PersistenceHealthy = true;
        if (!Context.IsMainPlayer)
            return;

        try
        {
            RecurringContractSaveData? saved = this.Helper.Data.ReadSaveData<RecurringContractSaveData>(SaveDataKey);
            if (saved is null)
                return;
            if (!RecurringContractPolicy.IsValid(saved))
            {
                this.PersistenceHealthy = false;
                this.Monitor.Log(
                    "Recurring contract save data failed validation; automatic dispatch is disabled fail-closed.",
                    LogLevel.Error);
                Game1.addHUDMessage(new HUDMessage(
                    this.Translation.Get("recurring.hud.invalid-state"),
                    HUDMessage.error_type));
                return;
            }

            this.State = saved;
        }
        catch (Exception ex)
        {
            this.PersistenceHealthy = false;
            this.Monitor.Log($"Could not load recurring contract state safely: {ex}", LogLevel.Error);
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("recurring.hud.invalid-state"),
                HUDMessage.error_type));
        }
    }

    public void OnSaving()
    {
        if (!Context.IsMainPlayer || !this.PersistenceHealthy)
            return;

        try
        {
            if (!RecurringContractPolicy.IsValid(this.State))
                throw new InvalidDataException("Recurring contract state became invalid before saving.");
            this.Helper.Data.WriteSaveData(SaveDataKey, this.State);
        }
        catch (Exception ex)
        {
            this.PersistenceHealthy = false;
            this.Monitor.Log($"Could not persist recurring contract state safely: {ex}", LogLevel.Error);
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("recurring.hud.save-failed"),
                HUDMessage.error_type));
        }
    }

    public void OnReturnedToTitle()
    {
        this.State = CreateEmptyState();
        this.PersistenceHealthy = true;
    }

    public bool CreateTemplate(
        string preferredWorkerName,
        NamedFarmTask task,
        IReadOnlyCollection<string> approvedSubstituteNames,
        bool allowSubstitutes,
        bool allowRestDays)
    {
        if (!Context.IsWorldReady
            || !Context.IsMainPlayer
            || !this.PersistenceHealthy
            || approvedSubstituteNames is null
            || !WorkerEfficiencyProfiles.HasExplicitProfile(preferredWorkerName))
            return false;

        if (allowSubstitutes
            && approvedSubstituteNames.Any(name => !WorkerEfficiencyProfiles.HasExplicitProfile(name)))
            return false;

        string[] substitutes = allowSubstitutes
            ? approvedSubstituteNames
                .Where(name => !string.Equals(name, preferredWorkerName, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();
        string[] authorizedWorkers = new[] { preferredWorkerName }.Concat(substitutes).ToArray();
        int maximumRegularGold = authorizedWorkers.Max(name => this.GetMaximumWage(name, task, dayOfMonth: 1));
        int maximumRestGold = allowRestDays
            ? authorizedWorkers.Max(name => this.GetMaximumWage(name, task, dayOfMonth: 6))
            : 0;

        RecurringContractTemplateData template = new()
        {
            Enabled = true,
            Task = task,
            PreferredWorkerName = preferredWorkerName,
            WorkerMode = allowSubstitutes
                ? RecurringWorkerMode.PreferredWithApprovedSubstitutes
                : RecurringWorkerMode.FixedWorkerOnly,
            ApprovedSubstituteNames = substitutes,
            MaximumRegularDayGold = maximumRegularGold,
            AllowRestDays = allowRestDays,
            MaximumRestDayGold = maximumRestGold,
            LastProcessedTotalDays = Game1.Date.TotalDays,
            LastEvaluation = new RecurringEvaluationData()
        };
        if (!RecurringContractPolicy.IsValid(template))
            return false;

        this.State.Template = template;
        return true;
    }

    public bool Pause()
    {
        if (!this.CanEdit() || this.State.Template is not { } template)
            return false;

        template.Enabled = false;
        return true;
    }

    public bool Resume()
    {
        if (!this.CanEdit() || this.State.Template is not { } template)
            return false;

        template.Enabled = true;
        template.LastProcessedTotalDays = Game1.Date.TotalDays;
        return true;
    }

    public bool Delete()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return false;

        if (!this.PersistenceHealthy)
        {
            this.State = CreateEmptyState();
            this.PersistenceHealthy = true;
            return true;
        }

        if (this.State.Template is null)
            return false;
        this.State.Template = null;
        return true;
    }

    public void Update(bool hasActiveNamedContract)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer || !this.PersistenceHealthy)
            return;

        this.RefreshLatestResult();
        RecurringContractTemplateData? template = this.State.Template;
        if (template is null)
            return;

        if (!RecurringContractPolicy.CanWaitForEvaluation(
                template.Enabled,
                Game1.Date.TotalDays,
                template.LastProcessedTotalDays,
                Game1.timeOfDay))
        {
            if (template.Enabled
                && template.LastProcessedTotalDays != Game1.Date.TotalDays
                && Game1.timeOfDay > RecurringContractPolicy.EvaluationWindowEnd)
                this.Skip(template, "recurring.reason.window-missed", Array.Empty<RecurringCandidateRejectionData>());
            return;
        }

        if (Game1.isFestival())
        {
            this.Skip(template, "roster.reason.festival", Array.Empty<RecurringCandidateRejectionData>());
            return;
        }

        if (Game1.eventUp)
        {
            this.Skip(template, "roster.reason.event", Array.Empty<RecurringCandidateRejectionData>());
            return;
        }

        if (Game1.activeClickableMenu is not null
            || Game1.player.currentLocation is not Farm currentFarm
            || !ReferenceEquals(currentFarm, Game1.getFarm()))
            return;

        string runId = Guid.NewGuid().ToString("N");
        template.LastProcessedTotalDays = Game1.Date.TotalDays;
        template.LastRunId = runId;

        if (hasActiveNamedContract)
        {
            this.Skip(template, "contract.start.already-active", Array.Empty<RecurringCandidateRejectionData>(), runId);
            return;
        }

        bool isRestDay = ContractPreviewService.Create(0, Game1.dayOfMonth).DayKind == ContractDayKind.RestDay;
        if (isRestDay && !template.AllowRestDays)
        {
            this.Skip(template, "recurring.reason.rest-day-disabled", Array.Empty<RecurringCandidateRejectionData>(), runId);
            return;
        }

        int authorizedCap = isRestDay
            ? template.MaximumRestDayGold
            : template.MaximumRegularDayGold;
        List<RecurringWorkerCandidate> candidates = new();
        List<RecurringCandidateRejectionData> rejections = new();
        foreach (string workerName in RecurringContractPolicy.GetAuthorizedWorkerNames(template))
        {
            if (!this.WorkerRoster.TryGetWorker(
                    workerName,
                    out NPC? worker,
                    out WorkerAvailabilityResult availability)
                || worker is null
                || availability.State != WorkerAvailabilityState.EligibleForPreview)
            {
                rejections.Add(new RecurringCandidateRejectionData
                {
                    WorkerName = workerName,
                    ReasonKey = GetAvailabilityReasonKey(availability.Reason)
                });
                continue;
            }

            int friendshipHearts = Game1.player.getFriendshipHeartLevelForNPC(worker.Name);
            WorkContractPreview preview = ContractPreviewService.Create(
                friendshipHearts,
                Game1.dayOfMonth,
                worker.Name,
                template.Task);
            RecurringBudgetFailure budgetFailure = RecurringContractPolicy.CheckBudget(
                preview.MaximumAuthorizedWage,
                authorizedCap,
                Game1.player.Money);
            if (budgetFailure == RecurringBudgetFailure.ExceedsAuthorizedCap)
            {
                rejections.Add(new RecurringCandidateRejectionData
                {
                    WorkerName = workerName,
                    ReasonKey = "recurring.reason.over-budget"
                });
                continue;
            }

            if (budgetFailure == RecurringBudgetFailure.InsufficientFunds)
            {
                rejections.Add(new RecurringCandidateRejectionData
                {
                    WorkerName = workerName,
                    ReasonKey = "recurring.reason.insufficient-funds"
                });
                continue;
            }

            candidates.Add(new RecurringWorkerCandidate(
                worker.Name,
                string.Equals(worker.Name, template.PreferredWorkerName, StringComparison.OrdinalIgnoreCase),
                preview.EfficiencyMultiplier,
                preview.MaximumAuthorizedWage,
                friendshipHearts,
                string.Equals(worker.Name, template.PreviousSelectedWorkerName, StringComparison.OrdinalIgnoreCase)));
        }

        RecurringWorkerCandidate? selected = RecurringContractPolicy.SelectCandidate(candidates);
        if (selected is null)
        {
            this.Skip(template, "recurring.reason.no-candidate", rejections, runId);
            return;
        }

        bool accepted = this.MultiplayerContracts.RequestStart(
            selected.WorkerName,
            template.Task,
            HarvestDestinationPolicy.AutomaticMode,
            runId);
        if (!accepted)
        {
            this.Skip(
                template,
                this.MultiplayerContracts.LastRequestFailureKey ?? "contract.failure.unknown",
                rejections,
                runId);
            return;
        }

        template.PreviousSelectedWorkerName = selected.WorkerName;
        template.LastEvaluation = new RecurringEvaluationData
        {
            TotalDays = Game1.Date.TotalDays,
            RunId = runId,
            Status = RecurringEvaluationStatus.Started,
            SelectedWorkerName = selected.WorkerName,
            AuthorizedGold = selected.MaximumAuthorizedWage,
            Rejections = rejections.ToArray()
        };
        Game1.addHUDMessage(new HUDMessage(
            this.Translation.Get("recurring.hud.started", new
            {
                worker = GetDisplayName(selected.WorkerName),
                task = this.Translation.Get(template.Task == NamedFarmTask.Watering
                    ? "contract.task.watering"
                    : "contract.task.harvesting"),
                gold = selected.MaximumAuthorizedWage
            }),
            HUDMessage.newQuest_type));
    }

    private bool CanEdit()
    {
        return Context.IsWorldReady && Context.IsMainPlayer && this.PersistenceHealthy;
    }

    private int GetMaximumWage(string workerName, NamedFarmTask task, int dayOfMonth)
    {
        int hearts = Game1.player.getFriendshipHeartLevelForNPC(workerName);
        return ContractPreviewService.Create(hearts, dayOfMonth, workerName, task).MaximumAuthorizedWage;
    }

    private void RefreshLatestResult()
    {
        RecurringContractTemplateData? template = this.State.Template;
        if (template?.LastEvaluation.Status != RecurringEvaluationStatus.Started
            || !this.MultiplayerContracts.TryGetRecentResult(
                Game1.player.UniqueMultiplayerID,
                out ContractResultMessage? result)
            || result is null
            || !string.Equals(result.RequestId, template.LastEvaluation.RunId, StringComparison.Ordinal))
            return;

        template.LastEvaluation.Status = result.Succeeded
            ? RecurringEvaluationStatus.Completed
            : RecurringEvaluationStatus.Stopped;
        template.LastEvaluation.ReasonKey = result.Succeeded ? "" : result.ReasonKey;
        template.LastEvaluation.CompletedWork = result.CompletedWork;
        template.LastEvaluation.ChargedGold = result.ChargedGold;
        template.LastEvaluation.RefundedGold = result.RefundedGold;
    }

    private void Skip(
        RecurringContractTemplateData template,
        string reasonKey,
        IReadOnlyCollection<RecurringCandidateRejectionData> rejections,
        string? runId = null)
    {
        string resolvedRunId = runId ?? Guid.NewGuid().ToString("N");
        template.LastProcessedTotalDays = Game1.Date.TotalDays;
        template.LastRunId = resolvedRunId;
        template.LastEvaluation = new RecurringEvaluationData
        {
            TotalDays = Game1.Date.TotalDays,
            RunId = resolvedRunId,
            Status = RecurringEvaluationStatus.Skipped,
            ReasonKey = reasonKey,
            Rejections = rejections.ToArray()
        };
        Game1.addHUDMessage(new HUDMessage(
            this.Translation.Get("recurring.hud.skipped", new
            {
                reason = this.Translation.Get(reasonKey)
            }),
            HUDMessage.error_type));
    }

    private static string GetAvailabilityReasonKey(WorkerAvailabilityReason reason)
    {
        return reason switch
        {
            WorkerAvailabilityReason.Child => "roster.reason.child",
            WorkerAvailabilityReason.UnsupportedCharacter => "roster.reason.unsupported-character",
            WorkerAvailabilityReason.ActiveFestival => "roster.reason.festival",
            WorkerAvailabilityReason.ActiveEvent => "roster.reason.event",
            WorkerAvailabilityReason.MissingLocation => "roster.reason.missing-location",
            WorkerAvailabilityReason.Sleeping => "roster.reason.sleeping",
            WorkerAvailabilityReason.IslandActivity => "roster.reason.island",
            WorkerAvailabilityReason.MedicalActivity => "roster.reason.medical",
            WorkerAvailabilityReason.WorkActivity => "roster.reason.work",
            WorkerAvailabilityReason.ControlledActivity => "roster.reason.controlled",
            WorkerAvailabilityReason.MovementActivity => "roster.reason.movement",
            WorkerAvailabilityReason.DialogueActivity => "roster.reason.dialogue",
            WorkerAvailabilityReason.ScriptedAnimation => "roster.reason.animation",
            WorkerAvailabilityReason.UnsupportedCustomNpc => "roster.reason.custom-npc",
            _ => "roster.reason.evaluation-failed"
        };
    }

    private static string GetDisplayName(string internalName)
    {
        NPC? npc = Utility.getAllCharacters()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, internalName, StringComparison.OrdinalIgnoreCase));
        return npc is null || string.IsNullOrWhiteSpace(npc.displayName)
            ? internalName
            : npc.displayName;
    }

    private static RecurringContractSaveData CreateEmptyState()
    {
        return new RecurringContractSaveData
        {
            SchemaVersion = RecurringContractPolicy.SchemaVersion
        };
    }
}
