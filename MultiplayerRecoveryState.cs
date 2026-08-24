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
            || state.ProtocolSchemaVersion != MultiplayerContractProtocol.SchemaVersion
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
            if (!IsValidResponse(response, expectedSaveId)
                || !requestKeys.Add((response.RequestingPlayerId, response.RequestId)))
                return false;

            if (response.Accepted)
                accepted[(response.RequestingPlayerId, response.RequestId)] = response;
        }

        HashSet<long> resultPlayers = new();
        foreach (ContractResultMessage result in state.RecentResults)
        {
            if (!IsValidResult(result, expectedSaveId)
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

    private static bool IsValidResponse(ContractStartResponseMessage? response, ulong expectedSaveId)
    {
        return response is not null
            && response.SchemaVersion == MultiplayerContractProtocol.SchemaVersion
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

    private static bool IsValidResult(ContractResultMessage? result, ulong expectedSaveId)
    {
        return result is not null
            && result.SchemaVersion == MultiplayerContractProtocol.SchemaVersion
            && result.SaveId == expectedSaveId
            && !string.IsNullOrWhiteSpace(result.HostSessionId)
            && Guid.TryParseExact(result.ContractId, "N", out _)
            && result.Sequence > 0
            && result.StateVersion > 0
            && Guid.TryParseExact(result.RequestId, "N", out _)
            && result.RequestingPlayerId > 0
            && !string.IsNullOrWhiteSpace(result.WorkerName)
            && Enum.IsDefined(result.Task)
            && result.CompletedWork >= 0
            && result.PlayerItems >= 0
            && result.ChestItems >= 0
            && result.OverflowItems >= 0
            && result.DroppedItems >= 0
            && result.BillableHours >= 0
            && result.ChargedGold >= 0
            && result.RefundedGold >= 0
            && result.ProducedItems is not null
            && result.CompletedTransferIds is not null
            && result.ProducedItems.All(item => item is not null
                && Guid.TryParseExact(item.TransferId, "N", out _)
                && !string.IsNullOrWhiteSpace(item.QualifiedItemId)
                && !string.IsNullOrWhiteSpace(item.DisplayName)
                && item.Quality >= 0
                && item.Stack > 0)
            && result.CompletedTransferIds.All(id => Guid.TryParseExact(id, "N", out _));
    }
}
