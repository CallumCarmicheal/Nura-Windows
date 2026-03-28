using System.Security.Cryptography;
using System.Text;

namespace NuraDesktopConsole.Library.Crypto;

internal sealed class NuraSessionCrypto {
    private static readonly byte[] ValidatePhrase = Encoding.ASCII.GetBytes("Kyle is awesome!");
    private static readonly UInt128 GHashReductionPolynomial = (UInt128)0xe100000000000000UL << 64;
    private readonly byte[] _key;
    private readonly byte[] _encryptCounterBlock;
    private readonly byte[] _decryptCounterBlock;
    private readonly byte[] _hashSubkey;

    public NuraSessionCrypto(byte[] key, byte[] nonce, uint encryptCounter, uint decryptCounter) {
        if (key.Length != 16) {
            throw new InvalidOperationException("AES key must be 16 bytes");
        }

        if (nonce.Length != 12) {
            throw new InvalidOperationException("session nonce must be 12 bytes");
        }

        _key = key.ToArray();
        _encryptCounterBlock = CreateCounterBlock(nonce, encryptCounter, decryptDirection: false);
        _decryptCounterBlock = CreateCounterBlock(nonce, decryptCounter, decryptDirection: true);
        _hashSubkey = EncryptBlock(new byte[16]);
    }

    public uint EncryptCounter => ReadCounter(_encryptCounterBlock);

    public uint DecryptCounter => ReadCounter(_decryptCounterBlock);

    public byte[] GenerateChallengeResponse(byte[] challenge) {
        if (challenge.Length != 16) {
            throw new InvalidOperationException("challenge must be 16 bytes");
        }

        return ComputeGmac(_encryptCounterBlock, challenge);
    }

    public bool ValidateResponse(byte[] responseGmac) {
        if (responseGmac.Length != 16) {
            return false;
        }

        var expected = ComputeGmac(_decryptCounterBlock, ValidatePhrase);
        return CryptographicOperations.FixedTimeEquals(expected, responseGmac);
    }

    public byte[] EncryptAuthenticated(byte[] payload) {
        var (tag, crypt) = EncryptAuthenticatedInternal(_encryptCounterBlock, payload);
        return ByteArray.Combine(tag, crypt);
    }

    public byte[] EncryptUnauthenticated(byte[] payload) {
        return ApplyCtr(payload, _encryptCounterBlock);
    }

    public byte[] DecryptAuthenticated(byte[] payload) {
        if (payload.Length < 16) {
            throw new InvalidOperationException("authenticated payload must contain a 16-byte tag");
        }

        var tag = payload[..16];
        var cipherText = payload[16..];
        return DecryptAuthenticatedInternal(_decryptCounterBlock, cipherText, tag);
    }

    public byte[] DecryptUnauthenticated(byte[] payload) {
        return ApplyCtr(payload, _decryptCounterBlock);
    }

    private (byte[] Tag, byte[] Crypt) EncryptAuthenticatedInternal(byte[] counterBlock, byte[] payload) {
        var j0 = counterBlock.ToArray();
        IncrementCounterBlock(counterBlock);
        var cipherText = ApplyCtr(payload, counterBlock);
        var tag = Xor16(EncryptBlock(j0), ComputeGHash(Array.Empty<byte>(), cipherText));
        return (tag, cipherText);
    }

    private byte[] DecryptAuthenticatedInternal(byte[] counterBlock, byte[] cipherText, byte[] tag) {
        var j0 = counterBlock.ToArray();
        IncrementCounterBlock(counterBlock);
        var expectedTag = Xor16(EncryptBlock(j0), ComputeGHash(Array.Empty<byte>(), cipherText));
        if (!CryptographicOperations.FixedTimeEquals(expectedTag, tag)) {
            throw new InvalidOperationException("authenticated packet MAC did not verify");
        }

        return ApplyCtr(cipherText, counterBlock);
    }

    private byte[] ComputeGmac(byte[] counterBlock, byte[] aad) {
        var j0 = counterBlock.ToArray();
        IncrementCounterBlock(counterBlock);
        return Xor16(EncryptBlock(j0), ComputeGHash(aad, Array.Empty<byte>()));
    }

    private byte[] ApplyCtr(byte[] payload, byte[] counterBlock) {
        var output = new byte[payload.Length];
        var offset = 0;

        while (offset + 16 <= payload.Length) {
            var keystream = EncryptBlock(counterBlock);
            for (var i = 0; i < 16; i++) {
                output[offset + i] = (byte)(payload[offset + i] ^ keystream[i]);
            }

            IncrementCounterBlock(counterBlock);
            offset += 16;
        }

        var remaining = payload.Length - offset;
        if (remaining > 0) {
            var keystream = EncryptBlock(counterBlock);
            for (var i = 0; i < remaining; i++) {
                output[offset + i] = (byte)(payload[offset + i] ^ keystream[i]);
            }
        }

        return output;
    }

    private byte[] ComputeGHash(byte[] aad, byte[] cipherText) {
        var accumulator = UInt128.Zero;
        var hashSubkey = ReadUInt128BigEndian(_hashSubkey);

        foreach (var block in EnumerateBlocks(aad)) {
            accumulator = MultiplyGalois(accumulator ^ ReadUInt128BigEndian(block), hashSubkey);
        }

        foreach (var block in EnumerateBlocks(cipherText)) {
            accumulator = MultiplyGalois(accumulator ^ ReadUInt128BigEndian(block), hashSubkey);
        }

        Span<byte> lengths = stackalloc byte[16];
        WriteUInt64BigEndian(lengths[..8], checked((ulong)aad.Length * 8));
        WriteUInt64BigEndian(lengths[8..], checked((ulong)cipherText.Length * 8));
        accumulator = MultiplyGalois(accumulator ^ ReadUInt128BigEndian(lengths), hashSubkey);

        return WriteUInt128BigEndian(accumulator);
    }

    private IEnumerable<byte[]> EnumerateBlocks(byte[] data) {
        for (var offset = 0; offset < data.Length; offset += 16) {
            var remaining = Math.Min(16, data.Length - offset);
            var block = new byte[16];
            Buffer.BlockCopy(data, offset, block, 0, remaining);
            yield return block;
        }
    }

    private byte[] EncryptBlock(byte[] block) {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        aes.Key = _key;

        using var encryptor = aes.CreateEncryptor();
        var output = new byte[16];
        encryptor.TransformBlock(block, 0, 16, output, 0);
        return output;
    }

    private static byte[] CreateCounterBlock(byte[] nonce, uint counter, bool decryptDirection) {
        var output = new byte[16];
        Buffer.BlockCopy(nonce, 0, output, 0, nonce.Length);
        output[12] = (byte)(((counter >> 24) & 0x7f) | (decryptDirection ? (uint)0x80 : 0u));
        output[13] = (byte)(counter >> 16);
        output[14] = (byte)(counter >> 8);
        output[15] = (byte)counter;
        return output;
    }

    private static uint ReadCounter(byte[] counterBlock) {
        return (uint)((counterBlock[12] & 0x7f) << 24 | counterBlock[13] << 16 | counterBlock[14] << 8 | counterBlock[15]);
    }

    private static void IncrementCounterBlock(byte[] counterBlock) {
        for (var index = 15; index >= 13; index--) {
            if (++counterBlock[index] != 0) {
                break;
            }
        }

        if (counterBlock[15] == 0 && counterBlock[14] == 0 && counterBlock[13] == 0) {
            counterBlock[12]++;
            if ((counterBlock[12] & 0x7f) == 0) {
                throw new InvalidOperationException("counter exhausted");
            }
        }
    }

    private static byte[] Xor16(byte[] left, byte[] right) {
        var output = new byte[16];
        for (var i = 0; i < 16; i++) {
            output[i] = (byte)(left[i] ^ right[i]);
        }

        return output;
    }

    private static UInt128 MultiplyGalois(UInt128 x, UInt128 y) {
        var z = UInt128.Zero;
        var v = x;
        for (var bit = 0; bit < 128; bit++) {
            if (((y >> (127 - bit)) & UInt128.One) != UInt128.Zero) {
                z ^= v;
            }

            v = (v & UInt128.One) != UInt128.Zero
                ? (v >> 1) ^ GHashReductionPolynomial
                : v >> 1;
        }

        return z;
    }

    private static UInt128 ReadUInt128BigEndian(ReadOnlySpan<byte> value) {
        var hi = ReadUInt64BigEndian(value[..8]);
        var lo = ReadUInt64BigEndian(value[8..]);
        return ((UInt128)hi << 64) | lo;
    }

    private static byte[] WriteUInt128BigEndian(UInt128 value) {
        var output = new byte[16];
        WriteUInt64BigEndian(output.AsSpan(0, 8), (ulong)(value >> 64));
        WriteUInt64BigEndian(output.AsSpan(8, 8), (ulong)value);
        return output;
    }

    private static ulong ReadUInt64BigEndian(ReadOnlySpan<byte> value) {
        ulong output = 0;
        foreach (var b in value) {
            output = (output << 8) | b;
        }

        return output;
    }

    private static void WriteUInt64BigEndian(Span<byte> destination, ulong value) {
        for (var index = destination.Length - 1; index >= 0; index--) {
            destination[index] = (byte)value;
            value >>= 8;
        }
    }
}
