using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;

namespace EvilFarmOwner;

internal sealed record StorageSortFixtureItem(
    string QualifiedItemId,
    int Category,
    int Quantity,
    int Quality = 0,
    int MaximumStackSize = 999);

internal static class StorageSortAcceptanceFixture
{
    private const string FixtureDataKey = "Aveouter.EvilFarmOwner/StorageSortFixture";
    private const string FixtureVersion = "1";
    private const int FixtureChestCount = 5;
    private const int ExpectedTransferCount = 4;

    private static readonly IReadOnlyList<IReadOnlyList<StorageSortFixtureItem>> FixtureContents =
        new IReadOnlyList<StorageSortFixtureItem>[]
        {
            new StorageSortFixtureItem[]
            {
                new("(O)24", -75, 10),
                new("(O)192", -75, 5)
            },
            new StorageSortFixtureItem[]
            {
                new("(O)382", -15, 1)
            },
            new StorageSortFixtureItem[]
            {
                new("(O)635", -79, 1)
            },
            Array.Empty<StorageSortFixtureItem>(),
            new StorageSortFixtureItem[]
            {
                new("(O)390", -16, 20),
                new("(O)382", -15, 5),
                new("(O)613", -79, 3),
                new("(O)770", -74, 4),
                new("(O)24", -75, 2, Quality: 1)
            }
        };

    public static IReadOnlyList<StorageSortChestSnapshot> CreateSnapshots(
        IReadOnlyList<GridPoint> tiles)
    {
        if (tiles.Count != FixtureChestCount)
            throw new ArgumentException($"Expected {FixtureChestCount} fixture tiles.", nameof(tiles));

        List<StorageSortChestSnapshot> snapshots = new(FixtureChestCount);
        for (int chestIndex = 0; chestIndex < FixtureChestCount; chestIndex++)
        {
            IReadOnlyList<StorageSortStackSnapshot> stacks = FixtureContents[chestIndex]
                .Select((item, slot) => new StorageSortStackSnapshot(
                    $"fixture:{chestIndex}:{slot}",
                    $"{item.QualifiedItemId}:q{item.Quality}",
                    item.QualifiedItemId,
                    item.Category,
                    item.Quantity,
                    item.MaximumStackSize))
                .ToArray();
            snapshots.Add(new StorageSortChestSnapshot(tiles[chestIndex], 36, stacks));
        }

        return snapshots;
    }

    public static bool TrySetup(Farm farm, Point playerTile, out string result)
    {
        result = string.Empty;
        Dictionary<GridPoint, Chest> eligible = StorageSortSnapshotService.GetEligibleChests(farm);
        if (eligible.Count > 0)
        {
            result = $"Fixture setup refused: the main farm already has {eligible.Count} eligible chest(s). Use the isolated chest-free save copy.";
            return false;
        }

        if (GetFixtureChests(farm).Count > 0)
        {
            result = "Fixture setup refused: tagged fixture chests already exist.";
            return false;
        }

        if (!TryFindLayout(farm, new GridPoint(playerTile.X, playerTile.Y), out GridPoint[]? tiles)
            || tiles is null)
        {
            result = "Fixture setup could not find five chest tiles with a clear interaction row near the player.";
            return false;
        }

        List<Vector2> insertedTiles = new();
        try
        {
            for (int index = 0; index < tiles.Length; index++)
            {
                Vector2 tile = new(tiles[index].X, tiles[index].Y);
                Chest chest = new(playerChest: true, tile);
                chest.modData[FixtureDataKey] = FixtureVersion;
                foreach (StorageSortFixtureItem fixtureItem in FixtureContents[index])
                {
                    Item item = ItemRegistry.Create(
                        fixtureItem.QualifiedItemId,
                        fixtureItem.Quantity,
                        fixtureItem.Quality);
                    if (item.Category != fixtureItem.Category
                        || item.maximumStackSize() != fixtureItem.MaximumStackSize)
                    {
                        throw new InvalidDataException(
                            $"Fixture item {fixtureItem.QualifiedItemId} no longer matches its reviewed category/stack metadata.");
                    }
                    chest.Items.Add(item);
                }

                farm.objects.Add(tile, chest);
                insertedTiles.Add(tile);
            }

            Dictionary<GridPoint, Chest> fixtureChests = GetFixtureChests(farm);
            StorageSortRuntimePlanResult preflight = StorageSortSnapshotService.TryCreate(fixtureChests);
            int transferCount = preflight.RuntimePlan?.Plan.Transfers.Count ?? 0;
            if (!preflight.IsSuccess || transferCount != ExpectedTransferCount)
            {
                throw new InvalidDataException(
                    $"Fixture preflight expected {ExpectedTransferCount} transfers but returned {preflight.Failure}/{transferCount}.");
            }

            result = $"Created the isolated five-chest sorting fixture at {string.Join(", ", tiles.Select(tile => $"({tile.X},{tile.Y})"))}. Preflight has exactly {transferCount} transfers; start one manual sorting contract, then run efo_storage_sort_fixture status.";
            return true;
        }
        catch (Exception ex)
        {
            foreach (Vector2 tile in insertedTiles)
            {
                if (farm.objects.TryGetValue(tile, out StardewValley.Object? item)
                    && item is Chest chest
                    && IsFixtureChest(chest))
                {
                    farm.objects.Remove(tile);
                }
            }

            result = $"Fixture setup rolled back without retaining test chests: {ex.Message}";
            return false;
        }
    }

    public static string Describe(Farm farm)
    {
        Dictionary<GridPoint, Chest> chests = GetFixtureChests(farm);
        if (chests.Count == 0)
            return "Storage sorting fixture is not present.";
        if (chests.Count != FixtureChestCount)
            return $"Storage sorting fixture is invalid: found {chests.Count}/{FixtureChestCount} tagged chests.";

        StorageSortRuntimePlanResult status = StorageSortSnapshotService.TryCreate(chests);
        if (status.Failure == StorageSortSnapshotFailure.NoTransfers)
            return "Storage sorting fixture converged: no transfers remain. A second contract must reject before wage reservation or NPC lease.";
        if (status.IsSuccess)
            return $"Storage sorting fixture is ready: {status.RuntimePlan!.Plan.Transfers.Count} deterministic transfer(s) remain.";
        return $"Storage sorting fixture is invalid: preflight returned {status.Failure}.";
    }

    private static Dictionary<GridPoint, Chest> GetFixtureChests(Farm farm)
    {
        return farm.objects.Pairs
            .Where(pair => pair.Value is Chest chest && IsFixtureChest(chest))
            .ToDictionary(
                pair => new GridPoint((int)pair.Key.X, (int)pair.Key.Y),
                pair => (Chest)pair.Value);
    }

    private static bool IsFixtureChest(Chest chest)
    {
        return chest.modData.TryGetValue(FixtureDataKey, out string? version)
            && string.Equals(version, FixtureVersion, StringComparison.Ordinal);
    }

    private static bool TryFindLayout(
        Farm farm,
        GridPoint anchor,
        out GridPoint[]? tiles)
    {
        tiles = null;
        int width = farm.Map.Layers[0].LayerWidth;
        int height = farm.Map.Layers[0].LayerHeight;
        IEnumerable<GridPoint> origins = Enumerable
            .Range(1, Math.Max(0, height - 3))
            .SelectMany(y => Enumerable.Range(1, Math.Max(0, width - 10)).Select(x => new GridPoint(x, y)))
            .OrderBy(origin => Math.Abs(origin.X - anchor.X) + Math.Abs(origin.Y - anchor.Y))
            .ThenBy(origin => origin.Y)
            .ThenBy(origin => origin.X);

        foreach (GridPoint origin in origins)
        {
            GridPoint[] candidate = Enumerable.Range(0, FixtureChestCount)
                .Select(index => new GridPoint(origin.X + index * 2, origin.Y))
                .ToArray();
            bool clear = candidate.All(tile =>
            {
                Vector2 chestTile = new(tile.X, tile.Y);
                Vector2 interactionTile = new(tile.X, tile.Y + 1);
                return farm.CanItemBePlacedHere(chestTile)
                    && farm.CanItemBePlacedHere(interactionTile)
                    && !farm.warps.Any(warp =>
                        (warp.X == tile.X && warp.Y == tile.Y)
                        || (warp.X == tile.X && warp.Y == tile.Y + 1))
                    && !farm.doors.ContainsKey(new Point(tile.X, tile.Y))
                    && !farm.doors.ContainsKey(new Point(tile.X, tile.Y + 1));
            });
            if (!clear)
                continue;

            tiles = candidate;
            return true;
        }

        return false;
    }
}
