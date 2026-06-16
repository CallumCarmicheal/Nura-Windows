namespace NuraLib.Devices;

public sealed record class NuraDeviceOperationStatus(
    NuraDeviceOperationKind Kind,
    string StageCode,
    string Message,
    bool IsRunning,
    bool IsCompleted,
    bool IsError,
    DateTimeOffset TimestampUtc);
