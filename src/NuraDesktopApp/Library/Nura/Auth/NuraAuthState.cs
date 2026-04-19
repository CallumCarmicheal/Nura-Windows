using System.Text.Json;
using System.Text.Encodings.Web;

namespace NuraDesktopConsole.Library.Nura.Auth;

internal sealed record class NuraAuthState {
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

    public DateTimeOffset? LastUpdatedUtc { get; init; }

    public bool HasAuthenticatedSession =>
        !string.IsNullOrWhiteSpace(AccessToken) &&
        !string.IsNullOrWhiteSpace(ClientKey) &&
        !string.IsNullOrWhiteSpace(AuthUid);

    public static NuraAuthState LoadOrCreate(string path) {
        if (!File.Exists(path)) {
            var state = new NuraAuthState();

            // Save initial state back to file
            state.Save(path);
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
        string? apiBase,
        Dictionary<string, object?>? responseBody,
        string? emailAddress = null,
        int? userSessionId = null,
        int? appSessionId = null,
        int? bluetoothSessionId = null,
        string? appSessionToken = null,
        string? userSessionStatus = null,
        string? appSessionStatus = null,
        string? appEncKey = null,
        string? appEncNonce = null) {
        return new NuraAuthState {
            ApiBase = string.IsNullOrWhiteSpace(apiBase) ? ApiBase : apiBase,
            Uuid = Uuid,
            EmailAddress = emailAddress ?? EmailAddress,
            AccessToken = accessToken ?? AccessToken,
            ClientKey = clientKey ?? ClientKey,
            AuthUid = authUid ?? AuthUid,
            TokenExpiryUnixSeconds = expiryUnixSeconds ?? TokenExpiryUnixSeconds,
            UserSessionId = userSessionId ?? UserSessionId,
            AppSessionId = appSessionId ?? AppSessionId,
            BluetoothSessionId = bluetoothSessionId ?? BluetoothSessionId,
            AppSessionToken = appSessionToken ?? AppSessionToken,
            UserSessionStatus = userSessionStatus ?? UserSessionStatus,
            AppSessionStatus = appSessionStatus ?? AppSessionStatus,
            AppEncKey = appEncKey ?? AppEncKey,
            AppEncNonce = appEncNonce ?? AppEncNonce,
            LastResponseBody = responseBody ?? LastResponseBody,
            LastUpdatedUtc = DateTimeOffset.UtcNow
        };
    }

    private static JsonSerializerOptions JsonOptions() {
        return new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }
}
