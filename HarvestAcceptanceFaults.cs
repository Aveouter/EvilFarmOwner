namespace EvilFarmOwner;

[Flags]
internal enum HarvestAcceptanceFault
{
    None = 0,
    OverflowLock = 1 << 0,
    VisibleDrop = 1 << 1,
    QuarantineLock = 1 << 2,
    RecoveryRecordWrite = 1 << 3,
    QuarantineWrite = 1 << 4,
    NormalStorage = 1 << 5
}

/// <summary>
/// Holds opt-in acceptance-test failures. The production build has no command
/// which can arm this state; see the EFO_ACCEPTANCE_FAULTS compile constant.
/// </summary>
internal sealed class HarvestAcceptanceFaults
{
    private HarvestAcceptanceFault Armed;

    public bool IsArmed(HarvestAcceptanceFault fault)
    {
        return fault != HarvestAcceptanceFault.None && this.Armed.HasFlag(fault);
    }

    public void Arm(HarvestAcceptanceFault fault)
    {
        this.Armed |= fault;
    }

    public void Clear()
    {
        this.Armed = HarvestAcceptanceFault.None;
    }

    public string Describe()
    {
        return this.Armed == HarvestAcceptanceFault.None
            ? "none"
            : string.Join(",", Enum.GetValues<HarvestAcceptanceFault>()
                .Where(fault => fault != HarvestAcceptanceFault.None && this.IsArmed(fault))
                .Select(ToCommandName));
    }

    public static bool TryParse(string value, out HarvestAcceptanceFault fault)
    {
        fault = value.Trim().ToLowerInvariant() switch
        {
            "overflow-lock" => HarvestAcceptanceFault.OverflowLock,
            "visible-drop" => HarvestAcceptanceFault.VisibleDrop,
            "quarantine-lock" => HarvestAcceptanceFault.QuarantineLock,
            "recovery-record-write" => HarvestAcceptanceFault.RecoveryRecordWrite,
            "quarantine-write" => HarvestAcceptanceFault.QuarantineWrite,
            "normal-storage" => HarvestAcceptanceFault.NormalStorage,
            _ => HarvestAcceptanceFault.None
        };
        return fault != HarvestAcceptanceFault.None;
    }

    private static string ToCommandName(HarvestAcceptanceFault fault)
    {
        return fault switch
        {
            HarvestAcceptanceFault.OverflowLock => "overflow-lock",
            HarvestAcceptanceFault.VisibleDrop => "visible-drop",
            HarvestAcceptanceFault.QuarantineLock => "quarantine-lock",
            HarvestAcceptanceFault.RecoveryRecordWrite => "recovery-record-write",
            HarvestAcceptanceFault.QuarantineWrite => "quarantine-write",
            HarvestAcceptanceFault.NormalStorage => "normal-storage",
            _ => "none"
        };
    }
}
