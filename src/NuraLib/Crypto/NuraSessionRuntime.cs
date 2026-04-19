using System.Security.Cryptography;

using NuraLib.Configuration;

namespace NuraLib.Crypto;

/// <summary>
/// Holds the active crypto runtime for a local Nura session.
/// </summary>
public sealed class NuraSessionRuntime {
    /// <summary>
    /// Gets the 12-byte session nonce.
    /// </summary>
    public required byte[] Nonce { get; init; }

    /// <summary>
    /// Gets the session crypto instance built from the current key and nonce.
    /// </summary>
    public required NuraSessionCrypto Crypto { get; init; }

    /// <summary>
    /// Creates a runtime from a raw device key and session nonce.
    /// </summary>
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

    /// <summary>
    /// Creates a runtime for a configured device using either the supplied nonce or a newly generated nonce.
    /// </summary>
    public static NuraSessionRuntime Create(NuraDeviceConfig device, byte[]? nonce = null) {
        return Create(device.GetRequiredDeviceKeyBytes(), nonce ?? GenerateFreshNonce());
    }

    /// <summary>
    /// Generates a fresh 12-byte session nonce.
    /// </summary>
    public static byte[] GenerateFreshNonce() => RandomNumberGenerator.GetBytes(12);
}
