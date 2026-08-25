namespace EvilFarmOwner;

internal enum TargetRouteFailureAction
{
    RetryRoute,
    SkipTarget,
    StopAtOrigin
}

internal readonly record struct TargetRouteFailureDecision(
    TargetRouteFailureAction Action,
    int RouteFailureCount,
    int MaximumRouteFailures,
    int StalledTargetCount,
    int MaximumStalledTargets);

/// <summary>
/// Isolates one crop whose live interaction routes have failed without letting a
/// dynamically blocked origin retry every crop forever.
/// </summary>
internal static class TargetRouteFailurePolicy
{
    public static TargetRouteFailureDecision RecordFailure(
        TravelReplanBudget budget,
        GridPoint origin)
    {
        TravelReplanDecision route = budget.RecordFailure(
            TravelRoutePurpose.Target,
            origin);
        if (route.CanReplan)
        {
            return new TargetRouteFailureDecision(
                TargetRouteFailureAction.RetryRoute,
                route.FailureCount,
                route.MaximumFailures,
                0,
                route.MaximumFailures);
        }

        budget.Reset(TravelRoutePurpose.Target);
        TravelReplanDecision skippedTarget = budget.RecordFailure(
            TravelRoutePurpose.TargetSkip,
            origin);
        return new TargetRouteFailureDecision(
            skippedTarget.CanReplan
                ? TargetRouteFailureAction.SkipTarget
                : TargetRouteFailureAction.StopAtOrigin,
            route.FailureCount,
            route.MaximumFailures,
            skippedTarget.FailureCount,
            skippedTarget.MaximumFailures);
    }

    public static void ResetAfterArrival(TravelReplanBudget budget)
    {
        budget.Reset(TravelRoutePurpose.Target);
        budget.Reset(TravelRoutePurpose.TargetSkip);
    }
}
