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
    private StorageSortRecoveryManager? StorageSortRecovery;
    private StorageSortContractExecutionController? StorageSortContracts;
    private MultiplayerContractCoordinator? MultiplayerContracts;
    private RecurringContractCoordinator? RecurringContracts;
    private readonly HarvestAcceptanceFaults AcceptanceFaults = new();

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
            this.WorkerRoster,
            this.AcceptanceFaults);
        this.StorageSortRecovery = new StorageSortRecoveryManager(this.Monitor);
        this.StorageSortContracts = new StorageSortContractExecutionController(
            helper.Translation,
            this.Monitor,
            this.WorkerRoster,
            this.StorageSortRecovery);
        this.MultiplayerContracts = new MultiplayerContractCoordinator(
            helper,
            this.ModManifest,
            helper.Translation,
            this.Monitor,
            this.WateringContracts,
            this.HarvestingContracts,
            this.StorageSortContracts);
        this.RecurringContracts = new RecurringContractCoordinator(
            helper,
            helper.Translation,
            this.Monitor,
            this.WorkerRoster,
            this.MultiplayerContracts);

        if (this.HasKnownHotkeyConflict)
            this.Monitor.Log(helper.Translation.Get("hud.hotkey-conflict"), LogLevel.Warn);

        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.SaveCreating += this.OnSaveCreating;
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
        helper.ConsoleCommands.Add("efo_quarantine", helper.Translation.Get("cmd.quarantine"), (_, _) => this.OpenHarvestQuarantine());
        helper.ConsoleCommands.Add("efo_netstatus", helper.Translation.Get("cmd.netstatus"), (_, _) => this.ShowNetworkStatus());
        helper.ConsoleCommands.Add("efo_report", helper.Translation.Get("cmd.report"), (_, _) => this.ShowLastWorkReport());
        helper.ConsoleCommands.Add("efo_auto", helper.Translation.Get("cmd.recurring"), (_, _) => this.OpenRecurringContractMenu());
#if EFO_ACCEPTANCE_FAULTS
        helper.ConsoleCommands.Add(
            "efo_acceptance_faults",
            "Arm test-only harvest storage failures. This command is excluded from production builds.",
            this.HandleAcceptanceFaultCommand);
        this.Monitor.Log(
            "ACCEPTANCE TEST BUILD: harvest storage fault injection is enabled. Do not distribute this DLL.",
            LogLevel.Alert);
#endif
    }

#if EFO_ACCEPTANCE_FAULTS
    private void HandleAcceptanceFaultCommand(string command, string[] args)
    {
        if (args.Length == 0 || string.Equals(args[0], "status", StringComparison.OrdinalIgnoreCase))
        {
            this.Monitor.Log($"Acceptance faults armed: {this.AcceptanceFaults.Describe()}.", LogLevel.Info);
            return;
        }

        if (string.Equals(args[0], "clear", StringComparison.OrdinalIgnoreCase))
        {
            this.AcceptanceFaults.Clear();
            this.Monitor.Log("Cleared all acceptance-test harvest storage faults.", LogLevel.Alert);
            return;
        }

        if (string.Equals(args[0], "finalize", StringComparison.OrdinalIgnoreCase))
        {
            if (!Context.IsWorldReady || !Context.IsMainPlayer)
            {
                this.Monitor.Log("Acceptance finalization requires the active host save.", LogLevel.Warn);
                return;
            }

            this.HarvestingContracts?.OnSaving();
            this.MultiplayerContracts?.Update();
            this.MultiplayerContracts?.OnSaving();
            this.Monitor.Log("Invoked the harvest save-boundary finalizer for acceptance testing.", LogLevel.Alert);
            return;
        }

        if (!string.Equals(args[0], "arm", StringComparison.OrdinalIgnoreCase) || args.Length < 2)
        {
            this.Monitor.Log(
                "Usage: efo_acceptance_faults arm <overflow-lock|visible-drop|quarantine-lock|recovery-record-write|quarantine-write> [...]; clear; status; finalize",
                LogLevel.Info);
            return;
        }

        List<HarvestAcceptanceFault> parsedFaults = new();
        foreach (string value in args.Skip(1))
        {
            if (!HarvestAcceptanceFaults.TryParse(value, out HarvestAcceptanceFault fault))
            {
                this.Monitor.Log($"Unknown acceptance fault '{value}'.", LogLevel.Warn);
                return;
            }

            parsedFaults.Add(fault);
        }

        foreach (HarvestAcceptanceFault fault in parsedFaults)
            this.AcceptanceFaults.Arm(fault);

        this.Monitor.Log($"Acceptance faults armed: {this.AcceptanceFaults.Describe()}.", LogLevel.Alert);
    }
#endif

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
        this.StorageSortRecovery?.OnSaveLoaded();
        this.HarvestingContracts?.OnSaveLoaded();
        this.MultiplayerContracts?.OnSaveLoaded();
        this.RecurringContracts?.OnSaveLoaded();
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
            Context.IsMainPlayer ? this.OpenRecurringContractMenu : null,
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
        WorkContractPreview preview = ContractPreviewService.Create(
            friendshipHearts,
            Game1.dayOfMonth,
            worker.InternalName,
            task);

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
        this.StorageSortContracts?.Update();
        this.StorageSortRecovery?.Update();
        this.MultiplayerContracts?.Update();
        this.RecurringContracts?.Update(this.HasActiveNamedContract());
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        this.WateringContracts?.OnDayEnding();
        this.HarvestingContracts?.OnDayEnding();
        this.StorageSortContracts?.OnSaving();
        this.MultiplayerContracts?.Update();
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        this.PrepareForSave();
    }

    private void OnSaveCreating(object? sender, SaveCreatingEventArgs e)
    {
        this.PrepareForSave();
    }

    private void PrepareForSave()
    {
        this.WateringContracts?.OnDayEnding();
        this.HarvestingContracts?.OnSaving();
        this.StorageSortContracts?.OnSaving();
        this.MultiplayerContracts?.Update();
        this.MultiplayerContracts?.OnSaving();
        this.RecurringContracts?.OnSaving();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.WateringContracts?.OnReturnedToTitle();
        this.HarvestingContracts?.OnReturnedToTitle();
        this.StorageSortContracts?.OnReturnedToTitle();
        this.StorageSortRecovery?.OnReturnedToTitle();
        this.MultiplayerContracts?.Update();
        this.MultiplayerContracts?.OnReturnedToTitle();
        this.RecurringContracts?.OnReturnedToTitle();
    }

    private bool HasActiveNamedContract()
    {
        return this.WateringContracts?.HasActiveContract == true
            || this.HarvestingContracts?.HasActiveContract == true
            || this.StorageSortContracts?.HasActiveContract == true
            || this.MultiplayerContracts?.HasObservedActiveContract == true
            || this.MultiplayerContracts?.HasPendingRequest == true;
    }

    private bool TryStartNamedContract(string workerInternalName, NamedFarmTask task)
    {
        return this.MultiplayerContracts?.RequestStart(workerInternalName, task) == true;
    }

    private void OpenRecurringContractMenu()
    {
        if (!Context.IsWorldReady)
        {
            this.Monitor.Log(this.Helper.Translation.Get("cmd.roster-world-not-ready"), LogLevel.Info);
            return;
        }

        if (!Context.IsMainPlayer)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Helper.Translation.Get("recurring.hud.host-only"),
                HUDMessage.error_type));
            return;
        }

        if (this.RecurringContracts is null || this.WorkerRoster is null)
            return;

        Game1.activeClickableMenu = new RecurringContractMenu(
            this.RecurringContracts,
            this.Helper.Translation,
            () => this.OpenRecurringWorkerRoster());
    }

    private void OpenRecurringWorkerRoster(int initialPage = 0)
    {
        if (!Context.IsWorldReady
            || !Context.IsMainPlayer
            || this.WorkerRoster is null)
            return;

        IReadOnlyList<WorkerRosterEntry> workers = this.WorkerRoster.GetRoster();
        Game1.activeClickableMenu = new WorkerRosterMenu(
            workers,
            this.Helper.Translation,
            (worker, page) => this.OpenRecurringTaskSelection(worker, page, workers),
            this.OpenRecurringContractMenu,
            initialPage);
    }

    private void OpenRecurringTaskSelection(
        WorkerRosterEntry worker,
        int rosterPage,
        IReadOnlyList<WorkerRosterEntry> availableWorkers)
    {
        if (!Context.IsWorldReady
            || !Context.IsMainPlayer
            || worker.Availability.State != WorkerAvailabilityState.EligibleForPreview)
            return;

        Game1.activeClickableMenu = new WorkerTaskSelectionMenu(
            worker,
            this.Helper.Translation,
            () => this.OpenRecurringWorkerRoster(rosterPage),
            task => this.OpenRecurringAuthorization(worker, rosterPage, task, availableWorkers),
            includeStorageSorting: false);
    }

    private void OpenRecurringAuthorization(
        WorkerRosterEntry worker,
        int rosterPage,
        NamedFarmTask task,
        IReadOnlyList<WorkerRosterEntry> availableWorkers)
    {
        if (!Context.IsWorldReady
            || !Context.IsMainPlayer
            || this.RecurringContracts is null)
            return;

        Game1.activeClickableMenu = new RecurringContractAuthorizationMenu(
            worker,
            task,
            availableWorkers,
            this.RecurringContracts,
            this.Helper.Translation,
            () => this.OpenRecurringTaskSelection(worker, rosterPage, availableWorkers),
            this.OpenRecurringContractMenu);
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

    private void OpenHarvestQuarantine()
    {
        if (!Context.IsWorldReady)
        {
            this.Monitor.Log(this.Helper.Translation.Get("cmd.roster-world-not-ready"), LogLevel.Info);
            return;
        }

        if (!Context.IsMainPlayer)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Helper.Translation.Get("quarantine.host-only"),
                HUDMessage.error_type));
            return;
        }

        if (this.HasActiveNamedContract())
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Helper.Translation.Get("overflow.busy"),
                HUDMessage.error_type));
            return;
        }

        if (this.StorageSortRecovery?.TryRecover() != true
            || this.HarvestingContracts?.TryRecoverQuarantinedCargo() != true)
            return;

        NetMutex mutex = Game1.player.team.GetOrCreateGlobalInventoryMutex(
            HarvestingContractExecutionController.QuarantineInventoryId);
        mutex.RequestLock(
            () =>
            {
                Chest proxy = new(playerChest: true, Vector2.Zero)
                {
                    GlobalInventoryId = HarvestingContractExecutionController.QuarantineInventoryId
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
                this.Helper.Translation.Get("quarantine.locked"),
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

    private void ShowLastWorkReport()
    {
        if (!Context.IsWorldReady)
        {
            this.Monitor.Log(this.Helper.Translation.Get("cmd.roster-world-not-ready"), LogLevel.Info);
            return;
        }

        if (this.MultiplayerContracts?.TryGetRecentResult(
                Game1.player.UniqueMultiplayerID,
                out ContractResultMessage? result) != true
            || result is null)
        {
            this.Monitor.Log(this.Helper.Translation.Get("report.none"), LogLevel.Info);
            return;
        }

        string task = this.Helper.Translation.Get(result.Task switch
        {
            NamedFarmTask.Watering => "contract.task.watering",
            NamedFarmTask.Harvesting => "contract.task.harvesting",
            NamedFarmTask.StorageSorting => "contract.task.storage-sorting",
            _ => "contract.task.harvesting"
        });
        string status = result.Succeeded
            ? this.Helper.Translation.Get("report.status.completed")
            : this.Helper.Translation.Get("report.status.stopped", new
            {
                reason = this.Helper.Translation.Get(result.ReasonKey)
            });
        string items = NamedContractReportFormatter.FormatItems(
            result.ProducedItems,
            this.Helper.Translation.Get("report.items.none"));

        this.Monitor.Log(this.Helper.Translation.Get("report.header", new
        {
            worker = result.WorkerName,
            task,
            status
        }), LogLevel.Info);
        if (result.Task == NamedFarmTask.StorageSorting)
        {
            this.Monitor.Log(this.Helper.Translation.Get("storage-sort.report.work", new
            {
                completed = result.CompletedTransfers.Length,
                items = result.ChestItems,
                skipped = result.SkippedTransfers.Length
            }), LogLevel.Info);
        }
        else
        {
            this.Monitor.Log(this.Helper.Translation.Get("report.work", new
            {
                completed = result.CompletedWork,
                items
            }), LogLevel.Info);
        }
        this.Monitor.Log(this.Helper.Translation.Get("report.destinations", new
        {
            player = result.PlayerItems,
            chest = result.ChestItems,
            overflow = result.OverflowItems,
            quarantine = result.QuarantinedItems,
            dropped = result.DroppedItems
        }), LogLevel.Info);
        this.Monitor.Log(this.Helper.Translation.Get("report.billing", new
        {
            hours = result.BillableHours,
            paid = result.ChargedGold,
            refunded = result.RefundedGold
        }), LogLevel.Info);

        if (result.Task == NamedFarmTask.StorageSorting)
        {
            foreach (ContractTransferReportMessage transfer in result.CompletedTransfers
                         .OrderBy(transfer => transfer.Sequence))
            {
                this.LogStorageSortTransfer("storage-sort.report.completed-transfer", transfer);
            }
            foreach (ContractTransferReportMessage transfer in result.SkippedTransfers
                         .OrderBy(transfer => transfer.Sequence))
            {
                this.LogStorageSortTransfer("storage-sort.report.skipped-transfer", transfer);
            }
        }
    }

    private void LogStorageSortTransfer(
        string translationKey,
        ContractTransferReportMessage transfer)
    {
        this.Monitor.Log(this.Helper.Translation.Get(translationKey, new
        {
            transfer.Sequence,
            item = transfer.DisplayName,
            transfer.Quality,
            transfer.Category,
            transfer.Quantity,
            transfer.SourceX,
            transfer.SourceY,
            transfer.DestinationX,
            transfer.DestinationY
        }), LogLevel.Info);
    }

}
