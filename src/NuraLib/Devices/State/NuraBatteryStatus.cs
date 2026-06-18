namespace NuraLib.Devices;

/// <summary>
/// Describes the last battery telemetry returned by a headset.
/// </summary>
public sealed record class NuraBatteryStatus {
    public int BatteryVoltageMillivolts { get; init; }

    public int BatteryLevelRaw { get; init; }

    public int BatteryPercentage { get; init; }

    public int ChargerStateRaw { get; init; }

    public int ChargerVoltageMillivolts { get; init; }

    public int ChargerLevelRaw { get; init; }

    public int NtcVoltageMillivolts { get; init; }

    public int NtcLevelRaw { get; init; }
}
