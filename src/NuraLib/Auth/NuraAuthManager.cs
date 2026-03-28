namespace NuraLib.Auth;

public sealed class NuraAuthManager {
    private readonly NuraConfigState _state;

    internal NuraAuthManager(NuraConfigState state) {
        _state = state;
    }

    public bool HasStoredCredentials => _state.Configuration.Auth.HasAuthenticatedSession;

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

    public Task RequestEmailCodeAsync(string email, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("Email-code auth has not been wired into NuraLib yet.");
    }

    public Task VerifyEmailCodeAsync(string code, string? email = null, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("Email-code verification has not been wired into NuraLib yet.");
    }

    public Task ResumeAsync(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("Auth session resume has not been wired into NuraLib yet.");
    }
}
