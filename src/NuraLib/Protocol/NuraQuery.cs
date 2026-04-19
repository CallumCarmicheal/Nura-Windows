namespace NuraLib.Protocol;

internal sealed record NuraQuery(
    NuraQueryId Id,
    string Description,
    byte[] Payload);
