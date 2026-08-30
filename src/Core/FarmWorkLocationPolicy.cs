namespace EvilFarmOwner;

[Flags]
internal enum FarmWorkScopeSelection
{
    None = 0,
    MainFarm = 1 << 0,
    Greenhouse = 1 << 1,
    FarmBuildingInteriors = 1 << 2,
    All = MainFarm | Greenhouse | FarmBuildingInteriors
}

internal enum FarmWorkLocationKind
{
    MainFarm = 0,
    Greenhouse = 1,
    FarmBuildingInterior = 2
}

internal sealed record FarmWorkLocationIdentity(
    string LocationKey,
    FarmWorkLocationKind Kind,
    int ExteriorDoorX,
    int ExteriorDoorY,
    string StableBuildingId);

internal static class FarmWorkLocationPolicy
{
    public static IReadOnlyList<FarmWorkLocationIdentity> Order(
        IEnumerable<FarmWorkLocationIdentity> locations)
    {
        return locations
            .Where(location => !string.IsNullOrWhiteSpace(location.LocationKey))
            .DistinctBy(location => location.LocationKey, StringComparer.Ordinal)
            .OrderBy(location => location.Kind)
            .ThenBy(location => location.ExteriorDoorY)
            .ThenBy(location => location.ExteriorDoorX)
            .ThenBy(location => location.StableBuildingId, StringComparer.Ordinal)
            .ThenBy(location => location.LocationKey, StringComparer.Ordinal)
            .ToArray();
    }

    public static FarmWorkScopeSelection Normalize(FarmWorkScopeSelection value)
    {
        FarmWorkScopeSelection normalized = value & FarmWorkScopeSelection.All;
        return (normalized & FarmWorkScopeSelection.MainFarm) != 0
            ? normalized
            : normalized | FarmWorkScopeSelection.MainFarm;
    }

    public static bool IsEnabled(FarmWorkLocationKind kind, FarmWorkScopeSelection scope)
    {
        FarmWorkScopeSelection flag = kind switch
        {
            FarmWorkLocationKind.MainFarm => FarmWorkScopeSelection.MainFarm,
            FarmWorkLocationKind.Greenhouse => FarmWorkScopeSelection.Greenhouse,
            FarmWorkLocationKind.FarmBuildingInterior => FarmWorkScopeSelection.FarmBuildingInteriors,
            _ => FarmWorkScopeSelection.None
        };
        return flag != FarmWorkScopeSelection.None && (scope & flag) != 0;
    }
}
