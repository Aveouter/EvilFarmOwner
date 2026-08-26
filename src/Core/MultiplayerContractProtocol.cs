namespace EvilFarmOwner;

internal static class MultiplayerContractProtocol
{
    public const int SchemaVersion = 11;
    public const int SingleWorkerSchemaVersion = 10;
    public const int ProcessedRequestCapacity = 256;
    public const string StartRequestType = "Contract/StartRequest";
    public const string StartResponseType = "Contract/StartResponse";
    public const string SnapshotType = "Contract/Snapshot";
    public const string ResultType = "Contract/Result";
    public const string SyncRequestType = "Contract/SyncRequest";
    public const string SyncStateType = "Contract/SyncState";
    public const string SettingsType = "Contract/Settings";
}

internal sealed class ContractStartRequestMessage
{
    public int SchemaVersion { get; set; }
    public string ModVersion { get; set; } = "";
    public ulong SaveId { get; set; }
    public int TotalDays { get; set; }
    public string RequestId { get; set; } = "";
    public long RequestingPlayerId { get; set; }
    public string WorkerName { get; set; } = "";
    public string[] WorkerNames { get; set; } = Array.Empty<string>();
    public NamedFarmTask Task { get; set; }
    public HarvestDestinationMode HarvestDestination { get; set; }

    public IReadOnlyList<string> GetWorkerNames()
    {
        if (this.WorkerNames is { Length: > 0 })
            return this.WorkerNames;
        return string.IsNullOrWhiteSpace(this.WorkerName)
            ? Array.Empty<string>()
            : new[] { this.WorkerName };
    }
}

internal sealed class ContractStartResponseMessage
{
    public int SchemaVersion { get; set; }
    public ulong SaveId { get; set; }
    public string HostSessionId { get; set; } = "";
    public long HostOrder { get; set; }
    public string RequestId { get; set; } = "";
    public long RequestingPlayerId { get; set; }
    public bool Accepted { get; set; }
    public string ContractId { get; set; } = "";
    public string ReasonKey { get; set; } = "";
}

internal sealed class ContractSnapshotMessage
{
    public int SchemaVersion { get; set; }
    public ulong SaveId { get; set; }
    public string HostSessionId { get; set; } = "";
    public string ContractId { get; set; } = "";
    public long Sequence { get; set; }
    public long StateVersion { get; set; }
    public string RequestId { get; set; } = "";
    public long RequestingPlayerId { get; set; }
    public string WorkerName { get; set; } = "";
    public NamedFarmTask Task { get; set; }
    public HarvestDestinationMode HarvestDestination { get; set; }
    public decimal EfficiencyMultiplier { get; set; }
    public string Phase { get; set; } = "";
    public int ArrivalX { get; set; }
    public int ArrivalY { get; set; }
    public FarmBoundarySide ArrivalSide { get; set; }
    public int EntranceSwitches { get; set; }
    public int TargetX { get; set; }
    public int TargetY { get; set; }
    public int ReservedGold { get; set; }
    public int StartTime { get; set; }
    public int CompletedWork { get; set; }
    public int CargoCount { get; set; }
    public ContractCargoSnapshotMessage[] Cargo { get; set; } = Array.Empty<ContractCargoSnapshotMessage>();
    public string[] CompletedTransferIds { get; set; } = Array.Empty<string>();
}

internal sealed class ContractCargoSnapshotMessage
{
    public string TransferId { get; set; } = "";
    public string QualifiedItemId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Quality { get; set; }
    public int Stack { get; set; }
}

internal sealed class ContractResultMessage
{
    public int SchemaVersion { get; set; }
    public ulong SaveId { get; set; }
    public string HostSessionId { get; set; } = "";
    public string ContractId { get; set; } = "";
    public long Sequence { get; set; }
    public long StateVersion { get; set; }
    public string RequestId { get; set; } = "";
    public long RequestingPlayerId { get; set; }
    public string WorkerName { get; set; } = "";
    public NamedFarmTask Task { get; set; }
    public HarvestDestinationMode HarvestDestination { get; set; }
    public bool Succeeded { get; set; }
    public string ReasonKey { get; set; } = "";
    public int CompletedWork { get; set; }
    public int PlayerItems { get; set; }
    public int ChestItems { get; set; }
    public int OverflowItems { get; set; }
    public int QuarantinedItems { get; set; }
    public int DroppedItems { get; set; }
    public int BillableHours { get; set; }
    public int ChargedGold { get; set; }
    public int RefundedGold { get; set; }
    public ContractCargoSnapshotMessage[] ProducedItems { get; set; } = Array.Empty<ContractCargoSnapshotMessage>();
    public string[] CompletedTransferIds { get; set; } = Array.Empty<string>();
    public ContractTransferReportMessage[] CompletedTransfers { get; set; } =
        Array.Empty<ContractTransferReportMessage>();
    public ContractTransferReportMessage[] SkippedTransfers { get; set; } =
        Array.Empty<ContractTransferReportMessage>();
}

internal sealed class ContractTransferReportMessage
{
    public int Sequence { get; set; }
    public string QualifiedItemId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Category { get; set; }
    public int Quality { get; set; }
    public int Quantity { get; set; }
    public int SourceX { get; set; }
    public int SourceY { get; set; }
    public int DestinationX { get; set; }
    public int DestinationY { get; set; }
}

internal sealed class ContractSyncRequestMessage
{
    public int SchemaVersion { get; set; }
    public string ModVersion { get; set; } = "";
    public ulong SaveId { get; set; }
    public long RequestingPlayerId { get; set; }
    public string SyncRequestId { get; set; } = "";
}

internal sealed class ContractSyncStateMessage
{
    public int SchemaVersion { get; set; }
    public ulong SaveId { get; set; }
    public string HostSessionId { get; set; } = "";
    public string SyncRequestId { get; set; } = "";
    public long StateVersion { get; set; }
    public bool HasActiveContract { get; set; }
    public ContractSnapshotMessage? ActiveContract { get; set; }
    public ContractResultMessage? RecentResult { get; set; }
    public ContractSettingsMessage Settings { get; set; } = new();
}

internal sealed class ContractSettingsMessage
{
    public int SchemaVersion { get; set; }
    public ulong SaveId { get; set; }
    public string HostSessionId { get; set; } = "";
    public long SettingsVersion { get; set; }
    public int BaseHourlyWage { get; set; }
    public int FriendshipWageImpactPercent { get; set; }
    public decimal RestDayMultiplier { get; set; }
    public HarvestDestinationMode DefaultHarvestDestination { get; set; }
    public FarmWorkStageSelection EnabledStages { get; set; }
    public int MaximumConcurrentWorkers { get; set; }

    public bool TryGetSnapshot(out ContractSettingsSnapshot settings)
    {
        int workerLimit = this.SchemaVersion == MultiplayerContractProtocol.SingleWorkerSchemaVersion
            && this.MaximumConcurrentWorkers <= 0
                ? ContractSettingsPolicy.DefaultMaximumConcurrentWorkers
                : this.MaximumConcurrentWorkers;
        settings = new ContractSettingsSnapshot(
            this.BaseHourlyWage,
            this.FriendshipWageImpactPercent,
            this.RestDayMultiplier,
            this.DefaultHarvestDestination,
            this.EnabledStages,
            workerLimit);
        return this.SchemaVersion is MultiplayerContractProtocol.SingleWorkerSchemaVersion
                or MultiplayerContractProtocol.SchemaVersion
            && this.SettingsVersion >= 0
            && settings.IsValid;
    }
}

internal sealed record ContractProtocolContext(
    string ModVersion,
    ulong SaveId,
    int TotalDays,
    IReadOnlySet<long> KnownPlayerIds,
    int MaximumConcurrentWorkers = ContractSettingsPolicy.DefaultMaximumConcurrentWorkers);

internal enum ContractRequestValidationFailure
{
    None,
    WrongSchema,
    WrongModVersion,
    WrongSave,
    StaleDay,
    SenderMismatch,
    UnknownPlayer,
    InvalidRequestId,
    InvalidWorker,
    InvalidTask,
    InvalidHarvestDestination
}

internal static class ContractRequestValidator
{
    public static ContractRequestValidationFailure Validate(
        ContractStartRequestMessage request,
        long senderPlayerId,
        ContractProtocolContext context)
    {
        if (request.SchemaVersion != MultiplayerContractProtocol.SchemaVersion)
            return ContractRequestValidationFailure.WrongSchema;
        if (!string.Equals(request.ModVersion, context.ModVersion, StringComparison.Ordinal))
            return ContractRequestValidationFailure.WrongModVersion;
        if (request.SaveId != context.SaveId)
            return ContractRequestValidationFailure.WrongSave;
        if (request.TotalDays != context.TotalDays)
            return ContractRequestValidationFailure.StaleDay;
        if (request.RequestingPlayerId != senderPlayerId)
            return ContractRequestValidationFailure.SenderMismatch;
        if (!context.KnownPlayerIds.Contains(request.RequestingPlayerId))
            return ContractRequestValidationFailure.UnknownPlayer;
        if (!Guid.TryParseExact(request.RequestId, "N", out _))
            return ContractRequestValidationFailure.InvalidRequestId;
        IReadOnlyList<string> workers = request.GetWorkerNames();
        int workerLimit = ContractSettingsPolicy.NormalizeMaximumConcurrentWorkers(
            context.MaximumConcurrentWorkers);
        if (workers.Count == 0
            || workers.Count > workerLimit
            || workers.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 100)
            || workers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != workers.Count)
            return ContractRequestValidationFailure.InvalidWorker;
        if (!Enum.IsDefined(request.Task) || request.Task != NamedFarmTask.FarmWork)
            return ContractRequestValidationFailure.InvalidTask;
        if (!HarvestDestinationPolicy.IsValidForTask(request.Task, request.HarvestDestination))
            return ContractRequestValidationFailure.InvalidHarvestDestination;

        return ContractRequestValidationFailure.None;
    }

    public static string GetReasonKey(ContractRequestValidationFailure failure)
    {
        return failure switch
        {
            ContractRequestValidationFailure.WrongSchema => "multiplayer.reject.schema",
            ContractRequestValidationFailure.WrongModVersion => "multiplayer.reject.version",
            ContractRequestValidationFailure.WrongSave => "multiplayer.reject.save",
            ContractRequestValidationFailure.StaleDay => "multiplayer.reject.stale",
            ContractRequestValidationFailure.SenderMismatch => "multiplayer.reject.sender",
            ContractRequestValidationFailure.UnknownPlayer => "multiplayer.reject.player",
            ContractRequestValidationFailure.InvalidRequestId => "multiplayer.reject.request-id",
            ContractRequestValidationFailure.InvalidWorker => "multiplayer.reject.worker",
            ContractRequestValidationFailure.InvalidTask => "multiplayer.reject.task",
            ContractRequestValidationFailure.InvalidHarvestDestination => "multiplayer.reject.destination",
            _ => "contract.failure.unknown"
        };
    }
}

internal sealed class ProcessedContractRequestLedger
{
    private readonly int Capacity;
    private readonly Dictionary<ContractRequestKey, ContractStartResponseMessage> Responses = new();
    private readonly Queue<ContractRequestKey> InsertionOrder = new();

    public ProcessedContractRequestLedger(int capacity = MultiplayerContractProtocol.ProcessedRequestCapacity)
    {
        this.Capacity = Math.Max(1, capacity);
    }

    public int Count => this.Responses.Count;

    public bool TryGet(long playerId, string requestId, out ContractStartResponseMessage? response)
    {
        return this.Responses.TryGetValue(new ContractRequestKey(playerId, requestId), out response);
    }

    public void Record(ContractStartResponseMessage response)
    {
        ContractRequestKey key = new(response.RequestingPlayerId, response.RequestId);
        if (this.Responses.ContainsKey(key))
            return;

        this.Responses[key] = response;
        this.InsertionOrder.Enqueue(key);
        while (this.Responses.Count > this.Capacity)
        {
            ContractRequestKey oldest = this.InsertionOrder.Dequeue();
            this.Responses.Remove(oldest);
        }
    }

    public IReadOnlyList<ContractStartResponseMessage> GetForPlayer(long playerId)
    {
        return this.InsertionOrder
            .Where(key => key.PlayerId == playerId)
            .Select(key => this.Responses[key])
            .ToArray();
    }

    public IReadOnlyList<ContractStartResponseMessage> GetAll()
    {
        return this.InsertionOrder
            .Select(key => this.Responses[key])
            .ToArray();
    }

    public void Clear()
    {
        this.Responses.Clear();
        this.InsertionOrder.Clear();
    }

    private readonly record struct ContractRequestKey(long PlayerId, string RequestId);
}

internal sealed class ContractSnapshotTracker
{
    private readonly Dictionary<string, long> LatestSequences = new(StringComparer.Ordinal);
    private string HostSessionId = "";

    public void BeginSession(string hostSessionId)
    {
        if (string.Equals(this.HostSessionId, hostSessionId, StringComparison.Ordinal))
            return;

        this.HostSessionId = hostSessionId;
        this.LatestSequences.Clear();
    }

    public bool TryAccept(
        ContractSnapshotMessage snapshot,
        int expectedSchemaVersion,
        ulong expectedSaveId)
    {
        return this.TryAccept(
            snapshot.SchemaVersion,
            snapshot.SaveId,
            snapshot.HostSessionId,
            snapshot.ContractId,
            snapshot.Sequence,
            expectedSchemaVersion,
            expectedSaveId);
    }

    public bool TryAccept(
        ContractResultMessage result,
        int expectedSchemaVersion,
        ulong expectedSaveId)
    {
        return this.TryAccept(
            result.SchemaVersion,
            result.SaveId,
            result.HostSessionId,
            result.ContractId,
            result.Sequence,
            expectedSchemaVersion,
            expectedSaveId);
    }

    public void Clear()
    {
        this.HostSessionId = "";
        this.LatestSequences.Clear();
    }

    private bool TryAccept(
        int schemaVersion,
        ulong saveId,
        string hostSessionId,
        string contractId,
        long sequence,
        int expectedSchemaVersion,
        ulong expectedSaveId)
    {
        if (schemaVersion != expectedSchemaVersion
            || saveId != expectedSaveId
            || string.IsNullOrWhiteSpace(hostSessionId)
            || string.IsNullOrWhiteSpace(contractId)
            || sequence <= 0)
            return false;

        if (string.IsNullOrWhiteSpace(this.HostSessionId))
            this.HostSessionId = hostSessionId;
        else if (!string.Equals(this.HostSessionId, hostSessionId, StringComparison.Ordinal))
            return false;

        if (this.LatestSequences.TryGetValue(contractId, out long latest)
            && sequence <= latest)
            return false;

        this.LatestSequences[contractId] = sequence;
        return true;
    }
}

internal sealed class HostStateVersionTracker
{
    public long Latest { get; private set; }

    public bool CanAccept(long stateVersion)
    {
        return stateVersion >= 0 && stateVersion >= this.Latest;
    }

    public void Commit(long stateVersion)
    {
        if (stateVersion > this.Latest)
            this.Latest = stateVersion;
    }

    public void Clear()
    {
        this.Latest = 0;
    }
}

internal sealed class HostSessionTracker
{
    private string PendingSyncRequestId = "";

    public string Current { get; private set; } = "";

    public bool HasSession => !string.IsNullOrWhiteSpace(this.Current);

    public bool BeginHandshake(string syncRequestId)
    {
        if (!Guid.TryParseExact(syncRequestId, "N", out _))
            return false;

        this.PendingSyncRequestId = syncRequestId;
        return true;
    }

    public bool TryEstablish(string hostSessionId, string syncRequestId)
    {
        if (!Guid.TryParseExact(hostSessionId, "N", out _)
            || string.IsNullOrWhiteSpace(this.PendingSyncRequestId)
            || !string.Equals(this.PendingSyncRequestId, syncRequestId, StringComparison.Ordinal))
            return false;

        if (!this.HasSession)
            this.Current = hostSessionId;

        if (!this.Matches(hostSessionId))
            return false;

        this.PendingSyncRequestId = "";
        return true;
    }

    public bool Matches(string hostSessionId)
    {
        return this.HasSession
            && string.Equals(this.Current, hostSessionId, StringComparison.Ordinal);
    }

    public void Clear()
    {
        this.Current = "";
        this.PendingSyncRequestId = "";
    }
}

internal sealed record NamedContractRuntimeState(
    string ContractId,
    string RequestId,
    long RequestingPlayerId,
    string WorkerName,
    NamedFarmTask Task,
    decimal EfficiencyMultiplier,
    string Phase,
    int ArrivalX,
    int ArrivalY,
    FarmBoundarySide ArrivalSide,
    int EntranceSwitches,
    int TargetX,
    int TargetY,
    int ReservedGold,
    int StartTime,
    int CompletedWork,
    IReadOnlyList<NamedContractCargoState> Cargo,
    IReadOnlyList<string> CompletedTransferIds)
{
    public int CargoCount => this.Cargo.Sum(item => item.Stack);
    public HarvestDestinationMode HarvestDestination { get; init; } =
        HarvestDestinationMode.ClassifiedChests;
}

internal sealed record NamedContractCargoState(
    string TransferId,
    string QualifiedItemId,
    string DisplayName,
    int Quality,
    int Stack);

internal sealed record NamedContractTransferState(
    int Sequence,
    string QualifiedItemId,
    string DisplayName,
    int Category,
    int Quality,
    int Quantity,
    int SourceX,
    int SourceY,
    int DestinationX,
    int DestinationY);

internal sealed record NamedContractCompletionState(
    string ContractId,
    string RequestId,
    long RequestingPlayerId,
    string WorkerName,
    NamedFarmTask Task,
    bool Succeeded,
    string ReasonKey,
    int CompletedWork,
    int PlayerItems,
    int ChestItems,
    int OverflowItems,
    int QuarantinedItems,
    int DroppedItems,
    int BillableHours,
    int ChargedGold,
    int RefundedGold,
    IReadOnlyList<NamedContractCargoState> ProducedItems,
    IReadOnlyList<string> CompletedTransferIds,
    IReadOnlyList<NamedContractTransferState> CompletedTransfers,
    IReadOnlyList<NamedContractTransferState> SkippedTransfers)
{
    public HarvestDestinationMode HarvestDestination { get; init; } =
        HarvestDestinationMode.ClassifiedChests;
}
