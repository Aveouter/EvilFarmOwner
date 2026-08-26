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
        string[] names = workerInternalNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
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
            totalReservation += ContractPreviewService.Create(
                requester.getFriendshipHeartLevelForNPC(worker.Name),
                Game1.dayOfMonth,
                worker.Name,
                NamedFarmTask.FarmWork,
                settings).MaximumAuthorizedWage;
        }
        if (requester.Money < totalReservation)
            return this.FailStart("contract.start.insufficient-funds");

        Guid groupId = Guid.NewGuid();
        ActiveWorkerGroup group = new(groupId, requestId, requestingPlayerId, harvestDestination);
        IReadOnlyList<FarmWorkStageSelection> assignments = WorkStagePartitionPolicy.Partition(
            settings.EnabledStages,
            workers.Count);
        if (assignments.Any(assignment => assignment == FarmWorkStageSelection.None))
            return this.FailStart("farm-work.start.no-work");
        for (int index = 0; index < workers.Count; index++)
        {
            ContractSettingsSnapshot workerSettings = settings with { EnabledStages = assignments[index] };
            WorkerRuntime runtime = this.CreateRuntime(
                workerSettings,
                workers[index].Name,
                assignments[index],
                isRecovery: false);
            if (!runtime.FarmWork.TryStart(
                    requestingPlayerId,
                    workers[index].Name,
                    requestId,
                    harvestDestination))
            {
                this.LastStartFailureKey = runtime.FarmWork.LastStartFailureKey
                    ?? "contract.failure.unknown";
                if (this.LastStartFailureKey == "farm-work.start.no-work")
                    continue;
                foreach (WorkerRuntime started in group.Workers)
                    started.FarmWork.Cancel("contract.failure.group-start", mustFinalizeNow: true);
                this.DrainCancelled(group.Workers);
                return false;
            }
            group.Workers.Add(runtime);
        }
        if (group.Workers.Count == 0)
            return this.FailStart("farm-work.start.no-work");
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
        bool isRecovery)
    {
        WateringContractExecutionController watering = new(this.Translation, this.Monitor, this.WorkerRoster);
        HarvestingContractExecutionController harvesting = new(
            this.Translation,
            this.Monitor,
            this.WorkerRoster,
            this.AcceptanceFaults);
        AnimalCareContractExecutionController animalCare = new(this.Monitor, this.Translation);
        StorageSortContractExecutionController storage = new(
            this.Translation,
            this.Monitor,
            this.WorkerRoster,
            this.StorageRecovery);
        FarmWorkContractExecutionController farmWork = new(
            this.Translation,
            this.Monitor,
            this.WorkerRoster,
            harvesting,
            watering,
            animalCare,
            storage,
            () => settings);
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
            FarmWorkStageSelection failedStages = initial
                .Where(worker => worker.Completion?.Succeeded == false)
                .Aggregate(
                    FarmWorkStageSelection.None,
                    (stages, worker) => stages | worker.AssignedStages);
            WorkerRuntime? recoveryWorker = initial
                .Where(worker => worker.Completion?.Succeeded == true)
                .OrderBy(worker => worker.WorkerName, StringComparer.Ordinal)
                .FirstOrDefault();
            if (failedStages != FarmWorkStageSelection.None && recoveryWorker is not null)
            {
                ContractSettingsSnapshot settings = this.GetSettings();
                if (!settings.IsValid)
                    settings = ContractSettingsSnapshot.Default;
                settings = settings with { EnabledStages = failedStages };
                WorkerRuntime recovery = this.CreateRuntime(
                    settings,
                    recoveryWorker.WorkerName,
                    failedStages,
                    isRecovery: true);
                if (recovery.FarmWork.TryStart(
                        group.RequestingPlayerId,
                        recoveryWorker.WorkerName,
                        group.RequestId,
                        group.HarvestDestination,
                        isBillable: false))
                {
                    group.Workers.Add(recovery);
                    this.Monitor.Log(
                        $"Reassigned failed stages {failedStages} to {recoveryWorker.WorkerName} "
                        + $"for group {group.Id:N} without a second wage charge.",
                        LogLevel.Warn);
                    return;
                }

                group.RecoverySatisfied = recovery.FarmWork.LastStartFailureKey == "farm-work.start.no-work";
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
        bool succeeded = initialResults.All(result => result.Succeeded) || group.RecoverySatisfied;
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
            succeeded ? "" : results.First(result => !result.Succeeded).ReasonKey,
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
            results.SelectMany(result => result.CompletedTransfers).ToArray(),
            results.SelectMany(result => result.SkippedTransfers).ToArray())
        {
            HarvestDestination = group.HarvestDestination,
            WorkerSettlements = workerSettlements
        };
        this.ActiveGroup = null;
    }

    private static NamedContractCompletionState MergeWorkerResults(
        NamedContractCompletionState initial,
        IReadOnlyList<NamedContractCompletionState> recovery)
    {
        if (recovery.Count == 0)
            return initial;
        NamedContractCompletionState[] all = new[] { initial }.Concat(recovery).ToArray();
        return new NamedContractCompletionState(
            initial.ContractId,
            initial.RequestId,
            initial.RequestingPlayerId,
            initial.WorkerName,
            initial.Task,
            all.All(result => result.Succeeded),
            all.All(result => result.Succeeded)
                ? ""
                : all.First(result => !result.Succeeded).ReasonKey,
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
            HarvestDestinationMode harvestDestination)
        {
            this.Id = id;
            this.RequestId = requestId;
            this.RequestingPlayerId = requestingPlayerId;
            this.HarvestDestination = harvestDestination;
        }

        public Guid Id { get; }
        public string RequestId { get; }
        public long RequestingPlayerId { get; }
        public HarvestDestinationMode HarvestDestination { get; }
        public List<WorkerRuntime> Workers { get; } = new();
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
