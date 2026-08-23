namespace EvilFarmOwner;

internal readonly record struct GridPoint(int X, int Y);

internal readonly record struct WateringTargetOption(
    GridPoint Target,
    GridPoint Interaction);

internal static class WateringTargetSelection
{
    public static IReadOnlyList<WateringTargetOption> Order(
        GridPoint start,
        IEnumerable<WateringTargetOption> options)
    {
        return options
            .OrderBy(option => ManhattanDistance(start, option.Interaction))
            .ThenBy(option => option.Target.Y)
            .ThenBy(option => option.Target.X)
            .ThenBy(option => option.Interaction.Y)
            .ThenBy(option => option.Interaction.X)
            .ToArray();
    }

    private static int ManhattanDistance(GridPoint first, GridPoint second)
    {
        return Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y);
    }
}
