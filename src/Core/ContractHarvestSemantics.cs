namespace EvilFarmOwner;

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
        return growthStage >= 4
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

internal static class CrabPotHarvestSemantics
{
    public static bool IsReadyTarget(
        bool showingCatch,
        bool readyForHarvest,
        bool hasOutput)
    {
        return showingCatch && readyForHarvest && hasOutput;
    }

    public static int GetOutputStack(
        int baseStack,
        bool hasCrabbingBook,
        double deterministicRoll,
        bool destinationAcceptsDouble)
    {
        if (baseStack <= 0)
            throw new ArgumentOutOfRangeException(nameof(baseStack));
        if (deterministicRoll < 0 || deterministicRoll >= 1)
            throw new ArgumentOutOfRangeException(nameof(deterministicRoll));
        return hasCrabbingBook
            && deterministicRoll < 0.25
            && destinationAcceptsDouble
                ? checked(baseStack * 2)
                : baseStack;
    }
}

internal static class FishPondHarvestSemantics
{
    public static bool IsReadyTarget(
        bool constructionComplete,
        bool upgradeComplete,
        bool hasOutput)
    {
        return constructionComplete && upgradeComplete && hasOutput;
    }

    public static int GetFishingExperience(int? storeSellPrice)
    {
        return 10 + (storeSellPrice.HasValue
            ? (int)(storeSellPrice.Value * 0.04f)
            : 0);
    }
}

internal sealed record BushHarvestPlan(
    string QualifiedItemId,
    int Stack,
    int Quality,
    int ForagingExperience);

internal static class BushHarvestSemantics
{
    public static bool IsReadyTarget(
        bool isMainFarm,
        bool isTownBush,
        int size,
        bool readyForHarvest,
        bool inBloom,
        bool hasOutput)
    {
        return isMainFarm
            && !isTownBush
            && size is 1 or 3
            && readyForHarvest
            && inBloom
            && hasOutput;
    }

    public static BushHarvestPlan CreatePlan(
        int size,
        string qualifiedItemId,
        int foragingLevel,
        bool hasBotanistProfession)
    {
        if (size is not (1 or 3))
            throw new ArgumentOutOfRangeException(nameof(size));
        if (string.IsNullOrWhiteSpace(qualifiedItemId))
            throw new ArgumentException("Bush output is required.", nameof(qualifiedItemId));
        if (foragingLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(foragingLevel));

        bool isTea = size == 3;
        int stack = isTea ? 1 : 1 + foragingLevel / 4;
        return new BushHarvestPlan(
            qualifiedItemId,
            stack,
            !isTea && hasBotanistProfession ? 4 : 0,
            isTea ? 0 : stack);
    }
}
