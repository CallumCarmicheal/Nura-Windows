namespace NuraLib.Protocol;

internal sealed record class HeadsetIndication(
    HeadsetIndicationIdentifier Identifier,
    byte Value);
