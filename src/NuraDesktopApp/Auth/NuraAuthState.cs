using System.Text.Json;

namespace desktop_app.Auth;

internal sealed record class NuraAuthState {
    public string ApiBase { get; init; } = "https://api-p1.nuraphone.com/";

    public string Uuid { get; init; } = Guid.NewGuid().ToString();

    public string? EmailAddress { get; init; }

    public string? AccessToken { get; init; }

    public string? ClientKey { get; init; }

    public string? AuthUid { get; init; }

    public long? TokenExpiryUnixSeconds { get; init; }

    public Dictionary<string, object?>? LastResponseBody { get; init; }

    public DateTimeOffset? LastUpdatedUtc { get; init; }

    public bool HasAuthenticatedSession =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(ClientKey) &&
        !string.IsNullOrWhiteSpace(AuthUid);

    public static NuraAuthState LoadOrCreate(string path) {
        if (!File.Exists(path)) {
            return new NuraAuthState();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<NuraAuthState>(json, JsonOptions())
               ?? new NuraAuthState();
    }

    public void Save(string path) {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(this, JsonOptions());
        File.WriteAllText(path, json);
    }

    public NuraAuthState WithAuthHeaders(
        string? accessToken,
        string? clientKey,
        string? authUid,
        long? expiryUnixSeconds,
        Dictionary<string, object?>? responseBody,
        string? emailAddress = null) {
        return new NuraAuthState {
            ApiBase = ApiBase,
            Uuid = Uuid,
            EmailAddress = emailAddress ?? EmailAddress,
            AccessToken = accessToken ?? AccessToken,
            ClientKey = clientKey ?? ClientKey,
            AuthUid = authUid ?? AuthUid,
            TokenExpiryUnixSeconds = expiryUnixSeconds ?? TokenExpiryUnixSeconds,
            LastResponseBody = responseBody ?? LastResponseBody,
            LastUpdatedUtc = DateTimeOffset.UtcNow
        };
    }

    private static JsonSerializerOptions JsonOptions() {
        return new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }
}
