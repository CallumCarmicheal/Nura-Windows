using System.Text.Json;

using NuraLib.Logging;

namespace NuraLib.Auth;

/// <summary>
/// Provides access to the stored Nura authentication state and backend-assisted login/session operations.
/// </summary>
public sealed class NuraAuthManager {
    private const string Source = nameof(NuraAuthManager);
    private readonly NuraConfigState _state;
    private readonly NuraClientLogger _logger;
    private readonly NuraAuthApiClient _apiClient;
    private NuraAuthApiState _runtime = new();

    internal NuraAuthManager(NuraConfigState state, NuraClientLogger logger) {
        _state = state;
        _logger = logger;
        _apiClient = new NuraAuthApiClient(logger);
    }

    /// <summary>
    /// Gets a value indicating whether the current configuration contains stored authenticated session headers.
    /// </summary>
    public bool HasStoredCredentials => _state.Configuration.Auth.HasAuthenticatedSession;

    /// <summary>
    /// Determines whether the stored session appears valid based on the available authentication fields and expiry.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> when stored credentials are present and have not expired according to local state;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public Task<bool> HasValidSessionAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();

        var auth = _state.Configuration.Auth;
        if (!auth.HasAuthenticatedSession) {
            return Task.FromResult(false);
        }

        if (auth.TokenExpiryUnix is null) {
            return Task.FromResult(true);
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return Task.FromResult(auth.TokenExpiryUnix > now);
    }

    /// <summary>
    /// Requests an email login code from the Nura backend.
    /// </summary>
    /// <param name="email">The email address that should receive the login code.</param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task RequestEmailCodeAsync(string email, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(email)) {
            throw new ArgumentException("email is required", nameof(email));
        }

        email = email.Trim();
        _logger.Information(Source, $"Requesting login email code for {email}.");
        var result = await _apiClient.SendLoginEmailAsync(BuildApiState(), email, cancellationToken);
        ThrowForFailure(result, "Email-code request failed.");
        PersistAuthentication(
            emailAddress: email,
            result,
            fallbackAuthUid: email,
            message: "Stored email after successful login-code request.");
    }

    /// <summary>
    /// Verifies an email login code and establishes an authenticated session.
    /// </summary>
    /// <param name="code">The verification code provided to the user.</param>
    /// <param name="email">
    /// Optional email address to use for verification. When omitted, the implementation uses the stored email state.
    /// </param>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task VerifyEmailCodeAsync(string code, string? email = null, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(code)) {
            throw new ArgumentException("code is required", nameof(code));
        }

        email = string.IsNullOrWhiteSpace(email) ? _state.Configuration.Auth.UserEmail : email.Trim();
        if (string.IsNullOrWhiteSpace(email)) {
            throw new InvalidOperationException("email is required before verifying a login code");
        }

        await EnsureAppSessionAsync(cancellationToken);
        _logger.Information(Source, $"Verifying login code for {email}.");
        var result = await _apiClient.VerifyCodeAsync(BuildApiState(), email, code, cancellationToken);
        ThrowForFailure(result, "Email-code verification failed.");
        PersistAuthentication(
            emailAddress: email,
            result,
            fallbackAuthUid: email,
            message: "Updated authenticated session after email-code verification.");
    }

    /// <summary>
    /// Attempts to resume an online authenticated session using the stored configuration and backend state.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the operation.</param>
    public async Task ResumeAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasStoredCredentials) {
            throw new InvalidOperationException("stored authenticated session is required before calling ResumeAsync");
        }

        await EnsureAppSessionAsync(cancellationToken);
        _logger.Information(Source, "Validating stored authenticated session with backend.");
        var result = await _apiClient.ValidateTokenAsync(BuildApiState(), withAppContext: false, appStartTimeUnixMilliseconds: null, cancellationToken);
        ThrowForFailure(result, "Stored session validation failed.");
        PersistAuthentication(
            emailAddress: _state.Configuration.Auth.UserEmail,
            result,
            fallbackAuthUid: _state.Configuration.Auth.AuthUid,
            message: "Refreshed authenticated session using validate-token.");
    }

    private async Task EnsureAppSessionAsync(CancellationToken cancellationToken) {
        if (_runtime.AppSessionId is not null) {
            return;
        }

        _logger.Information(Source, "Starting app session.");
        var appStartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var result = await _apiClient.AppSessionAsync(BuildApiState(), "app/session", appStartTime, cancellationToken);
        ThrowForFailure(result, "App-session request failed.");
        ApplyRuntimeState(result);
    }

    private NuraAuthApiState BuildApiState() {
        var auth = _state.Configuration.Auth;
        return _runtime with {
            ApiBase = _state.Configuration.ApiBase,
            Uuid = _state.Configuration.Uuid,
            EmailAddress = auth.UserEmail,
            AccessToken = auth.AccessToken,
            ClientKey = auth.ClientKey,
            AuthUid = auth.AuthUid,
            TokenExpiryUnixSeconds = auth.TokenExpiryUnix
        };
    }

    private void PersistAuthentication(
        string? emailAddress,
        AuthCallResult result,
        string? fallbackAuthUid,
        string message) {
        ApplyRuntimeState(result);

        var currentConfig = _state.Configuration;
        var currentAuth = currentConfig.Auth;
        var nextAuth = currentAuth with {
            UserEmail = emailAddress ?? currentAuth.UserEmail,
            AccessToken = result.AccessToken ?? currentAuth.AccessToken,
            ClientKey = result.ClientKey ?? currentAuth.ClientKey,
            AuthUid = result.AuthUid ?? fallbackAuthUid ?? currentAuth.AuthUid,
            TokenExpiryUnix = result.ExpiryUnixSeconds ?? currentAuth.TokenExpiryUnix
        };

        if (nextAuth != currentAuth) {
            _state.ReplaceConfiguration(
                currentConfig with { Auth = nextAuth },
                NuraStateSaveReason.Authentication,
                message);
        }
    }

    private void ApplyRuntimeState(AuthCallResult result) {
        var snapshot = NuraAuthResponseParser.ExtractSessionState(result.DecodedBody);
        _runtime = _runtime with {
            UserSessionId = snapshot.UserSessionId ?? _runtime.UserSessionId,
            AppSessionId = snapshot.AppSessionId ?? _runtime.AppSessionId,
            BluetoothSessionId = snapshot.BluetoothSessionId ?? _runtime.BluetoothSessionId,
            AppSessionToken = snapshot.AppSessionToken ?? _runtime.AppSessionToken,
            UserSessionStatus = snapshot.UserSessionStatus ?? _runtime.UserSessionStatus,
            AppSessionStatus = snapshot.AppSessionStatus ?? _runtime.AppSessionStatus,
            AppEncKey = snapshot.AppEncKey ?? _runtime.AppEncKey,
            AppEncNonce = snapshot.AppEncNonce ?? _runtime.AppEncNonce,
            LastResponseBody = snapshot.ResponseBody ?? _runtime.LastResponseBody
        };

        if (snapshot.AppSessionId is not null) {
            _logger.Debug(Source, $"runtime.app_session_id={snapshot.AppSessionId}");
        }

        if (snapshot.UserSessionId is not null) {
            _logger.Debug(Source, $"runtime.user_session_id={snapshot.UserSessionId}");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.AppSessionStatus)) {
            _logger.Debug(Source, $"runtime.app_session_status={snapshot.AppSessionStatus}");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.UserSessionStatus)) {
            _logger.Debug(Source, $"runtime.user_session_status={snapshot.UserSessionStatus}");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.AppEncKey)) {
            _logger.Debug(Source, $"runtime.app_enc.key={snapshot.AppEncKey}");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.AppEncNonce)) {
            _logger.Debug(Source, $"runtime.app_enc.nonce={snapshot.AppEncNonce}");
        }
    }

    private void ThrowForFailure(AuthCallResult result, string message) {
        if (result.IsSuccessStatusCode) {
            return;
        }

        var detail = result.DecodedBody is null
            ? "no_response_body"
            : JsonSerializer.Serialize(result.DecodedBody);
        var fullMessage = $"{message} StatusCode={result.StatusCode}. Response={detail}";
        var exception = new InvalidOperationException(fullMessage);
        _logger.Error(Source, fullMessage, exception);
        throw exception;
    }
}
