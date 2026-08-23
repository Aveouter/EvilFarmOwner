using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;

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
        helper.WriteConfig(this.Config);
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

        helper.ConsoleCommands.Add("efo_roster", helper.Translation.Get("cmd.roster"), (_, _) => this.OpenWorkerRoster());
        helper.ConsoleCommands.Add("efo_overflow", helper.Translation.Get("cmd.overflow"), (_, _) => this.OpenHarvestOverflow());
        helper.ConsoleCommands.Add("efo_netstatus", helper.Translation.Get("cmd.netstatus"), (_, _) => this.ShowNetworkStatus());
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
        this.Helper.WriteConfig(this.Config);
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

}
