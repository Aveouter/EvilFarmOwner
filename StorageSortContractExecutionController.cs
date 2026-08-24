using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Network;
using StardewValley.Objects;
using StardewValley.Pathfinding;

namespace EvilFarmOwner;

internal sealed record StorageSortCompletedTransfer(
    int Sequence,
    string ItemId,
    int Category,
    int Quantity,
    GridPoint SourceChest,
    GridPoint DestinationChest);

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

internal static class StorageSortContractAudit
{
    public static bool IsReportBalanced(
        int plannedTransfers,
        IReadOnlyList<StorageSortCompletedTransfer> completed,
        IReadOnlyList<StorageSortCompletedTransfer> skipped,
        int movedItems,
        int persistedRecoveryItems)
    {
        if (plannedTransfers < 0
            || movedItems < 0
            || persistedRecoveryItems < 0
            || completed.Count + skipped.Count != plannedTransfers
            || completed.Sum(transfer => (long)transfer.Quantity) != movedItems
            || skipped.Sum(transfer => (long)transfer.Quantity) < persistedRecoveryItems)
        {
            return false;
        }

        int[] sequences = completed
            .Concat(skipped)
            .Select(transfer => transfer.Sequence)
            .OrderBy(sequence => sequence)
            .ToArray();
        return sequences.SequenceEqual(Enumerable.Range(1, plannedTransfers));
    }
}

internal sealed class StorageSortContractExecutionController
{
    private const int LatestStartTime = 1600;
    private const int HardStopTime = 2200;
    private const int SourceInspectionTicks = 12;
    private const int DestinationActionTicks = 24;
    private const int MaximumTravelTicks = 3600;
    private const int MaximumStalledTravelTicks = 180;
    private const int MaximumLockWaitTicks = 300;

    private readonly IMonitor Monitor;
    private readonly WorkerRosterService WorkerRoster;
    private readonly StorageSortRoutePlanner RoutePlanner;
    private readonly StorageSortRecoveryManager RecoveryManager;
    private ActiveStorageSortContract? ActiveContract;

    public StorageSortContractExecutionController(
        IMonitor monitor,
        WorkerRosterService workerRoster,
        StorageSortRecoveryManager recoveryManager)
    {
        this.Monitor = monitor;
        this.WorkerRoster = workerRoster;
        this.RoutePlanner = new StorageSortRoutePlanner(monitor);
        this.RecoveryManager = recoveryManager;
    }

    public bool HasActiveContract => this.ActiveContract is not null;

    public string? ActiveContractId => this.ActiveContract?.Id.ToString("N");

    public string? LastStartFailureKey { get; private set; }

    public StorageSortContractReport? LastReport { get; private set; }

    public bool TryStart(
        long requestingPlayerId,
        string workerInternalName,
        string requestId)
    {
        this.LastStartFailureKey = null;
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return this.FailStart("contract.start.host-only");
        if (this.RecoveryManager.HasPendingRecovery)
            return this.FailStart("harvest.start.quarantine-pending");
        if (!Guid.TryParseExact(requestId, "N", out _))
            return this.FailStart("multiplayer.reject.request-id");
        if (this.ActiveContract is not null)
            return this.FailStart("contract.start.already-active");
        if (Game1.timeOfDay > LatestStartTime)
            return this.FailStart("harvest.start.too-late");

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
            return this.FailStart("contract.start.worker-unavailable");

        WorkContractPreview preview = ContractPreviewService.Create(
            requester.getFriendshipHeartLevelForNPC(worker.Name),
            Game1.dayOfMonth);
        if (requester.Money < preview.MaximumAuthorizedWage)
            return this.FailStart("contract.start.insufficient-funds");

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

        StorageSortRouteStep step = contract.CurrentStep;
        contract.Phase = StorageSortContractPhase.TravelingToSource;
        contract.PhaseTicks = 0;
        this.BeginPath(
            contract,
            step.SourcePath,
            step.SourceInteractionTile,
            GetFacingDirection(step.SourceInteractionTile, ToPoint(step.Transfer.SourceChest)),
            this.OnArrivedAtSource);
    }

    private void BeginDestinationTravel(ActiveStorageSortContract contract)
    {
        StorageSortRouteStep step = contract.CurrentStep;
        contract.Phase = StorageSortContractPhase.TravelingToDestination;
        contract.PhaseTicks = 0;
        this.BeginPath(
            contract,
            step.DestinationPath,
            step.DestinationInteractionTile,
            GetFacingDirection(
                step.DestinationInteractionTile,
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
                throw new InvalidOperationException(failure);
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
                throw new InvalidOperationException($"No path to {destination}.");

            contract.Controller = controller;
            contract.Lease.AttachController(controller);
            contract.TravelWatchdog.Reset(worker.Position.X, worker.Position.Y);
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Storage-sort route failed during {contract.Phase}: {ex.Message}",
                LogLevel.Warn);
            if (contract.Phase == StorageSortContractPhase.Returning)
            {
                this.FinishContract(
                    contract,
                    succeeded: false,
                    contract.FailureKey ?? "contract.failure.return-path");
            }
            else
            {
                this.BeginReturn(contract, "contract.failure.route-interrupted");
            }
        }
    }

    private void UpdateTravel(ActiveStorageSortContract contract)
    {
        if (this.TryCompleteTravelAtDestination(contract))
            return;
        if (contract.PhaseTicks > MaximumTravelTicks)
        {
            this.HandleTravelFailure(contract, "contract.failure.route-timeout");
            return;
        }

        NPC worker = contract.Lease.Worker;
        if (contract.Controller is not null
            && worker.controller is not null
            && !ReferenceEquals(worker.controller, contract.Controller))
        {
            this.HandleTravelFailure(contract, "contract.failure.controller-conflict");
            return;
        }
        if ((Game1.activeClickableMenu is null || Game1.IsMultiplayer)
            && contract.TravelWatchdog.Tick(
                worker.Position.X,
                worker.Position.Y,
                MaximumStalledTravelTicks))
        {
            this.HandleTravelFailure(contract, "contract.failure.route-interrupted");
            return;
        }
        if (contract.PhaseTicks > 1 && worker.controller is null)
            this.HandleTravelFailure(contract, "contract.failure.route-interrupted");
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
            this.HandleTravelFailure(contract, "contract.failure.controller-conflict");
            return true;
        }
        if (ReferenceEquals(worker.controller, contract.Controller))
            worker.controller = null;
        contract.Controller = null;
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

    private void HandleTravelFailure(ActiveStorageSortContract contract, string failureKey)
    {
        NPC worker = contract.Lease.Worker;
        if (ReferenceEquals(worker.controller, contract.Controller))
            worker.controller = null;
        contract.Controller = null;
        worker.Halt();
        if (contract.Phase == StorageSortContractPhase.Returning)
        {
            this.FinishContract(contract, succeeded: false, failureKey);
            return;
        }

        this.BeginReturn(contract, failureKey);
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
        first.GetMutex().RequestLock(
            () => this.OnFirstLockAcquired(contract.Id, sequence, first, second),
            () => this.OnLockFailed(contract.Id, sequence, first, second));
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
        second.GetMutex().RequestLock(
            () => this.OnSecondLockAcquired(contract.Id, sequence, first, second),
            () => this.OnLockFailed(contract.Id, sequence, first, second));
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
                ? "harvest.failure.quarantine-pending"
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
            step.Transfer.Category,
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
        if (contract.StepIndex >= contract.RoutePlan.Steps.Count)
        {
            this.BeginReturn(contract, failureKey: null, usePlannedReturn: true);
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
        this.BeginReturn(contract, contract.FailureKey ?? "harvest.failure.quarantine-pending");
    }

    private void BeginReturn(
        ActiveStorageSortContract contract,
        string? failureKey,
        bool usePlannedReturn = false)
    {
        if (!ReferenceEquals(this.ActiveContract, contract)
            || contract.Phase is StorageSortContractPhase.Returning
                or StorageSortContractPhase.Returned)
        {
            return;
        }

        this.ReleaseLocks(contract);
        contract.FailureKey ??= failureKey;
        Stack<Point>? returnPath = null;
        if (usePlannedReturn)
        {
            returnPath = contract.RoutePlan.ReturnPath;
        }
        else if (FarmNavigationMap.TryBuild(
                     contract.Farm,
                     contract.Lease.Worker,
                     contract.Lease.Worker.TilePoint,
                     this.Monitor,
                     out GridRouteMap? routes)
            && routes is not null
            && routes.TryGetPath(
                new GridPoint(contract.RoutePlan.ArrivalTile.X, contract.RoutePlan.ArrivalTile.Y),
                out IReadOnlyList<GridPoint> gridPath))
        {
            returnPath = FarmNavigationMap.ToPath(gridPath);
        }

        if (returnPath is null)
        {
            this.FinishContract(
                contract,
                succeeded: false,
                contract.FailureKey ?? "contract.failure.return-path");
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
            contract.FailureKey ??= "harvest.failure.quarantine-pending";
            contract.Phase = StorageSortContractPhase.RecoveryBlocked;
            contract.PhaseTicks = 0;
            return;
        }

        if (!contract.FinalizationPrepared)
        {
            contract.FinalizationPrepared = true;
            contract.PendingSucceeded = succeeded;
            contract.FailureKey ??= failureKey;
            contract.PhaseTicks = 0;
        }

        this.ContinueFinalization(contract, mustFinalizeNow);
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
            .Select(step => ToReportTransfer(step.Transfer, step.Transfer.Quantity))
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
        this.ActiveContract = null;
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
        int quantity)
    {
        return new StorageSortCompletedTransfer(
            transfer.Sequence,
            transfer.ItemId,
            transfer.Category,
            quantity,
            transfer.SourceChest,
            transfer.DestinationChest);
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
            StorageSortSnapshotFailure.NoEligibleChest => "harvest.start.no-storage-chest",
            StorageSortSnapshotFailure.BusyChest => "contract.failure.storage-lock",
            StorageSortSnapshotFailure.NoTransfers => "contract.failure.no-work",
            StorageSortSnapshotFailure.InsufficientCapacity => "harvest.failure.storage-unavailable",
            _ => "contract.failure.storage-changed"
        };
    }

    private static string GetRouteFailureKey(StorageSortRouteFailure failure)
    {
        return failure switch
        {
            StorageSortRouteFailure.UnsupportedFarmMap => "contract.start.unsupported-map",
            StorageSortRouteFailure.NoBoundaryEntrance or StorageSortRouteFailure.NoSafeArrival =>
                "contract.start.no-arrival",
            _ => "contract.failure.return-path"
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
            StorageSortExecutionSession session)
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
        public Dictionary<int, Guid> TransferIds { get; }
        public List<StorageSortCompletedTransfer> CompletedTransfers { get; } = new();
        public TravelProgressWatchdog TravelWatchdog { get; } = new();
        public StorageSortRouteStep CurrentStep => this.RoutePlan.Steps[this.StepIndex];
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
