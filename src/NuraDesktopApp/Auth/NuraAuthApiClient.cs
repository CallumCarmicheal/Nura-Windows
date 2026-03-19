using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using desktop_app.Logging;

namespace desktop_app.Auth;

internal sealed class NuraAuthApiClient : IDisposable {
    private readonly HttpClient _httpClient;
    private readonly SessionLogger _logger;
    private bool _disposed;

    public NuraAuthApiClient(SessionLogger logger) {
        _logger = logger;
        _httpClient = new HttpClient();
    }

    public async Task<AuthCallResult> SendLoginEmailAsync(
        NuraAuthState state,
        string emailAddress,
        CancellationToken cancellationToken) {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["email"] = emailAddress,
            ["emailAddress"] = emailAddress,
            ["uuid"] = state.Uuid
        };

        return await SendAsync(
            state,
            "auth/login_via_email",
            authenticated: false,
            payload,
            cancellationToken);
    }

    public async Task<AuthCallResult> VerifyCodeAsync(
        NuraAuthState state,
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

        return await SendAsync(
            state,
            "auth/login_via_email_verify",
            authenticated: false,
            payload,
            cancellationToken);
    }

    public async Task<AuthCallResult> ValidateTokenAsync(
        NuraAuthState state,
        CancellationToken cancellationToken) {
        return await SendAsync(
            state,
            "auth/validate_token",
            authenticated: true,
            payload: null,
            cancellationToken);
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _httpClient.Dispose();
        _disposed = true;
    }

    private async Task<AuthCallResult> SendAsync(
        NuraAuthState state,
        string relativeUrl,
        bool authenticated,
        IReadOnlyDictionary<string, object?>? payload,
        CancellationToken cancellationToken) {
        ThrowIfDisposed();

        var baseUri = EnsureTrailingSlash(state.ApiBase);
        var requestUri = new Uri(new Uri(baseUri, UriKind.Absolute), relativeUrl);
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
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
        _logger.WriteLine($"auth.request.url={relativeUrl}");
        _logger.WriteLine($"auth.request.authenticated={authenticated}");
        if (payload is not null) {
            _logger.WriteLine($"auth.request.payload.json={JsonSerializer.Serialize(payload)}");
        }

        if (payloadBytes is not null) {
            _logger.WriteLine($"auth.request.payload.msgpack.hex={Hex.Format(payloadBytes)}");
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
        _logger.WriteLine($"auth.response.status_code={(int)response.StatusCode}");
        _logger.WriteLine($"auth.response.is_success={response.IsSuccessStatusCode}");
        _logger.WriteLine($"auth.response.content_type={response.Content.Headers.ContentType?.MediaType ?? string.Empty}");
        _logger.WriteLine($"auth.response.body.hex={Hex.Format(responseBytes)}");

        if (decodedValue is not null) {
            _logger.WriteLine($"auth.response.body.json={JsonSerializer.Serialize(decodedValue)}");
        }

        if (!string.IsNullOrWhiteSpace(rotatedAccessToken)) {
            _logger.WriteLine($"auth.response.header.access_token={rotatedAccessToken}");
        }

        if (!string.IsNullOrWhiteSpace(rotatedClient)) {
            _logger.WriteLine($"auth.response.header.client={rotatedClient}");
        }

        if (!string.IsNullOrWhiteSpace(rotatedUid)) {
            _logger.WriteLine($"auth.response.header.uid={rotatedUid}");
        }

        if (!string.IsNullOrWhiteSpace(rotatedExpiry)) {
            _logger.WriteLine($"auth.response.header.expiry={rotatedExpiry}");
        }
    }

    private static string EnsureTrailingSlash(string apiBase) {
        return apiBase.EndsWith("/", StringComparison.Ordinal)
            ? apiBase
            : $"{apiBase}/";
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

    private void ThrowIfDisposed() {
        if (_disposed) {
            throw new ObjectDisposedException(nameof(NuraAuthApiClient));
        }
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
