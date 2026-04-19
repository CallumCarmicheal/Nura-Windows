namespace NuraLib.Devices;

/// <summary>
/// Describes normalized identity, capability, and compatibility information for a Nura device.
/// </summary>
public sealed record class NuraDeviceInfo {
    /// <summary>
    /// Gets the normalized lowercase type tag used by the library.
    /// </summary>
    public string TypeTag { get; init; } = "?";

    /// <summary>
    /// Gets the human-readable device type name.
    /// </summary>
    public string TypeName { get; init; } = "?";

    /// <summary>
    /// Gets the normalized device family.
    /// </summary>
    public NuraDeviceType DeviceType { get; init; } = NuraDeviceType.Unknown;

    /// <summary>
    /// Gets the Bluetooth device address.
    /// </summary>
    public string DeviceAddress { get; init; } = string.Empty;

    /// <summary>
    /// Gets the device serial number.
    /// </summary>
    public string Serial { get; init; } = string.Empty;

    /// <summary>
    /// Gets the last known firmware version.
    /// </summary>
    public int FirmwareVersion { get; init; }

    /// <summary>
    /// Gets the last known maximum packet-length hint.
    /// </summary>
    public int MaxPacketLengthHint { get; init; }

    /// <summary>
    /// Gets a value indicating whether the device is a true-wireless model.
    /// </summary>
    public bool IsTws { get; init; }

    public int MinimumFirmwareVersion { get; init; }

    public int MinimumFirmwareVersionForOfflineMode { get; init; }

    public int DefaultImmersionLevel { get; init; }

    /// <summary>
    /// Gets the derived supported-feature flags for the device and firmware combination.
    /// </summary>
    public NuraSupportedFeatures SupportedFeatures { get; init; } = NuraSupportedFeatures.None;

    /// <summary>
    /// Gets the grouped capability information derived from device family and firmware version.
    /// </summary>
    public NuraDeviceCapabilityInfo Capabilities { get; init; } = new();

    /// <summary>
    /// Gets the touch-button gesture slots supported by the current device and firmware combination.
    /// </summary>
    public NuraButtonGestureSupport SupportedButtonGestures { get; init; } = NuraButtonGestureSupport.None;

    /// <summary>
    /// Gets the supported audio-related capability set.
    /// </summary>
    public NuraAudioCapabilities AudioCapabilities => Capabilities.Audio;

    /// <summary>
    /// Gets the supported interaction capability set.
    /// </summary>
    public NuraInteractionCapabilities InteractionCapabilities => Capabilities.Interaction;

    /// <summary>
    /// Gets the supported system capability set.
    /// </summary>
    public NuraSystemCapabilities SystemCapabilities => Capabilities.System;

    /// <summary>
    /// Gets the supported button-function families.
    /// </summary>
    public NuraButtonFunctionSupport SupportedButtonFunctions => Capabilities.ButtonFunctions;

    /// <summary>
    /// Determines whether the device supports a specific feature flag.
    /// </summary>
    public bool Supports(NuraSupportedFeatures feature) => SupportedFeatures.HasFlag(feature);

    /// <summary>
    /// Determines whether the device supports a specific touch-button gesture.
    /// </summary>
    public bool SupportsButtonGesture(NuraButtonGesture gesture) =>
        gesture switch {
            NuraButtonGesture.SingleTap => SupportedButtonGestures.HasFlag(NuraButtonGestureSupport.SingleTap),
            NuraButtonGesture.DoubleTap => SupportedButtonGestures.HasFlag(NuraButtonGestureSupport.DoubleTap),
            NuraButtonGesture.TripleTap => SupportedButtonGestures.HasFlag(NuraButtonGestureSupport.TripleTap),
            NuraButtonGesture.TapAndHold => SupportedButtonGestures.HasFlag(NuraButtonGestureSupport.TapAndHold),
            _ => false
        };
}
