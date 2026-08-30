using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;

namespace EvilFarmOwner;

internal sealed record FarmWorkLocationPlan(
    FarmWorkLocationIdentity Identity,
    GameLocation Location,
    Point ArrivalTile,
    Point FarmReturnTile,
    bool IsMainFarm);

internal static class FarmWorkLocationScope
{
    public static IReadOnlyList<FarmWorkLocationPlan> Create(
        Farm farm,
        FarmWorkScopeSelection requestedScope)
    {
        FarmWorkScopeSelection scope = FarmWorkLocationPolicy.Normalize(requestedScope);
        List<FarmWorkLocationPlan> plans = new()
        {
            new FarmWorkLocationPlan(
                new FarmWorkLocationIdentity(
                    farm.NameOrUniqueName,
                    FarmWorkLocationKind.MainFarm,
                    0,
                    0,
                    ""),
                farm,
                Point.Zero,
                Point.Zero,
                IsMainFarm: true)
        };

        foreach (Building building in farm.buildings)
        {
            if (building.daysOfConstructionLeft.Value > 0
                || building.daysUntilUpgrade.Value > 0
                || building.GetIndoors() is not GameLocation indoors
                || indoors is AnimalHouse
                || indoors is FarmHouse
                || indoors.warps.Count == 0)
                continue;

            string locationKey = indoors.NameOrUniqueName ?? indoors.Name;
            if (string.IsNullOrWhiteSpace(locationKey)
                || locationKey.StartsWith("Island", StringComparison.OrdinalIgnoreCase))
                continue;

            FarmWorkLocationKind kind = building is GreenhouseBuilding || indoors.IsGreenhouse
                ? FarmWorkLocationKind.Greenhouse
                : FarmWorkLocationKind.FarmBuildingInterior;
            if (!FarmWorkLocationPolicy.IsEnabled(kind, scope))
                continue;

            Point door = building.getPointForHumanDoor();
            Point? exteriorInteraction = FindSafeFarmReturnTile(
                farm,
                new Point(door.X, door.Y + 1));
            Warp warp = indoors.warps[0];
            Point interiorEntry = new(warp.X, Math.Max(0, warp.Y - 1));
            if (!IsInBounds(indoors, interiorEntry) || exteriorInteraction is null)
                continue;

            plans.Add(new FarmWorkLocationPlan(
                new FarmWorkLocationIdentity(
                    locationKey,
                    kind,
                    door.X,
                    door.Y,
                    building.id.Value.ToString("N")),
                indoors,
                interiorEntry,
                exteriorInteraction.Value,
                IsMainFarm: false));
        }

        TryAddWarpLinkedLocation(
            plans,
            farm,
            "Greenhouse",
            FarmWorkLocationKind.Greenhouse,
            scope);
        TryAddWarpLinkedLocation(
            plans,
            farm,
            "FarmCave",
            FarmWorkLocationKind.FarmBuildingInterior,
            scope);

        IReadOnlyDictionary<string, FarmWorkLocationPlan> byKey = plans
            .GroupBy(plan => plan.Identity.LocationKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return FarmWorkLocationPolicy.Order(plans.Select(plan => plan.Identity))
            .Select(identity => byKey[identity.LocationKey])
            .ToArray();
    }

    private static void TryAddWarpLinkedLocation(
        ICollection<FarmWorkLocationPlan> plans,
        Farm farm,
        string vanillaLocationName,
        FarmWorkLocationKind kind,
        FarmWorkScopeSelection scope)
    {
        if (!FarmWorkLocationPolicy.IsEnabled(kind, scope))
            return;

        GameLocation? location = Game1.getLocationFromName(vanillaLocationName);
        if (location is null
            || location is FarmHouse
            || location is AnimalHouse
            || location.warps.Count == 0
            || plans.Any(plan => ReferenceEquals(plan.Location, location)))
            return;

        Warp? farmWarp = farm.warps.FirstOrDefault(warp =>
            string.Equals(warp.TargetName, vanillaLocationName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(warp.TargetName, location.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(warp.TargetName, location.NameOrUniqueName, StringComparison.OrdinalIgnoreCase));
        if (farmWarp is null)
            return;

        Warp interiorWarp = location.warps[0];
        Point arrival = new(interiorWarp.X, Math.Max(0, interiorWarp.Y - 1));
        Point? farmReturn = FindSafeFarmReturnTile(farm, new Point(
            farmWarp.X,
            Math.Min(farm.Map.Layers[0].LayerHeight - 1, farmWarp.Y + 1)));
        if (!IsInBounds(location, arrival) || farmReturn is null)
            return;

        plans.Add(new FarmWorkLocationPlan(
            new FarmWorkLocationIdentity(
                location.NameOrUniqueName,
                kind,
                farmWarp.X,
                farmWarp.Y,
                $"vanilla:{vanillaLocationName}"),
            location,
            arrival,
            farmReturn.Value,
            IsMainFarm: false));
    }

    private static Point? FindSafeFarmReturnTile(Farm farm, Point preferred)
    {
        const int maximumRadius = 3;
        for (int radius = 0; radius <= maximumRadius; radius++)
        {
            IEnumerable<Point> candidates = radius == 0
                ? new[] { preferred }
                : Enumerable.Range(-radius, radius * 2 + 1)
                    .SelectMany(offsetX => new[]
                    {
                        new Point(preferred.X + offsetX, preferred.Y - radius),
                        new Point(preferred.X + offsetX, preferred.Y + radius)
                    })
                    .Concat(Enumerable.Range(-radius + 1, Math.Max(0, radius * 2 - 1))
                        .SelectMany(offsetY => new[]
                        {
                            new Point(preferred.X - radius, preferred.Y + offsetY),
                            new Point(preferred.X + radius, preferred.Y + offsetY)
                        }));
            foreach (Point candidate in candidates
                         .Distinct()
                         .OrderBy(point => Math.Abs(point.X - preferred.X) + Math.Abs(point.Y - preferred.Y))
                         .ThenBy(point => point.Y)
                         .ThenBy(point => point.X))
            {
                if (!IsInBounds(farm, candidate)
                    || farm.warps.Any(warp => warp.X == candidate.X && warp.Y == candidate.Y)
                    || farm.doors.ContainsKey(candidate)
                    || !farm.CanSpawnCharacterHere(candidate.ToVector2()))
                    continue;
                return candidate;
            }
        }

        return null;
    }

    private static bool IsInBounds(GameLocation location, Point tile)
    {
        return tile.X >= 0
            && tile.Y >= 0
            && tile.X < location.Map.Layers[0].LayerWidth
            && tile.Y < location.Map.Layers[0].LayerHeight;
    }
}
