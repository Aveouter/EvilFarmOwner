using StardewModdingAPI;

namespace EvilFarmOwner;

internal sealed class GenericModConfigMenuIntegration
{
    private const string GenericModConfigMenuId = "spacechase0.GenericModConfigMenu";

    private readonly IModHelper Helper;
    private readonly IManifest Manifest;
    private readonly Func<ModConfig> GetConfig;
    private readonly Action<ModConfig> SetConfig;
    private readonly Action SaveConfig;

    public GenericModConfigMenuIntegration(
        IModHelper helper,
        IManifest manifest,
        Func<ModConfig> getConfig,
        Action<ModConfig> setConfig,
        Action saveConfig)
    {
        this.Helper = helper;
        this.Manifest = manifest;
        this.GetConfig = getConfig;
        this.SetConfig = setConfig;
        this.SaveConfig = saveConfig;
    }

    public bool Register()
    {
        IGenericModConfigMenuApi? api = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>(GenericModConfigMenuId);
        if (api is null)
            return false;

        api.Register(
            this.Manifest,
            reset: () => this.SetConfig(new ModConfig()),
            save: this.SaveConfig);

        api.AddSectionTitle(this.Manifest, this.Text("gmcm.section.interface"));
        api.AddKeybind(
            this.Manifest,
            getValue: () => this.GetConfig().OpenMenuKey,
            setValue: value => this.GetConfig().OpenMenuKey = value,
            name: this.Text("gmcm.open-menu-key.name"),
            tooltip: this.Text("gmcm.open-menu-key.tooltip"),
            fieldId: nameof(ModConfig.OpenMenuKey));

        api.AddSectionTitle(this.Manifest, this.Text("gmcm.section.economy"));
        api.AddParagraph(this.Manifest, this.Text("gmcm.host-authority.notice"));
        api.AddNumberOption(
            this.Manifest,
            getValue: () => this.GetConfig().BaseHourlyWage,
            setValue: value => this.GetConfig().BaseHourlyWage = value,
            name: this.Text("gmcm.base-wage.name"),
            tooltip: this.Text("gmcm.base-wage.tooltip"),
            min: ContractSettingsPolicy.MinimumBaseHourlyWage,
            max: ContractSettingsPolicy.MaximumBaseHourlyWage,
            interval: ContractSettingsPolicy.BaseHourlyWageStep,
            formatValue: value => $"{value}g",
            fieldId: nameof(ModConfig.BaseHourlyWage));
        api.AddNumberOption(
            this.Manifest,
            getValue: () => this.GetConfig().FriendshipWageImpactPercent,
            setValue: value => this.GetConfig().FriendshipWageImpactPercent = value,
            name: this.Text("gmcm.friendship-impact.name"),
            tooltip: this.Text("gmcm.friendship-impact.tooltip"),
            min: ContractSettingsPolicy.MinimumFriendshipImpactPercent,
            max: ContractSettingsPolicy.MaximumFriendshipImpactPercent,
            interval: ContractSettingsPolicy.FriendshipImpactStep,
            formatValue: value => $"{value}%",
            fieldId: nameof(ModConfig.FriendshipWageImpactPercent));
        api.AddNumberOption(
            this.Manifest,
            getValue: () => this.GetConfig().RestDayMultiplier,
            setValue: value => this.GetConfig().RestDayMultiplier = value,
            name: this.Text("gmcm.rest-day-multiplier.name"),
            tooltip: this.Text("gmcm.rest-day-multiplier.tooltip"),
            min: (float)ContractSettingsPolicy.MinimumRestDayMultiplier,
            max: (float)ContractSettingsPolicy.MaximumRestDayMultiplier,
            interval: (float)ContractSettingsPolicy.RestDayMultiplierStep,
            formatValue: value => $"{value:0.0}x",
            fieldId: nameof(ModConfig.RestDayMultiplier));

        api.AddSectionTitle(this.Manifest, this.Text("gmcm.section.shift"));
        api.AddNumberOption(
            this.Manifest,
            getValue: () => this.GetConfig().MaximumConcurrentWorkers,
            setValue: value =>
            {
                if (!Context.IsWorldReady || Context.IsMainPlayer)
                    this.GetConfig().MaximumConcurrentWorkers = value;
            },
            name: this.Text("gmcm.maximum-workers.name"),
            tooltip: this.Text("gmcm.maximum-workers.tooltip"),
            min: ContractSettingsPolicy.MinimumMaximumConcurrentWorkers,
            max: ContractSettingsPolicy.MaximumMaximumConcurrentWorkers,
            interval: 1,
            fieldId: nameof(ModConfig.MaximumConcurrentWorkers));
        api.AddTextOption(
            this.Manifest,
            getValue: () => this.GetConfig().DefaultHarvestDestination.ToString(),
            setValue: value => this.GetConfig().DefaultHarvestDestination =
                Enum.TryParse(value, out HarvestDestinationMode mode) && Enum.IsDefined(mode)
                    ? mode
                    : HarvestDestinationMode.ClassifiedChests,
            name: this.Text("gmcm.default-destination.name"),
            tooltip: this.Text("gmcm.default-destination.tooltip"),
            allowedValues: Enum.GetNames<HarvestDestinationMode>(),
            formatAllowedValue: value => this.Helper.Translation.Get(
                value == nameof(HarvestDestinationMode.RequesterInventory)
                    ? "gmcm.destination.inventory"
                    : "gmcm.destination.chests"),
            fieldId: nameof(ModConfig.DefaultHarvestDestination));
        this.AddStageOption(
            api,
            nameof(ModConfig.EnableHarvesting),
            "gmcm.stage.harvesting.name",
            config => config.EnableHarvesting,
            (config, value) => config.EnableHarvesting = value);
        this.AddStageOption(
            api,
            nameof(ModConfig.EnableWatering),
            "gmcm.stage.watering.name",
            config => config.EnableWatering,
            (config, value) => config.EnableWatering = value);
        this.AddStageOption(
            api,
            nameof(ModConfig.EnableAnimalCare),
            "gmcm.stage.animal-care.name",
            config => config.EnableAnimalCare,
            (config, value) => config.EnableAnimalCare = value);
        this.AddStageOption(
            api,
            nameof(ModConfig.EnableStorageSorting),
            "gmcm.stage.storage-sorting.name",
            config => config.EnableStorageSorting,
            (config, value) => config.EnableStorageSorting = value);
        api.AddParagraph(this.Manifest, this.Text("gmcm.stage.notice"));

        return true;
    }

    private void AddStageOption(
        IGenericModConfigMenuApi api,
        string fieldId,
        string nameKey,
        Func<ModConfig, bool> getValue,
        Action<ModConfig, bool> setValue)
    {
        api.AddBoolOption(
            this.Manifest,
            getValue: () => getValue(this.GetConfig()),
            setValue: value =>
            {
                ModConfig config = this.GetConfig();
                bool current = getValue(config);
                setValue(config, value);
                if (!config.EnableHarvesting
                    && !config.EnableWatering
                    && !config.EnableAnimalCare
                    && !config.EnableStorageSorting)
                    setValue(config, current);
            },
            name: this.Text(nameKey),
            tooltip: this.Text("gmcm.stage.tooltip"),
            fieldId: fieldId);
    }

    private Func<string> Text(string key)
    {
        return () => this.Helper.Translation.Get(key).ToString();
    }
}
