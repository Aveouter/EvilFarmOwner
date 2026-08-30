using StardewModdingAPI;
using StardewValley;

namespace EvilFarmOwner;

/// <summary>Owns a deterministic group of independent farm-work shifts.</summary>
internal sealed class ConcurrentFarmWorkContractExecutionController
{
    private readonly ITranslationHelper Translation;
    private readonly IMonitor Monitor;
    private readonly WorkerRosterService WorkerRoster;
    private readonly StorageSortRecoveryManager StorageRecovery;
    private readonly HarvestAcceptanceFaults AcceptanceFaults;
    private readonly Func<ContractSettingsSnapshot> GetSettings;
    private ActiveWorkerGroup? ActiveGroup;
    private NamedContractCompletionState? LastCompletion;

    public ConcurrentFarmWorkContractExecutionController(
        ITranslationHelper translation,
        IMonitor monitor,
        WorkerRosterService workerRoster,
        StorageSortRecoveryManager storageRecovery,
        HarvestAcceptanceFaults acceptanceFaults,
        Func<ContractSettingsSnapshot> getSettings)
    {
        this.Translation = translation;
        this.Monitor = monitor;
        this.WorkerRoster = workerRoster;
        this.StorageRecovery = storageRecovery;
        this.AcceptanceFaults = acceptanceFaults;
        this.GetSettings = getSettings;
    }

    public bool HasActiveContract => this.ActiveGroup is not null;
    public string? ActiveContractId => this.ActiveGroup?.Id.ToString("N");
    public string? LastStartFailureKey { get; private set; }

    public bool TryStart(
        long requestingPlayerId,
        IReadOnlyList<string> workerInternalNames,
        string requestId,
        HarvestDestinationMode harvestDestination)
    {
        this.LastStartFailureKey = null;
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return this.FailStart("contract.start.host-only");
        if (this.ActiveGroup is not null)
            return this.FailStart("contract.start.already-active");
        if (!Guid.TryParseExact(requestId, "N", out _))
            return this.FailStart("multiplayer.reject.request-id");

        ContractSettingsSnapshot settings = this.GetSettings();
        if (!settings.IsValid)
            settings = ContractSettingsSnapshot.Default;
        string[] names = ConcurrentWorkerSelectionPolicy
            .NormalizeManualSelection(workerInternalNames)
            .ToArray();
        if (names.Length == 0 || names.Length > settings.MaximumConcurrentWorkers)
            return this.FailStart("multiplayer.reject.worker-count");

        Farmer? requester = Game1.GetPlayer(requestingPlayerId, onlyOnline: true);
        if (requester is null)
            return this.FailStart("multiplayer.reject.player");

        List<NPC> workers = new(names.Length);
        int totalReservation = 0;
        foreach (string name in names)
        {
            if (!this.WorkerRoster.TryGetWorker(name, out NPC? worker, out WorkerAvailabilityResult availability)
                || worker is null)
                return this.FailStart("contract.start.worker-missing");
            if (availability.State != WorkerAvailabilityState.EligibleForPreview)
                return this.FailStart("contract.start.worker-unavailable");
            workers.Add(worker);
            totalReservation += this.WorkerRoster.CreatePreview(
                requester,
                worker,
                NamedFarmTask.FarmWork,
                settings).MaximumAuthorizedWage;
        }
        workers.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        if (requester.Money < totalReservation)
            return this.FailStart("contract.start.insufficient-funds");

        Guid groupId = Guid.NewGuid();
        ActiveWorkerGroup group = new(
            groupId,
            requestId,
            requestingPlayerId,
            requester,
            harvestDestination,
            settings);
        IReadOnlyList<FarmWorkStageSelection> assignments = WorkStagePartitionPolicy.Partition(
            settings.EnabledStages,
            workers.Count);
        if (assignments.Any(assignment => assignment == FarmWorkStageSelection.None))
            return this.FailStart("farm-work.start.no-work");
        bool startedAnyWork = false;
        for (int index = 0; index < workers.Count; index++)
        {
            ContractSettingsSnapshot workerSettings = settings with { EnabledStages = assignments[index] };
            WorkerRuntime runtime = this.CreateRuntime(
                workerSettings,
                workers[index].Name,
                assignments[index],
                isRecovery: false,
                workClaims: group.WorkClaims,
                workforceRoutes: group.WorkforceRoutes);
            if (!runtime.FarmWork.TryStart(
                    requestingPlayerId,
                    workers[index].Name,
                    requestId,
                    harvestDestination,
                    resumeScheduleOnRestore: false))
            {
                this.LastStartFailureKey = runtime.FarmWork.LastStartFailureKey
                    ?? "contract.failure.unknown";
                runtime.Completion = runtime.FarmWork.ConsumeCompletion();
                if (this.LastStartFailureKey == "farm-work.start.no-work")
                {
                    group.Workers.Add(runtime);
                    continue;
                }
                foreach (WorkerRuntime started in group.Workers)
                {
                    if (started.Completion is null)
                        started.FarmWork.Cancel("contract.failure.group-start", mustFinalizeNow: true);
                }
                this.DrainCancelled(group.Workers);
                this.RefundCompletionCharge(runtime.Completion);
                runtime.FarmWork.ResumeDeferredSchedule();
                this.ResumeDeferredSchedules(group.Workers);
                return false;
            }
            group.Workers.Add(runtime);
            startedAnyWork = true;
        }
        if (!startedAnyWork)
        {
            this.ResumeDeferredSchedules(group.Workers);
            return this.FailStart("farm-work.start.no-work");
        }
        group.InitialWorkerCount = group.Workers.Count;

        this.ActiveGroup = group;
        this.Monitor.Log(
            $"Started concurrent farm-work group {groupId:N} with {workers.Count} worker(s).",
            LogLevel.Info);
        return true;
    }

    public void Update()
    {
        if (this.ActiveGroup is not { } group)
            return;

        group.WorkforceRoutes.Tick();
        foreach (WorkerRuntime runtime in group.Workers.Where(worker => worker.Completion is null))
        {
            runtime.Watering.Update();
            runtime.Harvesting.Update();
            runtime.AnimalCare.Update();
            runtime.StorageSorting.Update();
            runtime.FarmWork.Update();
            runtime.Completion = runtime.FarmWork.ConsumeCompletion();
        }

        if (group.Workers.All(worker => worker.Completion is not null))
            this.FinishOrRecover(group);
    }

    public void OnDayEnding()
    {
        if (this.ActiveGroup is not { } group)
            return;
        foreach (WorkerRuntime runtime in group.Workers.Where(worker => worker.Completion is null))
        {
            runtime.Watering.OnDayEnding();
            runtime.Harvesting.OnDayEnding();
            runtime.AnimalCare.OnDayEnding();
            runtime.StorageSorting.OnSaving();
            runtime.FarmWork.OnDayEnding();
            runtime.Completion = runtime.FarmWork.ConsumeCompletion();
        }
        if (group.Workers.All(worker => worker.Completion is not null))
            this.CompleteGroup(group);
    }

    public void OnReturnedToTitle()
    {
        if (this.ActiveGroup is { } group && Context.IsWorldReady)
        {
            foreach (WorkerRuntime runtime in group.Workers.Where(worker => worker.Completion is null))
            {
                runtime.Watering.OnReturnedToTitle();
                runtime.Harvesting.OnReturnedToTitle();
                runtime.AnimalCare.OnReturnedToTitle();
                runtime.StorageSorting.OnReturnedToTitle();
                runtime.FarmWork.OnReturnedToTitle();
                runtime.Completion = runtime.FarmWork.ConsumeCompletion();
            }
            this.ResumeDeferredSchedules(group.Workers);
        }
        this.ActiveGroup = null;
    }

    public IReadOnlyList<NamedContractRuntimeState> GetRuntimeStates()
    {
        if (this.ActiveGroup is not { } group)
            return Array.Empty<NamedContractRuntimeState>();
        return group.Workers
            .Where(worker => worker.Completion is null)
            .Select(worker => worker.FarmWork.GetRuntimeState())
            .OfType<NamedContractRuntimeState>()
            .Select(state => state with { ContractId = group.Id.ToString("N") })
            .ToArray();
    }

    public NamedContractRuntimeState? GetRuntimeState() => this.GetRuntimeStates().FirstOrDefault();

    public NamedContractCompletionState? ConsumeCompletion()
    {
        NamedContractCompletionState? completion = this.LastCompletion;
        this.LastCompletion = null;
        return completion;
    }

    private WorkerRuntime CreateRuntime(
        ContractSettingsSnapshot settings,
        string workerName,
        FarmWorkStageSelection assignedStages,
        bool isRecovery,
        RuntimeWorkClaimCoordinator workClaims,
        RuntimeWorkforceRouteCoordinator workforceRoutes)
    {
        WateringContractExecutionController watering = new(
            this.Translation,
            this.Monitor,
            this.WorkerRoster,
            workClaims,
            workforceRoutes);
        HarvestingContractExecutionController harvesting = new(
            this.Translation,
            this.Monitor,
            this.WorkerRoster,
            this.AcceptanceFaults,
            workClaims,
            workforceRoutes);
        AnimalCareContractExecutionController animalCare = new(
            this.Monitor,
            this.Translation,
            workforceRoutes);
        StorageSortContractExecutionController storage = new(
            this.Translation,
            this.Monitor,
            this.WorkerRoster,
            this.StorageRecovery,
            workforceRoutes);
        FarmWorkContractExecutionController farmWork = new(
            this.Translation,
            this.Monitor,
            this.WorkerRoster,
            harvesting,
            watering,
            animalCare,
            storage,
            () => settings,
            showCompletionHud: false);
        return new WorkerRuntime(
            watering,
            harvesting,
            animalCare,
            storage,
            farmWork,
            workerName,
            assignedStages,
            isRecovery);
    }

    private void FinishOrRecover(ActiveWorkerGroup group)
    {
        if (!group.RecoveryAttempted)
        {
            group.RecoveryAttempted = true;
            WorkerRuntime[] initial = group.Workers.Take(group.InitialWorkerCount).ToArray();
            WorkforceStageOutcome[] outcomes = initial.Select(ToStageOutcome).ToArray();
            WorkforceRecoveryDecision decision = WorkforceRecoveryPolicy.Select(outcomes);
            WorkerRuntime? recoveryWorker = decision.RecoveryWorkerId is null
                ? null
                : initial.First(worker => string.Equals(
                    worker.WorkerName,
                    decision.RecoveryWorkerId,
                    StringComparison.OrdinalIgnoreCase));
            if (decision.IsRequired && recoveryWorker is not null)
            {
                ContractSettingsSnapshot settings = group.Settings with
                {
                    EnabledStages = decision.FailedStages
                };
                WorkerRuntime recovery = this.CreateRuntime(
                    settings,
                    recoveryWorker.WorkerName,
                    decision.FailedStages,
                    isRecovery: true,
                    workClaims: group.WorkClaims,
                    workforceRoutes: group.WorkforceRoutes);
                bool recoveryIsBillable = recoveryWorker.Completion?.ReasonKey
                    == "farm-work.start.no-work";
                if (recovery.FarmWork.TryStart(
                        group.RequestingPlayerId,
                        recoveryWorker.WorkerName,
                        group.RequestId,
                        group.HarvestDestination,
                        isBillable: recoveryIsBillable,
                        resumeScheduleOnRestore: false,
                        authenticatedRequester: group.Requester))
                {
                    group.Workers.Add(recovery);
                    this.Monitor.Log(
                        $"Reassigned failed stages {decision.FailedStages} to {recoveryWorker.WorkerName} "
                        + $"for group {group.Id:N} without a second wage charge.",
                        LogLevel.Warn);
                    return;
                }

                group.RecoverySatisfied = recovery.FarmWork.LastStartFailureKey == "farm-work.start.no-work";
                recovery.FarmWork.ResumeDeferredSchedule();
            }
        }
        else if (group.Workers.Skip(group.InitialWorkerCount).Any())
        {
            group.RecoverySatisfied = group.Workers
                .Skip(group.InitialWorkerCount)
                .All(worker => worker.Completion?.Succeeded == true);
        }

        this.CompleteGroup(group);
    }

    private void CompleteGroup(ActiveWorkerGroup group)
    {
        NamedContractCompletionState[] results = group.Workers
            .Select(worker => worker.Completion!)
            .ToArray();
        NamedContractCompletionState[] initialResults = results.Take(group.InitialWorkerCount).ToArray();
        bool succeeded = WorkforceRecoveryPolicy.AreInitialOutcomesSuccessful(
                group.Workers.Take(group.InitialWorkerCount).Select(ToStageOutcome))
            || group.RecoverySatisfied;
        NamedContractCompletionState[] recoveryResults = results.Skip(group.InitialWorkerCount).ToArray();
        IEnumerable<NamedContractTransferState> terminalSkipped = recoveryResults.Length > 0
            ? recoveryResults.SelectMany(result => result.SkippedTransfers)
            : initialResults.SelectMany(result => result.SkippedTransfers);
        GroupTransferReportSet transferReports = GroupTransferReportPolicy.Create(
            results.SelectMany(result => result.CompletedTransfers),
            terminalSkipped,
            succeeded);
        string failureReason = results
            .FirstOrDefault(result => !result.Succeeded
                && result.ReasonKey != "farm-work.start.no-work")
            ?.ReasonKey
            ?? results.FirstOrDefault(result => !result.Succeeded)?.ReasonKey
            ?? "contract.failure.unknown";
        NamedContractCompletionState[] workerSettlements = initialResults
            .Select(initial => MergeWorkerResults(
                initial,
                group.Workers.Skip(group.InitialWorkerCount)
                    .Where(worker => string.Equals(
                        worker.WorkerName,
                        initial.WorkerName,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(worker => worker.Completion!)
                    .ToArray()))
            .ToArray();
        this.LastCompletion = new NamedContractCompletionState(
            group.Id.ToString("N"),
            group.RequestId,
            group.RequestingPlayerId,
            results[0].WorkerName,
            NamedFarmTask.FarmWork,
            succeeded,
            succeeded ? "" : failureReason,
            results.Sum(result => result.CompletedWork),
            results.Sum(result => result.PlayerItems),
            results.Sum(result => result.ChestItems),
            results.Sum(result => result.OverflowItems),
            results.Sum(result => result.QuarantinedItems),
            results.Sum(result => result.DroppedItems),
            results.Sum(result => result.BillableHours),
            results.Sum(result => result.ChargedGold),
            results.Sum(result => result.RefundedGold),
            results.SelectMany(result => result.ProducedItems).ToArray(),
            results.SelectMany(result => result.CompletedTransferIds).Distinct(StringComparer.Ordinal).ToArray(),
            transferReports.Completed,
            transferReports.Skipped)
        {
            HarvestDestination = group.HarvestDestination,
            WorkerSettlements = workerSettlements
        };
        string workerNames = string.Join(", ", initialResults
            .Select(result => Game1.getCharacterFromName(result.WorkerName)?.displayName
                ?? result.WorkerName));
        Game1.addHUDMessage(new HUDMessage(
            this.Translation.Get(
                succeeded ? "farm-work.group-hud.completed" : "farm-work.group-hud.stopped",
                new
                {
                    workers = workerNames,
                    reason = succeeded
                        ? ""
                        : this.Translation.Get(this.LastCompletion.ReasonKey),
                    work = this.LastCompletion.CompletedWork,
                    hours = this.LastCompletion.BillableHours,
                    paid = this.LastCompletion.ChargedGold,
                    refunded = this.LastCompletion.RefundedGold
                }),
            succeeded ? HUDMessage.newQuest_type : HUDMessage.error_type));
        this.ResumeDeferredSchedules(group.Workers);
        this.ActiveGroup = null;
    }

    private static NamedContractCompletionState MergeWorkerResults(
        NamedContractCompletionState initial,
        IReadOnlyList<NamedContractCompletionState> recovery)
    {
        if (recovery.Count == 0)
            return initial;
        NamedContractCompletionState[] all = new[] { initial }.Concat(recovery).ToArray();
        bool succeeded = (initial.Succeeded || initial.ReasonKey == "farm-work.start.no-work")
            && recovery.All(result => result.Succeeded);
        return new NamedContractCompletionState(
            initial.ContractId,
            initial.RequestId,
            initial.RequestingPlayerId,
            initial.WorkerName,
            initial.Task,
            succeeded,
            succeeded
                ? ""
                : recovery.First(result => !result.Succeeded).ReasonKey,
            all.Sum(result => result.CompletedWork),
            all.Sum(result => result.PlayerItems),
            all.Sum(result => result.ChestItems),
            all.Sum(result => result.OverflowItems),
            all.Sum(result => result.QuarantinedItems),
            all.Sum(result => result.DroppedItems),
            all.Sum(result => result.BillableHours),
            all.Sum(result => result.ChargedGold),
            all.Sum(result => result.RefundedGold),
            all.SelectMany(result => result.ProducedItems).ToArray(),
            all.SelectMany(result => result.CompletedTransferIds).Distinct(StringComparer.Ordinal).ToArray(),
            all.SelectMany(result => result.CompletedTransfers).ToArray(),
            all.SelectMany(result => result.SkippedTransfers).ToArray())
        {
            HarvestDestination = initial.HarvestDestination
        };
    }

    private void DrainCancelled(IEnumerable<WorkerRuntime> workers)
    {
        foreach (WorkerRuntime runtime in workers)
        {
            NamedContractCompletionState? cancelled = runtime.FarmWork.ConsumeCompletion();
            if (cancelled?.ChargedGold > 0
                && Game1.GetPlayer(cancelled.RequestingPlayerId, onlyOnline: true) is { } requester)
            {
                requester.Money += cancelled.ChargedGold;
            }
        }
    }

    private void RefundCompletionCharge(NamedContractCompletionState? completion)
    {
        if (completion?.ChargedGold > 0
            && Game1.GetPlayer(completion.RequestingPlayerId, onlyOnline: true) is { } requester)
        {
            requester.Money += completion.ChargedGold;
        }
    }

    private void ResumeDeferredSchedules(IEnumerable<WorkerRuntime> workers)
    {
        foreach (IGrouping<string, WorkerRuntime> group in workers
                     .Where(worker => !string.IsNullOrWhiteSpace(
                         worker.FarmWork.DeferredScheduleWorkerName))
                     .GroupBy(
                         worker => worker.FarmWork.DeferredScheduleWorkerName!,
                         StringComparer.OrdinalIgnoreCase))
        {
            group.First().FarmWork.ResumeDeferredSchedule();
        }
    }

    private static WorkforceStageOutcome ToStageOutcome(WorkerRuntime worker) => new(
        worker.WorkerName,
        worker.AssignedStages,
        worker.Completion?.Succeeded == true,
        worker.Completion?.ReasonKey == "farm-work.start.no-work");

    private bool FailStart(string key)
    {
        this.LastStartFailureKey = key;
        return false;
    }

    private sealed class ActiveWorkerGroup
    {
        public ActiveWorkerGroup(
            Guid id,
            string requestId,
            long requestingPlayerId,
            Farmer requester,
            HarvestDestinationMode harvestDestination,
            ContractSettingsSnapshot settings)
        {
            this.Id = id;
            this.RequestId = requestId;
            this.RequestingPlayerId = requestingPlayerId;
            this.Requester = requester;
            this.HarvestDestination = harvestDestination;
            this.Settings = settings;
        }

        public Guid Id { get; }
        public string RequestId { get; }
        public long RequestingPlayerId { get; }
        public Farmer Requester { get; }
        public HarvestDestinationMode HarvestDestination { get; }
        public ContractSettingsSnapshot Settings { get; }
        public List<WorkerRuntime> Workers { get; } = new();
        public RuntimeWorkClaimCoordinator WorkClaims { get; } = new();
        public RuntimeWorkforceRouteCoordinator WorkforceRoutes { get; } = new();
        public int InitialWorkerCount { get; set; }
        public bool RecoveryAttempted { get; set; }
        public bool RecoverySatisfied { get; set; }
    }

    private sealed class WorkerRuntime
    {
        public WorkerRuntime(
            WateringContractExecutionController watering,
            HarvestingContractExecutionController harvesting,
            AnimalCareContractExecutionController animalCare,
            StorageSortContractExecutionController storageSorting,
            FarmWorkContractExecutionController farmWork,
            string workerName,
            FarmWorkStageSelection assignedStages,
            bool isRecovery)
        {
            this.Watering = watering;
            this.Harvesting = harvesting;
            this.AnimalCare = animalCare;
            this.StorageSorting = storageSorting;
            this.FarmWork = farmWork;
            this.WorkerName = workerName;
            this.AssignedStages = assignedStages;
            this.IsRecovery = isRecovery;
        }

        public WateringContractExecutionController Watering { get; }
        public HarvestingContractExecutionController Harvesting { get; }
        public AnimalCareContractExecutionController AnimalCare { get; }
        public StorageSortContractExecutionController StorageSorting { get; }
        public FarmWorkContractExecutionController FarmWork { get; }
        public string WorkerName { get; }
        public FarmWorkStageSelection AssignedStages { get; }
        public bool IsRecovery { get; }
        public NamedContractCompletionState? Completion { get; set; }
    }
}
