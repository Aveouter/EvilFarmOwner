using Microsoft.Xna.Framework.Graphics;

namespace EvilFarmOwner;

internal enum WorkerAvailabilityState
{
    EligibleForPreview,
    TemporarilyUnavailable,
    Ineligible,
    Unknown
}

internal enum WorkerAvailabilityReason
{
    AvailableForPreview,
    Child,
    UnsupportedCharacter,
    ActiveFestival,
    ActiveEvent,
    MissingLocation,
    Sleeping,
    IslandActivity,
    MedicalActivity,
    WorkActivity,
    ControlledActivity,
    MovementActivity,
    DialogueActivity,
    ScriptedAnimation,
    UnsupportedCustomNpc,
    EvaluationFailed
}

internal sealed record WorkerAvailabilityResult(
    WorkerAvailabilityState State,
    WorkerAvailabilityReason Reason);

internal sealed record WorkerRosterEntry(
    string InternalName,
    string DisplayName,
    Texture2D Portrait,
    WorkerAvailabilityResult Availability);
