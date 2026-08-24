namespace EvilFarmOwner;

internal sealed class MultiplayerAcceptanceReplayBuffer
{
    private ContractStartRequestMessage? LastRequest;

    public bool HasRequest => this.LastRequest is not null;

    public string RequestId => this.LastRequest?.RequestId ?? "none";

    public void Capture(ContractStartRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        this.LastRequest = Clone(request);
    }

    public bool TryCreateReplay(long requestingPlayerId, out ContractStartRequestMessage? request)
    {
        request = null;
        if (this.LastRequest is null
            || requestingPlayerId <= 0
            || this.LastRequest.RequestingPlayerId != requestingPlayerId)
        {
            return false;
        }

        request = Clone(this.LastRequest);
        return true;
    }

    private static ContractStartRequestMessage Clone(ContractStartRequestMessage request)
    {
        return new ContractStartRequestMessage
        {
            SchemaVersion = request.SchemaVersion,
            ModVersion = request.ModVersion,
            SaveId = request.SaveId,
            TotalDays = request.TotalDays,
            RequestId = request.RequestId,
            RequestingPlayerId = request.RequestingPlayerId,
            WorkerName = request.WorkerName,
            Task = request.Task
        };
    }
}
