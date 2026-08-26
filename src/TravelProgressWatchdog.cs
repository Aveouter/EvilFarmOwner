namespace EvilFarmOwner;

/// <summary>Detects a controller which remains at the same pixel position for too long.</summary>
internal sealed class TravelProgressWatchdog
{
    private const float MinimumProgressSquared = 1f;
    private float LastX;
    private float LastY;
    private int StalledTicks;
    private bool Initialized;

    public GridPoint? LastProgressTile { get; private set; }
    public GridPoint? PreviousProgressTile { get; private set; }

    public void Reset(float x, float y)
    {
        this.LastX = x;
        this.LastY = y;
        this.StalledTicks = 0;
        this.Initialized = true;
        this.LastProgressTile = null;
        this.PreviousProgressTile = null;
    }

    public void Reset(float x, float y, GridPoint tile)
    {
        this.Reset(x, y);
        this.LastProgressTile = tile;
        this.PreviousProgressTile = null;
    }

    public bool Tick(float x, float y, int maximumStalledTicks)
    {
        float xDelta = x - this.LastX;
        float yDelta = y - this.LastY;
        if (!this.Initialized
            || xDelta * xDelta + yDelta * yDelta >= MinimumProgressSquared)
        {
            this.Reset(x, y);
            return false;
        }

        this.StalledTicks++;
        return this.StalledTicks >= maximumStalledTicks;
    }

    public bool Tick(float x, float y, GridPoint tile, int maximumStalledTicks)
    {
        float xDelta = x - this.LastX;
        float yDelta = y - this.LastY;
        if (!this.Initialized
            || xDelta * xDelta + yDelta * yDelta >= MinimumProgressSquared)
        {
            if (this.LastProgressTile is { } last && last != tile)
                this.PreviousProgressTile = last;
            this.LastProgressTile = tile;
            this.LastX = x;
            this.LastY = y;
            this.StalledTicks = 0;
            this.Initialized = true;
            return false;
        }

        this.StalledTicks++;
        return this.StalledTicks >= maximumStalledTicks;
    }
}
