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
            worker.Halt();
            worker.Sprite?.ClearAnimation();

            if (worker.TilePoint == planResult.Plan.FirstTarget.InteractionTile)
            {
                this.OnArrivedAtTarget(worker, mainFarm);
                contract.Dispatched = true;
                Game1.addHUDMessage(new HUDMessage(
                    this.Translation.Get("contract.hud.dispatched", new
                    {
                        worker = worker.displayName,
                        gold = preview.MaximumAuthorizedWage
                    }),
                    HUDMessage.newQuest_type));
                return true;
            }

            PathFindController outbound = this.CreatePathController(
                contract,
                planResult.Plan.FirstTarget.InteractionTile,
                planResult.Plan.FirstTarget.FacingDirection,
                this.OnArrivedAtTarget);
            contract.Controller = outbound;
            lease.AttachController(outbound);
            contract.Dispatched = true;

            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("contract.hud.dispatched", new
                {
                    worker = worker.displayName,
                    gold = preview.MaximumAuthorizedWage
                }),
                HUDMessage.newQuest_type));
            return true;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Failed to dispatch watering worker '{worker.Name}': {ex}", LogLevel.Error);
            this.FinishContract(contract, succeeded: false, "contract.failure.dispatch");
            return false;
        }
    }

    public void Update()
    {
        ActiveWateringContract? contract = this.ActiveContract;
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
            case WateringContractPhase.TravelingToTarget:
                if (contract.PhaseTicks > MaximumTravelTicks)
                {
                    this.FinishContract(contract, succeeded: false, "contract.failure.travel-timeout");
                    return;
                }

                if (contract.Controller is not null
                    && contract.Lease.Worker.controller is not null
                    && !ReferenceEquals(contract.Lease.Worker.controller, contract.Controller))
                {
                    this.FinishContract(contract, succeeded: false, "contract.failure.controller-conflict");
                    return;
                }

                if (contract.PhaseTicks > 1 && contract.Lease.Worker.controller is null)
                    this.FinishContract(contract, succeeded: false, "contract.failure.path-interrupted");
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
                }

                if (contract.PhaseTicks >= ActionDurationTicks)
                    this.BeginNextOrReturn(contract);
                break;

            case WateringContractPhase.Returning:
                if (contract.PhaseTicks > MaximumTravelTicks)
                {
                    this.FinishContract(contract, succeeded: false, "contract.failure.return-timeout");
                    return;
                }

                if (contract.Controller is not null
                    && contract.Lease.Worker.controller is not null
                    && !ReferenceEquals(contract.Lease.Worker.controller, contract.Controller))
                {
                    this.FinishContract(contract, succeeded: false, "contract.failure.controller-conflict");
                    return;
                }

                if (contract.PhaseTicks > 1 && contract.Lease.Worker.controller is null)
                    this.FinishContract(contract, succeeded: false, "contract.failure.return-interrupted");
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
            contract.Phase = WateringContractPhase.Returning;
            contract.PhaseTicks = 0;
            PathFindController returning = this.CreatePathController(
                contract,
                contract.Plan.ArrivalTile,
                finalFacingDirection: Game1.down,
                this.OnReturnedToArrival);
            contract.Controller = returning;
            contract.Lease.AttachController(returning);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Worker '{contract.Lease.Worker.Name}' could not start the return path: {ex.Message}", LogLevel.Warn);
            this.FinishContract(contract, succeeded: false, "contract.failure.return-path");
        }
    }

    private void BeginNextOrReturn(ActiveWateringContract contract)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        if (Game1.timeOfDay >= StopAcquiringTime)
        {
            contract.RemainingTargets = WateringTargetPlanner.CountRemainingDryCrops(
                contract.Farm,
                contract.AttemptedTargets);
            this.BeginReturn(contract);
            return;
        }

        WateringTargetSearchResult next = this.TargetPlanner.TryFindNext(
            contract.Farm,
            contract.Lease.Worker,
            contract.Lease.Worker.TilePoint,
            contract.Plan.ArrivalTile,
            contract.AttemptedTargets);

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
            contract.AttemptedTargets.Add(next.Target.TargetTile);
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
                next.Target.InteractionTile,
                next.Target.FacingDirection,
                this.OnArrivedAtTarget);
            contract.Controller = controller;
            contract.Lease.AttachController(controller);
        }
        catch (Exception ex)
        {
            contract.UnreachableTargets++;
            this.Monitor.Log(
                $"Worker '{contract.Lease.Worker.Name}' could not start the next watering path: {ex.Message}",
                LogLevel.Warn);
            this.BeginReturn(contract);
        }
    }

    private PathFindController CreatePathController(
        ActiveWateringContract contract,
        Point destination,
        int finalFacingDirection,
        PathFindController.endBehavior onArrived)
    {
        PathFindController controller = new(
            contract.Lease.Worker,
            contract.Farm,
            PathFindController.isAtEndPoint,
            finalFacingDirection,
            onArrived,
            10000,
            destination,
            clearMarriageDialogues: false)
        {
            nonDestructivePathing = true
        };

        if (controller.pathToEndPoint is not { Count: > 0 })
            throw new InvalidOperationException($"No path to {destination}.");

        return controller;
    }

    private void FinishContract(
        ActiveWateringContract contract,
        bool succeeded,
        string? failureTranslationKey)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

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
            NamedFarmTask.Watering,
            finalSucceeded,
            finalReasonKey,
            contract.WateredTargets,
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
                this.Translation.Get("contract.hud.restore-failed", new { worker = contract.Lease.Worker.displayName }),
                HUDMessage.error_type));
            return;
        }

        if (succeeded)
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

        string reason = failureTranslationKey is null
            ? this.Translation.Get("contract.failure.unknown")
            : this.Translation.Get(failureTranslationKey);
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

    private enum WateringContractPhase
    {
        TravelingToTarget,
        Acting,
        Returning,
        Returned
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
            this.AttemptedTargets.Add(plan.FirstTarget.TargetTile);
        }

        public Guid Id { get; }
        public string RequestId { get; }
        public Farmer Requester { get; }
        public NpcWorkLease Lease { get; }
        public WorkContractPreview Preview { get; }
        public Farm Farm { get; }
        public WateringWorkPlan Plan { get; }
        public HashSet<Point> AttemptedTargets { get; } = new();
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
    }
}
