namespace EvilFarmOwner;

internal sealed record AnimalPettingRouteOption(
    long AnimalId,
    GridPoint Target,
    GridPoint Interaction,
    int Distance);

internal sealed partial class AnimalPettingTargetPlanner
{
    public static IReadOnlyList<AnimalPettingRouteOption> OrderOptions(
        IEnumerable<AnimalPettingRouteOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options
            .OrderBy(option => option.Distance)
            .ThenBy(option => option.Target.Y)
            .ThenBy(option => option.Target.X)
            .ThenBy(option => option.Interaction.Y)
            .ThenBy(option => option.Interaction.X)
            .ThenBy(option => option.AnimalId)
            .ToArray();
    }
}

internal sealed record AnimalProductRouteOption(
    string StableKey,
    GridPoint Target,
    GridPoint Interaction,
    int Distance);

internal sealed partial class AnimalProductTargetPlanner
{
    public static IReadOnlyList<AnimalProductRouteOption> OrderOptions(
        IEnumerable<AnimalProductRouteOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options
            .OrderBy(option => option.Distance)
            .ThenBy(option => option.Target.Y)
            .ThenBy(option => option.Target.X)
            .ThenBy(option => option.Interaction.Y)
            .ThenBy(option => option.Interaction.X)
            .ThenBy(option => option.StableKey, StringComparer.Ordinal)
            .ToArray();
    }
}
