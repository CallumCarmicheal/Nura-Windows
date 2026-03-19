using System.Security.Cryptography;

namespace desktop_app.Crypto;

internal sealed class SessionRuntime {
    public required byte[] Nonce { get; init; }

    public required NuraSessionCrypto Crypto { get; init; }

    public static SessionRuntime Create(Config.NuraOfflineConfig config) {
        var nonce = config.SessionNonce is { Length: 12 }
            ? config.SessionNonce.ToArray()
            : GenerateFreshNonce();

        return new SessionRuntime {
            Nonce = nonce,
            Crypto = new NuraSessionCrypto(config.DeviceKey, nonce, 1, 1)
        };
    }

    public static SessionRuntime CreateWithFreshNonce(Config.NuraOfflineConfig config) {
        var nonce = GenerateFreshNonce();

        return new SessionRuntime {
            Nonce = nonce,
            Crypto = new NuraSessionCrypto(config.DeviceKey, nonce, 1, 1)
        };
    }

    private static byte[] GenerateFreshNonce() => RandomNumberGenerator.GetBytes(12);
}
