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
    private ActiveAnimalCareStage? ActiveStage;
    private NamedContractCompletionState? LastCompletion;

    public AnimalCareContractExecutionController(IMonitor monitor)
    {
        this.Monitor = monitor;
        this.Planner = new AnimalPettingTargetPlanner(monitor);
    }

    public string? LastStartFailureKey { get; private set; }

    public bool TryStartManaged(FarmWorkShiftContext shift)
    {
        this.LastStartFailureKey = null;
        if (this.ActiveStage is not null)
            return this.FailStart("contract.start.already-active");

        Farm farm = Game1.getFarm();
        AnimalPettingWorkPlan? plan = this.Planner.TryCreate(farm, shift.Lease.Worker);
        if (plan is null)
            return this.FailStart("animal-care.start.no-work");

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
            this.BeginTravel(stage, plan.FirstTarget);
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
                if (this.TryFinishTravel(stage, stage.CurrentTarget.InteractionTile, this.OnArrivedAtAnimal))
                    return;
                if (stage.PhaseTicks > MaximumTravelTicks || stage.Context.Lease.Worker.controller is null)
                    this.SkipCurrentAndContinue(stage);
                break;
            case AnimalCarePhase.Acting:
                if (!stage.ActionApplied && stage.PhaseTicks >= ActionStartTicks)
                {
                    stage.ActionApplied = true;
                    stage.AttemptedAnimalIds.Add(stage.CurrentTarget.AnimalId);
                    if (this.TryPetAnimal(stage))
                        stage.PettedAnimals++;
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
            stage.Plan.ArrivalSide, 0, stage.CurrentTarget.TargetTile.X,
            stage.CurrentTarget.TargetTile.Y, stage.Context.BillingPreview.MaximumAuthorizedWage,
            stage.Context.Lease.StartTime, stage.PettedAnimals,
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
        stage.ActionApplied = false;
        stage.Phase = AnimalCarePhase.Traveling;
        stage.PhaseTicks = 0;
        NPC worker = stage.Context.Lease.Worker;
        if (worker.TilePoint == target.InteractionTile)
        {
            this.OnArrivedAtAnimal(worker, stage.Farm);
            return;
        }
        if (!FarmNavigationMap.CanBeginPath(
                stage.Farm, worker, worker.TilePoint, target.Path, out string failure))
            throw new InvalidOperationException($"Animal-care first step is unsafe: {failure}.");
        stage.Controller = this.CreateController(stage, target.Path, target.InteractionTile,
            target.FacingDirection, this.OnArrivedAtAnimal);
        stage.Context.Lease.AttachController(stage.Controller);
    }

    private void OnArrivedAtAnimal(Character character, GameLocation location)
    {
        if (this.ActiveStage is not { } stage
            || !ReferenceEquals(character, stage.Context.Lease.Worker)
            || !ReferenceEquals(location, stage.Farm))
            return;
        stage.Phase = AnimalCarePhase.Acting;
        stage.PhaseTicks = 0;
        stage.Context.Lease.Worker.Halt();
        stage.Context.Lease.Worker.faceDirection(stage.CurrentTarget.FacingDirection);
    }

    private bool TryPetAnimal(ActiveAnimalCareStage stage)
    {
        if (!stage.Farm.animals.TryGetValue(stage.CurrentTarget.AnimalId, out FarmAnimal? animal)
            || !ReferenceEquals(animal.currentLocation, stage.Farm)
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
        stage.AttemptedAnimalIds.Add(stage.CurrentTarget.AnimalId);
        stage.Context.Lease.Worker.controller = null;
        stage.Controller = null;
        this.BeginNextOrReturn(stage);
    }

    private void BeginNextOrReturn(ActiveAnimalCareStage stage)
    {
        AnimalPettingTargetPlan? next = this.Planner.TryFindNext(
            stage.Farm, stage.Context.Lease.Worker,
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

    private void OnReturned(Character character, GameLocation location)
    {
        if (this.ActiveStage is not { } stage
            || !ReferenceEquals(character, stage.Context.Lease.Worker)
            || !ReferenceEquals(location, stage.Farm))
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
        callback(worker, stage.Farm);
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
            new Stack<Point>(path.Reverse()), stage.Farm,
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
            NamedFarmTask.FarmWork, succeeded, reason, stage.PettedAnimals,
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

    private enum AnimalCarePhase { Traveling, Acting, Returning, Returned }

    private sealed class ActiveAnimalCareStage
    {
        public ActiveAnimalCareStage(FarmWorkShiftContext context, Farm farm, AnimalPettingWorkPlan plan)
        {
            this.Context = context;
            this.Farm = farm;
            this.Plan = plan;
            this.CurrentTarget = plan.FirstTarget;
        }
        public FarmWorkShiftContext Context { get; }
        public Farm Farm { get; }
        public AnimalPettingWorkPlan Plan { get; }
        public AnimalPettingTargetPlan CurrentTarget { get; set; }
        public HashSet<long> AttemptedAnimalIds { get; } = new();
        public AnimalCarePhase Phase { get; set; }
        public PathFindController? Controller { get; set; }
        public int PhaseTicks { get; set; }
        public bool ActionApplied { get; set; }
        public int PettedAnimals { get; set; }
    }
}
