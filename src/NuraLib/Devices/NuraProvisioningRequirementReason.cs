namespace NuraLib.Devices;

/// <summary>
/// Describes why a device currently requires backend-assisted provisioning.
/// </summary>
public enum NuraProvisioningRequirementReason {
    /// <summary>
    /// The device can use the existing local configuration without provisioning.
    /// </summary>
    None,

    /// <summary>
    /// No persistent device key is stored, so local encrypted control cannot be used yet.
    /// </summary>
    MissingDeviceKey,

    /// <summary>
    /// The host marked this as a NuraNow device and its backend entitlement refresh is due.
    /// </summary>
    NuraNowRefreshRequired
}
