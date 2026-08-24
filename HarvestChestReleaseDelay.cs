namespace EvilFarmOwner;

/// <summary>
/// Prevents a chest mutex from being reacquired from inside its own release callback.
/// </summary>
internal static class HarvestChestReleaseDelay
{
    private const int MinimumElapsedTicks = 1;

    public static bool CanContinue(int elapsedTicks)
    {
        return elapsedTicks >= MinimumElapsedTicks;
    }
}
