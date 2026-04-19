using NuraLib.Protocol;

namespace NuraLib.Devices;

/// <summary>
/// Provides the decoded headset indication received for a connected device.
/// </summary>
public sealed class NuraHeadsetIndicationEventArgs : EventArgs {
    internal NuraHeadsetIndicationEventArgs(HeadsetIndicationIdentifier identifier, byte value) {
        Identifier = identifier;
        Value = value;
    }

    /// <summary>
    /// Gets the indication identifier reported by the headset.
    /// </summary>
    public HeadsetIndicationIdentifier Identifier { get; }

    /// <summary>
    /// Gets the raw indication value byte.
    /// </summary>
    public byte Value { get; }
}
