using Microsoft.Xna.Framework;
using StardewModdingAPI;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Xml;
using System.Xml.Serialization;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Machines;
using StardewValley.Inventories;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Locations;
using StardewValley.Network;
using StardewValley.Objects;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;

namespace EvilFarmOwner;

internal sealed class HarvestingContractExecutionController
{
    internal const string OverflowInventoryId = "Aveouter.EvilFarmOwner/ContractOverflow";
    internal const string QuarantineInventoryId = "Aveouter.EvilFarmOwner/ContractQuarantine";
    internal const string QuarantineRecoveryDataKey = "Aveouter.EvilFarmOwner/QuarantineRecovery";
    internal const string QuarantineTransferDataKey = "Aveouter.EvilFarmOwner/QuarantineTransfer";

    private const int LatestStartTime = 1600;
    private const int StopAcquiringTime = 2100;
    private const int HardStopTime = 2200;
    private const int ActionStartTicks = 8;
    private const int ActionDurationTicks = 40;
    private const int MaximumTravelTicks = 3600;
    private const int MaximumStalledTravelTicks = 180;
    private const int MaximumReturnReplans = 3;
    private const int MaximumLockWaitTicks = 300;
    private const int MaximumOverflowWaitTicks = 600;
    private const int OverflowRetryIntervalTicks = 60;

    private readonly ITranslationHelper Translation;
    private readonly IMonitor Monitor;
    private readonly WorkerRosterService WorkerRoster;
    private readonly HarvestTargetPlanner TargetPlanner;
    private readonly HarvestChestRouter ChestRouter;
    private readonly HarvestAcceptanceFaults AcceptanceFaults;
    private readonly RuntimeWorkClaimCoordinator? WorkClaims;
    private readonly RuntimeWorkforceRouteCoordinator? WorkforceRoutes;
    private ActiveHarvestContract? ActiveContract;
    private NamedContractCompletionState? LastCompletion;
    private bool HasPendingQuarantineRecovery;
    private int QuarantineRecoveryRetryTicks;

    public HarvestingContractExecutionController(
        ITranslationHelper translation,
        IMonitor monitor,
        WorkerRosterService workerRoster,
        HarvestAcceptanceFaults? acceptanceFaults = null,
        RuntimeWorkClaimCoordinator? workClaims = null,
        RuntimeWorkforceRouteCoordinator? workforceRoutes = null)
    {
        this.Translation = translation;
        this.Monitor = monitor;
        this.WorkerRoster = workerRoster;
        this.TargetPlanner = new HarvestTargetPlanner(monitor);
        this.ChestRouter = new HarvestChestRouter(monitor);
        this.AcceptanceFaults = acceptanceFaults ?? new HarvestAcceptanceFaults();
        this.WorkClaims = workClaims;
        this.WorkforceRoutes = workforceRoutes;
    }

    public bool HasActiveContract => this.ActiveContract is not null;

    public string? LastStartFailureKey { get; private set; }

    public string? ActiveContractId => this.ActiveContract?.Id.ToString("N");

    public bool HasUnresolvedQuarantineRecovery => this.HasPendingQuarantineRecovery;

    public void OnSaveLoaded()
    {
        this.HasPendingQuarantineRecovery = false;
        this.QuarantineRecoveryRetryTicks = 0;
        if (Context.IsWorldReady && Context.IsMainPlayer)
            this.TryRestoreQuarantineRecovery(showHud: true);
    }

    public bool TryRecoverQuarantinedCargo()
    {
        return this.TryRestoreQuarantineRecovery(showHud: true);
    }

    public bool TryStartManaged(FarmWorkShiftContext shift)
    {
        Farm farm = Game1.getFarm();
        FarmWorkLocationPlan mainFarm = FarmWorkLocationScope.Create(
            farm,
            FarmWorkScopeSelection.MainFarm)[0];
        return this.TryStartManaged(shift, mainFarm);
    }

    public bool TryStartManaged(
        FarmWorkShiftContext shift,
        FarmWorkLocationPlan locationPlan)
    {
        this.LastStartFailureKey = null;
        if ((this.HasPendingQuarantineRecovery || this.HasStoredQuarantineRecovery())
            && !this.TryRestoreQuarantineRecovery(showHud: false))
            return this.FailManagedStart("harvest.start.quarantine-pending");
        if (this.ActiveContract is not null)
            return this.FailManagedStart("contract.start.already-active");

        Farm farm = Game1.getFarm();
        GameLocation workLocation = locationPlan.Location;
        NPC worker = shift.Lease.Worker;
        HarvestPlanResult planResult = locationPlan.IsMainFarm
            ? this.TargetPlanner.TryCreate(
                farm,
                worker,
                isTargetAvailable: target => this.IsTargetAvailable(workLocation, worker, target))
            : this.TargetPlanner.TryCreate(
                workLocation,
                worker,
                locationPlan.ArrivalTile,
                target => this.IsTargetAvailable(workLocation, worker, target));
        if (!planResult.IsSuccess || planResult.Plan is null)
            return this.FailManagedStart(this.GetPlanFailureTranslationKey(planResult.Failure));
        if (!this.TryClaimTarget(workLocation, worker, planResult.Plan.FirstTarget.TargetTile))
            return this.FailManagedStart("farm-work.start.no-work");
        if (shift.HarvestDestination == HarvestDestinationMode.ClassifiedChests
            && !HarvestChestRouter.HasEligibleChest(farm))
            return this.FailManagedStart("harvest.start.no-storage-chest");

        WorkContractPreview preview = this.WorkerRoster.CreatePreview(
            shift.Requester,
            worker,
            NamedFarmTask.Harvesting,
            shift.Settings);
        ActiveHarvestContract contract = new(
            shift.Id,
            shift.RequestId,
            shift.Requester,
            shift.Lease,
            preview,
            farm,
            workLocation,
            planResult.Plan,
            shift.HarvestDestination,
            managedByShift: true,
            returnTile: locationPlan.IsMainFarm
                ? planResult.Plan.ArrivalTile
                : locationPlan.FarmReturnTile);
        this.ActiveContract = contract;

        try
        {
            Game1.warpCharacter(worker, workLocation, new Vector2(
                planResult.Plan.ArrivalTile.X,
                planResult.Plan.ArrivalTile.Y));
            worker.Position = FarmNavigationMap.GetAlignedCharacterPosition(planResult.Plan.ArrivalTile);
            worker.Halt();
            contract.Dispatched = true;
            if (worker.TilePoint == planResult.Plan.FirstTarget.InteractionTile)
            {
                this.OnArrivedAtTarget(worker, workLocation);
                return true;
            }

            PathFindController controller = this.CreatePathController(
                contract,
                planResult.Plan.FirstTarget.Path,
                planResult.Plan.FirstTarget.InteractionTile,
                planResult.Plan.FirstTarget.FacingDirection,
                this.OnArrivedAtTarget);
            contract.Controller = controller;
            shift.Lease.AttachController(controller);
            contract.TravelWatchdog.Reset(worker.Position.X, worker.Position.Y);
            return true;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Failed to start managed harvest stage: {ex}", LogLevel.Error);
            this.FinishContract(contract, false, "contract.failure.dispatch", mustFinalizeNow: true);
            return false;
        }
    }

    public bool TryStart(
        long requestingPlayerId,
        string workerInternalName,
        string requestId,
        HarvestDestinationMode destinationMode = HarvestDestinationMode.ClassifiedChests)
    {
        this.LastStartFailureKey = null;
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return this.FailStart("contract.start.host-only");
        if (!HarvestDestinationPolicy.IsValidForTask(
                NamedFarmTask.Harvesting,
                destinationMode))
            return this.FailStart("multiplayer.reject.destination");

        if ((this.HasPendingQuarantineRecovery || this.HasStoredQuarantineRecovery())
            && !this.TryRestoreQuarantineRecovery(showHud: false))
            return this.FailStart("harvest.start.quarantine-pending");

        if (this.ActiveContract is not null)
            return this.FailStart("contract.start.already-active");

        if (Game1.timeOfDay > LatestStartTime)
            return this.FailStart("harvest.start.too-late");

        Farmer? requester = Game1.GetPlayer(requestingPlayerId, onlyOnline: true);
        if (requester is null)
            return this.FailStart("multiplayer.reject.player");

        Farm mainFarm = Game1.getFarm();
        if (requester.currentLocation is not Farm currentFarm || !ReferenceEquals(mainFarm, currentFarm))
            return this.FailStart("contract.start.must-be-on-farm");

        if (!this.WorkerRoster.TryGetWorker(workerInternalName, out NPC? worker, out WorkerAvailabilityResult availability)
            || worker is null)
            return this.FailStart("contract.start.worker-missing");

        if (availability.State != WorkerAvailabilityState.EligibleForPreview)
        {
            this.LastStartFailureKey = "contract.start.worker-unavailable";
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("contract.start.worker-unavailable", new { worker = worker.displayName }),
                HUDMessage.error_type));
            return false;
        }

        WorkContractPreview preview = this.WorkerRoster.CreatePreview(
            requester,
            worker,
            NamedFarmTask.Harvesting);
        if (requester.Money < preview.MaximumAuthorizedWage)
        {
            this.LastStartFailureKey = "contract.start.insufficient-funds";
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("contract.start.insufficient-funds", new { gold = preview.MaximumAuthorizedWage }),
                HUDMessage.error_type));
            return false;
        }

        HarvestPlanResult planResult = this.TargetPlanner.TryCreate(
            mainFarm,
            worker,
            isTargetAvailable: target => this.IsTargetAvailable(mainFarm, worker, target));
        if (!planResult.IsSuccess || planResult.Plan is null)
            return this.FailStart(this.GetPlanFailureTranslationKey(planResult.Failure));
        if (!this.TryClaimTarget(mainFarm, worker, planResult.Plan.FirstTarget.TargetTile))
            return this.FailStart("farm-work.start.no-work");

        if (destinationMode == HarvestDestinationMode.ClassifiedChests
            && !HarvestChestRouter.HasEligibleChest(mainFarm))
            return this.FailStart("harvest.start.no-storage-chest");

        if (!NpcWorkLease.TryAcquire(
                worker,
                preview.MaximumAuthorizedWage,
                this.Monitor,
                out NpcWorkLease? lease)
            || lease is null)
            return this.FailStart("contract.start.lease-failed");

        ActiveHarvestContract contract = new(
            Guid.NewGuid(),
            requestId,
            requester,
            lease,
            preview,
            mainFarm,
            mainFarm,
            planResult.Plan,
            destinationMode);
        this.ActiveContract = contract;
        requester.Money -= preview.MaximumAuthorizedWage;

        try
        {
            Game1.warpCharacter(worker, mainFarm, new Vector2(
                planResult.Plan.ArrivalTile.X,
                planResult.Plan.ArrivalTile.Y));
            if (!ReferenceEquals(worker.currentLocation, mainFarm)
                || !mainFarm.characters.Contains(worker)
                || worker.TilePoint != planResult.Plan.ArrivalTile)
            {
                throw new InvalidOperationException(
                    $"Worker did not arrive at the planned farm-edge tile {planResult.Plan.ArrivalTile}.");
            }

            worker.Halt();
            this.Monitor.Log(
                $"Dispatching harvest worker '{worker.Name}' from {planResult.Plan.ArrivalSide} "
                + $"farm-boundary tile {planResult.Plan.ArrivalTile}; "
                + $"destination={destinationMode}.",
                LogLevel.Debug);

            if (worker.TilePoint == planResult.Plan.FirstTarget.InteractionTile)
            {
                this.OnArrivedAtTarget(worker, mainFarm);
                contract.Dispatched = true;
                Game1.addHUDMessage(new HUDMessage(
                    this.Translation.Get("harvest.hud.dispatched", new
                    {
                        worker = worker.displayName,
                        gold = preview.MaximumAuthorizedWage,
                        entrance = this.GetArrivalDescription(contract.Plan.ArrivalSide)
                    }),
                    HUDMessage.newQuest_type));
                return true;
            }

            PathFindController outbound = this.CreatePathController(
                contract,
                planResult.Plan.FirstTarget.Path,
                planResult.Plan.FirstTarget.InteractionTile,
                planResult.Plan.FirstTarget.FacingDirection,
                this.OnArrivedAtTarget);
            contract.Controller = outbound;
            lease.AttachController(outbound);
            contract.TravelWatchdog.Reset(worker.Position.X, worker.Position.Y);
            contract.Dispatched = true;

            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("harvest.hud.dispatched", new
                {
                    worker = worker.displayName,
                    gold = preview.MaximumAuthorizedWage,
                    entrance = this.GetArrivalDescription(contract.Plan.ArrivalSide)
                }),
                HUDMessage.newQuest_type));
            return true;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Failed to dispatch harvest worker '{worker.Name}': {ex}", LogLevel.Error);
            this.FinishContract(
                contract,
                succeeded: false,
                "contract.failure.dispatch",
                mustFinalizeNow: true);
            return false;
        }
    }

    public void Update()
    {
        ActiveHarvestContract? contract = this.ActiveContract;
        if (!Context.IsWorldReady)
            return;

        if (contract is null)
        {
            if (Context.IsMainPlayer
                && this.HasPendingQuarantineRecovery
                && ++this.QuarantineRecoveryRetryTicks % OverflowRetryIntervalTicks == 0)
                this.TryRestoreQuarantineRecovery(showHud: false);
            return;
        }

        if (contract.FinalizationPrepared)
        {
            contract.RestoreWaitTicks++;
            this.ContinueFinalization(
                contract,
                mustFinalizeNow: !Context.IsMainPlayer
                    || Game1.Date.TotalDays != contract.Lease.StartTotalDays
                    || Game1.timeOfDay >= HardStopTime
                    || contract.RestoreWaitTicks >= NpcLeaseRecoveryPolicy.MaximumDeferredTicks);
            return;
        }

        if (!Context.IsMainPlayer
            || Game1.Date.TotalDays != contract.Lease.StartTotalDays
            || Game1.timeOfDay >= HardStopTime)
        {
            this.FinishContract(
                contract,
                succeeded: false,
                "contract.failure.safety-stop",
                mustFinalizeNow: true);
            return;
        }

        if (contract.Phase is HarvestContractPhase.TravelingToTarget
                or HarvestContractPhase.TravelingToChest
                or HarvestContractPhase.Returning
            && this.WorkforceRoutes?.IsWaiting(contract.Lease.Worker.Name) == true)
            return;

        contract.PhaseTicks++;
        switch (contract.Phase)
        {
            case HarvestContractPhase.TravelingToTarget:
            case HarvestContractPhase.TravelingToChest:
            case HarvestContractPhase.Returning:
                this.UpdateTravel(contract);
                break;

            case HarvestContractPhase.Acting:
                if (!contract.ActionApplied && contract.PhaseTicks >= ActionStartTicks)
                {
                    contract.ActionApplied = true;
                    if (this.TryApplyHarvest(contract))
                        contract.HarvestedTargets++;
                    else
                        contract.SkippedTargets++;
                    contract.CompletedTargets.Add(contract.CurrentTarget.TargetTile);
                    this.CommitCurrentTarget(contract);
                }

                if (contract.PhaseTicks >= contract.ActionDurationTicks)
                {
                    contract.Lease.Worker.Sprite?.ClearAnimation();
                    this.ContinueHarvestOrDeliver(contract);
                }
                break;

            case HarvestContractPhase.WaitingForChestLock:
                if (contract.PhaseTicks >= MaximumLockWaitTicks)
                {
                    this.MarkCurrentChestAttempted(contract);
                    this.ReleaseCurrentChestLock(contract);
                    this.BeginDeliveryOrReturn(contract);
                }
                break;

            case HarvestContractPhase.WaitingForChestRelease:
                if (HarvestChestReleaseDelay.CanContinue(contract.PhaseTicks))
                    this.BeginDeliveryOrReturn(contract);
                break;

            case HarvestContractPhase.WaitingForOverflowLock:
                if (contract.PhaseTicks >= MaximumOverflowWaitTicks)
                {
                    this.DropCargoVisibly(contract, "persistent overflow stayed locked until timeout");
                    this.ContinueAfterCargoStorage(contract);
                }
                else if (!contract.OverflowLockRequested
                    && contract.PhaseTicks % OverflowRetryIntervalTicks == 0)
                {
                    this.RequestOverflowLock(contract);
                }
                break;

            case HarvestContractPhase.Returned:
                this.FinishContract(
                    contract,
                    !contract.StorageUnavailable
                        && contract.HarvestedTargets > 0
                        && contract.Cargo.Count == 0,
                    contract.StorageUnavailable
                        ? contract.StorageFailureTranslationKey
                        : contract.HarvestedTargets > 0
                            ? null
                            : "harvest.failure.target-invalidated");
                break;

            case HarvestContractPhase.QuarantiningCargo:
                this.FinishContract(
                    contract,
                    succeeded: false,
                    "harvest.failure.quarantine-pending");
                break;
        }
    }

    public void OnDayEnding()
    {
        this.OnSaving();
    }

    public void OnSaving()
    {
        ActiveHarvestContract? contract = this.ActiveContract;
        if (contract is null)
            return;

        this.FinishContract(
            contract,
            succeeded: false,
            "contract.failure.day-ending",
            mustFinalizeNow: true);
        if (!ReferenceEquals(this.ActiveContract, contract) || contract.Cargo.Count == 0)
            return;

        this.Monitor.Log(
            $"CRITICAL: contract {contract.Id:N} reached the save boundary without verified cargo "
            + "ownership; forcing the exact remainder into the private team quarantine before save.",
            LogLevel.Error);
        if (!this.TryForceQuarantineAtSaveBoundary(contract))
        {
            this.Monitor.Log(
                $"CRITICAL: save-boundary quarantine failed for contract {contract.Id:N}; "
                + "the active contract is being retained and must not be reported as finalized.",
                LogLevel.Error);
            return;
        }

        this.FinishContract(
            contract,
            succeeded: false,
            "harvest.failure.quarantine-pending",
            mustFinalizeNow: true);
    }

    public void OnReturnedToTitle()
    {
        if (this.ActiveContract is { } contract && Context.IsWorldReady)
        {
            this.FinishContract(
                contract,
                succeeded: false,
                "contract.failure.world-closed",
                mustFinalizeNow: true);
        }

        this.ActiveContract = null;
        this.HasPendingQuarantineRecovery = false;
        this.QuarantineRecoveryRetryTicks = 0;
    }

    public NamedContractRuntimeState? GetRuntimeState()
    {
        ActiveHarvestContract? contract = this.ActiveContract;
        if (contract is null)
            return null;

        return new NamedContractRuntimeState(
            contract.Id.ToString("N"),
            contract.RequestId,
            contract.Requester.UniqueMultiplayerID,
            contract.Lease.Worker.Name,
            NamedFarmTask.Harvesting,
            contract.Preview.EfficiencyMultiplier,
            contract.Phase.ToString(),
            contract.Plan.ArrivalTile.X,
            contract.Plan.ArrivalTile.Y,
            contract.Plan.ArrivalSide,
            contract.EntranceSwitches,
            contract.CurrentTarget.TargetTile.X,
            contract.CurrentTarget.TargetTile.Y,
            contract.Preview.MaximumAuthorizedWage,
            contract.Lease.StartTime,
            contract.HarvestedTargets,
            contract.Cargo.Select(entry => new NamedContractCargoState(
                entry.TransferId,
                entry.Item.QualifiedItemId,
                entry.Item.DisplayName,
                entry.Item.Quality,
                entry.Item.Stack)).ToArray(),
            contract.TransferLedger.GetCompletedTransferIds())
        {
            HarvestDestination = contract.DestinationMode
        };
    }

    public NamedContractCompletionState? ConsumeCompletion()
    {
        NamedContractCompletionState? completion = this.LastCompletion;
        this.LastCompletion = null;
        return completion;
    }

    private void UpdateTravel(ActiveHarvestContract contract)
    {
        if (this.TryCompleteTravelAtDestination(contract))
            return;

        if (contract.PhaseTicks > MaximumTravelTicks)
        {
            this.HandleInterruptedTravel(contract, TravelInterruptionKind.Timeout);
            return;
        }

        if (contract.Controller is not null
            && contract.Lease.Worker.controller is not null
            && !ReferenceEquals(contract.Lease.Worker.controller, contract.Controller))
        {
            TravelInterruptionSnapshot diagnostic = this.CaptureTravelInterruption(
                contract,
                TravelInterruptionKind.ControllerReplaced);
            this.Monitor.Log(
                $"Harvest travel interrupted: contract={contract.Id:N}, "
                + $"worker={contract.Lease.Worker.Name}, phase={contract.Phase}, "
                + diagnostic.ToTechnicalReason() + ".",
                LogLevel.Warn);
            this.FinishContract(contract, succeeded: false, "contract.failure.controller-conflict");
            return;
        }

        if ((Game1.activeClickableMenu is null || Game1.IsMultiplayer)
            && contract.TravelWatchdog.Tick(
                contract.Lease.Worker.Position.X,
                contract.Lease.Worker.Position.Y,
                new GridPoint(
                    contract.Lease.Worker.TilePoint.X,
                    contract.Lease.Worker.TilePoint.Y),
                MaximumStalledTravelTicks))
        {
            this.HandleInterruptedTravel(contract, TravelInterruptionKind.ProgressStall);
            return;
        }

        if (contract.PhaseTicks > 1 && contract.Lease.Worker.controller is null)
            this.HandleInterruptedTravel(contract, TravelInterruptionKind.ControllerEnded);
    }

    private bool TryCompleteTravelAtDestination(ActiveHarvestContract contract)
    {
        Point? destination = contract.Phase switch
        {
            HarvestContractPhase.TravelingToTarget => contract.CurrentTarget.InteractionTile,
            HarvestContractPhase.TravelingToChest => contract.CurrentChestRoute?.InteractionTile,
            HarvestContractPhase.Returning => contract.ReturnTile,
            _ => null
        };
        NPC worker = contract.Lease.Worker;
        GameLocation travelLocation = this.GetTravelLocation(contract);
        if (destination is null
            || !ReferenceEquals(worker.currentLocation, travelLocation)
            || worker.TilePoint != destination.Value)
            return false;

        if (worker.controller is not null && !ReferenceEquals(worker.controller, contract.Controller))
        {
            TravelInterruptionSnapshot diagnostic = this.CaptureTravelInterruption(
                contract,
                TravelInterruptionKind.ControllerReplaced);
            this.Monitor.Log(
                $"Harvest travel interrupted: contract={contract.Id:N}, "
                + $"worker={contract.Lease.Worker.Name}, phase={contract.Phase}, "
                + diagnostic.ToTechnicalReason() + ".",
                LogLevel.Warn);
            this.FinishContract(contract, succeeded: false, "contract.failure.controller-conflict");
            return true;
        }

        if (ReferenceEquals(worker.controller, contract.Controller))
            worker.controller = null;
        contract.Controller = null;
        worker.Position = FarmNavigationMap.GetAlignedCharacterPosition(destination.Value);
        worker.Halt();
        this.Monitor.Log(
            $"Harvest worker '{worker.Name}' entered destination tile {destination.Value} during {contract.Phase}; completing travel before vanilla pixel centering.",
            LogLevel.Debug);

        switch (contract.Phase)
        {
            case HarvestContractPhase.TravelingToTarget:
                this.OnArrivedAtTarget(worker, contract.WorkLocation);
                break;
            case HarvestContractPhase.TravelingToChest:
                this.OnArrivedAtChest(worker, contract.Farm);
                break;
            case HarvestContractPhase.Returning:
                this.OnReturnedToArrival(worker, contract.Farm);
                break;
        }

        return true;
    }

    private TravelInterruptionSnapshot CaptureTravelInterruption(
        ActiveHarvestContract contract,
        TravelInterruptionKind kind)
    {
        Point? destination = contract.Phase switch
        {
            HarvestContractPhase.TravelingToTarget => contract.CurrentTarget.InteractionTile,
            HarvestContractPhase.TravelingToChest => contract.CurrentChestRoute?.InteractionTile,
            HarvestContractPhase.Returning => contract.ReturnTile,
            _ => null
        };
        return TravelInterruptionRuntime.Capture(
            this.GetTravelLocation(contract),
            contract.Lease.Worker,
            contract.Controller,
            destination,
            kind,
            contract.TravelWatchdog.PreviousProgressTile);
    }

    private void HandleInterruptedTravel(
        ActiveHarvestContract contract,
        TravelInterruptionKind kind)
    {
        TravelInterruptionSnapshot diagnostic = this.CaptureTravelInterruption(contract, kind);
        this.Monitor.Log(
            $"Harvest travel interrupted: contract={contract.Id:N}, "
            + $"worker={contract.Lease.Worker.Name}, phase={contract.Phase}, "
            + diagnostic.ToTechnicalReason() + ".",
            LogLevel.Debug);
        if (contract.Phase == HarvestContractPhase.TravelingToTarget)
            this.RecordTargetObstacle(contract, diagnostic);
        if (contract.Phase == HarvestContractPhase.TravelingToChest
            && DeliveryRouteExclusionPolicy.TrySelectFailedTile(
                diagnostic.Origin,
                diagnostic.NextWaypoint,
                out GridPoint failedTile)
            && contract.FailedDeliveryTiles.Add(failedTile))
        {
            this.Monitor.Log(
                $"Excluded stalled delivery tile {failedTile}; future chest routes in contract "
                + $"{contract.Id:N} will replan around it.",
                LogLevel.Debug);
        }

        if (contract.Lease.Worker.controller is not null
            && ReferenceEquals(contract.Lease.Worker.controller, contract.Controller))
            contract.Lease.Worker.controller = null;
        contract.Lease.Worker.Halt();

        switch (contract.Phase)
        {
            case HarvestContractPhase.TravelingToTarget:
                if (this.TryHandleStalledEntrance(contract))
                    break;

                this.HandleFailedTargetRoute(
                    contract,
                    diagnostic.ToTechnicalReason(),
                    diagnostic.ReasonTranslationKey);
                break;

            case HarvestContractPhase.TravelingToChest:
                this.HandleFailedDeliveryRoute(
                    contract,
                    diagnostic.ToTechnicalReason(),
                    diagnostic.ReasonTranslationKey);
                break;

            case HarvestContractPhase.Returning:
                contract.ReturnReplanAttempts++;
                if (contract.ReturnReplanAttempts > MaximumReturnReplans)
                {
                    this.FinishContract(
                        contract,
                        succeeded: false,
                        kind == TravelInterruptionKind.Timeout
                            ? "contract.failure.return-timeout"
                            : "contract.failure.return-interrupted");
                    break;
                }

                this.BeginReturn(contract, depositOverflowOnReturn: false);
                break;
        }
    }

    private void RecordTargetObstacle(
        ActiveHarvestContract contract,
        TravelInterruptionSnapshot diagnostic)
    {
        TravelObstacleSelection selection = TravelRouteExclusionPolicy.Select(
            diagnostic.LocationKey,
            diagnostic.Origin,
            diagnostic.PreviousProgressTile,
            diagnostic.NextWaypoint);
        if (contract.TargetObstacles.Add(selection))
        {
            this.Monitor.Log(
                $"Harvest target routing excluded dynamic obstacle tile={selection.Tile}, "
                + $"edge={selection.Edge} for contract {contract.Id:N}.",
                LogLevel.Debug);
        }
    }

    private void HandleFailedTargetRoute(
        ActiveHarvestContract contract,
        string reason,
        string reasonTranslationKey)
    {
        FarmTaskRouteEdge failedEdge = WateringTargetPlanner.ToEdge(
            contract.CurrentTarget.TargetTile,
            contract.CurrentTarget.InteractionTile);
        contract.FailedEdges.Add(failedEdge);

        GridPoint origin = new(
            contract.Lease.Worker.TilePoint.X,
            contract.Lease.Worker.TilePoint.Y);
        TargetRouteFailureDecision decision = TargetRouteFailurePolicy.RecordFailure(
            contract.ReplanBudget,
            origin);
        if (decision.Action == TargetRouteFailureAction.RetryRoute)
        {
            this.Monitor.Log(
                $"Harvest target route {failedEdge} failed from {origin} ({reason}); "
                + $"trying another safe interaction edge "
                + $"[{decision.RouteFailureCount}/{decision.MaximumRouteFailures}].",
                LogLevel.Debug);
            this.ContinueHarvestOrDeliver(contract);
            return;
        }

        Point skippedTarget = contract.CurrentTarget.TargetTile;
        if (contract.CompletedTargets.Add(skippedTarget))
            contract.UnreachableTargets++;
        this.ReleaseTarget(contract, skippedTarget);
        if (decision.Action == TargetRouteFailureAction.SkipTarget)
        {
            this.Monitor.Log(
                $"Harvest target {skippedTarget} exhausted {decision.MaximumRouteFailures} live routes "
                + $"from {origin}; skipping only that crop and continuing "
                + $"[{decision.StalledTargetCount}/{decision.MaximumStalledTargets} stalled crops at this origin].",
                LogLevel.Warn);
            this.ContinueHarvestOrDeliver(contract);
            return;
        }

        int remaining = HarvestTargetPlanner.CountRemainingHarvestTargets(
            contract.WorkLocation,
            contract.CompletedTargets);
        contract.UnreachableTargets += remaining;
        contract.WorkLocationComplete = true;
        this.Monitor.Log(
            $"Harvest worker '{contract.Lease.Worker.Name}' exhausted "
            + $"{decision.MaximumStalledTargets} stalled harvest targets from {origin}; "
            + $"returning with {remaining} harvest target(s) marked unreachable.",
            LogLevel.Warn);
        Game1.addHUDMessage(new HUDMessage(
            this.Translation.Get("route.hud.target-routes-exhausted", new
            {
                worker = contract.Lease.Worker.displayName,
                origin = $"{origin.X},{origin.Y}",
                reason = this.Translation.Get(reasonTranslationKey)
            }),
            HUDMessage.error_type));
        if (contract.Cargo.Count > 0)
            this.BeginDeliveryOrReturn(contract);
        else
            this.BeginReturn(contract, depositOverflowOnReturn: false);
    }

    private void HandleFailedDeliveryRoute(
        ActiveHarvestContract contract,
        string reason,
        string reasonTranslationKey)
    {
        Point? failedChest = contract.CurrentChestRoute?.ChestTile;
        Point? failedInteraction = contract.CurrentChestRoute?.InteractionTile;
        this.MarkCurrentChestRouteAttempted(contract);

        GridPoint origin = new(
            contract.Lease.Worker.TilePoint.X,
            contract.Lease.Worker.TilePoint.Y);
        TravelReplanDecision decision = contract.ReplanBudget.RecordFailure(
            TravelRoutePurpose.Delivery,
            origin);
        if (decision.CanReplan)
        {
            this.Monitor.Log(
                $"Harvest delivery route from {origin} to chest {failedChest} "
                + $"through interaction {failedInteraction} failed ({reason}); "
                + $"trying another safe interaction route "
                + $"[{decision.FailureCount}/{decision.MaximumFailures}].",
                LogLevel.Debug);
            this.BeginDeliveryOrReturn(contract);
            return;
        }

        this.Monitor.Log(
            $"Harvest worker '{contract.Lease.Worker.Name}' exhausted "
            + $"{decision.MaximumFailures} consecutive delivery routes from {origin}; "
            + $"last chest={failedChest}, interaction={failedInteraction}; "
            + $"excludedTiles={string.Join(",", contract.FailedDeliveryTiles)}; "
            + $"lastReason={reason}; stopping the contract because no classified chest route remains safely reachable.",
            LogLevel.Warn);
        string chestLabel = failedChest.HasValue
            ? $"{failedChest.Value.X},{failedChest.Value.Y}"
            : "-";
        Game1.addHUDMessage(new HUDMessage(
            this.Translation.Get("harvest.hud.delivery-route-stopped", new
            {
                worker = contract.Lease.Worker.displayName,
                origin = $"{origin.X},{origin.Y}",
                chest = chestLabel,
                reason = this.Translation.Get(reasonTranslationKey)
            }),
            HUDMessage.error_type));
        this.StopForUnavailableStorage(contract, "delivery route retries were exhausted");
    }

    private bool TryHandleStalledEntrance(ActiveHarvestContract contract)
    {
        NPC worker = contract.Lease.Worker;
        if (!contract.IsMainFarm
            || contract.CompletedTargets.Count > 0
            || contract.Cargo.Count > 0
            || !ReferenceEquals(worker.currentLocation, contract.Farm)
            || worker.TilePoint != contract.Plan.ArrivalTile)
            return false;

        FarmBoundarySide failedSide = contract.Plan.ArrivalSide;
        contract.FailedArrivalSides.Add(failedSide);
        contract.Controller = null;
        this.Monitor.Log(
            $"Harvest worker '{worker.Name}' could not leave the {failedSide} entrance at "
            + $"{contract.Plan.ArrivalTile}; excluding that side and planning a boundary fallback.",
            LogLevel.Warn);

        HarvestPlanResult replacement = this.TargetPlanner.TryCreate(
            contract.Farm,
            worker,
            contract.FailedArrivalSides,
            target => this.IsTargetAvailable(contract.Farm, worker, target));
        if (!replacement.IsSuccess || replacement.Plan is null)
        {
            this.Monitor.Log(
                $"No remaining farm-boundary entrance can start harvesting after excluding: "
                + $"{string.Join(", ", contract.FailedArrivalSides.OrderBy(FarmEntranceSelection.GetEntrancePriority))}.",
                LogLevel.Warn);
            this.FinishContract(
                contract,
                succeeded: false,
                replacement.Failure == HarvestPlanFailure.NoHarvestTarget
                    ? "harvest.failure.target-invalidated"
                    : "contract.failure.entrance-stalled");
            return true;
        }

        try
        {
            HarvestWorkPlan nextPlan = replacement.Plan;
            if (!this.TryClaimTarget(contract.Farm, worker, nextPlan.FirstTarget.TargetTile))
                throw new InvalidOperationException("Fallback harvest target was claimed by another worker.");
            contract.Plan = nextPlan;
            contract.CurrentTarget = nextPlan.FirstTarget;
            contract.ActionApplied = false;
            contract.Phase = HarvestContractPhase.TravelingToTarget;
            contract.PhaseTicks = 0;
            contract.ReturnReplanAttempts = 0;
            contract.CurrentChestRoute = null;
            contract.FailedEdges.Clear();
            contract.ReplanBudget.Reset(TravelRoutePurpose.Target);
            contract.ReplanBudget.Reset(TravelRoutePurpose.Delivery);
            contract.ReplanBudget.Reset(TravelRoutePurpose.TargetSkip);
            contract.EntranceSwitches++;

            Game1.warpCharacter(worker, contract.Farm, new Vector2(
                nextPlan.ArrivalTile.X,
                nextPlan.ArrivalTile.Y));
            if (!ReferenceEquals(worker.currentLocation, contract.Farm)
                || !contract.Farm.characters.Contains(worker)
                || worker.TilePoint != nextPlan.ArrivalTile)
            {
                throw new InvalidOperationException(
                    $"Worker did not arrive at fallback farm-edge tile {nextPlan.ArrivalTile}.");
            }

            worker.Position = FarmNavigationMap.GetAlignedCharacterPosition(nextPlan.ArrivalTile);
            worker.Halt();
            if (worker.TilePoint == nextPlan.FirstTarget.InteractionTile)
            {
                this.OnArrivedAtTarget(worker, contract.Farm);
            }
            else
            {
                PathFindController controller = this.CreatePathController(
                    contract,
                    nextPlan.FirstTarget.Path,
                    nextPlan.FirstTarget.InteractionTile,
                    nextPlan.FirstTarget.FacingDirection,
                    this.OnArrivedAtTarget);
                contract.Controller = controller;
                contract.Lease.AttachController(controller);
                contract.TravelWatchdog.Reset(worker.Position.X, worker.Position.Y);
            }

            this.Monitor.Log(
                $"Harvest contract switched from the failed {failedSide} entrance to "
                + $"{nextPlan.ArrivalSide} at {nextPlan.ArrivalTile}.",
                LogLevel.Warn);
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("contract.hud.entrance-fallback", new
                {
                    worker = worker.displayName,
                    entrance = this.GetArrivalDescription(nextPlan.ArrivalSide)
                }),
                HUDMessage.newQuest_type));
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Failed to switch harvest worker '{worker.Name}' to a fallback entrance: {ex}",
                LogLevel.Error);
            this.FinishContract(contract, succeeded: false, "contract.failure.entrance-stalled");
        }

        return true;
    }

    private void OnArrivedAtTarget(Character character, GameLocation location)
    {
        ActiveHarvestContract? contract = this.ActiveContract;
        if (contract is null
            || !ReferenceEquals(character, contract.Lease.Worker)
            || !ReferenceEquals(location, contract.WorkLocation)
            || contract.Phase != HarvestContractPhase.TravelingToTarget)
            return;

        contract.Phase = HarvestContractPhase.Acting;
        contract.PhaseTicks = 0;
        TargetRouteFailurePolicy.ResetAfterArrival(contract.ReplanBudget);
        contract.Lease.Worker.Halt();
        contract.Lease.Worker.faceDirection(contract.CurrentTarget.FacingDirection);
        this.StartHarvestAnimation(contract.Lease.Worker);
    }

    private void OnArrivedAtChest(Character character, GameLocation location)
    {
        ActiveHarvestContract? contract = this.ActiveContract;
        if (contract is null
            || !ReferenceEquals(character, contract.Lease.Worker)
            || !ReferenceEquals(location, contract.Farm)
            || contract.Phase != HarvestContractPhase.TravelingToChest
            || contract.CurrentChestRoute is null)
            return;

        contract.Phase = HarvestContractPhase.WaitingForChestLock;
        contract.PhaseTicks = 0;
        contract.ReplanBudget.Reset(TravelRoutePurpose.Delivery);
        contract.Lease.Worker.Halt();
        HarvestChestRoute route = contract.CurrentChestRoute;
        contract.Lease.Worker.faceDirection(GetFacingDirection(route.InteractionTile, route.ChestTile));
        route.Chest.GetMutex().RequestLock(
            () => this.OnChestLockAcquired(contract.Id, route),
            () => this.OnChestLockFailed(contract.Id, route));
    }

    private void OnReturnedToArrival(Character character, GameLocation location)
    {
        ActiveHarvestContract? contract = this.ActiveContract;
        if (contract is null
            || !ReferenceEquals(character, contract.Lease.Worker)
            || !ReferenceEquals(location, contract.Farm)
            || contract.Phase != HarvestContractPhase.Returning)
            return;

        contract.Lease.Worker.Halt();
        if (contract.DepositOverflowOnReturn && contract.Cargo.Count > 0)
        {
            this.BeginOverflowDeposit(contract);
            return;
        }

        contract.Phase = HarvestContractPhase.Returned;
        contract.PhaseTicks = 0;
    }

    private bool TryApplyHarvest(ActiveHarvestContract contract)
    {
        if (contract.Lease.Worker.currentLocation != contract.WorkLocation
            || contract.Lease.Worker.TilePoint != contract.CurrentTarget.InteractionTile)
            return false;

        return contract.CurrentTarget.Kind switch
        {
            HarvestTargetKind.Crop => this.TryApplyCropHarvest(contract),
            HarvestTargetKind.Tapper => this.TryApplyTapperHarvest(contract),
            HarvestTargetKind.FruitTree => this.TryApplyFruitTreeHarvest(contract),
            HarvestTargetKind.Machine => this.TryApplyMachineHarvest(contract),
            HarvestTargetKind.CrabPot => this.TryApplyCrabPotHarvest(contract),
            HarvestTargetKind.FishPond => this.TryApplyFishPondHarvest(contract),
            HarvestTargetKind.Bush => this.TryApplyBushHarvest(contract),
            _ => false
        };
    }

    private bool TryApplyCropHarvest(ActiveHarvestContract contract)
    {
        Vector2 targetTile = new(contract.CurrentTarget.TargetTile.X, contract.CurrentTarget.TargetTile.Y);
        if (!HarvestTargetPlanner.IsMatureSupportedCrop(contract.WorkLocation, targetTile)
            || !contract.WorkLocation.terrainFeatures.TryGetValue(targetTile, out TerrainFeature? feature)
            || feature is not HoeDirt dirt
            || dirt.crop is not { } crop)
            return false;

        ContractHarvestCollector collector = new(contract.WorkLocation, contract.Lease.Worker.Position);
        bool vanillaRequestsCropRemoval = crop.harvest(
            contract.CurrentTarget.TargetTile.X,
            contract.CurrentTarget.TargetTile.Y,
            dirt,
            collector);
        if (!ContractHarvestSemantics.HasCapturedOutput(
                vanillaRequestsCropRemoval,
                collector.Items.Count))
            return false;

        if (vanillaRequestsCropRemoval)
            dirt.destroyCrop(showAnimation: false);

        foreach (Item item in collector.Items)
            this.CaptureHarvestItem(contract, item, "crop");

        this.ShowHarvestedItem(contract, collector.Items[0]);
        return true;
    }

    private bool TryApplyTapperHarvest(ActiveHarvestContract contract)
    {
        Vector2 targetTile = new(contract.CurrentTarget.TargetTile.X, contract.CurrentTarget.TargetTile.Y);
        if (!HarvestTargetPlanner.IsReadySupportedTapper(contract.WorkLocation, targetTile)
            || !contract.WorkLocation.terrainFeatures.TryGetValue(targetTile, out TerrainFeature? feature)
            || feature is not Tree tree
            || !contract.WorkLocation.objects.TryGetValue(targetTile, out StardewValley.Object? tapper)
            || tapper.heldObject.Value is not { } output)
            return false;

        Item collected = output.getOne();
        collected.Stack = output.Stack;
        int previousMinutesUntilReady = tapper.MinutesUntilReady;
        try
        {
            tapper.heldObject.Value = null;
            tapper.readyForHarvest.Value = false;
            tree.UpdateTapperProduct(tapper, output, false);
        }
        catch (Exception ex)
        {
            tapper.heldObject.Value = output;
            tapper.readyForHarvest.Value = true;
            tapper.MinutesUntilReady = previousMinutesUntilReady;
            this.Monitor.Log(
                $"Could not reschedule tapper at {contract.CurrentTarget.TargetTile}; restored its exact output: {ex}",
                LogLevel.Error);
            return false;
        }

        this.CaptureHarvestItem(contract, collected, "tapper");
        this.ShowHarvestedItem(contract, collected);
        return true;
    }

    private bool TryApplyFruitTreeHarvest(ActiveHarvestContract contract)
    {
        Vector2 targetTile = new(contract.CurrentTarget.TargetTile.X, contract.CurrentTarget.TargetTile.Y);
        if (!HarvestTargetPlanner.IsReadySupportedFruitTree(contract.WorkLocation, targetTile)
            || !contract.WorkLocation.terrainFeatures.TryGetValue(targetTile, out TerrainFeature? feature)
            || feature is not FruitTree tree)
            return false;

        bool producesCoal = FruitTreeHarvestSemantics.ProducesCoal(
            tree.struckByLightningCountdown.Value > 0);
        List<Item> collected = new();
        foreach (Item? fruit in tree.fruit)
        {
            if (fruit is null)
                continue;

            if (producesCoal)
            {
                collected.Add(ItemRegistry.Create("(O)382"));
                continue;
            }

            Item exactFruit = fruit.getOne();
            exactFruit.Stack = fruit.Stack;
            collected.Add(exactFruit);
        }

        if (collected.Count == 0)
            return false;

        tree.fruit.Clear();
        foreach (Item item in collected)
            this.CaptureHarvestItem(contract, item, producesCoal ? "lightning-struck fruit tree" : "fruit tree");
        this.ShowHarvestedItem(contract, collected[0]);
        return true;
    }

    private bool TryApplyMachineHarvest(ActiveHarvestContract contract)
    {
        Vector2 targetTile = contract.CurrentTarget.TargetTile.ToVector2();
        if (!HarvestTargetPlanner.IsReadySupportedMachine(contract.WorkLocation, targetTile)
            || !contract.WorkLocation.objects.TryGetValue(
                targetTile,
                out StardewValley.Object? machine)
            || machine.heldObject.Value is not { } output
            || machine.GetMachineData() is not { } data)
            return false;

        Item collected = output.getOne();
        collected.Stack = output.Stack;
        collected.Quality = output.Quality;

        machine.heldObject.Value = null;
        machine.readyForHarvest.Value = false;
        machine.showNextIndex.Value = false;
        machine.ResetParentSheetIndex();

        MachineDataUtility.UpdateStats(
            data.StatsToIncrementWhenHarvested,
            collected,
            collected.Stack);
        ApplyMachineHarvestExperience(data.ExperienceGainOnHarvest, contract.Requester);
        this.CaptureHarvestItem(contract, collected, $"machine {machine.QualifiedItemId}");
        this.ShowHarvestedItem(contract, collected);
        return true;
    }

    private static void ApplyMachineHarvestExperience(string? experience, Farmer requester)
    {
        if (string.IsNullOrWhiteSpace(experience))
            return;

        string[] fields = experience.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index + 1 < fields.Length; index += 2)
        {
            int skill = Farmer.getSkillNumberFromName(fields[index]);
            if (skill >= 0 && int.TryParse(fields[index + 1], out int amount))
                requester.gainExperience(skill, amount);
        }
    }

    private bool TryApplyCrabPotHarvest(ActiveHarvestContract contract)
    {
        Vector2 targetTile = contract.CurrentTarget.TargetTile.ToVector2();
        if (!HarvestTargetPlanner.IsReadySupportedCrabPot(contract.WorkLocation, targetTile)
            || !contract.WorkLocation.objects.TryGetValue(targetTile, out StardewValley.Object? value)
            || value is not CrabPot pot
            || pot.heldObject.Value is not { } output)
            return false;

        Item collected = output.getOne();
        collected.Stack = output.Stack;
        collected.Quality = output.Quality;
        FieldInfo? ignoreRemovalTimer = typeof(CrabPot).GetField(
            "ignoreRemovalTimer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (ignoreRemovalTimer is null)
            return false;

        bool hasBook = contract.Requester.stats.Get("Book_Crabbing") != 0;
        double roll = Utility.CreateDaySaveRandom(
            Game1.uniqueIDForThisGame,
            Game1.stats.DaysPlayed * 77,
            targetTile.X * 777f + targetTile.Y).NextDouble();
        Item doubled = collected.getOne();
        doubled.Stack = checked(collected.Stack * 2);
        bool destinationAcceptsDouble = this.CanHarvestDestinationAccept(
            contract,
            doubled);
        collected.Stack = CrabPotHarvestSemantics.GetOutputStack(
            collected.Stack,
            hasBook,
            roll,
            destinationAcceptsDouble);

        int? caughtLength = null;
        if (DataLoader.Fish(Game1.content).TryGetValue(collected.ItemId, out string? fishData))
        {
            string[] fields = fishData.Split('/');
            int minimumLength = fields.Length <= 5 ? 1 : Convert.ToInt32(fields[5]);
            int maximumLength = fields.Length > 5 ? Convert.ToInt32(fields[6]) : 10;
            caughtLength = Game1.random.Next(minimumLength, maximumLength + 1);
        }

        ignoreRemovalTimer.SetValue(pot, 750);
        pot.heldObject.Value = null;
        pot.readyForHarvest.Value = false;
        pot.tileIndexToShow = 710;
        pot.lidFlapping = true;
        pot.lidFlapTimer = 60f;
        pot.bait.Value = null;
        pot.shake = Vector2.Zero;
        pot.shakeTimer = 0f;
        if (caughtLength.HasValue)
        {
            contract.Requester.caughtFish(
                collected.QualifiedItemId,
                caughtLength.Value,
                from_fish_pond: false,
                collected.Stack);
        }
        contract.Requester.gainExperience(1, 5);
        contract.WorkLocation.playSound("fishingRodBend", targetTile);
        this.CaptureHarvestItem(contract, collected, "crab pot");
        this.ShowHarvestedItem(contract, collected);
        return true;
    }

    private bool CanHarvestDestinationAccept(
        ActiveHarvestContract contract,
        Item item)
    {
        if (contract.DestinationMode == HarvestDestinationMode.RequesterInventory)
        {
            Farmer? requester = Game1.GetPlayer(
                contract.Requester.UniqueMultiplayerID,
                onlyOnline: true);
            bool requesterIsOnline = requester is not null;
            bool requesterIsOnMainFarm = requesterIsOnline
                && ReferenceEquals(requester!.currentLocation, contract.Farm);
            bool canAcceptCompleteStack = requesterIsOnline
                && RequesterInventoryCapacity.CanAcceptCompleteStack(requester!, item);
            return HarvestDestinationPolicy.CanRequesterInventoryAccept(
                requesterIsOnline,
                requesterIsOnMainFarm,
                canAcceptCompleteStack);
        }

        return this.ChestRouter.FindBestRoute(
            contract.Farm,
            contract.Lease.Worker,
            contract.Lease.Worker.TilePoint,
            item,
            new HashSet<Point>(),
            new HashSet<HarvestChestRouteKey>()) is not null;
    }

    private bool TryApplyFishPondHarvest(ActiveHarvestContract contract)
    {
        Point target = contract.CurrentTarget.TargetTile;
        FishPond? pond = contract.Farm.buildings.OfType<FishPond>()
            .FirstOrDefault(candidate => candidate.GetItemBucketTile().ToPoint() == target);
        if (pond is null
            || !FishPondHarvestSemantics.IsReadyTarget(
                pond.daysOfConstructionLeft.Value <= 0,
                pond.daysUntilUpgrade.Value <= 0,
                pond.output.Value is not null)
            || pond.output.Value is not { } output)
            return false;

        Item collected = output.getOne();
        collected.Stack = output.Stack;
        collected.Quality = output.Quality;
        int? price = collected is StardewValley.Object obj
            ? obj.sellToStorePrice(-1L)
            : null;
        int experience = FishPondHarvestSemantics.GetFishingExperience(price);

        pond.output.Value = null;
        contract.Requester.gainExperience(1, experience);
        contract.Farm.playSound("coin", target.ToVector2());
        this.CaptureHarvestItem(contract, collected, "fish pond");
        this.ShowHarvestedItem(contract, collected);
        return true;
    }

    private bool TryApplyBushHarvest(ActiveHarvestContract contract)
    {
        Vector2 target = contract.CurrentTarget.TargetTile.ToVector2();
        Bush? bush = contract.WorkLocation.largeTerrainFeatures.OfType<Bush>()
            .FirstOrDefault(candidate => candidate.Tile == target);
        if (bush is null
            || !BushHarvestSemantics.IsReadyTarget(
                contract.IsMainFarm,
                bush.townBush.Value,
                bush.size.Value,
                bush.readyForHarvest(),
                bush.inBloom(),
                bush.GetShakeOffItem() is not null)
            || bush.GetShakeOffItem() is not { } outputId)
            return false;

        BushHarvestPlan plan = BushHarvestSemantics.CreatePlan(
            bush.size.Value,
            outputId,
            contract.Requester.ForagingLevel,
            contract.Requester.professions.Contains(16));
        Item collected = ItemRegistry.Create(plan.QualifiedItemId);
        collected.Stack = plan.Stack;
        collected.Quality = plan.Quality;

        bush.tileSheetOffset.Value = 0;
        bush.setUpSourceRect();
        bush.shakeTimer = 500f;
        if (plan.ForagingExperience > 0)
            contract.Requester.gainExperience(2, plan.ForagingExperience);
        contract.WorkLocation.playSound("leafrustle", target);
        this.CaptureHarvestItem(
            contract,
            collected,
            bush.size.Value == Bush.greenTeaBush ? "tea bush" : "berry bush");
        this.ShowHarvestedItem(contract, collected);
        return true;
    }

    private void CaptureHarvestItem(ActiveHarvestContract contract, Item item, string source)
    {
        string transferId = Guid.NewGuid().ToString("N");
        contract.Cargo.Add(new HarvestCargoEntry(transferId, item));
        contract.HarvestedItems.Add(new HarvestItemSnapshot(
            transferId,
            item.QualifiedItemId,
            item.DisplayName,
            item.Quality,
            item.Stack));
        this.Monitor.Log(
            $"Captured harvest '{item.QualifiedItemId}' q{item.Quality} x{item.Stack} "
            + $"from {source} {contract.CurrentTarget.TargetTile}; transfer={transferId}.",
            LogLevel.Debug);
    }

    private void BeginDeliveryOrReturn(ActiveHarvestContract contract)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        if (!contract.IsMainFarm
            && !ReferenceEquals(contract.Lease.Worker.currentLocation, contract.Farm))
        {
            Game1.warpCharacter(
                contract.Lease.Worker,
                contract.Farm,
                contract.ReturnTile.ToVector2());
            contract.Lease.Worker.Position = FarmNavigationMap.GetAlignedCharacterPosition(
                contract.ReturnTile);
            contract.Lease.Worker.Halt();
            this.Monitor.Log(
                $"Harvest worker '{contract.Lease.Worker.Name}' returned from "
                + $"{contract.WorkLocation.NameOrUniqueName} to the main farm for lossless delivery.",
                LogLevel.Debug);
        }

        if (contract.Cargo.Count == 0)
        {
            if (contract.WorkLocationComplete)
                this.BeginReturn(contract, depositOverflowOnReturn: false);
            else
                this.BeginNextOrReturn(contract);
            return;
        }

        HarvestCargoEntry entry = contract.Cargo[0];
        if (contract.DestinationMode == HarvestDestinationMode.RequesterInventory)
        {
            this.DeliverToRequesterOrStop(contract, entry);
            return;
        }

        HashSet<Point> attempted = contract.GetAttemptedChests(entry.TransferId);
        HashSet<HarvestChestRouteKey> attemptedRoutes =
            contract.GetAttemptedChestRoutes(entry.TransferId);
        HarvestChestRoute? route = this.ChestRouter.FindBestRoute(
            contract.Farm,
            contract.Lease.Worker,
            contract.Lease.Worker.TilePoint,
            entry.Item,
            attempted,
            attemptedRoutes,
            contract.FailedDeliveryTiles);
        if (route is null)
        {
            this.StopForUnavailableStorage(
                contract,
                $"no reachable category-compatible chest can fully accept "
                + $"'{entry.Item.QualifiedItemId}' q{entry.Item.Quality} x{entry.Item.Stack}");
            return;
        }

        try
        {
            this.Monitor.Log(
                $"Routing harvest cargo '{entry.Item.QualifiedItemId}' q{entry.Item.Quality} x{entry.Item.Stack} "
                + $"to chest {route.ChestTile} (match={route.MatchKind}, capacity={route.AcceptableCapacity}).",
                LogLevel.Debug);
            contract.CurrentChestRoute = route;
            if (!FarmNavigationMap.CanBeginPath(
                    contract.Farm,
                    contract.Lease.Worker,
                    contract.Lease.Worker.TilePoint,
                    route.Path,
                    out string firstStepFailure))
            {
                this.HandleFailedDeliveryRoute(
                    contract,
                    $"first-step collision probe rejected the route: {firstStepFailure}",
                    "harvest.route-reason.first-step");
                return;
            }

            contract.Phase = HarvestContractPhase.TravelingToChest;
            contract.PhaseTicks = 0;
            if (contract.Lease.Worker.TilePoint == route.InteractionTile)
            {
                this.OnArrivedAtChest(contract.Lease.Worker, contract.Farm);
                return;
            }

            PathFindController controller = this.CreatePathController(
                contract,
                route.Path,
                route.InteractionTile,
                GetFacingDirection(route.InteractionTile, route.ChestTile),
                this.OnArrivedAtChest);
            contract.Controller = controller;
            contract.Lease.AttachController(controller);
            contract.TravelWatchdog.Reset(
                contract.Lease.Worker.Position.X,
                contract.Lease.Worker.Position.Y);
        }
        catch (Exception ex)
        {
            this.HandleFailedDeliveryRoute(
                contract,
                $"controller setup failed: {ex.Message}",
                "harvest.route-reason.controller-setup");
        }
    }

    private void DeliverToRequesterOrStop(
        ActiveHarvestContract contract,
        HarvestCargoEntry entry)
    {
        Farmer? requester = Game1.GetPlayer(
            contract.Requester.UniqueMultiplayerID,
            onlyOnline: true);
        bool requesterIsOnline = requester is not null;
        bool requesterIsOnMainFarm = requesterIsOnline
            && ReferenceEquals(requester!.currentLocation, contract.Farm);
        bool canAcceptCompleteStack = requesterIsOnline
            && RequesterInventoryCapacity.CanAcceptCompleteStack(requester!, entry.Item);
        HarvestDestinationAction action = HarvestDestinationPolicy.SelectAction(
            contract.DestinationMode,
            requesterIsOnline,
            requesterIsOnMainFarm,
            canAcceptCompleteStack);
        if (action != HarvestDestinationAction.DeliverToRequester)
        {
            this.StopForUnavailableStorage(
                contract,
                "the contract-selected requester inventory is offline or cannot accept the complete stack",
                "harvest.failure.requester-destination-unavailable");
            return;
        }

        int requested = entry.Item.Stack;
        int inventoryBefore = CountStackCompatibleItems(requester!, entry.Item);
        Item? remainder = entry.Item;
        try
        {
            bool applied = contract.TransferLedger.TryApply(
                entry.TransferId,
                () => remainder = requester!.addItemToInventory(entry.Item));
            if (!applied)
            {
                contract.Cargo.RemoveAt(0);
                this.BeginNextOrReturn(contract);
                return;
            }

            int remaining = remainder?.Stack ?? 0;
            int delivered = HarvestTransferMath.GetDeliveredCount(requested, remaining);
            contract.PlayerInventoryItems += delivered;
            this.Monitor.Log(
                $"Delivered harvest cargo '{entry.Item.QualifiedItemId}' q{entry.Item.Quality} x{delivered} "
                + $"to contract-selected requester {requester!.UniqueMultiplayerID}; remainder={remaining}.",
                LogLevel.Debug);
            if (remainder is null)
            {
                contract.Cargo.RemoveAt(0);
                this.BeginNextOrReturn(contract);
                return;
            }

            entry.Item = remainder;
            entry.TransferId = Guid.NewGuid().ToString("N");
            this.StopForUnavailableStorage(
                contract,
                "the contract-selected requester inventory stopped accepting the complete stack",
                "harvest.failure.requester-destination-unavailable");
        }
        catch (Exception ex)
        {
            int inventoryAfter = CountStackCompatibleItems(requester!, entry.Item);
            int delivered = HarvestDestinationPolicy.GetRetainedCount(
                inventoryBefore,
                inventoryAfter,
                requested);
            if (delivered > 0)
            {
                contract.TransferLedger.TryApply(entry.TransferId, () => { });
                contract.PlayerInventoryItems += delivered;
                if (delivered >= requested)
                {
                    contract.Cargo.RemoveAt(0);
                    this.Monitor.Log(
                        $"Requester delivery threw after retaining the complete x{delivered} stack; "
                        + $"continuing without replay: {ex}",
                        LogLevel.Error);
                    this.BeginNextOrReturn(contract);
                    return;
                }

                entry.Item.Stack = requested - delivered;
                entry.TransferId = Guid.NewGuid().ToString("N");
                this.Monitor.Log(
                    $"Requester delivery threw after retaining x{delivered}; "
                    + $"x{entry.Item.Stack} remains in contract cargo: {ex}",
                    LogLevel.Error);
            }
            else
            {
                entry.Item.Stack = requested;
                this.Monitor.Log(
                    $"Requester delivery failed before retaining cargo; the exact x{requested} stack "
                    + $"remains owned by the contract: {ex}",
                    LogLevel.Error);
            }

            this.StopForUnavailableStorage(
                contract,
                "the contract-selected requester inventory changed or rejected delivery",
                "harvest.failure.requester-destination-unavailable");
        }
    }

    private static int CountStackCompatibleItems(Farmer requester, Item sample)
    {
        return requester.Items
            .Where(item => item is not null && sample.canStackWith(item))
            .Sum(item => item?.Stack ?? 0);
    }

    private void OnChestLockAcquired(Guid contractId, HarvestChestRoute route)
    {
        ActiveHarvestContract? contract = this.ActiveContract;
        if (contract is null
            || contract.Id != contractId
            || contract.Phase != HarvestContractPhase.WaitingForChestLock
            || !ReferenceEquals(contract.CurrentChestRoute, route))
        {
            if (route.Chest.GetMutex().IsLockHeld())
                route.Chest.GetMutex().ReleaseLock();
            return;
        }

        bool storageBecameUnavailable = false;
        try
        {
            Vector2 chestTile = new(route.ChestTile.X, route.ChestTile.Y);
            bool canTransfer = contract.Farm.objects.TryGetValue(chestTile, out StardewValley.Object? current)
                && ReferenceEquals(current, route.Chest)
                && HarvestChestRouter.IsEligibleChest(route.Chest)
                && contract.Cargo.Count > 0;
            if (!canTransfer)
            {
                this.MarkCurrentChestAttempted(contract);
            }
            else
            {
                HarvestCargoEntry primary = contract.Cargo[0];
                storageBecameUnavailable = !this.TryTransferLockedChestEntry(
                    contract,
                    route,
                    primary);

                HarvestChestRoutingContext? routing = this.ChestRouter.CreateRoutingContext(
                    contract.Farm,
                    contract.Lease.Worker,
                    contract.Lease.Worker.TilePoint);
                while (!storageBecameUnavailable && routing is not null)
                {
                    HarvestCargoEntry? grouped = this.FindNextEntryForLockedChest(
                        contract,
                        route,
                        routing);
                    if (grouped is null)
                        break;

                    storageBecameUnavailable = !this.TryTransferLockedChestEntry(
                        contract,
                        route,
                        grouped);
                }
            }
        }
        catch (Exception ex)
        {
            this.MarkCurrentChestAttempted(contract);
            this.Monitor.Log(
                $"Harvest chest transfer failed at {route.ChestTile}; the exact remainder stays in contract cargo: {ex}",
                LogLevel.Error);
        }
        finally
        {
            this.ReleaseCurrentChestLock(contract);
        }

        if (storageBecameUnavailable)
            this.StopForUnavailableStorage(contract, "a locked chest stopped accepting the full stack");
        else
        {
            // NetMutex release completion is not re-entrant. Requesting the same chest again
            // from this acquisition callback can receive a false lock-failure response.
            contract.Phase = HarvestContractPhase.WaitingForChestRelease;
            contract.PhaseTicks = 0;
        }
    }

    private HarvestCargoEntry? FindNextEntryForLockedChest(
        ActiveHarvestContract contract,
        HarvestChestRoute lockedRoute,
        HarvestChestRoutingContext routing)
    {
        if (contract.Cargo.Count == 0)
            return null;

        HarvestCargoRouteRequest[] requests = contract.Cargo
            .Select(entry => new HarvestCargoRouteRequest(
                entry.TransferId,
                entry.Item,
                contract.GetAttemptedChests(entry.TransferId),
                contract.GetAttemptedChestRoutes(entry.TransferId)))
            .ToArray();
        IReadOnlyList<HarvestCargoDestination> destinations = HarvestChestRouter.FindBestRoutes(
                routing,
                requests)
            .Select(result => new HarvestCargoDestination(
                result.TransferId,
                new GridPoint(result.Route.ChestTile.X, result.Route.ChestTile.Y)))
            .ToArray();

        string? transferId = HarvestCargoBatchPolicy.SelectForChest(
                destinations,
                new GridPoint(lockedRoute.ChestTile.X, lockedRoute.ChestTile.Y))
            .FirstOrDefault();
        return transferId is null
            ? null
            : contract.Cargo.FirstOrDefault(entry => entry.TransferId == transferId);
    }

    private bool TryTransferLockedChestEntry(
        ActiveHarvestContract contract,
        HarvestChestRoute route,
        HarvestCargoEntry entry)
    {
        HarvestChestContents contents = HarvestChestRouter.GetContents(route.Chest, entry.Item);
        bool stillMatchesCategory = HarvestChestClassification.Classify(contents).HasValue;
        bool canFullyAccept = HarvestChestRouter.GetAcceptableCapacity(route.Chest, entry.Item)
            >= entry.Item.Stack;
        if (!stillMatchesCategory || !canFullyAccept)
        {
            contract.GetAttemptedChests(entry.TransferId).Add(route.ChestTile);
            return true;
        }

        int requested = entry.Item.Stack;
        Item? remainder = entry.Item;
        bool applied;
        try
        {
            applied = contract.TransferLedger.TryApply(
                entry.TransferId,
                () => remainder = route.Chest.addItem(entry.Item));
        }
        catch (Exception ex)
        {
            contract.GetAttemptedChests(entry.TransferId).Add(route.ChestTile);
            this.Monitor.Log(
                $"Grouped harvest transfer {entry.TransferId} failed at chest {route.ChestTile}; "
                + $"the exact remainder stays in contract cargo: {ex}",
                LogLevel.Error);
            return true;
        }
        if (!applied)
        {
            contract.Cargo.Remove(entry);
            return true;
        }

        int remaining = remainder?.Stack ?? 0;
        int delivered = HarvestTransferMath.GetDeliveredCount(requested, remaining);
        contract.ChestDeliveredItems += delivered;
        this.Monitor.Log(
            $"Placed harvest cargo '{entry.Item.QualifiedItemId}' q{entry.Item.Quality} x{delivered} "
            + $"in grouped chest batch at {route.ChestTile}; remainder={remaining}.",
            LogLevel.Debug);
        if (remainder is null)
        {
            contract.Cargo.Remove(entry);
            return true;
        }

        entry.Item = remainder;
        entry.TransferId = Guid.NewGuid().ToString("N");
        contract.GetAttemptedChests(entry.TransferId).Add(route.ChestTile);
        return false;
    }

    private void OnChestLockFailed(Guid contractId, HarvestChestRoute route)
    {
        ActiveHarvestContract? contract = this.ActiveContract;
        if (contract is null
            || contract.Id != contractId
            || contract.Phase != HarvestContractPhase.WaitingForChestLock
            || !ReferenceEquals(contract.CurrentChestRoute, route))
            return;

        this.MarkCurrentChestAttempted(contract);
        this.BeginDeliveryOrReturn(contract);
    }

    private void ContinueHarvestOrDeliver(ActiveHarvestContract contract)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        if (!contract.IsMainFarm)
        {
            this.BeginNextOrReturn(contract, allowHarvestWithCargo: true);
            return;
        }

        bool deliverImmediately = contract.DestinationMode ==
            HarvestDestinationMode.RequesterInventory;
        bool acquisitionClosed = Game1.timeOfDay >= StopAcquiringTime;
        if (deliverImmediately || HarvestCargoBatchPolicy.ShouldDeliver(
                this.CountCarriedSlots(contract),
                acquisitionClosed,
                noRemainingTarget: false))
        {
            this.BeginDeliveryOrReturn(contract);
            return;
        }

        this.BeginNextOrReturn(contract, allowHarvestWithCargo: true);
    }

    private void BeginNextOrReturn(
        ActiveHarvestContract contract,
        bool allowHarvestWithCargo = false)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        if (contract.WorkLocationComplete)
        {
            if (contract.Cargo.Count > 0)
                this.BeginDeliveryOrReturn(contract);
            else
                this.BeginReturn(contract, depositOverflowOnReturn: false);
            return;
        }

        if (contract.StorageUnavailable)
        {
            if (contract.Cargo.Count > 0)
                this.BeginOverflowDeposit(contract);
            else
                this.BeginReturn(contract, depositOverflowOnReturn: false);
            return;
        }

        if (contract.Cargo.Count > 0 && !allowHarvestWithCargo)
        {
            this.BeginDeliveryOrReturn(contract);
            return;
        }

        if (Game1.timeOfDay >= StopAcquiringTime)
        {
            contract.RemainingTargets = HarvestTargetPlanner.CountRemainingHarvestTargets(
                contract.WorkLocation,
                contract.CompletedTargets);
            contract.WorkLocationComplete = true;
            if (contract.Cargo.Count > 0)
                this.BeginDeliveryOrReturn(contract);
            else
                this.BeginReturn(contract, depositOverflowOnReturn: false);
            return;
        }

        HarvestTargetSearchResult next = this.TargetPlanner.TryFindNext(
            contract.WorkLocation,
            contract.Lease.Worker,
            contract.Lease.Worker.TilePoint,
            contract.Plan.ArrivalTile,
            contract.CompletedTargets,
            contract.FailedEdges,
            contract.TargetObstacles,
            target => this.IsTargetAvailable(contract.WorkLocation, contract.Lease.Worker, target));
        if (!next.IsSuccess || next.Target is null)
        {
            if (next.Failure == HarvestPlanFailure.NoReachableTarget)
            {
                contract.UnreachableTargets += next.CandidateTargetCount;
                this.Monitor.Log(
                    $"Harvest routing found {next.CandidateTargetCount} target(s) but no safe interaction path "
                    + $"from {contract.Lease.Worker.TilePoint}; completed={contract.CompletedTargets.Count}, "
                    + $"failedEdges={contract.FailedEdges.Count}, entrance={contract.Plan.ArrivalTile}. "
                    + "Remaining targets are isolated by live collision, raised-seed trellises, placed objects, or previously failed edges.",
                    LogLevel.Warn);
            }
            contract.WorkLocationComplete = true;
            if (HarvestCargoBatchPolicy.ShouldDeliver(
                    this.CountCarriedSlots(contract),
                    acquisitionClosed: false,
                    noRemainingTarget: true))
                this.BeginDeliveryOrReturn(contract);
            else
                this.BeginReturn(contract, depositOverflowOnReturn: false);
            return;
        }

        try
        {
            if (!this.TryClaimTarget(
                    contract.WorkLocation,
                    contract.Lease.Worker,
                    next.Target.TargetTile))
            {
                this.BeginNextOrReturn(contract, allowHarvestWithCargo);
                return;
            }
            contract.CurrentTarget = next.Target;
            contract.ActionApplied = false;
            contract.Phase = HarvestContractPhase.TravelingToTarget;
            contract.PhaseTicks = 0;
            if (contract.Lease.Worker.TilePoint == next.Target.InteractionTile)
            {
                this.OnArrivedAtTarget(contract.Lease.Worker, contract.WorkLocation);
                return;
            }

            if (!FarmNavigationMap.CanBeginPath(
                    contract.WorkLocation,
                    contract.Lease.Worker,
                    contract.Lease.Worker.TilePoint,
                    next.Target.Path,
                    out string firstStepFailure))
            {
                TravelInterruptionSnapshot diagnostic = TravelInterruptionRuntime.Capture(
                    contract.WorkLocation,
                    contract.Lease.Worker,
                    expectedController: null,
                    next.Target.InteractionTile,
                    TravelInterruptionKind.FirstStepRejected,
                    contract.TravelWatchdog.PreviousProgressTile,
                    next.Target.Path,
                    firstStepFailure);
                this.Monitor.Log(
                    $"Harvest target route rejected before controller setup: {diagnostic.ToTechnicalReason()}.",
                    LogLevel.Debug);
                this.RecordTargetObstacle(contract, diagnostic);
                this.HandleFailedTargetRoute(
                    contract,
                    diagnostic.ToTechnicalReason(),
                    diagnostic.ReasonTranslationKey);
                return;
            }

            PathFindController controller = this.CreatePathController(
                contract,
                next.Target.Path,
                next.Target.InteractionTile,
                next.Target.FacingDirection,
                this.OnArrivedAtTarget);
            contract.Controller = controller;
            contract.Lease.AttachController(controller);
            contract.TravelWatchdog.Reset(
                contract.Lease.Worker.Position.X,
                contract.Lease.Worker.Position.Y,
                new GridPoint(
                    contract.Lease.Worker.TilePoint.X,
                    contract.Lease.Worker.TilePoint.Y));
        }
        catch (Exception ex)
        {
            TravelInterruptionSnapshot diagnostic = TravelInterruptionRuntime.Capture(
                contract.WorkLocation,
                contract.Lease.Worker,
                expectedController: null,
                contract.CurrentTarget.InteractionTile,
                TravelInterruptionKind.ControllerSetupFailed,
                contract.TravelWatchdog.PreviousProgressTile,
                contract.CurrentTarget.Path,
                ex.Message);
            this.Monitor.Log(
                $"Harvest target controller setup failed: {diagnostic.ToTechnicalReason()}.",
                LogLevel.Debug);
            this.RecordTargetObstacle(contract, diagnostic);
            this.HandleFailedTargetRoute(
                contract,
                diagnostic.ToTechnicalReason(),
                diagnostic.ReasonTranslationKey);
        }
    }

    private int CountCarriedSlots(ActiveHarvestContract contract)
    {
        return HarvestCargoBatchPolicy.CountCarriedSlots(
            contract.Cargo,
            entry => entry.Item.Stack,
            entry => entry.Item.maximumStackSize(),
            (left, right) => left.Item.canStackWith(right.Item));
    }

    private void BeginReturn(ActiveHarvestContract contract, bool depositOverflowOnReturn)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        contract.DepositOverflowOnReturn |= depositOverflowOnReturn;
        if (!ReferenceEquals(contract.Lease.Worker.currentLocation, contract.Farm))
        {
            Game1.warpCharacter(
                contract.Lease.Worker,
                contract.Farm,
                contract.ReturnTile.ToVector2());
            contract.Lease.Worker.Position = FarmNavigationMap.GetAlignedCharacterPosition(
                contract.ReturnTile);
            contract.Lease.Worker.Halt();
        }

        if (contract.Lease.Worker.TilePoint == contract.ReturnTile)
        {
            if (contract.DepositOverflowOnReturn && contract.Cargo.Count > 0)
            {
                this.BeginOverflowDeposit(contract);
                return;
            }
            contract.CurrentChestRoute = null;
            contract.Phase = HarvestContractPhase.Returned;
            contract.PhaseTicks = 0;
            return;
        }

        try
        {
            if (!FarmNavigationMap.TryBuild(
                    contract.Farm,
                    contract.Lease.Worker,
                    contract.Lease.Worker.TilePoint,
                    this.Monitor,
                    out GridRouteMap? routes)
                || routes is null
                || !routes.TryGetPath(
                    new GridPoint(contract.ReturnTile.X, contract.ReturnTile.Y),
                    out IReadOnlyList<GridPoint> gridPath))
                throw new InvalidOperationException("No object-safe harvest return path to the farm entrance.");

            contract.CurrentChestRoute = null;
            contract.Phase = HarvestContractPhase.Returning;
            contract.PhaseTicks = 0;
            PathFindController returning = this.CreatePathController(
                contract,
                FarmNavigationMap.ToPath(gridPath),
                contract.ReturnTile,
                finalFacingDirection: Game1.left,
                this.OnReturnedToArrival);
            contract.Controller = returning;
            contract.Lease.AttachController(returning);
            contract.TravelWatchdog.Reset(
                contract.Lease.Worker.Position.X,
                contract.Lease.Worker.Position.Y);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Worker '{contract.Lease.Worker.Name}' could not start the harvest return path: {ex.Message}", LogLevel.Warn);
            this.FinishContract(contract, succeeded: false, "contract.failure.return-path");
        }
    }

    private PathFindController CreatePathController(
        ActiveHarvestContract contract,
        Stack<Point> path,
        Point destination,
        int finalFacingDirection,
        PathFindController.endBehavior onArrived)
    {
        PathFindController controller = new(
            new Stack<Point>(path.Reverse()),
            this.GetTravelLocation(contract),
            contract.Lease.Worker,
            destination)
        {
            finalFacingDirection = finalFacingDirection,
            endBehaviorFunction = onArrived,
            nonDestructivePathing = true,
            NPCSchedule = true
        };

        if (controller.pathToEndPoint is not { Count: > 0 })
            throw new InvalidOperationException($"No path to {destination}.");
        if (this.WorkforceRoutes?.TryReserve(contract.Lease, controller.pathToEndPoint) == false)
            throw new InvalidOperationException("The shared workforce route could not be reserved.");

        return controller;
    }

    private GameLocation GetTravelLocation(ActiveHarvestContract contract) =>
        contract.Phase == HarvestContractPhase.TravelingToTarget
            ? contract.WorkLocation
            : contract.Farm;

    private void FinishContract(
        ActiveHarvestContract contract,
        bool succeeded,
        string? failureTranslationKey,
        bool mustFinalizeNow = false)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        if (!contract.FinalizationPrepared)
        {
            this.WorkClaims?.ReleaseWorker(contract.Lease.Worker.Name);
            this.WorkforceRoutes?.ReleaseWorker(contract.Lease.Worker.Name);
            this.ReleaseCurrentChestLock(contract);
            if (contract.Cargo.Count > 0 && contract.Phase != HarvestContractPhase.QuarantiningCargo)
                this.PersistOrDropCargo(contract);
            if (contract.Cargo.Count > 0 && !this.TryQuarantineRemainingCargo(contract))
            {
                contract.PendingSucceeded = false;
                contract.PendingFailureTranslationKey = "harvest.failure.quarantine-pending";
                contract.Phase = HarvestContractPhase.QuarantiningCargo;
                contract.PhaseTicks = 0;
                return;
            }

            int harvestedItems = contract.HarvestedItems.Sum(item => item.Stack);
            int unresolvedItems = contract.Cargo.Sum(entry => entry.Item.Stack);
            bool placementBalanced = HarvestPlacementAudit.IsBalanced(
                harvestedItems,
                contract.PlayerInventoryItems,
                contract.ChestDeliveredItems,
                contract.OverflowItems,
                contract.QuarantinedItems,
                contract.DroppedItems,
                unresolvedItems);
            this.Monitor.Log(
                $"Harvest placement audit for contract {contract.Id:N}: harvested={harvestedItems}, "
                + $"player={contract.PlayerInventoryItems}, chest={contract.ChestDeliveredItems}, "
                + $"overflow={contract.OverflowItems}, "
                + $"quarantine={contract.QuarantinedItems}, "
                + $"dropped={contract.DroppedItems}, unresolved={unresolvedItems}, balanced={placementBalanced}.",
                placementBalanced && unresolvedItems == 0 ? LogLevel.Debug : LogLevel.Error);
            if (!placementBalanced || unresolvedItems > 0)
            {
                succeeded = false;
                failureTranslationKey = "harvest.failure.placement-audit";
            }

            contract.FinalizationPrepared = true;
            contract.PendingSucceeded = succeeded;
            contract.PendingFailureTranslationKey = failureTranslationKey;
            contract.Phase = HarvestContractPhase.RecoveringLease;
            contract.PhaseTicks = 0;
        }

        if (contract.ManagedByShift)
        {
            this.CompleteManagedStage(contract);
            return;
        }

        this.ContinueFinalization(contract, mustFinalizeNow);
    }

    private bool IsTargetAvailable(GameLocation farm, NPC worker, Point target) =>
        this.WorkClaims?.IsAvailable(
            farm.NameOrUniqueName,
            target.X,
            target.Y,
            worker.Name) != false;

    private bool TryClaimTarget(GameLocation farm, NPC worker, Point target) =>
        this.WorkClaims?.TryClaim(
            farm.NameOrUniqueName,
            target.X,
            target.Y,
            worker.Name) != false;

    private void CommitCurrentTarget(ActiveHarvestContract contract)
    {
        Point target = contract.CurrentTarget.TargetTile;
        this.WorkClaims?.TryCommit(
            contract.WorkLocation.NameOrUniqueName,
            target.X,
            target.Y,
            contract.Lease.Worker.Name);
    }

    private void ReleaseTarget(ActiveHarvestContract contract, Point target)
    {
        this.WorkClaims?.Release(
            contract.WorkLocation.NameOrUniqueName,
            target.X,
            target.Y,
            contract.Lease.Worker.Name);
    }

    private void CompleteManagedStage(ActiveHarvestContract contract)
    {
        this.ActiveContract = null;
        this.LastCompletion = new NamedContractCompletionState(
            contract.Id.ToString("N"),
            contract.RequestId,
            contract.Requester.UniqueMultiplayerID,
            contract.Lease.Worker.Name,
            NamedFarmTask.Harvesting,
            contract.PendingSucceeded,
            contract.PendingSucceeded
                ? ""
                : contract.PendingFailureTranslationKey ?? "contract.failure.unknown",
            contract.HarvestedTargets,
            contract.PlayerInventoryItems,
            contract.ChestDeliveredItems,
            contract.OverflowItems,
            contract.QuarantinedItems,
            contract.DroppedItems,
            BillableHours: 0,
            ChargedGold: 0,
            RefundedGold: 0,
            contract.HarvestedItems.Select(item => new NamedContractCargoState(
                item.TransferId,
                item.QualifiedItemId,
                item.Name,
                item.Quality,
                item.Stack)).ToArray(),
            contract.TransferLedger.GetCompletedTransferIds(),
            Array.Empty<NamedContractTransferState>(),
            Array.Empty<NamedContractTransferState>())
        {
            HarvestDestination = contract.DestinationMode
        };
    }

    private void ContinueFinalization(ActiveHarvestContract contract, bool mustFinalizeNow)
    {
        if (!ReferenceEquals(this.ActiveContract, contract) || !contract.FinalizationPrepared)
            return;

        NpcLeaseRestoreResult restoreResult = contract.Lease.Restore();
        NpcLeaseRecoveryAction recoveryAction = NpcLeaseRecoveryPolicy.Select(
            restoreResult,
            contract.RestoreWaitTicks,
            mustFinalizeNow);
        if (recoveryAction == NpcLeaseRecoveryAction.Retry)
        {
            if (!contract.RestoreWaitNoticeShown)
            {
                contract.RestoreWaitNoticeShown = true;
                this.Monitor.Log(
                    $"Harvest contract {contract.Id:N} is waiting for a conflicting controller to release "
                    + $"worker '{contract.Lease.Worker.Name}'.",
                    LogLevel.Warn);
                Game1.addHUDMessage(new HUDMessage(
                    this.Translation.Get("contract.hud.restore-waiting", new
                    {
                        worker = contract.Lease.Worker.displayName
                    }),
                    HUDMessage.error_type));
            }
            return;
        }

        if (recoveryAction == NpcLeaseRecoveryAction.Relinquish)
            restoreResult = contract.Lease.RelinquishToConflictingController();

        WateringContractSettlement settlement = WateringContractSettlement.Create(
            contract.Preview,
            contract.Dispatched,
            contract.HarvestedTargets,
            contract.Lease.StartTime,
            Game1.timeOfDay);
        contract.Requester.Money += settlement.RefundedGold;
        this.ActiveContract = null;

        bool finalSucceeded = contract.PendingSucceeded && restoreResult == NpcLeaseRestoreResult.Restored;
        string finalReasonKey = finalSucceeded
            ? ""
            : restoreResult != NpcLeaseRestoreResult.Restored
                ? GetRestoreFailureTranslationKey(restoreResult)
                : contract.PendingFailureTranslationKey ?? "contract.failure.unknown";
        this.LastCompletion = new NamedContractCompletionState(
            contract.Id.ToString("N"),
            contract.RequestId,
            contract.Requester.UniqueMultiplayerID,
            contract.Lease.Worker.Name,
            NamedFarmTask.Harvesting,
            finalSucceeded,
            finalReasonKey,
            contract.HarvestedTargets,
            contract.PlayerInventoryItems,
            contract.ChestDeliveredItems,
            contract.OverflowItems,
            contract.QuarantinedItems,
            contract.DroppedItems,
            settlement.BillableHours,
            settlement.ChargedGold,
            settlement.RefundedGold,
            contract.HarvestedItems.Select(item => new NamedContractCargoState(
                item.TransferId,
                item.QualifiedItemId,
                item.Name,
                item.Quality,
                item.Stack)).ToArray(),
            contract.TransferLedger.GetCompletedTransferIds(),
            Array.Empty<NamedContractTransferState>(),
            Array.Empty<NamedContractTransferState>())
        {
            HarvestDestination = contract.DestinationMode
        };

        if (restoreResult != NpcLeaseRestoreResult.Restored)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get(GetRestoreHudTranslationKey(restoreResult), new
                {
                    worker = contract.Lease.Worker.displayName
                }),
                HUDMessage.error_type));
            return;
        }

        string items = FormatHarvestedItems(contract.HarvestedItems);
        string destination = this.Translation.Get(
            contract.DestinationMode == HarvestDestinationMode.RequesterInventory
                ? "contract.destination.requester"
                : "contract.destination.chests");
        if (contract.PendingSucceeded)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("harvest.hud.completed", new
                {
                    worker = contract.Lease.Worker.displayName,
                    harvested = contract.HarvestedTargets,
                    skipped = contract.SkippedTargets,
                    unreachable = contract.UnreachableTargets,
                    remaining = contract.RemainingTargets,
                    destination,
                    items,
                    player = contract.PlayerInventoryItems,
                    chest = contract.ChestDeliveredItems,
                    overflow = contract.OverflowItems,
                    quarantine = contract.QuarantinedItems,
                    dropped = contract.DroppedItems,
                    hours = settlement.BillableHours,
                    paid = settlement.ChargedGold,
                    refunded = settlement.RefundedGold
                }),
                HUDMessage.newQuest_type));
            return;
        }

        string reason = contract.PendingFailureTranslationKey is null
            ? this.Translation.Get("contract.failure.unknown")
            : this.Translation.Get(contract.PendingFailureTranslationKey);
        Game1.addHUDMessage(new HUDMessage(
            this.Translation.Get("harvest.hud.stopped", new
            {
                worker = contract.Lease.Worker.displayName,
                reason,
                harvested = contract.HarvestedTargets,
                skipped = contract.SkippedTargets,
                unreachable = contract.UnreachableTargets,
                remaining = contract.RemainingTargets,
                destination,
                items,
                player = contract.PlayerInventoryItems,
                chest = contract.ChestDeliveredItems,
                overflow = contract.OverflowItems,
                quarantine = contract.QuarantinedItems,
                dropped = contract.DroppedItems,
                hours = settlement.BillableHours,
                paid = settlement.ChargedGold,
                refunded = settlement.RefundedGold
            }),
            HUDMessage.error_type));
    }

    private static string GetRestoreFailureTranslationKey(NpcLeaseRestoreResult result)
    {
        return result == NpcLeaseRestoreResult.Relinquished
            ? "contract.failure.restore-relinquished"
            : "contract.failure.restore-ownership-lost";
    }

    private static string GetRestoreHudTranslationKey(NpcLeaseRestoreResult result)
    {
        return result == NpcLeaseRestoreResult.Relinquished
            ? "contract.hud.restore-relinquished"
            : "contract.hud.restore-ownership-lost";
    }

    private void PersistOrDropCargo(ActiveHarvestContract contract)
    {
        if (contract.Cargo.Count == 0)
            return;

        NetMutex mutex = Game1.player.team.GetOrCreateGlobalInventoryMutex(OverflowInventoryId);
        try
        {
            bool forceLockFailure = this.AcceptanceFaults.IsArmed(HarvestAcceptanceFault.OverflowLock);
            if (forceLockFailure || !this.TryAcquireOverflowLockImmediately(mutex))
            {
                if (forceLockFailure)
                    this.LogInjectedFault(HarvestAcceptanceFault.OverflowLock);
                this.DropCargoVisibly(contract, "persistent overflow was locked during emergency settlement");
                return;
            }

            this.StoreCargoInOverflow(contract);
        }
        catch (Exception ex)
        {
            this.DropCargoVisibly(contract, $"persistent harvest overflow failed: {ex}");
        }
        finally
        {
            if (mutex.IsLockHeld())
                mutex.ReleaseLock();
        }
    }

    private void BeginOverflowDeposit(ActiveHarvestContract contract)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        contract.CurrentChestRoute = null;
        contract.Phase = HarvestContractPhase.WaitingForOverflowLock;
        contract.PhaseTicks = 0;
        contract.OverflowLockRequested = false;
        contract.Lease.Worker.Halt();
        this.RequestOverflowLock(contract);
    }

    private void RequestOverflowLock(ActiveHarvestContract contract)
    {
        if (!ReferenceEquals(this.ActiveContract, contract)
            || contract.Phase != HarvestContractPhase.WaitingForOverflowLock
            || contract.OverflowLockRequested)
            return;

        contract.OverflowLockRequested = true;
        if (this.AcceptanceFaults.IsArmed(HarvestAcceptanceFault.OverflowLock))
        {
            this.LogInjectedFault(HarvestAcceptanceFault.OverflowLock);
            contract.OverflowLockRequested = false;
            return;
        }

        NetMutex mutex = Game1.player.team.GetOrCreateGlobalInventoryMutex(OverflowInventoryId);
        mutex.RequestLock(
            () => this.OnOverflowLockAcquired(contract.Id, mutex),
            () => this.OnOverflowLockFailed(contract.Id));
    }

    private void OnOverflowLockAcquired(Guid contractId, NetMutex mutex)
    {
        ActiveHarvestContract? contract = this.ActiveContract;
        if (contract is null
            || contract.Id != contractId
            || contract.Phase != HarvestContractPhase.WaitingForOverflowLock)
        {
            if (mutex.IsLockHeld())
                mutex.ReleaseLock();
            return;
        }

        try
        {
            this.StoreCargoInOverflow(contract);
            this.ContinueAfterCargoStorage(contract);
        }
        catch (Exception ex)
        {
            this.DropCargoVisibly(contract, $"persistent harvest overflow failed after locking: {ex}");
            this.ContinueAfterCargoStorage(contract);
        }
        finally
        {
            if (mutex.IsLockHeld())
                mutex.ReleaseLock();
            contract.OverflowLockRequested = false;
        }
    }

    private void OnOverflowLockFailed(Guid contractId)
    {
        ActiveHarvestContract? contract = this.ActiveContract;
        if (contract is null
            || contract.Id != contractId
            || contract.Phase != HarvestContractPhase.WaitingForOverflowLock)
            return;

        contract.OverflowLockRequested = false;
    }

    private void StopForUnavailableStorage(
        ActiveHarvestContract contract,
        string detail,
        string failureTranslationKey = "harvest.failure.storage-unavailable")
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        if (!contract.StorageUnavailable)
        {
            contract.StorageUnavailable = true;
            contract.StorageFailureTranslationKey = failureTranslationKey;
            contract.RemainingTargets = HarvestTargetPlanner.CountRemainingHarvestTargets(
                contract.WorkLocation,
                contract.CompletedTargets);
            this.Monitor.Log(
                $"Stopping harvest contract {contract.Id:N}: {detail}. "
                + $"Remaining harvest targets={contract.RemainingTargets}; existing cargo will be preserved "
                + "through emergency storage before the worker returns.",
                LogLevel.Warn);
        }

        if (contract.Cargo.Count > 0)
            this.BeginOverflowDeposit(contract);
        else
            this.BeginReturn(contract, depositOverflowOnReturn: false);
    }

    private void ContinueAfterCargoStorage(ActiveHarvestContract contract)
    {
        if (contract.StorageUnavailable)
            this.BeginReturn(contract, depositOverflowOnReturn: false);
        else
            this.BeginNextOrReturn(contract);
    }

    private void StoreCargoInOverflow(ActiveHarvestContract contract)
    {
        Inventory overflow = Game1.player.team.GetOrCreateGlobalInventory(OverflowInventoryId);
        foreach (HarvestCargoEntry entry in contract.Cargo.ToArray())
        {
            int stack = entry.Item.Stack;
            bool applied = contract.TransferLedger.TryApply(
                entry.TransferId,
                () => overflow.Add(entry.Item));
            if (applied)
            {
                contract.OverflowItems += stack;
                this.Monitor.Log(
                    $"Placed harvest cargo '{entry.Item.QualifiedItemId}' q{entry.Item.Quality} x{stack} "
                    + "in persistent overflow.",
                    LogLevel.Debug);
            }
            contract.Cargo.Remove(entry);
        }
    }

    private bool TryQuarantineRemainingCargo(ActiveHarvestContract contract)
    {
        if (contract.Cargo.Count == 0)
            return true;

        NetMutex mutex = Game1.player.team.GetOrCreateGlobalInventoryMutex(QuarantineInventoryId);
        try
        {
            bool forceLockFailure = this.AcceptanceFaults.IsArmed(HarvestAcceptanceFault.QuarantineLock);
            if (forceLockFailure || !this.TryAcquireOverflowLockImmediately(mutex))
            {
                if (forceLockFailure)
                    this.LogInjectedFault(HarvestAcceptanceFault.QuarantineLock);
                this.Monitor.Log(
                    $"Emergency harvest quarantine is locked for contract {contract.Id:N}; "
                    + "persisting a serializable recovery record.",
                    LogLevel.Error);
                return this.TryPersistQuarantineRecoveryRecord(contract);
            }

            this.StoreCargoInQuarantine(contract);
            return contract.Cargo.Count == 0;
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Emergency harvest quarantine failed for contract {contract.Id:N}: {ex}",
                LogLevel.Error);
            return this.TryPersistQuarantineRecoveryRecord(contract);
        }
        finally
        {
            if (mutex.IsLockHeld())
                mutex.ReleaseLock();
        }
    }

    private void StoreCargoInQuarantine(ActiveHarvestContract contract)
    {
        if (this.AcceptanceFaults.IsArmed(HarvestAcceptanceFault.QuarantineWrite))
        {
            this.LogInjectedFault(HarvestAcceptanceFault.QuarantineWrite);
            throw new IOException("Acceptance test forced the quarantine inventory write to fail.");
        }

        Inventory quarantine = Game1.player.team.GetOrCreateGlobalInventory(QuarantineInventoryId);
        foreach (HarvestCargoEntry entry in contract.Cargo.ToArray())
        {
            Item? existing = quarantine.FirstOrDefault(item => item is not null
                && item.modData.TryGetValue(QuarantineTransferDataKey, out string? transferId)
                && string.Equals(transferId, entry.TransferId, StringComparison.Ordinal));
            if (existing is not null
                && (existing.QualifiedItemId != entry.Item.QualifiedItemId
                    || existing.Quality != entry.Item.Quality
                    || existing.Stack != entry.Item.Stack))
            {
                throw new InvalidDataException(
                    $"Quarantine transfer {entry.TransferId} already identifies different cargo.");
            }

            int stack = entry.Item.Stack;
            bool applied = contract.TransferLedger.TryApply(
                entry.TransferId,
                () =>
                {
                    if (existing is not null)
                        return;

                    entry.Item.modData[QuarantineTransferDataKey] = entry.TransferId;
                    quarantine.Add(entry.Item);
                    if (!quarantine.Any(item => ReferenceEquals(item, entry.Item)))
                        throw new InvalidDataException(
                            $"Quarantine did not retain transfer {entry.TransferId} after insertion.");
                });
            if (!applied && existing is null)
            {
                throw new InvalidDataException(
                    $"Transfer {entry.TransferId} was marked complete without a matching quarantine item.");
            }

            if ((applied || existing is not null)
                && contract.QuarantinedTransferIds.Add(entry.TransferId))
                contract.QuarantinedItems += stack;
            contract.Cargo.Remove(entry);
            this.Monitor.Log(
                $"Quarantined harvest cargo '{entry.Item.QualifiedItemId}' q{entry.Item.Quality} x{stack}; "
                + $"transfer={entry.TransferId}.",
                LogLevel.Error);
        }

        if (contract.Cargo.Count == 0)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("harvest.hud.quarantined", new
                {
                    count = contract.QuarantinedItems
                }),
                HUDMessage.error_type));
        }
    }

    private bool TryPersistQuarantineRecoveryRecord(ActiveHarvestContract contract)
    {
        try
        {
            if (this.AcceptanceFaults.IsArmed(HarvestAcceptanceFault.RecoveryRecordWrite))
            {
                this.LogInjectedFault(HarvestAcceptanceFault.RecoveryRecordWrite);
                throw new IOException("Acceptance test forced the quarantine recovery record write to fail.");
            }

            List<HarvestCargoRecoveryItemData> savedItems = new();
            long payloadContentLength = contract.Id.ToString("N").Length;
            foreach (HarvestCargoEntry entry in contract.Cargo)
            {
                HarvestCargoRecoveryItemData savedItem = new()
                {
                    TransferId = entry.TransferId,
                    QualifiedItemId = entry.Item.QualifiedItemId,
                    DisplayName = entry.Item.DisplayName,
                    RuntimeType = entry.Item.GetType().FullName ?? entry.Item.GetType().Name,
                    RuntimeAssembly = entry.Item.GetType().Assembly.GetName().Name ?? "",
                    SerializedItemXml = SerializeRecoveryItem(entry.Item),
                    Quality = entry.Item.Quality,
                    Stack = entry.Item.Stack,
                    ModData = entry.Item.modData.Pairs
                        .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                };
                if (!HarvestCargoRecoveryState.TryAccumulatePayloadContent(
                        savedItem,
                        ref payloadContentLength))
                {
                    this.Monitor.Log(
                        "Refusing to build an oversized harvest quarantine recovery payload.",
                        LogLevel.Error);
                    this.HasPendingQuarantineRecovery = true;
                    return false;
                }
                savedItems.Add(savedItem);
            }

            HarvestCargoRecoverySaveData state = HarvestCargoRecoveryState.Create(
                Game1.uniqueIDForThisGame,
                contract.Id.ToString("N"),
                savedItems);
            if (!HarvestCargoRecoveryState.IsValid(state, Game1.uniqueIDForThisGame))
                return false;

            string serialized = JsonSerializer.Serialize(state);
            if (!HarvestCargoRecoveryState.IsSerializedPayloadValid(serialized))
            {
                this.Monitor.Log(
                    $"Refusing to write a harvest quarantine recovery payload of {serialized.Length} "
                    + $"characters; maximum is {HarvestCargoRecoveryState.MaximumSerializedPayloadLength}.",
                    LogLevel.Error);
                this.HasPendingQuarantineRecovery = true;
                return false;
            }
            if (Game1.MasterPlayer.modData.TryGetValue(QuarantineRecoveryDataKey, out string? prior)
                && !string.IsNullOrWhiteSpace(prior)
                && !string.Equals(prior, serialized, StringComparison.Ordinal))
            {
                this.Monitor.Log(
                    "Refusing to overwrite a different unresolved harvest quarantine record.",
                    LogLevel.Error);
                this.HasPendingQuarantineRecovery = true;
                return false;
            }

            Game1.MasterPlayer.modData[QuarantineRecoveryDataKey] = serialized;
            if (!Game1.MasterPlayer.modData.TryGetValue(QuarantineRecoveryDataKey, out string? verified)
                || !string.Equals(verified, serialized, StringComparison.Ordinal))
                return false;

            foreach (HarvestCargoEntry entry in contract.Cargo.ToArray())
            {
                int stack = entry.Item.Stack;
                contract.TransferLedger.TryApply(entry.TransferId, () => { });
                if (contract.QuarantinedTransferIds.Add(entry.TransferId))
                    contract.QuarantinedItems += stack;
                contract.Cargo.Remove(entry);
            }

            this.HasPendingQuarantineRecovery = true;
            this.QuarantineRecoveryRetryTicks = 0;
            this.Monitor.Log(
                $"Persisted {state.Items.Length} unresolved harvest stack(s) from contract {contract.Id:N} "
                + "into the team quarantine recovery record.",
                LogLevel.Error);
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("harvest.hud.quarantine-record", new
                {
                    count = HarvestCargoRecoveryState.CountItems(state)
                }),
                HUDMessage.error_type));
            return true;
        }
        catch (Exception ex)
        {
            this.HasPendingQuarantineRecovery = true;
            this.Monitor.Log(
                $"CRITICAL: unresolved harvest cargo could not be written to the quarantine recovery record: {ex}",
                LogLevel.Error);
            return false;
        }
    }

    private bool TryRestoreQuarantineRecovery(bool showHud)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return false;
        if (!Game1.MasterPlayer.modData.TryGetValue(QuarantineRecoveryDataKey, out string? serialized)
            || string.IsNullOrWhiteSpace(serialized))
        {
            this.HasPendingQuarantineRecovery = false;
            this.QuarantineRecoveryRetryTicks = 0;
            return true;
        }

        this.HasPendingQuarantineRecovery = true;
        NetMutex mutex = Game1.player.team.GetOrCreateGlobalInventoryMutex(QuarantineInventoryId);
        try
        {
            if (!HarvestCargoRecoveryState.IsSerializedPayloadValid(serialized))
            {
                this.Monitor.Log(
                    "The persisted harvest quarantine record exceeds its safe payload limit.",
                    LogLevel.Error);
                if (showHud)
                {
                    Game1.addHUDMessage(new HUDMessage(
                        this.Translation.Get("harvest.hud.quarantine-pending"),
                        HUDMessage.error_type));
                }
                return false;
            }

            HarvestCargoRecoverySaveData? state =
                JsonSerializer.Deserialize<HarvestCargoRecoverySaveData>(serialized);
            if (!HarvestCargoRecoveryState.IsValid(state, Game1.uniqueIDForThisGame)
                || state is null)
            {
                this.Monitor.Log(
                    "The persisted harvest quarantine record failed schema, save, or cargo validation.",
                    LogLevel.Error);
                if (showHud)
                {
                    Game1.addHUDMessage(new HUDMessage(
                        this.Translation.Get("harvest.hud.quarantine-pending"),
                        HUDMessage.error_type));
                }
                return false;
            }
            bool forceLockFailure = this.AcceptanceFaults.IsArmed(HarvestAcceptanceFault.QuarantineLock);
            if (forceLockFailure || !this.TryAcquireOverflowLockImmediately(mutex))
            {
                if (forceLockFailure)
                    this.LogInjectedFault(HarvestAcceptanceFault.QuarantineLock);
                this.Monitor.Log(
                    "Harvest quarantine recovery is waiting for its persistent inventory lock.",
                    LogLevel.Warn);
                if (showHud)
                {
                    Game1.addHUDMessage(new HUDMessage(
                        this.Translation.Get("quarantine.locked"),
                        HUDMessage.error_type));
                }
                return false;
            }

            Inventory quarantine = Game1.player.team.GetOrCreateGlobalInventory(QuarantineInventoryId);
            foreach (HarvestCargoRecoveryItemData saved in state.Items)
            {
                Item? existing = quarantine.FirstOrDefault(item => item is not null
                    && item.modData.TryGetValue(QuarantineTransferDataKey, out string? transferId)
                    && string.Equals(transferId, saved.TransferId, StringComparison.Ordinal));
                if (existing is not null)
                {
                    if (existing.QualifiedItemId != saved.QualifiedItemId
                        || existing.Quality != saved.Quality
                        || existing.Stack != saved.Stack)
                        throw new InvalidDataException(
                            $"Recovered quarantine transfer {saved.TransferId} identifies different cargo.");
                    continue;
                }

                Item restored = DeserializeRecoveryItem(saved);
                string restoredType = restored.GetType().FullName ?? restored.GetType().Name;
                if (!string.Equals(restoredType, saved.RuntimeType, StringComparison.Ordinal)
                    || !string.Equals(
                        restored.GetType().Assembly.GetName().Name,
                        saved.RuntimeAssembly,
                        StringComparison.Ordinal)
                    || restored.QualifiedItemId != saved.QualifiedItemId
                    || restored.Stack != saved.Stack
                    || restored.Quality != saved.Quality)
                {
                    throw new InvalidDataException(
                        $"Could not reconstruct exact quarantine transfer {saved.TransferId} safely.");
                }

                restored.modData.Clear();
                foreach (KeyValuePair<string, string> pair in saved.ModData)
                    restored.modData[pair.Key] = pair.Value;
                restored.modData[QuarantineTransferDataKey] = saved.TransferId;
                quarantine.Add(restored);
                if (!quarantine.Any(item => ReferenceEquals(item, restored)))
                    throw new InvalidDataException(
                        $"Quarantine did not retain recovered transfer {saved.TransferId}.");
            }

            Game1.MasterPlayer.modData.Remove(QuarantineRecoveryDataKey);
            this.HasPendingQuarantineRecovery = false;
            this.QuarantineRecoveryRetryTicks = 0;
            this.Monitor.Log(
                $"Restored {state.Items.Length} quarantined harvest stack(s) from the persisted recovery record.",
                LogLevel.Warn);
            if (showHud)
            {
                Game1.addHUDMessage(new HUDMessage(
                    this.Translation.Get("harvest.hud.quarantine-restored", new
                    {
                        count = HarvestCargoRecoveryState.CountItems(state)
                    }),
                    HUDMessage.newQuest_type));
            }
            return true;
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Quarantine recovery remains fail-closed because its exact cargo could not be restored: {ex}",
                LogLevel.Error);
            if (showHud)
            {
                Game1.addHUDMessage(new HUDMessage(
                    this.Translation.Get("harvest.hud.quarantine-pending"),
                    HUDMessage.error_type));
            }
            return false;
        }
        finally
        {
            if (mutex.IsLockHeld())
                mutex.ReleaseLock();
        }
    }

    private bool HasStoredQuarantineRecovery()
    {
        return Context.IsWorldReady
            && Game1.MasterPlayer.modData.TryGetValue(QuarantineRecoveryDataKey, out string? serialized)
            && !string.IsNullOrWhiteSpace(serialized);
    }

    private bool TryForceQuarantineAtSaveBoundary(ActiveHarvestContract contract)
    {
        try
        {
            // Only this mod exposes this private inventory, and its retrieval command is
            // host-only and blocked while a named contract is active. At the synchronous
            // save boundary, retaining the exact Item instances in the team inventory is
            // safer than allowing a cooperative mutex failure to strand transient cargo.
            this.StoreCargoInQuarantine(contract);
            return contract.Cargo.Count == 0;
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Forced save-boundary quarantine could not retain exact cargo: {ex}",
                LogLevel.Error);
            return false;
        }
    }

    private static string SerializeRecoveryItem(Item item)
    {
        XmlSerializer serializer = new(item.GetType());
        XmlSerializerNamespaces namespaces = new();
        namespaces.Add("", "");
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        serializer.Serialize(writer, item, namespaces);
        return writer.ToString();
    }

    private static Item DeserializeRecoveryItem(HarvestCargoRecoveryItemData saved)
    {
        System.Reflection.Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => string.Equals(
                candidate.GetName().Name,
                saved.RuntimeAssembly,
                StringComparison.Ordinal));
        Type? itemType = assembly?.GetType(saved.RuntimeType, throwOnError: false, ignoreCase: false);
        if (itemType is null || !typeof(Item).IsAssignableFrom(itemType))
            throw new InvalidDataException(
                $"Quarantine item type '{saved.RuntimeType}' from '{saved.RuntimeAssembly}' is unavailable.");

        XmlSerializer serializer = new(itemType);
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using StringReader stringReader = new(saved.SerializedItemXml);
        using XmlReader xmlReader = XmlReader.Create(stringReader, settings);
        return serializer.Deserialize(xmlReader) as Item
            ?? throw new InvalidDataException(
                $"Quarantine transfer {saved.TransferId} did not deserialize as an item.");
    }

    private bool TryAcquireOverflowLockImmediately(NetMutex mutex)
    {
        if (mutex.IsLockHeld())
            return true;
        if (mutex.IsLocked())
            return false;

        bool acquired = false;
        mutex.RequestLock(() => acquired = true, () => acquired = false);
        if (!acquired)
            mutex.Update(Game1.getOnlineFarmers());
        return acquired || mutex.IsLockHeld();
    }

    private void DropCargoVisibly(ActiveHarvestContract contract, string reason)
    {
        EmergencyDropDestination destination = this.ResolveEmergencyDropDestination(contract);
        this.Monitor.Log(
            $"{reason}; dropping exact harvest cargo visibly at {destination.Label} {destination.Tile}.",
            LogLevel.Error);
        foreach (HarvestCargoEntry entry in contract.Cargo.ToArray())
        {
            int stack = entry.Item.Stack;
            try
            {
                if (this.AcceptanceFaults.IsArmed(HarvestAcceptanceFault.VisibleDrop))
                {
                    this.LogInjectedFault(HarvestAcceptanceFault.VisibleDrop);
                    throw new IOException("Acceptance test forced the visible ground drop to fail.");
                }

                Game1.createItemDebris(entry.Item, destination.Position, -1, contract.Farm);
                contract.DroppedItems += stack;
                contract.Cargo.Remove(entry);
                this.Monitor.Log(
                    $"Dropped harvest cargo '{entry.Item.QualifiedItemId}' q{entry.Item.Quality} x{stack} "
                    + $"visibly at {destination.Label} {destination.Tile}.",
                    LogLevel.Warn);
            }
            catch (Exception dropException)
            {
                this.Monitor.Log(
                    $"CRITICAL: exact harvest cargo '{entry.Item.QualifiedItemId}' x{entry.Item.Stack} could not be persisted or dropped: {dropException}",
                    LogLevel.Error);
            }
        }

        Game1.addHUDMessage(new HUDMessage(
            this.Translation.Get("harvest.hud.emergency-drop", new { location = destination.Label }),
            HUDMessage.error_type));
    }

    private void LogInjectedFault(HarvestAcceptanceFault fault)
    {
        this.Monitor.Log(
            $"ACCEPTANCE TEST ONLY: forcing harvest storage fault {fault}.",
            LogLevel.Alert);
    }

    private EmergencyDropDestination ResolveEmergencyDropDestination(ActiveHarvestContract contract)
    {
        Farmer? requester = Game1.GetPlayer(contract.Requester.UniqueMultiplayerID, onlyOnline: true);
        if (requester is not null && ReferenceEquals(requester.currentLocation, contract.Farm))
        {
            Point requesterTile = new(
                (int)Math.Floor(requester.Position.X / Game1.tileSize),
                (int)Math.Floor(requester.Position.Y / Game1.tileSize));
            return new EmergencyDropDestination(
                requester.Position,
                requesterTile,
                requester.displayName);
        }

        int width = contract.Farm.Map.Layers[0].LayerWidth;
        int height = contract.Farm.Map.Layers[0].LayerHeight;
        Point farmhouseEntry = contract.Farm.GetMainFarmHouseEntry();
        GridPoint? farmhouseTile = HarvestEmergencyDropSelection.FindNearest(
            width,
            height,
            new GridPoint(farmhouseEntry.X, farmhouseEntry.Y),
            tile => this.IsSafeEmergencyDropTile(contract, tile));
        if (farmhouseTile is { } safeFarmhouseTile)
        {
            return this.CreateTileDropDestination(
                safeFarmhouseTile,
                this.Translation.Get("harvest.drop-location.farmhouse"));
        }

        GridPoint? entranceTile = HarvestEmergencyDropSelection.FindNearest(
            width,
            height,
            new GridPoint(contract.ReturnTile.X, contract.ReturnTile.Y),
            tile => this.IsSafeEmergencyDropTile(contract, tile));
        if (entranceTile is { } safeEntranceTile)
        {
            return this.CreateTileDropDestination(
                safeEntranceTile,
                this.Translation.Get("harvest.drop-location.entrance"));
        }

        return new EmergencyDropDestination(
            contract.Lease.Worker.Position,
            contract.Lease.Worker.TilePoint,
            this.Translation.Get("harvest.drop-location.worker"));
    }

    private bool IsSafeEmergencyDropTile(ActiveHarvestContract contract, GridPoint tile)
    {
        Point point = new(tile.X, tile.Y);
        Vector2 tileVector = new(tile.X, tile.Y);
        if (contract.Farm.warps.Any(warp => warp.X == tile.X && warp.Y == tile.Y)
            || contract.Farm.doors.ContainsKey(point)
            || contract.Farm.objects.ContainsKey(tileVector)
            || contract.Farm.terrainFeatures.ContainsKey(tileVector)
            || !contract.Farm.isTilePassable(tileVector))
            return false;

        Rectangle bounds = new(
            tile.X * Game1.tileSize + 1,
            tile.Y * Game1.tileSize + 1,
            Game1.tileSize - 2,
            Game1.tileSize - 2);
        return !contract.Farm.isCollidingPosition(
            bounds,
            Game1.viewport,
            isFarmer: true,
            damagesFarmer: 0,
            glider: false,
            Game1.MasterPlayer,
            pathfinding: true);
    }

    private EmergencyDropDestination CreateTileDropDestination(GridPoint tile, string label)
    {
        Vector2 position = new(
            (tile.X + 0.5f) * Game1.tileSize,
            (tile.Y + 0.5f) * Game1.tileSize);
        return new EmergencyDropDestination(
            position,
            new Point(tile.X, tile.Y),
            label);
    }

    private void StartHarvestAnimation(NPC worker)
    {
        if (worker.Sprite is null)
            return;

        int frame = worker.Sprite.currentFrame;
        int totalFrames = Math.Max(1,
            worker.Sprite.Texture.Width / worker.Sprite.SpriteWidth
            * worker.Sprite.Texture.Height / worker.Sprite.SpriteHeight);
        int actionFrame = Math.Min(totalFrames - 1, frame + 1);
        worker.Sprite.setCurrentAnimation(new List<FarmerSprite.AnimationFrame>
        {
            new(frame, 180),
            new(actionFrame, 180),
            new(frame, 180)
        });
    }

    private void ShowHarvestedItem(ActiveHarvestContract contract, Item item)
    {
        ParsedItemData data = ItemRegistry.GetDataOrErrorItem(item.QualifiedItemId);
        contract.WorkLocation.temporarySprites.Add(new TemporaryAnimatedSprite(
            data.TextureName,
            data.GetSourceRect(),
            700f,
            1,
            0,
            contract.Lease.Worker.Position + new Vector2(0f, -48f),
            flicker: false,
            flipped: false,
            (contract.Lease.Worker.GetBoundingBox().Bottom + 2f) / 10000f,
            0.01f,
            Color.White,
            4f,
            0f,
            0f,
            0f));
    }

    private void MarkCurrentChestAttempted(ActiveHarvestContract contract)
    {
        if (contract.CurrentChestRoute is null || contract.Cargo.Count == 0)
            return;

        contract.GetAttemptedChests(contract.Cargo[0].TransferId).Add(contract.CurrentChestRoute.ChestTile);
    }

    private void MarkCurrentChestRouteAttempted(ActiveHarvestContract contract)
    {
        if (contract.CurrentChestRoute is null || contract.Cargo.Count == 0)
            return;

        HarvestChestRoute route = contract.CurrentChestRoute;
        contract.GetAttemptedChestRoutes(contract.Cargo[0].TransferId).Add(
            new HarvestChestRouteKey(
                new GridPoint(route.ChestTile.X, route.ChestTile.Y),
                new GridPoint(route.InteractionTile.X, route.InteractionTile.Y)));
    }

    private void ReleaseCurrentChestLock(ActiveHarvestContract contract)
    {
        NetMutex? mutex = contract.CurrentChestRoute?.Chest.GetMutex();
        if (mutex?.IsLockHeld() == true)
            mutex.ReleaseLock();
        contract.CurrentChestRoute = null;
    }

    private bool FailStart(string translationKey)
    {
        this.LastStartFailureKey = translationKey;
        Game1.addHUDMessage(new HUDMessage(this.Translation.Get(translationKey), HUDMessage.error_type));
        return false;
    }

    private bool FailManagedStart(string translationKey)
    {
        this.LastStartFailureKey = translationKey;
        return false;
    }

    private string GetPlanFailureTranslationKey(HarvestPlanFailure failure)
    {
        return failure switch
        {
            HarvestPlanFailure.UnsupportedFarmMap => "contract.start.unsupported-map",
            HarvestPlanFailure.NoSafeArrivalTile => "contract.start.no-arrival",
            HarvestPlanFailure.NoHarvestTarget => "harvest.start.no-mature-crop",
            _ => "harvest.start.no-reachable-crop"
        };
    }

    private string GetArrivalDescription(FarmBoundarySide side)
    {
        return this.Translation.Get($"contract.entrance.{side.ToString().ToLowerInvariant()}");
    }

    private static int GetFacingDirection(Point interaction, Point target)
    {
        if (target.X > interaction.X)
            return Game1.right;
        if (target.X < interaction.X)
            return Game1.left;
        if (target.Y > interaction.Y)
            return Game1.down;
        return Game1.up;
    }

    private static string FormatHarvestedItems(IEnumerable<HarvestItemSnapshot> items)
    {
        string summary = string.Join(", ", items
            .GroupBy(item => new { item.Name, item.Quality })
            .Select(group => $"{group.Key.Name} q{group.Key.Quality} x{group.Sum(item => item.Stack)}"));
        return string.IsNullOrWhiteSpace(summary) ? "-" : summary;
    }

    private enum HarvestContractPhase
    {
        TravelingToTarget,
        Acting,
        TravelingToChest,
        WaitingForChestLock,
        WaitingForChestRelease,
        WaitingForOverflowLock,
        QuarantiningCargo,
        Returning,
        Returned,
        RecoveringLease
    }

    private sealed class ActiveHarvestContract
    {
        private readonly Dictionary<string, HashSet<Point>> AttemptedChestTiles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<HarvestChestRouteKey>> AttemptedChestRoutes =
            new(StringComparer.Ordinal);

        public ActiveHarvestContract(
            Guid id,
            string requestId,
            Farmer requester,
            NpcWorkLease lease,
            WorkContractPreview preview,
            Farm farm,
            GameLocation workLocation,
            HarvestWorkPlan plan,
            HarvestDestinationMode destinationMode,
            bool managedByShift = false,
            Point? returnTile = null)
        {
            this.Id = id;
            this.RequestId = requestId;
            this.Requester = requester;
            this.Lease = lease;
            this.Preview = preview;
            this.ActionDurationTicks = WorkerEfficiencyTiming.GetActionDurationTicks(
                HarvestingContractExecutionController.ActionDurationTicks,
                HarvestingContractExecutionController.ActionStartTicks,
                preview.EfficiencyMultiplier);
            this.Farm = farm;
            this.WorkLocation = workLocation;
            this.Plan = plan;
            this.ReturnTile = returnTile ?? plan.ArrivalTile;
            this.DestinationMode = destinationMode;
            this.CurrentTarget = plan.FirstTarget;
            this.ManagedByShift = managedByShift;
        }

        public Guid Id { get; }
        public string RequestId { get; }
        public Farmer Requester { get; }
        public NpcWorkLease Lease { get; }
        public WorkContractPreview Preview { get; }
        public int ActionDurationTicks { get; }
        public Farm Farm { get; }
        public GameLocation WorkLocation { get; }
        public bool IsMainFarm => ReferenceEquals(this.Farm, this.WorkLocation);
        public Point ReturnTile { get; }
        public HarvestDestinationMode DestinationMode { get; }
        public HarvestWorkPlan Plan { get; set; }
        public HarvestTargetPlan CurrentTarget { get; set; }
        public bool ManagedByShift { get; }
        public HashSet<Point> CompletedTargets { get; } = new();
        public HashSet<FarmTaskRouteEdge> FailedEdges { get; } = new();
        public TravelObstacleLedger TargetObstacles { get; } = new();
        public HashSet<GridPoint> FailedDeliveryTiles { get; } = new();
        public HashSet<FarmBoundarySide> FailedArrivalSides { get; } = new();
        public TravelProgressWatchdog TravelWatchdog { get; } = new();
        public TravelReplanBudget ReplanBudget { get; } = new();
        public HarvestTransferLedger TransferLedger { get; } = new();
        public HashSet<string> QuarantinedTransferIds { get; } = new(StringComparer.Ordinal);
        public List<HarvestCargoEntry> Cargo { get; } = new();
        public List<HarvestItemSnapshot> HarvestedItems { get; } = new();
        public HarvestContractPhase Phase { get; set; } = HarvestContractPhase.TravelingToTarget;
        public PathFindController? Controller { get; set; }
        public HarvestChestRoute? CurrentChestRoute { get; set; }
        public int PhaseTicks { get; set; }
        public bool Dispatched { get; set; }
        public bool ActionApplied { get; set; }
        public bool StorageUnavailable { get; set; }
        public string StorageFailureTranslationKey { get; set; } =
            "harvest.failure.storage-unavailable";
        public bool DepositOverflowOnReturn { get; set; }
        public bool OverflowLockRequested { get; set; }
        public int HarvestedTargets { get; set; }
        public int SkippedTargets { get; set; }
        public int UnreachableTargets { get; set; }
        public int RemainingTargets { get; set; }
        public int PlayerInventoryItems { get; set; }
        public int ChestDeliveredItems { get; set; }
        public int OverflowItems { get; set; }
        public int QuarantinedItems { get; set; }
        public int DroppedItems { get; set; }
        public int ReturnReplanAttempts { get; set; }
        public int EntranceSwitches { get; set; }
        public bool FinalizationPrepared { get; set; }
        public bool PendingSucceeded { get; set; }
        public string? PendingFailureTranslationKey { get; set; }
        public int RestoreWaitTicks { get; set; }
        public bool RestoreWaitNoticeShown { get; set; }
        public bool WorkLocationComplete { get; set; }

        public HashSet<Point> GetAttemptedChests(string transferId)
        {
            if (!this.AttemptedChestTiles.TryGetValue(transferId, out HashSet<Point>? attempted))
            {
                attempted = new HashSet<Point>();
                this.AttemptedChestTiles[transferId] = attempted;
            }

            return attempted;
        }

        public HashSet<HarvestChestRouteKey> GetAttemptedChestRoutes(string transferId)
        {
            if (!this.AttemptedChestRoutes.TryGetValue(
                    transferId,
                    out HashSet<HarvestChestRouteKey>? attempted))
            {
                attempted = new HashSet<HarvestChestRouteKey>();
                this.AttemptedChestRoutes[transferId] = attempted;
            }

            return attempted;
        }
    }

    private sealed class HarvestCargoEntry
    {
        public HarvestCargoEntry(string transferId, Item item)
        {
            this.TransferId = transferId;
            this.Item = item;
        }

        public string TransferId { get; set; }
        public Item Item { get; set; }
    }

    private sealed record HarvestItemSnapshot(
        string TransferId,
        string QualifiedItemId,
        string Name,
        int Quality,
        int Stack);

    private sealed record EmergencyDropDestination(
        Vector2 Position,
        Point Tile,
        string Label);
}
