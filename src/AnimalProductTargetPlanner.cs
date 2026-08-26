using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.FarmAnimals;
using StardewValley.Locations;

namespace EvilFarmOwner;

internal enum AnimalProductTargetKind
{
    LooseOvernightProduct,
    ToolHarvestAnimal
}

internal sealed record AnimalProductTargetPlan(
    string StableKey,
    AnimalProductTargetKind Kind,
    long? AnimalId,
    Point TargetTile,
    Point InteractionTile,
    int FacingDirection,
    string QualifiedItemId,
    int Stack,
    int Quality,
    string? RequiredTool,
    Stack<Point> Path);

internal sealed record AnimalProductRouteOption(
    string StableKey,
    GridPoint Target,
    GridPoint Interaction,
    int Distance);

internal sealed class AnimalProductTargetPlanner
{
    private static readonly Point[] InteractionOffsets =
    {
        new(0, 1), new(-1, 0), new(1, 0), new(0, -1)
    };
    private readonly IMonitor Monitor;

    public AnimalProductTargetPlanner(IMonitor monitor) => this.Monitor = monitor;

    public AnimalProductTargetPlan? TryFindNext(
        AnimalHouse house,
        NPC worker,
        Point startTile,
        IReadOnlySet<string> attemptedTargets,
        TravelObstacleLedger? obstacles = null)
    {
        bool builtRoutes = obstacles is null
            ? FarmNavigationMap.TryBuild(
                house, worker, startTile, this.Monitor, out GridRouteMap? routes)
            : FarmNavigationMap.TryBuild(
                house, worker, startTile, this.Monitor,
                house.NameOrUniqueName, obstacles, out routes);
        if (HasAutoGrabber(house)
            || !builtRoutes
            || routes is null)
            return null;

        Dictionary<string, AnimalProductTargetPlan> targets = new(StringComparer.Ordinal);
        List<AnimalProductRouteOption> options = new();
        HashSet<string> looseIds = GetLooseProductIds(house);
        foreach (KeyValuePair<Vector2, StardewValley.Object> pair in house.objects.Pairs)
        {
            StardewValley.Object item = pair.Value;
            Point tile = new((int)pair.Key.X, (int)pair.Key.Y);
            string key = $"loose:{house.NameOrUniqueName}:{tile.X}:{tile.Y}:{item.QualifiedItemId}";
            if (attemptedTargets.Contains(key)
                || !AnimalProductSourcePolicy.IsEligibleLooseProduct(
                    item.QualifiedItemId,
                    item.bigCraftable.Value,
                    item.CanBeSetDown,
                    looseIds))
                continue;
            AddOptions(routes, options, key, tile);
            targets[key] = new AnimalProductTargetPlan(
                key, AnimalProductTargetKind.LooseOvernightProduct, null,
                tile, tile, Game1.down, item.QualifiedItemId,
                item.Stack, item.Quality, null, new Stack<Point>());
        }

        foreach (FarmAnimal animal in house.animals.Values)
        {
            FarmAnimalData? data = animal.GetAnimalData();
            string key = $"tool:{house.NameOrUniqueName}:{animal.myID.Value}";
            AnimalCareSkipReason result = AnimalProducePolicy.TryCreateToolHarvestPlan(
                Context.IsMainPlayer,
                animal.isAdult(),
                animal.currentProduce.Value,
                data?.HarvestType == FarmAnimalHarvestType.HarvestWithTool,
                data?.HarvestTool,
                animal.hasEatenAnimalCracker.Value,
                animal.produceQuality.Value,
                autoGrabberOwnsProduce: false,
                out AnimalProducePlan? product);
            if (attemptedTargets.Contains(key)
                || result != AnimalCareSkipReason.None
                || product is null
                || !ReferenceEquals(animal.currentLocation, house))
                continue;
            Point tile = animal.TilePoint;
            AddOptions(routes, options, key, tile);
            targets[key] = new AnimalProductTargetPlan(
                key, AnimalProductTargetKind.ToolHarvestAnimal, animal.myID.Value,
                tile, tile, Game1.down, product.QualifiedItemId,
                product.Stack, product.Quality, product.RequiredTool, new Stack<Point>());
        }

        AnimalProductRouteOption? best = OrderOptions(options).FirstOrDefault();
        if (best is null
            || !targets.TryGetValue(best.StableKey, out AnimalProductTargetPlan? target)
            || !routes.TryGetPath(best.Interaction, out IReadOnlyList<GridPoint> path))
            return null;
        Point interaction = new(best.Interaction.X, best.Interaction.Y);
        return target with
        {
            InteractionTile = interaction,
            FacingDirection = GetFacingDirection(interaction, target.TargetTile),
            Path = FarmNavigationMap.ToPath(path)
        };
    }

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

    public static bool HasAutoGrabber(AnimalHouse house)
    {
        return house.objects.Values.Any(item => item.QualifiedItemId == "(BC)165");
    }

    public static bool HasEligibleWork(AnimalHouse house)
    {
        if (HasAutoGrabber(house))
            return false;

        HashSet<string> looseIds = GetLooseProductIds(house);
        bool hasLooseProduct = house.objects.Values.Any(item =>
            AnimalProductSourcePolicy.IsEligibleLooseProduct(
                item.QualifiedItemId,
                item.bigCraftable.Value,
                item.CanBeSetDown,
                looseIds));
        if (hasLooseProduct)
            return true;

        return house.animals.Values.Any(animal =>
        {
            FarmAnimalData? data = animal.GetAnimalData();
            return ReferenceEquals(animal.currentLocation, house)
                && AnimalProducePolicy.TryCreateToolHarvestPlan(
                    Context.IsMainPlayer,
                    animal.isAdult(),
                    animal.currentProduce.Value,
                    data?.HarvestType == FarmAnimalHarvestType.HarvestWithTool,
                    data?.HarvestTool,
                    animal.hasEatenAnimalCracker.Value,
                    animal.produceQuality.Value,
                    autoGrabberOwnsProduce: false,
                    out _) == AnimalCareSkipReason.None;
        });
    }

    private static HashSet<string> GetLooseProductIds(AnimalHouse house)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (FarmAnimal animal in house.animals.Values)
        {
            FarmAnimalData? data = animal.GetAnimalData();
            if (data?.HarvestType != FarmAnimalHarvestType.DropOvernight)
                continue;
            foreach (FarmAnimalProduce produce in data.ProduceItemIds.Concat(data.DeluxeProduceItemIds))
                ids.Add(ItemRegistry.QualifyItemId(produce.ItemId) ?? $"(O){produce.ItemId}");
        }
        return ids;
    }

    private static void AddOptions(
        GridRouteMap routes,
        ICollection<AnimalProductRouteOption> options,
        string stableKey,
        Point target)
    {
        foreach (Point offset in InteractionOffsets)
        {
            Point interaction = new(target.X + offset.X, target.Y + offset.Y);
            GridPoint grid = new(interaction.X, interaction.Y);
            if (routes.TryGetDistance(grid, out int distance))
            {
                options.Add(new AnimalProductRouteOption(
                    stableKey,
                    new GridPoint(target.X, target.Y),
                    grid,
                    distance));
            }
        }
    }

    private static int GetFacingDirection(Point interaction, Point target)
    {
        if (target.X > interaction.X) return Game1.right;
        if (target.X < interaction.X) return Game1.left;
        if (target.Y > interaction.Y) return Game1.down;
        return Game1.up;
    }
}
