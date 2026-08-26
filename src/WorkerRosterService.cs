using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;

namespace EvilFarmOwner;

internal sealed class WorkerRosterService
{
    private static readonly HashSet<string> KnownUnsupportedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dwarf", "Krobus", "Sandy", "Wizard"
    };

    private static readonly HashSet<string> MedicalLocationNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hospital", "BathHouse_Pool", "BathHouse_MensLocker", "BathHouse_WomensLocker"
    };

    private static readonly Dictionary<string, ProtectedWorkplace[]> ProtectedWorkplaces = new(StringComparer.OrdinalIgnoreCase)
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

    private readonly IMonitor Monitor;
    private readonly Func<ContractSettingsSnapshot> GetSettings;

    public WorkerRosterService(
        IMonitor monitor,
        Func<ContractSettingsSnapshot>? getSettings = null)
    {
        this.Monitor = monitor;
        this.GetSettings = getSettings ?? (() => ContractSettingsSnapshot.Default);
    }

    public IReadOnlyList<WorkerRosterEntry> GetRoster()
    {
        List<WorkerRosterEntry> entries = new();
        HashSet<string> seenNames = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (NPC npc in Utility.getAllCharacters())
            {
                if (!this.ShouldDisplay(npc) || !seenNames.Add(npc.Name))
                    continue;

                try
                {
                    WorkerAvailabilityResult availability = this.Evaluate(npc);
                    if (!WorkerRosterPolicy.ShouldDisplay(availability.State))
                        continue;

                    entries.Add(this.CreateEntry(npc, availability));
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Skipping NPC '{npc.Name}' whose roster row could not be created safely: {ex.Message}", LogLevel.Warn);
                }
            }
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not build the read-only worker roster: {ex}", LogLevel.Error);
        }

        return entries
            .OrderBy(entry => entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public bool TryGetWorker(
        string internalName,
        out NPC? worker,
        out WorkerAvailabilityResult availability)
    {
        worker = null;
        availability = Unavailable(WorkerAvailabilityReason.MissingLocation);

        try
        {
            worker = Utility.getAllCharacters()
                .FirstOrDefault(npc => string.Equals(npc.Name, internalName, StringComparison.OrdinalIgnoreCase));

            if (worker is null || !this.ShouldDisplay(worker))
            {
                worker = null;
                return false;
            }

            availability = this.Evaluate(worker);
            return true;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Could not resolve named worker '{internalName}' safely: {ex.Message}", LogLevel.Warn);
            availability = Unknown(WorkerAvailabilityReason.EvaluationFailed);
            worker = null;
            return false;
        }
    }

    private bool ShouldDisplay(NPC npc)
    {
        try
        {
            return npc.IsVillager
                && npc.CanSocialize
                && !npc.IsInvisible
                && !string.IsNullOrWhiteSpace(npc.Name)
                && npc.Portrait is not null;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Skipping NPC whose roster identity could not be read safely: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    private WorkerRosterEntry CreateEntry(NPC npc, WorkerAvailabilityResult availability)
    {
        string displayName = string.IsNullOrWhiteSpace(npc.displayName) ? npc.Name : npc.displayName;
        Texture2D portrait = npc.Portrait;
        int friendshipHearts = Game1.player.getFriendshipHeartLevelForNPC(npc.Name);
        WorkContractPreview wagePreview = ContractPreviewService.Create(
            friendshipHearts,
            Game1.dayOfMonth,
            npc.Name,
            NamedFarmTask.FarmWork,
            this.GetSettings());

        return new WorkerRosterEntry(
            npc.Name,
            displayName,
            portrait,
            availability,
            wagePreview);
    }

    public WorkerAvailabilityResult Evaluate(NPC npc)
    {
        try
        {
            if (npc.Age == NPC.child)
                return Ineligible(WorkerAvailabilityReason.Child);

            if (KnownUnsupportedNames.Contains(npc.Name))
                return Ineligible(WorkerAvailabilityReason.UnsupportedCharacter);

            if (!WorkerEfficiencyProfiles.HasExplicitProfile(npc.Name))
                return Unknown(WorkerAvailabilityReason.UnsupportedCustomNpc);

            if (Game1.isFestival())
                return Unavailable(WorkerAvailabilityReason.ActiveFestival);

            if (Game1.eventUp)
                return Unavailable(WorkerAvailabilityReason.ActiveEvent);

            if (npc.currentLocation is null)
                return Unavailable(WorkerAvailabilityReason.MissingLocation);

            if (npc.isSleeping.Value)
                return Unavailable(WorkerAvailabilityReason.Sleeping);

            string locationName = npc.currentLocation.NameOrUniqueName ?? npc.currentLocation.Name;
            if (locationName.StartsWith("Island", StringComparison.OrdinalIgnoreCase))
                return Unavailable(WorkerAvailabilityReason.IslandActivity);

            if (MedicalLocationNames.Contains(locationName))
                return Unavailable(WorkerAvailabilityReason.MedicalActivity);

            if (IsProtectedWorkActivity(npc.Name, locationName, Game1.timeOfDay))
                return Unavailable(WorkerAvailabilityReason.WorkActivity);

            if (npc.controller is not null || npc.temporaryController is not null)
                return Unavailable(WorkerAvailabilityReason.ControlledActivity);

            if (npc.isMoving())
                return Unavailable(WorkerAvailabilityReason.MovementActivity);

            if (npc.CurrentDialogue.Count > 0 && npc.CurrentDialogue.Peek().removeOnNextMove)
                return Unavailable(WorkerAvailabilityReason.DialogueActivity);

            if (NpcActivityPolicy.HasProtectedActivity(
                    npc.doingEndOfRouteAnimation.Value,
                    npc.goingToDoEndOfRouteAnimation.Value,
                    npc.IsWalkingInSquare,
                    npc.Sprite?.CurrentAnimation is not null,
                    npc.movementPause))
                return Unavailable(WorkerAvailabilityReason.ScriptedAnimation);

            return new WorkerAvailabilityResult(
                WorkerAvailabilityState.EligibleForPreview,
                WorkerAvailabilityReason.AvailableForPreview);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Availability evaluation failed closed for NPC '{npc.Name}': {ex.Message}", LogLevel.Warn);
            return Unknown(WorkerAvailabilityReason.EvaluationFailed);
        }
    }

    private static WorkerAvailabilityResult Unavailable(WorkerAvailabilityReason reason)
    {
        return new WorkerAvailabilityResult(WorkerAvailabilityState.TemporarilyUnavailable, reason);
    }

    private static WorkerAvailabilityResult Ineligible(WorkerAvailabilityReason reason)
    {
        return new WorkerAvailabilityResult(WorkerAvailabilityState.Ineligible, reason);
    }

    private static WorkerAvailabilityResult Unknown(WorkerAvailabilityReason reason)
    {
        return new WorkerAvailabilityResult(WorkerAvailabilityState.Unknown, reason);
    }

    private static bool IsProtectedWorkActivity(string npcName, string locationName, int timeOfDay)
    {
        return ProtectedWorkplaces.TryGetValue(npcName, out ProtectedWorkplace[]? workplaces)
            && workplaces.Any(workplace => workplace.LocationName.Equals(locationName, StringComparison.OrdinalIgnoreCase)
                && timeOfDay >= workplace.StartTime
                && timeOfDay < workplace.EndTime);
    }

    private sealed record ProtectedWorkplace(string LocationName, int StartTime, int EndTime);
}
