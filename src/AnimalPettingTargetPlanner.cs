using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;

namespace EvilFarmOwner;

internal sealed record AnimalPettingTargetPlan(
    long AnimalId,
    Point TargetTile,
    Point InteractionTile,
    int FacingDirection,
    Stack<Point> Path);

internal sealed record AnimalPettingWorkPlan(
    Point ArrivalTile,
    FarmBoundarySide ArrivalSide,
    AnimalPettingTargetPlan? FirstTarget);

internal sealed record AnimalPettingRouteOption(
    long AnimalId,
    GridPoint Target,
    GridPoint Interaction,
    int Distance);

internal sealed class AnimalPettingTargetPlanner
{
    private static readonly Point[] InteractionOffsets =
    {
        new(0, 1), new(-1, 0), new(1, 0), new(0, -1)
    };

    private readonly IMonitor Monitor;

    public AnimalPettingTargetPlanner(IMonitor monitor)
    {
        this.Monitor = monitor;
    }

    public AnimalPettingWorkPlan? TryCreate(Farm farm, NPC worker)
    {
        int width = farm.Map.Layers[0].LayerWidth;
        int height = farm.Map.Layers[0].LayerHeight;
        foreach (GridPoint candidate in FarmEntranceSelection.OrderBoundaryArrivalCandidates(
                     width,
                     height,
                     farm.warps.Select(warp => new GridPoint(warp.X, warp.Y))))
        {
            Vector2 tile = new(candidate.X, candidate.Y);
            if (farm.warps.Any(warp => warp.X == candidate.X && warp.Y == candidate.Y)
                || farm.doors.ContainsKey(new Point(candidate.X, candidate.Y))
                || !farm.CanSpawnCharacterHere(tile))
                continue;

            Point arrival = new(candidate.X, candidate.Y);
            AnimalPettingTargetPlan? target = this.TryFindNext(
                farm, worker, arrival, new HashSet<long>());
            if (target is not null && FarmNavigationMap.CanBeginPath(
                    farm, worker, arrival, target.Path, out _))
            {
                return new AnimalPettingWorkPlan(
                    arrival,
                    FarmEntranceSelection.GetNearestBoundarySide(width, height, candidate),
                    target);
            }
        }
        return null;
    }

    public AnimalPettingTargetPlan? TryFindNext(
        GameLocation farm,
        NPC worker,
        Point startTile,
        IReadOnlySet<long> attemptedAnimalIds)
    {
        if (!FarmNavigationMap.TryBuild(farm, worker, startTile, this.Monitor, out GridRouteMap? routes)
            || routes is null)
            return null;

        List<AnimalPettingRouteOption> options = new();
        foreach (FarmAnimal animal in farm.animals.Values)
        {
            long id = animal.myID.Value;
            if (attemptedAnimalIds.Contains(id)
                || !ReferenceEquals(animal.currentLocation, farm)
                || AnimalPettingPolicy.GetSkipReason(
                    Context.IsMainPlayer,
                    animal.wasPet.Value,
                    Game1.timeOfDay >= 1900) != AnimalCareSkipReason.None)
                continue;

            Point target = animal.TilePoint;
            foreach (Point offset in InteractionOffsets)
            {
                Point interaction = new(target.X + offset.X, target.Y + offset.Y);
                if (routes.TryGetDistance(new GridPoint(interaction.X, interaction.Y), out int distance))
                    options.Add(new AnimalPettingRouteOption(
                        id,
                        new GridPoint(target.X, target.Y),
                        new GridPoint(interaction.X, interaction.Y),
                        distance));
            }
        }

        if (options.Count == 0)
            return null;

        AnimalPettingRouteOption best = OrderOptions(options)
            .First();
        if (!routes.TryGetPath(
                new GridPoint(best.Interaction.X, best.Interaction.Y),
                out IReadOnlyList<GridPoint> path))
            return null;

        return new AnimalPettingTargetPlan(
            best.AnimalId,
            new Point(best.Target.X, best.Target.Y),
            new Point(best.Interaction.X, best.Interaction.Y),
            GetFacingDirection(
                new Point(best.Interaction.X, best.Interaction.Y),
                new Point(best.Target.X, best.Target.Y)),
            FarmNavigationMap.ToPath(path));
    }

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

    public Stack<Point>? TryCreateReturnPath(GameLocation farm, NPC worker, Point arrivalTile)
    {
        if (!FarmNavigationMap.TryBuild(farm, worker, worker.TilePoint, this.Monitor, out GridRouteMap? routes)
            || routes is null
            || !routes.TryGetPath(new GridPoint(arrivalTile.X, arrivalTile.Y), out IReadOnlyList<GridPoint> path))
            return null;
        return FarmNavigationMap.ToPath(path);
    }

    private static int GetFacingDirection(Point interaction, Point target)
    {
        if (target.X > interaction.X) return Game1.right;
        if (target.X < interaction.X) return Game1.left;
        if (target.Y > interaction.Y) return Game1.down;
        return Game1.up;
    }
}
