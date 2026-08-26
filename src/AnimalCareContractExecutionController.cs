using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.FarmAnimals;
using StardewValley.Locations;
using StardewValley.Pathfinding;

namespace EvilFarmOwner;

internal sealed class AnimalCareContractExecutionController
{
    private const int ActionStartTicks = 8;
    private const int ActionDurationTicks = 32;
    private const int MaximumTravelTicks = 3600;
    private const int MaximumStalledTravelTicks = 180;
    private const int MaximumRouteFailures = 3;
    private const int MaximumProductLockWaitTicks = 300;
    private readonly IMonitor Monitor;
    private readonly ITranslationHelper Translation;
    private readonly AnimalPettingTargetPlanner Planner;
    private readonly AnimalHouseRoutePlanner HousePlanner;
    private readonly AnimalFeedingTargetPlanner FeedingPlanner;
    private readonly AnimalProductTargetPlanner ProductPlanner;
    private readonly AnimalProductTransferService ProductTransfer = new();
    private readonly RuntimeWorkforceRouteCoordinator? WorkforceRoutes;
    private ActiveAnimalCareStage? ActiveStage;
    private NamedContractCompletionState? LastCompletion;

    public AnimalCareContractExecutionController(
        IMonitor monitor,
        ITranslationHelper translation,
        RuntimeWorkforceRouteCoordinator? workforceRoutes = null)
    {
        this.Monitor = monitor;
        this.Translation = translation;
        this.Planner = new AnimalPettingTargetPlanner(monitor);
        this.HousePlanner = new AnimalHouseRoutePlanner(monitor);
        this.FeedingPlanner = new AnimalFeedingTargetPlanner(monitor);
        this.ProductPlanner = new AnimalProductTargetPlanner(monitor);
        this.WorkforceRoutes = workforceRoutes;
    }

    public string? LastStartFailureKey { get; private set; }

    public bool TryStartManaged(FarmWorkShiftContext shift)
    {
        this.LastStartFailureKey = null;
        if (this.ActiveStage is not null)
            return this.FailStart("contract.start.already-active");

        Farm farm = Game1.getFarm();
        AnimalPettingWorkPlan? plan = this.Planner.TryCreate(farm, shift.Lease.Worker);
        AnimalHouseWorkPlan? housePlan = plan is null
            ? this.HousePlanner.TryCreate(farm, shift.Lease.Worker)
            : null;
        if (plan is null && housePlan is null)
            return this.FailStart("animal-care.start.no-work");

        plan ??= new AnimalPettingWorkPlan(
            housePlan!.ArrivalTile,
            housePlan.ArrivalSide,
            FirstTarget: null);

        ActiveAnimalCareStage stage = new(shift, farm, plan);
        this.ActiveStage = stage;
        try
        {
            NPC worker = shift.Lease.Worker;
            Game1.warpCharacter(worker, farm, plan.ArrivalTile.ToVector2());
            worker.Position = FarmNavigationMap.GetAlignedCharacterPosition(plan.ArrivalTile);
            if (!ReferenceEquals(worker.currentLocation, farm)
                || !farm.characters.Contains(worker)
                || worker.TilePoint != plan.ArrivalTile)
                throw new InvalidOperationException("Worker did not arrive at the animal-care entrance.");
            worker.Halt();
            if (plan.FirstTarget is not null)
                this.BeginTravel(stage, plan.FirstTarget);
            else
                this.BeginTravelToHouse(stage, housePlan!.FirstHouse);
            return true;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Failed to dispatch animal-care worker: {ex}", LogLevel.Error);
            this.Complete(stage, false, "contract.failure.dispatch");
            return false;
        }
    }

    public void Update()
    {
        ActiveAnimalCareStage? stage = this.ActiveStage;
        if (stage is null || !Context.IsWorldReady)
            return;
        if (!Context.IsMainPlayer
            || Game1.Date.TotalDays != stage.Context.Lease.StartTotalDays
            || Game1.timeOfDay >= 2200)
        {
            this.Complete(stage, false, "contract.failure.safety-stop");
            return;
        }

        stage.PhaseTicks++;
        switch (stage.Phase)
        {
            case AnimalCarePhase.Traveling:
                if (stage.Context.Lease.Worker.controller is not null
                    && !ReferenceEquals(stage.Context.Lease.Worker.controller, stage.Controller))
                {
                    this.LogControllerConflict(stage);
                    this.Complete(stage, false, "contract.failure.controller-conflict");
                    return;
                }
                if (this.TryFinishTravel(stage, GetCurrentInteractionTile(stage), this.OnArrivedAtWork))
                    return;
                if (this.TryGetTravelInterruption(stage, out TravelInterruptionKind targetKind))
                    this.HandleTravelInterruption(stage, targetKind);
                break;
            case AnimalCarePhase.TravelingToBuilding:
                if (this.HasControllerConflict(stage))
                    return;
                if (this.TryFinishTravel(
                        stage,
                        stage.CurrentHouseRoute!.ExteriorInteractionTile,
                        this.OnArrivedAtBuilding))
                    return;
                if (this.TryGetTravelInterruption(stage, out TravelInterruptionKind buildingKind))
                    this.HandleTravelInterruption(stage, buildingKind);
                break;
            case AnimalCarePhase.TravelingToHouseExit:
                if (this.HasControllerConflict(stage))
                    return;
                if (this.TryFinishTravel(
                        stage,
                        stage.CurrentHouseRoute!.InteriorEntryTile,
                        this.OnArrivedAtHouseExit))
                    return;
                if (this.TryGetTravelInterruption(stage, out TravelInterruptionKind exitKind))
                    this.HandleTravelInterruption(stage, exitKind);
                break;
            case AnimalCarePhase.Acting:
                if (!stage.ActionApplied && stage.PhaseTicks >= ActionStartTicks)
                {
                    stage.ActionApplied = true;
                    if (stage.CurrentFeedingTarget is { } feeding)
                    {
                        stage.AttemptedTroughTiles.Add(feeding.TargetTile);
                        try
                        {
                            if (this.TryFeedTrough(stage, feeding))
                                stage.FilledTroughs++;
                        }
                        catch (Exception ex)
                        {
                            this.Monitor.Log($"Animal feeding failed closed: {ex}", LogLevel.Error);
                            this.Complete(stage, false, "contract.failure.target-invalidated");
                            return;
                        }
                    }
                    else if (stage.CurrentProductTarget is { } product)
                    {
                        stage.AttemptedProductTargets.Add(product.StableKey);
                        this.BeginProductTransfer(stage, product);
                        return;
                    }
                    else
                    {
                        stage.AttemptedAnimalIds.Add(stage.CurrentTarget!.AnimalId);
                        if (this.TryPetAnimal(stage))
                            stage.PettedAnimals++;
                    }
                }
                if (stage.PhaseTicks >= ActionDurationTicks)
                    this.BeginNextOrReturn(stage);
                break;
            case AnimalCarePhase.WaitingForProductChestLock:
                if (stage.PhaseTicks > MaximumProductLockWaitTicks)
                    this.Complete(stage, false, "harvest.failure.storage-unavailable");
                break;
            case AnimalCarePhase.Returning:
                if (stage.Context.Lease.Worker.controller is not null
                    && !ReferenceEquals(stage.Context.Lease.Worker.controller, stage.Controller))
                {
                    this.LogControllerConflict(stage);
                    this.Complete(stage, false, "contract.failure.controller-conflict");
                    return;
                }
                if (this.TryFinishTravel(stage, stage.Plan.ArrivalTile, this.OnReturned))
                    return;
                if (this.TryGetTravelInterruption(stage, out TravelInterruptionKind returnKind))
                    this.HandleTravelInterruption(stage, returnKind);
                break;
            case AnimalCarePhase.Returned:
                this.Complete(stage, true, "");
                break;
        }
    }

    public NamedContractRuntimeState? GetRuntimeState()
    {
        ActiveAnimalCareStage? stage = this.ActiveStage;
        if (stage is null)
            return null;
        return new NamedContractRuntimeState(
            stage.Context.Id.ToString("N"), stage.Context.RequestId,
            stage.Context.Requester.UniqueMultiplayerID, stage.Context.Lease.Worker.Name,
            NamedFarmTask.FarmWork, stage.Context.BillingPreview.EfficiencyMultiplier,
            stage.Phase.ToString(), stage.Plan.ArrivalTile.X, stage.Plan.ArrivalTile.Y,
            stage.Plan.ArrivalSide, 0,
            stage.CurrentProductTarget?.TargetTile.X
                ?? stage.CurrentTarget?.TargetTile.X
                ?? stage.Plan.ArrivalTile.X,
            stage.CurrentProductTarget?.TargetTile.Y
                ?? stage.CurrentTarget?.TargetTile.Y
                ?? stage.Plan.ArrivalTile.Y,
            stage.Context.BillingPreview.MaximumAuthorizedWage,
            stage.Context.Lease.StartTime,
            stage.PettedAnimals + stage.FilledTroughs + stage.CollectedProductTargets,
            stage.ProducedItems.ToArray(),
            stage.CompletedTransferIds.OrderBy(id => id, StringComparer.Ordinal).ToArray())
        {
            HarvestDestination = stage.Context.HarvestDestination
        };
    }

    public NamedContractCompletionState? ConsumeCompletion()
    {
        NamedContractCompletionState? completion = this.LastCompletion;
        this.LastCompletion = null;
        return completion;
    }

    public void OnReturnedToTitle()
    {
        if (this.ActiveStage?.CurrentProductDestination?.Chest.GetMutex().IsLockHeld() == true)
            this.ActiveStage.CurrentProductDestination.Chest.GetMutex().ReleaseLock();
        this.ActiveStage = null;
    }

    public void OnDayEnding()
    {
        if (this.ActiveStage is { } stage)
            this.Complete(stage, false, "contract.failure.day-ending");
    }

    private void BeginTravel(ActiveAnimalCareStage stage, AnimalPettingTargetPlan target)
    {
        stage.CurrentTarget = target;
        stage.CurrentFeedingTarget = null;
        stage.CurrentProductTarget = null;
        stage.CurrentProductDestination = null;
        stage.ActionApplied = false;
        stage.Phase = AnimalCarePhase.Traveling;
        stage.PhaseTicks = 0;
        NPC worker = stage.Context.Lease.Worker;
        if (worker.TilePoint == target.InteractionTile)
        {
            this.OnArrivedAtWork(worker, stage.WorkLocation);
            return;
        }
        if (!FarmNavigationMap.CanBeginPath(
                stage.WorkLocation, worker, worker.TilePoint, target.Path, out string failure))
        {
            this.HandleTravelInterruption(
                stage,
                TravelInterruptionKind.FirstStepRejected,
                target.Path,
                failure);
            return;
        }
        try
        {
            stage.Controller = this.CreateController(stage, target.Path, target.InteractionTile,
                target.FacingDirection, this.OnArrivedAtWork);
            stage.Context.Lease.AttachController(stage.Controller);
        }
        catch (Exception ex)
        {
            this.HandleTravelInterruption(
                stage,
                TravelInterruptionKind.ControllerSetupFailed,
                target.Path,
                ex.Message);
        }
    }

    private void OnArrivedAtWork(Character character, GameLocation location)
    {
        if (this.ActiveStage is not { } stage
            || !ReferenceEquals(character, stage.Context.Lease.Worker)
            || !ReferenceEquals(location, stage.WorkLocation))
            return;
        stage.Phase = AnimalCarePhase.Acting;
        stage.PhaseTicks = 0;
        stage.Context.Lease.Worker.Halt();
        stage.Context.Lease.Worker.faceDirection(
            stage.CurrentFeedingTarget?.FacingDirection
                ?? stage.CurrentProductTarget?.FacingDirection
                ?? stage.CurrentTarget!.FacingDirection);
    }

    private bool TryPetAnimal(ActiveAnimalCareStage stage)
    {
        if (stage.CurrentTarget is null
            || !stage.WorkLocation.animals.TryGetValue(stage.CurrentTarget.AnimalId, out FarmAnimal? animal)
            || !ReferenceEquals(animal.currentLocation, stage.WorkLocation)
            || Math.Abs(animal.TilePoint.X - stage.Context.Lease.Worker.TilePoint.X)
                + Math.Abs(animal.TilePoint.Y - stage.Context.Lease.Worker.TilePoint.Y) != 1
            || AnimalPettingPolicy.GetSkipReason(true, animal.wasPet.Value, Game1.timeOfDay >= 1900)
                != AnimalCareSkipReason.None)
            return false;

        Farmer requester = stage.Context.Requester;
        FarmAnimalData? data = animal.GetAnimalData();
        bool hasProfession = data is not null
            && data.ProfessionForHappinessBoost >= 0
            && requester.professions.Contains(data.ProfessionForHappinessBoost);
        ManualPetGains gains = AnimalPettingPolicy.GetManualPetGains(
            animal.wasAutoPet.Value,
            hasProfession,
            data?.HappinessDrain ?? 0);
        stage.Context.Lease.Worker.faceDirection(GetFacingDirection(
            stage.Context.Lease.Worker.TilePoint,
            animal.TilePoint));
        animal.wasPet.Value = true;
        animal.friendshipTowardFarmer.Value = Math.Min(
            1000,
            animal.friendshipTowardFarmer.Value + gains.Friendship);
        animal.happiness.Value = (byte)Math.Min(
            255, animal.happiness.Value + gains.Happiness);
        animal.doEmote(animal.wasAutoPet.Value
            ? 32
            : animal.moodMessage.Value == 4 ? 12 : 20);
        animal.makeSound();
        requester.gainExperience(0, 5);
        return true;
    }

    private void SkipCurrentAndContinue(ActiveAnimalCareStage stage)
    {
        if (stage.CurrentFeedingTarget is { } feeding)
            stage.AttemptedTroughTiles.Add(feeding.TargetTile);
        else if (stage.CurrentProductTarget is { } product)
            stage.AttemptedProductTargets.Add(product.StableKey);
        else
            stage.AttemptedAnimalIds.Add(stage.CurrentTarget!.AnimalId);
        if (ReferenceEquals(stage.Context.Lease.Worker.controller, stage.Controller))
            stage.Context.Lease.Worker.controller = null;
        this.WorkforceRoutes?.ReleaseWorker(stage.Context.Lease.Worker.Name);
        stage.Controller = null;
        this.BeginNextOrReturn(stage);
    }

    private void BeginNextOrReturn(ActiveAnimalCareStage stage)
    {
        if (stage.WorkLocation is AnimalHouse house)
        {
            AnimalFeedingTargetPlan? feeding = this.FeedingPlanner.TryFindNext(
                house,
                stage.Context.Lease.Worker,
                stage.Context.Lease.Worker.TilePoint,
                stage.AttemptedTroughTiles,
                stage.RouteObstacles);
            if (feeding is not null)
            {
                try
                {
                    this.BeginFeedingTravel(stage, feeding);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log(
                        $"Animal-feeding route to {feeding.TargetTile} failed: {ex.Message}",
                        LogLevel.Warn);
                    stage.AttemptedTroughTiles.Add(feeding.TargetTile);
                    this.BeginNextOrReturn(stage);
                }
                return;
            }
        }

        AnimalPettingTargetPlan? next = this.Planner.TryFindNext(
            stage.WorkLocation, stage.Context.Lease.Worker,
            stage.Context.Lease.Worker.TilePoint, stage.AttemptedAnimalIds,
            stage.RouteObstacles);
        if (next is not null)
        {
            try
            {
                this.BeginTravel(stage, next);
            }
            catch (Exception ex)
            {
                this.Monitor.Log(
                    $"Animal-care route to {next.AnimalId} failed: {ex.Message}",
                    LogLevel.Warn);
                stage.AttemptedAnimalIds.Add(next.AnimalId);
                this.BeginNextOrReturn(stage);
            }
            return;
        }

        if (stage.WorkLocation is AnimalHouse productHouse)
        {
            AnimalProductTargetPlan? product = this.ProductPlanner.TryFindNext(
                productHouse,
                stage.Context.Lease.Worker,
                stage.Context.Lease.Worker.TilePoint,
                stage.AttemptedProductTargets,
                stage.RouteObstacles);
            if (product is not null)
            {
                try
                {
                    this.BeginProductTravel(stage, product);
                }
                catch (Exception ex)
                {
                    this.Monitor.Log(
                        $"Animal-product route {product.StableKey} failed: {ex.Message}",
                        LogLevel.Warn);
                    stage.AttemptedProductTargets.Add(product.StableKey);
                    this.BeginNextOrReturn(stage);
                }
                return;
            }
        }

        if (stage.WorkLocation is AnimalHouse)
        {
            this.BeginExitHouse(stage);
            return;
        }

        if (this.TryBeginNextHouse(stage))
            return;

        this.BeginFarmReturn(stage);
    }

    private void BeginFarmReturn(ActiveAnimalCareStage stage)
    {
        Stack<Point>? path = this.Planner.TryCreateReturnPath(
            stage.Farm,
            stage.Context.Lease.Worker,
            stage.Plan.ArrivalTile,
            stage.RouteObstacles);
        if (path is null)
        {
            this.Complete(stage, false, "contract.failure.return-unreachable");
            return;
        }
        stage.Phase = AnimalCarePhase.Returning;
        stage.PhaseTicks = 0;
        if (stage.Context.Lease.Worker.TilePoint == stage.Plan.ArrivalTile)
        {
            this.OnReturned(stage.Context.Lease.Worker, stage.Farm);
            return;
        }
        try
        {
            if (!FarmNavigationMap.CanBeginPath(
                    stage.Farm,
                    stage.Context.Lease.Worker,
                    stage.Context.Lease.Worker.TilePoint,
                    path,
                    out string failure))
            {
                this.HandleTravelInterruption(
                    stage,
                    TravelInterruptionKind.FirstStepRejected,
                    path,
                    failure);
                return;
            }
            stage.Controller = this.CreateController(stage, path, stage.Plan.ArrivalTile,
                Game1.down, this.OnReturned);
            stage.Context.Lease.AttachController(stage.Controller);
        }
        catch (Exception ex)
        {
            this.HandleTravelInterruption(
                stage,
                TravelInterruptionKind.ControllerSetupFailed,
                path,
                ex.Message);
        }
    }

    private void BeginFeedingTravel(
        ActiveAnimalCareStage stage,
        AnimalFeedingTargetPlan target)
    {
        stage.CurrentTarget = null;
        stage.CurrentFeedingTarget = target;
        stage.CurrentProductTarget = null;
        stage.CurrentProductDestination = null;
        stage.ActionApplied = false;
        stage.Phase = AnimalCarePhase.Traveling;
        stage.PhaseTicks = 0;
        NPC worker = stage.Context.Lease.Worker;
        if (worker.TilePoint == target.InteractionTile)
        {
            this.OnArrivedAtWork(worker, stage.WorkLocation);
            return;
        }
        if (!FarmNavigationMap.CanBeginPath(
                stage.WorkLocation, worker, worker.TilePoint, target.Path, out string failure))
        {
            this.HandleTravelInterruption(
                stage,
                TravelInterruptionKind.FirstStepRejected,
                target.Path,
                failure);
            return;
        }
        try
        {
            stage.Controller = this.CreateController(
                stage,
                target.Path,
                target.InteractionTile,
                target.FacingDirection,
                this.OnArrivedAtWork);
            stage.Context.Lease.AttachController(stage.Controller);
        }
        catch (Exception ex)
        {
            this.HandleTravelInterruption(
                stage,
                TravelInterruptionKind.ControllerSetupFailed,
                target.Path,
                ex.Message);
        }
    }

    private bool TryFeedTrough(ActiveAnimalCareStage stage, AnimalFeedingTargetPlan target)
    {
        if (stage.WorkLocation is not AnimalHouse house
            || house.doesTileHaveProperty(
                target.TargetTile.X,
                target.TargetTile.Y,
                "Trough",
                "Back") is null
            || house.objects.ContainsKey(target.TargetTile.ToVector2()))
            return false;

        StardewValley.Object? hay = GameLocation.GetHayFromAnySilo(house.GetRootLocation());
        if (hay is null)
            return false;
        if (!house.objects.TryAdd(target.TargetTile.ToVector2(), hay))
        {
            int unstored = GameLocation.StoreHayInAnySilo(1, house.GetRootLocation());
            if (unstored > 0)
                throw new InvalidOperationException(
                    "Consumed hay could not be restored after a failed trough placement.");
            return false;
        }
        house.playSound("coin", target.TargetTile.ToVector2());
        return true;
    }

    private bool TryGetTravelInterruption(
        ActiveAnimalCareStage stage,
        out TravelInterruptionKind kind)
    {
        kind = default;
        if (stage.PhaseTicks > MaximumTravelTicks)
        {
            kind = TravelInterruptionKind.Timeout;
            return true;
        }

        NPC worker = stage.Context.Lease.Worker;
        if ((Game1.activeClickableMenu is null || Game1.IsMultiplayer)
            && stage.TravelWatchdog.Tick(
                worker.Position.X,
                worker.Position.Y,
                new GridPoint(worker.TilePoint.X, worker.TilePoint.Y),
                MaximumStalledTravelTicks))
        {
            kind = TravelInterruptionKind.ProgressStall;
            return true;
        }

        if (stage.PhaseTicks > 1 && worker.controller is null)
        {
            kind = TravelInterruptionKind.ControllerEnded;
            return true;
        }

        return false;
    }

    private void HandleTravelInterruption(
        ActiveAnimalCareStage stage,
        TravelInterruptionKind kind,
        Stack<Point>? explicitPath = null,
        string? explicitCollisionProbe = null)
    {
        Point destination = this.GetPhaseDestination(stage);
        TravelInterruptionSnapshot diagnostic = TravelInterruptionRuntime.Capture(
            stage.WorkLocation,
            stage.Context.Lease.Worker,
            stage.Controller,
            destination,
            kind,
            stage.TravelWatchdog.PreviousProgressTile,
            explicitPath,
            explicitCollisionProbe);
        string routeKey = GetRouteFailureKey(stage);
        this.Monitor.Log(
            $"Animal-care travel interrupted: shift={stage.Context.Id:N}, "
            + $"worker={stage.Context.Lease.Worker.Name}, phase={stage.Phase}, "
            + $"routeKey={routeKey}, {diagnostic.ToTechnicalReason()}.",
            LogLevel.Debug);
        TravelObstacleSelection obstacle = TravelRouteExclusionPolicy.Select(
            diagnostic.LocationKey,
            diagnostic.Origin,
            diagnostic.PreviousProgressTile,
            diagnostic.NextWaypoint);
        stage.RouteObstacles.Add(obstacle);

        if (ReferenceEquals(stage.Context.Lease.Worker.controller, stage.Controller))
            stage.Context.Lease.Worker.controller = null;
        stage.Controller = null;
        stage.Context.Lease.Worker.Halt();
        TravelFailureDecision decision = stage.RouteFailures.Record(routeKey);
        if (decision.CanRetry)
        {
            this.Monitor.Log(
                $"Animal-care route {routeKey} will replan around tile={obstacle.Tile}, "
                + $"edge={obstacle.Edge} [{decision.FailureCount}/{decision.MaximumFailures}].",
                LogLevel.Debug);
            switch (stage.Phase)
            {
                case AnimalCarePhase.Traveling:
                    this.BeginNextOrReturn(stage);
                    return;
                case AnimalCarePhase.TravelingToBuilding:
                    if (!this.TryBeginNextHouse(stage))
                        this.BeginFarmReturn(stage);
                    return;
                case AnimalCarePhase.TravelingToHouseExit:
                    this.BeginExitHouse(stage);
                    return;
                case AnimalCarePhase.Returning:
                    this.BeginFarmReturn(stage);
                    return;
            }
        }

        this.Monitor.Log(
            $"Animal-care route {routeKey} exhausted {MaximumRouteFailures} attempts; "
            + diagnostic.ToTechnicalReason() + ".",
            LogLevel.Warn);
        if (stage.Phase == AnimalCarePhase.Traveling)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("animal-care.hud.target-route-skipped", new
                {
                    worker = stage.Context.Lease.Worker.displayName,
                    reason = this.Translation.Get(diagnostic.ReasonTranslationKey)
                }),
                HUDMessage.error_type));
            this.SkipCurrentAndContinue(stage);
            return;
        }
        if (stage.Phase == AnimalCarePhase.TravelingToBuilding)
        {
            stage.VisitedBuildingIds.Add(stage.CurrentHouseRoute!.BuildingId);
            stage.CurrentHouseRoute = null;
            this.BeginNextOrReturn(stage);
            return;
        }

        Game1.addHUDMessage(new HUDMessage(
            this.Translation.Get("animal-care.hud.return-route-stopped", new
            {
                worker = stage.Context.Lease.Worker.displayName,
                reason = this.Translation.Get(diagnostic.ReasonTranslationKey)
            }),
            HUDMessage.error_type));
        this.Complete(stage, false, "contract.failure.return-unreachable");
    }

    private void LogControllerConflict(ActiveAnimalCareStage stage)
    {
        TravelInterruptionSnapshot diagnostic = TravelInterruptionRuntime.Capture(
            stage.WorkLocation,
            stage.Context.Lease.Worker,
            stage.Controller,
            this.GetPhaseDestination(stage),
            TravelInterruptionKind.ControllerReplaced,
            stage.TravelWatchdog.PreviousProgressTile);
        this.Monitor.Log(
            $"Animal-care controller conflict: shift={stage.Context.Id:N}, "
            + diagnostic.ToTechnicalReason() + ".",
            LogLevel.Warn);
    }

    private Point GetPhaseDestination(ActiveAnimalCareStage stage)
    {
        return stage.Phase switch
        {
            AnimalCarePhase.Traveling => GetCurrentInteractionTile(stage),
            AnimalCarePhase.TravelingToBuilding =>
                stage.CurrentHouseRoute!.ExteriorInteractionTile,
            AnimalCarePhase.TravelingToHouseExit =>
                stage.CurrentHouseRoute!.InteriorEntryTile,
            AnimalCarePhase.Returning => stage.Plan.ArrivalTile,
            _ => stage.Context.Lease.Worker.TilePoint
        };
    }

    private static string GetRouteFailureKey(ActiveAnimalCareStage stage)
    {
        return stage.Phase switch
        {
            AnimalCarePhase.Traveling when stage.CurrentFeedingTarget is { } feeding =>
                $"feed:{stage.WorkLocation.NameOrUniqueName}:{feeding.TargetTile.X}:{feeding.TargetTile.Y}",
            AnimalCarePhase.Traveling when stage.CurrentProductTarget is { } product =>
                $"product:{product.StableKey}",
            AnimalCarePhase.Traveling when stage.CurrentTarget is { } animal =>
                $"animal:{stage.WorkLocation.NameOrUniqueName}:{animal.AnimalId}",
            AnimalCarePhase.TravelingToBuilding =>
                $"building:{stage.CurrentHouseRoute!.BuildingId:N}",
            AnimalCarePhase.TravelingToHouseExit =>
                $"exit:{stage.CurrentHouseRoute!.BuildingId:N}",
            AnimalCarePhase.Returning => "return:Farm",
            _ => $"phase:{stage.Phase}"
        };
    }

    private void BeginProductTravel(
        ActiveAnimalCareStage stage,
        AnimalProductTargetPlan target)
    {
        stage.CurrentTarget = null;
        stage.CurrentFeedingTarget = null;
        stage.CurrentProductTarget = target;
        stage.CurrentProductDestination = null;
        stage.ActionApplied = false;
        stage.Phase = AnimalCarePhase.Traveling;
        stage.PhaseTicks = 0;
        NPC worker = stage.Context.Lease.Worker;
        if (worker.TilePoint == target.InteractionTile)
        {
            this.OnArrivedAtWork(worker, stage.WorkLocation);
            return;
        }
        if (!FarmNavigationMap.CanBeginPath(
                stage.WorkLocation, worker, worker.TilePoint, target.Path, out string failure))
        {
            this.HandleTravelInterruption(
                stage,
                TravelInterruptionKind.FirstStepRejected,
                target.Path,
                failure);
            return;
        }
        try
        {
            stage.Controller = this.CreateController(
                stage,
                target.Path,
                target.InteractionTile,
                target.FacingDirection,
                this.OnArrivedAtWork);
            stage.Context.Lease.AttachController(stage.Controller);
        }
        catch (Exception ex)
        {
            this.HandleTravelInterruption(
                stage,
                TravelInterruptionKind.ControllerSetupFailed,
                target.Path,
                ex.Message);
        }
    }

    private void BeginProductTransfer(
        ActiveAnimalCareStage stage,
        AnimalProductTargetPlan target)
    {
        if (stage.WorkLocation is not AnimalHouse house)
        {
            this.Complete(stage, false, "contract.failure.target-invalidated");
            return;
        }

        if (stage.Context.HarvestDestination == HarvestDestinationMode.RequesterInventory)
        {
            Farmer? requester = Game1.GetPlayer(
                stage.Context.Requester.UniqueMultiplayerID,
                onlyOnline: true);
            if (requester is null)
            {
                this.Complete(stage, false, "harvest.failure.requester-destination-unavailable");
                return;
            }

            AnimalProductTransferFailure failure = this.ProductTransfer.TryCommitToRequester(
                house,
                target,
                requester,
                out Item? delivered);
            if (failure == AnimalProductTransferFailure.SourceChanged)
            {
                this.Monitor.Log(
                    $"Animal-product target {target.StableKey} changed before commit; skipping it.",
                    LogLevel.Warn);
                stage.Phase = AnimalCarePhase.Acting;
                stage.PhaseTicks = ActionDurationTicks;
                return;
            }
            if (failure != AnimalProductTransferFailure.None || delivered is null)
            {
                this.Monitor.Log(
                    $"Animal-product requester transfer {target.StableKey} failed closed: {failure}.",
                    LogLevel.Warn);
                this.Complete(stage, false, "harvest.failure.requester-destination-unavailable");
                return;
            }

            this.RecordDeliveredProduct(stage, delivered, toRequester: true);
            stage.Phase = AnimalCarePhase.Acting;
            stage.PhaseTicks = ActionDurationTicks;
            return;
        }

        Item preview = ItemRegistry.Create(target.QualifiedItemId);
        preview.Stack = target.Stack;
        preview.Quality = target.Quality;
        AnimalProductChestDestination? destination =
            AnimalProductDestinationPlanner.FindBestChest(stage.Farm, preview);
        if (destination is null)
        {
            this.Complete(stage, false, "harvest.failure.storage-unavailable");
            return;
        }

        stage.CurrentProductDestination = destination;
        stage.Phase = AnimalCarePhase.WaitingForProductChestLock;
        stage.PhaseTicks = 0;
        destination.Chest.GetMutex().RequestLock(
            () => this.OnProductChestLockAcquired(
                stage.Context.Id,
                target.StableKey,
                destination),
            () => this.OnProductChestLockFailed(
                stage.Context.Id,
                target.StableKey,
                destination));
    }

    private void OnProductChestLockAcquired(
        Guid shiftId,
        string stableKey,
        AnimalProductChestDestination destination)
    {
        ActiveAnimalCareStage? stage = this.ActiveStage;
        if (stage is null
            || stage.Context.Id != shiftId
            || stage.Phase != AnimalCarePhase.WaitingForProductChestLock
            || stage.CurrentProductTarget?.StableKey != stableKey
            || !ReferenceEquals(stage.CurrentProductDestination, destination))
        {
            if (destination.Chest.GetMutex().IsLockHeld())
                destination.Chest.GetMutex().ReleaseLock();
            return;
        }

        try
        {
            Vector2 tile = destination.Tile.ToVector2();
            if (!stage.Farm.objects.TryGetValue(tile, out StardewValley.Object? current)
                || !ReferenceEquals(current, destination.Chest)
                || stage.WorkLocation is not AnimalHouse house)
            {
                this.Complete(stage, false, "harvest.failure.storage-unavailable");
                return;
            }

            AnimalProductTransferFailure failure = this.ProductTransfer.TryCommitToChest(
                house,
                stage.CurrentProductTarget,
                destination.Chest,
                stage.Context.Requester,
                out Item? delivered);
            if (failure == AnimalProductTransferFailure.SourceChanged)
            {
                this.Monitor.Log(
                    $"Animal-product target {stableKey} changed before locked commit; skipping it.",
                    LogLevel.Warn);
                stage.Phase = AnimalCarePhase.Acting;
                stage.PhaseTicks = ActionDurationTicks;
                return;
            }
            if (failure != AnimalProductTransferFailure.None || delivered is null)
            {
                this.Monitor.Log(
                    $"Animal-product chest transfer {stableKey} failed closed at "
                    + $"{destination.Tile}: {failure}.",
                    LogLevel.Warn);
                this.Complete(stage, false, "harvest.failure.storage-unavailable");
                return;
            }

            this.RecordDeliveredProduct(stage, delivered, toRequester: false);
            stage.Phase = AnimalCarePhase.Acting;
            stage.PhaseTicks = ActionDurationTicks;
        }
        finally
        {
            if (destination.Chest.GetMutex().IsLockHeld())
                destination.Chest.GetMutex().ReleaseLock();
        }
    }

    private void OnProductChestLockFailed(
        Guid shiftId,
        string stableKey,
        AnimalProductChestDestination destination)
    {
        ActiveAnimalCareStage? stage = this.ActiveStage;
        if (stage is null
            || stage.Context.Id != shiftId
            || stage.Phase != AnimalCarePhase.WaitingForProductChestLock
            || stage.CurrentProductTarget?.StableKey != stableKey
            || !ReferenceEquals(stage.CurrentProductDestination, destination))
            return;

        this.Complete(stage, false, "harvest.failure.storage-unavailable");
    }

    private void RecordDeliveredProduct(
        ActiveAnimalCareStage stage,
        Item delivered,
        bool toRequester)
    {
        string transferId = Guid.NewGuid().ToString("N");
        stage.CompletedTransferIds.Add(transferId);
        stage.ProducedItems.Add(new NamedContractCargoState(
            transferId,
            delivered.QualifiedItemId,
            delivered.DisplayName,
            delivered.Quality,
            delivered.Stack));
        stage.CollectedProductTargets++;
        if (toRequester)
            stage.PlayerItems += delivered.Stack;
        else
            stage.ChestItems += delivered.Stack;
        stage.WorkLocation.playSound("coin", stage.CurrentProductTarget!.TargetTile.ToVector2());
    }

    private static Point GetCurrentInteractionTile(ActiveAnimalCareStage stage)
    {
        return stage.CurrentFeedingTarget?.InteractionTile
            ?? stage.CurrentProductTarget?.InteractionTile
            ?? stage.CurrentTarget?.InteractionTile
            ?? throw new InvalidOperationException("Animal-care travel has no current target.");
    }

    private void OnReturned(Character character, GameLocation location)
    {
        if (this.ActiveStage is not { } stage
            || !ReferenceEquals(character, stage.Context.Lease.Worker)
            || !ReferenceEquals(location, stage.WorkLocation))
            return;
        stage.Context.Lease.Worker.Halt();
        stage.Phase = AnimalCarePhase.Returned;
        stage.PhaseTicks = 0;
    }

    private bool TryFinishTravel(
        ActiveAnimalCareStage stage,
        Point destination,
        PathFindController.endBehavior callback)
    {
        NPC worker = stage.Context.Lease.Worker;
        if (worker.TilePoint != destination)
            return false;
        if (worker.controller is not null && !ReferenceEquals(worker.controller, stage.Controller))
        {
            this.Complete(stage, false, "contract.failure.controller-conflict");
            return true;
        }
        if (ReferenceEquals(worker.controller, stage.Controller))
            worker.controller = null;
        stage.RouteFailures.Reset(GetRouteFailureKey(stage));
        stage.Controller = null;
        worker.Position = FarmNavigationMap.GetAlignedCharacterPosition(destination);
        callback(worker, stage.WorkLocation);
        return true;
    }

    private PathFindController CreateController(
        ActiveAnimalCareStage stage,
        Stack<Point> path,
        Point destination,
        int facing,
        PathFindController.endBehavior callback)
    {
        PathFindController controller = new(
            new Stack<Point>(path.Reverse()), stage.WorkLocation,
            stage.Context.Lease.Worker, destination)
        {
            finalFacingDirection = facing,
            endBehaviorFunction = callback,
            nonDestructivePathing = true,
            NPCSchedule = true
        };
        if (controller.pathToEndPoint is not { Count: > 0 })
            throw new InvalidOperationException($"No animal-care path to {destination}.");
        if (this.WorkforceRoutes?.TryReserve(stage.Context.Lease, controller.pathToEndPoint) == false)
            throw new InvalidOperationException("The shared workforce route could not be reserved.");
        stage.TravelWatchdog.Reset(
            stage.Context.Lease.Worker.Position.X,
            stage.Context.Lease.Worker.Position.Y,
            new GridPoint(
                stage.Context.Lease.Worker.TilePoint.X,
                stage.Context.Lease.Worker.TilePoint.Y));
        return controller;
    }

    private void Complete(ActiveAnimalCareStage stage, bool succeeded, string reason)
    {
        if (!ReferenceEquals(this.ActiveStage, stage))
            return;
        if (stage.CurrentProductDestination?.Chest.GetMutex().IsLockHeld() == true)
            stage.CurrentProductDestination.Chest.GetMutex().ReleaseLock();
        int producedItems = stage.ProducedItems.Sum(item => item.Stack);
        bool placementBalanced = HarvestPlacementAudit.IsBalanced(
            producedItems,
            stage.PlayerItems,
            stage.ChestItems,
            overflow: 0,
            quarantine: 0,
            dropped: 0,
            unresolved: 0);
        if (!placementBalanced)
        {
            succeeded = false;
            reason = "harvest.failure.placement-audit";
        }
        this.Monitor.Log(
            $"Animal-product placement audit for shift {stage.Context.Id:N}: "
            + $"produced={producedItems}, player={stage.PlayerItems}, "
            + $"chest={stage.ChestItems}, balanced={placementBalanced}.",
            placementBalanced ? LogLevel.Debug : LogLevel.Error);
        if (ReferenceEquals(stage.Context.Lease.Worker.controller, stage.Controller))
            stage.Context.Lease.Worker.controller = null;
        this.WorkforceRoutes?.ReleaseWorker(stage.Context.Lease.Worker.Name);
        stage.Controller = null;
        this.ActiveStage = null;
        this.LastCompletion = new NamedContractCompletionState(
            stage.Context.Id.ToString("N"), stage.Context.RequestId,
            stage.Context.Requester.UniqueMultiplayerID, stage.Context.Lease.Worker.Name,
            NamedFarmTask.FarmWork, succeeded, reason,
            stage.PettedAnimals + stage.FilledTroughs + stage.CollectedProductTargets,
            stage.PlayerItems, stage.ChestItems, 0, 0, 0, 0, 0, 0,
            stage.ProducedItems.ToArray(),
            stage.CompletedTransferIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            Array.Empty<NamedContractTransferState>(), Array.Empty<NamedContractTransferState>())
        {
            HarvestDestination = stage.Context.HarvestDestination
        };
    }

    private bool FailStart(string key)
    {
        this.LastStartFailureKey = key;
        return false;
    }

    private static int GetFacingDirection(Point interaction, Point target)
    {
        if (target.X > interaction.X) return Game1.right;
        if (target.X < interaction.X) return Game1.left;
        if (target.Y > interaction.Y) return Game1.down;
        return Game1.up;
    }

    private bool TryBeginNextHouse(ActiveAnimalCareStage stage)
    {
        AnimalHouseRoutePlan? route = this.HousePlanner.TryFindNext(
            stage.Farm,
            stage.Context.Lease.Worker,
            stage.Context.Lease.Worker.TilePoint,
            stage.VisitedBuildingIds,
            stage.RouteObstacles);
        if (route is null)
            return false;

        return this.BeginTravelToHouse(stage, route);
    }

    private bool BeginTravelToHouse(ActiveAnimalCareStage stage, AnimalHouseRoutePlan route)
    {

        stage.CurrentHouseRoute = route;
        stage.Phase = AnimalCarePhase.TravelingToBuilding;
        stage.PhaseTicks = 0;
        if (stage.Context.Lease.Worker.TilePoint == route.ExteriorInteractionTile)
        {
            this.OnArrivedAtBuilding(stage.Context.Lease.Worker, stage.Farm);
            return true;
        }
        try
        {
            if (!FarmNavigationMap.CanBeginPath(
                    stage.Farm,
                    stage.Context.Lease.Worker,
                    stage.Context.Lease.Worker.TilePoint,
                    route.ExteriorPath,
                    out string failure))
            {
                this.HandleTravelInterruption(
                    stage,
                    TravelInterruptionKind.FirstStepRejected,
                    route.ExteriorPath,
                    failure);
                return ReferenceEquals(this.ActiveStage, stage);
            }
            stage.Controller = this.CreateController(
                stage,
                route.ExteriorPath,
                route.ExteriorInteractionTile,
                Game1.up,
                this.OnArrivedAtBuilding);
            stage.Context.Lease.AttachController(stage.Controller);
            return true;
        }
        catch (Exception ex)
        {
            this.HandleTravelInterruption(
                stage,
                TravelInterruptionKind.ControllerSetupFailed,
                route.ExteriorPath,
                ex.Message);
            return ReferenceEquals(this.ActiveStage, stage);
        }
    }

    private void OnArrivedAtBuilding(Character character, GameLocation location)
    {
        if (this.ActiveStage is not { CurrentHouseRoute: { } route } stage
            || !ReferenceEquals(character, stage.Context.Lease.Worker)
            || !ReferenceEquals(location, stage.Farm)
            || stage.Phase != AnimalCarePhase.TravelingToBuilding)
            return;

        stage.Farm.playSound("doorClose", route.ExteriorDoorTile.ToVector2());
        Game1.warpCharacter(stage.Context.Lease.Worker, route.House, route.InteriorEntryTile.ToVector2());
        stage.Context.Lease.Worker.Position =
            FarmNavigationMap.GetAlignedCharacterPosition(route.InteriorEntryTile);
        if (!ReferenceEquals(stage.Context.Lease.Worker.currentLocation, route.House)
            || !route.House.characters.Contains(stage.Context.Lease.Worker))
        {
            this.Complete(stage, false, "contract.failure.dispatch");
            return;
        }
        stage.WorkLocation = route.House;
        stage.Context.Lease.Worker.Halt();
        this.BeginNextOrReturn(stage);
    }

    private void BeginExitHouse(ActiveAnimalCareStage stage)
    {
        AnimalHouseRoutePlan route = stage.CurrentHouseRoute
            ?? throw new InvalidOperationException("Animal-house exit has no active building.");
        stage.CurrentTarget = null;
        stage.CurrentFeedingTarget = null;
        stage.CurrentProductTarget = null;
        stage.CurrentProductDestination = null;
        Stack<Point>? path = this.Planner.TryCreateReturnPath(
            stage.WorkLocation,
            stage.Context.Lease.Worker,
            route.InteriorEntryTile,
            stage.RouteObstacles);
        if (path is null)
        {
            this.Complete(stage, false, "contract.failure.return-unreachable");
            return;
        }
        stage.Phase = AnimalCarePhase.TravelingToHouseExit;
        stage.PhaseTicks = 0;
        if (stage.Context.Lease.Worker.TilePoint == route.InteriorEntryTile)
        {
            this.OnArrivedAtHouseExit(stage.Context.Lease.Worker, stage.WorkLocation);
            return;
        }
        try
        {
            if (!FarmNavigationMap.CanBeginPath(
                    stage.WorkLocation,
                    stage.Context.Lease.Worker,
                    stage.Context.Lease.Worker.TilePoint,
                    path,
                    out string failure))
            {
                this.HandleTravelInterruption(
                    stage,
                    TravelInterruptionKind.FirstStepRejected,
                    path,
                    failure);
                return;
            }
            stage.Controller = this.CreateController(
                stage,
                path,
                route.InteriorEntryTile,
                Game1.down,
                this.OnArrivedAtHouseExit);
            stage.Context.Lease.AttachController(stage.Controller);
        }
        catch (Exception ex)
        {
            this.HandleTravelInterruption(
                stage,
                TravelInterruptionKind.ControllerSetupFailed,
                path,
                ex.Message);
        }
    }

    private void OnArrivedAtHouseExit(Character character, GameLocation location)
    {
        if (this.ActiveStage is not { CurrentHouseRoute: { } route } stage
            || !ReferenceEquals(character, stage.Context.Lease.Worker)
            || !ReferenceEquals(location, route.House)
            || stage.Phase != AnimalCarePhase.TravelingToHouseExit)
            return;

        route.House.playSound("doorClose", route.InteriorEntryTile.ToVector2());
        Game1.warpCharacter(
            stage.Context.Lease.Worker,
            stage.Farm,
            route.ExteriorInteractionTile.ToVector2());
        stage.Context.Lease.Worker.Position =
            FarmNavigationMap.GetAlignedCharacterPosition(route.ExteriorInteractionTile);
        if (!ReferenceEquals(stage.Context.Lease.Worker.currentLocation, stage.Farm)
            || !stage.Farm.characters.Contains(stage.Context.Lease.Worker))
        {
            this.Complete(stage, false, "contract.failure.dispatch");
            return;
        }
        stage.WorkLocation = stage.Farm;
        stage.VisitedBuildingIds.Add(route.BuildingId);
        stage.AttemptedTroughTiles.Clear();
        stage.CurrentFeedingTarget = null;
        stage.CurrentProductTarget = null;
        stage.CurrentProductDestination = null;
        stage.CurrentHouseRoute = null;
        stage.Context.Lease.Worker.Halt();
        this.BeginNextOrReturn(stage);
    }

    private bool HasControllerConflict(ActiveAnimalCareStage stage)
    {
        if (stage.Context.Lease.Worker.controller is null
            || ReferenceEquals(stage.Context.Lease.Worker.controller, stage.Controller))
            return false;
        this.LogControllerConflict(stage);
        this.Complete(stage, false, "contract.failure.controller-conflict");
        return true;
    }

    private enum AnimalCarePhase
    {
        Traveling,
        Acting,
        WaitingForProductChestLock,
        TravelingToBuilding,
        TravelingToHouseExit,
        Returning,
        Returned
    }

    private sealed class ActiveAnimalCareStage
    {
        public ActiveAnimalCareStage(FarmWorkShiftContext context, Farm farm, AnimalPettingWorkPlan plan)
        {
            this.Context = context;
            this.Farm = farm;
            this.Plan = plan;
            this.CurrentTarget = plan.FirstTarget;
            this.WorkLocation = farm;
        }
        public FarmWorkShiftContext Context { get; }
        public Farm Farm { get; }
        public AnimalPettingWorkPlan Plan { get; }
        public AnimalPettingTargetPlan? CurrentTarget { get; set; }
        public AnimalFeedingTargetPlan? CurrentFeedingTarget { get; set; }
        public AnimalProductTargetPlan? CurrentProductTarget { get; set; }
        public AnimalProductChestDestination? CurrentProductDestination { get; set; }
        public GameLocation WorkLocation { get; set; }
        public AnimalHouseRoutePlan? CurrentHouseRoute { get; set; }
        public HashSet<Guid> VisitedBuildingIds { get; } = new();
        public HashSet<Point> AttemptedTroughTiles { get; } = new();
        public HashSet<long> AttemptedAnimalIds { get; } = new();
        public HashSet<string> AttemptedProductTargets { get; } = new(StringComparer.Ordinal);
        public TravelProgressWatchdog TravelWatchdog { get; } = new();
        public TravelObstacleLedger RouteObstacles { get; } = new();
        public TravelFailureLedger RouteFailures { get; } = new(MaximumRouteFailures);
        public List<NamedContractCargoState> ProducedItems { get; } = new();
        public HashSet<string> CompletedTransferIds { get; } = new(StringComparer.Ordinal);
        public AnimalCarePhase Phase { get; set; }
        public PathFindController? Controller { get; set; }
        public int PhaseTicks { get; set; }
        public bool ActionApplied { get; set; }
        public int PettedAnimals { get; set; }
        public int FilledTroughs { get; set; }
        public int CollectedProductTargets { get; set; }
        public int PlayerItems { get; set; }
        public int ChestItems { get; set; }
    }
}
