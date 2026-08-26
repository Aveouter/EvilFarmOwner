using StardewModdingAPI;

namespace EvilFarmOwner;

internal sealed class ModConfig
{
    public SButton OpenMenuKey { get; set; } = SButton.K;

    public int BaseHourlyWage { get; set; } = ContractPreviewService.BaseHourlyWage;

    public int FriendshipWageImpactPercent { get; set; } = 20;

    public float RestDayMultiplier { get; set; } = (float)ContractPreviewService.RestDayMultiplier;

    public HarvestDestinationMode DefaultHarvestDestination { get; set; } =
        HarvestDestinationMode.ClassifiedChests;

    public bool EnableHarvesting { get; set; } = true;

    public bool EnableWatering { get; set; } = true;

    public bool EnableAnimalCare { get; set; } = true;

    public bool EnableStorageSorting { get; set; } = true;

    public bool Normalize()
    {
        ModConfig normalized = this.CreateNormalizedCopy();
        bool changed = !this.EqualsValues(normalized);
        this.BaseHourlyWage = normalized.BaseHourlyWage;
        this.FriendshipWageImpactPercent = normalized.FriendshipWageImpactPercent;
        this.RestDayMultiplier = normalized.RestDayMultiplier;
        this.DefaultHarvestDestination = normalized.DefaultHarvestDestination;
        this.EnableHarvesting = normalized.EnableHarvesting;
        this.EnableWatering = normalized.EnableWatering;
        this.EnableAnimalCare = normalized.EnableAnimalCare;
        this.EnableStorageSorting = normalized.EnableStorageSorting;
        return changed;
    }

    public ContractSettingsSnapshot CreateSnapshot()
    {
        ModConfig normalized = this.CreateNormalizedCopy();
        FarmWorkStageSelection stages = FarmWorkStageSelection.None;
        if (normalized.EnableHarvesting)
            stages |= FarmWorkStageSelection.Harvesting;
        if (normalized.EnableWatering)
            stages |= FarmWorkStageSelection.Watering;
        if (normalized.EnableAnimalCare)
            stages |= FarmWorkStageSelection.AnimalCare;
        if (normalized.EnableStorageSorting)
            stages |= FarmWorkStageSelection.StorageSorting;

        return new ContractSettingsSnapshot(
            normalized.BaseHourlyWage,
            normalized.FriendshipWageImpactPercent,
            (decimal)normalized.RestDayMultiplier,
            normalized.DefaultHarvestDestination,
            stages);
    }

    private ModConfig CreateNormalizedCopy()
    {
        ModConfig normalized = new()
        {
            OpenMenuKey = this.OpenMenuKey,
            BaseHourlyWage = ContractSettingsPolicy.NormalizeBaseHourlyWage(this.BaseHourlyWage),
            FriendshipWageImpactPercent = ContractSettingsPolicy.NormalizeFriendshipImpactPercent(
                this.FriendshipWageImpactPercent),
            RestDayMultiplier = (float)ContractSettingsPolicy.NormalizeRestDayMultiplier(
                (decimal)this.RestDayMultiplier),
            DefaultHarvestDestination = Enum.IsDefined(this.DefaultHarvestDestination)
                ? this.DefaultHarvestDestination
                : HarvestDestinationMode.ClassifiedChests,
            EnableHarvesting = this.EnableHarvesting,
            EnableWatering = this.EnableWatering,
            EnableAnimalCare = this.EnableAnimalCare,
            EnableStorageSorting = this.EnableStorageSorting
        };

        if (!normalized.EnableHarvesting
            && !normalized.EnableWatering
            && !normalized.EnableAnimalCare
            && !normalized.EnableStorageSorting)
        {
            normalized.EnableHarvesting = true;
            normalized.EnableWatering = true;
            normalized.EnableAnimalCare = true;
            normalized.EnableStorageSorting = true;
        }

        return normalized;
    }

    private bool EqualsValues(ModConfig other)
    {
        return this.BaseHourlyWage == other.BaseHourlyWage
            && this.FriendshipWageImpactPercent == other.FriendshipWageImpactPercent
            && this.RestDayMultiplier.Equals(other.RestDayMultiplier)
            && this.DefaultHarvestDestination == other.DefaultHarvestDestination
            && this.EnableHarvesting == other.EnableHarvesting
            && this.EnableWatering == other.EnableWatering
            && this.EnableAnimalCare == other.EnableAnimalCare
            && this.EnableStorageSorting == other.EnableStorageSorting;
    }
}
