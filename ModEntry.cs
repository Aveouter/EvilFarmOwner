using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Extensions;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace EvilFarmOwner;

public sealed class ModEntry : Mod
{
    private ModConfig Config = new();
    private bool HasKnownHotkeyConflict;
    private WorkerRosterService? WorkerRoster;
    private WateringContractExecutionController? WateringContracts;
    private HarvestingContractExecutionController? HarvestingContracts;
    private MultiplayerContractCoordinator? MultiplayerContracts;

    public override void Entry(IModHelper helper)
    {
        this.Config = helper.ReadConfig<ModConfig>();
        this.NormalizeAndSaveConfig(writeEvenIfUnchanged: false);
        this.RefreshHotkeyConflict();
        this.WorkerRoster = new WorkerRosterService(this.Monitor);
        this.WateringContracts = new WateringContractExecutionController(
            helper.Translation,
            this.Monitor,
            this.WorkerRoster);
        this.HarvestingContracts = new HarvestingContractExecutionController(
            helper.Translation,
            this.Monitor,
            this.WorkerRoster);
        this.MultiplayerContracts = new MultiplayerContractCoordinator(
            helper,
            this.ModManifest,
            helper.Translation,
            this.Monitor,
            this.WateringContracts,
            this.HarvestingContracts);

        if (this.HasKnownHotkeyConflict)
            this.Monitor.Log(helper.Translation.Get("hud.hotkey-conflict"), LogLevel.Warn);

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.DayEnding += this.OnDayEnding;
        helper.Events.GameLoop.Saving += this.OnSaving;
        helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;
        helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;
        helper.Events.Multiplayer.PeerConnected += this.OnPeerConnected;
        helper.Events.Multiplayer.PeerDisconnected += this.OnPeerDisconnected;

        helper.ConsoleCommands.Add("efo_work", helper.Translation.Get("cmd.work"), (_, _) => this.TryDoFarmWork(showEmptyMessage: true));
        helper.ConsoleCommands.Add("efo_roster", helper.Translation.Get("cmd.roster"), (_, _) => this.OpenWorkerRoster());
        helper.ConsoleCommands.Add("efo_overflow", helper.Translation.Get("cmd.overflow"), (_, _) => this.OpenHarvestOverflow());
        helper.ConsoleCommands.Add("efo_netstatus", helper.Translation.Get("cmd.netstatus"), (_, _) => this.ShowNetworkStatus());
        helper.ConsoleCommands.Add("efo_status", helper.Translation.Get("cmd.status"), (_, _) => this.ShowStatus());
        helper.ConsoleCommands.Add("efo_toggle", helper.Translation.Get("cmd.toggle"), this.ToggleTask);
    }

    private void NormalizeAndSaveConfig(bool writeEvenIfUnchanged)
    {
        IReadOnlyList<string> configWarnings = ConfigValidator.Normalize(this.Config);
        bool shouldWriteConfig = configWarnings.Count > 0;

        foreach (string warning in configWarnings)
            this.Monitor.Log($"Invalid config: {warning}", LogLevel.Warn);

        if (this.Config.ClearDebris)
        {
            this.Config.ClearDebris = false;
            shouldWriteConfig = true;
            this.Monitor.Log(this.Helper.Translation.Get("cmd.clear-disabled"), LogLevel.Warn);
        }

        if (shouldWriteConfig || writeEvenIfUnchanged)
            this.Helper.WriteConfig(this.Config);
    }

    private void RefreshHotkeyConflict()
    {
        this.HasKnownHotkeyConflict = this.Config.OpenMenuKey == SButton.H
            && this.Helper.ModRegistry.IsLoaded("Annosz.UiInfoSuite2");
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        GenericModConfigMenuIntegration integration = new(
            this.Helper,
            this.ModManifest,
            getConfig: () => this.Config,
            setConfig: config => this.Config = config,
            saveConfig: this.SaveConfigFromMenu);

        integration.Register();
    }

    private void SaveConfigFromMenu()
    {
        this.NormalizeAndSaveConfig(writeEvenIfUnchanged: true);
        this.RefreshHotkeyConflict();

        if (this.HasKnownHotkeyConflict)
            this.Monitor.Log(this.Helper.Translation.Get("hud.hotkey-conflict"), LogLevel.Warn);
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        this.MultiplayerContracts?.OnSaveLoaded();
        Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("hud.ready", new { key = this.Config.OpenMenuKey }), HUDMessage.newQuest_type));

        if (this.HasKnownHotkeyConflict)
            Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("hud.hotkey-conflict"), HUDMessage.error_type));
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady
            || Game1.activeClickableMenu is not null
            || !e.Button.Equals(this.Config.OpenMenuKey))
            return;

        this.Helper.Input.Suppress(e.Button);
        this.OpenWorkerRoster();
    }

    private void OpenWorkerRoster(int initialPage = 0)
    {
        if (!Context.IsWorldReady || this.WorkerRoster is null)
        {
            this.Monitor.Log(this.Helper.Translation.Get("cmd.roster-world-not-ready"), LogLevel.Info);
            return;
        }

        if (this.MultiplayerContracts?.HasPendingRequest == true)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Helper.Translation.Get("multiplayer.hud.pending-existing"),
                HUDMessage.error_type));
            return;
        }

        if (this.HasActiveNamedContract())
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Helper.Translation.Get("contract.start.already-active"),
                HUDMessage.error_type));
            return;
        }

        Game1.activeClickableMenu = new WorkerRosterMenu(
            this.WorkerRoster.GetRoster(),
            this.Helper.Translation,
            this.OpenWorkerTaskSelection,
            initialPage);
    }

    private void OpenWorkerTaskSelection(WorkerRosterEntry worker, int rosterPage)
    {
        if (!Context.IsWorldReady
            || worker.Availability.State != WorkerAvailabilityState.EligibleForPreview)
            return;

        Game1.activeClickableMenu = new WorkerTaskSelectionMenu(
            worker,
            this.Helper.Translation,
            () => this.OpenWorkerRoster(rosterPage),
            task => this.OpenWorkContractPreview(worker, rosterPage, task));
    }

    private void OpenWorkContractPreview(
        WorkerRosterEntry worker,
        int rosterPage,
        NamedFarmTask task)
    {
        if (!Context.IsWorldReady
            || worker.Availability.State != WorkerAvailabilityState.EligibleForPreview)
            return;

        int friendshipHearts = Game1.player.getFriendshipHeartLevelForNPC(worker.InternalName);
        WorkContractPreview preview = ContractPreviewService.Create(friendshipHearts, Game1.dayOfMonth);

        Game1.activeClickableMenu = new WorkContractPreviewMenu(
            worker,
            preview,
            task,
            this.Helper.Translation,
            () => this.OpenWorkerTaskSelection(worker, rosterPage),
            () => this.TryStartNamedContract(worker.InternalName, task));
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        this.WateringContracts?.Update();
        this.HarvestingContracts?.Update();
        this.MultiplayerContracts?.Update();
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        this.WateringContracts?.OnDayEnding();
        this.HarvestingContracts?.OnDayEnding();
        this.MultiplayerContracts?.Update();
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        this.WateringContracts?.OnDayEnding();
        this.HarvestingContracts?.OnDayEnding();
        this.MultiplayerContracts?.Update();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.WateringContracts?.OnReturnedToTitle();
        this.HarvestingContracts?.OnReturnedToTitle();
        this.MultiplayerContracts?.Update();
        this.MultiplayerContracts?.OnReturnedToTitle();
    }

    private bool HasActiveNamedContract()
    {
        return this.WateringContracts?.HasActiveContract == true
            || this.HarvestingContracts?.HasActiveContract == true
            || this.MultiplayerContracts?.HasObservedActiveContract == true
            || this.MultiplayerContracts?.HasPendingRequest == true;
    }

    private bool TryStartNamedContract(string workerInternalName, NamedFarmTask task)
    {
        return this.MultiplayerContracts?.RequestStart(workerInternalName, task) == true;
    }

    private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
    {
        this.MultiplayerContracts?.OnModMessageReceived(sender, e);
    }

    private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
    {
        this.MultiplayerContracts?.OnPeerConnected(sender, e);
    }

    private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
    {
        this.MultiplayerContracts?.OnPeerDisconnected(sender, e);
    }

    private void OpenHarvestOverflow()
    {
        if (!Context.IsWorldReady)
        {
            this.Monitor.Log(this.Helper.Translation.Get("cmd.roster-world-not-ready"), LogLevel.Info);
            return;
        }

        if (this.HasActiveNamedContract())
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Helper.Translation.Get("overflow.busy"),
                HUDMessage.error_type));
            return;
        }

        NetMutex mutex = Game1.player.team.GetOrCreateGlobalInventoryMutex(
            HarvestingContractExecutionController.OverflowInventoryId);
        mutex.RequestLock(
            () =>
            {
                Chest proxy = new(playerChest: true, Vector2.Zero)
                {
                    GlobalInventoryId = HarvestingContractExecutionController.OverflowInventoryId
                };
                proxy.ShowMenu();
                if (Game1.activeClickableMenu is ItemGrabMenu menu)
                {
                    IClickableMenu.onExit? originalExit = menu.exitFunction;
                    menu.exitFunction = () =>
                    {
                        originalExit?.Invoke();
                        if (mutex.IsLockHeld())
                            mutex.ReleaseLock();
                    };
                }
                else if (mutex.IsLockHeld())
                {
                    mutex.ReleaseLock();
                }
            },
            () => Game1.addHUDMessage(new HUDMessage(
                this.Helper.Translation.Get("overflow.locked"),
                HUDMessage.error_type)));
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

        if (!ConfigValidator.HasEnabledJobs(this.Config))
        {
            Game1.addHUDMessage(new HUDMessage(this.Helper.Translation.Get("hud.no-tasks"), HUDMessage.error_type));
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

    private void ShowNetworkStatus()
    {
        if (!Context.IsWorldReady)
        {
            this.Monitor.Log(this.Helper.Translation.Get("cmd.roster-world-not-ready"), LogLevel.Info);
            return;
        }

        this.Monitor.Log(
            this.MultiplayerContracts?.GetDiagnosticStatus() ?? "EFO network coordinator is unavailable.",
            LogLevel.Info);
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
                this.Config.ClearDebris = false;
                this.Monitor.Log(this.Helper.Translation.Get("cmd.clear-disabled"), LogLevel.Warn);
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
