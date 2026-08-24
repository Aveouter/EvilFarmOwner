using Microsoft.Xna.Framework;
using StardewModdingAPI;
using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Serialization;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Locations;
using StardewValley.Network;
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
    private ActiveHarvestContract? ActiveContract;
    private NamedContractCompletionState? LastCompletion;
    private bool HasPendingQuarantineRecovery;
    private int QuarantineRecoveryRetryTicks;

    public HarvestingContractExecutionController(
        ITranslationHelper translation,
        IMonitor monitor,
        WorkerRosterService workerRoster,
        HarvestAcceptanceFaults? acceptanceFaults = null)
    {
        this.Translation = translation;
        this.Monitor = monitor;
        this.WorkerRoster = workerRoster;
        this.TargetPlanner = new HarvestTargetPlanner(monitor);
        this.ChestRouter = new HarvestChestRouter(monitor);
        this.AcceptanceFaults = acceptanceFaults ?? new HarvestAcceptanceFaults();
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

    public bool TryStart(long requestingPlayerId, string workerInternalName, string requestId)
    {
        this.LastStartFailureKey = null;
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return this.FailStart("contract.start.host-only");

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

        int friendshipHearts = requester.getFriendshipHeartLevelForNPC(worker.Name);
        WorkContractPreview preview = ContractPreviewService.Create(
            friendshipHearts,
            Game1.dayOfMonth,
            worker.Name,
            NamedFarmTask.Harvesting);
        if (requester.Money < preview.MaximumAuthorizedWage)
        {
            this.LastStartFailureKey = "contract.start.insufficient-funds";
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("contract.start.insufficient-funds", new { gold = preview.MaximumAuthorizedWage }),
                HUDMessage.error_type));
            return false;
        }

        HarvestPlanResult planResult = this.TargetPlanner.TryCreate(mainFarm, worker);
        if (!planResult.IsSuccess || planResult.Plan is null)
            return this.FailStart(this.GetPlanFailureTranslationKey(planResult.Failure));

        if (!HarvestChestRouter.HasEligibleChest(mainFarm))
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
            planResult.Plan);
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
                + $"farm-boundary tile {planResult.Plan.ArrivalTile}.",
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
                }

                if (contract.PhaseTicks >= contract.ActionDurationTicks)
                {
                    contract.Lease.Worker.Sprite?.ClearAnimation();
                    this.BeginDeliveryOrReturn(contract);
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
                        ? "harvest.failure.storage-unavailable"
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
            contract.TransferLedger.GetCompletedTransferIds());
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
            this.HandleInterruptedTravel(contract, timedOut: true);
            return;
        }

        if (contract.Controller is not null
            && contract.Lease.Worker.controller is not null
            && !ReferenceEquals(contract.Lease.Worker.controller, contract.Controller))
        {
            this.FinishContract(contract, succeeded: false, "contract.failure.controller-conflict");
            return;
        }

        if ((Game1.activeClickableMenu is null || Game1.IsMultiplayer)
            && contract.TravelWatchdog.Tick(
                contract.Lease.Worker.Position.X,
                contract.Lease.Worker.Position.Y,
                MaximumStalledTravelTicks))
        {
            this.HandleInterruptedTravel(contract, timedOut: false);
            return;
        }

        if (contract.PhaseTicks > 1 && contract.Lease.Worker.controller is null)
            this.HandleInterruptedTravel(contract, timedOut: false);
    }

    private bool TryCompleteTravelAtDestination(ActiveHarvestContract contract)
    {
        Point? destination = contract.Phase switch
        {
            HarvestContractPhase.TravelingToTarget => contract.CurrentTarget.InteractionTile,
            HarvestContractPhase.TravelingToChest => contract.CurrentChestRoute?.InteractionTile,
            HarvestContractPhase.Returning => contract.Plan.ArrivalTile,
            _ => null
        };
        NPC worker = contract.Lease.Worker;
        if (destination is null
            || !ReferenceEquals(worker.currentLocation, contract.Farm)
            || worker.TilePoint != destination.Value)
            return false;

        if (worker.controller is not null && !ReferenceEquals(worker.controller, contract.Controller))
        {
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
                this.OnArrivedAtTarget(worker, contract.Farm);
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

    private void HandleInterruptedTravel(ActiveHarvestContract contract, bool timedOut)
    {
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
                    timedOut ? "travel timeout" : "stalled or stopped controller");
                break;

            case HarvestContractPhase.TravelingToChest:
                this.HandleFailedDeliveryRoute(
                    contract,
                    timedOut ? "travel timeout" : "stalled or stopped controller");
                break;

            case HarvestContractPhase.Returning:
                contract.ReturnReplanAttempts++;
                if (contract.ReturnReplanAttempts > MaximumReturnReplans)
                {
                    this.FinishContract(
                        contract,
                        succeeded: false,
                        timedOut
                            ? "contract.failure.return-timeout"
                            : "contract.failure.return-interrupted");
                    break;
                }

                this.BeginReturn(contract, depositOverflowOnReturn: false);
                break;
        }
    }

    private void HandleFailedTargetRoute(ActiveHarvestContract contract, string reason)
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
            this.BeginNextOrReturn(contract);
            return;
        }

        Point skippedTarget = contract.CurrentTarget.TargetTile;
        if (contract.CompletedTargets.Add(skippedTarget))
            contract.UnreachableTargets++;
        if (decision.Action == TargetRouteFailureAction.SkipTarget)
        {
            this.Monitor.Log(
                $"Harvest target {skippedTarget} exhausted {decision.MaximumRouteFailures} live routes "
                + $"from {origin}; skipping only that crop and continuing "
                + $"[{decision.StalledTargetCount}/{decision.MaximumStalledTargets} stalled crops at this origin].",
                LogLevel.Warn);
            this.BeginNextOrReturn(contract);
            return;
        }

        int remaining = HarvestTargetPlanner.CountRemainingMatureCrops(
            contract.Farm,
            contract.CompletedTargets);
        contract.UnreachableTargets += remaining;
        this.Monitor.Log(
            $"Harvest worker '{contract.Lease.Worker.Name}' exhausted "
            + $"{decision.MaximumStalledTargets} stalled crops from {origin}; "
            + $"returning with {remaining} mature crop(s) marked unreachable.",
            LogLevel.Warn);
        this.BeginReturn(contract, depositOverflowOnReturn: false);
    }

    private void HandleFailedDeliveryRoute(ActiveHarvestContract contract, string reason)
    {
        Point? failedChest = contract.CurrentChestRoute?.ChestTile;
        this.MarkCurrentChestAttempted(contract);

        GridPoint origin = new(
            contract.Lease.Worker.TilePoint.X,
            contract.Lease.Worker.TilePoint.Y);
        TravelReplanDecision decision = contract.ReplanBudget.RecordFailure(
            TravelRoutePurpose.Delivery,
            origin);
        if (decision.CanReplan)
        {
            this.Monitor.Log(
                $"Harvest delivery route from {origin} to chest {failedChest} failed ({reason}); "
                + $"trying another eligible destination "
                + $"[{decision.FailureCount}/{decision.MaximumFailures}].",
                LogLevel.Debug);
            this.BeginDeliveryOrReturn(contract);
            return;
        }

        this.Monitor.Log(
            $"Harvest worker '{contract.Lease.Worker.Name}' exhausted "
            + $"{decision.MaximumFailures} consecutive delivery routes from {origin}; "
            + "stopping the contract because no classified chest remains safely reachable.",
            LogLevel.Warn);
        this.StopForUnavailableStorage(contract, "delivery route retries were exhausted");
    }

    private bool TryHandleStalledEntrance(ActiveHarvestContract contract)
    {
        NPC worker = contract.Lease.Worker;
        if (contract.CompletedTargets.Count > 0
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
            contract.FailedArrivalSides);
        if (!replacement.IsSuccess || replacement.Plan is null)
        {
            this.Monitor.Log(
                $"No remaining farm-boundary entrance can start harvesting after excluding: "
                + $"{string.Join(", ", contract.FailedArrivalSides.OrderBy(FarmEntranceSelection.GetEntrancePriority))}.",
                LogLevel.Warn);
            this.FinishContract(
                contract,
                succeeded: false,
                replacement.Failure == HarvestPlanFailure.NoMatureCrop
                    ? "harvest.failure.target-invalidated"
                    : "contract.failure.entrance-stalled");
            return true;
        }

        try
        {
            HarvestWorkPlan nextPlan = replacement.Plan;
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
            || !ReferenceEquals(location, contract.Farm)
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
        Vector2 targetTile = new(contract.CurrentTarget.TargetTile.X, contract.CurrentTarget.TargetTile.Y);
        if (!HarvestTargetPlanner.IsMatureSupportedCrop(contract.Farm, targetTile)
            || contract.Lease.Worker.currentLocation != contract.Farm
            || contract.Lease.Worker.TilePoint != contract.CurrentTarget.InteractionTile
            || !contract.Farm.terrainFeatures.TryGetValue(targetTile, out TerrainFeature? feature)
            || feature is not HoeDirt dirt
            || dirt.crop is not { } crop)
            return false;

        ContractHarvestCollector collector = new(contract.Farm, contract.Lease.Worker.Position);
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
                + $"from crop {contract.CurrentTarget.TargetTile}; transfer={transferId}.",
                LogLevel.Debug);
        }

        this.ShowHarvestedItem(contract, collector.Items[0]);
        return true;
    }

    private void BeginDeliveryOrReturn(ActiveHarvestContract contract)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        if (contract.Cargo.Count == 0)
        {
            this.BeginNextOrReturn(contract);
            return;
        }

        HarvestCargoEntry entry = contract.Cargo[0];
        HashSet<Point> attempted = contract.GetAttemptedChests(entry.TransferId);
        HarvestChestRoute? route = this.ChestRouter.FindBestRoute(
            contract.Farm,
            contract.Lease.Worker,
            contract.Lease.Worker.TilePoint,
            entry.Item,
            attempted);
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
                    $"first-step collision probe rejected the route: {firstStepFailure}");
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
                $"controller setup failed: {ex.Message}");
        }
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
                HarvestCargoEntry entry = contract.Cargo[0];
                HarvestChestContents contents = HarvestChestRouter.GetContents(route.Chest, entry.Item);
                bool stillMatchesCategory = HarvestChestClassification.Classify(contents).HasValue;
                bool canFullyAccept = HarvestChestRouter.GetAcceptableCapacity(route.Chest, entry.Item)
                    >= entry.Item.Stack;
                if (!stillMatchesCategory || !canFullyAccept)
                {
                    this.MarkCurrentChestAttempted(contract);
                }
                else
                {
                    int requested = entry.Item.Stack;
                    Item? remainder = entry.Item;
                    bool applied = contract.TransferLedger.TryApply(
                        entry.TransferId,
                        () => remainder = route.Chest.addItem(entry.Item));
                    if (!applied)
                    {
                        contract.Cargo.RemoveAt(0);
                    }
                    else
                    {
                        int remaining = remainder?.Stack ?? 0;
                        int delivered = HarvestTransferMath.GetDeliveredCount(requested, remaining);
                        contract.ChestDeliveredItems += delivered;
                        this.Monitor.Log(
                            $"Placed harvest cargo '{entry.Item.QualifiedItemId}' q{entry.Item.Quality} x{delivered} "
                            + $"in chest {route.ChestTile}; remainder={remaining}.",
                            LogLevel.Debug);
                        if (remainder is null)
                        {
                            contract.Cargo.RemoveAt(0);
                        }
                        else
                        {
                            entry.Item = remainder;
                            entry.TransferId = Guid.NewGuid().ToString("N");
                            contract.GetAttemptedChests(entry.TransferId).Add(route.ChestTile);
                            storageBecameUnavailable = true;
                        }
                    }
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
            this.BeginDeliveryOrReturn(contract);
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

    private void BeginNextOrReturn(ActiveHarvestContract contract)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        if (contract.StorageUnavailable)
        {
            if (contract.Cargo.Count > 0)
                this.BeginOverflowDeposit(contract);
            else
                this.BeginReturn(contract, depositOverflowOnReturn: false);
            return;
        }

        if (contract.Cargo.Count > 0)
        {
            this.BeginDeliveryOrReturn(contract);
            return;
        }

        if (Game1.timeOfDay >= StopAcquiringTime)
        {
            contract.RemainingTargets = HarvestTargetPlanner.CountRemainingMatureCrops(
                contract.Farm,
                contract.CompletedTargets);
            this.BeginReturn(contract, depositOverflowOnReturn: false);
            return;
        }

        HarvestTargetSearchResult next = this.TargetPlanner.TryFindNext(
            contract.Farm,
            contract.Lease.Worker,
            contract.Lease.Worker.TilePoint,
            contract.Plan.ArrivalTile,
            contract.CompletedTargets,
            contract.FailedEdges);
        if (!next.IsSuccess || next.Target is null)
        {
            if (next.Failure == HarvestPlanFailure.NoReachableCrop)
            {
                contract.UnreachableTargets += next.CandidateTargetCount;
                this.Monitor.Log(
                    $"Harvest routing found {next.CandidateTargetCount} mature crop(s) but no safe interaction path "
                    + $"from {contract.Lease.Worker.TilePoint}; completed={contract.CompletedTargets.Count}, "
                    + $"failedEdges={contract.FailedEdges.Count}, entrance={contract.Plan.ArrivalTile}. "
                    + "Remaining crops are isolated by live collision, raised-seed trellises, or previously failed edges.",
                    LogLevel.Warn);
            }
            this.BeginReturn(contract, depositOverflowOnReturn: false);
            return;
        }

        try
        {
            contract.CurrentTarget = next.Target;
            contract.ActionApplied = false;
            contract.Phase = HarvestContractPhase.TravelingToTarget;
            contract.PhaseTicks = 0;
            if (contract.Lease.Worker.TilePoint == next.Target.InteractionTile)
            {
                this.OnArrivedAtTarget(contract.Lease.Worker, contract.Farm);
                return;
            }

            if (!FarmNavigationMap.CanBeginPath(
                    contract.Farm,
                    contract.Lease.Worker,
                    contract.Lease.Worker.TilePoint,
                    next.Target.Path,
                    out string firstStepFailure))
            {
                this.HandleFailedTargetRoute(
                    contract,
                    $"first-step collision probe rejected the route: {firstStepFailure}");
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
                contract.Lease.Worker.Position.Y);
        }
        catch (Exception ex)
        {
            this.HandleFailedTargetRoute(
                contract,
                $"controller setup failed: {ex.Message}");
        }
    }

    private void BeginReturn(ActiveHarvestContract contract, bool depositOverflowOnReturn)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        contract.DepositOverflowOnReturn |= depositOverflowOnReturn;
        if (contract.Lease.Worker.TilePoint == contract.Plan.ArrivalTile)
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
                    new GridPoint(contract.Plan.ArrivalTile.X, contract.Plan.ArrivalTile.Y),
                    out IReadOnlyList<GridPoint> gridPath))
                throw new InvalidOperationException("No object-safe harvest return path to the farm entrance.");

            contract.CurrentChestRoute = null;
            contract.Phase = HarvestContractPhase.Returning;
            contract.PhaseTicks = 0;
            PathFindController returning = this.CreatePathController(
                contract,
                FarmNavigationMap.ToPath(gridPath),
                contract.Plan.ArrivalTile,
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
            contract.Farm,
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

        return controller;
    }

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

        this.ContinueFinalization(contract, mustFinalizeNow);
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
            Array.Empty<NamedContractTransferState>());

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

    private void StopForUnavailableStorage(ActiveHarvestContract contract, string detail)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        if (!contract.StorageUnavailable)
        {
            contract.StorageUnavailable = true;
            contract.RemainingTargets = HarvestTargetPlanner.CountRemainingMatureCrops(
                contract.Farm,
                contract.CompletedTargets);
            this.Monitor.Log(
                $"Stopping harvest contract {contract.Id:N}: {detail}. "
                + $"Remaining mature crops={contract.RemainingTargets}; existing cargo will be preserved "
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
            new GridPoint(contract.Plan.ArrivalTile.X, contract.Plan.ArrivalTile.Y),
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
        contract.Farm.temporarySprites.Add(new TemporaryAnimatedSprite(
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

    private string GetPlanFailureTranslationKey(HarvestPlanFailure failure)
    {
        return failure switch
        {
            HarvestPlanFailure.UnsupportedFarmMap => "contract.start.unsupported-map",
            HarvestPlanFailure.NoSafeArrivalTile => "contract.start.no-arrival",
            HarvestPlanFailure.NoMatureCrop => "harvest.start.no-mature-crop",
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
        WaitingForOverflowLock,
        QuarantiningCargo,
        Returning,
        Returned,
        RecoveringLease
    }

    private sealed class ActiveHarvestContract
    {
        private readonly Dictionary<string, HashSet<Point>> AttemptedChestTiles = new(StringComparer.Ordinal);

        public ActiveHarvestContract(
            Guid id,
            string requestId,
            Farmer requester,
            NpcWorkLease lease,
            WorkContractPreview preview,
            Farm farm,
            HarvestWorkPlan plan)
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
            this.Plan = plan;
            this.CurrentTarget = plan.FirstTarget;
        }

        public Guid Id { get; }
        public string RequestId { get; }
        public Farmer Requester { get; }
        public NpcWorkLease Lease { get; }
        public WorkContractPreview Preview { get; }
        public int ActionDurationTicks { get; }
        public Farm Farm { get; }
        public HarvestWorkPlan Plan { get; set; }
        public HarvestTargetPlan CurrentTarget { get; set; }
        public HashSet<Point> CompletedTargets { get; } = new();
        public HashSet<FarmTaskRouteEdge> FailedEdges { get; } = new();
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

        public HashSet<Point> GetAttemptedChests(string transferId)
        {
            if (!this.AttemptedChestTiles.TryGetValue(transferId, out HashSet<Point>? attempted))
            {
                attempted = new HashSet<Point>();
                this.AttemptedChestTiles[transferId] = attempted;
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
