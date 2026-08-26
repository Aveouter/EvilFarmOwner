namespace EvilFarmOwner;

internal readonly record struct WorkforceRouteTile(int X, int Y);

internal sealed record WorkforceRouteProposal(
    string WorkerId,
    string AssignmentId,
    int RequestedStartSlot,
    IReadOnlyList<WorkforceRouteTile> Tiles);

internal enum WorkforceRouteReservationFailure
{
    None,
    WaitLimitExceeded
}

internal sealed record WorkforceRouteReservationResult(
    string WorkerId,
    string AssignmentId,
    bool Accepted,
    int StartSlot,
    int WaitSlots,
    WorkforceRouteReservationFailure Failure);

internal enum WorkforceRouteReservationState
{
    Reserved,
    Committed
}

internal sealed record WorkforceRouteReservationSnapshot(
    string WorkerId,
    string AssignmentId,
    int Slot,
    WorkforceRouteTile Tile,
    WorkforceRouteReservationState State);

/// <summary>Host-owned, shift-scoped tile and movement-edge reservations.</summary>
internal sealed class DeterministicWorkforceRouteLedger
{
    private readonly Dictionary<TileSlot, ReservationOwner> TileOwners = new();
    private readonly Dictionary<EdgeSlot, ReservationOwner> EdgeOwners = new();
    private int CommittedThroughSlot = -1;

    public IReadOnlyList<WorkforceRouteReservationResult> ReserveBatch(
        IEnumerable<WorkforceRouteProposal> proposals,
        int maximumWaitSlots)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        if (maximumWaitSlots < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumWaitSlots));

        WorkforceRouteProposal[] ordered = proposals
            .Select(Validate)
            .OrderBy(proposal => proposal.RequestedStartSlot)
            .ThenBy(proposal => proposal.WorkerId, StringComparer.Ordinal)
            .ThenBy(proposal => proposal.AssignmentId, StringComparer.Ordinal)
            .ToArray();
        if (ordered
            .Select(proposal => (proposal.WorkerId, proposal.AssignmentId))
            .Distinct()
            .Count() != ordered.Length)
        {
            throw new ArgumentException(
                "A route batch cannot contain the same worker assignment twice.",
                nameof(proposals));
        }

        List<WorkforceRouteReservationResult> results = new(ordered.Length);
        foreach (WorkforceRouteProposal proposal in ordered)
            results.Add(this.ReserveOne(proposal, maximumWaitSlots));
        return results;
    }

    public void AdvanceCommittedThrough(int slot)
    {
        if (slot < this.CommittedThroughSlot)
            throw new ArgumentOutOfRangeException(nameof(slot), "The host route clock cannot move backwards.");
        this.CommittedThroughSlot = slot;
    }

    public int ReleaseUncommitted(string workerId)
    {
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Worker ID is required.", nameof(workerId));

        TileSlot[] releasedTiles = this.TileOwners
            .Where(pair => pair.Value.WorkerId == workerId && pair.Key.Slot > this.CommittedThroughSlot)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (TileSlot tile in releasedTiles)
            this.TileOwners.Remove(tile);

        EdgeSlot[] releasedEdges = this.EdgeOwners
            .Where(pair => pair.Value.WorkerId == workerId && pair.Key.Slot > this.CommittedThroughSlot)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (EdgeSlot edge in releasedEdges)
            this.EdgeOwners.Remove(edge);

        return releasedTiles.Length;
    }

    public IReadOnlyList<WorkforceRouteReservationSnapshot> Snapshot()
    {
        return this.TileOwners
            .OrderBy(pair => pair.Key.Slot)
            .ThenBy(pair => pair.Key.Tile.X)
            .ThenBy(pair => pair.Key.Tile.Y)
            .ThenBy(pair => pair.Value.WorkerId, StringComparer.Ordinal)
            .Select(pair => new WorkforceRouteReservationSnapshot(
                pair.Value.WorkerId,
                pair.Value.AssignmentId,
                pair.Key.Slot,
                pair.Key.Tile,
                pair.Key.Slot <= this.CommittedThroughSlot
                    ? WorkforceRouteReservationState.Committed
                    : WorkforceRouteReservationState.Reserved))
            .ToArray();
    }

    private WorkforceRouteReservationResult ReserveOne(
        WorkforceRouteProposal proposal,
        int maximumWaitSlots)
    {
        for (int wait = 0; wait <= maximumWaitSlots; wait++)
        {
            int startSlot = checked(proposal.RequestedStartSlot + wait);
            if (!this.CanReserve(proposal, startSlot))
                continue;

            this.CommitReservation(proposal, startSlot);
            return new(
                proposal.WorkerId,
                proposal.AssignmentId,
                true,
                startSlot,
                wait,
                WorkforceRouteReservationFailure.None);
        }

        return new(
            proposal.WorkerId,
            proposal.AssignmentId,
            false,
            proposal.RequestedStartSlot,
            maximumWaitSlots,
            WorkforceRouteReservationFailure.WaitLimitExceeded);
    }

    private bool CanReserve(WorkforceRouteProposal proposal, int startSlot)
    {
        for (int step = 0; step < proposal.Tiles.Count; step++)
        {
            int slot = checked(startSlot + step);
            WorkforceRouteTile tile = proposal.Tiles[step];
            if (slot <= this.CommittedThroughSlot
                || this.TileOwners.ContainsKey(new TileSlot(slot, tile)))
            {
                return false;
            }

            if (step == 0)
                continue;

            WorkforceRouteTile from = proposal.Tiles[step - 1];
            EdgeSlot opposing = new(slot, tile, from);
            if (this.EdgeOwners.ContainsKey(opposing))
                return false;
        }
        return true;
    }

    private void CommitReservation(WorkforceRouteProposal proposal, int startSlot)
    {
        ReservationOwner owner = new(proposal.WorkerId, proposal.AssignmentId);
        for (int step = 0; step < proposal.Tiles.Count; step++)
        {
            int slot = checked(startSlot + step);
            WorkforceRouteTile tile = proposal.Tiles[step];
            this.TileOwners.Add(new TileSlot(slot, tile), owner);
            if (step > 0)
            {
                WorkforceRouteTile from = proposal.Tiles[step - 1];
                this.EdgeOwners.Add(new EdgeSlot(slot, from, tile), owner);
            }
        }
    }

    private static WorkforceRouteProposal Validate(WorkforceRouteProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (string.IsNullOrWhiteSpace(proposal.WorkerId)
            || string.IsNullOrWhiteSpace(proposal.AssignmentId)
            || proposal.RequestedStartSlot < 0
            || proposal.Tiles is null
            || proposal.Tiles.Count == 0)
        {
            throw new ArgumentException(
                "Every route requires worker and assignment IDs, a non-negative slot, and at least one tile.",
                nameof(proposal));
        }
        return proposal;
    }

    private readonly record struct TileSlot(int Slot, WorkforceRouteTile Tile);

    private readonly record struct EdgeSlot(
        int Slot,
        WorkforceRouteTile From,
        WorkforceRouteTile To);

    private sealed record ReservationOwner(string WorkerId, string AssignmentId);
}
