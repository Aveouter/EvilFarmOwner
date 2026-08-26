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
        int index = 0;
        foreach (FarmWorkStageSelection stage in StageOrder.Where(
                     stage => enabledStages.HasFlag(stage)))
        {
            result[index % workerCount] |= stage;
            index++;
        }
        return result;
    }
}
