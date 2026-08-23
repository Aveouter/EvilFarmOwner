namespace EvilFarmOwner;

internal enum ContractDayKind
{
    RegularWorkday,
    RestDay
}

internal enum FriendshipWageBand
{
    HighRisk,
    ElevatedRisk,
    Standard,
    Trusted
}

internal sealed record WateringContractPreview(
    int FriendshipHearts,
    FriendshipWageBand FriendshipBand,
    decimal FriendshipMultiplier,
    ContractDayKind DayKind,
    decimal DayMultiplier,
    int BaseHourlyWage,
    int RegularShiftHours,
    decimal EfficiencyMultiplier,
    bool OvertimeEnabled,
    decimal OvertimeMultiplier,
    int MaximumOvertimeHours,
    int EstimatedRegularWage,
    int MaximumAuthorizedWage,
    int MinimumCalloutWage,
    int MaximumWaterTiles);
