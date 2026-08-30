using StardewValley;

namespace EvilFarmOwner;

/// <summary>Classifies protected vanilla jobs and worker-specific rest days without changing NPC schedules.</summary>
internal static class WorkerSchedulePolicy
{
    private static readonly Dictionary<string, ProtectedWorkplace[]> ProtectedWorkplaces =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Clint"] = new[] { new ProtectedWorkplace("Blacksmith", 900, 1700) },
            ["Emily"] = new[] { new ProtectedWorkplace("Saloon", 1600, 2600) },
            ["Gus"] = new[] { new ProtectedWorkplace("Saloon", 1200, 2600) },
            ["Marnie"] = new[] { new ProtectedWorkplace("AnimalShop", 900, 1700) },
            ["Pam"] = new[] { new ProtectedWorkplace("BusStop", 900, 1700) },
            ["Pierre"] = new[] { new ProtectedWorkplace("SeedShop", 900, 1800) },
            ["Robin"] = new[] { new ProtectedWorkplace("ScienceHouse", 900, 1700) },
            ["Sam"] = new[] { new ProtectedWorkplace("JojaMart", 900, 1700) },
            ["Willy"] = new[] { new ProtectedWorkplace("FishShop", 900, 1800) }
        };

    public static bool IsProtectedWorkActivity(string npcName, string locationName, int timeOfDay)
    {
        return ProtectedWorkplaces.TryGetValue(npcName, out ProtectedWorkplace[]? workplaces)
            && workplaces.Any(workplace => workplace.LocationName.Equals(
                    locationName,
                    StringComparison.OrdinalIgnoreCase)
                && timeOfDay >= workplace.StartTime
                && timeOfDay < workplace.EndTime);
    }

    public static bool IsRestDay(NPC npc, RestDayRule rule, int dayOfMonth)
    {
        if (rule != RestDayRule.NpcSchedule
            || !ProtectedWorkplaces.TryGetValue(npc.Name, out ProtectedWorkplace[]? workplaces))
        {
            return WorkerRestDayPolicy.IsRestDay(rule, dayOfMonth);
        }

        bool scheduledForWork = npc.Schedule?.Values.Any(step => workplaces.Any(workplace =>
            workplace.LocationName.Equals(
                step.targetLocationName,
                StringComparison.OrdinalIgnoreCase))) == true;
        return WorkerRestDayPolicy.IsRestDay(rule, dayOfMonth, !scheduledForWork);
    }

    private sealed record ProtectedWorkplace(string LocationName, int StartTime, int EndTime);
}
