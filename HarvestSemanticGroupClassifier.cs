using SObject = StardewValley.Object;

namespace EvilFarmOwner;

/// <summary>
/// Stable integer aliases for the vanilla item categories used by storage routing.
/// Keeping the game-type boundary here lets the deterministic logic tests exercise
/// category behavior without referencing Stardew Valley assemblies directly.
/// </summary>
internal static class HarvestItemCategory
{
    public const int Crafting = SObject.CraftingCategory;
    public const int MetalResources = SObject.metalResources;
    public const int BuildingResources = SObject.buildingResources;
    public const int MonsterLoot = SObject.monsterLootCategory;
    public const int Greens = SObject.GreensCategory;
    public const int Vegetable = SObject.VegetableCategory;
    public const int Fruit = SObject.FruitsCategory;
    public const int Flower = SObject.flowersCategory;
    public const int Fish = SObject.FishCategory;
    public const int Cooking = SObject.CookingCategory;
    public const int Ingredient = SObject.ingredientsCategory;
    public const int Meat = SObject.meatCategory;
    public const int SoldAtPierre = SObject.sellAtPierres;
    public const int ArtisanGood = SObject.artisanGoodsCategory;
    public const int Syrup = SObject.syrupCategory;
    public const int Egg = SObject.EggCategory;
    public const int Milk = SObject.MilkCategory;
    public const int SoldAtPierreAndMarnie = SObject.sellAtPierresAndMarnies;
    public const int Seed = SObject.SeedsCategory;
    public const int Fertilizer = SObject.fertilizerCategory;
    public const int Gem = SObject.GemCategory;
    public const int Mineral = SObject.mineralsCategory;
    public const int Furniture = SObject.furnitureCategory;
    public const int Equipment = SObject.equipmentCategory;
    public const int ClothingSort = SObject.clothingCategorySortValue;
    public const int Hat = SObject.hatCategory;
    public const int Ring = SObject.ringCategory;
    public const int Weapon = SObject.weaponCategory;
    public const int Boots = SObject.bootsCategory;
    public const int Tool = SObject.toolCategory;
    public const int Clothing = SObject.clothingCategory;
    public const int Trinket = SObject.trinketCategory;
    public const int Bait = SObject.baitCategory;
    public const int Tackle = SObject.tackleCategory;
}

internal enum HarvestSemanticGroup
{
    Miscellaneous,
    Resources,
    CropsAndFood,
    ArtisanGoods,
    AnimalProducts,
    SeedsAndFertilizer,
    MineralsAndGems,
    Furniture,
    ClothingAndEquipment
}

internal static class HarvestSemanticGroupClassifier
{
    public static HarvestSemanticGroup Classify(int category)
    {
        return category switch
        {
            HarvestItemCategory.Crafting
                or HarvestItemCategory.MetalResources
                or HarvestItemCategory.BuildingResources
                or HarvestItemCategory.MonsterLoot => HarvestSemanticGroup.Resources,

            HarvestItemCategory.Greens
                or HarvestItemCategory.Vegetable
                or HarvestItemCategory.Fruit
                or HarvestItemCategory.Flower
                or HarvestItemCategory.Fish
                or HarvestItemCategory.Cooking
                or HarvestItemCategory.Ingredient
                or HarvestItemCategory.Meat
                or HarvestItemCategory.SoldAtPierre => HarvestSemanticGroup.CropsAndFood,

            HarvestItemCategory.ArtisanGood
                or HarvestItemCategory.Syrup => HarvestSemanticGroup.ArtisanGoods,

            HarvestItemCategory.Egg
                or HarvestItemCategory.Milk
                or HarvestItemCategory.SoldAtPierreAndMarnie => HarvestSemanticGroup.AnimalProducts,

            HarvestItemCategory.Seed
                or HarvestItemCategory.Fertilizer => HarvestSemanticGroup.SeedsAndFertilizer,

            HarvestItemCategory.Gem
                or HarvestItemCategory.Mineral => HarvestSemanticGroup.MineralsAndGems,

            HarvestItemCategory.Furniture => HarvestSemanticGroup.Furniture,

            HarvestItemCategory.Equipment
                or HarvestItemCategory.ClothingSort
                or HarvestItemCategory.Hat
                or HarvestItemCategory.Ring
                or HarvestItemCategory.Weapon
                or HarvestItemCategory.Boots
                or HarvestItemCategory.Tool
                or HarvestItemCategory.Clothing
                or HarvestItemCategory.Trinket
                or HarvestItemCategory.Bait
                or HarvestItemCategory.Tackle => HarvestSemanticGroup.ClothingAndEquipment,

            _ => HarvestSemanticGroup.Miscellaneous
        };
    }

    public static bool AreSameKnownGroup(int leftCategory, int rightCategory)
    {
        HarvestSemanticGroup left = Classify(leftCategory);
        return left != HarvestSemanticGroup.Miscellaneous
            && left == Classify(rightCategory);
    }
}
