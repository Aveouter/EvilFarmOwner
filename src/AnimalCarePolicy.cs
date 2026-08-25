namespace EvilFarmOwner;

internal enum AnimalCareSkipReason
{
    None,
    NotHost,
    AlreadyPet,
    Sleeping,
    Baby,
    NoProduce,
    WrongHarvestType,
    MissingHarvestTool,
    AutoGrabberOwned
}

internal sealed record AnimalProducePlan(
    string QualifiedItemId,
    int Stack,
    int Quality,
    string RequiredTool);

internal static class AnimalPettingPolicy
{
    public static AnimalCareSkipReason GetSkipReason(
        bool isHost,
        bool wasPet,
        bool isSleeping)
    {
        if (!isHost)
            return AnimalCareSkipReason.NotHost;
        if (wasPet)
            return AnimalCareSkipReason.AlreadyPet;
        if (isSleeping)
            return AnimalCareSkipReason.Sleeping;
        return AnimalCareSkipReason.None;
    }
}

internal static class AnimalFeedingPolicy
{
    public static int GetFillCount(int emptyTroughTiles, int ownedHay)
    {
        if (emptyTroughTiles < 0)
            throw new ArgumentOutOfRangeException(nameof(emptyTroughTiles));
        if (ownedHay < 0)
            throw new ArgumentOutOfRangeException(nameof(ownedHay));
        return Math.Min(emptyTroughTiles, ownedHay);
    }
}

internal static class AnimalProducePolicy
{
    public static AnimalCareSkipReason TryCreateToolHarvestPlan(
        bool isHost,
        bool isAdult,
        string? currentProduceId,
        bool harvestsWithTool,
        string? harvestTool,
        bool hasEatenAnimalCracker,
        int quality,
        bool autoGrabberOwnsProduce,
        out AnimalProducePlan? plan)
    {
        plan = null;
        if (!isHost)
            return AnimalCareSkipReason.NotHost;
        if (autoGrabberOwnsProduce)
            return AnimalCareSkipReason.AutoGrabberOwned;
        if (!isAdult)
            return AnimalCareSkipReason.Baby;
        if (string.IsNullOrWhiteSpace(currentProduceId))
            return AnimalCareSkipReason.NoProduce;
        if (!harvestsWithTool)
            return AnimalCareSkipReason.WrongHarvestType;
        if (string.IsNullOrWhiteSpace(harvestTool))
            return AnimalCareSkipReason.MissingHarvestTool;
        if (quality < 0)
            throw new ArgumentOutOfRangeException(nameof(quality));

        string qualifiedId = currentProduceId.StartsWith("(", StringComparison.Ordinal)
            ? currentProduceId
            : $"(O){currentProduceId}";
        plan = new AnimalProducePlan(
            qualifiedId,
            hasEatenAnimalCracker ? 2 : 1,
            quality,
            harvestTool);
        return AnimalCareSkipReason.None;
    }
}
