using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Serialization;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Network;

namespace EvilFarmOwner;

internal enum StorageSortRecoveryWriteStatus
{
    RejectedBeforeWrite,
    Persisted,
    UncertainAfterWrite
}

internal sealed class StorageSortRecoveryManager
{
    internal const string RecoveryDataKey = "Aveouter.EvilFarmOwner/StorageSortRecovery";
    internal const string RecoveryTransferDataKey =
        "Aveouter.EvilFarmOwner/StorageSortRecoveryTransfer";

    private const int RetryIntervalTicks = 60;

    private readonly IMonitor Monitor;
    private int RetryTicks;

    public StorageSortRecoveryManager(IMonitor monitor)
    {
        this.Monitor = monitor;
    }

    public bool HasPendingRecovery { get; private set; }

    public void OnSaveLoaded()
    {
        this.HasPendingRecovery = false;
        this.RetryTicks = 0;
        if (Context.IsWorldReady && Context.IsMainPlayer)
            this.TryRestore();
    }

    public void Update()
    {
        if (!Context.IsWorldReady
            || !Context.IsMainPlayer
            || !this.HasPendingRecovery
            || ++this.RetryTicks % RetryIntervalTicks != 0)
        {
            return;
        }

        this.TryRestore();
    }

    public void OnReturnedToTitle()
    {
        this.HasPendingRecovery = false;
        this.RetryTicks = 0;
    }

    public bool TryRecover()
    {
        return this.TryRestore();
    }

    public StorageSortRecoveryWriteStatus TryPersistDetached(
        Guid contractId,
        Guid transferId,
        Item detachedItem)
    {
        if (!Context.IsWorldReady
            || !Context.IsMainPlayer
            || contractId == Guid.Empty
            || transferId == Guid.Empty
            || detachedItem.Stack <= 0)
            return StorageSortRecoveryWriteStatus.RejectedBeforeWrite;

        bool writeAttempted = false;
        try
        {
            HarvestCargoRecoveryItemData savedItem = CreateRecoveryItem(transferId, detachedItem);
            HarvestCargoRecoverySaveData state = HarvestCargoRecoveryState.Create(
                Game1.uniqueIDForThisGame,
                contractId.ToString("N"),
                new[] { savedItem });
            if (!HarvestCargoRecoveryState.IsValid(state, Game1.uniqueIDForThisGame))
                return StorageSortRecoveryWriteStatus.RejectedBeforeWrite;

            string serialized = JsonSerializer.Serialize(state);
            if (!HarvestCargoRecoveryState.IsSerializedPayloadValid(serialized))
                return StorageSortRecoveryWriteStatus.RejectedBeforeWrite;
            if (Game1.MasterPlayer.modData.TryGetValue(RecoveryDataKey, out string? prior)
                && !string.IsNullOrWhiteSpace(prior)
                && !string.Equals(prior, serialized, StringComparison.Ordinal))
            {
                this.HasPendingRecovery = true;
                this.Monitor.Log(
                    "Refusing to overwrite a different unresolved storage-sort recovery record.",
                    LogLevel.Error);
                return StorageSortRecoveryWriteStatus.RejectedBeforeWrite;
            }

            writeAttempted = true;
            Game1.MasterPlayer.modData[RecoveryDataKey] = serialized;
            bool verified = Game1.MasterPlayer.modData.TryGetValue(
                    RecoveryDataKey,
                    out string? written)
                && string.Equals(written, serialized, StringComparison.Ordinal);
            this.HasPendingRecovery = true;
            this.RetryTicks = 0;
            if (verified)
            {
                this.Monitor.Log(
                    $"Persisted storage-sort transfer {transferId:N} x{detachedItem.Stack} "
                    + "into the shared emergency-quarantine recovery record.",
                    LogLevel.Error);
            }

            return verified
                ? StorageSortRecoveryWriteStatus.Persisted
                : StorageSortRecoveryWriteStatus.UncertainAfterWrite;
        }
        catch (Exception ex)
        {
            this.HasPendingRecovery = writeAttempted || HasStoredRecoveryRecord();
            this.Monitor.Log(
                $"CRITICAL: storage-sort recovery record write failed: {ex}",
                LogLevel.Error);
            return writeAttempted
                ? StorageSortRecoveryWriteStatus.UncertainAfterWrite
                : StorageSortRecoveryWriteStatus.RejectedBeforeWrite;
        }
    }

    public bool TryForceQuarantineAtSaveBoundary(Guid transferId, Item detachedItem)
    {
        if (!Context.IsWorldReady
            || !Context.IsMainPlayer
            || transferId == Guid.Empty
            || detachedItem.Stack <= 0)
        {
            return false;
        }

        try
        {
            // This private inventory is host-only and cannot be opened while a named
            // contract is active. At the synchronous save boundary, retaining the
            // original detached Item instance is safer than leaving it in transient
            // controller memory when the serializable recovery record is unavailable.
            string transferKey = transferId.ToString("N");
            Inventory quarantine = Game1.player.team.GetOrCreateGlobalInventory(
                HarvestingContractExecutionController.QuarantineInventoryId);
            Item? existing = quarantine.FirstOrDefault(item => item is not null
                && item.modData.TryGetValue(RecoveryTransferDataKey, out string? existingId)
                && string.Equals(existingId, transferKey, StringComparison.Ordinal));
            if (existing is not null)
            {
                if (!ReferenceEquals(existing, detachedItem))
                {
                    throw new InvalidDataException(
                        $"Storage-sort transfer {transferKey} already identifies a different item instance.");
                }

                return true;
            }

            detachedItem.modData[RecoveryTransferDataKey] = transferKey;
            quarantine.Add(detachedItem);
            if (!quarantine.Any(item => ReferenceEquals(item, detachedItem)))
            {
                throw new InvalidDataException(
                    $"Quarantine did not retain storage-sort transfer {transferKey} at save time.");
            }

            this.Monitor.Log(
                $"Forced detached storage-sort transfer {transferKey} "
                + $"('{detachedItem.QualifiedItemId}' q{detachedItem.Quality} x{detachedItem.Stack}) "
                + "into the private team quarantine at the save boundary.",
                LogLevel.Error);
            return true;
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"CRITICAL: save-boundary storage-sort quarantine failed for transfer "
                + $"{transferId:N}: {ex}",
                LogLevel.Error);
            return false;
        }
    }

    private bool TryRestore()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return false;
        if (!Game1.MasterPlayer.modData.TryGetValue(RecoveryDataKey, out string? serialized)
            || string.IsNullOrWhiteSpace(serialized))
        {
            this.HasPendingRecovery = false;
            this.RetryTicks = 0;
            return true;
        }

        this.HasPendingRecovery = true;
        NetMutex mutex = Game1.player.team.GetOrCreateGlobalInventoryMutex(
            HarvestingContractExecutionController.QuarantineInventoryId);
        try
        {
            if (!HarvestCargoRecoveryState.IsSerializedPayloadValid(serialized))
                throw new InvalidDataException("Storage-sort recovery payload exceeds its safe limit.");

            HarvestCargoRecoverySaveData? state =
                JsonSerializer.Deserialize<HarvestCargoRecoverySaveData>(serialized);
            if (!HarvestCargoRecoveryState.IsValid(state, Game1.uniqueIDForThisGame)
                || state is null
                || state.Items.Length != 1)
            {
                throw new InvalidDataException(
                    "Storage-sort recovery payload failed schema, save, or item validation.");
            }
            if (!TryAcquireImmediately(mutex))
                return false;

            HarvestCargoRecoveryItemData saved = state.Items[0];
            Inventory quarantine = Game1.player.team.GetOrCreateGlobalInventory(
                HarvestingContractExecutionController.QuarantineInventoryId);
            Item? existing = quarantine.FirstOrDefault(item => item is not null
                && item.modData.TryGetValue(
                    RecoveryTransferDataKey,
                    out string? transferId)
                && string.Equals(transferId, saved.TransferId, StringComparison.Ordinal));
            if (existing is not null)
            {
                if (!MatchesSavedItem(existing, saved, ignoreRecoveryTag: true))
                {
                    throw new InvalidDataException(
                        $"Storage-sort recovery transfer {saved.TransferId} identifies different cargo.");
                }
            }
            else
            {
                Item restored = DeserializeRecoveryItem(saved);
                if (!MatchesSavedItem(restored, saved, ignoreRecoveryTag: false))
                {
                    throw new InvalidDataException(
                        $"Storage-sort transfer {saved.TransferId} could not be reconstructed exactly.");
                }

                restored.modData[
                    RecoveryTransferDataKey] = saved.TransferId;
                quarantine.Add(restored);
                if (!quarantine.Any(item => ReferenceEquals(item, restored)))
                {
                    throw new InvalidDataException(
                        $"Quarantine did not retain storage-sort transfer {saved.TransferId}.");
                }
            }

            Game1.MasterPlayer.modData.Remove(RecoveryDataKey);
            if (Game1.MasterPlayer.modData.TryGetValue(
                    RecoveryDataKey,
                    out string? remaining)
                && !string.IsNullOrWhiteSpace(remaining))
            {
                throw new IOException("Storage-sort recovery record could not be cleared after restore.");
            }
            this.HasPendingRecovery = false;
            this.RetryTicks = 0;
            this.Monitor.Log(
                $"Restored storage-sort transfer {saved.TransferId} into the shared emergency quarantine.",
                LogLevel.Warn);
            return true;
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Storage-sort recovery remains fail-closed: {ex}",
                LogLevel.Error);
            return false;
        }
        finally
        {
            if (mutex.IsLockHeld())
                mutex.ReleaseLock();
        }
    }

    private static HarvestCargoRecoveryItemData CreateRecoveryItem(Guid transferId, Item item)
    {
        Type type = item.GetType();
        return new HarvestCargoRecoveryItemData
        {
            TransferId = transferId.ToString("N"),
            QualifiedItemId = item.QualifiedItemId,
            DisplayName = item.DisplayName,
            RuntimeType = type.FullName ?? type.Name,
            RuntimeAssembly = type.Assembly.GetName().Name ?? "",
            SerializedItemXml = SerializeItem(item),
            Quality = item.Quality,
            Stack = item.Stack,
            ModData = item.modData.Pairs
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
        };
    }

    private static bool MatchesSavedItem(
        Item item,
        HarvestCargoRecoveryItemData saved,
        bool ignoreRecoveryTag)
    {
        Type type = item.GetType();
        if (!string.Equals(item.QualifiedItemId, saved.QualifiedItemId, StringComparison.Ordinal)
            || !string.Equals(type.FullName ?? type.Name, saved.RuntimeType, StringComparison.Ordinal)
            || !string.Equals(
                type.Assembly.GetName().Name,
                saved.RuntimeAssembly,
                StringComparison.Ordinal)
            || item.Quality != saved.Quality
            || item.Stack != saved.Stack)
        {
            return false;
        }

        Dictionary<string, string> actualModData = item.modData.Pairs
            .Where(pair => !ignoreRecoveryTag
                || !string.Equals(
                    pair.Key,
                    RecoveryTransferDataKey,
                    StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        bool modDataMatches = actualModData.Count == saved.ModData.Count
            && saved.ModData.All(pair => actualModData.TryGetValue(pair.Key, out string? value)
                && string.Equals(value, pair.Value, StringComparison.Ordinal));
        if (!modDataMatches)
            return false;

        string? recoveryTag = null;
        if (ignoreRecoveryTag)
        {
            item.modData.TryGetValue(
                RecoveryTransferDataKey,
                out recoveryTag);
            item.modData.Remove(RecoveryTransferDataKey);
        }

        try
        {
            return string.Equals(
                SerializeItem(item),
                saved.SerializedItemXml,
                StringComparison.Ordinal);
        }
        finally
        {
            if (ignoreRecoveryTag && recoveryTag is not null)
            {
                item.modData[
                    RecoveryTransferDataKey] = recoveryTag;
            }
        }
    }

    private static string SerializeItem(Item item)
    {
        XmlSerializer serializer = new(item.GetType());
        XmlSerializerNamespaces namespaces = new();
        namespaces.Add("", "");
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        serializer.Serialize(writer, item, namespaces);
        return writer.ToString();
    }

    private static Item DeserializeRecoveryItem(HarvestCargoRecoveryItemData saved)
    {
        System.Reflection.Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => string.Equals(
                candidate.GetName().Name,
                saved.RuntimeAssembly,
                StringComparison.Ordinal));
        Type? itemType = assembly?.GetType(saved.RuntimeType, throwOnError: false, ignoreCase: false);
        if (itemType is null || !typeof(Item).IsAssignableFrom(itemType))
        {
            throw new InvalidDataException(
                $"Recovery item type '{saved.RuntimeType}' from '{saved.RuntimeAssembly}' is unavailable.");
        }

        XmlSerializer serializer = new(itemType);
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using StringReader stringReader = new(saved.SerializedItemXml);
        using XmlReader xmlReader = XmlReader.Create(stringReader, settings);
        return serializer.Deserialize(xmlReader) as Item
            ?? throw new InvalidDataException(
                $"Storage-sort transfer {saved.TransferId} did not deserialize as an item.");
    }

    private static bool TryAcquireImmediately(NetMutex mutex)
    {
        if (mutex.IsLockHeld())
            return true;
        if (mutex.IsLocked())
            return false;

        bool acquired = false;
        mutex.RequestLock(() => acquired = true, () => acquired = false);
        if (!acquired)
            mutex.Update(Game1.getOnlineFarmers());
        return acquired || mutex.IsLockHeld();
    }

    private static bool HasStoredRecoveryRecord()
    {
        try
        {
            return Context.IsWorldReady
                && Game1.MasterPlayer.modData.TryGetValue(RecoveryDataKey, out string? serialized)
                && !string.IsNullOrWhiteSpace(serialized);
        }
        catch
        {
            return true;
        }
    }
}
