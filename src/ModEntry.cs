using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;
using System.Reflection;

namespace EvilFarmOwner;

public sealed class ModEntry : Mod
{
    private ModConfig Config = new();
    private bool HasKnownHotkeyConflict;
    private WorkerRosterService? WorkerRoster;
    private WateringContractExecutionController? WateringContracts;
    private HarvestingContractExecutionController? HarvestingContracts;
    private AnimalCareContractExecutionController? AnimalCareContracts;
    private StorageSortRecoveryManager? StorageSortRecovery;
    private StorageSortContractExecutionController? StorageSortContracts;
    private ConcurrentFarmWorkContractExecutionController? FarmWorkContracts;
    private MultiplayerContractCoordinator? MultiplayerContracts;
    private RecurringContractCoordinator? RecurringContracts;
    private readonly HarvestAcceptanceFaults AcceptanceFaults = new();

    public override void Entry(IModHelper helper)
    {
        this.Config = helper.ReadConfig<ModConfig>();
        this.Config.Normalize();
        helper.WriteConfig(this.Config);
        this.RefreshHotkeyConflict();
        this.WorkerRoster = new WorkerRosterService(
            this.Monitor,
            () => this.GetEffectiveContractSettings());
        this.WateringContracts = new WateringContractExecutionController(
            helper.Translation,
            this.Monitor,
            this.WorkerRoster);
        this.HarvestingContracts = new HarvestingContractExecutionController(
            helper.Translation,
            this.Monitor,
            this.WorkerRoster,
            this.AcceptanceFaults);
        this.AnimalCareContracts = new AnimalCareContractExecutionController(
            this.Monitor,
            this.Helper.Translation);
        this.StorageSortRecovery = new StorageSortRecoveryManager(this.Monitor);
        this.StorageSortContracts = new StorageSortContractExecutionController(
            helper.Translation,
            this.Monitor,
            this.WorkerRoster,
            this.StorageSortRecovery);
        this.FarmWorkContracts = new ConcurrentFarmWorkContractExecutionController(
            helper.Translation,
            this.Monitor,
            this.WorkerRoster,
            this.StorageSortRecovery,
            this.AcceptanceFaults,
            () => this.Config.CreateSnapshot());
        this.MultiplayerContracts = new MultiplayerContractCoordinator(
            helper,
            this.ModManifest,
            helper.Translation,
            this.Monitor,
            this.WateringContracts,
            this.HarvestingContracts,
            this.StorageSortContracts,
            this.FarmWorkContracts,
            () => this.Config.CreateSnapshot());
        this.RecurringContracts = new RecurringContractCoordinator(
            helper,
            helper.Translation,
            this.Monitor,
            this.WorkerRoster,
            this.MultiplayerContracts,
            () => this.Config.CreateSnapshot());

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
        helper.ConsoleCommands.Add(
            "efo_acceptance_click",
            "Click the active game menu at test coordinates. This command is excluded from production builds.",
            this.HandleAcceptanceClickCommand);
        this.Monitor.Log(
            "ACCEPTANCE TEST BUILD: harvest storage fault injection is enabled. Do not distribute this DLL.",
            LogLevel.Alert);
#endif
    }

#if EFO_ACCEPTANCE_FAULTS
    private void HandleAcceptanceClickCommand(string command, string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "status", StringComparison.OrdinalIgnoreCase))
        {
            if (Game1.activeClickableMenu is not IClickableMenu activeMenu)
            {
                this.Monitor.Log("Acceptance menu status requires an active game menu.", LogLevel.Warn);
                return;
            }

            IEnumerable<string> components = activeMenu.GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => typeof(ClickableComponent).IsAssignableFrom(field.FieldType))
                .Select(field => (field.Name, Component: field.GetValue(activeMenu) as ClickableComponent))
                .Where(value => value.Component is not null)
                .Select(value => $"{value.Name}={value.Component!.bounds}");
            this.Monitor.Log(
                $"Acceptance menu {activeMenu.GetType().Name}: bounds=({activeMenu.xPositionOnScreen},{activeMenu.yPositionOnScreen},{activeMenu.width},{activeMenu.height}); components=[{string.Join("; ", components)}].",
                LogLevel.Info);
            return;
        }

        if (args.Length != 2
            || !int.TryParse(args[0], out int x)
            || !int.TryParse(args[1], out int y))
        {
            this.Monitor.Log("Usage: efo_acceptance_click status | <x> <y>", LogLevel.Info);
            return;
        }

        if (Game1.activeClickableMenu is not IClickableMenu menu)
        {
            this.Monitor.Log("Acceptance menu click requires an active game menu.", LogLevel.Warn);
            return;
        }

        menu.receiveLeftClick(x, y);
        this.Monitor.Log(
            $"Acceptance menu click invoked {menu.GetType().Name}.receiveLeftClick({x}, {y}).",
            LogLevel.Debug);
    }

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
        this.Config.Normalize();
        this.Helper.WriteConfig(this.Config);
        this.RefreshHotkeyConflict();
        this.MultiplayerContracts?.NotifyHostContractSettingsChanged();

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

    private void OpenWorkerRoster(int initialPage = 0, IEnumerable<string>? initialSelections = null)
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

        IReadOnlyList<WorkerRosterEntry> roster = this.WorkerRoster.GetRoster();
        ContractSettingsSnapshot settings = this.GetEffectiveContractSettings();
        int selectableWorkers = Math.Min(
            settings.MaximumConcurrentWorkers,
            WorkStagePartitionPolicy.CountEnabled(settings.EnabledStages));
        if (selectableWorkers <= 1)
        {
            Game1.activeClickableMenu = new WorkerRosterMenu(
                roster,
                this.Helper.Translation,
                (worker, page) => this.OpenWorkContractPreview(
                    worker,
                    page,
                    NamedFarmTask.FarmWork),
                Context.IsMainPlayer ? this.OpenRecurringContractMenu : null,
                initialPage);
            return;
        }

        Game1.activeClickableMenu = new WorkerRosterMenu(
            roster,
            this.Helper.Translation,
            (workers, page) => this.OpenWorkContractPreview(
                workers,
                page,
                NamedFarmTask.FarmWork),
            selectableWorkers,
            Context.IsMainPlayer ? this.OpenRecurringContractMenu : null,
            initialPage,
            initialSelections);
    }

    private void OpenWorkContractPreview(
        WorkerRosterEntry worker,
        int rosterPage,
        NamedFarmTask task)
    {
        this.OpenWorkContractPreview(new[] { worker }, rosterPage, task);
    }

    private void OpenWorkContractPreview(
        IReadOnlyList<WorkerRosterEntry> workers,
        int rosterPage,
        NamedFarmTask task)
    {
        if (!Context.IsWorldReady
            || workers.Count == 0
            || workers.Any(worker => worker.Availability.State != WorkerAvailabilityState.EligibleForPreview))
            return;

        WorkerRosterEntry firstWorker = workers[0];
        int friendshipHearts = Game1.player.getFriendshipHeartLevelForNPC(firstWorker.InternalName);
        ContractSettingsSnapshot settings = this.GetEffectiveContractSettings();
        WorkContractPreview preview = ContractPreviewService.Create(
            friendshipHearts,
            Game1.dayOfMonth,
            firstWorker.InternalName,
            task,
            settings);

        Game1.activeClickableMenu = new WorkContractPreviewMenu(
            workers,
            preview,
            task,
            this.Helper.Translation,
            () => this.OpenWorkerRoster(
                rosterPage,
                workers.Select(worker => worker.InternalName)),
            destinationMode => this.TryStartNamedContract(
                workers.Select(worker => worker.InternalName).ToArray(),
                task,
                destinationMode),
            settings.DefaultHarvestDestination);
    }

    private ContractSettingsSnapshot GetEffectiveContractSettings()
    {
        return Context.IsWorldReady && !Context.IsMainPlayer
            ? this.MultiplayerContracts?.GetHostContractSettings()
                ?? ContractSettingsSnapshot.Default
            : this.Config.CreateSnapshot();
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        this.WateringContracts?.Update();
        this.HarvestingContracts?.Update();
        this.AnimalCareContracts?.Update();
        this.StorageSortContracts?.Update();
        this.FarmWorkContracts?.Update();
        this.StorageSortRecovery?.Update();
        this.MultiplayerContracts?.Update();
        this.RecurringContracts?.Update(this.HasActiveNamedContract());
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        this.WateringContracts?.OnDayEnding();
        this.HarvestingContracts?.OnDayEnding();
        this.AnimalCareContracts?.OnDayEnding();
        this.StorageSortContracts?.OnSaving();
        this.FarmWorkContracts?.OnDayEnding();
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
        this.AnimalCareContracts?.OnDayEnding();
        this.StorageSortContracts?.OnSaving();
        this.FarmWorkContracts?.OnDayEnding();
        this.MultiplayerContracts?.Update();
        this.MultiplayerContracts?.OnSaving();
        this.RecurringContracts?.OnSaving();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        this.WateringContracts?.OnReturnedToTitle();
        this.HarvestingContracts?.OnReturnedToTitle();
        this.AnimalCareContracts?.OnReturnedToTitle();
        this.StorageSortContracts?.OnReturnedToTitle();
        this.FarmWorkContracts?.OnReturnedToTitle();
        this.StorageSortRecovery?.OnReturnedToTitle();
        this.MultiplayerContracts?.Update();
        this.MultiplayerContracts?.OnReturnedToTitle();
        this.RecurringContracts?.OnReturnedToTitle();
    }

    private bool HasActiveNamedContract()
    {
        return this.FarmWorkContracts?.HasActiveContract == true
            || this.WateringContracts?.HasActiveContract == true
            || this.HarvestingContracts?.HasActiveContract == true
            || this.StorageSortContracts?.HasActiveContract == true
            || this.MultiplayerContracts?.HasObservedActiveContract == true
            || this.MultiplayerContracts?.HasPendingRequest == true;
    }

    private bool TryStartNamedContract(
        string workerInternalName,
        NamedFarmTask task,
        HarvestDestinationMode destinationMode)
    {
        return this.TryStartNamedContract(new[] { workerInternalName }, task, destinationMode);
    }

    private bool TryStartNamedContract(
        IReadOnlyList<string> workerInternalNames,
        NamedFarmTask task,
        HarvestDestinationMode destinationMode)
    {
        return this.MultiplayerContracts?.RequestStart(
            workerInternalNames,
            task,
            destinationMode) == true;
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
            (worker, page) => this.OpenRecurringAuthorization(
                worker,
                page,
                NamedFarmTask.FarmWork,
                workers),
            this.OpenRecurringContractMenu,
            initialPage);
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
            () => this.OpenRecurringWorkerRoster(rosterPage),
            this.OpenRecurringContractMenu,
            this.Config.CreateSnapshot());
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
            NamedFarmTask.FarmWork => "contract.task.farm-work",
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
        if (result.Task == NamedFarmTask.FarmWork && result.CompletedTransfers.Length > 0)
        {
            this.Monitor.Log(this.Helper.Translation.Get("storage-sort.report.work", new
            {
                completed = result.CompletedTransfers.Length,
                items = result.CompletedTransfers.Sum(transfer => transfer.Quantity),
                skipped = result.SkippedTransfers.Length
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
        if (result.Task is NamedFarmTask.FarmWork or NamedFarmTask.Harvesting)
        {
            this.Monitor.Log(this.Helper.Translation.Get("report.destination-mode", new
            {
                destination = this.Helper.Translation.Get(
                    result.HarvestDestination == HarvestDestinationMode.RequesterInventory
                        ? "contract.destination.requester"
                        : "contract.destination.chests")
            }), LogLevel.Info);
        }
        this.Monitor.Log(this.Helper.Translation.Get("report.billing", new
        {
            hours = result.BillableHours,
            paid = result.ChargedGold,
            refunded = result.RefundedGold
        }), LogLevel.Info);

        if (result.Task is NamedFarmTask.FarmWork or NamedFarmTask.StorageSorting)
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
