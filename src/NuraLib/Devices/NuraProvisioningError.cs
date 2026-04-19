namespace NuraLib.Devices;

/// <summary>
/// Describes a known provisioning failure reason.
/// </summary>
public enum NuraProvisioningError {
    /// <summary>
    /// The specific failure reason is not yet known.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The device could not be provisioned because the user is not authenticated with the backend.
    /// </summary>
    NotAuthenticated
}
