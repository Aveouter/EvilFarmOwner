using EvilFarmOwner;
using System.Text.Json;

List<(string Name, Action Test)> tests = new()
{
    ("regular high-risk wage", TestRegularHighRiskWage),
    ("rest-day triple wage", TestRestDayTripleWage),
    ("trusted wage", TestTrustedWage),
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
    ("external boundary arrival ordering", TestExternalBoundaryArrivalOrdering),
    ("right entrance priority", TestRightEntrancePriority),
    ("stalled entrance fallback ordering", TestStalledEntranceFallbackOrdering),
    ("nearest arrival boundary side", TestNearestArrivalBoundarySide),
    ("NPC lease recovery policy", TestNpcLeaseRecoveryPolicy),
    ("six-hour wage cap", TestSixHourWageCap),
    ("harvest chest match priority", TestHarvestChestMatchPriority),
    ("harvest chest full acceptance", TestHarvestChestFullAcceptance),
    ("harvest route cost before spare capacity", TestHarvestRouteCostBeforeSpareCapacity),
    ("harvest partial remainder", TestHarvestPartialRemainder),
    ("regrowing harvest capture semantics", TestRegrowingHarvestCaptureSemantics),
    ("harvest overflow fallback", TestHarvestOverflowFallback),
    ("harvest transfer replay protection", TestHarvestTransferReplayProtection),
    ("harvest placement conservation", TestHarvestPlacementConservation),
    ("emergency drop tile ordering", TestEmergencyDropTileOrdering),
    ("multiplayer request authorization", TestMultiplayerRequestAuthorization),
    ("multiplayer request replay", TestMultiplayerRequestReplay),
    ("multiplayer deterministic order", TestMultiplayerDeterministicOrder),
    ("multiplayer reconnect ledger", TestMultiplayerReconnectLedger),
    ("multiplayer restart recovery state", TestMultiplayerRestartRecoveryState),
    ("multiplayer stale snapshot rejection", TestMultiplayerStaleSnapshotRejection),
    ("multiplayer stale sync-state rejection", TestMultiplayerStaleSyncStateRejection),
    ("multiplayer snapshot serialization", TestMultiplayerSnapshotSerialization),
    ("multiplayer result serialization", TestMultiplayerResultSerialization)
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

static void TestHarvestChestMatchPriority()
{
    HarvestChestOption[] options =
    {
        new(new GridPoint(1, 1), new GridPoint(1, 2), HarvestChestMatchKind.SameItem, 999, 10, 1),
        new(new GridPoint(8, 8), new GridPoint(8, 9), HarvestChestMatchKind.ExactStack, 1, 10, 20),
        new(new GridPoint(2, 2), new GridPoint(2, 3), HarvestChestMatchKind.SameGroup, 999, 10, 2),
        new(new GridPoint(3, 3), new GridPoint(3, 4), HarvestChestMatchKind.AvailableCapacity, 9999, 10, 1)
    };

    IReadOnlyList<HarvestChestOption> ordered = HarvestChestRanking.Order(options);
    Equal(HarvestChestMatchKind.ExactStack, ordered[0].MatchKind);
    Equal(HarvestChestMatchKind.SameItem, ordered[1].MatchKind);
    Equal(HarvestChestMatchKind.SameGroup, ordered[2].MatchKind);
    Equal(HarvestChestMatchKind.AvailableCapacity, ordered[3].MatchKind);
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
}

static void TestHarvestRouteCostBeforeSpareCapacity()
{
    HarvestChestOption[] options =
    {
        new(new GridPoint(1, 1), new GridPoint(1, 2), HarvestChestMatchKind.AvailableCapacity, 5, 10, 1),
        new(new GridPoint(8, 8), new GridPoint(8, 9), HarvestChestMatchKind.AvailableCapacity, 20, 10, 20),
        new(new GridPoint(2, 2), new GridPoint(2, 3), HarvestChestMatchKind.AvailableCapacity, 12, 10, 2)
    };

    IReadOnlyList<HarvestChestOption> ordered = HarvestChestRanking.Order(options);
    Equal(new GridPoint(2, 2), ordered[0].ChestTile);
    Equal(new GridPoint(8, 8), ordered[1].ChestTile);
    Equal(new GridPoint(1, 1), ordered[2].ChestTile);
}

static void TestHarvestPartialRemainder()
{
    Equal(6, HarvestTransferMath.GetDeliveredCount(requestedStack: 10, remainingStack: 4));
    Equal(0, HarvestTransferMath.GetDeliveredCount(requestedStack: 10, remainingStack: 10));
    Equal(10, HarvestTransferMath.GetDeliveredCount(requestedStack: 10, remainingStack: 0));
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

static void TestHarvestPlacementConservation()
{
    Equal(true, HarvestPlacementAudit.IsBalanced(
        harvested: 17,
        playerInventory: 3,
        chest: 7,
        overflow: 4,
        dropped: 3,
        unresolved: 0));
    Equal(false, HarvestPlacementAudit.IsBalanced(
        harvested: 17,
        playerInventory: 3,
        chest: 7,
        overflow: 4,
        dropped: 2,
        unresolved: 0));
    Equal(true, HarvestPlacementAudit.IsBalanced(
        harvested: 17,
        playerInventory: 3,
        chest: 7,
        overflow: 4,
        dropped: 2,
        unresolved: 1));
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

static void TestHarvestOverflowFallback()
{
    Equal(
        HarvestFallbackDestination.Chest,
        HarvestDeliveryFallback.Select(
            hasEligibleChest: true,
            requesterOnFarm: true,
            playerInventoryCanAccept: true,
            persistentOverflowAvailable: true));
    Equal(
        HarvestFallbackDestination.PlayerInventory,
        HarvestDeliveryFallback.Select(
            hasEligibleChest: false,
            requesterOnFarm: true,
            playerInventoryCanAccept: true,
            persistentOverflowAvailable: true));
    Equal(
        HarvestFallbackDestination.PersistentOverflow,
        HarvestDeliveryFallback.Select(
            hasEligibleChest: false,
            requesterOnFarm: false,
            playerInventoryCanAccept: true,
            persistentOverflowAvailable: true));
    Equal(
        HarvestFallbackDestination.PersistentOverflow,
        HarvestDeliveryFallback.Select(
            hasEligibleChest: false,
            requesterOnFarm: true,
            playerInventoryCanAccept: false,
            persistentOverflowAvailable: true));
    Equal(
        HarvestFallbackDestination.VisibleGroundDrop,
        HarvestDeliveryFallback.Select(
            hasEligibleChest: false,
            requesterOnFarm: true,
            playerInventoryCanAccept: false,
            persistentOverflowAvailable: false));
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
    valid.Task = (NamedFarmTask)999;
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
        Task = NamedFarmTask.Watering,
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
        Task = NamedFarmTask.Watering
    };
    Equal(true, tracker.TryAccept(result, MultiplayerContractProtocol.SchemaVersion, expectedSaveId: 445566));
    Equal(false, tracker.TryAccept(result, MultiplayerContractProtocol.SchemaVersion, expectedSaveId: 445566));
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
        Task = NamedFarmTask.Harvesting,
        Phase = "TravelingToChest",
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
    Equal(NamedFarmTask.Harvesting, restored.Task);
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
    Equal(3, MultiplayerContractProtocol.SchemaVersion);
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
        Task = NamedFarmTask.Harvesting,
        Succeeded = true,
        CompletedWork = 3,
        PlayerItems = 2,
        ChestItems = 1
    };

    string json = JsonSerializer.Serialize(source);
    ContractResultMessage? restored = JsonSerializer.Deserialize<ContractResultMessage>(json);
    Equal(2, restored!.PlayerItems);
    Equal(1, restored.ChestItems);
    Equal(13L, restored.StateVersion);
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
        Task = NamedFarmTask.Watering
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
        Task = NamedFarmTask.Watering
    };
}

static void Equal<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected={expected}, actual={actual}");
}
