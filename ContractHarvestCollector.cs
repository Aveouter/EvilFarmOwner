using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;

namespace EvilFarmOwner;

internal sealed class ContractHarvestCollector : JunimoHarvester
{
    private readonly List<Item> CapturedItems = new();

    public ContractHarvestCollector(GameLocation location, Vector2 position)
    {
        this.currentLocation = location;
        this.Position = position;
    }

    public IReadOnlyList<Item> Items => this.CapturedItems;

    public override void tryToAddItemToHut(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        this.CapturedItems.Add(item);
    }
}
