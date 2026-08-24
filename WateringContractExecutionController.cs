using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace EvilFarmOwner;

internal sealed class WateringContractExecutionController
{
    private const int LatestStartTime = 1600;
    private const int StopAcquiringTime = 2100;
    private const int HardStopTime = 2200;
    private const int ActionStartTicks = 8;
    private const int ActionDurationTicks = 36;
    private const int MaximumTravelTicks = 3600;
    private const int MaximumStalledTravelTicks = 180;
    private const int MaximumReturnReplans = 3;

    private readonly ITranslationHelper Translation;
    private readonly IMonitor Monitor;
    private readonly WorkerRosterService WorkerRoster;
    private readonly WateringTargetPlanner TargetPlanner;
    private ActiveWateringContract? ActiveContract;
    private NamedContractCompletionState? LastCompletion;

    public WateringContractExecutionController(
        ITranslationHelper translation,
        IMonitor monitor,
        WorkerRosterService workerRoster)
    {
        this.Translation = translation;
        this.Monitor = monitor;
        this.WorkerRoster = workerRoster;
        this.TargetPlanner = new WateringTargetPlanner(monitor);
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
            return this.FailStart("contract.start.too-late");

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

        WateringPlanResult planResult = this.TargetPlanner.TryCreate(mainFarm, worker);
        if (!planResult.IsSuccess || planResult.Plan is null)
            return this.FailStart(this.GetPlanFailureTranslationKey(planResult.Failure));

        if (!NpcWorkLease.TryAcquire(
                worker,
                preview.MaximumAuthorizedWage,
                this.Monitor,
                out NpcWorkLease? lease)
            || lease is null)
            return this.FailStart("contract.start.lease-failed");

        ActiveWateringContract contract = new(
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
                $"Dispatching watering worker '{worker.Name}' from {planResult.Plan.ArrivalSide} "
                + $"farm-boundary tile {planResult.Plan.ArrivalTile}.",
                LogLevel.Debug);

            if (worker.TilePoint == planResult.Plan.FirstTarget.InteractionTile)
            {
                this.OnArrivedAtTarget(worker, mainFarm);
                contract.Dispatched = true;
                Game1.addHUDMessage(new HUDMessage(
                    this.Translation.Get("contract.hud.dispatched", new
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
                this.Translation.Get("contract.hud.dispatched", new
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
            this.Monitor.Log($"Failed to dispatch watering worker '{worker.Name}': {ex}", LogLevel.Error);
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
        ActiveWateringContract? contract = this.ActiveContract;
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
            case WateringContractPhase.TravelingToTarget:
                if (this.TryCompleteTravelAtDestination(
                        contract,
                        contract.CurrentTarget.InteractionTile,
                        this.OnArrivedAtTarget))
                    return;

                if (contract.PhaseTicks > MaximumTravelTicks)
                {
                    this.HandleInterruptedTargetTravel(contract);
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
                        $"Watering worker '{contract.Lease.Worker.Name}' stalled at {contract.Lease.Worker.TilePoint}; replanning the target route.",
                        LogLevel.Warn);
                    this.HandleInterruptedTargetTravel(contract);
                    return;
                }

                if (contract.PhaseTicks > 1 && contract.Lease.Worker.controller is null)
                    this.HandleInterruptedTargetTravel(contract);
                break;

            case WateringContractPhase.Acting:
                if (!contract.ActionApplied && contract.PhaseTicks >= ActionStartTicks)
                {
                    if (!this.TryApplyWatering(contract))
                    {
                        contract.SkippedTargets++;
                        contract.ActionApplied = true;
                    }
                    else
                    {
                        contract.ActionApplied = true;
                        contract.WateredTargets++;
                    }
                    contract.CompletedTargets.Add(contract.CurrentTarget.TargetTile);
                }

                if (contract.PhaseTicks >= ActionDurationTicks)
                    this.BeginNextOrReturn(contract);
                break;

            case WateringContractPhase.Returning:
                if (this.TryCompleteTravelAtDestination(
                        contract,
                        contract.Plan.ArrivalTile,
                        this.OnReturnedToArrival))
                    return;

                if (contract.PhaseTicks > MaximumTravelTicks)
                {
                    this.HandleInterruptedReturnTravel(contract);
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
                        $"Watering worker '{contract.Lease.Worker.Name}' stalled while returning from {contract.Lease.Worker.TilePoint}; replanning the return route.",
                        LogLevel.Warn);
                    this.HandleInterruptedReturnTravel(contract);
                    return;
                }

                if (contract.PhaseTicks > 1 && contract.Lease.Worker.controller is null)
                    this.HandleInterruptedReturnTravel(contract);
                break;

            case WateringContractPhase.Returned:
                this.FinishContract(
                    contract,
                    contract.WateredTargets > 0,
                    contract.WateredTargets > 0
                        ? null
                        : "contract.failure.target-invalidated");
                break;
        }
    }

    public void OnDayEnding()
    {
        if (this.ActiveContract is { } contract)
        {
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
            this.FinishContract(
                contract,
                succeeded: false,
                "contract.failure.world-closed",
                mustFinalizeNow: true);
        }

        this.ActiveContract = null;
    }

    public NamedContractRuntimeState? GetRuntimeState()
    {
        ActiveWateringContract? contract = this.ActiveContract;
        if (contract is null)
            return null;

        return new NamedContractRuntimeState(
            contract.Id.ToString("N"),
            contract.RequestId,
            contract.Requester.UniqueMultiplayerID,
            contract.Lease.Worker.Name,
            NamedFarmTask.Watering,
            contract.Phase.ToString(),
            contract.Plan.ArrivalTile.X,
            contract.Plan.ArrivalTile.Y,
            contract.Plan.ArrivalSide,
            contract.EntranceSwitches,
            contract.CurrentTarget.TargetTile.X,
            contract.CurrentTarget.TargetTile.Y,
            contract.Preview.MaximumAuthorizedWage,
            contract.Lease.StartTime,
            contract.WateredTargets,
            Array.Empty<NamedContractCargoState>(),
            Array.Empty<string>());
    }

    public NamedContractCompletionState? ConsumeCompletion()
    {
        NamedContractCompletionState? completion = this.LastCompletion;
        this.LastCompletion = null;
        return completion;
    }

    private void OnArrivedAtTarget(Character character, GameLocation location)
    {
        ActiveWateringContract? contract = this.ActiveContract;
        if (contract is null
            || !ReferenceEquals(character, contract.Lease.Worker)
            || !ReferenceEquals(location, contract.Farm)
            || contract.Phase != WateringContractPhase.TravelingToTarget)
            return;

        contract.Phase = WateringContractPhase.Acting;
        contract.PhaseTicks = 0;
        contract.Lease.Worker.Halt();
        contract.Lease.Worker.faceDirection(contract.CurrentTarget.FacingDirection);
    }

    private void OnReturnedToArrival(Character character, GameLocation location)
    {
        ActiveWateringContract? contract = this.ActiveContract;
        if (contract is null
            || !ReferenceEquals(character, contract.Lease.Worker)
            || !ReferenceEquals(location, contract.Farm)
            || contract.Phase != WateringContractPhase.Returning)
            return;

        contract.Phase = WateringContractPhase.Returned;
        contract.PhaseTicks = 0;
        contract.Lease.Worker.Halt();
    }

    private bool TryCompleteTravelAtDestination(
        ActiveWateringContract contract,
        Point destination,
        PathFindController.endBehavior onArrived)
    {
        NPC worker = contract.Lease.Worker;
        if (!ReferenceEquals(worker.currentLocation, contract.Farm)
            || worker.TilePoint != destination)
            return false;

        if (worker.controller is not null && !ReferenceEquals(worker.controller, contract.Controller))
        {
            this.FinishContract(contract, succeeded: false, "contract.failure.controller-conflict");
            return true;
        }

        if (ReferenceEquals(worker.controller, contract.Controller))
            worker.controller = null;
        contract.Controller = null;
        worker.Position = FarmNavigationMap.GetAlignedCharacterPosition(destination);
        worker.Halt();
        this.Monitor.Log(
            $"Watering worker '{worker.Name}' entered destination tile {destination}; completing travel before vanilla pixel centering.",
            LogLevel.Debug);
        onArrived(worker, contract.Farm);
        return true;
    }

    private bool TryApplyWatering(ActiveWateringContract contract)
    {
        Vector2 targetTile = new(contract.CurrentTarget.TargetTile.X, contract.CurrentTarget.TargetTile.Y);
        if (!WateringTargetPlanner.IsDryCrop(contract.Farm, targetTile)
            || contract.Lease.Worker.currentLocation != contract.Farm
            || contract.Lease.Worker.TilePoint != contract.CurrentTarget.InteractionTile)
            return false;

        if (!contract.Farm.terrainFeatures.TryGetValue(targetTile, out TerrainFeature? feature)
            || feature is not HoeDirt dirt)
            return false;

        contract.Lease.Worker.faceDirection(contract.CurrentTarget.FacingDirection);
        dirt.state.Value = HoeDirt.watered;
        contract.Farm.playSound("wateringCan", targetTile);

        TemporaryAnimatedSprite splash = new(
            13,
            targetTile * Game1.tileSize,
            Color.White,
            10,
            Game1.random.Next(2) == 0,
            70f,
            0,
            64,
            (targetTile.Y * Game1.tileSize + 32f) / 10000f - 0.01f)
        {
            delayBeforeAnimationStart = 100
        };

        TemporaryAnimatedSprite tool = this.CreateWateringCanSprite(contract.Lease.Worker);
        contract.Farm.temporarySprites.Add(splash);
        contract.Farm.temporarySprites.Add(tool);
        return true;
    }

    private TemporaryAnimatedSprite CreateWateringCanSprite(NPC worker)
    {
        WateringCan wateringCan = new();
        int tileIndex = wateringCan.CurrentParentTileIndex;
        Rectangle source = new(
            tileIndex * 16 % Game1.toolSpriteSheet.Width,
            tileIndex * 16 / Game1.toolSpriteSheet.Width * 16,
            16,
            32);

        Vector2 position = worker.Position + worker.FacingDirection switch
        {
            Game1.up => new Vector2(12f, -80f),
            Game1.right => new Vector2(46f, -52f),
            Game1.down => new Vector2(24f, -10f),
            _ => new Vector2(-18f, -52f)
        };

        float rotation = worker.FacingDirection switch
        {
            Game1.right => MathF.PI / 5f,
            Game1.down => MathF.PI / 3f,
            Game1.left => -MathF.PI / 5f,
            _ => 0f
        };

        return new TemporaryAnimatedSprite(
            Game1.toolSpriteSheetName,
            source,
            600f,
            1,
            0,
            position,
            flicker: false,
            flipped: worker.FacingDirection == Game1.left,
            layerDepth: (worker.GetBoundingBox().Bottom + 1f) / 10000f,
            alphaFade: 0.01f,
            color: Color.White,
            scale: 4f,
            scaleChange: 0f,
            rotation,
            rotationChange: 0f);
    }

    private void BeginReturn(ActiveWateringContract contract)
    {
        if (this.ActiveContract != contract)
            return;

        if (contract.Lease.Worker.TilePoint == contract.Plan.ArrivalTile)
        {
            contract.Phase = WateringContractPhase.Returned;
            contract.PhaseTicks = 0;
            contract.Lease.Worker.Halt();
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
                throw new InvalidOperationException("No object-safe return path to the farm entrance.");

            contract.Phase = WateringContractPhase.Returning;
            contract.PhaseTicks = 0;
            PathFindController returning = this.CreatePathController(
                contract,
                FarmNavigationMap.ToPath(gridPath),
                contract.Plan.ArrivalTile,
                finalFacingDirection: Game1.down,
                this.OnReturnedToArrival);
            contract.Controller = returning;
            contract.Lease.AttachController(returning);
            contract.TravelWatchdog.Reset(
                contract.Lease.Worker.Position.X,
                contract.Lease.Worker.Position.Y);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Worker '{contract.Lease.Worker.Name}' could not start the return path: {ex.Message}", LogLevel.Warn);
            this.FinishContract(contract, succeeded: false, "contract.failure.return-path");
        }
    }

    private void HandleInterruptedTargetTravel(ActiveWateringContract contract)
    {
        if (contract.Lease.Worker.controller is not null
            && !ReferenceEquals(contract.Lease.Worker.controller, contract.Controller))
        {
            this.FinishContract(contract, succeeded: false, "contract.failure.controller-conflict");
            return;
        }

        if (ReferenceEquals(contract.Lease.Worker.controller, contract.Controller))
            contract.Lease.Worker.controller = null;
        contract.Lease.Worker.Halt();
        if (this.TryHandleStalledEntrance(contract))
            return;

        contract.FailedEdges.Add(WateringTargetPlanner.ToEdge(
            contract.CurrentTarget.TargetTile,
            contract.CurrentTarget.InteractionTile));
        this.BeginNextOrReturn(contract);
    }

    private bool TryHandleStalledEntrance(ActiveWateringContract contract)
    {
        NPC worker = contract.Lease.Worker;
        if (contract.CompletedTargets.Count > 0
            || !ReferenceEquals(worker.currentLocation, contract.Farm)
            || worker.TilePoint != contract.Plan.ArrivalTile)
            return false;

        FarmBoundarySide failedSide = contract.Plan.ArrivalSide;
        contract.FailedArrivalSides.Add(failedSide);
        contract.Controller = null;
        this.Monitor.Log(
            $"Watering worker '{worker.Name}' could not leave the {failedSide} entrance at "
            + $"{contract.Plan.ArrivalTile}; excluding that side and planning a boundary fallback.",
            LogLevel.Warn);

        WateringPlanResult replacement = this.TargetPlanner.TryCreate(
            contract.Farm,
            worker,
            contract.FailedArrivalSides);
        if (!replacement.IsSuccess || replacement.Plan is null)
        {
            this.Monitor.Log(
                $"No remaining farm-boundary entrance can start watering after excluding: "
                + $"{string.Join(", ", contract.FailedArrivalSides.OrderBy(FarmEntranceSelection.GetEntrancePriority))}.",
                LogLevel.Warn);
            this.FinishContract(
                contract,
                succeeded: false,
                replacement.Failure == WateringPlanFailure.NoDryCrop
                    ? "contract.failure.target-invalidated"
                    : "contract.failure.entrance-stalled");
            return true;
        }

        try
        {
            WateringWorkPlan nextPlan = replacement.Plan;
            contract.Plan = nextPlan;
            contract.CurrentTarget = nextPlan.FirstTarget;
            contract.ActionApplied = false;
            contract.Phase = WateringContractPhase.TravelingToTarget;
            contract.PhaseTicks = 0;
            contract.ReturnReplanAttempts = 0;
            contract.FailedEdges.Clear();
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
            worker.Sprite?.ClearAnimation();
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
                $"Watering contract switched from the failed {failedSide} entrance to "
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
                $"Failed to switch watering worker '{worker.Name}' to a fallback entrance: {ex}",
                LogLevel.Error);
            this.FinishContract(contract, succeeded: false, "contract.failure.entrance-stalled");
        }

        return true;
    }

    private void HandleInterruptedReturnTravel(ActiveWateringContract contract)
    {
        if (contract.Lease.Worker.controller is not null
            && !ReferenceEquals(contract.Lease.Worker.controller, contract.Controller))
        {
            this.FinishContract(contract, succeeded: false, "contract.failure.controller-conflict");
            return;
        }

        if (ReferenceEquals(contract.Lease.Worker.controller, contract.Controller))
            contract.Lease.Worker.controller = null;
        contract.Lease.Worker.Halt();

        contract.ReturnReplanAttempts++;
        if (contract.ReturnReplanAttempts > MaximumReturnReplans)
        {
            this.FinishContract(contract, succeeded: false, "contract.failure.return-interrupted");
            return;
        }

        this.BeginReturn(contract);
    }

    private void BeginNextOrReturn(ActiveWateringContract contract)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        if (Game1.timeOfDay >= StopAcquiringTime)
        {
            contract.RemainingTargets = WateringTargetPlanner.CountRemainingDryCrops(
                contract.Farm,
                contract.CompletedTargets);
            this.BeginReturn(contract);
            return;
        }

        WateringTargetSearchResult next = this.TargetPlanner.TryFindNext(
            contract.Farm,
            contract.Lease.Worker,
            contract.Lease.Worker.TilePoint,
            contract.Plan.ArrivalTile,
            contract.CompletedTargets,
            contract.FailedEdges);

        if (!next.IsSuccess || next.Target is null)
        {
            if (next.Failure == WateringPlanFailure.NoReachableCrop)
                contract.UnreachableTargets += next.CandidateTargetCount;

            this.BeginReturn(contract);
            return;
        }

        try
        {
            contract.CurrentTarget = next.Target;
            contract.ActionApplied = false;
            contract.Phase = WateringContractPhase.TravelingToTarget;
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
                $"Worker '{contract.Lease.Worker.Name}' could not start the next watering path: {ex.Message}",
                LogLevel.Warn);
            this.BeginNextOrReturn(contract);
        }
    }

    private PathFindController CreatePathController(
        ActiveWateringContract contract,
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
        ActiveWateringContract contract,
        bool succeeded,
        string? failureTranslationKey,
        bool mustFinalizeNow = false)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        if (!contract.FinalizationPrepared)
        {
            contract.FinalizationPrepared = true;
            contract.PendingSucceeded = succeeded;
            contract.PendingFailureTranslationKey = failureTranslationKey;
            contract.Phase = WateringContractPhase.RecoveringLease;
            contract.PhaseTicks = 0;
        }

        this.ContinueFinalization(contract, mustFinalizeNow);
    }

    private void ContinueFinalization(ActiveWateringContract contract, bool mustFinalizeNow)
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
                    $"Watering contract {contract.Id:N} is waiting for a conflicting controller to release "
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
            NamedFarmTask.Watering,
            finalSucceeded,
            finalReasonKey,
            contract.WateredTargets,
            PlayerItems: 0,
            ChestItems: 0,
            OverflowItems: 0,
            DroppedItems: 0,
            settlement.BillableHours,
            settlement.ChargedGold,
            settlement.RefundedGold,
            Array.Empty<NamedContractCargoState>(),
            Array.Empty<string>());

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

        if (contract.PendingSucceeded)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("contract.hud.completed", new
                {
                    worker = contract.Lease.Worker.displayName,
                    watered = contract.WateredTargets,
                    skipped = contract.SkippedTargets,
                    unreachable = contract.UnreachableTargets,
                    remaining = contract.RemainingTargets,
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
            this.Translation.Get("contract.hud.stopped", new
            {
                worker = contract.Lease.Worker.displayName,
                reason,
                watered = contract.WateredTargets,
                skipped = contract.SkippedTargets,
                unreachable = contract.UnreachableTargets,
                remaining = contract.RemainingTargets,
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

    private bool FailStart(string translationKey)
    {
        this.LastStartFailureKey = translationKey;
        Game1.addHUDMessage(new HUDMessage(this.Translation.Get(translationKey), HUDMessage.error_type));
        return false;
    }

    private string GetPlanFailureTranslationKey(WateringPlanFailure failure)
    {
        return failure switch
        {
            WateringPlanFailure.UnsupportedFarmMap => "contract.start.unsupported-map",
            WateringPlanFailure.NoSafeArrivalTile => "contract.start.no-arrival",
            WateringPlanFailure.NoDryCrop => "contract.start.no-dry-crop",
            _ => "contract.start.no-reachable-crop"
        };
    }

    private string GetArrivalDescription(FarmBoundarySide side)
    {
        return this.Translation.Get($"contract.entrance.{side.ToString().ToLowerInvariant()}");
    }

    private enum WateringContractPhase
    {
        TravelingToTarget,
        Acting,
        Returning,
        Returned,
        RecoveringLease
    }

    private sealed class ActiveWateringContract
    {
        public ActiveWateringContract(
            Guid id,
            string requestId,
            Farmer requester,
            NpcWorkLease lease,
            WorkContractPreview preview,
            Farm farm,
            WateringWorkPlan plan)
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
        public WateringWorkPlan Plan { get; set; }
        public HashSet<Point> CompletedTargets { get; } = new();
        public HashSet<FarmTaskRouteEdge> FailedEdges { get; } = new();
        public HashSet<FarmBoundarySide> FailedArrivalSides { get; } = new();
        public TravelProgressWatchdog TravelWatchdog { get; } = new();
        public WateringTargetPlan CurrentTarget { get; set; }
        public WateringContractPhase Phase { get; set; } = WateringContractPhase.TravelingToTarget;
        public PathFindController? Controller { get; set; }
        public int PhaseTicks { get; set; }
        public bool Dispatched { get; set; }
        public bool ActionApplied { get; set; }
        public int WateredTargets { get; set; }
        public int SkippedTargets { get; set; }
        public int UnreachableTargets { get; set; }
        public int RemainingTargets { get; set; }
        public int ReturnReplanAttempts { get; set; }
        public int EntranceSwitches { get; set; }
        public bool FinalizationPrepared { get; set; }
        public bool PendingSucceeded { get; set; }
        public string? PendingFailureTranslationKey { get; set; }
        public int RestoreWaitTicks { get; set; }
        public bool RestoreWaitNoticeShown { get; set; }
    }
}
