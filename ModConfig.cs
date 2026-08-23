using StardewModdingAPI;

namespace EvilFarmOwner;

internal sealed class ModConfig
{
    public SButton OpenMenuKey { get; set; } = SButton.H;
    public int WorkRadius { get; set; } = 64;
    public int DailyWage { get; set; } = 500;
    public int MaxTilesPerJob { get; set; } = 250;
    public bool WaterCrops { get; set; } = true;
    public bool HarvestCrops { get; set; } = true;
    public bool ClearDebris { get; set; } = false;
    public bool FertilizeEmptyDirt { get; set; } = false;
    public bool PlantSeedsFromInventory { get; set; } = false;
    public bool DepositHarvestToNearestChest { get; set; } = false;
}
