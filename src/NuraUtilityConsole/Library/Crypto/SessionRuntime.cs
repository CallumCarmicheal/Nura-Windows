using System.Security.Cryptography;

namespace NuraDesktopConsole.Library.Crypto;

internal sealed class SessionRuntime {
    public required byte[] Nonce { get; init; }

    public required NuraSessionCrypto Crypto { get; init; }

    public static SessionRuntime Create(byte[] key, byte[] nonce) {
        if (key.Length != 16) {
            throw new InvalidOperationException("session key must be 16 bytes");
        }

        if (nonce.Length != 12) {
            throw new InvalidOperationException("session nonce must be 12 bytes");
        }

        return new SessionRuntime {
            Nonce = nonce.ToArray(),
            Crypto = new NuraSessionCrypto(key.ToArray(), nonce.ToArray(), 1, 1)
        };
    }

    public static SessionRuntime Create(Config.NuraOfflineConfig config) {
        var nonce = config.SessionNonce is { Length: 12 }
            ? config.SessionNonce.ToArray()
            : GenerateFreshNonce();

        return Create(config.DeviceKey, nonce);
    }

    public static SessionRuntime CreateWithFreshNonce(Config.NuraOfflineConfig config) {
        var nonce = GenerateFreshNonce();

        return Create(config.DeviceKey, nonce);
    }

    private static byte[] GenerateFreshNonce() => RandomNumberGenerator.GetBytes(12);
}
