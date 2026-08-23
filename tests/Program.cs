using EvilFarmOwner;

List<(string Name, Action Test)> tests = new()
{
    ("regular high-risk wage", TestRegularHighRiskWage),
    ("rest-day triple wage", TestRestDayTripleWage),
    ("trusted wage", TestTrustedWage),
    ("pre-dispatch settlement", TestPreDispatchSettlement),
    ("dispatched one-hour settlement", TestDispatchedSettlement),
    ("elapsed multi-hour settlement", TestElapsedMultiHourSettlement),
    ("target ordering", TestTargetOrdering),
    ("left entrance selection", TestLeftEntranceSelection),
    ("six-hour wage cap", TestSixHourWageCap),
    ("harvest chest match priority", TestHarvestChestMatchPriority),
    ("harvest chest full acceptance", TestHarvestChestFullAcceptance),
    ("harvest partial remainder", TestHarvestPartialRemainder),
    ("harvest overflow fallback", TestHarvestOverflowFallback),
    ("harvest transfer replay protection", TestHarvestTransferReplayProtection)
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
    GridPoint start = new(5, 5);
    WateringTargetOption[] unordered =
    {
        new(new GridPoint(9, 5), new GridPoint(8, 5)),
        new(new GridPoint(5, 7), new GridPoint(5, 6)),
        new(new GridPoint(4, 6), new GridPoint(5, 6)),
        new(new GridPoint(2, 5), new GridPoint(3, 5))
    };

    IReadOnlyList<WateringTargetOption> ordered = WateringTargetSelection.Order(start, unordered);
    Equal(new GridPoint(4, 6), ordered[0].Target);
    Equal(new GridPoint(5, 7), ordered[1].Target);
    Equal(new GridPoint(2, 5), ordered[2].Target);
    Equal(new GridPoint(9, 5), ordered[3].Target);
}

static void TestLeftEntranceSelection()
{
    GridPoint selected = FarmEntranceSelection.SelectLeftEntrance(
        mapWidth: 100,
        mapHeight: 80,
        new[]
        {
            new GridPoint(99, 20),
            new GridPoint(40, 79),
            new GridPoint(0, 24)
        });

    Equal(new GridPoint(1, 24), selected);
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
        new(new GridPoint(2, 2), new GridPoint(2, 3), HarvestChestMatchKind.SameGroup, 999, 10, 2)
    };

    IReadOnlyList<HarvestChestOption> ordered = HarvestChestRanking.Order(options);
    Equal(HarvestChestMatchKind.ExactStack, ordered[0].MatchKind);
    Equal(HarvestChestMatchKind.SameItem, ordered[1].MatchKind);
    Equal(HarvestChestMatchKind.SameGroup, ordered[2].MatchKind);
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

static void TestHarvestPartialRemainder()
{
    Equal(6, HarvestTransferMath.GetDeliveredCount(requestedStack: 10, remainingStack: 4));
    Equal(0, HarvestTransferMath.GetDeliveredCount(requestedStack: 10, remainingStack: 10));
    Equal(10, HarvestTransferMath.GetDeliveredCount(requestedStack: 10, remainingStack: 0));
}

static void TestHarvestOverflowFallback()
{
    Equal(
        HarvestFallbackDestination.PersistentOverflow,
        HarvestDeliveryFallback.Select(hasEligibleChest: false, persistentOverflowAvailable: true));
    Equal(
        HarvestFallbackDestination.VisibleGroundDrop,
        HarvestDeliveryFallback.Select(hasEligibleChest: false, persistentOverflowAvailable: false));
}

static void TestHarvestTransferReplayProtection()
{
    HarvestTransferLedger ledger = new();
    int applied = 0;
    Equal(true, ledger.TryApply("transfer-1", () => applied++));
    Equal(false, ledger.TryApply("transfer-1", () => applied++));
    Equal(1, applied);
}

static void Equal<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected={expected}, actual={actual}");
}
