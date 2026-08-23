using EvilFarmOwner;

List<(string Name, Action Test)> tests = new()
{
    ("regular high-risk wage", TestRegularHighRiskWage),
    ("rest-day triple wage", TestRestDayTripleWage),
    ("trusted wage", TestTrustedWage),
    ("pre-dispatch settlement", TestPreDispatchSettlement),
    ("dispatched settlement", TestDispatchedSettlement),
    ("target ordering", TestTargetOrdering),
    ("one-tile contract limit", TestOneTileLimit)
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
    WateringContractPreview preview = ContractPreviewService.Create(friendshipHearts: 0, dayOfMonth: 1);
    Equal(ContractDayKind.RegularWorkday, preview.DayKind);
    Equal(120, preview.MinimumCalloutWage);
    Equal(720, preview.MaximumAuthorizedWage);
}

static void TestRestDayTripleWage()
{
    WateringContractPreview preview = ContractPreviewService.Create(friendshipHearts: 0, dayOfMonth: 6);
    Equal(ContractDayKind.RestDay, preview.DayKind);
    Equal(3.00m, preview.DayMultiplier);
    Equal(360, preview.MinimumCalloutWage);
    Equal(2160, preview.MaximumAuthorizedWage);
}

static void TestTrustedWage()
{
    WateringContractPreview preview = ContractPreviewService.Create(friendshipHearts: 8, dayOfMonth: 2);
    Equal(FriendshipWageBand.Trusted, preview.FriendshipBand);
    Equal(90, preview.MinimumCalloutWage);
    Equal(540, preview.MaximumAuthorizedWage);
}

static void TestPreDispatchSettlement()
{
    WateringContractPreview preview = ContractPreviewService.Create(friendshipHearts: 4, dayOfMonth: 1);
    WateringContractSettlement settlement = WateringContractSettlement.Create(preview, dispatched: false);
    Equal(600, settlement.ReservedGold);
    Equal(0, settlement.ChargedGold);
    Equal(600, settlement.RefundedGold);
}

static void TestDispatchedSettlement()
{
    WateringContractPreview preview = ContractPreviewService.Create(friendshipHearts: 4, dayOfMonth: 1);
    WateringContractSettlement settlement = WateringContractSettlement.Create(preview, dispatched: true);
    Equal(600, settlement.ReservedGold);
    Equal(100, settlement.ChargedGold);
    Equal(500, settlement.RefundedGold);
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

static void TestOneTileLimit()
{
    WateringContractPreview preview = ContractPreviewService.Create(friendshipHearts: 4, dayOfMonth: 1);
    Equal(1, preview.MaximumWaterTiles);
}

static void Equal<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected={expected}, actual={actual}");
}
