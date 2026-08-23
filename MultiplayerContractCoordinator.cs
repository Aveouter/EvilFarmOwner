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
    private readonly ProcessedContractRequestLedger ProcessedRequests = new();
    private readonly ContractSnapshotTracker SnapshotTracker = new();
    private readonly HostStateVersionTracker RemoteStateVersions = new();
    private readonly Dictionary<long, ContractResultMessage> RecentResults = new();
    private readonly Dictionary<string, long> HostSequences = new(StringComparer.Ordinal);
    private readonly Queue<string> HostSequenceOrder = new();
    private readonly HashSet<string> SeenClientResponses = new(StringComparer.Ordinal);
    private readonly Queue<string> ClientResponseOrder = new();

    private string HostSessionId = "";
    private string ExpectedHostSessionId = "";
    private long HostOrder;
    private long HostStateVersion;
    private ContractStartRequestMessage? PendingRequest;
    private int PendingTicks;
    private ContractSnapshotMessage? CurrentHostSnapshot;
    private ContractSnapshotMessage? RemoteActiveSnapshot;
    private string LastHostStateSignature = "";

    public MultiplayerContractCoordinator(
        IModHelper helper,
        IManifest manifest,
        ITranslationHelper translation,
        IMonitor monitor,
        WateringContractExecutionController wateringContracts,
        HarvestingContractExecutionController harvestingContracts)
    {
        this.Helper = helper;
        this.Manifest = manifest;
        this.Translation = translation;
        this.Monitor = monitor;
        this.WateringContracts = wateringContracts;
        this.HarvestingContracts = harvestingContracts;
    }

    public bool HasPendingRequest => this.PendingRequest is not null;

    public bool HasObservedActiveContract => this.CurrentHostSnapshot is not null
        || this.RemoteActiveSnapshot is not null;

    public string GetDiagnosticStatus()
    {
        string role = Context.IsMainPlayer ? "host" : "farmhand";
        string session = Context.IsMainPlayer ? this.HostSessionId : this.ExpectedHostSessionId;
        string active = this.GetHostRuntimeState()?.ContractId
            ?? this.RemoteActiveSnapshot?.ContractId
            ?? "none";
        string pending = this.PendingRequest?.RequestId ?? "none";
        return $"EFO network: role={role}, session={session}, active={active}, pending={pending}, processed={this.ProcessedRequests.Count}, stateVersion={(Context.IsMainPlayer ? this.HostStateVersion : this.RemoteStateVersions.Latest)}";
    }

    public bool RequestStart(string workerInternalName, NamedFarmTask task)
    {
        if (!Context.IsWorldReady)
            return false;

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
            RequestId = Guid.NewGuid().ToString("N"),
            RequestingPlayerId = Game1.player.UniqueMultiplayerID,
            WorkerName = workerInternalName,
            Task = task
        };

        if (Context.IsMainPlayer)
        {
            ContractStartResponseMessage response = this.ProcessHostRequest(
                request,
                Game1.player.UniqueMultiplayerID);
            return response.Accepted;
        }

        this.PendingRequest = request;
        this.PendingTicks = 0;
        this.SendMessage(
            request,
            MultiplayerContractProtocol.StartRequestType,
            Game1.MasterPlayer.UniqueMultiplayerID);
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
        }
        else if (Context.IsMultiplayer)
        {
            this.SendSyncRequest();
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

        if (Context.IsMainPlayer)
        {
            this.SendSyncState(e.Peer.PlayerID);
            foreach (ContractStartResponseMessage response in this.ProcessedRequests.GetForPlayer(e.Peer.PlayerID))
            {
                this.SendMessage(
                    response,
                    MultiplayerContractProtocol.StartResponseType,
                    e.Peer.PlayerID);
            }
        }
        else if (e.Peer.IsHost)
        {
            this.ExpectedHostSessionId = "";
            this.SnapshotTracker.Clear();
            this.RemoteStateVersions.Clear();
            this.RemoteActiveSnapshot = null;
            this.SeenClientResponses.Clear();
            this.ClientResponseOrder.Clear();
            this.SendSyncRequest();
            if (this.PendingRequest is not null)
            {
                this.SendMessage(
                    this.PendingRequest,
                    MultiplayerContractProtocol.StartRequestType,
                    e.Peer.PlayerID);
            }
        }
    }

    public void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
    {
        if (Context.IsMainPlayer)
        {
            NamedContractRuntimeState? active = this.GetHostRuntimeState();
            if (active?.RequestingPlayerId == e.Peer.PlayerID)
            {
                this.Monitor.Log(
                    $"Contract requester {e.Peer.PlayerID} disconnected; host will keep authority and complete safe delivery/return.",
                    LogLevel.Info);
            }
        }
        else if (e.Peer.IsHost)
        {
            this.ExpectedHostSessionId = "";
            this.SnapshotTracker.Clear();
            this.RemoteStateVersions.Clear();
            this.RemoteActiveSnapshot = null;
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
                .ToHashSet());
        ContractRequestValidationFailure validation = ContractRequestValidator.Validate(
            request,
            senderPlayerId,
            protocolContext);
        if (validation != ContractRequestValidationFailure.None)
        {
            response.Accepted = false;
            response.ReasonKey = ContractRequestValidator.GetReasonKey(validation);
            this.ProcessedRequests.Record(response);
            this.Monitor.Log(
                $"Rejected contract request {request.RequestId} from player {senderPlayerId}: {validation}.",
                LogLevel.Warn);
            return response;
        }

        if (this.WateringContracts.HasActiveContract || this.HarvestingContracts.HasActiveContract)
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
                    request.RequestId);
                failureKey = this.HarvestingContracts.LastStartFailureKey;
                contractId = this.HarvestingContracts.ActiveContractId;
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
        NamedContractRuntimeState? state = this.GetHostRuntimeState();
        if (state is null)
        {
            this.CurrentHostSnapshot = null;
            this.LastHostStateSignature = "";
            return;
        }

        string signature = string.Join(
            '|',
            state.ContractId,
            state.Phase,
            state.TargetX,
            state.TargetY,
            state.CompletedWork,
            state.CargoCount,
            string.Join(',', state.Cargo.Select(item => $"{item.TransferId}:{item.Stack}")),
            string.Join(',', state.CompletedTransferIds));
        if (string.Equals(signature, this.LastHostStateSignature, StringComparison.Ordinal))
            return;

        this.LastHostStateSignature = signature;
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
            Phase = state.Phase,
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
        this.CurrentHostSnapshot = snapshot;
        if (Context.IsMultiplayer)
            this.BroadcastMessage(snapshot, MultiplayerContractProtocol.SnapshotType);
    }

    private void PublishHostCompletions()
    {
        NamedContractCompletionState? watering = this.WateringContracts.ConsumeCompletion();
        NamedContractCompletionState? harvesting = this.HarvestingContracts.ConsumeCompletion();
        foreach (NamedContractCompletionState completion in new[] { watering, harvesting }.OfType<NamedContractCompletionState>())
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
                Task = completion.Task,
                Succeeded = completion.Succeeded,
                ReasonKey = completion.ReasonKey,
                CompletedWork = completion.CompletedWork,
                ChestItems = completion.ChestItems,
                OverflowItems = completion.OverflowItems,
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
                CompletedTransferIds = completion.CompletedTransferIds.ToArray()
            };
            this.RecentResults[completion.RequestingPlayerId] = result;
            if (this.CurrentHostSnapshot?.ContractId == completion.ContractId)
                this.CurrentHostSnapshot = null;
            if (Context.IsMultiplayer)
                this.BroadcastMessage(result, MultiplayerContractProtocol.ResultType);
        }
    }

    private void HandleStartResponse(ContractStartResponseMessage response)
    {
        if (response.SchemaVersion != MultiplayerContractProtocol.SchemaVersion
            || response.SaveId != Game1.uniqueIDForThisGame
            || response.RequestingPlayerId != Game1.player.UniqueMultiplayerID
            || string.IsNullOrWhiteSpace(response.RequestId)
            || string.IsNullOrWhiteSpace(response.HostSessionId))
            return;

        if (!this.TryAcceptHostSession(response.HostSessionId))
            return;

        string responseKey = $"{response.RequestingPlayerId}:{response.RequestId}";
        if (!this.SeenClientResponses.Add(responseKey))
            return;
        this.ClientResponseOrder.Enqueue(responseKey);
        while (this.SeenClientResponses.Count > ClientResponseHistoryCapacity)
            this.SeenClientResponses.Remove(this.ClientResponseOrder.Dequeue());

        if (this.PendingRequest?.RequestId == response.RequestId)
        {
            this.PendingRequest = null;
            this.PendingTicks = 0;
        }

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
        if (!this.TryAcceptHostSession(snapshot.HostSessionId)
            || !this.RemoteStateVersions.CanAccept(snapshot.StateVersion)
            || !this.SnapshotTracker.TryAccept(
                snapshot,
                MultiplayerContractProtocol.SchemaVersion,
                Game1.uniqueIDForThisGame)
            || !Enum.IsDefined(snapshot.Task))
            return;

        this.RemoteStateVersions.Commit(snapshot.StateVersion);

        ContractSnapshotMessage? previous = this.RemoteActiveSnapshot;
        this.RemoteActiveSnapshot = snapshot;
        if (this.PendingRequest?.RequestId == snapshot.RequestId)
        {
            this.PendingRequest = null;
            this.PendingTicks = 0;
        }
        bool newContract = previous?.ContractId != snapshot.ContractId;
        bool newAction = string.Equals(snapshot.Phase, "Acting", StringComparison.Ordinal)
            && (previous?.ContractId != snapshot.ContractId
                || !string.Equals(previous.Phase, snapshot.Phase, StringComparison.Ordinal)
                || previous.TargetX != snapshot.TargetX
                || previous.TargetY != snapshot.TargetY);
        if (newContract)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("multiplayer.hud.observing", new
                {
                    worker = snapshot.WorkerName,
                    task = GetTaskText(snapshot.Task)
                }),
                HUDMessage.newQuest_type));
        }
        if (newAction)
            this.RenderRemoteAction(snapshot);
    }

    private void HandleResult(ContractResultMessage result)
    {
        if (!this.TryAcceptHostSession(result.HostSessionId)
            || !this.RemoteStateVersions.CanAccept(result.StateVersion)
            || !this.SnapshotTracker.TryAccept(
                result,
                MultiplayerContractProtocol.SchemaVersion,
                Game1.uniqueIDForThisGame)
            || !Enum.IsDefined(result.Task))
            return;

        this.RemoteStateVersions.Commit(result.StateVersion);

        if (this.RemoteActiveSnapshot?.ContractId == result.ContractId)
            this.RemoteActiveSnapshot = null;
        if (this.PendingRequest?.RequestId == result.RequestId)
        {
            this.PendingRequest = null;
            this.PendingTicks = 0;
        }

        if (result.Succeeded)
        {
            Game1.addHUDMessage(new HUDMessage(
                this.Translation.Get("multiplayer.hud.result", new
                {
                    worker = result.WorkerName,
                    task = GetTaskText(result.Task),
                    completed = result.CompletedWork,
                    chest = result.ChestItems,
                    overflow = result.OverflowItems,
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
                    worker = result.WorkerName,
                    reason,
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
            || Game1.GetPlayer(senderPlayerId, onlyOnline: true) is null)
            return;

        this.SendSyncState(senderPlayerId);
        foreach (ContractStartResponseMessage response in this.ProcessedRequests.GetForPlayer(senderPlayerId))
        {
            this.SendMessage(
                response,
                MultiplayerContractProtocol.StartResponseType,
                senderPlayerId);
        }
    }

    private void HandleSyncState(ContractSyncStateMessage state)
    {
        if (state.SchemaVersion != MultiplayerContractProtocol.SchemaVersion
            || state.SaveId != Game1.uniqueIDForThisGame
            || string.IsNullOrWhiteSpace(state.HostSessionId))
            return;

        if (!this.TryAcceptHostSession(state.HostSessionId))
            return;

        if (!this.RemoteStateVersions.CanAccept(state.StateVersion))
            return;

        if (state.RecentResult is not null)
            this.HandleResult(state.RecentResult);

        if (state.HasActiveContract && state.ActiveContract is not null)
        {
            this.HandleSnapshot(state.ActiveContract);
        }
        else
        {
            this.RemoteActiveSnapshot = null;
            this.RemoteStateVersions.Commit(state.StateVersion);
        }
    }

    private void SendSyncRequest()
    {
        if (!Context.IsWorldReady || Context.IsMainPlayer || !Context.IsMultiplayer)
            return;

        ContractSyncRequestMessage request = new()
        {
            SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
            ModVersion = this.Manifest.Version.ToString(),
            SaveId = Game1.uniqueIDForThisGame,
            RequestingPlayerId = Game1.player.UniqueMultiplayerID
        };
        this.SendMessage(
            request,
            MultiplayerContractProtocol.SyncRequestType,
            Game1.MasterPlayer.UniqueMultiplayerID);
    }

    private void SendSyncState(long playerId)
    {
        ContractSyncStateMessage state = new()
        {
            SchemaVersion = MultiplayerContractProtocol.SchemaVersion,
            SaveId = Game1.uniqueIDForThisGame,
            HostSessionId = this.HostSessionId,
            StateVersion = this.HostStateVersion,
            HasActiveContract = this.CurrentHostSnapshot is not null,
            ActiveContract = this.CurrentHostSnapshot,
            RecentResult = this.RecentResults.GetValueOrDefault(playerId)
        };
        this.SendMessage(state, MultiplayerContractProtocol.SyncStateType, playerId);
    }

    private void RenderRemoteAction(ContractSnapshotMessage snapshot)
    {
        Farm farm = Game1.getFarm();
        if (!ReferenceEquals(Game1.currentLocation, farm))
            return;

        Vector2 tile = new(snapshot.TargetX, snapshot.TargetY);
        if (snapshot.Task == NamedFarmTask.Watering)
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
        else
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
    }

    private NamedContractRuntimeState? GetHostRuntimeState()
    {
        return this.WateringContracts.GetRuntimeState()
            ?? this.HarvestingContracts.GetRuntimeState();
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
        this.ExpectedHostSessionId = "";
        this.HostOrder = 0;
        this.HostStateVersion = 0;
        this.PendingRequest = null;
        this.PendingTicks = 0;
        this.CurrentHostSnapshot = null;
        this.RemoteActiveSnapshot = null;
        this.LastHostStateSignature = "";
        this.ProcessedRequests.Clear();
        this.SnapshotTracker.Clear();
        this.RemoteStateVersions.Clear();
        this.RecentResults.Clear();
        this.HostSequences.Clear();
        this.HostSequenceOrder.Clear();
        this.SeenClientResponses.Clear();
        this.ClientResponseOrder.Clear();
    }

    private string GetTaskText(NamedFarmTask task)
    {
        return this.Translation.Get(task == NamedFarmTask.Watering
            ? "contract.task.watering"
            : "contract.task.harvesting");
    }

    private bool TryAcceptHostSession(string hostSessionId)
    {
        if (string.IsNullOrWhiteSpace(hostSessionId))
            return false;
        if (string.IsNullOrWhiteSpace(this.ExpectedHostSessionId))
        {
            this.ExpectedHostSessionId = hostSessionId;
            this.SnapshotTracker.BeginSession(hostSessionId);
            return true;
        }

        return string.Equals(this.ExpectedHostSessionId, hostSessionId, StringComparison.Ordinal);
    }

    private static string NormalizeFailureKey(string? failureKey)
    {
        return failureKey switch
        {
            "contract.start.insufficient-funds" => "multiplayer.reject.funds",
            "contract.start.worker-unavailable" => "multiplayer.reject.worker-state",
            "contract.hud.restore-failed" => "multiplayer.reject.restore",
            null or "" => "contract.failure.unknown",
            _ => failureKey
        };
    }
}
