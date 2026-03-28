namespace NuraLib.Configuration;

public sealed record class NuraAuthConfig {
    public string? UserEmail { get; init; }

    public string? AuthUid { get; init; }

    public string? AccessToken { get; init; }

    public string? ClientKey { get; init; }

    public string TokenType { get; init; } = "Bearer";

    public long? TokenExpiryUnix { get; init; }

    public bool HasAuthenticatedSession =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(ClientKey) &&
        !string.IsNullOrWhiteSpace(AuthUid);
}
