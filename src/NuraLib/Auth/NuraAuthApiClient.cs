using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using NuraLib.Logging;
using NuraLib.Utilities;

namespace NuraLib.Auth;

internal sealed class NuraAuthApiClient {
    private const string Source = nameof(NuraAuthApiClient);
    private readonly HttpClient _httpClient = new();
    private readonly NuraClientLogger _logger;

    public NuraAuthApiClient(NuraClientLogger logger) {
        _logger = logger;
    }

    public Task<AuthCallResult> SendLoginEmailAsync(
        NuraAuthApiState state,
        string emailAddress,
        CancellationToken cancellationToken) {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["email"] = emailAddress,
            ["emailAddress"] = emailAddress,
            ["uuid"] = state.Uuid
        };

        return SendAsync(state, "auth/login_via_email", authenticated: false, payload, cancellationToken);
    }

    public Task<AuthCallResult> VerifyCodeAsync(
        NuraAuthApiState state,
        string emailAddress,
        string oneTimeCode,
        CancellationToken cancellationToken) {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["email"] = emailAddress,
            ["emailAddress"] = emailAddress,
            ["token"] = oneTimeCode,
            ["code"] = oneTimeCode,
            ["oneTimeCode"] = oneTimeCode,
            ["uuid"] = state.Uuid
        };

        if (state.AppSessionId is { } appSessionId) {
            payload["asid"] = appSessionId;
            payload["app_session_id"] = appSessionId;
            payload["appSessionId"] = appSessionId;
        }

        return SendAsync(state, "auth/login_via_email_verify", authenticated: false, payload, cancellationToken);
    }

    public Task<AuthCallResult> ValidateTokenAsync(
        NuraAuthApiState state,
        bool withAppContext,
        long? appStartTimeUnixMilliseconds,
        CancellationToken cancellationToken) {
        Dictionary<string, object?>? payload = null;
        if (state.AppSessionId is { } appSessionId) {
            payload ??= new Dictionary<string, object?>(StringComparer.Ordinal);
            payload["asid"] = appSessionId;
        }

        if (withAppContext) {
            var appContext = BuildAppContextPayload(state, appStartTimeUnixMilliseconds ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            payload ??= new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var entry in appContext) {
                payload[entry.Key] = entry.Value;
            }
        }

        return SendAsync(state, "auth/validate_token", authenticated: true, payload, cancellationToken);
    }

    public Task<AuthCallResult> CallAuthenticatedEndpointAsync(
        NuraAuthApiState state,
        string endpoint,
        IReadOnlyDictionary<string, object?>? payload,
        CancellationToken cancellationToken) {
        return SendAsync(state, endpoint, authenticated: true, payload, cancellationToken);
    }

    public async Task<AuthCallResult> SessionStartAsync(
        NuraAuthApiState state,
        int serialNumber,
        int firmwareVersion,
        int maxPacketLength,
        int userSessionId,
        CancellationToken cancellationToken) {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["serial"] = serialNumber,
            ["firmware_version"] = firmwareVersion,
            ["max_packet_length"] = maxPacketLength,
            ["usid"] = userSessionId
        };

        var primaryResult = await SendAsync(
            state,
            "end_to_end/session/start",
            authenticated: true,
            payload,
            cancellationToken);

        if (primaryResult.StatusCode != 404 ||
            !state.ApiBase.Contains("api-p1", StringComparison.OrdinalIgnoreCase)) {
            return primaryResult;
        }

        var retryState = state with {
            ApiBase = "https://api-p3.nuraphone.com/"
        };
        _logger.Warning(Source, "Retrying session/start on api-p3 after 404 from api-p1.");
        return await SendAsync(
            retryState,
            "end_to_end/session/start",
            authenticated: true,
            payload,
            cancellationToken);
    }

    public Task<AuthCallResult> AutomatedEntryAsync(
        NuraAuthApiState state,
        string endpoint,
        int sessionId,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> packets,
        CancellationToken cancellationToken) {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["session"] = sessionId
        };

        if (packets.Count > 0) {
            payload["packets"] = packets.Cast<object?>().ToArray();
        }

        return SendAsync(
            state,
            NormalizeAutomatedEntryEndpoint(endpoint),
            authenticated: true,
            payload,
            cancellationToken);
    }

    public Task<AuthCallResult> AppSessionAsync(
        NuraAuthApiState state,
        string endpoint,
        long appStartTimeUnixMilliseconds,
        CancellationToken cancellationToken) {
        var payload = BuildAppContextPayload(state, appStartTimeUnixMilliseconds);
        return SendAsync(state, endpoint, authenticated: false, payload, cancellationToken);
    }

    private async Task<AuthCallResult> SendAsync(
        NuraAuthApiState state,
        string relativeUrl,
        bool authenticated,
        IReadOnlyDictionary<string, object?>? payload,
        CancellationToken cancellationToken) {
        var baseUri = EnsureTrailingSlash(state.ApiBase);
        var requestUri = new Uri(new Uri(baseUri, UriKind.Absolute), relativeUrl);
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/msgpack"));

        if (authenticated) {
            request.Headers.TryAddWithoutValidation("access-token", state.AccessToken ?? string.Empty);
            request.Headers.TryAddWithoutValidation("client", state.ClientKey ?? string.Empty);
            request.Headers.TryAddWithoutValidation("uid", state.AuthUid ?? string.Empty);
            request.Headers.TryAddWithoutValidation("token-type", "Bearer");
        }

        byte[]? payloadBytes = null;
        if (payload is { Count: > 0 }) {
            payloadBytes = MessagePackLite.SerializeMap(payload);
            var multipart = new MultipartFormDataContent();
            var msgpack = new ByteArrayContent(payloadBytes);
            msgpack.Headers.ContentType = new MediaTypeHeaderValue("application/msgpack");
            multipart.Add(msgpack, "msgpack");
            request.Content = multipart;
        } else {
            request.Content = new StringContent(string.Empty, Encoding.UTF8, "text/plain");
        }

        LogRequest(relativeUrl, authenticated, payload, payloadBytes);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var responseContentType = response.Content.Headers.ContentType?.MediaType;

        Dictionary<string, object?>? decodedBody = null;
        object? decodedValue = null;
        if (responseBytes.Length > 0 && string.Equals(responseContentType, "application/msgpack", StringComparison.OrdinalIgnoreCase)) {
            decodedValue = MessagePackLite.Deserialize(responseBytes);
            decodedBody = decodedValue as Dictionary<string, object?>;
        }

        var rotatedAccessToken = TryGetHeader(response.Headers, response.Content.Headers, "access-token");
        var rotatedClient = TryGetHeader(response.Headers, response.Content.Headers, "client");
        var rotatedUid = TryGetHeader(response.Headers, response.Content.Headers, "uid");
        var rotatedExpiry = TryGetHeader(response.Headers, response.Content.Headers, "expiry");

        LogResponse(response, responseBytes, decodedValue, rotatedAccessToken, rotatedClient, rotatedUid, rotatedExpiry);

        return new AuthCallResult(
            StatusCode: (int)response.StatusCode,
            IsSuccessStatusCode: response.IsSuccessStatusCode,
            DecodedBody: decodedBody,
            AccessToken: rotatedAccessToken,
            ClientKey: rotatedClient,
            AuthUid: rotatedUid,
            ExpiryUnixSeconds: ParseNullableInt64(rotatedExpiry));
    }

    private void LogRequest(
        string relativeUrl,
        bool authenticated,
        IReadOnlyDictionary<string, object?>? payload,
        byte[]? payloadBytes) {
        _logger.Debug(Source, $"request.url={relativeUrl}");
        _logger.Debug(Source, $"request.authenticated={authenticated}");

        if (payload is not null) {
            _logger.Trace(Source, $"request.payload.json={JsonSerializer.Serialize(payload)}");
        }

        if (payloadBytes is not null) {
            _logger.Trace(Source, $"request.payload.msgpack.hex={HexEncoding.Format(payloadBytes)}");
        }
    }

    private void LogResponse(
        HttpResponseMessage response,
        byte[] responseBytes,
        object? decodedValue,
        string? rotatedAccessToken,
        string? rotatedClient,
        string? rotatedUid,
        string? rotatedExpiry) {
        _logger.Debug(Source, $"response.status_code={(int)response.StatusCode}");
        _logger.Debug(Source, $"response.is_success={response.IsSuccessStatusCode}");
        _logger.Debug(Source, $"response.content_type={response.Content.Headers.ContentType?.MediaType ?? string.Empty}");
        _logger.Trace(Source, $"response.body.hex={HexEncoding.Format(responseBytes)}");
        LogInterestingHeaders("response.header", response.Headers);
        LogInterestingHeaders("response.content_header", response.Content.Headers);

        if (decodedValue is not null) {
            _logger.Trace(Source, $"response.body.json={JsonSerializer.Serialize(decodedValue)}");
        }

        if (!string.IsNullOrWhiteSpace(rotatedAccessToken)) {
            _logger.Debug(Source, $"response.header.access_token={rotatedAccessToken}");
        }

        if (!string.IsNullOrWhiteSpace(rotatedClient)) {
            _logger.Debug(Source, $"response.header.client={rotatedClient}");
        }

        if (!string.IsNullOrWhiteSpace(rotatedUid)) {
            _logger.Debug(Source, $"response.header.uid={rotatedUid}");
        }

        if (!string.IsNullOrWhiteSpace(rotatedExpiry)) {
            _logger.Debug(Source, $"response.header.expiry={rotatedExpiry}");
        }
    }

    private void LogInterestingHeaders(string prefix, HttpHeaders headers) {
        foreach (var header in headers) {
            if (!ShouldLogHeader(header.Key)) {
                continue;
            }

            var joinedValue = string.Join("; ", header.Value);
            _logger.Trace(Source, $"{prefix}.{SanitizeHeaderName(header.Key)}={joinedValue}");
        }
    }

    private static bool ShouldLogHeader(string name) {
        return name.Equals("access-token", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("client", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("uid", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("token-type", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("expiry", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("set-cookie", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("session", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("usid", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("bsid", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeHeaderName(string name) {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name) {
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_');
        }

        return builder.ToString();
    }

    private static string EnsureTrailingSlash(string apiBase) {
        return apiBase.EndsWith("/", StringComparison.Ordinal)
            ? apiBase
            : $"{apiBase}/";
    }

    private static Dictionary<string, object?> BuildAppContextPayload(
        NuraAuthApiState state,
        long appStartTimeUnixMilliseconds) {
        return new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["uuid"] = state.Uuid,
            ["os"] = 1,
            ["os_name"] = "android",
            ["os_version"] = "14",
            ["os_api"] = 34,
            ["app_version"] = "4.5.4",
            ["appVersion"] = "4.5.4",
            ["app_build"] = 1410,
            ["appBuild"] = 1410,
            ["device"] = "samsung/SM-S918B",
            ["device_info"] = new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["brand"] = "samsung",
                ["manufacturer"] = "samsung",
                ["model"] = "SM-S918B",
                ["device"] = "dm3q",
                ["product"] = "dm3qxxx",
                ["sdkInt"] = 34,
                ["securityPatch"] = "2026-03-01"
            },
            ["installer"] = "google_play",
            ["lang"] = "en",
            ["action"] = "app_start_cold",
            ["app_session"] = "app_start_cold",
            ["appSession"] = "app_start_cold",
            ["app_start_time"] = appStartTimeUnixMilliseconds,
            ["appStartTime"] = appStartTimeUnixMilliseconds
        };
    }

    private static string NormalizeAutomatedEntryEndpoint(string endpoint) {
        if (string.IsNullOrWhiteSpace(endpoint)) {
            throw new InvalidOperationException("endpoint is required");
        }

        var trimmed = endpoint.Trim().TrimStart('/');
        if (trimmed.StartsWith("end_to_end/", StringComparison.OrdinalIgnoreCase)) {
            return trimmed;
        }

        return $"end_to_end/{trimmed}";
    }

    private static string? TryGetHeader(
        HttpResponseHeaders responseHeaders,
        HttpContentHeaders contentHeaders,
        string name) {
        if (responseHeaders.TryGetValues(name, out var responseValues)) {
            return responseValues.FirstOrDefault();
        }

        if (contentHeaders.TryGetValues(name, out var contentValues)) {
            return contentValues.FirstOrDefault();
        }

        return null;
    }

    private static long? ParseNullableInt64(string? value) {
        return long.TryParse(value, out var parsed) ? parsed : null;
    }
}

internal sealed record AuthCallResult(
    int StatusCode,
    bool IsSuccessStatusCode,
    Dictionary<string, object?>? DecodedBody,
    string? AccessToken,
    string? ClientKey,
    string? AuthUid,
    long? ExpiryUnixSeconds);
