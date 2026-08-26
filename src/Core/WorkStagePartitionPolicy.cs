namespace EvilFarmOwner;

internal static class WorkStagePartitionPolicy
{
    private static readonly FarmWorkStageSelection[] StageOrder =
    {
        FarmWorkStageSelection.Harvesting,
        FarmWorkStageSelection.Watering,
        FarmWorkStageSelection.AnimalCare,
        FarmWorkStageSelection.StorageSorting
    };

    public static IReadOnlyList<FarmWorkStageSelection> Partition(
        FarmWorkStageSelection enabledStages,
        int workerCount)
    {
        if (workerCount <= 0)
            return Array.Empty<FarmWorkStageSelection>();

        FarmWorkStageSelection[] result = Enumerable
            .Repeat(FarmWorkStageSelection.None, workerCount)
            .ToArray();

        FarmWorkStageSelection parallelStages = enabledStages
            & (FarmWorkStageSelection.Harvesting | FarmWorkStageSelection.Watering);
        if (parallelStages != FarmWorkStageSelection.None)
        {
            for (int worker = 0; worker < result.Length; worker++)
                result[worker] |= parallelStages;
        }

        int index = 0;
        foreach (FarmWorkStageSelection stage in new[]
                 {
                     FarmWorkStageSelection.AnimalCare,
                     FarmWorkStageSelection.StorageSorting
                 }.Where(stage => enabledStages.HasFlag(stage)))
        {
            result[index % workerCount] |= stage;
            index++;
        }
        return result;
    }

    public static int CountEnabled(FarmWorkStageSelection enabledStages) =>
        StageOrder.Count(stage => enabledStages.HasFlag(stage));

    public static int GetMaximumUsefulWorkerCount(
        FarmWorkStageSelection enabledStages,
        int configuredMaximum)
    {
        int limit = ContractSettingsPolicy.NormalizeMaximumConcurrentWorkers(configuredMaximum);
        bool hasParallelStage = enabledStages.HasFlag(FarmWorkStageSelection.Harvesting)
            || enabledStages.HasFlag(FarmWorkStageSelection.Watering);
        if (hasParallelStage)
            return limit;

        int exclusiveStages = new[]
        {
            FarmWorkStageSelection.AnimalCare,
            FarmWorkStageSelection.StorageSorting
        }.Count(stage => enabledStages.HasFlag(stage));
        return Math.Min(limit, exclusiveStages);
    }
}
