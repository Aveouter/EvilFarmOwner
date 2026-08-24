namespace EvilFarmOwner;

/// <summary>Fail-closed policy for vanilla NPC state that a work lease must not interrupt.</summary>
internal static class NpcActivityPolicy
{
    public static bool HasProtectedActivity(
        bool doingEndOfRouteAnimation,
        bool goingToDoEndOfRouteAnimation,
        bool isWalkingInSquare,
        bool hasSpriteAnimation,
        int movementPause)
    {
        return doingEndOfRouteAnimation
            || goingToDoEndOfRouteAnimation
            || isWalkingInSquare
            || hasSpriteAnimation
            || movementPause > 0;
    }
}
