using System.Text.Json;

namespace NuraLib.Auth;

/// <summary>
/// Base exception for backend authentication and provisioning calls that complete but return a failure response.
/// </summary>
public class NuraAuthFailureException : Exception {
    public NuraAuthFailureException(
        string message,
        int statusCode,
        IReadOnlyDictionary<string, object?>? responseBody,
        Exception? innerException = null)
        : base(BuildMessage(message, statusCode, responseBody), innerException) {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        ResponseBodyJson = responseBody is null ? null : JsonSerializer.Serialize(responseBody);
    }

    /// <summary>
    /// Gets the HTTP status code returned by the backend.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Gets the decoded response body, when the backend returned MessagePack that could be parsed as a map.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? ResponseBody { get; }

    /// <summary>
    /// Gets the decoded response body serialized to JSON for logging or UI display.
    /// </summary>
    public string? ResponseBodyJson { get; }

    private static string BuildMessage(
        string message,
        int statusCode,
        IReadOnlyDictionary<string, object?>? responseBody) {
        var detail = responseBody is null
            ? "no_response_body"
            : JsonSerializer.Serialize(responseBody);

        return $"{message} StatusCode={statusCode}. Response={detail}";
    }
}

/// <summary>
/// Raised when the backend rejects an app-session bootstrap request.
/// </summary>
public sealed class AppSessionFailureException : NuraAuthFailureException {
    public AppSessionFailureException(string message, int statusCode, IReadOnlyDictionary<string, object?>? responseBody)
        : base(message, statusCode, responseBody) {
    }
}

/// <summary>
/// Raised when the backend rejects a login, code verification, or stored-token validation request.
/// </summary>
public sealed class AuthenticationFailureException : NuraAuthFailureException {
    public AuthenticationFailureException(string message, int statusCode, IReadOnlyDictionary<string, object?>? responseBody)
        : base(message, statusCode, responseBody) {
    }
}

/// <summary>
/// Raised when the backend rejects the initial end-to-end device session request.
/// </summary>
public sealed class DeviceSessionStartFailureException : NuraAuthFailureException {
    public DeviceSessionStartFailureException(string message, int statusCode, IReadOnlyDictionary<string, object?>? responseBody)
        : base(message, statusCode, responseBody) {
    }
}

/// <summary>
/// Raised when the backend rejects an end-to-end provisioning continuation request.
/// </summary>
public sealed class ProvisioningContinuationFailureException : NuraAuthFailureException {
    public ProvisioningContinuationFailureException(string message, int statusCode, IReadOnlyDictionary<string, object?>? responseBody)
        : base(message, statusCode, responseBody) {
    }
}
