namespace EvilFarmOwner;

internal sealed record GroupTransferReportSet(
    IReadOnlyList<NamedContractTransferState> Completed,
    IReadOnlyList<NamedContractTransferState> Skipped);

/// <summary>Produces one contiguous transfer-report sequence for an aggregated worker group.</summary>
internal static class GroupTransferReportPolicy
{
    public static GroupTransferReportSet Create(
        IEnumerable<NamedContractTransferState> completed,
        IEnumerable<NamedContractTransferState> terminalSkipped,
        bool groupSucceeded)
    {
        ArgumentNullException.ThrowIfNull(completed);
        ArgumentNullException.ThrowIfNull(terminalSkipped);

        NamedContractTransferState[] normalizedCompleted = completed
            .Select((transfer, index) => transfer with { Sequence = index + 1 })
            .ToArray();
        NamedContractTransferState[] normalizedSkipped = groupSucceeded
            ? Array.Empty<NamedContractTransferState>()
            : terminalSkipped
                .Select((transfer, index) => transfer with
                {
                    Sequence = normalizedCompleted.Length + index + 1
                })
                .ToArray();
        return new GroupTransferReportSet(normalizedCompleted, normalizedSkipped);
    }
}
