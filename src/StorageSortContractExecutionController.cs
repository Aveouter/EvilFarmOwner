using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Network;
using StardewValley.Objects;
using StardewValley.Pathfinding;

namespace EvilFarmOwner;

internal sealed record StorageSortContractReport(
    string ContractId,
    string RequestId,
    long RequestingPlayerId,
    string WorkerName,
    bool Succeeded,
    string ReasonKey,
    IReadOnlyList<StorageSortCompletedTransfer> Transfers,
    int MovedItems,
    IReadOnlyList<StorageSortCompletedTransfer> SkippedTransfers,
    int PersistedRecoveryItems,
    int BillableHours,
    int ChargedGold,
    int RefundedGold);

internal sealed class StorageSortContractExecutionController
{
    private const int LatestStartTime = 1600;
    private const int HardStopTime = 2200;
    private const int SourceInspectionTicks = 12;
    private const int DestinationActionTicks = 24;
    private const int MaximumTravelTicks = 3600;
    private const int MaximumStalledTravelTicks = 180;
    private const int MaximumRouteFailures = 3;
    private const int MaximumLockWaitTicks = 300;

    private readonly ITranslationHelper Translation;
    private readonly IMonitor Monitor;
    private readonly WorkerRosterService WorkerRoster;
    private readonly StorageSortRoutePlanner RoutePlanner;
    private readonly StorageSortRecoveryManager RecoveryManager;
    private readonly RuntimeWorkforceRouteCoordinator? WorkforceRoutes;
    private ActiveStorageSortContract? ActiveContract;
    private NamedContractCompletionState? LastCompletion;

    public StorageSortContractExecutionController(
        ITranslationHelper translation,
        IMonitor monitor,
        WorkerRosterService workerRoster,
        StorageSortRecoveryManager recoveryManager,
        RuntimeWorkforceRouteCoordinator? workforceRoutes = null)
    {
        this.Translation = translation;
        this.Monitor = monitor;
        this.WorkerRoster = workerRoster;
        this.RoutePlanner = new StorageSortRoutePlanner(monitor);
        this.RecoveryManager = recoveryManager;
        this.WorkforceRoutes = workforceRoutes;
    }

    public bool HasActiveContract => this.ActiveContract is not null;

    public bool HasUnresolvedRecovery => this.RecoveryManager.HasPendingRecovery
        || this.ActiveContract?.UnresolvedItem is not null;

    public string? ActiveContractId => this.ActiveContract?.Id.ToString("N");

    public string? LastStartFailureKey { get; private set; }

    public StorageSortContractReport? LastReport { get; private set; }

    public NamedContractRuntimeState? GetRuntimeState()
    {
        ActiveStorageSortContract? contract = this.ActiveContract;
        if (contract is null)
            return null;

        StorageSortRouteStep step = contract.StepIndex < contract.RoutePlan.Steps.Count
            ? contract.RoutePlan.Steps[contract.StepIndex]
            : contract.RoutePlan.Steps[^1];
        GridPoint target = contract.Phase is StorageSortContractPhase.TravelingToSource
            or StorageSortContractPhase.InspectingSource
            ? step.Transfer.SourceChest
            : step.Transfer.DestinationChest;
        IReadOnlyList<NamedContractCargoState> cargo = contract.UnresolvedItem is null
            ? Array.Empty<NamedContractCargoState>()
            : new[]
            {
                new NamedContractCargoState(
                    contract.UnresolvedTransferId.ToString("N"),
                    contract.UnresolvedItem.QualifiedItemId,
                    contract.UnresolvedItem.DisplayName,
                    contract.UnresolvedItem.Quality,
                    contract.UnresolvedItem.Stack)
            };
        return new NamedContractRuntimeState(
            contract.Id.ToString("N"),
            contract.RequestId,
            contract.Requester.UniqueMultiplayerID,
            contract.Lease.Worker.Name,
            NamedFarmTask.StorageSorting,
            contract.Preview.EfficiencyMultiplier,
            contract.Phase.ToString(),
            contract.RoutePlan.ArrivalTile.X,
            contract.RoutePlan.ArrivalTile.Y,
            contract.RoutePlan.ArrivalSide,
            EntranceSwitches: 0,
            target.X,
            target.Y,
            contract.Preview.MaximumAuthorizedWage,
            contract.Lease.StartTime,
            contract.CompletedTransfers.Count,
            cargo,
            contract.CompletedTransfers
                .Select(transfer => contract.TransferIds[transfer.Sequence].ToString("N"))
                .ToArray());
    }

    public NamedContractCompletionState? ConsumeCompletion()
    {
        NamedContractCompletionState? completion = this.LastCompletion;
        this.LastCompletion = null;
        return completion;
    }

    public bool TryStart(
        long requestingPlayerId,
        string workerInternalName,
        string requestId)
    {
        this.LastStartFailureKey = null;
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return this.FailStart("contract.start.host-only");
        if (this.RecoveryManager.HasPendingRecovery)
            return this.FailStart("storage-sort.start.recovery-pending");
        if (!Guid.TryParseExact(requestId, "N", out _))
            return this.FailStart("multiplayer.reject.request-id");
        if (this.ActiveContract is not null)
            return this.FailStart("contract.start.already-active");
        if (Game1.timeOfDay > LatestStartTime)
            return this.FailStart("storage-sort.start.too-late");

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
        {
            return this.FailStart("contract.start.worker-missing");
        }
        if (availability.State != WorkerAvailabilityState.EligibleForPreview)
            return this.FailStart("storage-sort.start.worker-unavailable");

        WorkContractPreview preview = ContractPreviewService.Create(
            requester.getFriendshipHeartLevelForNPC(worker.Name),
            Game1.dayOfMonth,
            worker.Name,
            NamedFarmTask.StorageSorting);
        if (requester.Money < preview.MaximumAuthorizedWage)
            return this.FailStart("storage-sort.start.insufficient-funds");

        StorageSortRuntimePlanResult snapshotResult = StorageSortSnapshotService.TryCreate(farm);
        if (!snapshotResult.IsSuccess || snapshotResult.RuntimePlan is null)
            return this.FailStart(GetSnapshotFailureKey(snapshotResult.Failure));

        StorageSortRoutePlanResult routeResult = this.RoutePlanner.TryCreate(
            farm,
            worker,
            snapshotResult.RuntimePlan);
        if (!routeResult.IsSuccess || routeResult.Plan is null)
            return this.FailStart(GetRouteFailureKey(routeResult.Failure));

        if (!StorageSortExecutionSession.TryCreate(
                farm,
                snapshotResult.RuntimePlan,
                out StorageSortExecutionSession? session,
                out StorageSortSnapshotFailure sessionFailure)
            || session is null)
        {
            return this.FailStart(GetSnapshotFailureKey(sessionFailure));
        }

        if (!NpcWorkLease.TryAcquire(
                worker,
                preview.MaximumAuthorizedWage,
                this.Monitor,
                out NpcWorkLease? lease)
            || lease is null)
        {
            return this.FailStart("contract.start.lease-failed");
        }

        ActiveStorageSortContract contract = new(
            Guid.NewGuid(),
            requestId,
            requester,
            lease,
            preview,
            farm,
            snapshotResult.RuntimePlan,
            routeResult.Plan,
            session);
        this.ActiveContract = contract;
        requester.Money -= preview.MaximumAuthorizedWage;

        try
        {
            Game1.warpCharacter(worker, farm, routeResult.Plan.ArrivalTile.ToVector2());
            if (!ReferenceEquals(worker.currentLocation, farm)
                || !farm.characters.Contains(worker)
                || worker.TilePoint != routeResult.Plan.ArrivalTile)
            {
                throw new InvalidOperationException(
                    $"Worker did not arrive at storage-sort entrance {routeResult.Plan.ArrivalTile}.");
            }

            worker.Position = FarmNavigationMap.GetAlignedCharacterPosition(routeResult.Plan.ArrivalTile);
            worker.Halt();
            contract.Dispatched = true;
            this.BeginSourceTravel(contract);
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("storage-sort.hud.dispatched", new
                {
                    worker = worker.displayName,
                    transfers = routeResult.Plan.Steps.Count,
                    gold = preview.MaximumAuthorizedWage
                }),
                HUDMessage.newQuest_type));
            return true;
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Failed to dispatch storage-sort worker '{worker.Name}': {ex}",
                LogLevel.Error);
            this.FinishContract(contract, succeeded: false, "contract.failure.dispatch", mustFinalizeNow: true);
            return false;
        }
    }

    public bool TryStartManaged(FarmWorkShiftContext shift)
    {
        this.LastStartFailureKey = null;
        if (this.RecoveryManager.HasPendingRecovery)
            return this.FailStart("storage-sort.start.recovery-pending");
        if (this.ActiveContract is not null)
            return this.FailStart("contract.start.already-active");

        Farm farm = Game1.getFarm();
        NPC worker = shift.Lease.Worker;
        StorageSortRuntimePlanResult snapshotResult = StorageSortSnapshotService.TryCreate(farm);
        if (!snapshotResult.IsSuccess || snapshotResult.RuntimePlan is null)
            return this.FailStart(GetSnapshotFailureKey(snapshotResult.Failure));
        StorageSortRoutePlanResult routeResult = this.RoutePlanner.TryCreate(
            farm,
            worker,
            snapshotResult.RuntimePlan);
        if (!routeResult.IsSuccess || routeResult.Plan is null)
            return this.FailStart(GetRouteFailureKey(routeResult.Failure));
        if (!StorageSortExecutionSession.TryCreate(
                farm,
                snapshotResult.RuntimePlan,
                out StorageSortExecutionSession? session,
                out StorageSortSnapshotFailure sessionFailure)
            || session is null)
            return this.FailStart(GetSnapshotFailureKey(sessionFailure));

        WorkContractPreview preview = ContractPreviewService.Create(
            shift.Requester.getFriendshipHeartLevelForNPC(worker.Name),
            Game1.dayOfMonth,
            worker.Name,
            NamedFarmTask.StorageSorting);
        ActiveStorageSortContract contract = new(
            shift.Id,
            shift.RequestId,
            shift.Requester,
            shift.Lease,
            preview,
            farm,
            snapshotResult.RuntimePlan,
            routeResult.Plan,
            session,
            managedByShift: true);
        this.ActiveContract = contract;

        try
        {
            Game1.warpCharacter(worker, farm, routeResult.Plan.ArrivalTile.ToVector2());
            worker.Position = FarmNavigationMap.GetAlignedCharacterPosition(routeResult.Plan.ArrivalTile);
            worker.Halt();
            contract.Dispatched = true;
            this.BeginSourceTravel(contract);
            return true;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Failed to start managed storage-sort stage: {ex}", LogLevel.Error);
            this.FinishContract(contract, false, "contract.failure.dispatch", mustFinalizeNow: true);
            return false;
        }
    }

    public void Update()
    {
        ActiveStorageSortContract? contract = this.ActiveContract;
        if (contract is null || !Context.IsWorldReady)
            return;

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

        if (contract.Phase == StorageSortContractPhase.RecoveryBlocked)
        {
            this.TryResolveBlockedRecovery(contract);
            return;
        }

        if (!Context.IsMainPlayer
            || Game1.Date.TotalDays != contract.Lease.StartTotalDays
            || Game1.timeOfDay >= HardStopTime)
        {
            this.BeginReturn(contract, "contract.failure.safety-stop");
            return;
        }

        if (contract.Phase is StorageSortContractPhase.TravelingToSource
                or StorageSortContractPhase.TravelingToDestination
                or StorageSortContractPhase.Returning
            && this.WorkforceRoutes?.IsWaiting(contract.Lease.Worker.Name) == true)
            return;

        contract.PhaseTicks++;
        switch (contract.Phase)
        {
            case StorageSortContractPhase.TravelingToSource:
            case StorageSortContractPhase.TravelingToDestination:
            case StorageSortContractPhase.Returning:
                this.UpdateTravel(contract);
                break;

            case StorageSortContractPhase.InspectingSource:
                if (contract.PhaseTicks >= SourceInspectionTicks)
                    this.BeginDestinationTravel(contract);
                break;

            case StorageSortContractPhase.WaitingForFirstLock:
            case StorageSortContractPhase.WaitingForSecondLock:
                if (contract.PhaseTicks >= MaximumLockWaitTicks)
                {
                    this.ReleaseLocks(contract);
                    this.BeginReturn(contract, "contract.failure.storage-lock");
                }
                break;

            case StorageSortContractPhase.ActingAtDestination:
                if (contract.PhaseTicks >= DestinationActionTicks)
                    this.AdvanceOrReturn(contract);
                break;

            case StorageSortContractPhase.Returned:
                this.FinishContract(
                    contract,
                    succeeded: contract.FailureKey is null
                        && contract.CompletedTransfers.Count == contract.RoutePlan.Steps.Count,
                    contract.FailureKey);
                break;

            case StorageSortContractPhase.RecoveryBlocked:
                break;
        }
    }

    public void OnSaving()
    {
        if (this.ActiveContract is { } contract)
        {
            this.ReleaseLocks(contract);
            this.TryResolveBlockedRecovery(contract);
            if (StorageSortSaveBoundaryPolicy.CanForceQuarantine(
                    contract.UnresolvedItem is not null,
                    contract.UnresolvedItemDetached,
                    contract.UnresolvedTransferId)
                && contract.UnresolvedItem is { } detachedItem)
            {
                int stack = detachedItem.Stack;
                if (this.RecoveryManager.TryForceQuarantineAtSaveBoundary(
                        contract.UnresolvedTransferId,
                        detachedItem))
                {
                    contract.PersistedRecoveryItems += stack;
                    contract.UnresolvedItem = null;
                    contract.UnresolvedItemDetached = false;
                }
            }

            if (contract.UnresolvedItem is not null)
            {
                this.Monitor.Log(
                    $"CRITICAL: storage-sort contract {contract.Id:N} reached the save boundary "
                    + "without verified durable ownership; retaining the active contract and refusing "
                    + "to report finalization.",
                    LogLevel.Error);
            }
            this.FinishContract(
                contract,
                succeeded: false,
                "contract.failure.day-ending",
                mustFinalizeNow: true);
        }
    }

    public void OnReturnedToTitle()
    {
        if (this.ActiveContract is { } contract && Context.IsWorldReady)
        {
            this.ReleaseLocks(contract);
            this.TryResolveBlockedRecovery(contract);
            this.FinishContract(
                contract,
                succeeded: false,
                "contract.failure.world-closed",
                mustFinalizeNow: true);
        }

        if (this.ActiveContract?.UnresolvedItem is null)
            this.ActiveContract = null;
    }

    private void BeginSourceTravel(ActiveStorageSortContract contract)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        contract.Phase = StorageSortContractPhase.TravelingToSource;
        contract.PhaseTicks = 0;
        NPC worker = contract.Lease.Worker;
        StorageSortRouteStep step = contract.CurrentStep;
        if (!this.RoutePlanner.TryCreateChestRoute(
                contract.Farm,
                worker,
                worker.TilePoint,
                step.Transfer.SourceChest,
                contract.RouteObstacles,
                out Point interaction,
                out Stack<Point>? path)
            || path is null)
        {
            this.HandleTravelInterruption(
                contract,
                TravelInterruptionKind.ControllerSetupFailed,
                explicitCollisionProbe: "no safe route to source chest");
            return;
        }

        contract.RouteOverride = step with
        {
            SourceInteractionTile = interaction,
            SourcePath = path
        };
        this.BeginPath(
            contract,
            path,
            interaction,
            GetFacingDirection(interaction, ToPoint(step.Transfer.SourceChest)),
            this.OnArrivedAtSource);
    }

    private void BeginDestinationTravel(ActiveStorageSortContract contract)
    {
        contract.Phase = StorageSortContractPhase.TravelingToDestination;
        contract.PhaseTicks = 0;
        NPC worker = contract.Lease.Worker;
        StorageSortRouteStep step = contract.CurrentStep;
        if (!this.RoutePlanner.TryCreateChestRoute(
                contract.Farm,
                worker,
                worker.TilePoint,
                step.Transfer.DestinationChest,
                contract.RouteObstacles,
                out Point interaction,
                out Stack<Point>? path)
            || path is null)
        {
            this.HandleTravelInterruption(
                contract,
                TravelInterruptionKind.ControllerSetupFailed,
                explicitCollisionProbe: "no safe route to destination chest");
            return;
        }

        contract.RouteOverride = step with
        {
            DestinationInteractionTile = interaction,
            DestinationPath = path
        };
        this.BeginPath(
            contract,
            path,
            interaction,
            GetFacingDirection(
                interaction,
                ToPoint(step.Transfer.DestinationChest)),
            this.OnArrivedAtDestination);
    }

    private void BeginPath(
        ActiveStorageSortContract contract,
        Stack<Point> path,
        Point destination,
        int finalFacingDirection,
        PathFindController.endBehavior onArrived)
    {
        try
        {
            NPC worker = contract.Lease.Worker;
            if (worker.TilePoint == destination)
            {
                worker.Position = FarmNavigationMap.GetAlignedCharacterPosition(destination);
                onArrived(worker, contract.Farm);
                return;
            }
            if (!FarmNavigationMap.CanBeginPath(
                    contract.Farm,
                    worker,
                    worker.TilePoint,
                    path,
                    out string failure))
            {
                this.HandleTravelInterruption(
                    contract,
                    TravelInterruptionKind.FirstStepRejected,
                    path,
                    failure);
                return;
            }

            PathFindController controller = new(
                new Stack<Point>(path.Reverse()),
                contract.Farm,
                worker,
                destination)
            {
                finalFacingDirection = finalFacingDirection,
                endBehaviorFunction = onArrived,
                nonDestructivePathing = true,
                NPCSchedule = true
            };
            if (controller.pathToEndPoint is not { Count: > 0 })
            {
                this.HandleTravelInterruption(
                    contract,
                    TravelInterruptionKind.ControllerSetupFailed,
                    path,
                    $"controller produced no path to {destination}");
                return;
            }
            if (this.WorkforceRoutes?.TryReserve(contract.Lease, controller.pathToEndPoint) == false)
            {
                this.HandleTravelInterruption(
                    contract,
                    TravelInterruptionKind.ControllerSetupFailed,
                    path,
                    "the shared workforce route could not be reserved");
                return;
            }

            contract.Controller = controller;
            contract.Lease.AttachController(controller);
            contract.TravelWatchdog.Reset(
                worker.Position.X,
                worker.Position.Y,
                new GridPoint(worker.TilePoint.X, worker.TilePoint.Y));
        }
        catch (Exception ex)
        {
            this.HandleTravelInterruption(
                contract,
                TravelInterruptionKind.ControllerSetupFailed,
                path,
                ex.Message);
        }
    }

    private void UpdateTravel(ActiveStorageSortContract contract)
    {
        if (this.TryCompleteTravelAtDestination(contract))
            return;
        if (contract.PhaseTicks > MaximumTravelTicks)
        {
            this.HandleTravelInterruption(contract, TravelInterruptionKind.Timeout);
            return;
        }

        NPC worker = contract.Lease.Worker;
        if (contract.Controller is not null
            && worker.controller is not null
            && !ReferenceEquals(worker.controller, contract.Controller))
        {
            this.HandleTravelInterruption(contract, TravelInterruptionKind.ControllerReplaced);
            return;
        }
        if ((Game1.activeClickableMenu is null || Game1.IsMultiplayer)
            && contract.TravelWatchdog.Tick(
                worker.Position.X,
                worker.Position.Y,
                new GridPoint(worker.TilePoint.X, worker.TilePoint.Y),
                MaximumStalledTravelTicks))
        {
            this.HandleTravelInterruption(contract, TravelInterruptionKind.ProgressStall);
            return;
        }
        if (contract.PhaseTicks > 1 && worker.controller is null)
            this.HandleTravelInterruption(contract, TravelInterruptionKind.ControllerEnded);
    }

    private bool TryCompleteTravelAtDestination(ActiveStorageSortContract contract)
    {
        Point? destination = contract.Phase switch
        {
            StorageSortContractPhase.TravelingToSource => contract.CurrentStep.SourceInteractionTile,
            StorageSortContractPhase.TravelingToDestination =>
                contract.CurrentStep.DestinationInteractionTile,
            StorageSortContractPhase.Returning => contract.RoutePlan.ArrivalTile,
            _ => null
        };
        NPC worker = contract.Lease.Worker;
        if (destination is null
            || !ReferenceEquals(worker.currentLocation, contract.Farm)
            || worker.TilePoint != destination.Value)
        {
            return false;
        }

        if (worker.controller is not null && !ReferenceEquals(worker.controller, contract.Controller))
        {
            this.HandleTravelInterruption(contract, TravelInterruptionKind.ControllerReplaced);
            return true;
        }
        if (ReferenceEquals(worker.controller, contract.Controller))
            worker.controller = null;
        contract.Controller = null;
        contract.RouteFailures.Reset(GetRouteFailureKey(contract));
        worker.Position = FarmNavigationMap.GetAlignedCharacterPosition(destination.Value);
        worker.Halt();

        switch (contract.Phase)
        {
            case StorageSortContractPhase.TravelingToSource:
                this.OnArrivedAtSource(worker, contract.Farm);
                break;
            case StorageSortContractPhase.TravelingToDestination:
                this.OnArrivedAtDestination(worker, contract.Farm);
                break;
            case StorageSortContractPhase.Returning:
                this.OnReturned(worker, contract.Farm);
                break;
        }
        return true;
    }

    private void HandleTravelInterruption(
        ActiveStorageSortContract contract,
        TravelInterruptionKind kind,
        Stack<Point>? explicitPath = null,
        string? explicitCollisionProbe = null)
    {
        NPC worker = contract.Lease.Worker;
        Point destination = GetPhaseDestination(contract);
        TravelInterruptionSnapshot diagnostic = TravelInterruptionRuntime.Capture(
            contract.Farm,
            worker,
            contract.Controller,
            destination,
            kind,
            contract.TravelWatchdog.PreviousProgressTile,
            explicitPath,
            explicitCollisionProbe);
        string routeKey = GetRouteFailureKey(contract);
        this.Monitor.Log(
            $"Storage-sort travel interrupted: contract={contract.Id:N}, "
            + $"worker={worker.Name}, phase={contract.Phase}, routeKey={routeKey}, "
            + diagnostic.ToTechnicalReason() + ".",
            LogLevel.Debug);
        TravelObstacleSelection obstacle = TravelRouteExclusionPolicy.Select(
            diagnostic.LocationKey,
            diagnostic.Origin,
            diagnostic.PreviousProgressTile,
            diagnostic.NextWaypoint);
        contract.RouteObstacles.Add(obstacle);

        if (ReferenceEquals(worker.controller, contract.Controller))
            worker.controller = null;
        contract.Controller = null;
        if (kind == TravelInterruptionKind.ControllerReplaced)
        {
            this.Monitor.Log(
                $"Storage-sort route {routeKey} was replaced by an external controller; "
                + "the contract will stop without overwriting it.",
                LogLevel.Warn);
            if (this.MustResolveDetachedCargo(contract, "contract.failure.controller-conflict"))
                return;
            this.FinishContract(contract, false, "contract.failure.controller-conflict");
            return;
        }

        worker.Halt();
        TravelFailureDecision decision = contract.RouteFailures.Record(routeKey);
        if (decision.CanRetry)
        {
            this.Monitor.Log(
                $"Storage-sort route {routeKey} will replan around tile={obstacle.Tile}, "
                + $"edge={obstacle.Edge} [{decision.FailureCount}/{decision.MaximumFailures}].",
                LogLevel.Debug);
            switch (contract.Phase)
            {
                case StorageSortContractPhase.TravelingToSource:
                    this.BeginSourceTravel(contract);
                    return;
                case StorageSortContractPhase.TravelingToDestination:
                    this.BeginDestinationTravel(contract);
                    return;
                case StorageSortContractPhase.Returning:
                    this.BeginReturn(contract, contract.FailureKey, retry: true);
                    return;
            }
        }

        this.Monitor.Log(
            $"Storage-sort route {routeKey} exhausted {MaximumRouteFailures} attempts; "
            + diagnostic.ToTechnicalReason() + ".",
            LogLevel.Warn);
        Game1.addHUDMessage(new HUDMessage(
            this.Translation.Get("storage-sort.hud.route-stopped", new
            {
                worker = worker.displayName,
                reason = this.Translation.Get(diagnostic.ReasonTranslationKey)
            }),
            HUDMessage.error_type));

        if (contract.Phase == StorageSortContractPhase.Returning)
        {
            this.FinishContract(contract, false, "contract.failure.return-path");
            return;
        }

        if (this.MustResolveDetachedCargo(contract, "contract.failure.route-interrupted"))
            return;

        // Normally no item has left its source chest during either travel phase.
        // The locked transfer is synchronous and must rollback or enter recovery
        // before it can return control to this route state machine.
        this.BeginReturn(contract, "contract.failure.route-interrupted");
    }

    private bool MustResolveDetachedCargo(
        ActiveStorageSortContract contract,
        string failureKey)
    {
        if (StorageSortRouteFailurePolicy.Decide(
                contract.UnresolvedItem is not null,
                contract.UnresolvedItemDetached)
            != StorageSortRouteFailureDisposition.ResolveDetachedCargo)
        {
            return false;
        }

        contract.FailureKey ??= failureKey;
        contract.Phase = StorageSortContractPhase.RecoveryBlocked;
        contract.PhaseTicks = 0;
        this.TryResolveBlockedRecovery(contract);
        return true;
    }

    private static Point GetPhaseDestination(ActiveStorageSortContract contract)
    {
        return contract.Phase switch
        {
            StorageSortContractPhase.TravelingToSource => contract.CurrentStep.SourceInteractionTile,
            StorageSortContractPhase.TravelingToDestination => contract.CurrentStep.DestinationInteractionTile,
            StorageSortContractPhase.Returning => contract.RoutePlan.ArrivalTile,
            _ => contract.Lease.Worker.TilePoint
        };
    }

    private static string GetRouteFailureKey(ActiveStorageSortContract contract)
    {
        return contract.Phase switch
        {
            StorageSortContractPhase.TravelingToSource => $"source:{contract.CurrentStep.Transfer.Sequence}",
            StorageSortContractPhase.TravelingToDestination => $"destination:{contract.CurrentStep.Transfer.Sequence}",
            StorageSortContractPhase.Returning => "return",
            _ => contract.Phase.ToString()
        };
    }

    private void OnArrivedAtSource(Character character, GameLocation location)
    {
        ActiveStorageSortContract? contract = this.ActiveContract;
        if (!IsExpectedArrival(
                contract,
                character,
                location,
                StorageSortContractPhase.TravelingToSource))
        {
            return;
        }

        contract!.Phase = StorageSortContractPhase.InspectingSource;
        contract.PhaseTicks = 0;
        contract.Lease.Worker.Halt();
        contract.Lease.Worker.faceDirection(GetFacingDirection(
            contract.CurrentStep.SourceInteractionTile,
            ToPoint(contract.CurrentStep.Transfer.SourceChest)));
    }

    private void OnArrivedAtDestination(Character character, GameLocation location)
    {
        ActiveStorageSortContract? contract = this.ActiveContract;
        if (!IsExpectedArrival(
                contract,
                character,
                location,
                StorageSortContractPhase.TravelingToDestination))
        {
            return;
        }

        contract!.Lease.Worker.Halt();
        contract.Lease.Worker.faceDirection(GetFacingDirection(
            contract.CurrentStep.DestinationInteractionTile,
            ToPoint(contract.CurrentStep.Transfer.DestinationChest)));
        this.RequestFirstLock(contract);
    }

    private void RequestFirstLock(ActiveStorageSortContract contract)
    {
        StorageSortTransfer transfer = contract.CurrentStep.Transfer;
        StorageSortLockPair order = StorageSortTransferPolicy.GetLockOrder(
            transfer.SourceChest,
            transfer.DestinationChest);
        if (!contract.RuntimePlan.RuntimeChests.TryGetValue(order.First, out Chest? first)
            || !contract.RuntimePlan.RuntimeChests.TryGetValue(order.Second, out Chest? second))
        {
            this.BeginReturn(contract, "contract.failure.storage-changed");
            return;
        }

        contract.FirstLockChest = first;
        contract.SecondLockChest = second;
        contract.Phase = StorageSortContractPhase.WaitingForFirstLock;
        contract.PhaseTicks = 0;
        int sequence = transfer.Sequence;
        try
        {
            first.GetMutex().RequestLock(
                () => this.OnFirstLockAcquired(contract.Id, sequence, first, second),
                () => this.OnLockFailed(contract.Id, sequence, first, second));
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Could not request first storage-sort chest lock: {ex.Message}",
                LogLevel.Warn);
            this.ReleaseLocks(contract);
            this.BeginReturn(contract, "contract.failure.storage-lock");
        }
    }

    private void OnFirstLockAcquired(
        Guid contractId,
        int sequence,
        Chest first,
        Chest second)
    {
        ActiveStorageSortContract? contract = this.ActiveContract;
        if (contract is null
            || contract.Id != contractId
            || contract.Phase != StorageSortContractPhase.WaitingForFirstLock
            || contract.CurrentStep.Transfer.Sequence != sequence
            || !ReferenceEquals(contract.FirstLockChest, first)
            || !ReferenceEquals(contract.SecondLockChest, second))
        {
            if (first.GetMutex().IsLockHeld())
                first.GetMutex().ReleaseLock();
            return;
        }

        contract.Phase = StorageSortContractPhase.WaitingForSecondLock;
        contract.PhaseTicks = 0;
        try
        {
            second.GetMutex().RequestLock(
                () => this.OnSecondLockAcquired(contract.Id, sequence, first, second),
                () => this.OnLockFailed(contract.Id, sequence, first, second));
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Could not request second storage-sort chest lock: {ex.Message}",
                LogLevel.Warn);
            this.ReleaseLocks(contract);
            this.BeginReturn(contract, "contract.failure.storage-lock");
        }
    }

    private void OnSecondLockAcquired(
        Guid contractId,
        int sequence,
        Chest first,
        Chest second)
    {
        ActiveStorageSortContract? contract = this.ActiveContract;
        if (contract is null
            || contract.Id != contractId
            || contract.Phase != StorageSortContractPhase.WaitingForSecondLock
            || contract.CurrentStep.Transfer.Sequence != sequence
            || !ReferenceEquals(contract.FirstLockChest, first)
            || !ReferenceEquals(contract.SecondLockChest, second))
        {
            ReleaseLock(second);
            ReleaseLock(first);
            return;
        }

        StorageSortRouteStep step = contract.CurrentStep;
        Guid transferId = contract.TransferIds[step.Transfer.Sequence];
        StorageSortLockedTransferResult result;
        try
        {
            result = contract.Session.TryExecuteLocked(
                step.Transfer,
                this.RecoveryManager,
                contract.Id,
                transferId);
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Storage-sort transfer {step.Transfer.Sequence} failed before a result was produced: {ex}",
                LogLevel.Error);
            this.BeginReturn(contract, "contract.failure.storage-changed");
            return;
        }
        finally
        {
            this.ReleaseLocks(contract);
        }

        if (!result.IsSuccess)
        {
            contract.PersistedRecoveryItems += result.PersistedRecoveryItems;
            contract.UnresolvedItem = result.UnresolvedItem;
            contract.UnresolvedItemDetached = result.UnresolvedItemDetached;
            contract.UnresolvedTransferId = transferId;
            contract.FailureKey = result.RequiresPersistentRecovery
                ? "storage-sort.failure.recovery-pending"
                : "contract.failure.storage-changed";
            if (result.RequiresPersistentRecovery && result.UnresolvedItem is not null)
            {
                contract.Phase = StorageSortContractPhase.RecoveryBlocked;
                contract.PhaseTicks = 0;
                this.Monitor.Log(
                    $"Storage-sort contract {contract.Id:N} retained unresolved transfer "
                    + $"{transferId:N}; finalization is blocked.",
                    LogLevel.Error);
                return;
            }

            this.BeginReturn(contract, contract.FailureKey);
            return;
        }

        contract.CompletedTransfers.Add(new StorageSortCompletedTransfer(
            step.Transfer.Sequence,
            step.Transfer.ItemId,
            contract.TransferSummaries[step.Transfer.Sequence].DisplayName,
            step.Transfer.Category,
            contract.TransferSummaries[step.Transfer.Sequence].Quality,
            result.MovedItems,
            step.Transfer.SourceChest,
            step.Transfer.DestinationChest));
        contract.MovedItems += result.MovedItems;
        contract.Phase = StorageSortContractPhase.ActingAtDestination;
        contract.PhaseTicks = 0;
    }

    private void OnLockFailed(
        Guid contractId,
        int sequence,
        Chest first,
        Chest second)
    {
        ActiveStorageSortContract? contract = this.ActiveContract;
        if (contract is null
            || contract.Id != contractId
            || contract.Phase is not (StorageSortContractPhase.WaitingForFirstLock
                or StorageSortContractPhase.WaitingForSecondLock)
            || contract.CurrentStep.Transfer.Sequence != sequence
            || !ReferenceEquals(contract.FirstLockChest, first)
            || !ReferenceEquals(contract.SecondLockChest, second))
        {
            ReleaseLock(second);
            ReleaseLock(first);
            return;
        }

        this.ReleaseLocks(contract);
        this.BeginReturn(contract, "contract.failure.storage-lock");
    }

    private void AdvanceOrReturn(ActiveStorageSortContract contract)
    {
        contract.StepIndex++;
        contract.RouteOverride = null;
        if (contract.StepIndex >= contract.RoutePlan.Steps.Count)
        {
            this.BeginReturn(contract, failureKey: null);
            return;
        }

        this.BeginSourceTravel(contract);
    }

    private void TryResolveBlockedRecovery(ActiveStorageSortContract contract)
    {
        if (!ReferenceEquals(this.ActiveContract, contract)
            || contract.UnresolvedItem is null
            || !contract.UnresolvedItemDetached
            || contract.UnresolvedTransferId == Guid.Empty)
        {
            return;
        }

        int stack = contract.UnresolvedItem.Stack;
        StorageSortRecoveryWriteStatus status = this.RecoveryManager.TryPersistDetached(
            contract.Id,
            contract.UnresolvedTransferId,
            contract.UnresolvedItem);
        if (status != StorageSortRecoveryWriteStatus.Persisted)
            return;

        contract.PersistedRecoveryItems += stack;
        contract.UnresolvedItem = null;
        contract.UnresolvedItemDetached = false;
        this.BeginReturn(contract, contract.FailureKey ?? "storage-sort.failure.recovery-pending");
    }

    private void BeginReturn(
        ActiveStorageSortContract contract,
        string? failureKey,
        bool retry = false)
    {
        if (!ReferenceEquals(this.ActiveContract, contract)
            || contract.Phase == StorageSortContractPhase.Returned
            || (contract.Phase == StorageSortContractPhase.Returning && !retry))
        {
            return;
        }

        this.ReleaseLocks(contract);
        contract.FailureKey ??= failureKey;
        this.RoutePlanner.TryCreateDirectRoute(
            contract.Farm,
            contract.Lease.Worker,
            contract.Lease.Worker.TilePoint,
            contract.RoutePlan.ArrivalTile,
            contract.RouteObstacles,
            out Stack<Point>? returnPath);

        if (returnPath is null)
        {
            contract.Phase = StorageSortContractPhase.Returning;
            contract.PhaseTicks = 0;
            this.HandleTravelInterruption(
                contract,
                TravelInterruptionKind.ControllerSetupFailed,
                explicitCollisionProbe: "no safe route to farm entrance");
            return;
        }

        contract.Phase = StorageSortContractPhase.Returning;
        contract.PhaseTicks = 0;
        this.BeginPath(
            contract,
            returnPath,
            contract.RoutePlan.ArrivalTile,
            Game1.left,
            this.OnReturned);
    }

    private void OnReturned(Character character, GameLocation location)
    {
        ActiveStorageSortContract? contract = this.ActiveContract;
        if (!IsExpectedArrival(
                contract,
                character,
                location,
                StorageSortContractPhase.Returning))
        {
            return;
        }

        contract!.Phase = StorageSortContractPhase.Returned;
        contract.PhaseTicks = 0;
        contract.Lease.Worker.Halt();
    }

    private void FinishContract(
        ActiveStorageSortContract contract,
        bool succeeded,
        string? failureKey,
        bool mustFinalizeNow = false)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;
        this.ReleaseLocks(contract);
        if (contract.UnresolvedItem is not null)
        {
            contract.FailureKey ??= "storage-sort.failure.recovery-pending";
            contract.Phase = StorageSortContractPhase.RecoveryBlocked;
            contract.PhaseTicks = 0;
            return;
        }

        if (!contract.FinalizationPrepared)
        {
            this.WorkforceRoutes?.ReleaseWorker(contract.Lease.Worker.Name);
            contract.FinalizationPrepared = true;
            contract.PendingSucceeded = succeeded;
            contract.FailureKey ??= failureKey;
            contract.PhaseTicks = 0;
        }

        if (contract.ManagedByShift)
        {
            this.CompleteManagedStage(contract);
            return;
        }

        this.ContinueFinalization(contract, mustFinalizeNow);
    }

    private void CompleteManagedStage(ActiveStorageSortContract contract)
    {
        StorageSortCompletedTransfer[] skippedTransfers = contract.RoutePlan.Steps
            .Skip(contract.CompletedTransfers.Count)
            .Select(step => ToReportTransfer(
                step.Transfer,
                contract.TransferSummaries[step.Transfer.Sequence],
                step.Transfer.Quantity))
            .ToArray();
        bool succeeded = contract.PendingSucceeded && StorageSortContractAudit.IsReportBalanced(
            contract.RoutePlan.Steps.Count,
            contract.CompletedTransfers,
            skippedTransfers,
            contract.MovedItems,
            contract.PersistedRecoveryItems);
        string reasonKey = succeeded
            ? ""
            : contract.FailureKey ?? "contract.failure.storage-audit";
        List<NamedContractCargoState> placedItems = contract.CompletedTransfers
            .Select(transfer => new NamedContractCargoState(
                contract.TransferIds[transfer.Sequence].ToString("N"),
                transfer.ItemId,
                transfer.DisplayName,
                transfer.Quality,
                transfer.Quantity))
            .ToList();
        List<string> completedTransferIds = contract.CompletedTransfers
            .Select(transfer => contract.TransferIds[transfer.Sequence].ToString("N"))
            .ToList();
        if (contract.PersistedRecoveryItems > 0
            && contract.UnresolvedTransferId != Guid.Empty
            && contract.StepIndex < contract.RoutePlan.Steps.Count)
        {
            StorageSortRouteStep recoveryStep = contract.RoutePlan.Steps[contract.StepIndex];
            StorageSortTransferItemSummary recoverySummary =
                contract.TransferSummaries[recoveryStep.Transfer.Sequence];
            placedItems.Add(new NamedContractCargoState(
                contract.UnresolvedTransferId.ToString("N"),
                recoverySummary.QualifiedItemId,
                recoverySummary.DisplayName,
                recoverySummary.Quality,
                contract.PersistedRecoveryItems));
            completedTransferIds.Add(contract.UnresolvedTransferId.ToString("N"));
        }

        this.LastCompletion = new NamedContractCompletionState(
            contract.Id.ToString("N"),
            contract.RequestId,
            contract.Requester.UniqueMultiplayerID,
            contract.Lease.Worker.Name,
            NamedFarmTask.StorageSorting,
            succeeded,
            reasonKey,
            contract.CompletedTransfers.Count,
            PlayerItems: 0,
            ChestItems: contract.MovedItems,
            OverflowItems: 0,
            QuarantinedItems: contract.PersistedRecoveryItems,
            DroppedItems: 0,
            BillableHours: 0,
            ChargedGold: 0,
            RefundedGold: 0,
            placedItems,
            completedTransferIds,
            contract.CompletedTransfers.Select(ToProtocolTransfer).ToArray(),
            skippedTransfers.Select(ToProtocolTransfer).ToArray());
        this.ActiveContract = null;
    }

    private void ContinueFinalization(ActiveStorageSortContract contract, bool mustFinalizeNow)
    {
        if (!ReferenceEquals(this.ActiveContract, contract) || !contract.FinalizationPrepared)
            return;

        NpcLeaseRestoreResult restoreResult = contract.Lease.Restore();
        NpcLeaseRecoveryAction action = NpcLeaseRecoveryPolicy.Select(
            restoreResult,
            contract.RestoreWaitTicks,
            mustFinalizeNow);
        if (action == NpcLeaseRecoveryAction.Retry)
            return;
        if (action == NpcLeaseRecoveryAction.Relinquish)
            restoreResult = contract.Lease.RelinquishToConflictingController();

        WateringContractSettlement settlement = WateringContractSettlement.Create(
            contract.Preview,
            contract.Dispatched,
            contract.Lease.StartTime,
            Game1.timeOfDay);
        contract.Requester.Money += settlement.RefundedGold;
        bool finalSucceeded = contract.PendingSucceeded
            && restoreResult == NpcLeaseRestoreResult.Restored;
        StorageSortCompletedTransfer[] skippedTransfers = contract.RoutePlan.Steps
            .Skip(contract.CompletedTransfers.Count)
            .Select(step => ToReportTransfer(
                step.Transfer,
                contract.TransferSummaries[step.Transfer.Sequence],
                step.Transfer.Quantity))
            .ToArray();
        bool reportBalanced = StorageSortContractAudit.IsReportBalanced(
            contract.RoutePlan.Steps.Count,
            contract.CompletedTransfers,
            skippedTransfers,
            contract.MovedItems,
            contract.PersistedRecoveryItems);
        if (!reportBalanced)
        {
            finalSucceeded = false;
            contract.FailureKey = "contract.failure.storage-audit";
        }
        string reasonKey = finalSucceeded
            ? ""
            : restoreResult != NpcLeaseRestoreResult.Restored
                ? GetRestoreFailureKey(restoreResult)
                : contract.FailureKey ?? "contract.failure.unknown";
        this.LastReport = new StorageSortContractReport(
            contract.Id.ToString("N"),
            contract.RequestId,
            contract.Requester.UniqueMultiplayerID,
            contract.Lease.Worker.Name,
            finalSucceeded,
            reasonKey,
            contract.CompletedTransfers.ToArray(),
            contract.MovedItems,
            skippedTransfers,
            contract.PersistedRecoveryItems,
            settlement.BillableHours,
            settlement.ChargedGold,
            settlement.RefundedGold);
        List<NamedContractCargoState> placedItems = contract.CompletedTransfers
            .Select(transfer => new NamedContractCargoState(
                contract.TransferIds[transfer.Sequence].ToString("N"),
                transfer.ItemId,
                transfer.DisplayName,
                transfer.Quality,
                transfer.Quantity))
            .ToList();
        List<string> completedTransferIds = contract.CompletedTransfers
            .Select(transfer => contract.TransferIds[transfer.Sequence].ToString("N"))
            .ToList();
        if (contract.PersistedRecoveryItems > 0
            && contract.UnresolvedTransferId != Guid.Empty
            && contract.StepIndex < contract.RoutePlan.Steps.Count)
        {
            StorageSortRouteStep recoveryStep = contract.RoutePlan.Steps[contract.StepIndex];
            StorageSortTransferItemSummary recoverySummary =
                contract.TransferSummaries[recoveryStep.Transfer.Sequence];
            placedItems.Add(new NamedContractCargoState(
                contract.UnresolvedTransferId.ToString("N"),
                recoverySummary.QualifiedItemId,
                recoverySummary.DisplayName,
                recoverySummary.Quality,
                contract.PersistedRecoveryItems));
            completedTransferIds.Add(contract.UnresolvedTransferId.ToString("N"));
        }
        this.LastCompletion = new NamedContractCompletionState(
            contract.Id.ToString("N"),
            contract.RequestId,
            contract.Requester.UniqueMultiplayerID,
            contract.Lease.Worker.Name,
            NamedFarmTask.StorageSorting,
            finalSucceeded,
            reasonKey,
            contract.CompletedTransfers.Count,
            PlayerItems: 0,
            ChestItems: contract.MovedItems,
            OverflowItems: 0,
            QuarantinedItems: contract.PersistedRecoveryItems,
            DroppedItems: 0,
            settlement.BillableHours,
            settlement.ChargedGold,
            settlement.RefundedGold,
            placedItems,
            completedTransferIds,
            contract.CompletedTransfers.Select(ToProtocolTransfer).ToArray(),
            skippedTransfers.Select(ToProtocolTransfer).ToArray());
        this.ActiveContract = null;

        string messageKey = finalSucceeded
            ? "storage-sort.hud.completed"
            : "storage-sort.hud.stopped";
        object messageArguments = finalSucceeded
            ? new
            {
                worker = contract.Lease.Worker.displayName,
                transfers = contract.CompletedTransfers.Count,
                items = contract.MovedItems,
                skipped = skippedTransfers.Length,
                hours = settlement.BillableHours,
                paid = settlement.ChargedGold,
                refunded = settlement.RefundedGold
            }
            : new
            {
                worker = contract.Lease.Worker.displayName,
                reason = this.Translation.Get(reasonKey),
                transfers = contract.CompletedTransfers.Count,
                items = contract.MovedItems,
                skipped = skippedTransfers.Length,
                recovery = contract.PersistedRecoveryItems,
                hours = settlement.BillableHours,
                paid = settlement.ChargedGold,
                refunded = settlement.RefundedGold
            };
        Game1.addHUDMessage(new HUDMessage(
            this.Translation.Get(messageKey, messageArguments),
            finalSucceeded ? HUDMessage.newQuest_type : HUDMessage.error_type));
    }

    private void ReleaseLocks(ActiveStorageSortContract contract)
    {
        ReleaseLock(contract.SecondLockChest);
        ReleaseLock(contract.FirstLockChest);
        contract.FirstLockChest = null;
        contract.SecondLockChest = null;
    }

    private static void ReleaseLock(Chest? chest)
    {
        NetMutex? mutex = chest?.GetMutex();
        if (mutex?.IsLockHeld() == true)
            mutex.ReleaseLock();
    }

    private bool FailStart(string failureKey)
    {
        this.LastStartFailureKey = failureKey;
        Game1.addHUDMessage(new HUDMessage(
            this.Translation.Get(failureKey),
            HUDMessage.error_type));
        return false;
    }

    private static bool IsExpectedArrival(
        ActiveStorageSortContract? contract,
        Character character,
        GameLocation location,
        StorageSortContractPhase phase)
    {
        return contract is not null
            && ReferenceEquals(character, contract.Lease.Worker)
            && ReferenceEquals(location, contract.Farm)
            && contract.Phase == phase;
    }

    private static Point ToPoint(GridPoint tile)
    {
        return new Point(tile.X, tile.Y);
    }

    private static StorageSortCompletedTransfer ToReportTransfer(
        StorageSortTransfer transfer,
        StorageSortTransferItemSummary summary,
        int quantity)
    {
        return new StorageSortCompletedTransfer(
            transfer.Sequence,
            transfer.ItemId,
            summary.DisplayName,
            transfer.Category,
            summary.Quality,
            quantity,
            transfer.SourceChest,
            transfer.DestinationChest);
    }

    private static NamedContractTransferState ToProtocolTransfer(
        StorageSortCompletedTransfer transfer)
    {
        return new NamedContractTransferState(
            transfer.Sequence,
            transfer.ItemId,
            transfer.DisplayName,
            transfer.Category,
            transfer.Quality,
            transfer.Quantity,
            transfer.SourceChest.X,
            transfer.SourceChest.Y,
            transfer.DestinationChest.X,
            transfer.DestinationChest.Y);
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

    private static string GetSnapshotFailureKey(StorageSortSnapshotFailure failure)
    {
        return failure switch
        {
            StorageSortSnapshotFailure.NoEligibleChest => "storage-sort.start.no-chest",
            StorageSortSnapshotFailure.BusyChest => "contract.failure.storage-lock",
            StorageSortSnapshotFailure.NoTransfers => "storage-sort.start.no-work",
            StorageSortSnapshotFailure.InsufficientCapacity => "storage-sort.start.no-capacity",
            _ => "storage-sort.start.invalid"
        };
    }

    private static string GetRouteFailureKey(StorageSortRouteFailure failure)
    {
        return failure switch
        {
            StorageSortRouteFailure.UnsupportedFarmMap => "contract.start.unsupported-map",
            StorageSortRouteFailure.NoBoundaryEntrance or StorageSortRouteFailure.NoSafeArrival =>
                "contract.start.no-arrival",
            _ => "storage-sort.start.no-route"
        };
    }

    private static string GetRestoreFailureKey(NpcLeaseRestoreResult result)
    {
        return result == NpcLeaseRestoreResult.Relinquished
            ? "contract.failure.restore-relinquished"
            : "contract.failure.restore-ownership-lost";
    }

    private enum StorageSortContractPhase
    {
        TravelingToSource,
        InspectingSource,
        TravelingToDestination,
        WaitingForFirstLock,
        WaitingForSecondLock,
        ActingAtDestination,
        Returning,
        Returned,
        RecoveryBlocked
    }

    private sealed class ActiveStorageSortContract
    {
        public ActiveStorageSortContract(
            Guid id,
            string requestId,
            Farmer requester,
            NpcWorkLease lease,
            WorkContractPreview preview,
            Farm farm,
            StorageSortRuntimePlan runtimePlan,
            StorageSortRoutePlan routePlan,
            StorageSortExecutionSession session,
            bool managedByShift = false)
        {
            this.Id = id;
            this.RequestId = requestId;
            this.Requester = requester;
            this.Lease = lease;
            this.Preview = preview;
            this.Farm = farm;
            this.RuntimePlan = runtimePlan;
            this.RoutePlan = routePlan;
            this.Session = session;
            this.ManagedByShift = managedByShift;
            this.TransferSummaries = routePlan.Steps.ToDictionary(
                step => step.Transfer.Sequence,
                step => session.TryGetItemSummary(
                    step.Transfer.StackId,
                    out StorageSortTransferItemSummary? summary)
                    && summary is not null
                        ? summary
                        : throw new InvalidOperationException(
                            $"Storage-sort transfer {step.Transfer.Sequence} lost its item summary."));
            this.TransferIds = routePlan.Steps.ToDictionary(
                step => step.Transfer.Sequence,
                _ => Guid.NewGuid());
        }

        public Guid Id { get; }
        public string RequestId { get; }
        public Farmer Requester { get; }
        public NpcWorkLease Lease { get; }
        public WorkContractPreview Preview { get; }
        public Farm Farm { get; }
        public StorageSortRuntimePlan RuntimePlan { get; }
        public StorageSortRoutePlan RoutePlan { get; }
        public StorageSortExecutionSession Session { get; }
        public bool ManagedByShift { get; }
        public Dictionary<int, StorageSortTransferItemSummary> TransferSummaries { get; }
        public Dictionary<int, Guid> TransferIds { get; }
        public List<StorageSortCompletedTransfer> CompletedTransfers { get; } = new();
        public TravelProgressWatchdog TravelWatchdog { get; } = new();
        public TravelObstacleLedger RouteObstacles { get; } = new();
        public TravelFailureLedger RouteFailures { get; } = new(MaximumRouteFailures);
        public StorageSortRouteStep CurrentStep => this.RouteOverride
            ?? this.RoutePlan.Steps[this.StepIndex];
        public StorageSortRouteStep? RouteOverride { get; set; }
        public StorageSortContractPhase Phase { get; set; }
        public PathFindController? Controller { get; set; }
        public Chest? FirstLockChest { get; set; }
        public Chest? SecondLockChest { get; set; }
        public Item? UnresolvedItem { get; set; }
        public Guid UnresolvedTransferId { get; set; }
        public bool UnresolvedItemDetached { get; set; }
        public int StepIndex { get; set; }
        public int PhaseTicks { get; set; }
        public int RestoreWaitTicks { get; set; }
        public int MovedItems { get; set; }
        public int PersistedRecoveryItems { get; set; }
        public bool Dispatched { get; set; }
        public bool FinalizationPrepared { get; set; }
        public bool PendingSucceeded { get; set; }
        public string? FailureKey { get; set; }
    }
}
