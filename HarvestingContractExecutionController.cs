using Microsoft.Xna.Framework;
using StardewModdingAPI;
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
    private ActiveHarvestContract? ActiveContract;
    private NamedContractCompletionState? LastCompletion;

    public HarvestingContractExecutionController(
        ITranslationHelper translation,
        IMonitor monitor,
        WorkerRosterService workerRoster)
    {
        this.Translation = translation;
        this.Monitor = monitor;
        this.WorkerRoster = workerRoster;
        this.TargetPlanner = new HarvestTargetPlanner(monitor);
        this.ChestRouter = new HarvestChestRouter(monitor);
    }

    public bool HasActiveContract => this.ActiveContract is not null;

    public string? LastStartFailureKey { get; private set; }

    public string? ActiveContractId => this.ActiveContract?.Id.ToString("N");

    public bool TryStart(long requestingPlayerId, string workerInternalName, string requestId)
    {
        this.LastStartFailureKey = null;
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return this.FailStart("contract.start.host-only");

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
        WorkContractPreview preview = ContractPreviewService.Create(friendshipHearts, Game1.dayOfMonth);
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
            worker.Sprite?.ClearAnimation();
            this.Monitor.Log(
                $"Dispatching harvest worker '{worker.Name}' from visible farm-edge tile {planResult.Plan.ArrivalTile}.",
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
                        entrance = this.GetArrivalDescription()
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
                    entrance = this.GetArrivalDescription()
                }),
                HUDMessage.newQuest_type));
            return true;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Failed to dispatch harvest worker '{worker.Name}': {ex}", LogLevel.Error);
            this.FinishContract(contract, succeeded: false, "contract.failure.dispatch");
            return false;
        }
    }

    public void Update()
    {
        ActiveHarvestContract? contract = this.ActiveContract;
        if (contract is null || !Context.IsWorldReady)
            return;

        if (!Context.IsMainPlayer
            || Game1.Date.TotalDays != contract.Lease.StartTotalDays
            || Game1.timeOfDay >= HardStopTime)
        {
            this.FinishContract(contract, succeeded: false, "contract.failure.safety-stop");
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

                if (contract.PhaseTicks >= ActionDurationTicks)
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
                    this.BeginNextOrReturn(contract);
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
                    contract.HarvestedTargets > 0 && contract.Cargo.Count == 0,
                    contract.HarvestedTargets > 0
                        ? null
                        : "harvest.failure.target-invalidated");
                break;
        }
    }

    public void OnDayEnding()
    {
        if (this.ActiveContract is { } contract)
            this.FinishContract(contract, succeeded: false, "contract.failure.day-ending");
    }

    public void OnReturnedToTitle()
    {
        if (this.ActiveContract is { } contract && Context.IsWorldReady)
            this.FinishContract(contract, succeeded: false, "contract.failure.world-closed");

        this.ActiveContract = null;
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
            contract.Phase.ToString(),
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
            this.Monitor.Log(
                $"Harvest worker '{contract.Lease.Worker.Name}' stalled during {contract.Phase} at {contract.Lease.Worker.TilePoint}; replanning.",
                LogLevel.Warn);
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
                contract.FailedEdges.Add(WateringTargetPlanner.ToEdge(
                    contract.CurrentTarget.TargetTile,
                    contract.CurrentTarget.InteractionTile));
                this.BeginNextOrReturn(contract);
                break;

            case HarvestContractPhase.TravelingToChest:
                this.MarkCurrentChestAttempted(contract);
                this.BeginDeliveryOrReturn(contract);
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

        bool destroyAfterHarvest = !crop.RegrowsAfterHarvest();
        ContractHarvestCollector collector = new(contract.Farm, contract.Lease.Worker.Position);
        bool harvested = crop.harvest(
            contract.CurrentTarget.TargetTile.X,
            contract.CurrentTarget.TargetTile.Y,
            dirt,
            collector);
        if (!harvested || collector.Items.Count == 0)
            return false;

        if (destroyAfterHarvest)
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
            if (this.TryDeliverCargoToRequester(contract))
            {
                this.BeginDeliveryOrReturn(contract);
                return;
            }

            this.Monitor.Log(
                $"No reachable eligible chest can accept harvest cargo '{entry.Item.QualifiedItemId}' "
                + $"q{entry.Item.Quality} x{entry.Item.Stack}, and the on-farm requester inventory "
                + "could not accept it; using persistent overflow.",
                LogLevel.Debug);
            this.BeginOverflowDeposit(contract);
            return;
        }

        try
        {
            this.Monitor.Log(
                $"Routing harvest cargo '{entry.Item.QualifiedItemId}' q{entry.Item.Quality} x{entry.Item.Stack} "
                + $"to chest {route.ChestTile} (match={route.MatchKind}, capacity={route.AcceptableCapacity}).",
                LogLevel.Debug);
            contract.CurrentChestRoute = route;
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
            attempted.Add(route.ChestTile);
            this.Monitor.Log(
                $"Worker '{contract.Lease.Worker.Name}' could not reach harvest chest at {route.ChestTile}: {ex.Message}",
                LogLevel.Warn);
            this.BeginDeliveryOrReturn(contract);
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

        this.BeginDeliveryOrReturn(contract);
    }

    private bool TryDeliverCargoToRequester(ActiveHarvestContract contract)
    {
        if (contract.Cargo.Count == 0)
            return false;

        Farmer? requester = Game1.GetPlayer(contract.Requester.UniqueMultiplayerID, onlyOnline: true);
        HarvestCargoEntry entry = contract.Cargo[0];
        if (requester is null
            || !ReferenceEquals(requester, contract.Requester)
            || !ReferenceEquals(requester.currentLocation, contract.Farm)
            || !requester.couldInventoryAcceptThisItem(entry.Item))
            return false;

        int requested = entry.Item.Stack;
        string qualifiedItemId = entry.Item.QualifiedItemId;
        int quality = entry.Item.Quality;
        Item? remainder = entry.Item;
        bool applied = contract.TransferLedger.TryApply(
            entry.TransferId,
            () => remainder = requester.addItemToInventory(entry.Item));
        if (!applied)
        {
            contract.Cargo.RemoveAt(0);
            return true;
        }

        int remaining = remainder?.Stack ?? 0;
        int delivered = HarvestTransferMath.GetDeliveredCount(requested, remaining);
        contract.PlayerInventoryItems += delivered;
        this.Monitor.Log(
            $"Gave harvest cargo '{qualifiedItemId}' q{quality} x{delivered} directly to on-farm "
            + $"requester '{requester.Name}'; remainder={remaining}.",
            LogLevel.Debug);

        if (remainder is null)
        {
            contract.Cargo.RemoveAt(0);
        }
        else
        {
            entry.Item = remainder;
            entry.TransferId = Guid.NewGuid().ToString("N");
        }

        return delivered > 0;
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
            contract.FailedEdges.Add(WateringTargetPlanner.ToEdge(
                next.Target.TargetTile,
                next.Target.InteractionTile));
            this.Monitor.Log(
                $"Worker '{contract.Lease.Worker.Name}' could not start the next harvest path: {ex.Message}",
                LogLevel.Warn);
            this.BeginNextOrReturn(contract);
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
        string? failureTranslationKey)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        this.ReleaseCurrentChestLock(contract);
        if (contract.Cargo.Count > 0)
            this.PersistOrDropCargo(contract);

        int harvestedItems = contract.HarvestedItems.Sum(item => item.Stack);
        int unresolvedItems = contract.Cargo.Sum(entry => entry.Item.Stack);
        bool placementBalanced = HarvestPlacementAudit.IsBalanced(
            harvestedItems,
            contract.PlayerInventoryItems,
            contract.ChestDeliveredItems,
            contract.OverflowItems,
            contract.DroppedItems,
            unresolvedItems);
        this.Monitor.Log(
            $"Harvest placement audit for contract {contract.Id:N}: harvested={harvestedItems}, "
            + $"player={contract.PlayerInventoryItems}, chest={contract.ChestDeliveredItems}, "
            + $"overflow={contract.OverflowItems}, "
            + $"dropped={contract.DroppedItems}, unresolved={unresolvedItems}, balanced={placementBalanced}.",
            placementBalanced && unresolvedItems == 0 ? LogLevel.Debug : LogLevel.Error);
        if (!placementBalanced || unresolvedItems > 0)
        {
            succeeded = false;
            failureTranslationKey = "harvest.failure.placement-audit";
        }

        NpcLeaseRestoreResult restoreResult = contract.Lease.Restore();
        WateringContractSettlement settlement = WateringContractSettlement.Create(
            contract.Preview,
            contract.Dispatched,
            contract.Lease.StartTime,
            Game1.timeOfDay);
        contract.Requester.Money += settlement.RefundedGold;
        this.ActiveContract = null;

        bool finalSucceeded = succeeded && restoreResult == NpcLeaseRestoreResult.Restored;
        string finalReasonKey = finalSucceeded
            ? ""
            : restoreResult != NpcLeaseRestoreResult.Restored
                ? "contract.hud.restore-failed"
                : failureTranslationKey ?? "contract.failure.unknown";
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
            contract.TransferLedger.GetCompletedTransferIds());

        if (restoreResult != NpcLeaseRestoreResult.Restored)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("contract.hud.restore-failed", new { worker = contract.Lease.Worker.displayName }),
                HUDMessage.error_type));
            return;
        }

        string items = FormatHarvestedItems(contract.HarvestedItems);
        if (succeeded)
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
                    dropped = contract.DroppedItems,
                    hours = settlement.BillableHours,
                    paid = settlement.ChargedGold,
                    refunded = settlement.RefundedGold
                }),
                HUDMessage.newQuest_type));
            return;
        }

        string reason = failureTranslationKey is null
            ? this.Translation.Get("contract.failure.unknown")
            : this.Translation.Get(failureTranslationKey);
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
                dropped = contract.DroppedItems,
                hours = settlement.BillableHours,
                paid = settlement.ChargedGold,
                refunded = settlement.RefundedGold
            }),
            HUDMessage.error_type));
    }

    private void PersistOrDropCargo(ActiveHarvestContract contract)
    {
        if (contract.Cargo.Count == 0)
            return;

        NetMutex mutex = Game1.player.team.GetOrCreateGlobalInventoryMutex(OverflowInventoryId);
        try
        {
            if (!this.TryAcquireOverflowLockImmediately(mutex))
            {
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
            this.BeginNextOrReturn(contract);
        }
        catch (Exception ex)
        {
            this.DropCargoVisibly(contract, $"persistent harvest overflow failed after locking: {ex}");
            this.BeginNextOrReturn(contract);
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
        this.Monitor.Log($"{reason}; dropping exact harvest cargo visibly.", LogLevel.Error);
        foreach (HarvestCargoEntry entry in contract.Cargo.ToArray())
        {
            int stack = entry.Item.Stack;
            try
            {
                Game1.createItemDebris(entry.Item, contract.Lease.Worker.Position, -1, contract.Farm);
                contract.DroppedItems += stack;
                contract.Cargo.Remove(entry);
                this.Monitor.Log(
                    $"Dropped harvest cargo '{entry.Item.QualifiedItemId}' q{entry.Item.Quality} x{stack} "
                    + $"visibly at {contract.Lease.Worker.TilePoint}.",
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
            this.Translation.Get("harvest.hud.emergency-drop"),
            HUDMessage.error_type));
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

    private string GetArrivalDescription()
    {
        return this.Translation.Get("contract.entrance.fixed-main");
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
        Returning,
        Returned
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
            this.Farm = farm;
            this.Plan = plan;
            this.CurrentTarget = plan.FirstTarget;
        }

        public Guid Id { get; }
        public string RequestId { get; }
        public Farmer Requester { get; }
        public NpcWorkLease Lease { get; }
        public WorkContractPreview Preview { get; }
        public Farm Farm { get; }
        public HarvestWorkPlan Plan { get; }
        public HarvestTargetPlan CurrentTarget { get; set; }
        public HashSet<Point> CompletedTargets { get; } = new();
        public HashSet<FarmTaskRouteEdge> FailedEdges { get; } = new();
        public TravelProgressWatchdog TravelWatchdog { get; } = new();
        public HarvestTransferLedger TransferLedger { get; } = new();
        public List<HarvestCargoEntry> Cargo { get; } = new();
        public List<HarvestItemSnapshot> HarvestedItems { get; } = new();
        public HarvestContractPhase Phase { get; set; } = HarvestContractPhase.TravelingToTarget;
        public PathFindController? Controller { get; set; }
        public HarvestChestRoute? CurrentChestRoute { get; set; }
        public int PhaseTicks { get; set; }
        public bool Dispatched { get; set; }
        public bool ActionApplied { get; set; }
        public bool DepositOverflowOnReturn { get; set; }
        public bool OverflowLockRequested { get; set; }
        public int HarvestedTargets { get; set; }
        public int SkippedTargets { get; set; }
        public int UnreachableTargets { get; set; }
        public int RemainingTargets { get; set; }
        public int PlayerInventoryItems { get; set; }
        public int ChestDeliveredItems { get; set; }
        public int OverflowItems { get; set; }
        public int DroppedItems { get; set; }
        public int ReturnReplanAttempts { get; set; }

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
}
