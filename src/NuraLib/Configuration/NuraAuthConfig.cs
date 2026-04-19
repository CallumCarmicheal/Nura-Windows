namespace NuraLib.Configuration;

/// <summary>
/// Persisted authentication fields used for online Nura backend operations.
/// </summary>
public sealed record class NuraAuthConfig {
    /// <summary>
    /// Gets the email address associated with the current account.
    /// </summary>
    public string? UserEmail { get; init; }

    /// <summary>
    /// Gets the auth-layer UID header value, which is typically the user's email for email login.
    /// </summary>
    public string? AuthUid { get; init; }

    /// <summary>
    /// Gets the current access token.
    /// </summary>
    public string? AccessToken { get; init; }

    /// <summary>
    /// Gets the current client key.
    /// </summary>
    public string? ClientKey { get; init; }

    /// <summary>
    /// Gets the token type sent to the backend.
    /// </summary>
    public string TokenType { get; init; } = "Bearer";

    /// <summary>
    /// Gets the token expiry as a Unix timestamp in seconds, when known.
    /// </summary>
    public long? TokenExpiryUnix { get; init; }

    /// <summary>
    /// Gets a value indicating whether the configuration contains the minimum fields required for an authenticated session.
    /// </summary>
    public bool HasAuthenticatedSession =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(ClientKey) &&
        !string.IsNullOrWhiteSpace(AuthUid);
}
