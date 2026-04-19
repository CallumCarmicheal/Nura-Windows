namespace NuraLib.Devices;

/// <summary>
/// Describes the outcome of a provisioning attempt.
/// </summary>
/// <param name="Success">Whether provisioning completed successfully.</param>
/// <param name="Error">The known failure reason when provisioning did not succeed.</param>
public readonly record struct NuraProvisioningResult(
    bool Success,
    NuraProvisioningError Error = NuraProvisioningError.Unknown);
