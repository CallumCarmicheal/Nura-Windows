namespace NuraLib.Auth;

internal sealed record class NuraAuthApiState {
    public string ApiBase { get; init; } = "https://api-p1.nuraphone.com/";

    public string Uuid { get; init; } = Guid.NewGuid().ToString();

    public string? EmailAddress { get; init; }

    public string? AccessToken { get; init; }

    public string? ClientKey { get; init; }

    public string? AuthUid { get; init; }

    public long? TokenExpiryUnixSeconds { get; init; }

    public int? UserSessionId { get; init; }

    public int? AppSessionId { get; init; }

    public int? BluetoothSessionId { get; init; }

    public string? AppSessionToken { get; init; }

    public string? UserSessionStatus { get; init; }

    public string? AppSessionStatus { get; init; }

    public string? AppEncKey { get; init; }

    public string? AppEncNonce { get; init; }

    public Dictionary<string, object?>? LastResponseBody { get; init; }

    public bool HasAuthenticatedSession =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(ClientKey) &&
        !string.IsNullOrWhiteSpace(AuthUid);
}
