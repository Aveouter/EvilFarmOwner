using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;
using StardewValley.TerrainFeatures;

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

internal static class ContractHarvestSemantics
{
    public static bool HasCapturedOutput(bool vanillaRequestsCropRemoval, int capturedItemCount)
    {
        // Crop.harvest returns whether the containing HoeDirt should remove the crop.
        // Regrowing crops return false after successfully handing items to the Junimo collector.
        _ = vanillaRequestsCropRemoval;
        return capturedItemCount > 0;
    }
}

internal static class TapperHarvestSemantics
{
    public static bool IsReadyTarget(
        bool isTapper,
        bool attachedToTree,
        bool hasOutput,
        bool readyForHarvest)
    {
        return isTapper && attachedToTree && hasOutput && readyForHarvest;
    }
}

internal static class FruitTreeHarvestSemantics
{
    public static bool IsReadyTarget(
        int growthStage,
        bool isStump,
        int fruitSlots)
    {
        return growthStage >= FruitTree.treeStage
            && !isStump
            && fruitSlots > 0;
    }

    public static bool ProducesCoal(bool struckByLightning)
    {
        return struckByLightning;
    }
}

internal static class MachineHarvestSemantics
{
    public static bool IsReadyTarget(
        bool isExactVanillaObject,
        bool hasNumericVanillaId,
        bool isBigCraftable,
        bool isReady,
        bool hasPlainObjectOutput,
        bool hasMachineData,
        bool isIncubator,
        bool isTapper,
        bool recalculatesOnCollect,
        bool hasOutputCollectedRule)
    {
        return isExactVanillaObject
            && hasNumericVanillaId
            && isBigCraftable
            && isReady
            && hasPlainObjectOutput
            && hasMachineData
            && !isIncubator
            && !isTapper
            && !recalculatesOnCollect
            && !hasOutputCollectedRule;
    }
}
