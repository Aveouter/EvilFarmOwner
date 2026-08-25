using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;

namespace EvilFarmOwner;

internal sealed record AnimalHouseRoutePlan(
    Guid BuildingId,
    Building Building,
    AnimalHouse House,
    Point ExteriorDoorTile,
    Point ExteriorInteractionTile,
    Point InteriorEntryTile,
    Stack<Point> ExteriorPath,
    int Distance);

internal sealed record AnimalHouseWorkPlan(
    Point ArrivalTile,
    FarmBoundarySide ArrivalSide,
    AnimalHouseRoutePlan FirstHouse);

internal sealed class AnimalHouseRoutePlanner
{
    private readonly IMonitor Monitor;

    public AnimalHouseRoutePlanner(IMonitor monitor)
    {
        this.Monitor = monitor;
    }

    public AnimalHouseWorkPlan? TryCreate(Farm farm, NPC worker)
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
            AnimalHouseRoutePlan? house = this.TryFindNext(
                farm,
                worker,
                arrival,
                new HashSet<Guid>());
            if (house is not null
                && FarmNavigationMap.CanBeginPath(
                    farm, worker, arrival, house.ExteriorPath, out _))
            {
                return new AnimalHouseWorkPlan(
                    arrival,
                    FarmEntranceSelection.GetNearestBoundarySide(width, height, candidate),
                    house);
            }
        }
        return null;
    }

    public AnimalHouseRoutePlan? TryFindNext(
        Farm farm,
        NPC worker,
        Point startTile,
        IReadOnlySet<Guid> visitedBuildingIds)
    {
        if (!FarmNavigationMap.TryBuild(farm, worker, startTile, this.Monitor, out GridRouteMap? routes)
            || routes is null)
            return null;

        List<AnimalHouseRoutePlan> candidates = new();
        foreach (Building building in farm.buildings)
        {
            Guid buildingId = building.id.Value;
            if (visitedBuildingIds.Contains(buildingId)
                || building.daysOfConstructionLeft.Value > 0
                || building.daysUntilUpgrade.Value > 0
                || building.GetIndoors() is not AnimalHouse house
                || house.warps.Count == 0
                || !HasEligibleWork(house))
                continue;

            Point door = building.getPointForHumanDoor();
            Point interaction = new(door.X, door.Y + 1);
            if (!routes.TryGetDistance(
                    new GridPoint(interaction.X, interaction.Y),
                    out int distance)
                || !routes.TryGetPath(
                    new GridPoint(interaction.X, interaction.Y),
                    out IReadOnlyList<GridPoint> path))
                continue;

            Warp warp = house.warps[0];
            Point interiorEntry = new(warp.X, warp.Y - 1);
            if (interiorEntry.X < 0
                || interiorEntry.Y < 0
                || interiorEntry.X >= house.Map.Layers[0].LayerWidth
                || interiorEntry.Y >= house.Map.Layers[0].LayerHeight)
                continue;

            candidates.Add(new AnimalHouseRoutePlan(
                buildingId,
                building,
                house,
                door,
                interaction,
                interiorEntry,
                FarmNavigationMap.ToPath(path),
                distance));
        }

        return candidates
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.ExteriorDoorTile.Y)
            .ThenBy(candidate => candidate.ExteriorDoorTile.X)
            .ThenBy(candidate => candidate.BuildingId)
            .FirstOrDefault();
    }

    public static bool HasEligibleWork(AnimalHouse house)
    {
        return AnimalFeedingTargetPlanner.HasEmptyTrough(house)
            || house.animals.Values.Any(animal =>
            ReferenceEquals(animal.currentLocation, house)
            && AnimalPettingPolicy.GetSkipReason(
                Context.IsMainPlayer,
                animal.wasPet.Value,
                Game1.timeOfDay >= 1900) == AnimalCareSkipReason.None);
    }
}
