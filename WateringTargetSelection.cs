namespace EvilFarmOwner;

internal readonly record struct GridPoint(int X, int Y);

internal readonly record struct FarmTaskRouteEdge(
    GridPoint Target,
    GridPoint Interaction);

internal readonly record struct FarmTaskRouteOption(
    GridPoint Target,
    GridPoint Interaction,
    int PathCost);

internal static class FarmTaskRouteSelection
{
    public static IReadOnlyList<FarmTaskRouteOption> Order(
        IEnumerable<FarmTaskRouteOption> options)
    {
        return options
            .OrderBy(option => option.PathCost)
            .ThenBy(option => option.Target.Y)
            .ThenBy(option => option.Target.X)
            .ThenBy(option => option.Interaction.Y)
            .ThenBy(option => option.Interaction.X)
            .ToArray();
    }
}
