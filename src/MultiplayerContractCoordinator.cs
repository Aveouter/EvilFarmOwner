using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;

namespace EvilFarmOwner;

internal sealed class MultiplayerContractCoordinator
{
    private const int PendingTimeoutTicks = 900;
    private const int ClientResponseHistoryCapacity = 256;

    private readonly IModHelper Helper;
    private readonly IManifest Manifest;
    private readonly ITranslationHelper Translation;
    private readonly IMonitor Monitor;
    private readonly WateringContractExecutionController WateringContracts;
    private readonly HarvestingContractExecutionController HarvestingContracts;
    private readonly StorageSortContractExecutionController StorageSortContracts;
    private readonly ConcurrentFarmWorkContractExecutionController FarmWorkContracts;
    private readonly Func<ContractSettingsSnapshot> GetLocalSettings;
    private readonly ProcessedContractRequestLedger ProcessedRequests = new();
    private readonly ContractSnapshotTracker SnapshotTracker = new();
    private readonly HostStateVersionTracker RemoteStateVersions = new();
    private readonly HostSessionTracker ClientHostSession = new();
    private readonly Dictionary<long, ContractResultMessage> RecentResults = new();
    private readonly Dictionary<string, long> HostSequences = new(StringComparer.Ordinal);
    private readonly Queue<string> HostSequenceOrder = new();
    private readonly HashSet<string> SeenClientResponses = new(StringComparer.Ordinal);
    private readonly Queue<string> ClientResponseOrder = new();

    private string HostSessionId = "";
    private long HostOrder;
    private long HostStateVersion;
    private ContractStartRequestMessage? PendingRequest;
    private int PendingTicks;
    private readonly Dictionary<string, ContractSnapshotMessage> CurrentHostSnapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ContractSnapshotMessage> RemoteActiveSnapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> LastHostStateSignatures =
        new(StringComparer.OrdinalIgnoreCase);
    private bool RecoveryStateHealthy = true;
    private ContractSettingsSnapshot RemoteHostSettings = ContractSettingsSnapshot.Default;
    private long HostSettingsVersion;
    private long RemoteSettingsVersion;

    public string? LastRequestFailureKey { get; private set; }

    public MultiplayerContractCoordinator(
        IModHelper helper,
        IManifest manifest,
        ITranslationHelper translation,
        IMonitor monitor,
        WateringContractExecutionController wateringContracts,
        HarvestingContractExecutionController harvestingContracts,
        StorageSortContractExecutionController storageSortContracts,
        ConcurrentFarmWorkContractExecutionController farmWorkContracts,
        Func<ContractSettingsSnapshot>? getLocalSettings = null)
    {
        this.Helper = helper;
        this.Manifest = manifest;
        this.Translation = translation;
        this.Monitor = monitor;
        this.WateringContracts = wateringContracts;
        this.HarvestingContracts = harvestingContracts;
        this.StorageSortContracts = storageSortContracts;
        this.FarmWorkContracts = farmWorkContracts;
        this.GetLocalSettings = getLocalSettings ?? (() => ContractSettingsSnapshot.Default);
    }

    public bool HasPendingRequest => this.PendingRequest is not null;

    public bool HasObservedActiveContract => this.CurrentHostSnapshots.Count > 0
        || this.RemoteActiveSnapshots.Count > 0;

    public ContractSettingsSnapshot GetHostContractSettings()
    {
        ContractSettingsSnapshot settings = Context.IsMainPlayer
            ? this.GetLocalSettings()
            : this.RemoteHostSettings;
        return settings.IsValid ? settings : ContractSettingsSnapshot.Default;
    }

    public void NotifyHostContractSettingsChanged()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer || !Context.IsMultiplayer)
            return;

        this.HostSettingsVersion++;
        this.BroadcastMessage(this.CreateSettingsMessage(), MultiplayerContractProtocol.SettingsType);
    }

    public bool TryGetRecentResult(long requestingPlayerId, out ContractResultMessage? result)
    {
        return this.RecentResults.TryGetValue(requestingPlayerId, out result);
    }

    public string GetDiagnosticStatus()
    {
        string role = Context.IsMainPlayer ? "host" : "farmhand";
        string session = Context.IsMainPlayer ? this.HostSessionId : this.ClientHostSession.Current;
        string active = this.GetHostRuntimeStates().FirstOrDefault()?.ContractId
            ?? this.RemoteActiveSnapshots.Values.FirstOrDefault()?.ContractId
            ?? "none";
        string pending = this.PendingRequest?.RequestId ?? "none";
        string quarantineHealth = Context.IsMainPlayer
            ? (!this.HarvestingContracts.HasUnresolvedQuarantineRecovery
                && !this.StorageSortContracts.HasUnresolvedRecovery).ToString()
            : "host-authoritative";
        return $"EFO network: role={role}, session={session}, active={active}, pending={pending}, "
            + $"processed={this.ProcessedRequests.Count}, recoveryHealthy={this.RecoveryStateHealthy}, "
            + $"quarantineHealthy={quarantineHealth}, "
            + $"stateVersion={(Context.IsMainPlayer ? this.HostStateVersion : this.RemoteStateVersions.Latest)}";
    }

    public bool RequestStart(
        string workerInternalName,
        NamedFarmTask task,
        HarvestDestinationMode destinationMode = HarvestDestinationMode.ClassifiedChests,
        string? requestId = null)
    {
        return this.RequestStart(
            new[] { workerInternalName },
            task,
            destinationMode,
            requestId);
    }

    public bool RequestStart(
        IEnumerable<string> workerInternalNames,
        NamedFarmTask task,
        HarvestDestinationMode destinationMode = HarvestDestinationMode.ClassifiedChests,
        string? requestId = null)
    {
        this.LastRequestFailureKey = null;
        if (!Context.IsWorldReady)
            return false;

        string[] workers = workerInternalNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (workers.Length == 0)
        {
            this.LastRequestFailureKey = "multiplayer.reject.worker";
            return false;
        }

        if (this.PendingRequest is not null)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("multiplayer.hud.pending-existing"),
                HUDMessage.error_type));
            return true;
        }

        ContractStartRequestMessage request = new()
        {
            SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
            ModVersion = this.Manifest.Version.ToString(),
            SaveId = Game1.uniqueIDForThisGame,
            TotalDays = Game1.Date.TotalDays,
            RequestId = string.IsNullOrWhiteSpace(requestId)
                ? Guid.NewGuid().ToString("N")
                : requestId,
            RequestingPlayerId = Game1.player.UniqueMultiplayerID,
            WorkerName = workers[0],
            WorkerNames = workers,
            Task = task,
            HarvestDestination = destinationMode
        };

        if (Context.IsMainPlayer)
        {
            ContractStartResponseMessage response = this.ProcessHostRequest(
                request,
                Game1.player.UniqueMultiplayerID);
            this.LastRequestFailureKey = response.Accepted ? null : response.ReasonKey;
            return response.Accepted;
        }

        this.PendingRequest = request;
        this.PendingTicks = 0;
        if (this.ClientHostSession.HasSession)
        {
            this.SendMessage(
                request,
                MultiplayerContractProtocol.StartRequestType,
                Game1.MasterPlayer.UniqueMultiplayerID);
        }
        else
        {
            this.SendSyncRequest();
        }
        Game1.addHUDMessage(new HUDMessage(
            this.Translation.Get("multiplayer.hud.pending"),
            HUDMessage.newQuest_type));
        return true;
    }

    public void OnSaveLoaded()
    {
        this.ResetSessionState();
        if (Context.IsMainPlayer)
        {
            this.HostSessionId = Guid.NewGuid().ToString("N");
            this.LoadRecoveryState();
        }
        else if (Context.IsMultiplayer)
        {
            this.SendSyncRequest();
        }
    }

    public void OnSaving()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        this.PublishHostCompletions();
        bool hasActiveContract = this.GetHostRuntimeStates().Count > 0;
        if (hasActiveContract)
        {
            this.RecoveryStateHealthy = false;
            this.Monitor.Log(
                "A named contract remained active at save time; persisting an explicit fail-closed recovery marker.",
                LogLevel.Error);
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("multiplayer.hud.recovery-save-failed"),
                HUDMessage.error_type));
        }

        if (!this.RecoveryStateHealthy && !hasActiveContract)
        {
            this.Monitor.Log(
                "Multiplayer recovery state is unhealthy; preserving the existing save data and keeping contracts fail-closed.",
                LogLevel.Error);
            return;
        }

        MultiplayerRecoverySaveData state = MultiplayerRecoveryState.Create(
            this.Manifest.Version.ToString(),
            Game1.uniqueIDForThisGame,
            this.ProcessedRequests.GetAll(),
            this.RecentResults.Values.OrderBy(result => result.RequestingPlayerId),
            isClean: !hasActiveContract);
        try
        {
            this.Helper.Data.WriteSaveData(MultiplayerRecoveryState.SaveDataKey, state);
            this.Monitor.Log(
                $"Persisted {state.ProcessedRequests.Length} processed contract request(s) and "
                + $"{state.RecentResults.Length} recent result(s).",
                LogLevel.Debug);
        }
        catch (Exception ex)
        {
            this.RecoveryStateHealthy = false;
            this.Monitor.Log($"Could not persist multiplayer recovery state: {ex}", LogLevel.Error);
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("multiplayer.hud.recovery-save-failed"),
                HUDMessage.error_type));
        }
    }

    public void Update()
    {
        if (!Context.IsWorldReady)
            return;

        if (Context.IsMainPlayer)
        {
            this.PublishHostRuntimeState();
            this.PublishHostCompletions();
            return;
        }

        if (this.PendingRequest is null)
            return;

        this.PendingTicks++;
        if (this.PendingTicks < PendingTimeoutTicks)
            return;

        this.PendingRequest = null;
        this.PendingTicks = 0;
        Game1.addHUDMessage(new HUDMessage(
            this.Translation.Get("multiplayer.hud.timeout"),
            HUDMessage.error_type));
        this.SendSyncRequest();
    }

    public void OnReturnedToTitle()
    {
        this.ResetSessionState();
    }

    public void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        if (Context.IsMainPlayer || !e.Peer.IsHost)
            return;

        this.ClientHostSession.Clear();
        this.SnapshotTracker.Clear();
        this.RemoteStateVersions.Clear();
        this.RemoteActiveSnapshots.Clear();
        this.SeenClientResponses.Clear();
        this.ClientResponseOrder.Clear();
        this.SendSyncRequest();
    }

    public void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
    {
        if (Context.IsMainPlayer)
        {
            NamedContractRuntimeState? active = this.GetHostRuntimeStates().FirstOrDefault();
            if (active?.RequestingPlayerId == e.Peer.PlayerID)
            {
                this.Monitor.Log(
                    $"Contract requester {e.Peer.PlayerID} disconnected; host will keep authority and complete safe delivery/return.",
                    LogLevel.Info);
            }
        }
        else if (e.Peer.IsHost)
        {
            this.ClientHostSession.Clear();
            this.SnapshotTracker.Clear();
            this.RemoteStateVersions.Clear();
            this.RemoteActiveSnapshots.Clear();
            this.SeenClientResponses.Clear();
            this.ClientResponseOrder.Clear();
        }
    }

    public void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
    {
        if (!string.Equals(e.FromModID, this.Manifest.UniqueID, StringComparison.Ordinal)
            || !Context.IsWorldReady)
            return;

        try
        {
            switch (e.Type)
            {
                case MultiplayerContractProtocol.StartRequestType when Context.IsMainPlayer:
                    {
                        ContractStartRequestMessage request = e.ReadAs<ContractStartRequestMessage>();
                        ContractStartResponseMessage response = this.ProcessHostRequest(request, e.FromPlayerID);
                        if (e.FromPlayerID != Game1.player.UniqueMultiplayerID)
                        {
                            this.SendMessage(
                                response,
                                MultiplayerContractProtocol.StartResponseType,
                                e.FromPlayerID);
                        }
                        break;
                    }

                case MultiplayerContractProtocol.StartResponseType when !Context.IsMainPlayer
                    && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID:
                    this.HandleStartResponse(e.ReadAs<ContractStartResponseMessage>());
                    break;

                case MultiplayerContractProtocol.SnapshotType when !Context.IsMainPlayer
                    && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID:
                    this.HandleSnapshot(e.ReadAs<ContractSnapshotMessage>());
                    break;

                case MultiplayerContractProtocol.ResultType when !Context.IsMainPlayer
                    && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID:
                    this.HandleResult(e.ReadAs<ContractResultMessage>());
                    break;

                case MultiplayerContractProtocol.SyncRequestType when Context.IsMainPlayer:
                    this.HandleSyncRequest(e.ReadAs<ContractSyncRequestMessage>(), e.FromPlayerID);
                    break;

                case MultiplayerContractProtocol.SyncStateType when !Context.IsMainPlayer
                    && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID:
                    this.HandleSyncState(e.ReadAs<ContractSyncStateMessage>());
                    break;

                case MultiplayerContractProtocol.SettingsType when !Context.IsMainPlayer
                    && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID:
                    this.HandleSettings(e.ReadAs<ContractSettingsMessage>());
                    break;
            }
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Rejected malformed multiplayer contract message '{e.Type}' from player {e.FromPlayerID}: {ex}",
                LogLevel.Warn);
        }
    }

    private ContractStartResponseMessage ProcessHostRequest(
        ContractStartRequestMessage request,
        long senderPlayerId)
    {
        if (request.RequestingPlayerId == senderPlayerId
            && this.ProcessedRequests.TryGet(
                request.RequestingPlayerId,
                request.RequestId,
                out ContractStartResponseMessage? prior)
            && prior is not null)
            return prior;

        long order = ++this.HostOrder;
        ContractStartResponseMessage response = new()
        {
            SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
            SaveId = Game1.uniqueIDForThisGame,
            HostSessionId = this.HostSessionId,
            HostOrder = order,
            RequestId = request.RequestId,
            RequestingPlayerId = request.RequestingPlayerId
        };

        ContractProtocolContext protocolContext = new(
            this.Manifest.Version.ToString(),
            Game1.uniqueIDForThisGame,
            Game1.Date.TotalDays,
            Game1.getOnlineFarmers()
                .Select(farmer => farmer.UniqueMultiplayerID)
                .ToHashSet(),
            this.GetHostContractSettings().MaximumConcurrentWorkers);
        ContractRequestValidationFailure validation = ContractRequestValidator.Validate(
            request,
            senderPlayerId,
            protocolContext);
        if (validation != ContractRequestValidationFailure.None)
        {
            response.Accepted = false;
            response.ReasonKey = ContractRequestValidator.GetReasonKey(validation);
            if (CanTrackRequest(request, senderPlayerId))
                this.ProcessedRequests.Record(response);
            this.Monitor.Log(
                $"Rejected contract request {request.RequestId} from player {senderPlayerId}: {validation}.",
                LogLevel.Warn);
            return response;
        }

        if (!this.RecoveryStateHealthy)
        {
            response.Accepted = false;
            response.ReasonKey = "multiplayer.reject.recovery-state";
            this.ProcessedRequests.Record(response);
            return response;
        }

        if (this.FarmWorkContracts.HasActiveContract
            || this.WateringContracts.HasActiveContract
            || this.HarvestingContracts.HasActiveContract
            || this.StorageSortContracts.HasActiveContract)
        {
            response.Accepted = false;
            response.ReasonKey = "contract.start.already-active";
            this.ProcessedRequests.Record(response);
            return response;
        }

        bool accepted;
        string? failureKey;
        string? contractId;
        switch (request.Task)
        {
            case NamedFarmTask.FarmWork:
                accepted = this.FarmWorkContracts.TryStart(
                    request.RequestingPlayerId,
                    request.GetWorkerNames(),
                    request.RequestId,
                    request.HarvestDestination);
                failureKey = this.FarmWorkContracts.LastStartFailureKey;
                contractId = this.FarmWorkContracts.ActiveContractId;
                break;

            case NamedFarmTask.Watering:
                accepted = this.WateringContracts.TryStart(
                    request.RequestingPlayerId,
                    request.WorkerName,
                    request.RequestId);
                failureKey = this.WateringContracts.LastStartFailureKey;
                contractId = this.WateringContracts.ActiveContractId;
                break;

            case NamedFarmTask.Harvesting:
                accepted = this.HarvestingContracts.TryStart(
                    request.RequestingPlayerId,
                    request.WorkerName,
                    request.RequestId,
                    request.HarvestDestination);
                failureKey = this.HarvestingContracts.LastStartFailureKey;
                contractId = this.HarvestingContracts.ActiveContractId;
                break;

            case NamedFarmTask.StorageSorting:
                accepted = this.StorageSortContracts.TryStart(
                    request.RequestingPlayerId,
                    request.WorkerName,
                    request.RequestId);
                failureKey = this.StorageSortContracts.LastStartFailureKey;
                contractId = this.StorageSortContracts.ActiveContractId;
                break;

            default:
                accepted = false;
                failureKey = "multiplayer.reject.task";
                contractId = null;
                break;
        }

        response.Accepted = accepted && !string.IsNullOrWhiteSpace(contractId);
        response.ContractId = response.Accepted ? contractId! : "";
        response.ReasonKey = response.Accepted
            ? ""
            : NormalizeFailureKey(failureKey);
        this.ProcessedRequests.Record(response);
        return response;
    }

    private void PublishHostRuntimeState()
    {
        IReadOnlyList<NamedContractRuntimeState> states = this.GetHostRuntimeStates();
        if (states.Count == 0)
        {
            this.CurrentHostSnapshots.Clear();
            this.LastHostStateSignatures.Clear();
            return;
        }

        HashSet<string> activeWorkers = states.Select(state => state.WorkerName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string stale in this.CurrentHostSnapshots.Keys
                     .Where(worker => !activeWorkers.Contains(worker)).ToArray())
        {
            ContractSnapshotMessage retired = this.CurrentHostSnapshots[stale];
            retired.IsActive = false;
            retired.Sequence = this.NextHostSequence(retired.ContractId);
            retired.StateVersion = ++this.HostStateVersion;
            if (Context.IsMultiplayer)
                this.BroadcastMessage(retired, MultiplayerContractProtocol.SnapshotType);
            this.CurrentHostSnapshots.Remove(stale);
            this.LastHostStateSignatures.Remove(stale);
        }

        foreach (NamedContractRuntimeState state in states)
        {
            string signature = string.Join(
                '|', state.ContractId, state.Phase, state.HarvestDestination,
                state.EfficiencyMultiplier, state.ArrivalX, state.ArrivalY,
                state.ArrivalSide, state.EntranceSwitches, state.TargetX, state.TargetY,
                state.CompletedWork, state.CargoCount,
                string.Join(',', state.Cargo.Select(item => $"{item.TransferId}:{item.Stack}")),
                string.Join(',', state.CompletedTransferIds));
            if (this.LastHostStateSignatures.TryGetValue(state.WorkerName, out string? prior)
                && string.Equals(signature, prior, StringComparison.Ordinal))
                continue;

            this.LastHostStateSignatures[state.WorkerName] = signature;
            ContractSnapshotMessage snapshot = new()
            {
                SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
                SaveId = Game1.uniqueIDForThisGame,
                HostSessionId = this.HostSessionId,
                ContractId = state.ContractId,
                Sequence = this.NextHostSequence(state.ContractId),
                StateVersion = ++this.HostStateVersion,
                RequestId = state.RequestId,
                RequestingPlayerId = state.RequestingPlayerId,
                WorkerName = state.WorkerName,
                Task = state.Task,
                HarvestDestination = state.HarvestDestination,
                EfficiencyMultiplier = state.EfficiencyMultiplier,
                Phase = state.Phase,
                ArrivalX = state.ArrivalX,
                ArrivalY = state.ArrivalY,
                ArrivalSide = state.ArrivalSide,
                EntranceSwitches = state.EntranceSwitches,
                TargetX = state.TargetX,
                TargetY = state.TargetY,
                ReservedGold = state.ReservedGold,
                StartTime = state.StartTime,
                CompletedWork = state.CompletedWork,
                CargoCount = state.CargoCount,
                Cargo = state.Cargo.Select(item => new ContractCargoSnapshotMessage
                {
                    TransferId = item.TransferId,
                    QualifiedItemId = item.QualifiedItemId,
                    DisplayName = item.DisplayName,
                    Quality = item.Quality,
                    Stack = item.Stack
                }).ToArray(),
                CompletedTransferIds = state.CompletedTransferIds.ToArray()
            };
            this.CurrentHostSnapshots[state.WorkerName] = snapshot;
            if (Context.IsMultiplayer)
                this.BroadcastMessage(snapshot, MultiplayerContractProtocol.SnapshotType);
        }
    }

    private void PublishHostCompletions()
    {
        NamedContractCompletionState? farmWork = this.FarmWorkContracts.ConsumeCompletion();
        foreach (NamedContractCompletionState completion in new[]
                 {
                     farmWork
                 }.OfType<NamedContractCompletionState>())
        {
            ContractResultMessage result = new()
            {
                SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
                SaveId = Game1.uniqueIDForThisGame,
                HostSessionId = this.HostSessionId,
                ContractId = completion.ContractId,
                Sequence = this.NextHostSequence(completion.ContractId),
                StateVersion = ++this.HostStateVersion,
                RequestId = completion.RequestId,
                RequestingPlayerId = completion.RequestingPlayerId,
                WorkerName = completion.WorkerName,
                WorkerNames = completion.WorkerSettlements.Count > 0
                    ? completion.WorkerSettlements.Select(item => item.WorkerName).ToArray()
                    : new[] { completion.WorkerName },
                Task = completion.Task,
                HarvestDestination = completion.HarvestDestination,
                Succeeded = completion.Succeeded,
                ReasonKey = completion.ReasonKey,
                CompletedWork = completion.CompletedWork,
                PlayerItems = completion.PlayerItems,
                ChestItems = completion.ChestItems,
                OverflowItems = completion.OverflowItems,
                QuarantinedItems = completion.QuarantinedItems,
                DroppedItems = completion.DroppedItems,
                BillableHours = completion.BillableHours,
                ChargedGold = completion.ChargedGold,
                RefundedGold = completion.RefundedGold,
                ProducedItems = completion.ProducedItems.Select(item => new ContractCargoSnapshotMessage
                {
                    TransferId = item.TransferId,
                    QualifiedItemId = item.QualifiedItemId,
                    DisplayName = item.DisplayName,
                    Quality = item.Quality,
                    Stack = item.Stack
                }).ToArray(),
                CompletedTransferIds = completion.CompletedTransferIds.ToArray(),
                CompletedTransfers = completion.CompletedTransfers.Select(ToTransferMessage).ToArray(),
                SkippedTransfers = completion.SkippedTransfers.Select(ToTransferMessage).ToArray(),
                WorkerSettlements = completion.WorkerSettlements.Select(item =>
                    new ContractWorkerSettlementMessage
                    {
                        WorkerName = item.WorkerName,
                        Succeeded = item.Succeeded,
                        ReasonKey = item.ReasonKey,
                        CompletedWork = item.CompletedWork,
                        PlayerItems = item.PlayerItems,
                        ChestItems = item.ChestItems,
                        OverflowItems = item.OverflowItems,
                        QuarantinedItems = item.QuarantinedItems,
                        DroppedItems = item.DroppedItems,
                        BillableHours = item.BillableHours,
                        ChargedGold = item.ChargedGold,
                        RefundedGold = item.RefundedGold
                    }).ToArray()
            };
            this.RecentResults[completion.RequestingPlayerId] = result;
            foreach (string worker in this.CurrentHostSnapshots
                         .Where(pair => pair.Value.ContractId == completion.ContractId)
                         .Select(pair => pair.Key).ToArray())
            {
                this.CurrentHostSnapshots.Remove(worker);
                this.LastHostStateSignatures.Remove(worker);
            }
            if (Context.IsMultiplayer)
                this.BroadcastMessage(result, MultiplayerContractProtocol.ResultType);
        }
    }

    private static ContractTransferReportMessage ToTransferMessage(
        NamedContractTransferState transfer)
    {
        return new ContractTransferReportMessage
        {
            Sequence = transfer.Sequence,
            QualifiedItemId = transfer.QualifiedItemId,
            DisplayName = transfer.DisplayName,
            Category = transfer.Category,
            Quality = transfer.Quality,
            Quantity = transfer.Quantity,
            SourceX = transfer.SourceX,
            SourceY = transfer.SourceY,
            DestinationX = transfer.DestinationX,
            DestinationY = transfer.DestinationY
        };
    }

    private void HandleStartResponse(ContractStartResponseMessage response)
    {
        if (response.SchemaVersion != MultiplayerContractProtocol.SchemaVersion
            || response.SaveId != Game1.uniqueIDForThisGame
            || response.RequestingPlayerId != Game1.player.UniqueMultiplayerID
            || response.HostOrder <= 0
            || !Guid.TryParseExact(response.RequestId, "N", out _)
            || (response.Accepted
                ? !Guid.TryParseExact(response.ContractId, "N", out _)
                    || !string.IsNullOrWhiteSpace(response.ReasonKey)
                : !string.IsNullOrWhiteSpace(response.ContractId)
                    || string.IsNullOrWhiteSpace(response.ReasonKey)))
            return;

        bool matchesPendingRequest = this.PendingRequest?.RequestId == response.RequestId;
        if (!matchesPendingRequest || !this.ClientHostSession.Matches(response.HostSessionId))
            return;

        string responseKey = $"{response.RequestingPlayerId}:{response.RequestId}";
        if (!this.SeenClientResponses.Add(responseKey))
            return;
        this.ClientResponseOrder.Enqueue(responseKey);
        while (this.SeenClientResponses.Count > ClientResponseHistoryCapacity)
            this.SeenClientResponses.Remove(this.ClientResponseOrder.Dequeue());

        this.PendingRequest = null;
        this.PendingTicks = 0;

        if (response.Accepted)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("multiplayer.hud.accepted"),
                HUDMessage.newQuest_type));
        }
        else
        {
            string reason = this.Translation.Get(NormalizeFailureKey(response.ReasonKey));
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("multiplayer.hud.rejected", new { reason }),
                HUDMessage.error_type));
        }
    }

    private void HandleSnapshot(ContractSnapshotMessage snapshot)
    {
        if (!Enum.IsDefined(snapshot.Task)
            || !HarvestDestinationPolicy.IsValidForTask(
                snapshot.Task,
                snapshot.HarvestDestination)
            || !Enum.IsDefined(snapshot.ArrivalSide)
            || !WorkerEfficiencyProfiles.IsValidMultiplier(snapshot.EfficiencyMultiplier)
            || !this.ClientHostSession.Matches(snapshot.HostSessionId)
            || !this.RemoteStateVersions.CanAccept(snapshot.StateVersion)
            || !this.SnapshotTracker.TryAccept(
                snapshot,
                MultiplayerContractProtocol.SchemaVersion,
                Game1.uniqueIDForThisGame))
            return;

        this.RemoteStateVersions.Commit(snapshot.StateVersion);

        if (!snapshot.IsActive)
        {
            this.RemoteActiveSnapshots.Remove(snapshot.WorkerName);
            return;
        }

        this.RemoteActiveSnapshots.TryGetValue(snapshot.WorkerName, out ContractSnapshotMessage? previous);
        this.RemoteActiveSnapshots[snapshot.WorkerName] = snapshot;
        if (this.PendingRequest?.RequestId == snapshot.RequestId)
        {
            this.PendingRequest = null;
            this.PendingTicks = 0;
        }
        bool newContract = previous?.ContractId != snapshot.ContractId;
        bool entranceChanged = previous?.ContractId == snapshot.ContractId
            && (previous.ArrivalX != snapshot.ArrivalX
                || previous.ArrivalY != snapshot.ArrivalY
                || previous.ArrivalSide != snapshot.ArrivalSide
                || previous.EntranceSwitches != snapshot.EntranceSwitches);
        bool newAction = IsActionPhase(snapshot)
            && (previous?.ContractId != snapshot.ContractId
                || previous is null
                || !IsActionPhase(previous)
                || previous.TargetX != snapshot.TargetX
                || previous.TargetY != snapshot.TargetY);
        if (newContract)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("multiplayer.hud.observing", new
                {
                    worker = snapshot.WorkerName,
                    task = GetTaskText(snapshot.Task),
                    efficiency = $"{snapshot.EfficiencyMultiplier:0.00}x",
                    entrance = this.GetEntranceText(snapshot.ArrivalSide)
                }),
                HUDMessage.newQuest_type));
        }
        else if (entranceChanged)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("multiplayer.hud.entrance-fallback", new
                {
                    worker = snapshot.WorkerName,
                    entrance = this.GetEntranceText(snapshot.ArrivalSide)
                }),
                HUDMessage.newQuest_type));
        }
        if (newAction)
            this.RenderRemoteAction(snapshot);
    }

    private void HandleResult(ContractResultMessage result)
    {
        if (!MultiplayerRecoveryState.IsValidResult(
                result,
                Game1.uniqueIDForThisGame,
                MultiplayerContractProtocol.SchemaVersion)
            || !this.ClientHostSession.Matches(result.HostSessionId)
            || !this.RemoteStateVersions.CanAccept(result.StateVersion)
            || !this.SnapshotTracker.TryAccept(
                result,
                MultiplayerContractProtocol.SchemaVersion,
                Game1.uniqueIDForThisGame))
            return;

        this.RemoteStateVersions.Commit(result.StateVersion);
        this.RecentResults[result.RequestingPlayerId] = result;

        foreach (string worker in this.RemoteActiveSnapshots
                     .Where(pair => pair.Value.ContractId == result.ContractId)
                     .Select(pair => pair.Key).ToArray())
            this.RemoteActiveSnapshots.Remove(worker);
        if (this.PendingRequest?.RequestId == result.RequestId)
        {
            this.PendingRequest = null;
            this.PendingTicks = 0;
        }
        string workerNames = GetWorkerDisplayNames(result);

        if (result.Succeeded)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("multiplayer.hud.result", new
                {
                    worker = workerNames,
                    task = GetTaskText(result.Task),
                    completed = result.CompletedWork,
                    player = result.PlayerItems,
                    chest = result.ChestItems,
                    overflow = result.OverflowItems,
                    quarantine = result.QuarantinedItems,
                    dropped = result.DroppedItems,
                    hours = result.BillableHours,
                    paid = result.ChargedGold,
                    refunded = result.RefundedGold
                }),
                HUDMessage.newQuest_type));
        }
        else
        {
            string reason = this.Translation.Get(NormalizeFailureKey(result.ReasonKey));
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("multiplayer.hud.result-stopped", new
                {
                    worker = workerNames,
                    reason,
                    player = result.PlayerItems,
                    chest = result.ChestItems,
                    overflow = result.OverflowItems,
                    quarantine = result.QuarantinedItems,
                    dropped = result.DroppedItems,
                    paid = result.ChargedGold,
                    refunded = result.RefundedGold
                }),
                HUDMessage.error_type));
        }
    }

    private void HandleSyncRequest(ContractSyncRequestMessage request, long senderPlayerId)
    {
        if (request.SchemaVersion != MultiplayerContractProtocol.SchemaVersion
            || !string.Equals(request.ModVersion, this.Manifest.Version.ToString(), StringComparison.Ordinal)
            || request.SaveId != Game1.uniqueIDForThisGame
            || request.RequestingPlayerId != senderPlayerId
            || !Guid.TryParseExact(request.SyncRequestId, "N", out _)
            || Game1.GetPlayer(senderPlayerId, onlyOnline: true) is null)
            return;

        this.SendSyncState(senderPlayerId, request.SyncRequestId);
    }

    private void HandleSyncState(ContractSyncStateMessage state)
    {
        bool validSettings = state.Settings.TryGetSnapshot(out ContractSettingsSnapshot settings)
            && state.Settings.SaveId == Game1.uniqueIDForThisGame
            && state.Settings.SettingsVersion >= this.RemoteSettingsVersion
            && string.Equals(
                state.Settings.HostSessionId,
                state.HostSessionId,
                StringComparison.Ordinal);
        ContractSnapshotMessage[] activeContracts = state.ActiveContracts.Length > 0
            ? state.ActiveContracts
            : state.ActiveContract is null
                ? Array.Empty<ContractSnapshotMessage>()
                : new[] { state.ActiveContract };
        if (state.SchemaVersion != MultiplayerContractProtocol.SchemaVersion
            || state.SaveId != Game1.uniqueIDForThisGame
            || !Guid.TryParseExact(state.HostSessionId, "N", out _)
            || state.StateVersion < 0
            || state.HasActiveContract != (activeContracts.Length > 0)
            || activeContracts.Any(snapshot => !snapshot.IsActive)
            || activeContracts.Any(snapshot => snapshot.StateVersion > state.StateVersion)
            || activeContracts.Select(snapshot => snapshot.WorkerName)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != activeContracts.Length
            || state.RecentResult?.StateVersion > state.StateVersion
            || !validSettings)
            return;

        if (!this.RemoteStateVersions.CanAccept(state.StateVersion))
            return;

        if (!this.ClientHostSession.TryEstablish(state.HostSessionId, state.SyncRequestId))
            return;
        this.SnapshotTracker.BeginSession(state.HostSessionId);
        this.RemoteHostSettings = settings;
        this.RemoteSettingsVersion = state.Settings.SettingsVersion;

        if (state.RecentResult is not null)
            this.HandleResult(state.RecentResult);

        if (state.HasActiveContract)
        {
            this.RemoteActiveSnapshots.Clear();
            foreach (ContractSnapshotMessage snapshot in activeContracts.OrderBy(item => item.StateVersion))
                this.HandleSnapshot(snapshot);
        }
        else
        {
            this.RemoteActiveSnapshots.Clear();
            this.RemoteStateVersions.Commit(state.StateVersion);
        }

        if (this.PendingRequest is not null)
        {
            this.SendMessage(
                this.PendingRequest,
                MultiplayerContractProtocol.StartRequestType,
                Game1.MasterPlayer.UniqueMultiplayerID);
        }
    }

    private void HandleSettings(ContractSettingsMessage message)
    {
        if (message.SaveId != Game1.uniqueIDForThisGame
            || !this.ClientHostSession.Matches(message.HostSessionId)
            || message.SettingsVersion < this.RemoteSettingsVersion
            || !message.TryGetSnapshot(out ContractSettingsSnapshot settings))
            return;

        this.RemoteHostSettings = settings;
        this.RemoteSettingsVersion = message.SettingsVersion;
    }

    private void SendSyncRequest()
    {
        if (!Context.IsWorldReady || Context.IsMainPlayer || !Context.IsMultiplayer)
            return;

        string syncRequestId = Guid.NewGuid().ToString("N");
        if (!this.ClientHostSession.BeginHandshake(syncRequestId))
            return;

        ContractSyncRequestMessage request = new()
        {
            SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
            ModVersion = this.Manifest.Version.ToString(),
            SaveId = Game1.uniqueIDForThisGame,
            RequestingPlayerId = Game1.player.UniqueMultiplayerID,
            SyncRequestId = syncRequestId
        };
        this.SendMessage(
            request,
            MultiplayerContractProtocol.SyncRequestType,
            Game1.MasterPlayer.UniqueMultiplayerID);
    }

    private void SendSyncState(long playerId, string syncRequestId)
    {
        ContractSnapshotMessage[] activeContracts = this.CurrentHostSnapshots.Values
            .OrderBy(snapshot => snapshot.WorkerName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ContractSyncStateMessage state = new()
        {
            SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
            SaveId = Game1.uniqueIDForThisGame,
            HostSessionId = this.HostSessionId,
            SyncRequestId = syncRequestId,
            StateVersion = this.HostStateVersion,
            HasActiveContract = activeContracts.Length > 0,
            ActiveContract = activeContracts.FirstOrDefault(),
            ActiveContracts = activeContracts,
            RecentResult = this.RecentResults.GetValueOrDefault(playerId),
            Settings = this.CreateSettingsMessage()
        };
        this.SendMessage(state, MultiplayerContractProtocol.SyncStateType, playerId);
    }

    private ContractSettingsMessage CreateSettingsMessage()
    {
        ContractSettingsSnapshot settings = this.GetHostContractSettings();
        return new ContractSettingsMessage
        {
            SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
            SaveId = Game1.uniqueIDForThisGame,
            HostSessionId = this.HostSessionId,
            SettingsVersion = this.HostSettingsVersion,
            BaseHourlyWage = settings.BaseHourlyWage,
            FriendshipWageImpactPercent = settings.FriendshipWageImpactPercent,
            RestDayMultiplier = settings.RestDayMultiplier,
            DefaultHarvestDestination = settings.DefaultHarvestDestination,
            EnabledStages = settings.EnabledStages,
            MaximumConcurrentWorkers = settings.MaximumConcurrentWorkers
        };
    }

    private void RenderRemoteAction(ContractSnapshotMessage snapshot)
    {
        Farm farm = Game1.getFarm();
        if (!ReferenceEquals(Game1.currentLocation, farm))
            return;

        Vector2 tile = new(snapshot.TargetX, snapshot.TargetY);
        bool farmWorkWatering = snapshot.Task == NamedFarmTask.FarmWork
            && snapshot.Phase.StartsWith("Watering/", StringComparison.Ordinal);
        bool farmWorkHarvesting = snapshot.Task == NamedFarmTask.FarmWork
            && snapshot.Phase.StartsWith("Harvesting/", StringComparison.Ordinal);
        bool farmWorkSorting = snapshot.Task == NamedFarmTask.FarmWork
            && snapshot.Phase.StartsWith("StorageSorting/", StringComparison.Ordinal);
        if (snapshot.Task == NamedFarmTask.Watering || farmWorkWatering)
        {
            farm.playSound("wateringCan", tile);
            farm.temporarySprites.Add(new TemporaryAnimatedSprite(
                13,
                tile * Game1.tileSize,
                Color.White,
                10,
                Game1.random.Next(2) == 0,
                70f,
                0,
                64,
                (tile.Y * Game1.tileSize + 32f) / 10000f - 0.01f));
        }
        else if (snapshot.Task == NamedFarmTask.Harvesting || farmWorkHarvesting)
        {
            farm.playSound("harvest", tile);
            farm.temporarySprites.Add(new TemporaryAnimatedSprite(
                17,
                tile * Game1.tileSize,
                Color.White,
                7,
                Game1.random.Next(2) == 0,
                125f));
        }
        else if (snapshot.Task == NamedFarmTask.StorageSorting || farmWorkSorting)
        {
            farm.playSound("openChest", tile);
        }
    }

    private static bool IsActionPhase(ContractSnapshotMessage snapshot)
    {
        return snapshot.Task == NamedFarmTask.StorageSorting
            ? string.Equals(snapshot.Phase, "ActingAtDestination", StringComparison.Ordinal)
            : snapshot.Task == NamedFarmTask.FarmWork
                ? snapshot.Phase.EndsWith("/Acting", StringComparison.Ordinal)
                    || snapshot.Phase.EndsWith("/ActingAtDestination", StringComparison.Ordinal)
                : string.Equals(snapshot.Phase, "Acting", StringComparison.Ordinal);
    }

    private IReadOnlyList<NamedContractRuntimeState> GetHostRuntimeStates()
    {
        return this.FarmWorkContracts.GetRuntimeStates();
    }

    private long NextHostSequence(string contractId)
    {
        if (!this.HostSequences.TryGetValue(contractId, out long sequence))
        {
            sequence = 0;
            this.HostSequenceOrder.Enqueue(contractId);
        }

        sequence++;
        this.HostSequences[contractId] = sequence;
        while (this.HostSequences.Count > MultiplayerContractProtocol.ProcessedRequestCapacity)
        {
            string oldest = this.HostSequenceOrder.Dequeue();
            this.HostSequences.Remove(oldest);
        }
        return sequence;
    }

    private void BroadcastMessage<TMessage>(TMessage message, string type)
    {
        this.Helper.Multiplayer.SendMessage(
            message,
            type,
            modIDs: new[] { this.Manifest.UniqueID });
    }

    private void SendMessage<TMessage>(TMessage message, string type, long playerId)
    {
        this.Helper.Multiplayer.SendMessage(
            message,
            type,
            modIDs: new[] { this.Manifest.UniqueID },
            playerIDs: new[] { playerId });
    }

    private void ResetSessionState()
    {
        this.HostSessionId = "";
        this.ClientHostSession.Clear();
        this.HostOrder = 0;
        this.HostStateVersion = 0;
        this.PendingRequest = null;
        this.PendingTicks = 0;
        this.CurrentHostSnapshots.Clear();
        this.RemoteActiveSnapshots.Clear();
        this.LastHostStateSignatures.Clear();
        this.ProcessedRequests.Clear();
        this.SnapshotTracker.Clear();
        this.RemoteStateVersions.Clear();
        this.RecentResults.Clear();
        this.HostSequences.Clear();
        this.HostSequenceOrder.Clear();
        this.SeenClientResponses.Clear();
        this.ClientResponseOrder.Clear();
        this.RecoveryStateHealthy = true;
        this.RemoteHostSettings = ContractSettingsSnapshot.Default;
        this.HostSettingsVersion = 0;
        this.RemoteSettingsVersion = 0;
    }

    private void LoadRecoveryState()
    {
        MultiplayerRecoverySaveData? state;
        try
        {
            state = this.Helper.Data.ReadSaveData<MultiplayerRecoverySaveData>(
                MultiplayerRecoveryState.SaveDataKey);
        }
        catch (Exception ex)
        {
            this.MarkRecoveryStateInvalid($"Could not read multiplayer recovery state: {ex}");
            return;
        }

        if (state is null)
            return;

        if (!MultiplayerRecoveryState.IsValid(
                state,
                Game1.uniqueIDForThisGame))
        {
            this.MarkRecoveryStateInvalid(
                "Multiplayer recovery state failed schema, save, or transaction validation.");
            return;
        }

        foreach (ContractStartResponseMessage response in state.ProcessedRequests)
        {
            MultiplayerRecoveryState.RebindResponse(
                response,
                this.HostSessionId,
                Game1.uniqueIDForThisGame);
            this.ProcessedRequests.Record(response);
            this.HostOrder = Math.Max(this.HostOrder, response.HostOrder);
        }

        foreach (ContractResultMessage result in state.RecentResults
                     .OrderBy(result => result.RequestingPlayerId))
        {
            MultiplayerRecoveryState.RebindResult(
                result,
                this.HostSessionId,
                Game1.uniqueIDForThisGame,
                this.NextHostSequence(result.ContractId),
                ++this.HostStateVersion);
            this.RecentResults[result.RequestingPlayerId] = result;
        }

        this.Monitor.Log(
            $"Restored {state.ProcessedRequests.Length} processed contract request(s) and "
            + $"{state.RecentResults.Length} recent result(s) into host session {this.HostSessionId}.",
            LogLevel.Info);
    }

    private void MarkRecoveryStateInvalid(string message)
    {
        this.RecoveryStateHealthy = false;
        this.Monitor.Log(
            $"{message} New contract requests are disabled to prevent duplicate charges or work.",
            LogLevel.Error);
        Game1.addHUDMessage(new HUDMessage(
            this.Translation.Get("multiplayer.hud.recovery-invalid"),
            HUDMessage.error_type));
    }

    private string GetTaskText(NamedFarmTask task)
    {
        return this.Translation.Get(task switch
        {
            NamedFarmTask.FarmWork => "contract.task.farm-work",
            NamedFarmTask.Watering => "contract.task.watering",
            NamedFarmTask.Harvesting => "contract.task.harvesting",
            NamedFarmTask.StorageSorting => "contract.task.storage-sorting",
            _ => "contract.task.harvesting"
        });
    }

    private string GetEntranceText(FarmBoundarySide side)
    {
        return this.Translation.Get($"contract.entrance.{side.ToString().ToLowerInvariant()}");
    }

    private static string GetWorkerDisplayNames(ContractResultMessage result)
    {
        IReadOnlyList<string> names = result.WorkerNames.Length > 0
            ? result.WorkerNames
            : new[] { result.WorkerName };
        return string.Join(", ", names.Select(name =>
            Game1.getCharacterFromName(name)?.displayName ?? name));
    }

    private static string NormalizeFailureKey(string? failureKey)
    {
        return failureKey switch
        {
            "contract.start.insufficient-funds" => "multiplayer.reject.funds",
            "contract.start.worker-unavailable" => "multiplayer.reject.worker-state",
            null or "" => "contract.failure.unknown",
            _ => failureKey
        };
    }

    private static bool CanTrackRequest(ContractStartRequestMessage request, long senderPlayerId)
    {
        return request.RequestingPlayerId == senderPlayerId
            && senderPlayerId > 0
            && Guid.TryParseExact(request.RequestId, "N", out _);
    }
}
