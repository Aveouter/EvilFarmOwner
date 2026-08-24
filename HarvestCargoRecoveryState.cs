namespace EvilFarmOwner;

internal sealed class HarvestCargoRecoverySaveData
{
    public int SchemaVersion { get; set; }
    public ulong SaveId { get; set; }
    public string ContractId { get; set; } = "";
    public HarvestCargoRecoveryItemData[] Items { get; set; } =
        Array.Empty<HarvestCargoRecoveryItemData>();
}

internal sealed class HarvestCargoRecoveryItemData
{
    public string TransferId { get; set; } = "";
    public string QualifiedItemId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string RuntimeType { get; set; } = "";
    public string RuntimeAssembly { get; set; } = "";
    public string SerializedItemXml { get; set; } = "";
    public int Quality { get; set; }
    public int Stack { get; set; }
    public Dictionary<string, string> ModData { get; set; } = new(StringComparer.Ordinal);
}

internal static class HarvestCargoRecoveryState
{
    public const int SchemaVersion = 1;
    public const int MaximumItemRecords = 1024;
    public const int MaximumModDataEntriesPerItem = 256;
    public const int MaximumSerializedItemLength = 1_000_000;
    public const int MaximumSerializedPayloadLength = 1_000_000;

    public static HarvestCargoRecoverySaveData Create(
        ulong saveId,
        string contractId,
        IEnumerable<HarvestCargoRecoveryItemData> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new HarvestCargoRecoverySaveData
        {
            SchemaVersion = SchemaVersion,
            SaveId = saveId,
            ContractId = contractId,
            Items = items.ToArray()
        };
    }

    public static bool IsValid(HarvestCargoRecoverySaveData? state, ulong expectedSaveId)
    {
        if (state is null
            || state.SchemaVersion != SchemaVersion
            || state.SaveId != expectedSaveId
            || !Guid.TryParseExact(state.ContractId, "N", out _)
            || state.Items is null
            || state.Items.Length == 0
            || state.Items.Length > MaximumItemRecords)
            return false;

        HashSet<string> transferIds = new(StringComparer.Ordinal);
        long payloadContentLength = state.ContractId.Length;
        foreach (HarvestCargoRecoveryItemData item in state.Items)
        {
            if (item is null
                || !Guid.TryParseExact(item.TransferId, "N", out _)
                || !transferIds.Add(item.TransferId)
                || string.IsNullOrWhiteSpace(item.QualifiedItemId)
                || item.QualifiedItemId.Length > 200
                || string.IsNullOrWhiteSpace(item.DisplayName)
                || item.DisplayName.Length > 200
                || string.IsNullOrWhiteSpace(item.RuntimeType)
                || item.RuntimeType.Length > 500
                || string.IsNullOrWhiteSpace(item.RuntimeAssembly)
                || item.RuntimeAssembly.Length > 200
                || string.IsNullOrWhiteSpace(item.SerializedItemXml)
                || item.SerializedItemXml.Length > MaximumSerializedItemLength
                || item.Quality < 0
                || item.Stack <= 0
                || item.ModData is null
                || item.ModData.Count > MaximumModDataEntriesPerItem
                || item.ModData.Any(pair => string.IsNullOrWhiteSpace(pair.Key)
                    || pair.Key.Length > 500
                    || pair.Value is null
                    || pair.Value.Length > 10000))
                return false;
            if (!TryAccumulatePayloadContent(item, ref payloadContentLength))
                return false;
        }

        return true;
    }

    public static bool IsSerializedPayloadValid(string? serialized)
    {
        return !string.IsNullOrWhiteSpace(serialized)
            && serialized.Length <= MaximumSerializedPayloadLength;
    }

    public static bool TryAccumulatePayloadContent(
        HarvestCargoRecoveryItemData item,
        ref long contentLength)
    {
        ArgumentNullException.ThrowIfNull(item);
        long added = item.TransferId.Length
            + item.QualifiedItemId.Length
            + item.DisplayName.Length
            + item.RuntimeType.Length
            + item.RuntimeAssembly.Length
            + item.SerializedItemXml.Length;
        foreach (KeyValuePair<string, string> pair in item.ModData)
            added += pair.Key.Length + pair.Value.Length;

        if (contentLength > MaximumSerializedPayloadLength - added)
            return false;

        contentLength += added;
        return true;
    }

    public static int CountItems(HarvestCargoRecoverySaveData state)
    {
        ArgumentNullException.ThrowIfNull(state);
        long total = state.Items?.Sum(item => (long)item.Stack) ?? 0;
        return (int)Math.Min(int.MaxValue, Math.Max(0, total));
    }
}
