namespace EvilFarmOwner;

internal static class ConfigValidator
{
    internal const int MinWorkRadius = 1;
    internal const int MaxWorkRadius = 256;
    internal const int MinDailyWage = 0;
    internal const int MaxDailyWage = 100_000_000;
    internal const int MinTilesPerJob = 1;
    internal const int MaxTilesPerJob = 10_000;

    public static IReadOnlyList<string> Normalize(ModConfig config)
    {
        List<string> warnings = new();

        int workRadius = Math.Clamp(config.WorkRadius, MinWorkRadius, MaxWorkRadius);
        if (workRadius != config.WorkRadius)
        {
            warnings.Add($"WorkRadius must be between {MinWorkRadius} and {MaxWorkRadius}; using {workRadius}.");
            config.WorkRadius = workRadius;
        }

        int dailyWage = Math.Clamp(config.DailyWage, MinDailyWage, MaxDailyWage);
        if (dailyWage != config.DailyWage)
        {
            warnings.Add($"DailyWage must be between {MinDailyWage} and {MaxDailyWage}; using {dailyWage}.");
            config.DailyWage = dailyWage;
        }

        int maxTilesPerJob = Math.Clamp(config.MaxTilesPerJob, MinTilesPerJob, MaxTilesPerJob);
        if (maxTilesPerJob != config.MaxTilesPerJob)
        {
            warnings.Add($"MaxTilesPerJob must be between {MinTilesPerJob} and {MaxTilesPerJob}; using {maxTilesPerJob}.");
            config.MaxTilesPerJob = maxTilesPerJob;
        }

        return warnings;
    }

    public static bool HasEnabledJobs(ModConfig config)
    {
        return config.WaterCrops
            || config.HarvestCrops
            || config.ClearDebris
            || config.FertilizeEmptyDirt
            || config.PlantSeedsFromInventory;
    }
}
