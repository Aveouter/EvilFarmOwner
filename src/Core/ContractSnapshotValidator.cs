namespace EvilFarmOwner;

internal static class ContractSnapshotValidator
{
    private const int MaximumCollectionSize = 4096;

    public static bool IsValid(
        ContractSnapshotMessage? snapshot,
        int expectedSchemaVersion,
        ulong expectedSaveId)
    {
        if (snapshot is null
            || snapshot.SchemaVersion != expectedSchemaVersion
            || snapshot.SaveId != expectedSaveId
            || !Guid.TryParseExact(snapshot.HostSessionId, "N", out _)
            || !Guid.TryParseExact(snapshot.ContractId, "N", out _)
            || snapshot.Sequence <= 0
            || snapshot.StateVersion <= 0
            || !Guid.TryParseExact(snapshot.RequestId, "N", out _)
            || snapshot.RequestingPlayerId <= 0
            || string.IsNullOrWhiteSpace(snapshot.WorkerName)
            || snapshot.WorkerName.Length > 100
            || snapshot.Task != NamedFarmTask.FarmWork
            || !HarvestDestinationPolicy.IsValidForTask(
                snapshot.Task,
                snapshot.HarvestDestination)
            || !WorkerEfficiencyProfiles.IsValidMultiplier(snapshot.EfficiencyMultiplier)
            || string.IsNullOrWhiteSpace(snapshot.Phase)
            || snapshot.Phase.Length > 200
            || !Enum.IsDefined(snapshot.ArrivalSide)
            || snapshot.ArrivalX < 0
            || snapshot.ArrivalY < 0
            || snapshot.EntranceSwitches < 0
            || snapshot.TargetX < 0
            || snapshot.TargetY < 0
            || snapshot.ReservedGold < 0
            || snapshot.StartTime < 0
            || snapshot.CompletedWork < 0
            || snapshot.Cargo is null
            || snapshot.Cargo.Length > MaximumCollectionSize
            || snapshot.CompletedTransferIds is null
            || snapshot.CompletedTransferIds.Length > MaximumCollectionSize)
            return false;

        HashSet<string> cargoIds = new(StringComparer.Ordinal);
        long cargoItems = 0;
        foreach (ContractCargoSnapshotMessage item in snapshot.Cargo)
        {
            if (item is null
                || !Guid.TryParseExact(item.TransferId, "N", out _)
                || !cargoIds.Add(item.TransferId)
                || string.IsNullOrWhiteSpace(item.QualifiedItemId)
                || item.QualifiedItemId.Length > 256
                || string.IsNullOrWhiteSpace(item.DisplayName)
                || item.DisplayName.Length > 256
                || item.Quality < 0
                || item.Stack <= 0)
                return false;
            cargoItems += item.Stack;
        }
        if (cargoItems > int.MaxValue || snapshot.CargoCount != cargoItems)
            return false;

        HashSet<string> completedIds = new(StringComparer.Ordinal);
        return snapshot.CompletedTransferIds.All(transferId =>
            Guid.TryParseExact(transferId, "N", out _)
            && !cargoIds.Contains(transferId)
            && completedIds.Add(transferId));
    }
}
