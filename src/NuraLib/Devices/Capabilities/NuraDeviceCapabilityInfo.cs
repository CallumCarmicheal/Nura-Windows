namespace NuraLib.Devices;

/// <summary>
/// Groups device capabilities into higher-level sets that are easier for library callers to query.
/// </summary>
public sealed record class NuraDeviceCapabilityInfo {
    /// <summary>
    /// Gets the raw flattened feature flags derived from device family and firmware version.
    /// </summary>
    public NuraSupportedFeatures Features { get; init; } = NuraSupportedFeatures.None;

    /// <summary>
    /// Gets the supported audio-related capabilities.
    /// </summary>
    public NuraAudioCapabilities Audio { get; init; } = NuraAudioCapabilities.None;

    /// <summary>
    /// Gets the supported interaction and control-surface capabilities.
    /// </summary>
    public NuraInteractionCapabilities Interaction { get; init; } = NuraInteractionCapabilities.None;

    /// <summary>
    /// Gets the supported device and transport capabilities.
    /// </summary>
    public NuraSystemCapabilities System { get; init; } = NuraSystemCapabilities.None;

    /// <summary>
    /// Gets the supported button gesture slots.
    /// </summary>
    public NuraButtonGestureSupport ButtonGestures { get; init; } = NuraButtonGestureSupport.None;

    /// <summary>
    /// Gets the supported button-function families.
    /// </summary>
    public NuraButtonFunctionSupport ButtonFunctions { get; init; } = NuraButtonFunctionSupport.None;

    /// <summary>
    /// Determines whether a specific raw feature flag is supported.
    /// </summary>
    public bool Supports(NuraSupportedFeatures feature) => Features.HasFlag(feature);
}
