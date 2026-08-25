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
    private readonly IMonitor Monitor;
    private readonly AnimalPettingTargetPlanner Planner;
    private readonly AnimalHouseRoutePlanner HousePlanner;
    private readonly AnimalFeedingTargetPlanner FeedingPlanner;
    private ActiveAnimalCareStage? ActiveStage;
    private NamedContractCompletionState? LastCompletion;

    public AnimalCareContractExecutionController(IMonitor monitor)
    {
        this.Monitor = monitor;
        this.Planner = new AnimalPettingTargetPlanner(monitor);
        this.HousePlanner = new AnimalHouseRoutePlanner(monitor);
        this.FeedingPlanner = new AnimalFeedingTargetPlanner(monitor);
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
                    this.Complete(stage, false, "contract.failure.controller-conflict");
                    return;
                }
                if (this.TryFinishTravel(stage, GetCurrentInteractionTile(stage), this.OnArrivedAtWork))
                    return;
                if (stage.PhaseTicks > MaximumTravelTicks || stage.Context.Lease.Worker.controller is null)
                    this.SkipCurrentAndContinue(stage);
                break;
            case AnimalCarePhase.TravelingToBuilding:
                if (this.HasControllerConflict(stage))
                    return;
                if (this.TryFinishTravel(
                        stage,
                        stage.CurrentHouseRoute!.ExteriorInteractionTile,
                        this.OnArrivedAtBuilding))
                    return;
                if (stage.PhaseTicks > MaximumTravelTicks
                    || stage.Context.Lease.Worker.controller is null)
                {
                    stage.VisitedBuildingIds.Add(stage.CurrentHouseRoute!.BuildingId);
                    stage.CurrentHouseRoute = null;
                    this.BeginNextOrReturn(stage);
                }
                break;
            case AnimalCarePhase.TravelingToHouseExit:
                if (this.HasControllerConflict(stage))
                    return;
                if (this.TryFinishTravel(
                        stage,
                        stage.CurrentHouseRoute!.InteriorEntryTile,
                        this.OnArrivedAtHouseExit))
                    return;
                if (stage.PhaseTicks > MaximumTravelTicks
                    || stage.Context.Lease.Worker.controller is null)
                    this.Complete(stage, false, "contract.failure.return-unreachable");
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
            case AnimalCarePhase.Returning:
                if (stage.Context.Lease.Worker.controller is not null
                    && !ReferenceEquals(stage.Context.Lease.Worker.controller, stage.Controller))
                {
                    this.Complete(stage, false, "contract.failure.controller-conflict");
                    return;
                }
                if (this.TryFinishTravel(stage, stage.Plan.ArrivalTile, this.OnReturned))
                    return;
                if (stage.PhaseTicks > MaximumTravelTicks || stage.Context.Lease.Worker.controller is null)
                    this.Complete(stage, false, "contract.failure.return-unreachable");
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
            stage.Plan.ArrivalSide, 0, stage.CurrentTarget?.TargetTile.X ?? stage.Plan.ArrivalTile.X,
            stage.CurrentTarget?.TargetTile.Y ?? stage.Plan.ArrivalTile.Y, stage.Context.BillingPreview.MaximumAuthorizedWage,
            stage.Context.Lease.StartTime, stage.PettedAnimals + stage.FilledTroughs,
            Array.Empty<NamedContractCargoState>(), Array.Empty<string>());
    }

    public NamedContractCompletionState? ConsumeCompletion()
    {
        NamedContractCompletionState? completion = this.LastCompletion;
        this.LastCompletion = null;
        return completion;
    }

    public void OnReturnedToTitle()
    {
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
            throw new InvalidOperationException($"Animal-care first step is unsafe: {failure}.");
        stage.Controller = this.CreateController(stage, target.Path, target.InteractionTile,
            target.FacingDirection, this.OnArrivedAtWork);
        stage.Context.Lease.AttachController(stage.Controller);
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
        else
            stage.AttemptedAnimalIds.Add(stage.CurrentTarget!.AnimalId);
        stage.Context.Lease.Worker.controller = null;
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
                stage.AttemptedTroughTiles);
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
            stage.Context.Lease.Worker.TilePoint, stage.AttemptedAnimalIds);
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

        if (stage.WorkLocation is AnimalHouse)
        {
            this.BeginExitHouse(stage);
            return;
        }

        if (this.TryBeginNextHouse(stage))
            return;

        Stack<Point>? path = this.Planner.TryCreateReturnPath(
            stage.Farm, stage.Context.Lease.Worker, stage.Plan.ArrivalTile);
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
                throw new InvalidOperationException($"Animal-care return step is unsafe: {failure}.");
            stage.Controller = this.CreateController(stage, path, stage.Plan.ArrivalTile,
                Game1.down, this.OnReturned);
            stage.Context.Lease.AttachController(stage.Controller);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Animal-care return route failed: {ex.Message}", LogLevel.Warn);
            this.Complete(stage, false, "contract.failure.return-unreachable");
        }
    }

    private void BeginFeedingTravel(
        ActiveAnimalCareStage stage,
        AnimalFeedingTargetPlan target)
    {
        stage.CurrentTarget = null;
        stage.CurrentFeedingTarget = target;
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
            throw new InvalidOperationException($"Feeding first step is unsafe: {failure}.");
        stage.Controller = this.CreateController(
            stage,
            target.Path,
            target.InteractionTile,
            target.FacingDirection,
            this.OnArrivedAtWork);
        stage.Context.Lease.AttachController(stage.Controller);
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

    private static Point GetCurrentInteractionTile(ActiveAnimalCareStage stage)
    {
        return stage.CurrentFeedingTarget?.InteractionTile
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
        return controller;
    }

    private void Complete(ActiveAnimalCareStage stage, bool succeeded, string reason)
    {
        if (!ReferenceEquals(this.ActiveStage, stage))
            return;
        stage.Context.Lease.Worker.controller = null;
        this.ActiveStage = null;
        this.LastCompletion = new NamedContractCompletionState(
            stage.Context.Id.ToString("N"), stage.Context.RequestId,
            stage.Context.Requester.UniqueMultiplayerID, stage.Context.Lease.Worker.Name,
            NamedFarmTask.FarmWork, succeeded, reason, stage.PettedAnimals + stage.FilledTroughs,
            0, 0, 0, 0, 0, 0, 0, 0,
            Array.Empty<NamedContractCargoState>(), Array.Empty<string>(),
            Array.Empty<NamedContractTransferState>(), Array.Empty<NamedContractTransferState>());
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
            stage.VisitedBuildingIds);
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
                throw new InvalidOperationException($"Animal-house first step is unsafe: {failure}.");
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
            this.Monitor.Log(
                $"Animal-house route {route.BuildingId:N} failed: {ex.Message}",
                LogLevel.Warn);
            stage.VisitedBuildingIds.Add(route.BuildingId);
            stage.CurrentHouseRoute = null;
            return this.TryBeginNextHouse(stage);
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
        Stack<Point>? path = this.Planner.TryCreateReturnPath(
            stage.WorkLocation,
            stage.Context.Lease.Worker,
            route.InteriorEntryTile);
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
                throw new InvalidOperationException($"Animal-house exit step is unsafe: {failure}.");
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
            this.Monitor.Log($"Animal-house exit failed: {ex.Message}", LogLevel.Warn);
            this.Complete(stage, false, "contract.failure.return-unreachable");
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
        stage.CurrentHouseRoute = null;
        stage.Context.Lease.Worker.Halt();
        this.BeginNextOrReturn(stage);
    }

    private bool HasControllerConflict(ActiveAnimalCareStage stage)
    {
        if (stage.Context.Lease.Worker.controller is null
            || ReferenceEquals(stage.Context.Lease.Worker.controller, stage.Controller))
            return false;
        this.Complete(stage, false, "contract.failure.controller-conflict");
        return true;
    }

    private enum AnimalCarePhase
    {
        Traveling,
        Acting,
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
        public GameLocation WorkLocation { get; set; }
        public AnimalHouseRoutePlan? CurrentHouseRoute { get; set; }
        public HashSet<Guid> VisitedBuildingIds { get; } = new();
        public HashSet<Point> AttemptedTroughTiles { get; } = new();
        public HashSet<long> AttemptedAnimalIds { get; } = new();
        public AnimalCarePhase Phase { get; set; }
        public PathFindController? Controller { get; set; }
        public int PhaseTicks { get; set; }
        public bool ActionApplied { get; set; }
        public int PettedAnimals { get; set; }
        public int FilledTroughs { get; set; }
    }
}
