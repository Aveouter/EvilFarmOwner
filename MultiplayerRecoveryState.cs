namespace EvilFarmOwner;

internal sealed class MultiplayerRecoverySaveData
{
    public int SchemaVersion { get; set; }
    public int ProtocolSchemaVersion { get; set; }
    public string ModVersion { get; set; } = "";
    public ulong SaveId { get; set; }
    public bool IsClean { get; set; }
    public ContractStartResponseMessage[] ProcessedRequests { get; set; } =
        Array.Empty<ContractStartResponseMessage>();
    public ContractResultMessage[] RecentResults { get; set; } =
        Array.Empty<ContractResultMessage>();
}

internal static class MultiplayerRecoveryState
{
    public const int SchemaVersion = 1;
    public const string SaveDataKey = "multiplayer-recovery";
    private const int LegacyHandshakeProtocolSchemaVersion = 3;
    private const int LegacyQuarantineProtocolSchemaVersion = 4;
    private const int LegacyPlacementProtocolSchemaVersion = 5;
    private const int LegacyEfficiencyProtocolSchemaVersion = 6;

    public static MultiplayerRecoverySaveData Create(
        string modVersion,
        ulong saveId,
        IEnumerable<ContractStartResponseMessage> processedRequests,
        IEnumerable<ContractResultMessage> recentResults,
        bool isClean = true)
    {
        ArgumentNullException.ThrowIfNull(processedRequests);
        ArgumentNullException.ThrowIfNull(recentResults);
        ContractStartResponseMessage[] requests = processedRequests.ToArray();
        Dictionary<(long PlayerId, string RequestId), string> acceptedContracts = requests
            .Where(response => response.Accepted)
            .GroupBy(response => (response.RequestingPlayerId, response.RequestId))
            .ToDictionary(group => group.Key, group => group.Last().ContractId);
        ContractResultMessage[] pairedResults = recentResults
            .Where(result => acceptedContracts.TryGetValue(
                    (result.RequestingPlayerId, result.RequestId),
                    out string? contractId)
                && string.Equals(contractId, result.ContractId, StringComparison.Ordinal))
            .ToArray();
        return new MultiplayerRecoverySaveData
        {
            SchemaVersion = SchemaVersion,
            ProtocolSchemaVersion = MultiplayerContractProtocol.SchemaVersion,
            ModVersion = modVersion,
            SaveId = saveId,
            IsClean = isClean,
            ProcessedRequests = requests,
            RecentResults = pairedResults
        };
    }

    public static bool IsValid(
        MultiplayerRecoverySaveData? state,
        ulong expectedSaveId)
    {
        if (state is null
            || state.SchemaVersion != SchemaVersion
            || !IsSupportedProtocolSchemaVersion(state.ProtocolSchemaVersion)
            || string.IsNullOrWhiteSpace(state.ModVersion)
            || state.SaveId != expectedSaveId
            || !state.IsClean
            || state.ProcessedRequests is null
            || state.RecentResults is null
            || state.ProcessedRequests.Length > MultiplayerContractProtocol.ProcessedRequestCapacity
            || state.RecentResults.Length > MultiplayerContractProtocol.ProcessedRequestCapacity)
            return false;

        HashSet<(long PlayerId, string RequestId)> requestKeys = new();
        Dictionary<(long PlayerId, string RequestId), ContractStartResponseMessage> accepted = new();
        foreach (ContractStartResponseMessage response in state.ProcessedRequests)
        {
            if (!IsValidResponse(response, expectedSaveId, state.ProtocolSchemaVersion)
                || !requestKeys.Add((response.RequestingPlayerId, response.RequestId)))
                return false;

            if (response.Accepted)
                accepted[(response.RequestingPlayerId, response.RequestId)] = response;
        }

        HashSet<long> resultPlayers = new();
        foreach (ContractResultMessage result in state.RecentResults)
        {
            if (!IsValidResult(result, expectedSaveId, state.ProtocolSchemaVersion)
                || !resultPlayers.Add(result.RequestingPlayerId)
                || !accepted.TryGetValue(
                    (result.RequestingPlayerId, result.RequestId),
                    out ContractStartResponseMessage? response)
                || !string.Equals(response.ContractId, result.ContractId, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    public static void RebindResponse(
        ContractStartResponseMessage response,
        string hostSessionId,
        ulong saveId)
    {
        response.SchemaVersion = MultiplayerContractProtocol.SchemaVersion;
        response.SaveId = saveId;
        response.HostSessionId = hostSessionId;
    }

    public static void RebindResult(
        ContractResultMessage result,
        string hostSessionId,
        ulong saveId,
        long sequence,
        long stateVersion)
    {
        result.SchemaVersion = MultiplayerContractProtocol.SchemaVersion;
        result.SaveId = saveId;
        result.HostSessionId = hostSessionId;
        result.Sequence = sequence;
        result.StateVersion = stateVersion;
    }

    private static bool IsSupportedProtocolSchemaVersion(int protocolSchemaVersion)
    {
        // Protocol 4 only adds the reconnect sync nonce. Protocol 5 adds a nonnegative
        // quarantine destination count. Protocol 6 adds efficiency only to live snapshots,
        // and protocol 7 adds storage sorting as a task. Persisted transaction identities from
        // these versions remain compatible after their own schema is fully validated.
        return protocolSchemaVersion is LegacyHandshakeProtocolSchemaVersion
            or LegacyQuarantineProtocolSchemaVersion
            or LegacyPlacementProtocolSchemaVersion
            or LegacyEfficiencyProtocolSchemaVersion
            or MultiplayerContractProtocol.SchemaVersion;
    }

    private static bool IsValidResponse(
        ContractStartResponseMessage? response,
        ulong expectedSaveId,
        int expectedProtocolSchemaVersion)
    {
        return response is not null
            && response.SchemaVersion == expectedProtocolSchemaVersion
            && response.SaveId == expectedSaveId
            && !string.IsNullOrWhiteSpace(response.HostSessionId)
            && response.HostOrder > 0
            && response.RequestingPlayerId > 0
            && Guid.TryParseExact(response.RequestId, "N", out _)
            && (response.Accepted
                ? Guid.TryParseExact(response.ContractId, "N", out _)
                    && string.IsNullOrWhiteSpace(response.ReasonKey)
                : string.IsNullOrWhiteSpace(response.ContractId)
                    && !string.IsNullOrWhiteSpace(response.ReasonKey));
    }

    internal static bool IsValidResult(
        ContractResultMessage? result,
        ulong expectedSaveId,
        int expectedProtocolSchemaVersion)
    {
        if (result is null
            || result.SchemaVersion != expectedProtocolSchemaVersion
            || result.SaveId != expectedSaveId
            || string.IsNullOrWhiteSpace(result.HostSessionId)
            || !Guid.TryParseExact(result.ContractId, "N", out _)
            || result.Sequence <= 0
            || result.StateVersion <= 0
            || !Guid.TryParseExact(result.RequestId, "N", out _)
            || result.RequestingPlayerId <= 0
            || string.IsNullOrWhiteSpace(result.WorkerName)
            || result.WorkerName.Length > 100
            || !Enum.IsDefined(result.Task)
            || result.CompletedWork < 0
            || (result.Succeeded && result.CompletedWork == 0)
            || result.PlayerItems < 0
            || result.ChestItems < 0
            || result.OverflowItems < 0
            || result.QuarantinedItems < 0
            || result.DroppedItems < 0
            || result.BillableHours < 0
            || result.BillableHours > ContractPreviewService.RegularShiftHours
            || (result.Succeeded && result.BillableHours == 0)
            || result.ChargedGold < 0
            || result.RefundedGold < 0
            || (result.Succeeded
                ? !string.IsNullOrWhiteSpace(result.ReasonKey)
                : string.IsNullOrWhiteSpace(result.ReasonKey))
            || result.ProducedItems is null
            || result.CompletedTransferIds is null
            || result.CompletedTransfers is null
            || result.SkippedTransfers is null)
            return false;

        HashSet<string> producedTransferIds = new(StringComparer.Ordinal);
        long producedItems = 0;
        foreach (ContractCargoSnapshotMessage item in result.ProducedItems)
        {
            if (item is null
                || !Guid.TryParseExact(item.TransferId, "N", out _)
                || !producedTransferIds.Add(item.TransferId)
                || string.IsNullOrWhiteSpace(item.QualifiedItemId)
                || string.IsNullOrWhiteSpace(item.DisplayName)
                || item.Quality < 0
                || item.Stack <= 0)
                return false;

            producedItems += item.Stack;
        }

        HashSet<string> completedTransferIds = new(StringComparer.Ordinal);
        foreach (string transferId in result.CompletedTransferIds)
        {
            if (!Guid.TryParseExact(transferId, "N", out _)
                || !completedTransferIds.Add(transferId))
                return false;
        }

        if (!AreTransferReportsValid(result))
            return false;

        long placedItems = (long)result.PlayerItems
            + result.ChestItems
            + result.OverflowItems
            + result.QuarantinedItems
            + result.DroppedItems;
        return producedItems == placedItems;
    }

    private static bool AreTransferReportsValid(ContractResultMessage result)
    {
        if (result.Task != NamedFarmTask.StorageSorting)
        {
            return result.CompletedTransfers.Length == 0
                && result.SkippedTransfers.Length == 0;
        }

        if (result.SchemaVersion < MultiplayerContractProtocol.SchemaVersion
            || result.CompletedTransfers.Length != result.CompletedWork
            || result.CompletedTransfers.Length + result.SkippedTransfers.Length > 4096
            || (result.Succeeded && result.SkippedTransfers.Length != 0))
        {
            return false;
        }

        HashSet<int> sequences = new();
        long movedItems = 0;
        foreach (ContractTransferReportMessage transfer in result.CompletedTransfers)
        {
            if (!IsValidTransferReport(transfer) || !sequences.Add(transfer.Sequence))
                return false;
            movedItems += transfer.Quantity;
        }
        foreach (ContractTransferReportMessage transfer in result.SkippedTransfers)
        {
            if (!IsValidTransferReport(transfer) || !sequences.Add(transfer.Sequence))
                return false;
        }

        return movedItems == result.ChestItems
            && sequences.OrderBy(sequence => sequence).SequenceEqual(
                Enumerable.Range(1, sequences.Count));
    }

    private static bool IsValidTransferReport(ContractTransferReportMessage? transfer)
    {
        return transfer is not null
            && transfer.Sequence > 0
            && !string.IsNullOrWhiteSpace(transfer.QualifiedItemId)
            && transfer.QualifiedItemId.Length <= 256
            && !string.IsNullOrWhiteSpace(transfer.DisplayName)
            && transfer.DisplayName.Length <= 256
            && transfer.Quality >= 0
            && transfer.Quantity > 0
            && transfer.SourceX >= 0
            && transfer.SourceY >= 0
            && transfer.DestinationX >= 0
            && transfer.DestinationY >= 0
            && (transfer.SourceX != transfer.DestinationX
                || transfer.SourceY != transfer.DestinationY);
    }
}
