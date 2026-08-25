using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

namespace EvilFarmOwner;

internal sealed record AnimalFeedingTargetPlan(
    Point TargetTile,
    Point InteractionTile,
    int FacingDirection,
    Stack<Point> Path);

internal sealed class AnimalFeedingTargetPlanner
{
    private static readonly Point[] InteractionOffsets =
    {
        new(0, 1), new(-1, 0), new(1, 0), new(0, -1)
    };
    private readonly IMonitor Monitor;

    public AnimalFeedingTargetPlanner(IMonitor monitor) => this.Monitor = monitor;

    public AnimalFeedingTargetPlan? TryFindNext(
        AnimalHouse house,
        NPC worker,
        Point startTile,
        IReadOnlySet<Point> attemptedTiles)
    {
        if (!FarmNavigationMap.TryBuild(house, worker, startTile, this.Monitor, out GridRouteMap? routes)
            || routes is null)
            return null;
        List<(Point Target, Point Interaction, int Distance)> options = new();
        int width = house.Map.Layers[0].LayerWidth;
        int height = house.Map.Layers[0].LayerHeight;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Point target = new(x, y);
                if (attemptedTiles.Contains(target)
                    || house.objects.ContainsKey(target.ToVector2())
                    || house.doesTileHaveProperty(x, y, "Trough", "Back") is null)
                    continue;
                foreach (Point offset in InteractionOffsets)
                {
                    Point interaction = new(x + offset.X, y + offset.Y);
                    if (routes.TryGetDistance(new GridPoint(interaction.X, interaction.Y), out int distance))
                        options.Add((target, interaction, distance));
                }
            }
        }
        if (options.Count == 0)
            return null;
        (Point Target, Point Interaction, int Distance) best = options
            .OrderBy(option => option.Distance)
            .ThenBy(option => option.Target.Y)
            .ThenBy(option => option.Target.X)
            .ThenBy(option => option.Interaction.Y)
            .ThenBy(option => option.Interaction.X)
            .First();
        if (!routes.TryGetPath(new GridPoint(best.Interaction.X, best.Interaction.Y), out IReadOnlyList<GridPoint> path))
            return null;
        return new AnimalFeedingTargetPlan(
            best.Target,
            best.Interaction,
            GetFacingDirection(best.Interaction, best.Target),
            FarmNavigationMap.ToPath(path));
    }

    public static bool HasEmptyTrough(AnimalHouse house)
    {
        int width = house.Map.Layers[0].LayerWidth;
        int height = house.Map.Layers[0].LayerHeight;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (house.doesTileHaveProperty(x, y, "Trough", "Back") is not null
                    && !house.objects.ContainsKey(new Vector2(x, y)))
                    return true;
            }
        }
        return false;
    }

    private static int GetFacingDirection(Point interaction, Point target)
    {
        if (target.X > interaction.X) return Game1.right;
        if (target.X < interaction.X) return Game1.left;
        if (target.Y > interaction.Y) return Game1.down;
        return Game1.up;
    }
}
