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
    private const int HardStopTime = 2200;
    private const int ActionStartTicks = 8;
    private const int ActionDurationTicks = 40;
    private const int MaximumTravelTicks = 3600;
    private const int MaximumLockWaitTicks = 300;

    private readonly ITranslationHelper Translation;
    private readonly IMonitor Monitor;
    private readonly WorkerRosterService WorkerRoster;
    private readonly HarvestTargetPlanner TargetPlanner;
    private readonly HarvestChestRouter ChestRouter;
    private ActiveHarvestContract? ActiveContract;

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

    public bool TryStart(string workerInternalName)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return this.FailStart("contract.start.host-only");

        if (Context.IsMultiplayer)
            return this.FailStart("contract.start.multiplayer-deferred");

        if (this.ActiveContract is not null)
            return this.FailStart("contract.start.already-active");

        if (Game1.timeOfDay > LatestStartTime)
            return this.FailStart("harvest.start.too-late");

        Farm mainFarm = Game1.getFarm();
        if (Game1.currentLocation is not Farm currentFarm || !ReferenceEquals(mainFarm, currentFarm))
            return this.FailStart("contract.start.must-be-on-farm");

        if (!this.WorkerRoster.TryGetWorker(workerInternalName, out NPC? worker, out WorkerAvailabilityResult availability)
            || worker is null)
            return this.FailStart("contract.start.worker-missing");

        if (availability.State != WorkerAvailabilityState.EligibleForPreview)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("contract.start.worker-unavailable", new { worker = worker.displayName }),
                HUDMessage.error_type));
            return false;
        }

        int friendshipHearts = Game1.player.getFriendshipHeartLevelForNPC(worker.Name);
        WorkContractPreview preview = ContractPreviewService.Create(friendshipHearts, Game1.dayOfMonth);
        if (Game1.player.Money < preview.MaximumAuthorizedWage)
        {
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
            lease,
            preview,
            mainFarm,
            planResult.Plan);
        this.ActiveContract = contract;
        Game1.player.Money -= preview.MaximumAuthorizedWage;

        try
        {
            Game1.warpCharacter(worker, mainFarm, new Vector2(
                planResult.Plan.ArrivalTile.X,
                planResult.Plan.ArrivalTile.Y));
            worker.Halt();
            worker.Sprite?.ClearAnimation();
            contract.Dispatched = true;

            if (worker.TilePoint == planResult.Plan.Target.InteractionTile)
            {
                this.OnArrivedAtTarget(worker, mainFarm);
                Game1.addHUDMessage(new HUDMessage(
                    this.Translation.Get("harvest.hud.dispatched", new
                    {
                        worker = worker.displayName,
                        gold = preview.MaximumAuthorizedWage
                    }),
                    HUDMessage.newQuest_type));
                return true;
            }

            PathFindController outbound = this.CreatePathController(
                contract,
                planResult.Plan.Target.InteractionTile,
                planResult.Plan.Target.FacingDirection,
                this.OnArrivedAtTarget);
            contract.Controller = outbound;
            lease.AttachController(outbound);

            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("harvest.hud.dispatched", new
                {
                    worker = worker.displayName,
                    gold = preview.MaximumAuthorizedWage
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
            || Context.IsMultiplayer
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
                        contract.HarvestedTargets = 1;
                    else
                        contract.SkippedTargets = 1;
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

            case HarvestContractPhase.Returned:
                this.FinishContract(
                    contract,
                    contract.HarvestedTargets == 1 && contract.Cargo.Count == 0,
                    contract.HarvestedTargets == 1
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

    private void UpdateTravel(ActiveHarvestContract contract)
    {
        if (contract.PhaseTicks > MaximumTravelTicks)
        {
            this.FinishContract(
                contract,
                succeeded: false,
                contract.Phase == HarvestContractPhase.Returning
                    ? "contract.failure.return-timeout"
                    : "contract.failure.travel-timeout");
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
        {
            this.FinishContract(
                contract,
                succeeded: false,
                contract.Phase == HarvestContractPhase.Returning
                    ? "contract.failure.return-interrupted"
                    : "contract.failure.path-interrupted");
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
        contract.Lease.Worker.faceDirection(contract.Plan.Target.FacingDirection);
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
            this.PersistOrDropCargo(contract);

        contract.Phase = HarvestContractPhase.Returned;
        contract.PhaseTicks = 0;
    }

    private bool TryApplyHarvest(ActiveHarvestContract contract)
    {
        Vector2 targetTile = new(contract.Plan.Target.TargetTile.X, contract.Plan.Target.TargetTile.Y);
        if (!HarvestTargetPlanner.IsMatureSupportedCrop(contract.Farm, targetTile)
            || contract.Lease.Worker.currentLocation != contract.Farm
            || contract.Lease.Worker.TilePoint != contract.Plan.Target.InteractionTile
            || !contract.Farm.terrainFeatures.TryGetValue(targetTile, out TerrainFeature? feature)
            || feature is not HoeDirt dirt
            || dirt.crop is not { } crop)
            return false;

        bool destroyAfterHarvest = !crop.RegrowsAfterHarvest();
        ContractHarvestCollector collector = new(contract.Farm, contract.Lease.Worker.Position);
        bool harvested = crop.harvest(
            contract.Plan.Target.TargetTile.X,
            contract.Plan.Target.TargetTile.Y,
            dirt,
            collector);
        if (!harvested || collector.Items.Count == 0)
            return false;

        if (destroyAfterHarvest)
            dirt.destroyCrop(showAnimation: false);

        foreach (Item item in collector.Items)
        {
            contract.Cargo.Add(new HarvestCargoEntry(Guid.NewGuid().ToString("N"), item));
            contract.HarvestedItems.Add(new HarvestItemSnapshot(item.DisplayName, item.Quality, item.Stack));
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
            this.BeginReturn(contract, depositOverflowOnReturn: false);
            return;
        }

        HarvestCargoEntry entry = contract.Cargo[0];
        HashSet<Point> attempted = contract.GetAttemptedChests(entry.TransferId);
        HarvestChestRoute? route = this.ChestRouter.FindBestRoute(
            contract.Farm,
            contract.Lease.Worker,
            contract.Lease.Worker.TilePoint,
            contract.Plan.ArrivalTile,
            entry.Item,
            attempted);
        if (route is null)
        {
            this.BeginReturn(contract, depositOverflowOnReturn: true);
            return;
        }

        try
        {
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
                route.InteractionTile,
                GetFacingDirection(route.InteractionTile, route.ChestTile),
                this.OnArrivedAtChest);
            contract.Controller = controller;
            contract.Lease.AttachController(controller);
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
                    contract.ChestDeliveredItems += HarvestTransferMath.GetDeliveredCount(requested, remaining);
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

    private void BeginReturn(ActiveHarvestContract contract, bool depositOverflowOnReturn)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        contract.DepositOverflowOnReturn |= depositOverflowOnReturn;
        if (contract.Lease.Worker.TilePoint == contract.Plan.ArrivalTile)
        {
            if (contract.DepositOverflowOnReturn && contract.Cargo.Count > 0)
                this.PersistOrDropCargo(contract);
            contract.CurrentChestRoute = null;
            contract.Phase = HarvestContractPhase.Returned;
            contract.PhaseTicks = 0;
            return;
        }

        try
        {
            contract.CurrentChestRoute = null;
            contract.Phase = HarvestContractPhase.Returning;
            contract.PhaseTicks = 0;
            PathFindController returning = this.CreatePathController(
                contract,
                contract.Plan.ArrivalTile,
                finalFacingDirection: Game1.left,
                this.OnReturnedToArrival);
            contract.Controller = returning;
            contract.Lease.AttachController(returning);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Worker '{contract.Lease.Worker.Name}' could not start the harvest return path: {ex.Message}", LogLevel.Warn);
            this.FinishContract(contract, succeeded: false, "contract.failure.return-path");
        }
    }

    private PathFindController CreatePathController(
        ActiveHarvestContract contract,
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
        ActiveHarvestContract contract,
        bool succeeded,
        string? failureTranslationKey)
    {
        if (!ReferenceEquals(this.ActiveContract, contract))
            return;

        this.ReleaseCurrentChestLock(contract);
        if (contract.Cargo.Count > 0)
            this.PersistOrDropCargo(contract);

        NpcLeaseRestoreResult restoreResult = contract.Lease.Restore();
        WateringContractSettlement settlement = WateringContractSettlement.Create(
            contract.Preview,
            contract.Dispatched,
            contract.Lease.StartTime,
            Game1.timeOfDay);
        Game1.player.Money += settlement.RefundedGold;
        this.ActiveContract = null;

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
                    items,
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
                items,
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

        try
        {
            Inventory overflow = Game1.player.team.GetOrCreateGlobalInventory(OverflowInventoryId);
            foreach (HarvestCargoEntry entry in contract.Cargo.ToArray())
            {
                int stack = entry.Item.Stack;
                bool applied = contract.TransferLedger.TryApply(
                    entry.TransferId,
                    () => overflow.Add(entry.Item));
                if (applied)
                    contract.OverflowItems += stack;
                contract.Cargo.Remove(entry);
            }
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Persistent harvest overflow failed; dropping exact cargo visibly: {ex}", LogLevel.Error);
            foreach (HarvestCargoEntry entry in contract.Cargo.ToArray())
            {
                int stack = entry.Item.Stack;
                try
                {
                    Game1.createItemDebris(entry.Item, contract.Lease.Worker.Position, -1, contract.Farm);
                    contract.DroppedItems += stack;
                    contract.Cargo.Remove(entry);
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
        Returning,
        Returned
    }

    private sealed class ActiveHarvestContract
    {
        private readonly Dictionary<string, HashSet<Point>> AttemptedChestTiles = new(StringComparer.Ordinal);

        public ActiveHarvestContract(
            Guid id,
            NpcWorkLease lease,
            WorkContractPreview preview,
            Farm farm,
            HarvestWorkPlan plan)
        {
            this.Id = id;
            this.Lease = lease;
            this.Preview = preview;
            this.Farm = farm;
            this.Plan = plan;
        }

        public Guid Id { get; }
        public NpcWorkLease Lease { get; }
        public WorkContractPreview Preview { get; }
        public Farm Farm { get; }
        public HarvestWorkPlan Plan { get; }
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
        public int HarvestedTargets { get; set; }
        public int SkippedTargets { get; set; }
        public int ChestDeliveredItems { get; set; }
        public int OverflowItems { get; set; }
        public int DroppedItems { get; set; }

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

    private sealed record HarvestItemSnapshot(string Name, int Quality, int Stack);
}
