using EvilFarmOwner;
using System.Runtime.CompilerServices;
using System.Text.Json;

List<(string Name, Action Test)> tests = new()
{
    ("regular high-risk wage", TestRegularHighRiskWage),
    ("rest-day triple wage", TestRestDayTripleWage),
    ("trusted wage", TestTrustedWage),
    ("worker efficiency profile coverage", TestWorkerEfficiencyProfileCoverage),
    ("worker task-specific efficiency", TestWorkerTaskSpecificEfficiency),
    ("worker efficiency fallback", TestWorkerEfficiencyFallback),
    ("worker efficiency timing", TestWorkerEfficiencyTiming),
    ("worker efficiency contract snapshot", TestWorkerEfficiencyContractSnapshot),
    ("farm-work stage order", TestFarmWorkStageOrder),
    ("deterministic workforce partition", TestDeterministicWorkforcePartition),
    ("workforce claim ownership", TestWorkforceClaimOwnership),
    ("workforce final reconciliation", TestWorkforceFinalReconciliation),
    ("workforce settlement aggregation", TestWorkforceSettlementAggregation),
    ("animal petting idempotency", TestAnimalPettingIdempotency),
    ("animal petting route ordering", TestAnimalPettingRouteOrdering),
    ("animal finite hay conservation", TestAnimalFiniteHayConservation),
    ("animal tool produce readiness", TestAnimalToolProduceReadiness),
    ("animal loose product ownership", TestAnimalLooseProductOwnership),
    ("animal product route ordering", TestAnimalProductRouteOrdering),
    ("animal product commit preflight", TestAnimalProductCommitPreflight),
    ("recurring contract state validation", TestRecurringContractStateValidation),
    ("recurring contract candidate pool", TestRecurringContractCandidatePool),
    ("recurring contract ranking", TestRecurringContractRanking),
    ("recurring contract budget gates", TestRecurringContractBudgetGates),
    ("recurring contract daily idempotency", TestRecurringContractDailyIdempotency),
    ("recurring contract persistence", TestRecurringContractPersistence),
    ("recurring contract legacy upgrade", TestRecurringContractLegacyUpgrade),
    ("pre-dispatch settlement", TestPreDispatchSettlement),
    ("dispatched one-hour settlement", TestDispatchedSettlement),
    ("elapsed multi-hour settlement", TestElapsedMultiHourSettlement),
    ("target ordering", TestTargetOrdering),
    ("dry crop eligibility", TestDryCropEligibility),
    ("actual path cost ordering", TestActualPathCostOrdering),
    ("trellis detour route", TestTrellisDetourRoute),
    ("failed interaction edge isolation", TestFailedInteractionEdgeIsolation),
    ("controller path skips current tile", TestControllerPathSkipsCurrentTile),
    ("destination tile alignment", TestDestinationTileAlignment),
    ("travel progress watchdog", TestTravelProgressWatchdog),
    ("consecutive route failure budget", TestConsecutiveRouteFailureBudget),
    ("target route failure isolation", TestTargetRouteFailureIsolation),
    ("NPC protected activity policy", TestNpcProtectedActivityPolicy),
    ("path first-step offsets", TestPathFirstStepOffsets),
    ("external boundary arrival ordering", TestExternalBoundaryArrivalOrdering),
    ("right entrance priority", TestRightEntrancePriority),
    ("stalled entrance fallback ordering", TestStalledEntranceFallbackOrdering),
    ("nearest arrival boundary side", TestNearestArrivalBoundarySide),
    ("NPC lease recovery policy", TestNpcLeaseRecoveryPolicy),
    ("six-hour wage cap", TestSixHourWageCap),
    ("harvest chest classification", TestHarvestChestClassification),
    ("harvest category purity", TestHarvestCategoryPurity),
    ("harvest incompatible chest exclusion", TestHarvestIncompatibleChestExclusion),
    ("harvest chest full acceptance", TestHarvestChestFullAcceptance),
    ("harvest stable category destination", TestHarvestStableCategoryDestination),
    ("harvest empty chest capacity fallback", TestHarvestEmptyChestCapacityFallback),
    ("harvest chest route attempt isolation", TestHarvestChestRouteAttemptIsolation),
    ("harvest contract destination policy", TestHarvestContractDestinationPolicy),
    ("storage sort classification priority", TestStorageSortClassificationPriority),
    ("storage sort category purity", TestStorageSortCategoryPurity),
    ("storage sort stable tie", TestStorageSortStableTie),
    ("storage sort capacity preflight", TestStorageSortCapacityPreflight),
    ("storage sort idempotence", TestStorageSortIdempotence),
    ("storage sort conservation", TestStorageSortConservation),
    ("storage sort invalid snapshot", TestStorageSortInvalidSnapshot),
    ("storage sort generated invariants", TestStorageSortGeneratedInvariants),
    ("storage snapshot validation", TestStorageSnapshotValidation),
    ("storage transfer lock order", TestStorageTransferLockOrder),
    ("storage transfer sequence", TestStorageTransferSequence),
    ("storage transfer conservation", TestStorageTransferConservation),
    ("storage transfer recovery ownership", TestStorageTransferRecoveryOwnership),
    ("storage sort interaction ordering", TestStorageSortInteractionOrdering),
    ("storage sort report accounting", TestStorageSortReportAccounting),
    ("storage sort save-boundary policy", TestStorageSortSaveBoundaryPolicy),
    ("harvest partial remainder", TestHarvestPartialRemainder),
    ("harvest chest release deferral", TestHarvestChestReleaseDeferral),
    ("regrowing harvest capture semantics", TestRegrowingHarvestCaptureSemantics),
    ("ready tapper target semantics", TestReadyTapperTargetSemantics),
    ("ready fruit-tree target semantics", TestReadyFruitTreeTargetSemantics),
    ("ready machine target semantics", TestReadyMachineTargetSemantics),
    ("ready crab-pot target semantics", TestReadyCrabPotTargetSemantics),
    ("ready fish-pond target semantics", TestReadyFishPondTargetSemantics),
    ("ready bush target semantics", TestReadyBushTargetSemantics),
    ("harvest unavailable storage stop", TestHarvestUnavailableStorageStop),
    ("harvest transfer replay protection", TestHarvestTransferReplayProtection),
    ("harvest placement conservation", TestHarvestPlacementConservation),
    ("harvest quarantine recovery state", TestHarvestQuarantineRecoveryState),
    ("harvest acceptance fault controls", TestHarvestAcceptanceFaultControls),
    ("emergency drop tile ordering", TestEmergencyDropTileOrdering),
    ("multiplayer request authorization", TestMultiplayerRequestAuthorization),
    ("multiplayer request replay", TestMultiplayerRequestReplay),
    ("multiplayer deterministic order", TestMultiplayerDeterministicOrder),
    ("multiplayer reconnect ledger", TestMultiplayerReconnectLedger),
    ("multiplayer restart recovery state", TestMultiplayerRestartRecoveryState),
    ("multiplayer host session handshake", TestMultiplayerHostSessionHandshake),
    ("multiplayer sync handshake serialization", TestMultiplayerSyncHandshakeSerialization),
    ("multiplayer stale snapshot rejection", TestMultiplayerStaleSnapshotRejection),
    ("multiplayer stale sync-state rejection", TestMultiplayerStaleSyncStateRejection),
    ("multiplayer snapshot serialization", TestMultiplayerSnapshotSerialization),
    ("multiplayer result serialization", TestMultiplayerResultSerialization),
    ("multiplayer storage result validation", TestMultiplayerStorageResultValidation),
    ("named contract report grouping", TestNamedContractReportGrouping)
};

int failures = 0;
foreach ((string name, Action test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

Console.WriteLine($"Executed {tests.Count} deterministic logic tests; failures={failures}.");
return failures == 0 ? 0 : 1;

static void TestRegularHighRiskWage()
{
    WorkContractPreview preview = ContractPreviewService.Create(friendshipHearts: 0, dayOfMonth: 1);
    Equal(ContractDayKind.RegularWorkday, preview.DayKind);
    Equal(120, preview.MinimumCalloutWage);
    Equal(720, preview.MaximumAuthorizedWage);
}

static void TestRestDayTripleWage()
{
    WorkContractPreview preview = ContractPreviewService.Create(friendshipHearts: 0, dayOfMonth: 6);
    Equal(ContractDayKind.RestDay, preview.DayKind);
    Equal(3.00m, preview.DayMultiplier);
    Equal(360, preview.MinimumCalloutWage);
    Equal(2160, preview.MaximumAuthorizedWage);
}

static void TestTrustedWage()
{
    WorkContractPreview preview = ContractPreviewService.Create(friendshipHearts: 8, dayOfMonth: 2);
    Equal(FriendshipWageBand.Trusted, preview.FriendshipBand);
    Equal(90, preview.MinimumCalloutWage);
    Equal(540, preview.MaximumAuthorizedWage);
}

static void TestWorkerEfficiencyProfileCoverage()
{
    IReadOnlyCollection<WorkerEfficiencyProfile> profiles =
        WorkerEfficiencyProfiles.GetExplicitProfiles();
    Equal(27, profiles.Count);
    Equal(27, profiles.Select(profile => profile.WorkerName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    Equal(true, profiles.All(profile =>
        WorkerEfficiencyProfiles.IsValidMultiplier(profile.WateringMultiplier)
        && WorkerEfficiencyProfiles.IsValidMultiplier(profile.HarvestingMultiplier)));
    Equal(false, WorkerEfficiencyProfiles.IsValidMultiplier(0.99m));
    Equal(false, WorkerEfficiencyProfiles.IsValidMultiplier(1.11m));
}

static void TestWorkerTaskSpecificEfficiency()
{
    WorkerEfficiencyProfile alex = WorkerEfficiencyProfiles.GetProfile("Alex");
    Equal(1.10m, alex.GetMultiplier(NamedFarmTask.Watering));
    Equal(1.05m, alex.GetMultiplier(NamedFarmTask.Harvesting));

    WorkerEfficiencyProfile leah = WorkerEfficiencyProfiles.GetProfile("leah");
    Equal(1.05m, leah.GetMultiplier(NamedFarmTask.Watering));
    Equal(1.10m, leah.GetMultiplier(NamedFarmTask.Harvesting));
}

static void TestWorkerEfficiencyFallback()
{
    WorkerEfficiencyProfile custom = WorkerEfficiencyProfiles.GetProfile("ExampleCustomNpc");
    Equal(WorkerEfficiencyBackground.Baseline, custom.Background);
    Equal(1.00m, custom.GetMultiplier(NamedFarmTask.Watering));
    Equal(1.00m, custom.GetMultiplier(NamedFarmTask.Harvesting));
}

static void TestWorkerEfficiencyTiming()
{
    Equal(36, WorkerEfficiencyTiming.GetActionDurationTicks(36, 8, 1.00m));
    Equal(35, WorkerEfficiencyTiming.GetActionDurationTicks(36, 8, 1.05m));
    Equal(33, WorkerEfficiencyTiming.GetActionDurationTicks(36, 8, 1.10m));
    Equal(37, WorkerEfficiencyTiming.GetActionDurationTicks(40, 8, 1.10m));
    Equal(36, WorkerEfficiencyTiming.GetActionDurationTicks(36, 8, 0.00m));
    Equal(9, WorkerEfficiencyTiming.GetActionDurationTicks(9, 8, 1.10m));
}

static void TestWorkerEfficiencyContractSnapshot()
{
    WorkContractPreview first = ContractPreviewService.Create(
        friendshipHearts: 4,
        dayOfMonth: 1,
        workerName: "Alex",
        task: NamedFarmTask.Watering);
    WorkContractPreview second = ContractPreviewService.Create(
        friendshipHearts: 4,
        dayOfMonth: 1,
        workerName: "Alex",
        task: NamedFarmTask.Watering);
    WorkContractPreview harvesting = ContractPreviewService.Create(
        friendshipHearts: 4,
        dayOfMonth: 1,
        workerName: "Alex",
        task: NamedFarmTask.Harvesting);

    Equal(first, second);
    Equal(1.10m, first.EfficiencyMultiplier);
    Equal(WorkerEfficiencyBackground.ManualFieldwork, first.EfficiencyBackground);
    Equal(1.05m, harvesting.EfficiencyMultiplier);
    Equal(first.MaximumAuthorizedWage, harvesting.MaximumAuthorizedWage);

    WorkContractPreview sorting = ContractPreviewService.Create(
        friendshipHearts: 4,
        dayOfMonth: 1,
        workerName: "Alex",
        task: NamedFarmTask.StorageSorting);
    Equal(1.00m, sorting.EfficiencyMultiplier);
    Equal(WorkerEfficiencyBackground.Baseline, sorting.EfficiencyBackground);
    Equal(first.MaximumAuthorizedWage, sorting.MaximumAuthorizedWage);
}

static void TestFarmWorkStageOrder()
{
    Equal(FarmWorkStage.Harvesting, FarmWorkStagePolicy.GetNext(null));
    Equal(FarmWorkStage.Watering, FarmWorkStagePolicy.GetNext(FarmWorkStage.Harvesting));
    Equal(FarmWorkStage.AnimalCare, FarmWorkStagePolicy.GetNext(FarmWorkStage.Watering));
    Equal(FarmWorkStage.StorageSorting, FarmWorkStagePolicy.GetNext(FarmWorkStage.AnimalCare));
    Equal(FarmWorkStage.Complete, FarmWorkStagePolicy.GetNext(FarmWorkStage.StorageSorting));
    Equal(true, FarmWorkStagePolicy.IsEmptyStageFailure(
        FarmWorkStage.Harvesting,
        "harvest.start.no-mature-crop"));
    Equal(true, FarmWorkStagePolicy.IsEmptyStageFailure(
        FarmWorkStage.Watering,
        "contract.start.no-dry-crop"));
    Equal(true, FarmWorkStagePolicy.IsEmptyStageFailure(
        FarmWorkStage.AnimalCare,
        "animal-care.start.no-work"));
    Equal(true, FarmWorkStagePolicy.IsEmptyStageFailure(
        FarmWorkStage.StorageSorting,
        "storage-sort.start.no-work"));
    Equal(false, FarmWorkStagePolicy.IsEmptyStageFailure(
        FarmWorkStage.Harvesting,
        "harvest.start.no-reachable-crop"));

    Equal(FarmWorkPassPolicy.MaximumPasses, 2);
    Equal(true, FarmWorkPassPolicy.TryGetNext(
        FarmWorkPass.Initial,
        out FarmWorkPass reconciliation));
    Equal(FarmWorkPass.Reconciliation, reconciliation);
    Equal(false, FarmWorkPassPolicy.TryGetNext(
        FarmWorkPass.Reconciliation,
        out FarmWorkPass terminal));
    Equal(FarmWorkPass.Reconciliation, terminal);
    Equal(8, FarmWorkPassPolicy.OrderedSteps.Count);
    Equal(
        (FarmWorkPass.Initial, FarmWorkStage.Harvesting),
        FarmWorkPassPolicy.OrderedSteps[0]);
    Equal(
        (FarmWorkPass.Initial, FarmWorkStage.StorageSorting),
        FarmWorkPassPolicy.OrderedSteps[3]);
    Equal(
        (FarmWorkPass.Reconciliation, FarmWorkStage.Harvesting),
        FarmWorkPassPolicy.OrderedSteps[4]);
    Equal(
        (FarmWorkPass.Reconciliation, FarmWorkStage.StorageSorting),
        FarmWorkPassPolicy.OrderedSteps[7]);
    Equal(
        "Watering/Reconciliation/Acting",
        FarmWorkPassPolicy.FormatRuntimePhase(
            FarmWorkStage.Watering,
            FarmWorkPass.Reconciliation,
            "Acting"));
    Throws<ArgumentException>(() => FarmWorkPassPolicy.FormatRuntimePhase(
        FarmWorkStage.Harvesting,
        FarmWorkPass.Initial,
        " "));
}

static void TestDeterministicWorkforcePartition()
{
    SchedulableWorker[] workers =
    {
        new("Leah", 1m),
        new("Alex", 2m)
    };
    SchedulableWorkTarget[] targets =
    {
        new(new("Harvest", "Farm", "C"), 10),
        new(new("Harvest", "Farm", "A"), 10),
        new(new("Water", "Farm", "B"), 10)
    };

    IReadOnlyList<WorkerTargetAssignment> first =
        DeterministicWorkforceScheduler.Partition(workers, targets);
    IReadOnlyList<WorkerTargetAssignment> second =
        DeterministicWorkforceScheduler.Partition(workers.Reverse(), targets.Reverse());

    Equal("Alex", first[0].WorkerId);
    Equal("Leah", first[1].WorkerId);
    Equal(string.Join(",", first[0].Targets.Select(target => target.TargetId)),
        string.Join(",", second[0].Targets.Select(target => target.TargetId)));
    Equal(string.Join(",", first[1].Targets.Select(target => target.TargetId)),
        string.Join(",", second[1].Targets.Select(target => target.TargetId)));
    Equal(3, first.Sum(assignment => assignment.Targets.Count));

    IReadOnlyList<WorkerTargetAssignment> single =
        DeterministicWorkforceScheduler.Partition(new[] { workers[0] }, targets);
    Equal(3, single[0].Targets.Count);
}

static void TestWorkforceClaimOwnership()
{
    WorkTargetIdentity first = new("Harvest", "Farm", "crop-1");
    WorkTargetIdentity second = new("Water", "Farm", "crop-2");
    DeterministicWorkClaimLedger ledger = new();

    Equal(true, ledger.TryClaim(first, "Alex"));
    Equal(false, ledger.TryClaim(first, "Leah"));
    Equal(true, ledger.TryCommit(first, "Alex"));
    Equal(true, ledger.TryClaim(second, "Alex"));
    Equal(1, ledger.ReleaseUncommitted("Alex"));
    Equal(1, ledger.Snapshot().Count);
    Equal(WorkClaimState.Committed, ledger.Snapshot()[0].State);
    Equal(false, ledger.TryClaim(first, "Leah"));
    Equal(true, ledger.TryClaim(second, "Leah"));
}

static void TestWorkforceFinalReconciliation()
{
    WorkTargetIdentity committed = new("Harvest", "Farm", "crop-1");
    WorkTargetIdentity interrupted = new("Water", "Farm", "crop-2");
    DeterministicWorkClaimLedger ledger = new();
    Equal(true, ledger.TryClaim(committed, "Alex"));
    Equal(true, ledger.TryCommit(committed, "Alex"));
    Equal(true, ledger.TryClaim(interrupted, "Alex"));
    Equal(1, ledger.ReleaseUncommitted("Alex"));

    SchedulableWorkTarget[] discovered =
    {
        new(committed, 10),
        new(interrupted, 10)
    };
    SchedulableWorkTarget[] available = discovered
        .Where(target => !ledger.IsClaimed(target.Identity))
        .ToArray();
    IReadOnlyList<WorkerTargetAssignment> reconciled =
        DeterministicWorkforceScheduler.Partition(
            new[] { new SchedulableWorker("Leah", 1m) },
            available);

    Equal(1, reconciled[0].Targets.Count);
    Equal("crop-2", reconciled[0].Targets[0].TargetId);
}

static void TestWorkforceSettlementAggregation()
{
    Equal(900, WorkforceSettlementPolicy.GetAggregateCharge(new[]
    {
        new WorkerWageSettlement("Alex", 700, 600),
        new WorkerWageSettlement("Leah", 400, 300)
    }));
    Throws<ArgumentException>(() => WorkforceSettlementPolicy.GetAggregateCharge(new[]
    {
        new WorkerWageSettlement("Alex", 700, 600),
        new WorkerWageSettlement("Alex", 400, 300)
    }));
}

static void TestAnimalPettingIdempotency()
{
    Equal(AnimalCareSkipReason.None,
        AnimalPettingPolicy.GetSkipReason(true, false, false));
    Equal(AnimalCareSkipReason.AlreadyPet,
        AnimalPettingPolicy.GetSkipReason(true, true, false));
    Equal(AnimalCareSkipReason.Sleeping,
        AnimalPettingPolicy.GetSkipReason(true, false, true));
    Equal(AnimalCareSkipReason.NotHost,
        AnimalPettingPolicy.GetSkipReason(false, false, false));
    Equal(new ManualPetGains(15, 30),
        AnimalPettingPolicy.GetManualPetGains(false, false, 0));
    Equal(new ManualPetGains(7, 30),
        AnimalPettingPolicy.GetManualPetGains(true, false, 0));
    Equal(new ManualPetGains(30, 60),
        AnimalPettingPolicy.GetManualPetGains(false, true, 0));
    Equal(new ManualPetGains(30, 10),
        AnimalPettingPolicy.GetManualPetGains(false, true, -40));
}

static void TestAnimalPettingRouteOrdering()
{
    AnimalPettingRouteOption[] options =
    {
        new(9, new(5, 5), new(5, 6), 8),
        new(8, new(5, 5), new(5, 6), 8),
        new(7, new(8, 8), new(8, 9), 2)
    };
    IReadOnlyList<AnimalPettingRouteOption> ordered =
        AnimalPettingTargetPlanner.OrderOptions(options.Reverse());
    Equal(7L, ordered[0].AnimalId);
    Equal(8L, ordered[1].AnimalId);
    Equal(9L, ordered[2].AnimalId);
}

static void TestAnimalFiniteHayConservation()
{
    Equal(0, AnimalFeedingPolicy.GetFillCount(0, 20));
    Equal(2, AnimalFeedingPolicy.GetFillCount(8, 2));
    Equal(8, AnimalFeedingPolicy.GetFillCount(8, 20));
    Throws<ArgumentOutOfRangeException>(() => AnimalFeedingPolicy.GetFillCount(1, -1));
}

static void TestAnimalToolProduceReadiness()
{
    AnimalCareSkipReason ready = AnimalProducePolicy.TryCreateToolHarvestPlan(
        true, true, "184", true, "Milk Pail", true, 2, false,
        out AnimalProducePlan? plan);
    Equal(AnimalCareSkipReason.None, ready);
    Equal("(O)184", plan!.QualifiedItemId);
    Equal(2, plan.Stack);
    Equal(2, plan.Quality);
    Equal("Milk Pail", plan.RequiredTool);

    Equal(AnimalCareSkipReason.Baby, AnimalProducePolicy.TryCreateToolHarvestPlan(
        true, false, "184", true, "Milk Pail", false, 0, false, out _));
    Equal(AnimalCareSkipReason.NoProduce, AnimalProducePolicy.TryCreateToolHarvestPlan(
        true, true, null, true, "Milk Pail", false, 0, false, out _));
    Equal(AnimalCareSkipReason.WrongHarvestType, AnimalProducePolicy.TryCreateToolHarvestPlan(
        true, true, "184", false, "Milk Pail", false, 0, false, out _));
    Equal(AnimalCareSkipReason.AutoGrabberOwned, AnimalProducePolicy.TryCreateToolHarvestPlan(
        true, true, "184", true, "Milk Pail", false, 0, true, out _));
}

static void TestAnimalLooseProductOwnership()
{
    IReadOnlySet<string> allowed = new HashSet<string> { "(O)176", "(O)174" };
    Equal(true, AnimalProductSourcePolicy.IsEligibleLooseProduct(
        "(O)176", false, false, allowed));
    Equal(false, AnimalProductSourcePolicy.IsEligibleLooseProduct(
        "(O)176", false, true, allowed));
    Equal(false, AnimalProductSourcePolicy.IsEligibleLooseProduct(
        "(O)178", false, false, allowed));
    Equal(false, AnimalProductSourcePolicy.IsEligibleLooseProduct(
        "(BC)165", true, false, allowed));
    Equal(false, AnimalProductSourcePolicy.IsEligibleLooseProduct(
        "(O)390", false, false, allowed));
}

static void TestAnimalProductRouteOrdering()
{
    AnimalProductRouteOption[] options =
    {
        new("tool:b", new(5, 5), new(5, 6), 8),
        new("tool:a", new(5, 5), new(5, 6), 8),
        new("loose:c", new(8, 8), new(8, 9), 2)
    };
    IReadOnlyList<AnimalProductRouteOption> ordered =
        AnimalProductTargetPlanner.OrderOptions(options.Reverse());
    Equal("loose:c", ordered[0].StableKey);
    Equal("tool:a", ordered[1].StableKey);
    Equal("tool:b", ordered[2].StableKey);
}

static void TestAnimalProductCommitPreflight()
{
    Equal(AnimalProductTransferFailure.None,
        AnimalProductCommitPolicy.EvaluatePreflight(true, true, 2, 2));
    Equal(AnimalProductTransferFailure.SourceChanged,
        AnimalProductCommitPolicy.EvaluatePreflight(false, true, 99, 1));
    Equal(AnimalProductTransferFailure.DestinationChanged,
        AnimalProductCommitPolicy.EvaluatePreflight(true, false, 99, 1));
    Equal(AnimalProductTransferFailure.InsufficientCapacity,
        AnimalProductCommitPolicy.EvaluatePreflight(true, true, 1, 2));
    Throws<ArgumentOutOfRangeException>(() =>
        AnimalProductCommitPolicy.EvaluatePreflight(true, true, 0, 0));
}

static void TestRecurringContractStateValidation()
{
    RecurringContractSaveData valid = NewRecurringContractState();
    Equal(true, RecurringContractPolicy.IsValid(valid));

    RecurringContractSaveData wrongSchema = NewRecurringContractState();
    wrongSchema.SchemaVersion++;
    Equal(false, RecurringContractPolicy.IsValid(wrongSchema));

    RecurringContractSaveData unsupportedStorageTask = NewRecurringContractState();
    unsupportedStorageTask.Template!.Task = NamedFarmTask.StorageSorting;
    Equal(false, RecurringContractPolicy.IsValid(unsupportedStorageTask));

    RecurringContractSaveData unknownWorker = NewRecurringContractState();
    unknownWorker.Template!.PreferredWorkerName = "UnknownCustomNpc";
    Equal(false, RecurringContractPolicy.IsValid(unknownWorker));

    RecurringContractSaveData duplicateSubstitute = NewRecurringContractState();
    duplicateSubstitute.Template!.ApprovedSubstituteNames = new[] { "Leah", "leah" };
    Equal(false, RecurringContractPolicy.IsValid(duplicateSubstitute));

    RecurringContractSaveData widenedFixedPool = NewRecurringContractState();
    widenedFixedPool.Template!.WorkerMode = RecurringWorkerMode.FixedWorkerOnly;
    Equal(false, RecurringContractPolicy.IsValid(widenedFixedPool));

    RecurringContractSaveData invalidRestCap = NewRecurringContractState();
    invalidRestCap.Template!.MaximumRestDayGold = 500;
    Equal(false, RecurringContractPolicy.IsValid(invalidRestCap));

    RecurringContractSaveData mismatchedRun = NewRecurringContractState();
    mismatchedRun.Template!.LastRunId = Guid.NewGuid().ToString("N");
    Equal(false, RecurringContractPolicy.IsValid(mismatchedRun));

    RecurringContractSaveData completed = NewRecurringContractState();
    string runId = Guid.NewGuid().ToString("N");
    completed.Template!.LastRunId = runId;
    completed.Template.LastEvaluation = new RecurringEvaluationData
    {
        TotalDays = 11,
        RunId = runId,
        Status = RecurringEvaluationStatus.Completed,
        SelectedWorkerName = "Alex",
        AuthorizedGold = 720,
        CompletedWork = 12,
        ChargedGold = 120,
        RefundedGold = 600
    };
    Equal(true, RecurringContractPolicy.IsValid(completed));
    completed.Template.LastEvaluation.RefundedGold = 601;
    Equal(false, RecurringContractPolicy.IsValid(completed));

    RecurringContractSaveData nullReason = NewRecurringContractState();
    nullReason.Template!.LastEvaluation.ReasonKey = null!;
    Equal(false, RecurringContractPolicy.IsValid(nullReason));
}

static void TestRecurringContractCandidatePool()
{
    RecurringContractTemplateData template = NewRecurringContractState().Template!;
    IReadOnlyList<string> names = RecurringContractPolicy.GetAuthorizedWorkerNames(template);
    Equal(3, names.Count);
    Equal("Alex", names[0]);
    Equal("Leah", names[1]);
    Equal("Robin", names[2]);

    template.WorkerMode = RecurringWorkerMode.FixedWorkerOnly;
    template.ApprovedSubstituteNames = Array.Empty<string>();
    names = RecurringContractPolicy.GetAuthorizedWorkerNames(template);
    Equal(1, names.Count);
    Equal("Alex", names[0]);
}

static void TestRecurringContractRanking()
{
    RecurringWorkerCandidate[] preferredPool =
    {
        new("Leah", false, 1.10m, 540, 8, true),
        new("Alex", true, 1.05m, 720, 0, false)
    };
    Equal("Alex", RecurringContractPolicy.SelectCandidate(preferredPool)!.WorkerName);

    RecurringWorkerCandidate[] substitutePool =
    {
        new("Robin", false, 1.05m, 600, 4, false),
        new("Leah", false, 1.10m, 720, 0, false),
        new("Linus", false, 1.10m, 600, 4, false)
    };
    Equal("Linus", RecurringContractPolicy.SelectCandidate(substitutePool)!.WorkerName);
    Equal(true, RecurringContractPolicy.SelectCandidate(Array.Empty<RecurringWorkerCandidate>()) is null);
}

static void TestRecurringContractBudgetGates()
{
    Equal(RecurringBudgetFailure.None, RecurringContractPolicy.CheckBudget(600, 600, 600));
    Equal(RecurringBudgetFailure.ExceedsAuthorizedCap, RecurringContractPolicy.CheckBudget(601, 600, 1000));
    Equal(RecurringBudgetFailure.InsufficientFunds, RecurringContractPolicy.CheckBudget(600, 720, 599));
    Equal(RecurringBudgetFailure.ExceedsAuthorizedCap, RecurringContractPolicy.CheckBudget(0, 720, 1000));
}

static void TestRecurringContractDailyIdempotency()
{
    Equal(true, RecurringContractPolicy.CanWaitForEvaluation(true, 12, 11, 610));
    Equal(true, RecurringContractPolicy.CanWaitForEvaluation(true, 12, 11, 1600));
    Equal(false, RecurringContractPolicy.CanWaitForEvaluation(true, 12, 12, 900));
    Equal(false, RecurringContractPolicy.CanWaitForEvaluation(false, 12, 11, 900));
    Equal(false, RecurringContractPolicy.CanWaitForEvaluation(true, 12, 11, 600));
    Equal(false, RecurringContractPolicy.CanWaitForEvaluation(true, 12, 11, 1610));
}

static void TestRecurringContractPersistence()
{
    RecurringContractSaveData source = NewRecurringContractState();
    string json = JsonSerializer.Serialize(source);
    RecurringContractSaveData? restored = JsonSerializer.Deserialize<RecurringContractSaveData>(json);
    Equal(true, RecurringContractPolicy.IsValid(restored));
    Equal("Alex", restored!.Template!.PreferredWorkerName);
    Equal(NamedFarmTask.FarmWork, restored.Template.Task);
    Equal(2, restored.Template.ApprovedSubstituteNames.Length);
    Equal(2160, restored.Template.MaximumRestDayGold);
}

static void TestRecurringContractLegacyUpgrade()
{
    RecurringContractSaveData legacy = NewRecurringContractState();
    legacy.SchemaVersion = 1;
    legacy.Template!.Task = NamedFarmTask.Harvesting;

    RecurringContractSaveData upgraded = RecurringContractPolicy.Upgrade(legacy);
    Equal(RecurringContractPolicy.SchemaVersion, upgraded.SchemaVersion);
    Equal(NamedFarmTask.FarmWork, upgraded.Template!.Task);
    Equal(true, RecurringContractPolicy.IsValid(upgraded));

    RecurringContractSaveData empty = new() { SchemaVersion = 1 };
    upgraded = RecurringContractPolicy.Upgrade(empty);
    Equal(RecurringContractPolicy.SchemaVersion, upgraded.SchemaVersion);
    Equal(true, RecurringContractPolicy.IsValid(upgraded));
}

static void TestPreDispatchSettlement()
{
    WorkContractPreview preview = ContractPreviewService.Create(friendshipHearts: 4, dayOfMonth: 1);
    WateringContractSettlement settlement = WateringContractSettlement.Create(preview, dispatched: false, 900, 1200);
    Equal(600, settlement.ReservedGold);
    Equal(0, settlement.ChargedGold);
    Equal(600, settlement.RefundedGold);
    Equal(0, settlement.BillableHours);
}

static void TestDispatchedSettlement()
{
    WorkContractPreview preview = ContractPreviewService.Create(friendshipHearts: 4, dayOfMonth: 1);
    WateringContractSettlement settlement = WateringContractSettlement.Create(preview, dispatched: true, 900, 910);
    Equal(600, settlement.ReservedGold);
    Equal(100, settlement.ChargedGold);
    Equal(500, settlement.RefundedGold);
    Equal(1, settlement.BillableHours);
}

static void TestElapsedMultiHourSettlement()
{
    WorkContractPreview preview = ContractPreviewService.Create(friendshipHearts: 4, dayOfMonth: 1);
    WateringContractSettlement settlement = WateringContractSettlement.Create(preview, dispatched: true, 900, 1110);
    Equal(3, settlement.BillableHours);
    Equal(300, settlement.ChargedGold);
    Equal(300, settlement.RefundedGold);
}

static void TestTargetOrdering()
{
    FarmTaskRouteOption[] unordered =
    {
        new(new GridPoint(9, 5), new GridPoint(8, 5), PathCost: 3),
        new(new GridPoint(5, 7), new GridPoint(5, 6), PathCost: 2),
        new(new GridPoint(4, 6), new GridPoint(5, 6), PathCost: 2),
        new(new GridPoint(2, 5), new GridPoint(3, 5), PathCost: 3)
    };

    IReadOnlyList<FarmTaskRouteOption> ordered = FarmTaskRouteSelection.Order(unordered);
    Equal(new GridPoint(4, 6), ordered[0].Target);
    Equal(new GridPoint(5, 7), ordered[1].Target);
    Equal(new GridPoint(2, 5), ordered[2].Target);
    Equal(new GridPoint(9, 5), ordered[3].Target);
}

static void TestDryCropEligibility()
{
    Equal(true, WateringTargetPlanner.IsEligibleDryCropState(
        hasCrop: true,
        isDead: false,
        isWatered: false));
    Equal(false, WateringTargetPlanner.IsEligibleDryCropState(
        hasCrop: true,
        isDead: true,
        isWatered: false));
    Equal(false, WateringTargetPlanner.IsEligibleDryCropState(
        hasCrop: true,
        isDead: false,
        isWatered: true));
    Equal(false, WateringTargetPlanner.IsEligibleDryCropState(
        hasCrop: false,
        isDead: false,
        isWatered: false));
}

static void TestActualPathCostOrdering()
{
    FarmTaskRouteOption[] options =
    {
        new(new GridPoint(2, 0), new GridPoint(1, 0), PathCost: 10),
        new(new GridPoint(6, 0), new GridPoint(5, 0), PathCost: 5),
        new(new GridPoint(5, 1), new GridPoint(5, 0), PathCost: 5)
    };

    IReadOnlyList<FarmTaskRouteOption> ordered = FarmTaskRouteSelection.Order(options);
    Equal(new GridPoint(6, 0), ordered[0].Target);
    Equal(new GridPoint(5, 1), ordered[1].Target);
    Equal(new GridPoint(2, 0), ordered[2].Target);
}

static void TestTrellisDetourRoute()
{
    HashSet<GridPoint> blocked = new()
    {
        new(3, 0),
        new(3, 1),
        new(3, 2),
        new(3, 3)
    };
    GridRouteMap routes = GridRouteMap.Build(
        width: 7,
        height: 5,
        start: new GridPoint(0, 2),
        isPassable: tile => !blocked.Contains(tile));

    Equal(false, routes.IsReachable(new GridPoint(3, 2)));
    Equal(true, routes.TryGetDistance(new GridPoint(4, 2), out int distance));
    Equal(8, distance);
    Equal(true, routes.TryGetPath(new GridPoint(4, 2), out IReadOnlyList<GridPoint> path));
    Equal(new GridPoint(0, 2), path[0]);
    Equal(new GridPoint(4, 2), path[^1]);
    Equal(9, path.Count);
}

static void TestFailedInteractionEdgeIsolation()
{
    FarmTaskRouteEdge blocked = new(
        new GridPoint(8, 8),
        new GridPoint(8, 9));
    HashSet<FarmTaskRouteEdge> failed = new() { blocked };

    Equal(true, failed.Contains(blocked));
    Equal(false, failed.Contains(new FarmTaskRouteEdge(
        new GridPoint(8, 8),
        new GridPoint(7, 8))));
}

static void TestControllerPathSkipsCurrentTile()
{
    IReadOnlyList<GridPoint> steps = FarmNavigationMap.ToControllerSteps(new[]
    {
        new GridPoint(78, 15),
        new GridPoint(77, 15),
        new GridPoint(76, 15)
    });

    Equal(2, steps.Count);
    Equal(new GridPoint(77, 15), steps[0]);
    Equal(new GridPoint(76, 15), steps[1]);
}

static void TestDestinationTileAlignment()
{
    GridPoint pixel = FarmNavigationMap.GetAlignedCharacterPixel(new GridPoint(45, 32), 64);
    Equal(new GridPoint(45 * 64, 32 * 64), pixel);
}

static void TestTravelProgressWatchdog()
{
    TravelProgressWatchdog watchdog = new();
    watchdog.Reset(100f, 200f);
    Equal(false, watchdog.Tick(100f, 200f, maximumStalledTicks: 3));
    Equal(false, watchdog.Tick(100f, 200f, maximumStalledTicks: 3));
    Equal(true, watchdog.Tick(100f, 200f, maximumStalledTicks: 3));
    Equal(false, watchdog.Tick(102f, 200f, maximumStalledTicks: 3));
}

static void TestConsecutiveRouteFailureBudget()
{
    TravelReplanBudget budget = new(maximumFailures: 3);
    GridPoint origin = new(68, 17);

    TravelReplanDecision first = budget.RecordFailure(TravelRoutePurpose.Target, origin);
    Equal(1, first.FailureCount);
    Equal(3, first.MaximumFailures);
    Equal(true, first.CanReplan);

    Equal(true, budget.RecordFailure(TravelRoutePurpose.Target, origin).CanReplan);
    TravelReplanDecision exhausted = budget.RecordFailure(TravelRoutePurpose.Target, origin);
    Equal(3, exhausted.FailureCount);
    Equal(false, exhausted.CanReplan);

    TravelReplanDecision moved = budget.RecordFailure(
        TravelRoutePurpose.Target,
        new GridPoint(67, 17));
    Equal(1, moved.FailureCount);
    Equal(true, moved.CanReplan);

    TravelReplanDecision delivery = budget.RecordFailure(
        TravelRoutePurpose.Delivery,
        origin);
    Equal(1, delivery.FailureCount);
    Equal(true, delivery.CanReplan);

    budget.Reset(TravelRoutePurpose.Target);
    Equal(1, budget.RecordFailure(TravelRoutePurpose.Target, origin).FailureCount);
}

static void TestTargetRouteFailureIsolation()
{
    TravelReplanBudget budget = new(maximumFailures: 3);
    GridPoint origin = new(73, 14);

    TargetRouteFailureDecision first = TargetRouteFailurePolicy.RecordFailure(budget, origin);
    Equal(TargetRouteFailureAction.RetryRoute, first.Action);
    Equal(1, first.RouteFailureCount);

    Equal(TargetRouteFailureAction.RetryRoute,
        TargetRouteFailurePolicy.RecordFailure(budget, origin).Action);
    TargetRouteFailureDecision firstSkipped = TargetRouteFailurePolicy.RecordFailure(budget, origin);
    Equal(TargetRouteFailureAction.SkipTarget, firstSkipped.Action);
    Equal(1, firstSkipped.StalledTargetCount);

    for (int skippedTarget = 2; skippedTarget <= 3; skippedTarget++)
    {
        Equal(TargetRouteFailureAction.RetryRoute,
            TargetRouteFailurePolicy.RecordFailure(budget, origin).Action);
        Equal(TargetRouteFailureAction.RetryRoute,
            TargetRouteFailurePolicy.RecordFailure(budget, origin).Action);
        TargetRouteFailureDecision isolated = TargetRouteFailurePolicy.RecordFailure(budget, origin);
        Equal(skippedTarget == 3
                ? TargetRouteFailureAction.StopAtOrigin
                : TargetRouteFailureAction.SkipTarget,
            isolated.Action);
        Equal(skippedTarget, isolated.StalledTargetCount);
    }

    TargetRouteFailurePolicy.ResetAfterArrival(budget);
    Equal(TargetRouteFailureAction.RetryRoute,
        TargetRouteFailurePolicy.RecordFailure(budget, origin).Action);
    Equal(TargetRouteFailureAction.RetryRoute,
        TargetRouteFailurePolicy.RecordFailure(budget, origin).Action);
    TargetRouteFailureDecision afterSuccess = TargetRouteFailurePolicy.RecordFailure(budget, origin);
    Equal(TargetRouteFailureAction.SkipTarget, afterSuccess.Action);
    Equal(1, afterSuccess.StalledTargetCount);
}

static void TestNpcProtectedActivityPolicy()
{
    Equal(false, NpcActivityPolicy.HasProtectedActivity(
        doingEndOfRouteAnimation: false,
        goingToDoEndOfRouteAnimation: false,
        isWalkingInSquare: false,
        hasSpriteAnimation: false,
        movementPause: 0));

    Equal(true, NpcActivityPolicy.HasProtectedActivity(false, false, false, false, 1));
    Equal(true, NpcActivityPolicy.HasProtectedActivity(true, false, false, false, 0));
    Equal(true, NpcActivityPolicy.HasProtectedActivity(false, true, false, false, 0));
    Equal(true, NpcActivityPolicy.HasProtectedActivity(false, false, true, false, 0));
    Equal(true, NpcActivityPolicy.HasProtectedActivity(false, false, false, true, 0));
    Equal(false, NpcActivityPolicy.HasProtectedActivity(false, false, false, false, -1));

    Equal(true, WorkerRosterPolicy.ShouldDisplay(WorkerAvailabilityState.EligibleForPreview));
    Equal(false, WorkerRosterPolicy.ShouldDisplay(WorkerAvailabilityState.TemporarilyUnavailable));
    Equal(false, WorkerRosterPolicy.ShouldDisplay(WorkerAvailabilityState.Ineligible));
    Equal(false, WorkerRosterPolicy.ShouldDisplay(WorkerAvailabilityState.Unknown));
}

static void TestPathFirstStepOffsets()
{
    Equal(true, FarmNavigationMap.TryGetFirstStepOffset(
        new GridPoint(10, 10),
        new GridPoint(9, 10),
        2,
        out GridPoint left));
    Equal(new GridPoint(-2, 0), left);

    Equal(true, FarmNavigationMap.TryGetFirstStepOffset(
        new GridPoint(10, 10),
        new GridPoint(11, 10),
        2,
        out GridPoint right));
    Equal(new GridPoint(2, 0), right);

    Equal(true, FarmNavigationMap.TryGetFirstStepOffset(
        new GridPoint(10, 10),
        new GridPoint(10, 9),
        2,
        out GridPoint up));
    Equal(new GridPoint(0, -2), up);

    Equal(true, FarmNavigationMap.TryGetFirstStepOffset(
        new GridPoint(10, 10),
        new GridPoint(10, 11),
        2,
        out GridPoint down));
    Equal(new GridPoint(0, 2), down);

    Equal(false, FarmNavigationMap.TryGetFirstStepOffset(
        new GridPoint(10, 10),
        new GridPoint(11, 11),
        2,
        out _));
    Equal(false, FarmNavigationMap.TryGetFirstStepOffset(
        new GridPoint(10, 10),
        new GridPoint(10, 10),
        2,
        out _));
    Equal(false, FarmNavigationMap.TryGetFirstStepOffset(
        new GridPoint(10, 10),
        new GridPoint(11, 10),
        0,
        out _));
}

static void TestExternalBoundaryArrivalOrdering()
{
    IReadOnlyList<GridPoint> candidates = FarmEntranceSelection.OrderBoundaryArrivalCandidates(
        mapWidth: 80,
        mapHeight: 65,
        new[]
        {
            new GridPoint(34, 5), // interior cave warp
            new GridPoint(41, -1), // backwoods
            new GridPoint(40, 65), // forest
            new GridPoint(80, 15) // bus stop
        });

    Equal(new GridPoint(79, 15), candidates[0]);
    int firstSouth = Array.FindIndex(
        candidates.ToArray(),
        candidate => FarmEntranceSelection.GetNearestBoundarySide(80, 65, candidate) == FarmBoundarySide.South);
    int firstNorth = Array.FindIndex(
        candidates.ToArray(),
        candidate => FarmEntranceSelection.GetNearestBoundarySide(80, 65, candidate) == FarmBoundarySide.North);
    Equal(true, firstSouth > 0);
    Equal(new GridPoint(40, 64), candidates[firstSouth]);
    Equal(true, firstNorth > firstSouth);
    Equal(new GridPoint(41, 0), candidates[firstNorth]);
    for (int index = 0; index < firstSouth; index++)
    {
        Equal(
            FarmBoundarySide.East,
            FarmEntranceSelection.GetNearestBoundarySide(80, 65, candidates[index]));
    }
    Equal(false, candidates.Contains(new GridPoint(34, 7)));
}

static void TestRightEntrancePriority()
{
    Equal(0, FarmEntranceSelection.GetEntrancePriority(FarmBoundarySide.East));
    Equal(1, FarmEntranceSelection.GetEntrancePriority(FarmBoundarySide.South));
    Equal(2, FarmEntranceSelection.GetEntrancePriority(FarmBoundarySide.North));
    Equal(3, FarmEntranceSelection.GetEntrancePriority(FarmBoundarySide.West));
}

static void TestStalledEntranceFallbackOrdering()
{
    IReadOnlyList<GridPoint> candidates = FarmEntranceSelection.OrderBoundaryArrivalCandidates(
        mapWidth: 80,
        mapHeight: 65,
        new[]
        {
            new GridPoint(41, -1),
            new GridPoint(40, 65),
            new GridPoint(80, 15)
        },
        excludedSides: new HashSet<FarmBoundarySide> { FarmBoundarySide.East });

    Equal(new GridPoint(40, 64), candidates[0]);
    Equal(false, candidates.Any(candidate =>
        FarmEntranceSelection.GetNearestBoundarySide(80, 65, candidate) == FarmBoundarySide.East));
}

static void TestNearestArrivalBoundarySide()
{
    Equal(FarmBoundarySide.East, FarmEntranceSelection.GetNearestBoundarySide(
        mapWidth: 80,
        mapHeight: 65,
        new GridPoint(78, 15)));
    Equal(FarmBoundarySide.South, FarmEntranceSelection.GetNearestBoundarySide(
        mapWidth: 80,
        mapHeight: 65,
        new GridPoint(40, 61)));
    Equal(FarmBoundarySide.North, FarmEntranceSelection.GetNearestBoundarySide(
        mapWidth: 80,
        mapHeight: 65,
        new GridPoint(41, 1)));
}

static void TestNpcLeaseRecoveryPolicy()
{
    Equal(
        NpcLeaseRecoveryAction.Retry,
        NpcLeaseRecoveryPolicy.Select(
            NpcLeaseRestoreResult.ConflictingController,
            deferredTicks: 0,
            mustFinalizeNow: false));
    Equal(
        NpcLeaseRecoveryAction.Relinquish,
        NpcLeaseRecoveryPolicy.Select(
            NpcLeaseRestoreResult.ConflictingController,
            NpcLeaseRecoveryPolicy.MaximumDeferredTicks,
            mustFinalizeNow: false));
    Equal(
        NpcLeaseRecoveryAction.Relinquish,
        NpcLeaseRecoveryPolicy.Select(
            NpcLeaseRestoreResult.ConflictingController,
            deferredTicks: 0,
            mustFinalizeNow: true));
    Equal(
        NpcLeaseRecoveryAction.Complete,
        NpcLeaseRecoveryPolicy.Select(
            NpcLeaseRestoreResult.Restored,
            deferredTicks: 0,
            mustFinalizeNow: false));
    Equal(
        NpcLeaseRecoveryAction.Complete,
        NpcLeaseRecoveryPolicy.Select(
            NpcLeaseRestoreResult.LeaseOwnershipLost,
            deferredTicks: 0,
            mustFinalizeNow: false));
    Equal(
        NpcLeaseRecoveryAction.Complete,
        NpcLeaseRecoveryPolicy.Select(
            NpcLeaseRestoreResult.Relinquished,
            deferredTicks: 0,
            mustFinalizeNow: false));
}

static void TestSixHourWageCap()
{
    WorkContractPreview preview = ContractPreviewService.Create(friendshipHearts: 4, dayOfMonth: 1);
    WateringContractSettlement settlement = WateringContractSettlement.Create(preview, dispatched: true, 900, 2200);
    Equal(6, settlement.BillableHours);
    Equal(600, settlement.ChargedGold);
    Equal(0, settlement.RefundedGold);
}

static void TestHarvestChestClassification()
{
    HarvestChestOption[] options =
    {
        new(new GridPoint(1, 1), new GridPoint(1, 2), HarvestChestMatchKind.SameItem, 999, 10, 1),
        new(new GridPoint(8, 8), new GridPoint(8, 9), HarvestChestMatchKind.ExactStack, 999, 10, 20),
        new(new GridPoint(2, 2), new GridPoint(2, 3), HarvestChestMatchKind.SameCategory, 999, 10, 2),
        new(new GridPoint(3, 3), new GridPoint(3, 4), HarvestChestMatchKind.Empty, 9999, 10, 1)
    };

    IReadOnlyList<HarvestChestOption> ordered = HarvestChestRanking.Order(options);
    Equal(HarvestChestMatchKind.ExactStack, ordered[0].MatchKind);
    Equal(HarvestChestMatchKind.SameItem, ordered[1].MatchKind);
    Equal(HarvestChestMatchKind.SameCategory, ordered[2].MatchKind);
    Equal(HarvestChestMatchKind.Empty, ordered[3].MatchKind);

    Equal(HarvestChestMatchKind.ExactStack, HarvestChestClassification.Classify(
        new HarvestChestContents(1, 1, 1, 4))!.Value);
    Equal(HarvestChestMatchKind.SameItem, HarvestChestClassification.Classify(
        new HarvestChestContents(0, 1, 1, 4))!.Value);
    Equal(HarvestChestMatchKind.SameCategory, HarvestChestClassification.Classify(
        new HarvestChestContents(0, 0, 2, 4))!.Value);
    Equal(HarvestChestMatchKind.Empty, HarvestChestClassification.Classify(
        new HarvestChestContents(0, 0, 0, 0))!.Value);
}

static void TestHarvestCategoryPurity()
{
    HarvestChestOption dedicated = new(
        new GridPoint(2, 2),
        new GridPoint(2, 3),
        HarvestChestMatchKind.SameCategory,
        AcceptableCapacity: 999,
        RequestedStack: 1,
        TravelDistance: 20,
        Contents: new HarvestChestContents(0, 0, 4, 4));
    HarvestChestOption mixed = new(
        new GridPoint(8, 8),
        new GridPoint(8, 9),
        HarvestChestMatchKind.SameCategory,
        AcceptableCapacity: 999,
        RequestedStack: 1,
        TravelDistance: 1,
        Contents: new HarvestChestContents(0, 0, 4, 12));

    IReadOnlyList<HarvestChestOption> ordered = HarvestChestRanking.Order(new[] { mixed, dedicated });
    Equal(dedicated, ordered[0]);
    Equal(10_000, dedicated.Contents.CategoryPurityBasisPoints);
    Equal(3_333, mixed.Contents.CategoryPurityBasisPoints);
}

static void TestHarvestIncompatibleChestExclusion()
{
    Equal(true, HarvestChestClassification.Classify(
        new HarvestChestContents(0, 0, 0, 8)) is null);
}

static void TestHarvestChestFullAcceptance()
{
    HarvestChestOption[] options =
    {
        new(new GridPoint(1, 1), new GridPoint(1, 2), HarvestChestMatchKind.SameItem, 9, 10, 1),
        new(new GridPoint(8, 8), new GridPoint(8, 9), HarvestChestMatchKind.SameItem, 10, 10, 20)
    };

    IReadOnlyList<HarvestChestOption> ordered = HarvestChestRanking.Order(options);
    Equal(new GridPoint(8, 8), ordered[0].ChestTile);
    Equal(true, ordered[0].CanFullyAccept);
    Equal(1, ordered.Count);
}

static void TestHarvestStableCategoryDestination()
{
    HarvestChestOption[] options =
    {
        new(new GridPoint(8, 8), new GridPoint(8, 9), HarvestChestMatchKind.SameCategory, 999, 10, 1,
            new HarvestChestContents(0, 0, 3, 3)),
        new(new GridPoint(2, 2), new GridPoint(2, 3), HarvestChestMatchKind.SameCategory, 999, 10, 20,
            new HarvestChestContents(0, 0, 3, 3))
    };

    IReadOnlyList<HarvestChestOption> ordered = HarvestChestRanking.Order(options);
    Equal(new GridPoint(2, 2), ordered[0].ChestTile);
}

static void TestHarvestEmptyChestCapacityFallback()
{
    HarvestChestOption[] options =
    {
        new(new GridPoint(1, 1), new GridPoint(1, 2), HarvestChestMatchKind.Empty, 36, 10, 1),
        new(new GridPoint(8, 8), new GridPoint(8, 9), HarvestChestMatchKind.Empty, 70, 10, 20)
    };

    IReadOnlyList<HarvestChestOption> ordered = HarvestChestRanking.Order(options);
    Equal(new GridPoint(8, 8), ordered[0].ChestTile);
}

static void TestHarvestChestRouteAttemptIsolation()
{
    GridPoint chest = new(12, 8);
    GridPoint south = new(12, 9);
    GridPoint west = new(11, 8);
    HashSet<GridPoint> attemptedChests = new();
    HashSet<HarvestChestRouteKey> attemptedRoutes = new()
    {
        new(chest, south)
    };

    Equal(true, HarvestChestRouteAttemptPolicy.IsExcluded(
        chest,
        south,
        attemptedChests,
        attemptedRoutes));
    Equal(false, HarvestChestRouteAttemptPolicy.IsExcluded(
        chest,
        west,
        attemptedChests,
        attemptedRoutes));

    attemptedChests.Add(chest);
    Equal(true, HarvestChestRouteAttemptPolicy.IsExcluded(
        chest,
        west,
        attemptedChests,
        attemptedRoutes));
}

static void TestHarvestContractDestinationPolicy()
{
    Equal(
        HarvestDestinationMode.ClassifiedChests,
        HarvestDestinationPolicy.DefaultManualMode);
    Equal(
        HarvestDestinationMode.ClassifiedChests,
        HarvestDestinationPolicy.AutomaticMode);
    Equal(true, HarvestDestinationPolicy.IsValidForTask(
        NamedFarmTask.Harvesting,
        HarvestDestinationMode.RequesterInventory));
    Equal(false, HarvestDestinationPolicy.IsValidForTask(
        NamedFarmTask.Watering,
        HarvestDestinationMode.RequesterInventory));
    Equal(
        HarvestDestinationAction.RouteToClassifiedChest,
        HarvestDestinationPolicy.SelectAction(
            HarvestDestinationMode.ClassifiedChests,
            requesterIsOnline: true,
            requesterIsOnMainFarm: true,
            requesterCanAcceptCompleteStack: true));
    Equal(
        HarvestDestinationAction.DeliverToRequester,
        HarvestDestinationPolicy.SelectAction(
            HarvestDestinationMode.RequesterInventory,
            requesterIsOnline: true,
            requesterIsOnMainFarm: true,
            requesterCanAcceptCompleteStack: true));
    Equal(
        HarvestDestinationAction.StopUnavailable,
        HarvestDestinationPolicy.SelectAction(
            HarvestDestinationMode.RequesterInventory,
            requesterIsOnline: true,
            requesterIsOnMainFarm: true,
            requesterCanAcceptCompleteStack: false));
    Equal(
        HarvestDestinationAction.StopUnavailable,
        HarvestDestinationPolicy.SelectAction(
            HarvestDestinationMode.RequesterInventory,
            requesterIsOnline: false,
            requesterIsOnMainFarm: false,
            requesterCanAcceptCompleteStack: true));
    Equal(0, HarvestDestinationPolicy.GetRetainedCount(20, 20, 7));
    Equal(3, HarvestDestinationPolicy.GetRetainedCount(20, 23, 7));
    Equal(7, HarvestDestinationPolicy.GetRetainedCount(20, 29, 7));
}

static void TestStorageSortClassificationPriority()
{
    StorageSortPlan exact = StorageSortPlanner.Create(new[]
    {
        SortChest(0, 0, 12, SortStack("source", "carrot-q0", "carrot", -75, 10)),
        SortChest(2, 0, 12, SortStack("exact", "carrot-q0", "carrot", -75, 5)),
        SortChest(1, 0, 12, SortStack("same", "carrot-q1", "carrot", -75, 5)),
        SortChest(3, 0, 12, SortStack("category", "potato-q0", "potato", -75, 5)),
        SortChest(4, 0, 12)
    });
    Equal(true, exact.CanExecute);
    Equal(new GridPoint(2, 0), exact.Transfers[0].DestinationChest);

    StorageSortPlan sameItem = StorageSortPlanner.Create(new[]
    {
        SortChest(0, 0, 12, SortStack("source", "carrot-q0", "carrot", -75, 10)),
        SortChest(1, 0, 12, SortStack("same", "carrot-q1", "carrot", -75, 5)),
        SortChest(2, 0, 12, SortStack("category", "potato-q0", "potato", -75, 5)),
        SortChest(3, 0, 12)
    });
    Equal(true, sameItem.CanExecute);
    Equal(new GridPoint(1, 0), sameItem.Transfers[0].DestinationChest);

    StorageSortPlan category = StorageSortPlanner.Create(new[]
    {
        SortChest(0, 0, 12, SortStack("source", "carrot-q0", "carrot", -75, 10)),
        SortChest(1, 0, 12, SortStack("category", "potato-q0", "potato", -75, 5)),
        SortChest(2, 0, 12)
    });
    Equal(true, category.CanExecute);
    Equal(new GridPoint(1, 0), category.Transfers[0].DestinationChest);

    StorageSortPlan emptyFallback = StorageSortPlanner.Create(new[]
    {
        SortChest(
            0,
            0,
            12,
            SortStack("anchor", "stone-q0", "stone", -12, 20),
            SortStack("source", "carrot-q0", "carrot", -75, 10)),
        SortChest(1, 0, 12)
    });
    Equal(true, emptyFallback.CanExecute);
    Equal(new GridPoint(1, 0), emptyFallback.Transfers[0].DestinationChest);
}

static void TestStorageSortCategoryPurity()
{
    StorageSortPlan plan = StorageSortPlanner.Create(new[]
    {
        SortChest(
            0,
            0,
            12,
            SortStack("source", "carrot-q0", "carrot", -75, 10),
            SortStack("source-stone", "stone-q0", "stone", -12, 1)),
        SortChest(
            2,
            0,
            12,
            SortStack("mixed-potato", "potato-q0", "potato", -75, 20),
            SortStack("mixed-coal", "coal-q0", "coal", -15, 19)),
        SortChest(3, 0, 12, SortStack("pure-potato", "potato-q1", "potato", -75, 5)),
        SortChest(4, 0, 12),
        SortChest(5, 0, 12)
    });

    Equal(true, plan.CanExecute);
    StorageSortTransfer carrot = plan.Transfers.First(transfer => transfer.StackId == "source");
    Equal(new GridPoint(3, 0), carrot.DestinationChest);
}

static void TestStorageSortStableTie()
{
    StorageSortPlan plan = StorageSortPlanner.Create(new[]
    {
        SortChest(
            0,
            0,
            12,
            SortStack("anchor", "stone-q0", "stone", -12, 20),
            SortStack("source", "carrot-q0", "carrot", -75, 10)),
        SortChest(8, 8, 12, SortStack("late", "potato-q0", "potato", -75, 5)),
        SortChest(2, 2, 12, SortStack("early", "parsnip-q0", "parsnip", -75, 5))
    });

    Equal(true, plan.CanExecute);
    Equal(new GridPoint(2, 2), plan.Transfers.First().DestinationChest);
}

static void TestStorageSortCapacityPreflight()
{
    StorageSortPlan plan = StorageSortPlanner.Create(new[]
    {
        SortChest(
            0,
            0,
            2,
            SortStack("anchor", "stone-q0", "stone", -12, 20),
            SortStack("source", "carrot-q0", "carrot", -75, 10)),
        SortChest(1, 0, 1, SortStack("full", "fiber-q0", "fiber", -16, 999))
    });

    Equal(false, plan.CanExecute);
    Equal(StorageSortPlanFailure.InsufficientCapacity, plan.Failure);
    Equal(0, plan.Transfers.Count);
    Equal(1029, SortQuantity(plan.ResultChests));
}

static void TestStorageSortIdempotence()
{
    StorageSortPlan first = StorageSortPlanner.Create(new[]
    {
        SortChest(0, 0, 12, SortStack("carrot-a", "carrot-q0", "carrot", -75, 10)),
        SortChest(1, 0, 12, SortStack("carrot-b", "carrot-q0", "carrot", -75, 20)),
        SortChest(
            2,
            0,
            12,
            SortStack("stone", "stone-q0", "stone", -12, 20),
            SortStack("potato", "potato-q0", "potato", -75, 5)),
        SortChest(3, 0, 12)
    });

    Equal(true, first.CanExecute);
    Equal(true, first.Transfers.Count > 0);
    StorageSortPlan second = StorageSortPlanner.Create(first.ResultChests);
    Equal(true, second.CanExecute);
    Equal(0, second.Transfers.Count);
}

static void TestStorageSortConservation()
{
    StorageSortChestSnapshot[] input =
    {
        SortChest(
            0,
            0,
            12,
            SortStack("stone", "stone-q0", "stone", -12, 20),
            SortStack("carrot", "carrot-q0", "carrot", -75, 10)),
        SortChest(1, 0, 12, SortStack("potato", "potato-q0", "potato", -75, 5)),
        SortChest(2, 0, 12)
    };

    StorageSortPlan plan = StorageSortPlanner.Create(input);
    Equal(true, plan.CanExecute);
    Equal(SortQuantity(input), SortQuantity(plan.ResultChests));
    Equal(
        true,
        input.SelectMany(chest => chest.Stacks)
            .Select(stack => stack.ItemId)
            .OrderBy(id => id)
            .SequenceEqual(plan.ResultChests
                .SelectMany(chest => chest.Stacks)
                .Select(stack => stack.ItemId)
                .OrderBy(id => id)));
}

static void TestStorageSortInvalidSnapshot()
{
    StorageSortPlan duplicateStackId = StorageSortPlanner.Create(new[]
    {
        SortChest(0, 0, 12, SortStack("duplicate", "stone-q0", "stone", -12, 20)),
        SortChest(1, 0, 12, SortStack("duplicate", "carrot-q0", "carrot", -75, 10))
    });
    Equal(StorageSortPlanFailure.InvalidSnapshot, duplicateStackId.Failure);

    StorageSortPlan overCapacity = StorageSortPlanner.Create(new[]
    {
        SortChest(
            0,
            0,
            1,
            SortStack("one", "stone-q0", "stone", -12, 20),
            SortStack("two", "carrot-q0", "carrot", -75, 10))
    });
    Equal(StorageSortPlanFailure.InvalidSnapshot, overCapacity.Failure);

    StorageSortPlan nullChest = StorageSortPlanner.Create(new StorageSortChestSnapshot[] { null! });
    Equal(StorageSortPlanFailure.InvalidSnapshot, nullChest.Failure);

    StorageSortPlan nullStack = StorageSortPlanner.Create(new[]
    {
        new StorageSortChestSnapshot(
            new GridPoint(0, 0),
            12,
            new StorageSortStackSnapshot[] { null! })
    });
    Equal(StorageSortPlanFailure.InvalidSnapshot, nullStack.Failure);
}

static void TestStorageSortGeneratedInvariants()
{
    Random random = new(4207);
    int[] categories = { -75, -79, -12 };
    for (int sample = 0; sample < 500; sample++)
    {
        List<StorageSortChestSnapshot> input = new();
        int chestCount = random.Next(2, 6);
        int stackNumber = 0;
        for (int chestNumber = 0; chestNumber < chestCount; chestNumber++)
        {
            int capacity = random.Next(1, 5);
            List<StorageSortStackSnapshot> stacks = new();
            int occupied = random.Next(0, capacity + 1);
            for (int slot = 0; slot < occupied; slot++)
            {
                int category = categories[random.Next(categories.Length)];
                string itemId = $"item-{category}-{random.Next(2)}";
                int quality = random.Next(2);
                stacks.Add(SortStack(
                    $"stack-{sample}-{stackNumber++}",
                    $"{itemId}-q{quality}",
                    itemId,
                    category,
                    random.Next(1, 20),
                    maximumStackSize: 20));
            }

            input.Add(SortChest(chestNumber, sample, capacity, stacks.ToArray()));
        }

        StorageSortPlan plan = StorageSortPlanner.Create(input);
        if (plan.Failure == StorageSortPlanFailure.NonConvergent)
        {
            throw new InvalidOperationException(
                $"sample={sample}; input={DescribeSortChests(input)}");
        }
        if (!plan.CanExecute)
            Equal(0, plan.Transfers.Count);
        Equal(
            SortTotals(input),
            SortTotals(plan.ResultChests));

        if (!plan.CanExecute)
            continue;

        foreach (StorageSortChestSnapshot chest in plan.ResultChests)
        {
            Equal(true, chest.Stacks.Count <= chest.Capacity);
            Equal(true, chest.Stacks.All(stack =>
                stack.Quantity > 0 && stack.Quantity <= stack.MaximumStackSize));
            Equal(true, chest.Stacks.Select(stack => stack.Category).Distinct().Count() <= 1);
        }

        StorageSortPlan repeated = StorageSortPlanner.Create(plan.ResultChests);
        Equal(true, repeated.CanExecute);
        Equal(0, repeated.Transfers.Count);
    }
}

static void TestStorageSnapshotValidation()
{
    GridPoint firstTile = new(1, 2);
    GridPoint secondTile = new(3, 4);
    Equal(true, StorageSortSnapshotValidation.HasSameChestSet(
        new[] { firstTile, secondTile },
        new[] { secondTile, firstTile }));
    Equal(false, StorageSortSnapshotValidation.HasSameChestSet(
        new[] { firstTile },
        new[] { firstTile, secondTile }));

    StorageSortItemFingerprint item = new(
        "(O)24",
        "StardewValley.Object",
        "Stardew Valley",
        Category: -75,
        Quality: 0,
        Quantity: 10,
        MaximumStackSize: 999,
        SerializedXml: "carrot");
    StorageSortChestFingerprint expected = new(
        firstTile,
        Capacity: 36,
        new[]
        {
            new StorageSortStackBinding("1:2:0", firstTile, Slot: 0, item)
        });
    StorageSortChestFingerprint unchanged = new(
        firstTile,
        Capacity: 36,
        new[]
        {
            new StorageSortStackBinding("1:2:0", firstTile, Slot: 0, item with { })
        });
    StorageSortChestFingerprint changed = new(
        firstTile,
        Capacity: 36,
        new[]
        {
            new StorageSortStackBinding(
                "1:2:0",
                firstTile,
                Slot: 0,
                item with { Quantity = 9 })
        });

    Equal(true, StorageSortSnapshotValidation.IsChestUnchanged(expected, unchanged));
    Equal(false, StorageSortSnapshotValidation.IsChestUnchanged(expected, changed));
    Equal(false, StorageSortSnapshotValidation.IsChestUnchanged(
        expected,
        unchanged with { Capacity = 48 }));
    Equal(false, StorageSortSnapshotValidation.IsChestUnchanged(
        expected,
        unchanged with { ChestTile = secondTile }));
}

static void TestStorageTransferLockOrder()
{
    GridPoint earlier = new(8, 2);
    GridPoint later = new(1, 3);
    Equal(new StorageSortLockPair(earlier, later),
        StorageSortTransferPolicy.GetLockOrder(later, earlier));
    Equal(new StorageSortLockPair(earlier, later),
        StorageSortTransferPolicy.GetLockOrder(earlier, later));

    GridPoint left = new(1, 5);
    GridPoint right = new(9, 5);
    Equal(new StorageSortLockPair(left, right),
        StorageSortTransferPolicy.GetLockOrder(right, left));
}

static void TestStorageTransferSequence()
{
    StorageSortTransfer first = new(
        Sequence: 1,
        SourceChest: new GridPoint(1, 1),
        DestinationChest: new GridPoint(2, 2),
        StackId: "stack-1",
        StackingKey: "carrot-q0",
        ItemId: "(O)24",
        Category: -75,
        Quantity: 10);
    StorageSortTransfer second = first with
    {
        Sequence = 2,
        StackId = "stack-2",
        Quantity = 5
    };
    StorageSortTransfer[] transfers = { first, second };

    Equal(true, StorageSortTransferPolicy.IsExpectedTransfer(transfers, 1, first));
    Equal(false, StorageSortTransferPolicy.IsExpectedTransfer(transfers, 1, second));
    Equal(true, StorageSortTransferPolicy.IsExpectedTransfer(transfers, 2, second));
    Equal(false, StorageSortTransferPolicy.IsExpectedTransfer(transfers, 3, second));
    Equal(false, StorageSortTransferPolicy.IsExpectedTransfer(
        transfers,
        1,
        first with { Quantity = 9 }));
}

static void TestStorageTransferConservation()
{
    Equal(true, StorageSortTransferAudit.IsConserved(
        expected: 20,
        destination: 20,
        restoredSource: 0,
        quarantine: 0,
        unresolved: 0));
    Equal(true, StorageSortTransferAudit.IsConserved(
        expected: 20,
        destination: 0,
        restoredSource: 20,
        quarantine: 0,
        unresolved: 0));
    Equal(true, StorageSortTransferAudit.IsConserved(
        expected: 20,
        destination: 7,
        restoredSource: 0,
        quarantine: 8,
        unresolved: 5));
    Equal(false, StorageSortTransferAudit.IsConserved(
        expected: 20,
        destination: 20,
        restoredSource: 1,
        quarantine: 0,
        unresolved: 0));
    Equal(false, StorageSortTransferAudit.IsConserved(
        expected: 20,
        destination: -1,
        restoredSource: 21,
        quarantine: 0,
        unresolved: 0));
}

static void TestStorageTransferRecoveryOwnership()
{
    GridPoint tile = new(4, 7);
    StorageSortItemFingerprint first = SortFingerprint("(O)24", -75, 8, "first");
    StorageSortItemFingerprint removed = SortFingerprint("(O)190", -79, 3, "removed");
    StorageSortItemFingerprint last = SortFingerprint("(O)378", -12, 12, "last");
    StorageSortChestFingerprint expected = new(
        tile,
        Capacity: 36,
        new StorageSortStackBinding[]
        {
            new("4:7:0", tile, 0, first),
            new("4:7:1", tile, 1, removed),
            new("4:7:2", tile, 2, last)
        });
    StorageSortChestFingerprint actualWithoutRemoved = new(
        tile,
        Capacity: 36,
        new StorageSortStackBinding[]
        {
            new("4:7:0", tile, 0, first),
            new("4:7:1", tile, 1, last)
        });

    Equal(true, StorageSortRecoveryValidation.IsSourceWithoutTransfer(
        "4:7:1",
        expected,
        actualWithoutRemoved));
    Equal(false, StorageSortRecoveryValidation.IsSourceWithoutTransfer(
        "4:7:9",
        expected,
        actualWithoutRemoved));
    Equal(false, StorageSortRecoveryValidation.IsSourceWithoutTransfer(
        "4:7:1",
        expected,
        actualWithoutRemoved with { Capacity = 12 }));
    Equal(false, StorageSortRecoveryValidation.IsSourceWithoutTransfer(
        "4:7:1",
        expected,
        actualWithoutRemoved with
        {
            Stacks = new StorageSortStackBinding[]
            {
                new("4:7:0", tile, 0, first),
                new("4:7:1", tile, 1, removed)
            }
        }));
}

static StorageSortItemFingerprint SortFingerprint(
    string itemId,
    int category,
    int quantity,
    string xml)
{
    return new StorageSortItemFingerprint(
        itemId,
        RuntimeType: "StardewValley.Object",
        RuntimeAssembly: "Stardew Valley",
        category,
        Quality: 0,
        quantity,
        MaximumStackSize: 999,
        SerializedXml: xml);
}

static void TestStorageSortInteractionOrdering()
{
    StorageSortInteractionOption[] options =
    {
        new(new GridPoint(5, 4), Distance: 9, OffsetPriority: 0),
        new(new GridPoint(4, 5), Distance: 3, OffsetPriority: 2),
        new(new GridPoint(6, 5), Distance: 3, OffsetPriority: 1),
        new(new GridPoint(5, 6), Distance: 3, OffsetPriority: 1)
    };

    Equal(
        "6,5|5,6|4,5|5,4",
        string.Join("|", StorageSortRouteSelection.Order(options)
            .Select(option => $"{option.InteractionTile.X},{option.InteractionTile.Y}")));
}

static void TestStorageSortReportAccounting()
{
    StorageSortCompletedTransfer first = new(
        Sequence: 1,
        ItemId: "(O)24",
        DisplayName: "Parsnip",
        Category: -75,
        Quality: 0,
        Quantity: 8,
        SourceChest: new GridPoint(1, 1),
        DestinationChest: new GridPoint(2, 2));
    StorageSortCompletedTransfer second = first with
    {
        Sequence = 2,
        ItemId = "(O)190",
        Category = -79,
        Quantity = 3
    };

    Equal(true, StorageSortContractAudit.IsReportBalanced(
        plannedTransfers: 2,
        completed: new[] { first },
        skipped: new[] { second },
        movedItems: 8,
        persistedRecoveryItems: 0));
    Equal(false, StorageSortContractAudit.IsReportBalanced(
        plannedTransfers: 2,
        completed: new[] { first },
        skipped: Array.Empty<StorageSortCompletedTransfer>(),
        movedItems: 8,
        persistedRecoveryItems: 0));
    Equal(false, StorageSortContractAudit.IsReportBalanced(
        plannedTransfers: 2,
        completed: new[] { first },
        skipped: new[] { second with { Sequence = 1 } },
        movedItems: 8,
        persistedRecoveryItems: 0));
    Equal(false, StorageSortContractAudit.IsReportBalanced(
        plannedTransfers: 2,
        completed: new[] { first },
        skipped: new[] { second },
        movedItems: 7,
        persistedRecoveryItems: 0));
    Equal(false, StorageSortContractAudit.IsReportBalanced(
        plannedTransfers: 2,
        completed: new[] { first },
        skipped: new[] { second },
        movedItems: 8,
        persistedRecoveryItems: 4));
}

static void TestStorageSortSaveBoundaryPolicy()
{
    Guid transferId = Guid.NewGuid();
    Equal(true, StorageSortSaveBoundaryPolicy.CanForceQuarantine(
        hasUnresolvedItem: true,
        unresolvedItemDetached: true,
        transferId));
    Equal(false, StorageSortSaveBoundaryPolicy.CanForceQuarantine(
        hasUnresolvedItem: false,
        unresolvedItemDetached: true,
        transferId));
    Equal(false, StorageSortSaveBoundaryPolicy.CanForceQuarantine(
        hasUnresolvedItem: true,
        unresolvedItemDetached: false,
        transferId));
    Equal(false, StorageSortSaveBoundaryPolicy.CanForceQuarantine(
        hasUnresolvedItem: true,
        unresolvedItemDetached: true,
        Guid.Empty));
}

static StorageSortChestSnapshot SortChest(
    int x,
    int y,
    int capacity,
    params StorageSortStackSnapshot[] stacks)
{
    return new StorageSortChestSnapshot(new GridPoint(x, y), capacity, stacks);
}

static StorageSortStackSnapshot SortStack(
    string stackId,
    string stackingKey,
    string itemId,
    int category,
    int quantity,
    int maximumStackSize = 999)
{
    return new StorageSortStackSnapshot(
        stackId,
        stackingKey,
        itemId,
        category,
        quantity,
        maximumStackSize);
}

static int SortQuantity(IEnumerable<StorageSortChestSnapshot> chests)
{
    return chests.SelectMany(chest => chest.Stacks).Sum(stack => stack.Quantity);
}

static string SortTotals(IEnumerable<StorageSortChestSnapshot> chests)
{
    return string.Join(
        '|',
        chests
            .SelectMany(chest => chest.Stacks)
            .GroupBy(stack => (stack.StackingKey, stack.ItemId, stack.Category, stack.MaximumStackSize))
            .OrderBy(group => group.Key.StackingKey, StringComparer.Ordinal)
            .ThenBy(group => group.Key.ItemId, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Category)
            .ThenBy(group => group.Key.MaximumStackSize)
            .Select(group => $"{group.Key}:{group.Sum(stack => stack.Quantity)}"));
}

static string DescribeSortChests(IEnumerable<StorageSortChestSnapshot> chests)
{
    return string.Join(
        '|',
        chests.Select(chest =>
            $"{chest.ChestTile.X},{chest.ChestTile.Y}/{chest.Capacity}:" + string.Join(
                ';',
                chest.Stacks.Select(stack =>
                    $"{stack.StackId},{stack.StackingKey},{stack.Category},{stack.Quantity}"))));
}

static void TestHarvestPartialRemainder()
{
    Equal(6, HarvestTransferMath.GetDeliveredCount(requestedStack: 10, remainingStack: 4));
    Equal(0, HarvestTransferMath.GetDeliveredCount(requestedStack: 10, remainingStack: 10));
    Equal(10, HarvestTransferMath.GetDeliveredCount(requestedStack: 10, remainingStack: 0));
}

static void TestHarvestChestReleaseDeferral()
{
    int remainingCargoStacks = 2;
    int deliveredStacks = 0;

    remainingCargoStacks--;
    deliveredStacks++;

    Equal(false, HarvestChestReleaseDelay.CanContinue(elapsedTicks: 0));
    Equal(1, remainingCargoStacks);
    Equal(1, deliveredStacks);

    Equal(true, HarvestChestReleaseDelay.CanContinue(elapsedTicks: 1));
    remainingCargoStacks--;
    deliveredStacks++;

    Equal(0, remainingCargoStacks);
    Equal(2, deliveredStacks);
}

static void TestRegrowingHarvestCaptureSemantics()
{
    Equal(true, ContractHarvestSemantics.HasCapturedOutput(
        vanillaRequestsCropRemoval: false,
        capturedItemCount: 3));
    Equal(true, ContractHarvestSemantics.HasCapturedOutput(
        vanillaRequestsCropRemoval: true,
        capturedItemCount: 1));
    Equal(false, ContractHarvestSemantics.HasCapturedOutput(
        vanillaRequestsCropRemoval: false,
        capturedItemCount: 0));
    Equal(false, ContractHarvestSemantics.HasCapturedOutput(
        vanillaRequestsCropRemoval: true,
        capturedItemCount: 0));
}

static void TestReadyTapperTargetSemantics()
{
    Equal(true, TapperHarvestSemantics.IsReadyTarget(
        isTapper: true,
        attachedToTree: true,
        hasOutput: true,
        readyForHarvest: true));
    Equal(false, TapperHarvestSemantics.IsReadyTarget(false, true, true, true));
    Equal(false, TapperHarvestSemantics.IsReadyTarget(true, false, true, true));
    Equal(false, TapperHarvestSemantics.IsReadyTarget(true, true, false, true));
    Equal(false, TapperHarvestSemantics.IsReadyTarget(true, true, true, false));
}

static void TestReadyFruitTreeTargetSemantics()
{
    Equal(true, FruitTreeHarvestSemantics.IsReadyTarget(
        growthStage: 4,
        isStump: false,
        fruitSlots: 1));
    Equal(true, FruitTreeHarvestSemantics.IsReadyTarget(
        growthStage: 4,
        isStump: false,
        fruitSlots: 3));
    Equal(false, FruitTreeHarvestSemantics.IsReadyTarget(
        growthStage: 3,
        isStump: false,
        fruitSlots: 1));
    Equal(false, FruitTreeHarvestSemantics.IsReadyTarget(
        growthStage: 4,
        isStump: true,
        fruitSlots: 1));
    Equal(false, FruitTreeHarvestSemantics.IsReadyTarget(
        growthStage: 4,
        isStump: false,
        fruitSlots: 0));
    Equal(false, FruitTreeHarvestSemantics.ProducesCoal(struckByLightning: false));
    Equal(true, FruitTreeHarvestSemantics.ProducesCoal(struckByLightning: true));
}

static void TestReadyMachineTargetSemantics()
{
    Equal(true, MachineHarvestSemantics.IsReadyTarget(
        true, true, true, true, true, true, false, false, false, false));
    Equal(false, MachineHarvestSemantics.IsReadyTarget(
        false, true, true, true, true, true, false, false, false, false));
    Equal(false, MachineHarvestSemantics.IsReadyTarget(
        true, false, true, true, true, true, false, false, false, false));
    Equal(false, MachineHarvestSemantics.IsReadyTarget(
        true, true, true, true, true, true, true, false, false, false));
    Equal(false, MachineHarvestSemantics.IsReadyTarget(
        true, true, true, true, true, true, false, true, false, false));
    Equal(false, MachineHarvestSemantics.IsReadyTarget(
        true, true, true, true, true, true, false, false, true, false));
    Equal(false, MachineHarvestSemantics.IsReadyTarget(
        true, true, true, true, true, true, false, false, false, true));
}

static void TestReadyCrabPotTargetSemantics()
{
    Equal(true, CrabPotHarvestSemantics.IsReadyTarget(true, true, true));
    Equal(false, CrabPotHarvestSemantics.IsReadyTarget(false, true, true));
    Equal(false, CrabPotHarvestSemantics.IsReadyTarget(true, false, true));
    Equal(false, CrabPotHarvestSemantics.IsReadyTarget(true, true, false));
    Equal(2, CrabPotHarvestSemantics.GetOutputStack(1, true, 0.10, true));
    Equal(1, CrabPotHarvestSemantics.GetOutputStack(1, false, 0.10, true));
    Equal(1, CrabPotHarvestSemantics.GetOutputStack(1, true, 0.25, true));
    Equal(1, CrabPotHarvestSemantics.GetOutputStack(1, true, 0.10, false));
    Throws<ArgumentOutOfRangeException>(() =>
        CrabPotHarvestSemantics.GetOutputStack(0, true, 0.10, true));
    Throws<ArgumentOutOfRangeException>(() =>
        CrabPotHarvestSemantics.GetOutputStack(1, true, 1, true));
}

static void TestReadyFishPondTargetSemantics()
{
    Equal(true, FishPondHarvestSemantics.IsReadyTarget(true, true, true));
    Equal(false, FishPondHarvestSemantics.IsReadyTarget(false, true, true));
    Equal(false, FishPondHarvestSemantics.IsReadyTarget(true, false, true));
    Equal(false, FishPondHarvestSemantics.IsReadyTarget(true, true, false));
    Equal(10, FishPondHarvestSemantics.GetFishingExperience(null));
    Equal(14, FishPondHarvestSemantics.GetFishingExperience(100));
    Equal(33, FishPondHarvestSemantics.GetFishingExperience(599));
}

static void TestReadyBushTargetSemantics()
{
    Equal(true, BushHarvestSemantics.IsReadyTarget(
        true, false, 1, true, true, true));
    Equal(true, BushHarvestSemantics.IsReadyTarget(
        true, false, 3, true, true, true));
    Equal(false, BushHarvestSemantics.IsReadyTarget(
        false, false, 1, true, true, true));
    Equal(false, BushHarvestSemantics.IsReadyTarget(
        true, true, 1, true, true, true));
    Equal(false, BushHarvestSemantics.IsReadyTarget(
        true, false, 4, true, true, true));

    Equal(new BushHarvestPlan("(O)296", 1, 0, 1),
        BushHarvestSemantics.CreatePlan(1, "(O)296", 0, false));
    Equal(new BushHarvestPlan("(O)410", 3, 4, 3),
        BushHarvestSemantics.CreatePlan(1, "(O)410", 8, true));
    Equal(new BushHarvestPlan("(O)815", 1, 0, 0),
        BushHarvestSemantics.CreatePlan(3, "(O)815", 10, true));
    Throws<ArgumentOutOfRangeException>(() =>
        BushHarvestSemantics.CreatePlan(4, "(O)73", 10, true));
}

static void TestHarvestPlacementConservation()
{
    Equal(true, HarvestPlacementAudit.IsBalanced(
        harvested: 17,
        playerInventory: 3,
        chest: 7,
        overflow: 4,
        quarantine: 2,
        dropped: 1,
        unresolved: 0));
    Equal(false, HarvestPlacementAudit.IsBalanced(
        harvested: 17,
        playerInventory: 3,
        chest: 7,
        overflow: 4,
        quarantine: 2,
        dropped: 2,
        unresolved: 0));
    Equal(true, HarvestPlacementAudit.IsBalanced(
        harvested: 17,
        playerInventory: 3,
        chest: 7,
        overflow: 4,
        quarantine: 1,
        dropped: 1,
        unresolved: 1));
}

static void TestHarvestQuarantineRecoveryState()
{
    string contractId = Guid.NewGuid().ToString("N");
    string firstTransfer = Guid.NewGuid().ToString("N");
    string secondTransfer = Guid.NewGuid().ToString("N");
    HarvestCargoRecoverySaveData state = HarvestCargoRecoveryState.Create(
        445566,
        contractId,
        new[]
        {
            new HarvestCargoRecoveryItemData
            {
                TransferId = firstTransfer,
                QualifiedItemId = "(O)24",
                DisplayName = "Parsnip",
                RuntimeType = "StardewValley.Object",
                RuntimeAssembly = "Stardew Valley",
                SerializedItemXml = "<Object />",
                Quality = 2,
                Stack = 3,
                ModData = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["example/key"] = "value"
                }
            },
            new HarvestCargoRecoveryItemData
            {
                TransferId = secondTransfer,
                QualifiedItemId = "(O)188",
                DisplayName = "Green Bean",
                RuntimeType = "StardewValley.Object",
                RuntimeAssembly = "Stardew Valley",
                SerializedItemXml = "<Object />",
                Stack = 2
            }
        });
    Equal(true, HarvestCargoRecoveryState.IsValid(state, 445566));
    Equal(5, HarvestCargoRecoveryState.CountItems(state));

    string json = JsonSerializer.Serialize(state);
    HarvestCargoRecoverySaveData? restored =
        JsonSerializer.Deserialize<HarvestCargoRecoverySaveData>(json);
    Equal(true, HarvestCargoRecoveryState.IsValid(restored, 445566));
    Equal("value", restored!.Items[0].ModData["example/key"]);

    restored.Items[1].TransferId = firstTransfer;
    Equal(false, HarvestCargoRecoveryState.IsValid(restored, 445566));
    restored.Items[1].TransferId = secondTransfer;
    restored.Items[1].Stack = 0;
    Equal(false, HarvestCargoRecoveryState.IsValid(restored, 445566));
    restored.Items[1].Stack = 2;
    Equal(false, HarvestCargoRecoveryState.IsValid(restored, 123));

    Equal(true, HarvestCargoRecoveryState.IsSerializedPayloadValid("{}"));
    Equal(false, HarvestCargoRecoveryState.IsSerializedPayloadValid(""));
    Equal(false, HarvestCargoRecoveryState.IsSerializedPayloadValid(
        new string('x', HarvestCargoRecoveryState.MaximumSerializedPayloadLength + 1)));

    HarvestCargoRecoverySaveData excessiveModData = HarvestCargoRecoveryState.Create(
        445566,
        contractId,
        new[]
        {
            new HarvestCargoRecoveryItemData
            {
                TransferId = Guid.NewGuid().ToString("N"),
                QualifiedItemId = "(O)24",
                DisplayName = "Parsnip",
                RuntimeType = "StardewValley.Object",
                RuntimeAssembly = "Stardew Valley",
                SerializedItemXml = "<Object />",
                Stack = 1,
                ModData = Enumerable.Range(
                        0,
                        HarvestCargoRecoveryState.MaximumModDataEntriesPerItem + 1)
                    .ToDictionary(index => $"key-{index}", index => $"value-{index}")
            }
        });
    Equal(false, HarvestCargoRecoveryState.IsValid(excessiveModData, 445566));

    HarvestCargoRecoverySaveData excessivePayload = HarvestCargoRecoveryState.Create(
        445566,
        contractId,
        new[]
        {
            new HarvestCargoRecoveryItemData
            {
                TransferId = Guid.NewGuid().ToString("N"),
                QualifiedItemId = "(O)24",
                DisplayName = "Parsnip",
                RuntimeType = "StardewValley.Object",
                RuntimeAssembly = "Stardew Valley",
                SerializedItemXml = new string(
                    'x',
                    HarvestCargoRecoveryState.MaximumSerializedPayloadLength / 2 + 1),
                Stack = 1
            },
            new HarvestCargoRecoveryItemData
            {
                TransferId = Guid.NewGuid().ToString("N"),
                QualifiedItemId = "(O)188",
                DisplayName = "Green Bean",
                RuntimeType = "StardewValley.Object",
                RuntimeAssembly = "Stardew Valley",
                SerializedItemXml = new string(
                    'y',
                    HarvestCargoRecoveryState.MaximumSerializedPayloadLength / 2 + 1),
                Stack = 1
            }
        });
    Equal(false, HarvestCargoRecoveryState.IsValid(excessivePayload, 445566));
}

static void TestHarvestAcceptanceFaultControls()
{
    HarvestAcceptanceFaults faults = new();
    Equal("none", faults.Describe());
    Equal(true, HarvestAcceptanceFaults.TryParse("overflow-lock", out HarvestAcceptanceFault overflow));
    Equal(true, HarvestAcceptanceFaults.TryParse("VISIBLE-DROP", out HarvestAcceptanceFault visibleDrop));
    Equal(true, HarvestAcceptanceFaults.TryParse("quarantine-lock", out HarvestAcceptanceFault quarantineLock));
    Equal(true, HarvestAcceptanceFaults.TryParse("recovery-record-write", out HarvestAcceptanceFault recoveryWrite));
    Equal(true, HarvestAcceptanceFaults.TryParse("quarantine-write", out HarvestAcceptanceFault quarantineWrite));
    Equal(false, HarvestAcceptanceFaults.TryParse("unknown", out _));

    faults.Arm(overflow);
    faults.Arm(visibleDrop);
    faults.Arm(quarantineLock);
    faults.Arm(recoveryWrite);
    faults.Arm(quarantineWrite);
    Equal(true, faults.IsArmed(HarvestAcceptanceFault.OverflowLock));
    Equal(true, faults.IsArmed(HarvestAcceptanceFault.VisibleDrop));
    Equal(true, faults.IsArmed(HarvestAcceptanceFault.QuarantineLock));
    Equal(true, faults.IsArmed(HarvestAcceptanceFault.RecoveryRecordWrite));
    Equal(true, faults.IsArmed(HarvestAcceptanceFault.QuarantineWrite));
    Equal(
        "overflow-lock,visible-drop,quarantine-lock,recovery-record-write,quarantine-write",
        faults.Describe());

    faults.Clear();
    Equal("none", faults.Describe());
    Equal(false, faults.IsArmed(HarvestAcceptanceFault.OverflowLock));
}

static void TestEmergencyDropTileOrdering()
{
    HashSet<GridPoint> blocked = new()
    {
        new(4, 4),
        new(4, 3),
        new(3, 4)
    };
    GridPoint? selected = HarvestEmergencyDropSelection.FindNearest(
        mapWidth: 10,
        mapHeight: 10,
        anchor: new GridPoint(4, 4),
        isEligible: tile => !blocked.Contains(tile),
        maximumRadius: 2);

    Equal(new GridPoint(5, 4), selected!.Value);
    Equal(true, HarvestEmergencyDropSelection.FindNearest(
        mapWidth: 2,
        mapHeight: 2,
        anchor: new GridPoint(0, 0),
        isEligible: _ => false,
        maximumRadius: 1) is null);
}

static void TestHarvestUnavailableStorageStop()
{
    HarvestChestOption insufficient = new(
        new GridPoint(1, 1),
        new GridPoint(1, 2),
        HarvestChestMatchKind.SameCategory,
        AcceptableCapacity: 9,
        RequestedStack: 10,
        TravelDistance: 1,
        Contents: new HarvestChestContents(0, 0, 5, 5));
    Equal(0, HarvestChestRanking.Order(new[] { insufficient }).Count);
}

static void TestHarvestTransferReplayProtection()
{
    HarvestTransferLedger ledger = new();
    int applied = 0;
    Equal(true, ledger.TryApply("transfer-1", () => applied++));
    Equal(false, ledger.TryApply("transfer-1", () => applied++));
    Equal(1, applied);
}

static void TestMultiplayerRequestAuthorization()
{
    long playerId = 112233;
    ContractProtocolContext context = new(
        ModVersion: "0.1.0",
        SaveId: 445566,
        TotalDays: 12,
        KnownPlayerIds: new HashSet<long> { playerId });
    ContractStartRequestMessage valid = NewMultiplayerRequest(playerId);

    Equal(
        ContractRequestValidationFailure.None,
        ContractRequestValidator.Validate(valid, playerId, context));

    valid.RequestingPlayerId = 998877;
    Equal(
        ContractRequestValidationFailure.SenderMismatch,
        ContractRequestValidator.Validate(valid, playerId, context));

    valid.RequestingPlayerId = playerId;
    valid.ModVersion = "9.9.9";
    Equal(
        ContractRequestValidationFailure.WrongModVersion,
        ContractRequestValidator.Validate(valid, playerId, context));

    valid.ModVersion = "0.1.0";
    valid.SaveId = 1;
    Equal(
        ContractRequestValidationFailure.WrongSave,
        ContractRequestValidator.Validate(valid, playerId, context));

    valid.SaveId = 445566;
    valid.TotalDays = 11;
    Equal(
        ContractRequestValidationFailure.StaleDay,
        ContractRequestValidator.Validate(valid, playerId, context));

    valid.TotalDays = 12;
    valid.Task = NamedFarmTask.StorageSorting;
    Equal(
        ContractRequestValidationFailure.InvalidTask,
        ContractRequestValidator.Validate(valid, playerId, context));

    valid.Task = (NamedFarmTask)999;
    Equal(
        ContractRequestValidationFailure.InvalidTask,
        ContractRequestValidator.Validate(valid, playerId, context));

    valid.Task = NamedFarmTask.FarmWork;
    valid.HarvestDestination = HarvestDestinationMode.RequesterInventory;
    Equal(
        ContractRequestValidationFailure.None,
        ContractRequestValidator.Validate(valid, playerId, context));

    valid.Task = NamedFarmTask.Harvesting;
    Equal(
        ContractRequestValidationFailure.InvalidTask,
        ContractRequestValidator.Validate(valid, playerId, context));
}

static void TestMultiplayerRequestReplay()
{
    ProcessedContractRequestLedger ledger = new();
    ContractStartResponseMessage first = NewStartResponse(playerId: 1, requestId: "request-a", order: 1);
    ContractStartResponseMessage retry = NewStartResponse(playerId: 1, requestId: "request-a", order: 2);
    ledger.Record(first);
    ledger.Record(retry);

    Equal(1, ledger.Count);
    Equal(true, ledger.TryGet(1, "request-a", out ContractStartResponseMessage? stored));
    Equal(1L, stored!.HostOrder);
}

static void TestMultiplayerDeterministicOrder()
{
    ProcessedContractRequestLedger ledger = new();
    ledger.Record(NewStartResponse(playerId: 7, requestId: "first", order: 10));
    ledger.Record(NewStartResponse(playerId: 7, requestId: "second", order: 11));

    IReadOnlyList<ContractStartResponseMessage> responses = ledger.GetForPlayer(7);
    Equal("first", responses[0].RequestId);
    Equal("second", responses[1].RequestId);
    Equal(10L, responses[0].HostOrder);
    Equal(11L, responses[1].HostOrder);
}

static void TestMultiplayerReconnectLedger()
{
    ProcessedContractRequestLedger ledger = new(capacity: 2);
    ledger.Record(NewStartResponse(playerId: 4, requestId: "old", order: 1));
    ledger.Record(NewStartResponse(playerId: 5, requestId: "other", order: 2));
    ledger.Record(NewStartResponse(playerId: 4, requestId: "recent", order: 3));

    Equal(false, ledger.TryGet(4, "old", out _));
    IReadOnlyList<ContractStartResponseMessage> reconnect = ledger.GetForPlayer(4);
    Equal(1, reconnect.Count);
    Equal("recent", reconnect[0].RequestId);
}

static void TestMultiplayerRestartRecoveryState()
{
    string requestId = Guid.NewGuid().ToString("N");
    string contractId = Guid.NewGuid().ToString("N");
    ContractStartResponseMessage response = new()
    {
        SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
        SaveId = 445566,
        HostSessionId = "old-host-session",
        HostOrder = 8,
        RequestId = requestId,
        RequestingPlayerId = 55,
        Accepted = true,
        ContractId = contractId
    };
    ContractResultMessage result = new()
    {
        SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
        SaveId = 445566,
        HostSessionId = "old-host-session",
        ContractId = contractId,
        Sequence = 2,
        StateVersion = 9,
        RequestId = requestId,
        RequestingPlayerId = 55,
        WorkerName = "Leah",
        Task = NamedFarmTask.FarmWork,
        Succeeded = true,
        CompletedWork = 3,
        BillableHours = 1,
        ChargedGold = 100,
        RefundedGold = 500
    };
    MultiplayerRecoverySaveData state = MultiplayerRecoveryState.Create(
        "0.1.0",
        445566,
        new[] { response },
        new[] { result });

    Equal(true, MultiplayerRecoveryState.IsValid(state, 445566));
    string json = JsonSerializer.Serialize(state);
    MultiplayerRecoverySaveData? restored =
        JsonSerializer.Deserialize<MultiplayerRecoverySaveData>(json);
    Equal(true, MultiplayerRecoveryState.IsValid(restored, 445566));

    MultiplayerRecoverySaveData legacy =
        JsonSerializer.Deserialize<MultiplayerRecoverySaveData>(json)!;
    legacy.ProtocolSchemaVersion = 3;
    foreach (ContractStartResponseMessage legacyResponse in legacy.ProcessedRequests)
        legacyResponse.SchemaVersion = 3;
    foreach (ContractResultMessage legacyResult in legacy.RecentResults)
    {
        legacyResult.SchemaVersion = 3;
        legacyResult.Task = NamedFarmTask.Watering;
    }
    Equal(true, MultiplayerRecoveryState.IsValid(legacy, 445566));
    legacy.ProcessedRequests[0].SchemaVersion = MultiplayerContractProtocol.SchemaVersion;
    Equal(false, MultiplayerRecoveryState.IsValid(legacy, 445566));
    legacy.ProcessedRequests[0].SchemaVersion = 3;
    legacy.ProtocolSchemaVersion = 2;
    Equal(false, MultiplayerRecoveryState.IsValid(legacy, 445566));

    MultiplayerRecoverySaveData legacyQuarantine =
        JsonSerializer.Deserialize<MultiplayerRecoverySaveData>(json)!;
    legacyQuarantine.ProtocolSchemaVersion = 4;
    foreach (ContractStartResponseMessage legacyResponse in legacyQuarantine.ProcessedRequests)
        legacyResponse.SchemaVersion = 4;
    foreach (ContractResultMessage legacyResult in legacyQuarantine.RecentResults)
    {
        legacyResult.SchemaVersion = 4;
        legacyResult.Task = NamedFarmTask.Watering;
    }
    Equal(true, MultiplayerRecoveryState.IsValid(legacyQuarantine, 445566));

    MultiplayerRecoverySaveData legacyPlacement =
        JsonSerializer.Deserialize<MultiplayerRecoverySaveData>(json)!;
    legacyPlacement.ProtocolSchemaVersion = 5;
    foreach (ContractStartResponseMessage legacyResponse in legacyPlacement.ProcessedRequests)
        legacyResponse.SchemaVersion = 5;
    foreach (ContractResultMessage legacyResult in legacyPlacement.RecentResults)
    {
        legacyResult.SchemaVersion = 5;
        legacyResult.Task = NamedFarmTask.Watering;
    }
    Equal(true, MultiplayerRecoveryState.IsValid(legacyPlacement, 445566));

    MultiplayerRecoverySaveData legacyEfficiency =
        JsonSerializer.Deserialize<MultiplayerRecoverySaveData>(json)!;
    legacyEfficiency.ProtocolSchemaVersion = 6;
    foreach (ContractStartResponseMessage legacyResponse in legacyEfficiency.ProcessedRequests)
        legacyResponse.SchemaVersion = 6;
    foreach (ContractResultMessage legacyResult in legacyEfficiency.RecentResults)
    {
        legacyResult.SchemaVersion = 6;
        legacyResult.Task = NamedFarmTask.Watering;
    }
    Equal(true, MultiplayerRecoveryState.IsValid(legacyEfficiency, 445566));

    MultiplayerRecoverySaveData legacyDestination =
        JsonSerializer.Deserialize<MultiplayerRecoverySaveData>(json)!;
    legacyDestination.ProtocolSchemaVersion = 7;
    foreach (ContractStartResponseMessage legacyResponse in legacyDestination.ProcessedRequests)
        legacyResponse.SchemaVersion = 7;
    foreach (ContractResultMessage legacyResult in legacyDestination.RecentResults)
    {
        legacyResult.SchemaVersion = 7;
        legacyResult.Task = NamedFarmTask.Watering;
    }
    Equal(true, MultiplayerRecoveryState.IsValid(legacyDestination, 445566));
    legacyDestination.RecentResults[0].Task = NamedFarmTask.StorageSorting;
    Equal(false, MultiplayerRecoveryState.IsValid(legacyDestination, 445566));

    MultiplayerRecoveryState.RebindResponse(
        restored!.ProcessedRequests[0],
        "new-host-session",
        445566);
    MultiplayerRecoveryState.RebindResult(
        restored.RecentResults[0],
        "new-host-session",
        445566,
        sequence: 1,
        stateVersion: 1);
    Equal("new-host-session", restored.ProcessedRequests[0].HostSessionId);
    Equal("new-host-session", restored.RecentResults[0].HostSessionId);
    Equal(1L, restored.RecentResults[0].Sequence);
    Equal(1L, restored.RecentResults[0].StateVersion);

    MultiplayerRecoverySaveData filteredResult = MultiplayerRecoveryState.Create(
        "0.1.0",
        445566,
        Array.Empty<ContractStartResponseMessage>(),
        new[] { result });
    Equal(0, filteredResult.RecentResults.Length);

    MultiplayerRecoverySaveData orphanedResult = MultiplayerRecoveryState.Create(
        "0.1.0",
        445566,
        Array.Empty<ContractStartResponseMessage>(),
        Array.Empty<ContractResultMessage>());
    orphanedResult.RecentResults = new[] { result };
    Equal(false, MultiplayerRecoveryState.IsValid(orphanedResult, 445566));

    MultiplayerRecoverySaveData unclean = MultiplayerRecoveryState.Create(
        "0.1.0",
        445566,
        new[] { response },
        new[] { result },
        isClean: false);
    Equal(false, MultiplayerRecoveryState.IsValid(unclean, 445566));

    state.ModVersion = "0.1.1";
    Equal(true, MultiplayerRecoveryState.IsValid(state, 445566));

    string transferId = Guid.NewGuid().ToString("N");
    string partialTransferId = Guid.NewGuid().ToString("N");
    ContractResultMessage recoveredResult = state.RecentResults[0];
    recoveredResult.ProducedItems = new[]
    {
        new ContractCargoSnapshotMessage
        {
            TransferId = transferId,
            QualifiedItemId = "(O)24",
            DisplayName = "Parsnip",
            Stack = 2
        }
    };
    recoveredResult.CompletedTransferIds = new[] { transferId, partialTransferId };
    recoveredResult.CompletedWork = 1;
    recoveredResult.ChestItems = 2;
    Equal(true, MultiplayerRecoveryState.IsValid(state, 445566));

    recoveredResult.CompletedTransferIds = new[] { transferId, transferId };
    Equal(false, MultiplayerRecoveryState.IsValid(state, 445566));
    recoveredResult.CompletedTransferIds = new[] { transferId, partialTransferId };

    recoveredResult.ProducedItems = new[]
    {
        recoveredResult.ProducedItems[0],
        recoveredResult.ProducedItems[0]
    };
    recoveredResult.ChestItems = 4;
    Equal(false, MultiplayerRecoveryState.IsValid(state, 445566));
    recoveredResult.ProducedItems = new[] { recoveredResult.ProducedItems[0] };
    recoveredResult.ChestItems = 1;
    Equal(false, MultiplayerRecoveryState.IsValid(state, 445566));

    recoveredResult.QuarantinedItems = 1;
    Equal(true, MultiplayerRecoveryState.IsValid(state, 445566));
    recoveredResult.QuarantinedItems = -1;
    Equal(false, MultiplayerRecoveryState.IsValid(state, 445566));
    recoveredResult.QuarantinedItems = 0;

    recoveredResult.ChestItems = 2;
    recoveredResult.ReasonKey = "contract.failure.unknown";
    Equal(false, MultiplayerRecoveryState.IsValid(state, 445566));
    recoveredResult.ReasonKey = "";

    recoveredResult.Succeeded = false;
    Equal(false, MultiplayerRecoveryState.IsValid(state, 445566));
    recoveredResult.ReasonKey = "contract.failure.unknown";
    Equal(true, MultiplayerRecoveryState.IsValid(state, 445566));
    recoveredResult.Succeeded = true;
    recoveredResult.ReasonKey = "";
    recoveredResult.CompletedWork = 0;
    Equal(false, MultiplayerRecoveryState.IsValid(state, 445566));
    recoveredResult.CompletedWork = 1;

    recoveredResult.BillableHours = ContractPreviewService.RegularShiftHours + 1;
    Equal(false, MultiplayerRecoveryState.IsValid(state, 445566));
}

static void TestMultiplayerStaleSnapshotRejection()
{
    ContractSnapshotTracker tracker = new();
    ContractSnapshotMessage first = NewSnapshot(session: "host-a", sequence: 1);
    Equal(true, tracker.TryAccept(first, MultiplayerContractProtocol.SchemaVersion, expectedSaveId: 445566));

    ContractSnapshotMessage replay = NewSnapshot(session: "host-a", sequence: 1);
    Equal(false, tracker.TryAccept(replay, MultiplayerContractProtocol.SchemaVersion, expectedSaveId: 445566));

    ContractSnapshotMessage next = NewSnapshot(session: "host-a", sequence: 2);
    Equal(true, tracker.TryAccept(next, MultiplayerContractProtocol.SchemaVersion, expectedSaveId: 445566));

    ContractSnapshotMessage newHostSession = NewSnapshot(session: "host-b", sequence: 1);
    Equal(false, tracker.TryAccept(newHostSession, MultiplayerContractProtocol.SchemaVersion, expectedSaveId: 445566));
    tracker.BeginSession("host-b");
    Equal(true, tracker.TryAccept(newHostSession, MultiplayerContractProtocol.SchemaVersion, expectedSaveId: 445566));

    ContractSnapshotMessage wrongSave = NewSnapshot(session: "host-b", sequence: 2);
    wrongSave.SaveId = 123;
    Equal(false, tracker.TryAccept(wrongSave, MultiplayerContractProtocol.SchemaVersion, expectedSaveId: 445566));

    ContractResultMessage result = new()
    {
        SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
        SaveId = 445566,
        HostSessionId = "host-b",
        ContractId = "contract-1",
        Sequence = 2,
        Task = NamedFarmTask.FarmWork
    };
    Equal(true, tracker.TryAccept(result, MultiplayerContractProtocol.SchemaVersion, expectedSaveId: 445566));
    Equal(false, tracker.TryAccept(result, MultiplayerContractProtocol.SchemaVersion, expectedSaveId: 445566));
}

static void TestMultiplayerHostSessionHandshake()
{
    HostSessionTracker tracker = new();
    string syncA = Guid.NewGuid().ToString("N");
    string syncB = Guid.NewGuid().ToString("N");
    string hostA = Guid.NewGuid().ToString("N");
    string hostB = Guid.NewGuid().ToString("N");
    string oldHost = Guid.NewGuid().ToString("N");
    Equal(false, tracker.HasSession);
    Equal(false, tracker.Matches(hostA));
    Equal(false, tracker.BeginHandshake("invalid"));
    Equal(true, tracker.BeginHandshake(syncA));
    Equal(false, tracker.TryEstablish(oldHost, syncB));
    Equal(false, tracker.HasSession);
    Equal(false, tracker.TryEstablish("invalid-host", syncA));
    Equal(true, tracker.TryEstablish(hostA, syncA));
    Equal(true, tracker.HasSession);
    Equal(hostA, tracker.Current);
    Equal(true, tracker.Matches(hostA));
    Equal(false, tracker.Matches(hostB));
    Equal(true, tracker.BeginHandshake(syncB));
    Equal(false, tracker.TryEstablish(hostB, syncB));
    Equal(hostA, tracker.Current);

    tracker.Clear();
    Equal(false, tracker.HasSession);
    Equal(false, tracker.Matches(hostA));
    Equal(true, tracker.BeginHandshake(syncB));
    Equal(false, tracker.TryEstablish(oldHost, syncA));
    Equal(true, tracker.TryEstablish(hostB, syncB));
    Equal(hostB, tracker.Current);
}

static void TestMultiplayerSyncHandshakeSerialization()
{
    string syncRequestId = Guid.NewGuid().ToString("N");
    string hostSessionId = Guid.NewGuid().ToString("N");
    ContractSyncRequestMessage request = new()
    {
        SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
        ModVersion = "0.1.0",
        SaveId = 445566,
        RequestingPlayerId = 55,
        SyncRequestId = syncRequestId
    };
    ContractSyncRequestMessage? restoredRequest = JsonSerializer.Deserialize<ContractSyncRequestMessage>(
        JsonSerializer.Serialize(request));
    Equal(syncRequestId, restoredRequest!.SyncRequestId);

    ContractSyncStateMessage state = new()
    {
        SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
        SaveId = 445566,
        HostSessionId = hostSessionId,
        SyncRequestId = syncRequestId,
        StateVersion = 12
    };
    ContractSyncStateMessage? restoredState = JsonSerializer.Deserialize<ContractSyncStateMessage>(
        JsonSerializer.Serialize(state));
    Equal(hostSessionId, restoredState!.HostSessionId);
    Equal(syncRequestId, restoredState.SyncRequestId);
    Equal(12L, restoredState.StateVersion);
}

static void TestMultiplayerStaleSyncStateRejection()
{
    HostStateVersionTracker tracker = new();
    Equal(true, tracker.CanAccept(1));
    tracker.Commit(1);
    Equal(true, tracker.CanAccept(1));
    Equal(false, tracker.CanAccept(0));
    Equal(true, tracker.CanAccept(2));
    tracker.Commit(2);
    Equal(2L, tracker.Latest);
}

static void TestMultiplayerSnapshotSerialization()
{
    ContractSnapshotMessage source = new()
    {
        SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
        SaveId = 445566,
        HostSessionId = "host-session",
        ContractId = "contract-1",
        Sequence = 7,
        StateVersion = 12,
        RequestId = "request-1",
        RequestingPlayerId = 55,
        WorkerName = "Leah",
        Task = NamedFarmTask.FarmWork,
        HarvestDestination = HarvestDestinationMode.RequesterInventory,
        EfficiencyMultiplier = 1.10m,
        Phase = "Harvesting/TravelingToChest",
        ArrivalX = 78,
        ArrivalY = 15,
        ArrivalSide = FarmBoundarySide.East,
        EntranceSwitches = 1,
        TargetX = 10,
        TargetY = 20,
        ReservedGold = 600,
        CargoCount = 3,
        Cargo = new[]
        {
            new ContractCargoSnapshotMessage
            {
                TransferId = "transfer-1",
                QualifiedItemId = "(O)24",
                DisplayName = "Parsnip",
                Quality = 2,
                Stack = 3
            }
        },
        CompletedTransferIds = new[] { "transfer-0" }
    };

    string json = JsonSerializer.Serialize(source);
    ContractSnapshotMessage? restored = JsonSerializer.Deserialize<ContractSnapshotMessage>(json);
    Equal("contract-1", restored!.ContractId);
    Equal(12L, restored.StateVersion);
    Equal(NamedFarmTask.FarmWork, restored.Task);
    Equal(HarvestDestinationMode.RequesterInventory, restored.HarvestDestination);
    Equal(1.10m, restored.EfficiencyMultiplier);
    Equal(78, restored.ArrivalX);
    Equal(15, restored.ArrivalY);
    Equal(FarmBoundarySide.East, restored.ArrivalSide);
    Equal(1, restored.EntranceSwitches);
    Equal("(O)24", restored.Cargo[0].QualifiedItemId);
    Equal(3, restored.Cargo[0].Stack);
    Equal("transfer-0", restored.CompletedTransferIds[0]);
}

static void TestMultiplayerResultSerialization()
{
    Equal(9, MultiplayerContractProtocol.SchemaVersion);
    ContractResultMessage source = new()
    {
        SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
        SaveId = 445566,
        HostSessionId = "host-session",
        ContractId = "contract-1",
        Sequence = 8,
        StateVersion = 13,
        RequestId = "request-1",
        RequestingPlayerId = 55,
        WorkerName = "Leah",
        Task = NamedFarmTask.FarmWork,
        HarvestDestination = HarvestDestinationMode.RequesterInventory,
        Succeeded = true,
        CompletedWork = 3,
        PlayerItems = 2,
        ChestItems = 1,
        QuarantinedItems = 4
    };

    string json = JsonSerializer.Serialize(source);
    ContractResultMessage? restored = JsonSerializer.Deserialize<ContractResultMessage>(json);
    Equal(2, restored!.PlayerItems);
    Equal(1, restored.ChestItems);
    Equal(4, restored.QuarantinedItems);
    Equal(HarvestDestinationMode.RequesterInventory, restored.HarvestDestination);
    Equal(13L, restored.StateVersion);
}

static void TestMultiplayerStorageResultValidation()
{
    string contractId = Guid.NewGuid().ToString("N");
    string requestId = Guid.NewGuid().ToString("N");
    string transferId = Guid.NewGuid().ToString("N");
    ContractResultMessage result = new()
    {
        SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
        SaveId = 445566,
        HostSessionId = "host-session",
        ContractId = contractId,
        Sequence = 2,
        StateVersion = 3,
        RequestId = requestId,
        RequestingPlayerId = 55,
        WorkerName = "Leah",
        Task = NamedFarmTask.FarmWork,
        Succeeded = true,
        CompletedWork = 1,
        ChestItems = 10,
        BillableHours = 1,
        ChargedGold = 100,
        RefundedGold = 500,
        ProducedItems = new[]
        {
            new ContractCargoSnapshotMessage
            {
                TransferId = transferId,
                QualifiedItemId = "(O)24",
                DisplayName = "Parsnip",
                Stack = 10
            }
        },
        CompletedTransferIds = new[] { transferId },
        CompletedTransfers = new[]
        {
            new ContractTransferReportMessage
            {
                Sequence = 1,
                QualifiedItemId = "(O)24",
                DisplayName = "Parsnip",
                Category = -75,
                Quantity = 10,
                SourceX = 4,
                SourceY = 8,
                DestinationX = 10,
                DestinationY = 8
            }
        }
    };

    Equal(true, MultiplayerRecoveryState.IsValidResult(
        result,
        expectedSaveId: 445566,
        expectedProtocolSchemaVersion: MultiplayerContractProtocol.SchemaVersion));

    ContractResultMessage restored = JsonSerializer.Deserialize<ContractResultMessage>(
        JsonSerializer.Serialize(result))!;
    Equal(NamedFarmTask.FarmWork, restored.Task);
    Equal(1, restored.CompletedTransfers.Length);
    Equal(-75, restored.CompletedTransfers[0].Category);
    Equal(4, restored.CompletedTransfers[0].SourceX);
    Equal(10, restored.CompletedTransfers[0].DestinationX);

    result.CompletedTransfers[0].Quantity = 0;
    Equal(false, MultiplayerRecoveryState.IsValidResult(
        result,
        445566,
        MultiplayerContractProtocol.SchemaVersion));
    result.CompletedTransfers[0].Quantity = 10;

    result.CompletedTransfers[0].DestinationX = result.CompletedTransfers[0].SourceX;
    result.CompletedTransfers[0].DestinationY = result.CompletedTransfers[0].SourceY;
    Equal(false, MultiplayerRecoveryState.IsValidResult(
        result,
        445566,
        MultiplayerContractProtocol.SchemaVersion));
    result.CompletedTransfers[0].DestinationX = 10;

    result.SkippedTransfers = new[]
    {
        new ContractTransferReportMessage
        {
            Sequence = 2,
            QualifiedItemId = "(O)188",
            DisplayName = "Green Bean",
            Category = -75,
            Quantity = 1,
            SourceX = 6,
            SourceY = 8,
            DestinationX = 10,
            DestinationY = 8
        }
    };
    Equal(false, MultiplayerRecoveryState.IsValidResult(
        result,
        445566,
        MultiplayerContractProtocol.SchemaVersion));

    result.Succeeded = false;
    result.ReasonKey = "contract.failure.storage-changed";
    Equal(true, MultiplayerRecoveryState.IsValidResult(
        result,
        445566,
        MultiplayerContractProtocol.SchemaVersion));
}

static void TestNamedContractReportGrouping()
{
    ContractCargoSnapshotMessage[] items =
    {
        new() { DisplayName = "Parsnip", Quality = 2, Stack = 1 },
        new() { DisplayName = "Bean", Quality = 0, Stack = 3 },
        new() { DisplayName = "Parsnip", Quality = 0, Stack = 4 },
        new() { DisplayName = "Parsnip", Quality = 2, Stack = 2 }
    };

    IReadOnlyList<NamedContractReportItem> summarized =
        NamedContractReportFormatter.SummarizeItems(items);
    Equal(3, summarized.Count);
    Equal(new NamedContractReportItem("Bean", 0, 3), summarized[0]);
    Equal(new NamedContractReportItem("Parsnip", 0, 4), summarized[1]);
    Equal(new NamedContractReportItem("Parsnip", 2, 3), summarized[2]);
    Equal(
        "Bean q0 x3, Parsnip q0 x4, Parsnip q2 x3",
        NamedContractReportFormatter.FormatItems(items, "none"));
    Equal("none", NamedContractReportFormatter.FormatItems(
        Array.Empty<ContractCargoSnapshotMessage>(),
        "none"));
}

static ContractStartRequestMessage NewMultiplayerRequest(long playerId)
{
    return new ContractStartRequestMessage
    {
        SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
        ModVersion = "0.1.0",
        SaveId = 445566,
        TotalDays = 12,
        RequestId = Guid.NewGuid().ToString("N"),
        RequestingPlayerId = playerId,
        WorkerName = "Leah",
        Task = NamedFarmTask.FarmWork,
        HarvestDestination = HarvestDestinationMode.ClassifiedChests
    };
}

static RecurringContractSaveData NewRecurringContractState()
{
    return new RecurringContractSaveData
    {
        SchemaVersion = RecurringContractPolicy.SchemaVersion,
        Template = new RecurringContractTemplateData
        {
            Enabled = true,
            Task = NamedFarmTask.FarmWork,
            PreferredWorkerName = "Alex",
            WorkerMode = RecurringWorkerMode.PreferredWithApprovedSubstitutes,
            ApprovedSubstituteNames = new[] { "Leah", "Robin" },
            MaximumRegularDayGold = 720,
            AllowRestDays = true,
            MaximumRestDayGold = 2160,
            LastProcessedTotalDays = 11,
            LastRunId = "",
            PreviousSelectedWorkerName = "Leah",
            LastEvaluation = new RecurringEvaluationData()
        }
    };
}

static ContractStartResponseMessage NewStartResponse(long playerId, string requestId, long order)
{
    return new ContractStartResponseMessage
    {
        SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
        SaveId = 445566,
        HostSessionId = "host-session",
        HostOrder = order,
        RequestId = requestId,
        RequestingPlayerId = playerId,
        Accepted = true,
        ContractId = $"contract-{order}"
    };
}

static ContractSnapshotMessage NewSnapshot(string session, long sequence)
{
    return new ContractSnapshotMessage
    {
        SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
        SaveId = 445566,
        HostSessionId = session,
        ContractId = "contract-1",
        Sequence = sequence,
        Task = NamedFarmTask.FarmWork
    };
}

static void Equal<T>(T expected, T actual, [CallerLineNumber] int line = 0)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"line={line}, expected={expected}, actual={actual}");
}

static void Throws<TException>(Action action, [CallerLineNumber] int line = 0)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(
        $"line={line}, expected exception={typeof(TException).Name}");
}
