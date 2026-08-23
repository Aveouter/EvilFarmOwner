using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Extensions;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Locations;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace EvilFarmOwner;

public sealed class ModEntry : Mod
{
    private ModConfig Config = new();

    public override void Entry(IModHelper helper)
    {
        this.Config = helper.ReadConfig<ModConfig>();

        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;

        helper.ConsoleCommands.Add("efo_work", helper.Translation.Get("cmd.work"), (_, _) => this.TryDoFarmWork(showEmptyMessage: true));
        helper.ConsoleCommands.Add("efo_status", helper.Translation.Get("cmd.status"), (_, _) => this.ShowStatus());
        helper.ConsoleCommands.Add("efo_toggle", helper.Translation.Get("cmd.toggle"), this.ToggleTask);
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("hud.ready", new { key = this.Config.OpenMenuKey }), HUDMessage.newQuest_type));
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady
            || Game1.activeClickableMenu is not null
            || !e.Button.Equals(this.Config.OpenMenuKey))
            return;

        this.Helper.Input.Suppress(e.Button);
        Game1.activeClickableMenu = new HiringMenu(
            this.Config,
            this.Helper.Translation,
            () => this.TryDoFarmWork(showEmptyMessage: true));
    }

    private void TryDoFarmWork(bool showEmptyMessage)
    {
        if (!Context.IsMainPlayer)
        {
            this.Monitor.Log("Farmhand automation should run on the host player in multiplayer.", LogLevel.Info);
            return;
        }

        if (Game1.currentLocation is not Farm farm)
        {
            Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("hud.no-farm"), HUDMessage.error_type));
            return;
        }

        if (Game1.player.Money < this.Config.DailyWage)
        {
            Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("hud.no-money", new { gold = this.Config.DailyWage }), HUDMessage.error_type));
            return;
        }

        WorkReport report = new();
        int remaining = Math.Max(1, this.Config.MaxTilesPerJob);

        foreach (Vector2 tile in this.GetWorkTiles(farm))
        {
            if (remaining <= 0)
                break;

            bool didWork = false;

            if (this.Config.WaterCrops)
                didWork |= this.TryWater(farm, tile, report);

            if (this.Config.HarvestCrops)
                didWork |= this.TryHarvest(farm, tile, report);

            if (this.Config.ClearDebris)
                didWork |= this.TryClearDebris(farm, tile, report);

            if (this.Config.FertilizeEmptyDirt)
                didWork |= this.TryFertilize(farm, tile, report);

            if (this.Config.PlantSeedsFromInventory)
                didWork |= this.TryPlantSeed(farm, tile, report);

            if (didWork)
                remaining--;
        }

        if (report.Total == 0)
        {
            if (showEmptyMessage)
                Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("hud.empty"), HUDMessage.error_type));
            return;
        }

        Game1.player.Money -= this.Config.DailyWage;
        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("hud.done", new
        {
            watered = report.Watered,
            harvested = report.Harvested,
            cleared = report.Cleared,
            fertilized = report.Fertilized,
            planted = report.Planted
        }), HUDMessage.newQuest_type));
    }

    private IEnumerable<Vector2> GetWorkTiles(Farm farm)
    {
        Vector2 origin = Game1.player.Tile;
        int radius = Math.Max(1, this.Config.WorkRadius);
        int minX = Math.Max(0, (int)origin.X - radius);
        int maxX = Math.Min(farm.Map.Layers[0].LayerWidth - 1, (int)origin.X + radius);
        int minY = Math.Max(0, (int)origin.Y - radius);
        int maxY = Math.Min(farm.Map.Layers[0].LayerHeight - 1, (int)origin.Y + radius);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
                yield return new Vector2(x, y);
        }
    }

    private bool TryWater(GameLocation location, Vector2 tile, WorkReport report)
    {
        if (!location.terrainFeatures.TryGetValue(tile, out TerrainFeature feature) || feature is not HoeDirt dirt)
            return false;

        if (dirt.crop is null || dirt.state.Value == HoeDirt.watered)
            return false;

        dirt.state.Value = HoeDirt.watered;
        report.Watered++;
        return true;
    }

    private bool TryHarvest(GameLocation location, Vector2 tile, WorkReport report)
    {
        if (!location.terrainFeatures.TryGetValue(tile, out TerrainFeature feature) || feature is not HoeDirt dirt || dirt.crop is null)
            return false;

        if (!dirt.crop.programColored.Value && dirt.crop.currentPhase.Value < dirt.crop.phaseDays.Count - 1)
            return false;

        bool harvested = dirt.crop.harvest((int)tile.X, (int)tile.Y, dirt);
        if (!harvested)
            return false;

        report.Harvested++;
        return true;
    }

    private bool TryClearDebris(GameLocation location, Vector2 tile, WorkReport report)
    {
        if (!location.objects.TryGetValue(tile, out SObject obj))
            return false;

        string name = obj.Name.ToLowerInvariant();
        bool isDebris = name.Contains("stone", StringComparison.Ordinal)
            || name.Contains("twig", StringComparison.Ordinal)
            || name.Contains("weed", StringComparison.Ordinal)
            || obj.Category == SObject.junkCategory;

        if (!isDebris)
            return false;

        location.objects.Remove(tile);
        report.Cleared++;
        return true;
    }

    private bool TryFertilize(GameLocation location, Vector2 tile, WorkReport report)
    {
        if (!location.terrainFeatures.TryGetValue(tile, out TerrainFeature feature) || feature is not HoeDirt dirt)
            return false;

        if (dirt.fertilizer.Value != "0")
            return false;

        Item? fertilizer = Game1.player.Items.FirstOrDefault(item => item is SObject obj && obj.Category == SObject.fertilizerCategory);
        if (fertilizer is not SObject fertilizerObject)
            return false;

        dirt.fertilizer.Value = fertilizerObject.QualifiedItemId;
        fertilizerObject.Stack--;
        if (fertilizerObject.Stack <= 0)
            Game1.player.Items.Remove(fertilizerObject);

        report.Fertilized++;
        return true;
    }

    private bool TryPlantSeed(GameLocation location, Vector2 tile, WorkReport report)
    {
        if (!location.terrainFeatures.TryGetValue(tile, out TerrainFeature feature) || feature is not HoeDirt dirt)
            return false;

        if (dirt.crop is not null)
            return false;

        Item? seed = Game1.player.Items.FirstOrDefault(item => item is SObject obj && obj.Category == SObject.SeedsCategory);
        if (seed is not SObject seedObject)
            return false;

        dirt.crop = new Crop(seedObject.ItemId, (int)tile.X, (int)tile.Y, location);
        seedObject.Stack--;
        if (seedObject.Stack <= 0)
            Game1.player.Items.Remove(seedObject);

        report.Planted++;
        return true;
    }

    private void ShowStatus()
    {
        this.Monitor.Log(this.Helper.Translation.Get("cmd.status.line", new
        {
            water = this.Config.WaterCrops,
            harvest = this.Config.HarvestCrops,
            clear = this.Config.ClearDebris,
            fertilize = this.Config.FertilizeEmptyDirt,
            plant = this.Config.PlantSeedsFromInventory,
            radius = this.Config.WorkRadius,
            wage = this.Config.DailyWage
        }), LogLevel.Info);
    }

    private void ToggleTask(string command, string[] args)
    {
        if (args.Length == 0)
        {
            this.ShowStatus();
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "water":
                this.Config.WaterCrops = !this.Config.WaterCrops;
                break;
            case "harvest":
                this.Config.HarvestCrops = !this.Config.HarvestCrops;
                break;
            case "clear":
                this.Config.ClearDebris = !this.Config.ClearDebris;
                break;
            case "fertilize":
                this.Config.FertilizeEmptyDirt = !this.Config.FertilizeEmptyDirt;
                break;
            case "plant":
                this.Config.PlantSeedsFromInventory = !this.Config.PlantSeedsFromInventory;
                break;
            default:
                this.Monitor.Log("Unknown task. Use: water, harvest, clear, fertilize, plant.", LogLevel.Warn);
                return;
        }

        this.Helper.WriteConfig(this.Config);
        this.ShowStatus();
    }

    private sealed class WorkReport
    {
        public int Watered { get; set; }
        public int Harvested { get; set; }
        public int Cleared { get; set; }
        public int Fertilized { get; set; }
        public int Planted { get; set; }
        public int Total => this.Watered + this.Harvested + this.Cleared + this.Fertilized + this.Planted;
    }
}
