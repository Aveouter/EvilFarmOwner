using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;

namespace EvilFarmOwner;

internal sealed class NpcWorkLease
{
    private const string LeaseDataKey = "Aveouter.EvilFarmOwner/WorkLease";

    private readonly IMonitor Monitor;
    private readonly HashSet<PathFindController> OwnedControllers = new();
    private readonly GameLocation OriginalLocation;
    private readonly Vector2 OriginalPosition;
    private readonly int OriginalFacingDirection;
    private readonly int OriginalSpeed;
    private readonly float OriginalAddedSpeed;
    private readonly int OriginalBlockedInterval;
    private readonly bool OriginalIsCharging;
    private readonly bool OriginalWillDestroyObjectsUnderfoot;
    private readonly bool ResumeScheduleOnRestore;
    private readonly string Token;
    private NpcLeaseRestoreResult? FinalRestoreResult;
    private bool ConflictReported;
    private bool ScheduleResumeAttempted;

    private NpcWorkLease(
        NPC worker,
        int reservedWage,
        IMonitor monitor,
        bool resumeScheduleOnRestore)
    {
        this.Worker = worker;
        this.ReservedWage = reservedWage;
        this.Monitor = monitor;
        this.OriginalLocation = worker.currentLocation;
        this.OriginalPosition = worker.Position;
        this.OriginalFacingDirection = worker.FacingDirection;
        this.OriginalSpeed = worker.speed;
        this.OriginalAddedSpeed = worker.addedSpeed;
        this.OriginalBlockedInterval = worker.blockedInterval;
        this.OriginalIsCharging = worker.isCharging;
        this.OriginalWillDestroyObjectsUnderfoot = worker.willDestroyObjectsUnderfoot;
        this.ResumeScheduleOnRestore = resumeScheduleOnRestore;
        this.StartTime = Game1.timeOfDay;
        this.StartTotalDays = Game1.Date.TotalDays;
        this.Token = Guid.NewGuid().ToString("N");

        worker.modData[LeaseDataKey] = this.Token;
        worker.willDestroyObjectsUnderfoot = false;
        worker.isCharging = false;
        worker.blockedInterval = 0;
    }

    public NPC Worker { get; }

    public int ReservedWage { get; }

    public int StartTime { get; }

    public int StartTotalDays { get; }

    public static bool IsLeasedWorker(NPC npc) => npc.modData.ContainsKey(LeaseDataKey);

    public void SetRoutePaused(bool paused)
    {
        this.Worker.speed = paused ? 0 : this.OriginalSpeed;
        this.Worker.addedSpeed = paused ? 0f : this.OriginalAddedSpeed;
        if (paused)
            this.Worker.Halt();
    }

    public static bool TryAcquire(
        NPC worker,
        int reservedWage,
        IMonitor monitor,
        out NpcWorkLease? lease,
        bool resumeScheduleOnRestore = true)
    {
        lease = null;

        if (worker.currentLocation is null
            || !worker.currentLocation.characters.Contains(worker)
            || !worker.currentLocation.isTileOnMap(worker.Tile)
            || worker.controller is not null
            || worker.temporaryController is not null
            || worker.modData.ContainsKey(LeaseDataKey))
            return false;

        lease = new NpcWorkLease(worker, reservedWage, monitor, resumeScheduleOnRestore);
        return true;
    }

    public void AttachController(PathFindController controller)
    {
        if (!controller.nonDestructivePathing)
            throw new InvalidOperationException("Work-lease controllers must use non-destructive pathing.");

        this.OwnedControllers.Add(controller);
        this.Worker.controller = controller;
    }

    public bool IsCurrentController(PathFindController controller)
    {
        return ReferenceEquals(this.Worker.controller, controller);
    }

    public NpcLeaseRestoreResult Restore()
    {
        if (this.FinalRestoreResult is { } finalResult)
            return finalResult;

        if (!this.Worker.modData.TryGetValue(LeaseDataKey, out string? token)
            || !string.Equals(token, this.Token, StringComparison.Ordinal))
        {
            this.Monitor.Log(
                $"Could not restore worker '{this.Worker.Name}' because the work-lease marker is no longer owned by this contract.",
                LogLevel.Error);
            this.FinalRestoreResult = NpcLeaseRestoreResult.LeaseOwnershipLost;
            return NpcLeaseRestoreResult.LeaseOwnershipLost;
        }

        if ((this.Worker.controller is not null && !this.OwnedControllers.Contains(this.Worker.controller))
            || this.Worker.temporaryController is not null)
        {
            if (!this.ConflictReported)
            {
                this.ConflictReported = true;
                this.Monitor.Log(
                    $"Deferring restoration of worker '{this.Worker.Name}' because another controller took control during the work lease.",
                    LogLevel.Warn);
            }
            return NpcLeaseRestoreResult.ConflictingController;
        }

        if (this.Worker.controller is not null)
            this.Worker.controller = null;

        this.Worker.Halt();
        this.Worker.Sprite?.ClearAnimation();
        Game1.warpCharacter(
            this.Worker,
            this.OriginalLocation,
            new Vector2(
                (int)Math.Floor(this.OriginalPosition.X / Game1.tileSize),
                (int)Math.Floor(this.OriginalPosition.Y / Game1.tileSize)));

        this.Worker.Position = this.OriginalPosition;
        this.Worker.faceDirection(this.OriginalFacingDirection);
        this.Worker.speed = this.OriginalSpeed;
        this.Worker.addedSpeed = this.OriginalAddedSpeed;
        this.Worker.blockedInterval = this.OriginalBlockedInterval;
        this.Worker.isCharging = this.OriginalIsCharging;
        this.Worker.willDestroyObjectsUnderfoot = this.OriginalWillDestroyObjectsUnderfoot;
        this.Worker.modData.Remove(LeaseDataKey);
        this.FinalRestoreResult = NpcLeaseRestoreResult.Restored;

        if (this.ResumeScheduleOnRestore)
            this.ResumeVanillaSchedule();

        return NpcLeaseRestoreResult.Restored;
    }

    /// <summary>Resume the vanilla schedule after a concurrent group no longer needs this worker.</summary>
    public void ResumeVanillaSchedule()
    {
        if (this.ScheduleResumeAttempted)
            return;
        this.ScheduleResumeAttempted = true;

        if (this.FinalRestoreResult != NpcLeaseRestoreResult.Restored
            || !Context.IsWorldReady
            || Game1.Date.TotalDays != this.StartTotalDays
            || this.Worker.modData.ContainsKey(LeaseDataKey)
            || this.Worker.controller is not null
            || this.Worker.temporaryController is not null)
            return;

        try
        {
            this.Worker.checkSchedule(Game1.timeOfDay);
        }
        catch (Exception ex)
        {
            this.Monitor.Log(
                $"Worker '{this.Worker.Name}' was restored, but resuming the vanilla schedule failed: {ex.Message}",
                LogLevel.Warn);
        }
    }

    public NpcLeaseRestoreResult RelinquishToConflictingController()
    {
        if (this.FinalRestoreResult is { } finalResult)
            return finalResult;

        if (!this.Worker.modData.TryGetValue(LeaseDataKey, out string? token)
            || !string.Equals(token, this.Token, StringComparison.Ordinal))
        {
            this.Monitor.Log(
                $"Could not relinquish worker '{this.Worker.Name}' because the work-lease marker is no longer owned by this contract.",
                LogLevel.Error);
            this.FinalRestoreResult = NpcLeaseRestoreResult.LeaseOwnershipLost;
            return NpcLeaseRestoreResult.LeaseOwnershipLost;
        }

        if (this.Worker.controller is not null && this.OwnedControllers.Contains(this.Worker.controller))
            this.Worker.controller = null;

        // Restore only fields changed when this lease was acquired. Do not halt, warp,
        // clear animation, or replace a controller now owned by another activity.
        this.Worker.blockedInterval = this.OriginalBlockedInterval;
        this.Worker.isCharging = this.OriginalIsCharging;
        this.Worker.willDestroyObjectsUnderfoot = this.OriginalWillDestroyObjectsUnderfoot;
        this.Worker.modData.Remove(LeaseDataKey);
        this.FinalRestoreResult = NpcLeaseRestoreResult.Relinquished;
        this.Monitor.Log(
            $"Relinquished work lease for '{this.Worker.Name}' without overriding the conflicting controller; "
            + "the other activity now owns NPC position and movement.",
            LogLevel.Warn);
        return NpcLeaseRestoreResult.Relinquished;
    }
}
