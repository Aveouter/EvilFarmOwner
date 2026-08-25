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

        api.AddParagraph(this.Manifest, this.Text("gmcm.contract-settings.notice"));

        return true;
    }

    private Func<string> Text(string key)
    {
        return () => this.Helper.Translation.Get(key).ToString();
    }
}
