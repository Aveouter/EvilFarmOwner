namespace EvilFarmOwner;

internal enum TravelInterruptionKind
{
    Timeout,
    ProgressStall,
    ControllerEnded,
    ControllerReplaced,
    FirstStepRejected,
    ControllerSetupFailed
}

internal enum TravelControllerState
{
    None,
    Attached,
    Replaced
}

internal readonly record struct TravelBlockedTile(string LocationKey, GridPoint Tile);

internal readonly record struct TravelBlockedEdge(
    string LocationKey,
    GridPoint From,
    GridPoint To);

internal readonly record struct TravelObstacleSelection(
    TravelBlockedTile? Tile,
    TravelBlockedEdge? Edge)
{
    public bool HasValue => this.Tile.HasValue || this.Edge.HasValue;
}

internal sealed class TravelObstacleLedger
{
    private readonly HashSet<TravelBlockedTile> Tiles = new();
    private readonly HashSet<TravelBlockedEdge> Edges = new();

    public IReadOnlyCollection<TravelBlockedTile> BlockedTiles => this.Tiles;
    public IReadOnlyCollection<TravelBlockedEdge> BlockedEdges => this.Edges;

    public bool Add(TravelObstacleSelection selection)
    {
        if (selection.Tile is { } tile)
            return this.Tiles.Add(tile);
        if (selection.Edge is { } edge)
            return this.Edges.Add(edge);
        return false;
    }

    public bool IsTileBlocked(string locationKey, GridPoint tile)
    {
        return this.Tiles.Contains(new TravelBlockedTile(locationKey, tile));
    }

    public bool IsEdgeBlocked(string locationKey, GridPoint from, GridPoint to)
    {
        return this.Edges.Contains(new TravelBlockedEdge(locationKey, from, to));
    }

    public void Clear()
    {
        this.Tiles.Clear();
        this.Edges.Clear();
    }
}

internal readonly record struct TravelFailureDecision(
    string RouteKey,
    int FailureCount,
    int MaximumFailures)
{
    public bool CanRetry => this.FailureCount < this.MaximumFailures;
}

internal sealed class TravelFailureLedger
{
    private readonly int MaximumFailures;
    private readonly Dictionary<string, int> Failures = new(StringComparer.Ordinal);

    public TravelFailureLedger(int maximumFailures = 3)
    {
        if (maximumFailures <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumFailures));
        this.MaximumFailures = maximumFailures;
    }

    public TravelFailureDecision Record(string routeKey)
    {
        if (string.IsNullOrWhiteSpace(routeKey))
            throw new ArgumentException("A route key is required.", nameof(routeKey));
        int count = this.Failures.TryGetValue(routeKey, out int existing)
            ? existing + 1
            : 1;
        this.Failures[routeKey] = count;
        return new TravelFailureDecision(routeKey, count, this.MaximumFailures);
    }

    public void Reset(string routeKey)
    {
        this.Failures.Remove(routeKey);
    }
}

internal readonly record struct TravelInterruptionSnapshot(
    TravelInterruptionKind Kind,
    string LocationKey,
    GridPoint Origin,
    float PixelX,
    float PixelY,
    GridPoint? Destination,
    int RemainingWaypoints,
    GridPoint? NextWaypoint,
    GridPoint? PreviousProgressTile,
    TravelControllerState ControllerState,
    string CollisionProbe)
{
    public string ReasonTranslationKey => this.Kind switch
    {
        TravelInterruptionKind.Timeout => "route-reason.timeout",
        TravelInterruptionKind.ProgressStall => "route-reason.progress-stall",
        TravelInterruptionKind.ControllerEnded => "route-reason.controller-ended",
        TravelInterruptionKind.ControllerReplaced => "route-reason.controller-replaced",
        TravelInterruptionKind.FirstStepRejected => "route-reason.first-step",
        TravelInterruptionKind.ControllerSetupFailed => "route-reason.controller-setup",
        _ => "route-reason.unknown"
    };

    public string ToTechnicalReason()
    {
        return $"kind={this.Kind}, location={this.LocationKey}, tile={this.Origin}, "
            + $"pixel=({this.PixelX:0.##},{this.PixelY:0.##}), "
            + $"destination={this.Destination?.ToString() ?? "-"}, "
            + $"remainingWaypoints={this.RemainingWaypoints}, "
            + $"next={this.NextWaypoint?.ToString() ?? "-"}, "
            + $"previousProgressTile={this.PreviousProgressTile?.ToString() ?? "-"}, "
            + $"controller={this.ControllerState}, liveProbe={this.CollisionProbe}";
    }
}

internal static class TravelRouteExclusionPolicy
{
    public static TravelObstacleSelection Select(
        string locationKey,
        GridPoint currentTile,
        GridPoint? previousProgressTile,
        GridPoint? nextWaypoint)
    {
        if (string.IsNullOrWhiteSpace(locationKey))
            throw new ArgumentException("A location key is required.", nameof(locationKey));
        if (!nextWaypoint.HasValue)
            return default;

        GridPoint next = nextWaypoint.Value;
        int nextDistance = ManhattanDistance(currentTile, next);
        if (nextDistance == 1)
        {
            return new TravelObstacleSelection(
                new TravelBlockedTile(locationKey, next),
                null);
        }

        if (next == currentTile
            && previousProgressTile is { } previous
            && ManhattanDistance(previous, currentTile) == 1)
        {
            return new TravelObstacleSelection(
                null,
                new TravelBlockedEdge(locationKey, previous, currentTile));
        }

        return default;
    }

    private static int ManhattanDistance(GridPoint left, GridPoint right)
    {
        return Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);
    }
}

internal static class DeliveryRouteExclusionPolicy
{
    public static bool TrySelectFailedTile(
        GridPoint currentTile,
        GridPoint? nextWaypoint,
        out GridPoint failedTile)
    {
        failedTile = default;
        if (!nextWaypoint.HasValue)
            return false;

        TravelObstacleSelection selection = TravelRouteExclusionPolicy.Select(
            "legacy-delivery",
            currentTile,
            previousProgressTile: null,
            nextWaypoint);
        if (selection.Tile is not { } tile)
            return false;

        failedTile = tile.Tile;
        return true;
    }
}
