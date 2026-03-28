using System.Security.Cryptography;

using NuraLib.Configuration;

namespace NuraLib.Crypto;

public sealed class NuraSessionRuntime {
    public required byte[] Nonce { get; init; }

    public required NuraSessionCrypto Crypto { get; init; }

    public static NuraSessionRuntime Create(byte[] key, byte[] nonce) {
        if (key.Length != 16) {
            throw new InvalidOperationException("session key must be 16 bytes");
        }

        if (nonce.Length != 12) {
            throw new InvalidOperationException("session nonce must be 12 bytes");
        }

        return new NuraSessionRuntime {
            Nonce = nonce.ToArray(),
            Crypto = new NuraSessionCrypto(key.ToArray(), nonce.ToArray(), 1, 1)
        };
    }

    public static NuraSessionRuntime Create(NuraDeviceConfig device, byte[]? nonce = null) {
        return Create(device.GetRequiredDeviceKeyBytes(), nonce ?? GenerateFreshNonce());
    }

    public static byte[] GenerateFreshNonce() => RandomNumberGenerator.GetBytes(12);
}
