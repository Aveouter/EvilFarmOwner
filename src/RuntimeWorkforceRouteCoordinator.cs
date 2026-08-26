using Microsoft.Xna.Framework;

namespace EvilFarmOwner;

/// <summary>Turns deterministic tile reservations into short movement gates for live NPC routes.</summary>
internal sealed class RuntimeWorkforceRouteCoordinator
{
    private const int TicksPerTile = 32;
    private const int MaximumWaitTicks = 3600;
    private readonly DeterministicWorkforceRouteLedger Ledger = new();
    private readonly Dictionary<string, MovementGate> Gates = new(StringComparer.Ordinal);
    private int CurrentSlot;
    private long AssignmentSequence;

    public bool TryReserve(NpcWorkLease lease, Stack<Point> path)
    {
        string workerId = lease.Worker.Name;
        string location = lease.Worker.currentLocation.NameOrUniqueName;
        this.ReleaseWorker(workerId);
        List<WorkforceRouteTile> expanded = new()
        {
            new(location, lease.Worker.TilePoint.X, lease.Worker.TilePoint.Y)
        };
        foreach (Point tile in path)
        {
            for (int tick = 0; tick < TicksPerTile; tick++)
                expanded.Add(new WorkforceRouteTile(location, tile.X, tile.Y));
        }

        string assignmentId = $"route-{++this.AssignmentSequence}";
        WorkforceRouteReservationResult reservation = this.Ledger.ReserveBatch(
            new[]
            {
                new WorkforceRouteProposal(
                    workerId,
                    assignmentId,
                    this.CurrentSlot,
                    expanded)
            },
            MaximumWaitTicks)[0];
        if (!reservation.Accepted)
            return false;

        int readyAt = this.CurrentSlot + reservation.WaitSlots;
        this.Gates[workerId] = new MovementGate(lease, readyAt);
        lease.SetRoutePaused(readyAt > this.CurrentSlot);
        return true;
    }

    public void Tick()
    {
        this.CurrentSlot++;
        this.Ledger.AdvanceCommittedThrough(this.CurrentSlot - 1);
        foreach ((string workerId, MovementGate gate) in this.Gates.ToArray())
        {
            if (this.CurrentSlot < gate.ReadyAtSlot)
                continue;
            gate.Lease.SetRoutePaused(false);
            this.Gates.Remove(workerId);
        }
    }

    public bool IsWaiting(string workerId) =>
        this.Gates.TryGetValue(workerId, out MovementGate? gate)
        && this.CurrentSlot < gate.ReadyAtSlot;

    public void ReleaseWorker(string workerId)
    {
        this.Ledger.ReleaseUncommitted(workerId);
        if (this.Gates.Remove(workerId, out MovementGate? gate))
            gate.Lease.SetRoutePaused(false);
    }

    private sealed record MovementGate(NpcWorkLease Lease, int ReadyAtSlot);
}
