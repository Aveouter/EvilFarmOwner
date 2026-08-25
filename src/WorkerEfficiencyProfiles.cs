namespace EvilFarmOwner;

internal enum WorkerEfficiencyBackground
{
    Baseline,
    Gardening,
    Ranching,
    OutdoorGathering,
    ManualFieldwork,
    FieldScience
}

internal sealed record WorkerEfficiencyProfile(
    string WorkerName,
    decimal WateringMultiplier,
    decimal HarvestingMultiplier,
    WorkerEfficiencyBackground Background)
{
    public decimal GetMultiplier(NamedFarmTask task)
    {
        return task switch
        {
            NamedFarmTask.FarmWork => Math.Max(this.WateringMultiplier, this.HarvestingMultiplier),
            NamedFarmTask.Watering => this.WateringMultiplier,
            NamedFarmTask.Harvesting => this.HarvestingMultiplier,
            _ => WorkerEfficiencyProfiles.BaselineMultiplier
        };
    }
}

internal static class WorkerEfficiencyProfiles
{
    internal const decimal BaselineMultiplier = 1.00m;
    internal const decimal MinimumSupportedMultiplier = 1.00m;
    internal const decimal MaximumSupportedMultiplier = 1.10m;

    private static readonly IReadOnlyDictionary<string, WorkerEfficiencyProfile> Profiles =
        CreateProfiles().ToDictionary(profile => profile.WorkerName, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<WorkerEfficiencyProfile> GetExplicitProfiles()
    {
        return Profiles.Values.ToArray();
    }

    public static bool HasExplicitProfile(string workerName)
    {
        return !string.IsNullOrWhiteSpace(workerName) && Profiles.ContainsKey(workerName);
    }

    public static WorkerEfficiencyProfile GetProfile(string workerName)
    {
        if (!string.IsNullOrWhiteSpace(workerName)
            && Profiles.TryGetValue(workerName, out WorkerEfficiencyProfile? profile))
            return profile;

        return new WorkerEfficiencyProfile(
            string.IsNullOrWhiteSpace(workerName) ? "Unknown" : workerName,
            BaselineMultiplier,
            BaselineMultiplier,
            WorkerEfficiencyBackground.Baseline);
    }

    public static bool IsValidMultiplier(decimal multiplier)
    {
        return multiplier >= MinimumSupportedMultiplier
            && multiplier <= MaximumSupportedMultiplier;
    }

    private static IEnumerable<WorkerEfficiencyProfile> CreateProfiles()
    {
        yield return Baseline("Abigail");
        yield return ManualFieldwork("Alex");
        yield return Gardening("Caroline");
        yield return ManualFieldwork("Clint");
        yield return FieldScience("Demetrius");
        yield return Baseline("Elliott");
        yield return Baseline("Emily");
        yield return Gardening("Evelyn");
        yield return Baseline("George");
        yield return Baseline("Gus");
        yield return Baseline("Haley");
        yield return Baseline("Harvey");
        yield return Baseline("Jodi");
        yield return ManualFieldwork("Kent");
        yield return OutdoorGathering("Leah");
        yield return Baseline("Lewis");
        yield return OutdoorGathering("Linus");
        yield return Ranching("Marnie");
        yield return FieldScience("Maru");
        yield return Baseline("Pam");
        yield return Baseline("Penny");
        yield return Baseline("Pierre");
        yield return ManualFieldwork("Robin");
        yield return Baseline("Sam");
        yield return Baseline("Sebastian");
        yield return Ranching("Shane");
        yield return ManualFieldwork("Willy");
    }

    private static WorkerEfficiencyProfile Baseline(string workerName)
    {
        return new WorkerEfficiencyProfile(
            workerName,
            BaselineMultiplier,
            BaselineMultiplier,
            WorkerEfficiencyBackground.Baseline);
    }

    private static WorkerEfficiencyProfile Gardening(string workerName)
    {
        return new WorkerEfficiencyProfile(workerName, 1.10m, 1.10m, WorkerEfficiencyBackground.Gardening);
    }

    private static WorkerEfficiencyProfile Ranching(string workerName)
    {
        return new WorkerEfficiencyProfile(workerName, 1.10m, 1.10m, WorkerEfficiencyBackground.Ranching);
    }

    private static WorkerEfficiencyProfile OutdoorGathering(string workerName)
    {
        return new WorkerEfficiencyProfile(workerName, 1.05m, 1.10m, WorkerEfficiencyBackground.OutdoorGathering);
    }

    private static WorkerEfficiencyProfile ManualFieldwork(string workerName)
    {
        return new WorkerEfficiencyProfile(workerName, 1.10m, 1.05m, WorkerEfficiencyBackground.ManualFieldwork);
    }

    private static WorkerEfficiencyProfile FieldScience(string workerName)
    {
        return new WorkerEfficiencyProfile(workerName, 1.05m, 1.00m, WorkerEfficiencyBackground.FieldScience);
    }
}

internal static class WorkerEfficiencyTiming
{
    public static int GetActionDurationTicks(
        int baseDurationTicks,
        int actionStartTicks,
        decimal efficiencyMultiplier)
    {
        if (baseDurationTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(baseDurationTicks));
        if (actionStartTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(actionStartTicks));

        decimal normalizedMultiplier = WorkerEfficiencyProfiles.IsValidMultiplier(efficiencyMultiplier)
            ? efficiencyMultiplier
            : WorkerEfficiencyProfiles.BaselineMultiplier;
        int adjustedDuration = (int)Math.Ceiling(baseDurationTicks / normalizedMultiplier);
        return Math.Max(actionStartTicks + 1, adjustedDuration);
    }
}
