using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

namespace EvilFarmOwner;

internal sealed class FarmWorkContractExecutionController
{
    private const int LatestStartTime = 1600;
    private const int HardStopTime = 2200;

    private readonly ITranslationHelper Translation;
    private readonly IMonitor Monitor;
    private readonly WorkerRosterService WorkerRoster;
    private readonly HarvestingContractExecutionController Harvesting;
    private readonly WateringContractExecutionController Watering;
    private readonly AnimalCareContractExecutionController AnimalCare;
    private readonly StorageSortContractExecutionController StorageSorting;
    private readonly Func<ContractSettingsSnapshot> GetSettings;
    private ActiveFarmWorkShift? ActiveShift;
    private NamedContractCompletionState? LastCompletion;

    public FarmWorkContractExecutionController(
        ITranslationHelper translation,
        IMonitor monitor,
        WorkerRosterService workerRoster,
        HarvestingContractExecutionController harvesting,
        WateringContractExecutionController watering,
        AnimalCareContractExecutionController animalCare,
        StorageSortContractExecutionController storageSorting,
        Func<ContractSettingsSnapshot>? getSettings = null)
    {
        this.Translation = translation;
        this.Monitor = monitor;
        this.WorkerRoster = workerRoster;
        this.Harvesting = harvesting;
        this.Watering = watering;
        this.AnimalCare = animalCare;
        this.StorageSorting = storageSorting;
        this.GetSettings = getSettings ?? (() => ContractSettingsSnapshot.Default);
    }

    public bool HasActiveContract => this.ActiveShift is not null;

    public string? ActiveContractId => this.ActiveShift?.Context.Id.ToString("N");

    public string? LastStartFailureKey { get; private set; }

    public bool TryStart(
        long requestingPlayerId,
        string workerInternalName,
        string requestId,
        HarvestDestinationMode harvestDestination)
    {
        this.LastStartFailureKey = null;
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return this.FailStart("contract.start.host-only");
        if (this.ActiveShift is not null)
            return this.FailStart("contract.start.already-active");
        if (Game1.timeOfDay > LatestStartTime)
            return this.FailStart("farm-work.start.too-late");
        if (!Guid.TryParseExact(requestId, "N", out _))
            return this.FailStart("multiplayer.reject.request-id");
        if (!HarvestDestinationPolicy.IsValidForTask(NamedFarmTask.FarmWork, harvestDestination))
            return this.FailStart("multiplayer.reject.destination");

        Farmer? requester = Game1.GetPlayer(requestingPlayerId, onlyOnline: true);
        if (requester is null)
            return this.FailStart("multiplayer.reject.player");
        Farm farm = Game1.getFarm();
        if (requester.currentLocation is not Farm currentFarm || !ReferenceEquals(farm, currentFarm))
            return this.FailStart("contract.start.must-be-on-farm");
        if (!this.WorkerRoster.TryGetWorker(
                workerInternalName,
                out NPC? worker,
                out WorkerAvailabilityResult availability)
            || worker is null)
            return this.FailStart("contract.start.worker-missing");
        if (availability.State != WorkerAvailabilityState.EligibleForPreview)
            return this.FailStart("contract.start.worker-unavailable");

        ContractSettingsSnapshot settings = this.GetSettings();
        if (!settings.IsValid)
            settings = ContractSettingsSnapshot.Default;
        WorkContractPreview preview = ContractPreviewService.Create(
            requester.getFriendshipHeartLevelForNPC(worker.Name),
            Game1.dayOfMonth,
            worker.Name,
            NamedFarmTask.FarmWork,
            settings);
        if (requester.Money < preview.MaximumAuthorizedWage)
            return this.FailStart("contract.start.insufficient-funds");
        if (!NpcWorkLease.TryAcquire(
                worker,
                preview.MaximumAuthorizedWage,
                this.Monitor,
                out NpcWorkLease? lease)
            || lease is null)
            return this.FailStart("contract.start.lease-failed");

        FarmWorkShiftContext context = new(
            Guid.NewGuid(),
            requestId,
            requester,
            lease,
            preview,
            harvestDestination,
            settings.EnabledStages);
        ActiveFarmWorkShift shift = new(context);
        this.ActiveShift = shift;
        requester.Money -= preview.MaximumAuthorizedWage;
        this.BeginNextAvailableStage(shift);
        if (this.ActiveShift is not null)
            return true;

        this.LastStartFailureKey ??= this.LastCompletion?.ReasonKey ?? "contract.failure.unknown";
        this.LastCompletion = null;
        return false;
    }

    public void Update()
    {
        ActiveFarmWorkShift? shift = this.ActiveShift;
        if (shift is null || !Context.IsWorldReady)
            return;

        if (shift.Finalizing)
        {
            shift.RestoreWaitTicks++;
            this.ContinueFinalization(
                shift,
                mustFinalizeNow: !Context.IsMainPlayer
                    || Game1.Date.TotalDays != shift.Context.Lease.StartTotalDays
                    || Game1.timeOfDay >= HardStopTime
                    || shift.RestoreWaitTicks >= NpcLeaseRecoveryPolicy.MaximumDeferredTicks);
            return;
        }

        NamedContractCompletionState? stageCompletion = this.ConsumeStageCompletion(shift.CurrentStage);
        if (stageCompletion is not null)
        {
            shift.StageCompletions.Add(stageCompletion);
            shift.LastFinishedStage = shift.CurrentStage;
            if (!stageCompletion.Succeeded)
            {
                this.BeginFinalization(shift, false, stageCompletion.ReasonKey);
                return;
            }
        }

        if (!Context.IsMainPlayer
            || Game1.Date.TotalDays != shift.Context.Lease.StartTotalDays
            || Game1.timeOfDay >= HardStopTime)
        {
            this.BeginFinalization(shift, false, "contract.failure.safety-stop", mustFinalizeNow: true);
            return;
        }

        if (stageCompletion is not null)
            this.BeginNextAvailableStage(shift);
    }

    public void OnDayEnding()
    {
        if (this.ActiveShift is { } shift)
        {
            this.CaptureCurrentStageCompletion(shift);
            this.BeginFinalization(shift, false, "contract.failure.day-ending", mustFinalizeNow: true);
        }
    }

    public void OnReturnedToTitle()
    {
        if (this.ActiveShift is { } shift && Context.IsWorldReady)
        {
            this.CaptureCurrentStageCompletion(shift);
            this.BeginFinalization(shift, false, "contract.failure.world-closed", mustFinalizeNow: true);
        }
        this.ActiveShift = null;
    }

    public NamedContractRuntimeState? GetRuntimeState()
    {
        ActiveFarmWorkShift? shift = this.ActiveShift;
        if (shift is null)
            return null;
        NamedContractRuntimeState? stage = shift.CurrentStage switch
        {
            FarmWorkStage.Harvesting => this.Harvesting.GetRuntimeState(),
            FarmWorkStage.Watering => this.Watering.GetRuntimeState(),
            FarmWorkStage.AnimalCare => this.AnimalCare.GetRuntimeState(),
            FarmWorkStage.StorageSorting => this.StorageSorting.GetRuntimeState(),
            _ => null
        };
        if (stage is null)
            return null;
        return stage with
        {
            Task = NamedFarmTask.FarmWork,
            Phase = FarmWorkPassPolicy.FormatRuntimePhase(
                shift.CurrentStage,
                shift.CurrentPass,
                stage.Phase),
            ReservedGold = shift.Context.BillingPreview.MaximumAuthorizedWage,
            StartTime = shift.Context.Lease.StartTime,
            CompletedWork = shift.StageCompletions.Sum(result => result.CompletedWork) + stage.CompletedWork
        };
    }

    public NamedContractCompletionState? ConsumeCompletion()
    {
        NamedContractCompletionState? completion = this.LastCompletion;
        this.LastCompletion = null;
        return completion;
    }

    private void BeginNextAvailableStage(ActiveFarmWorkShift shift)
    {
        FarmWorkStage stage = FarmWorkStagePolicy.GetNext(
            shift.LastFinishedStage,
            shift.Context.EnabledStages);
        while (stage != FarmWorkStage.Complete)
        {
            shift.CurrentStage = stage;
            bool started = stage switch
            {
                FarmWorkStage.Harvesting => this.Harvesting.TryStartManaged(shift.Context),
                FarmWorkStage.Watering => this.Watering.TryStartManaged(shift.Context),
                FarmWorkStage.AnimalCare => this.AnimalCare.TryStartManaged(shift.Context),
                FarmWorkStage.StorageSorting => this.StorageSorting.TryStartManaged(shift.Context),
                _ => false
            };
            if (started)
            {
                shift.Dispatched = true;
                return;
            }

            string? failureKey = this.GetStageStartFailure(stage);
            NamedContractCompletionState? failedDispatch = this.ConsumeStageCompletion(stage);
            if (failedDispatch is not null)
            {
                shift.StageCompletions.Add(failedDispatch);
                this.BeginFinalization(shift, false, failedDispatch.ReasonKey);
                return;
            }
            if (!FarmWorkStagePolicy.IsEmptyStageFailure(stage, failureKey))
            {
                this.BeginFinalization(
                    shift,
                    false,
                    failureKey ?? "contract.failure.unknown");
                return;
            }

            shift.LastFinishedStage = stage;
            stage = FarmWorkStagePolicy.GetNext(stage, shift.Context.EnabledStages);
        }

        if (FarmWorkPassPolicy.TryGetNext(shift.CurrentPass, out FarmWorkPass nextPass))
        {
            shift.CurrentPass = nextPass;
            shift.LastFinishedStage = null;
            this.Monitor.Log(
                $"Farm-work shift {shift.Context.Id:N} is starting its bounded reconciliation pass.",
                LogLevel.Debug);
            this.BeginNextAvailableStage(shift);
            return;
        }

        bool completedAnyWork = shift.StageCompletions.Any(result => result.CompletedWork > 0);
        if (!completedAnyWork)
            this.LastStartFailureKey = "farm-work.start.no-work";
        this.BeginFinalization(
            shift,
            completedAnyWork,
            completedAnyWork ? "" : "farm-work.start.no-work");
    }

    private string? GetStageStartFailure(FarmWorkStage stage)
    {
        return stage switch
        {
            FarmWorkStage.Harvesting => this.Harvesting.LastStartFailureKey,
            FarmWorkStage.Watering => this.Watering.LastStartFailureKey,
            FarmWorkStage.AnimalCare => this.AnimalCare.LastStartFailureKey,
            FarmWorkStage.StorageSorting => this.StorageSorting.LastStartFailureKey,
            _ => null
        };
    }

    private NamedContractCompletionState? ConsumeStageCompletion(FarmWorkStage stage)
    {
        return stage switch
        {
            FarmWorkStage.Harvesting => this.Harvesting.ConsumeCompletion(),
            FarmWorkStage.Watering => this.Watering.ConsumeCompletion(),
            FarmWorkStage.AnimalCare => this.AnimalCare.ConsumeCompletion(),
            FarmWorkStage.StorageSorting => this.StorageSorting.ConsumeCompletion(),
            _ => null
        };
    }

    private void CaptureCurrentStageCompletion(ActiveFarmWorkShift shift)
    {
        NamedContractCompletionState? completion = this.ConsumeStageCompletion(shift.CurrentStage);
        if (completion is null)
            return;
        shift.StageCompletions.Add(completion);
        shift.LastFinishedStage = shift.CurrentStage;
    }

    private void BeginFinalization(
        ActiveFarmWorkShift shift,
        bool succeeded,
        string reasonKey,
        bool mustFinalizeNow = false)
    {
        if (!ReferenceEquals(this.ActiveShift, shift))
            return;
        shift.Finalizing = true;
        shift.PendingSucceeded = succeeded;
        shift.PendingReasonKey = reasonKey;
        this.ContinueFinalization(shift, mustFinalizeNow);
    }

    private void ContinueFinalization(ActiveFarmWorkShift shift, bool mustFinalizeNow)
    {
        NpcLeaseRestoreResult restoreResult = shift.Context.Lease.Restore();
        NpcLeaseRecoveryAction action = NpcLeaseRecoveryPolicy.Select(
            restoreResult,
            shift.RestoreWaitTicks,
            mustFinalizeNow);
        if (action == NpcLeaseRecoveryAction.Retry)
            return;
        if (action == NpcLeaseRecoveryAction.Relinquish)
            restoreResult = shift.Context.Lease.RelinquishToConflictingController();

        WateringContractSettlement settlement = WateringContractSettlement.Create(
            shift.Context.BillingPreview,
            shift.Dispatched,
            shift.Context.Lease.StartTime,
            Game1.timeOfDay);
        shift.Context.Requester.Money += settlement.RefundedGold;
        bool succeeded = shift.PendingSucceeded && restoreResult == NpcLeaseRestoreResult.Restored;
        string reasonKey = succeeded
            ? ""
            : restoreResult != NpcLeaseRestoreResult.Restored
                ? restoreResult == NpcLeaseRestoreResult.Relinquished
                    ? "contract.failure.restore-relinquished"
                    : "contract.failure.restore-ownership-lost"
                : shift.PendingReasonKey;
        NamedContractCompletionState[] stages = shift.StageCompletions.ToArray();
        this.LastCompletion = new NamedContractCompletionState(
            shift.Context.Id.ToString("N"),
            shift.Context.RequestId,
            shift.Context.Requester.UniqueMultiplayerID,
            shift.Context.Lease.Worker.Name,
            NamedFarmTask.FarmWork,
            succeeded,
            reasonKey,
            stages.Sum(result => result.CompletedWork),
            stages.Sum(result => result.PlayerItems),
            stages.Sum(result => result.ChestItems),
            stages.Sum(result => result.OverflowItems),
            stages.Sum(result => result.QuarantinedItems),
            stages.Sum(result => result.DroppedItems),
            settlement.BillableHours,
            settlement.ChargedGold,
            settlement.RefundedGold,
            stages.SelectMany(result => result.ProducedItems).ToArray(),
            stages.SelectMany(result => result.CompletedTransferIds).Distinct(StringComparer.Ordinal).ToArray(),
            stages.SelectMany(result => result.CompletedTransfers).ToArray(),
            stages.SelectMany(result => result.SkippedTransfers).ToArray())
        {
            HarvestDestination = shift.Context.HarvestDestination
        };
        this.ActiveShift = null;

        Game1.addHUDMessage(new HUDMessage(
            this.Translation.Get(succeeded ? "farm-work.hud.completed" : "farm-work.hud.stopped", new
            {
                worker = shift.Context.Lease.Worker.displayName,
                reason = succeeded ? "" : this.Translation.Get(reasonKey),
                stages = stages.Count(result => result.Succeeded),
                work = stages.Sum(result => result.CompletedWork),
                hours = settlement.BillableHours,
                paid = settlement.ChargedGold,
                refunded = settlement.RefundedGold
            }),
            succeeded ? HUDMessage.newQuest_type : HUDMessage.error_type));
    }

    private bool FailStart(string key)
    {
        this.LastStartFailureKey = key;
        Game1.addHUDMessage(new HUDMessage(this.Translation.Get(key), HUDMessage.error_type));
        return false;
    }

    private sealed class ActiveFarmWorkShift
    {
        public ActiveFarmWorkShift(FarmWorkShiftContext context)
        {
            this.Context = context;
        }

        public FarmWorkShiftContext Context { get; }
        public List<NamedContractCompletionState> StageCompletions { get; } = new();
        public FarmWorkStage CurrentStage { get; set; } = FarmWorkStage.Harvesting;
        public FarmWorkPass CurrentPass { get; set; } = FarmWorkPass.Initial;
        public FarmWorkStage? LastFinishedStage { get; set; }
        public bool Dispatched { get; set; }
        public bool Finalizing { get; set; }
        public bool PendingSucceeded { get; set; }
        public string PendingReasonKey { get; set; } = "contract.failure.unknown";
        public int RestoreWaitTicks { get; set; }
    }
}
