using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Crypto;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionExtractFridaFolderLanguageProbe : IAction {
    private static readonly Regex AppEncArrayRegex = new(
        @"app_enc.*?key:\s*\[([^\]]+)\].*?nonce:\s*\[([^\]]+)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PacketRegex = new(
        @"\{e:\s*(true|false),\s*a:\s*(true|false),\s*b:\s*\[([^\]]*)\]\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        var folder = Path.GetFullPath(ArgumentReader.RequiredValue(args, "--folder"));
        var outputDirectory = Path.GetFullPath(
            ArgumentReader.OptionalValue(args, "--output-dir") ??
            Path.Combine(folder, "crypto_probe"));
        Directory.CreateDirectory(outputDirectory);

        logger.WriteLine($"extract.frida_folder={folder}");
        logger.WriteLine($"extract.output_dir={outputDirectory}");

        var events = Directory.GetFiles(folder, "*.json")
            .Select(ReadEvent)
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.FileName, StringComparer.Ordinal)
            .ToArray();

        var firstLanguageIndex = Array.FindIndex(
            events,
            item => string.Equals(item.Endpoint, "end_to_end/change_language", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(item.Kind, "resp", StringComparison.OrdinalIgnoreCase));
        if (firstLanguageIndex < 0) {
            throw new InvalidOperationException("could not find end_to_end/change_language response in folder");
        }

        var appEncEvent = events
            .Take(firstLanguageIndex)
            .LastOrDefault(item => string.Equals(item.Endpoint, "end_to_end/session/start_4", StringComparison.OrdinalIgnoreCase) &&
                                   string.Equals(item.Kind, "resp", StringComparison.OrdinalIgnoreCase) &&
                                   TryParseAppEnc(item.Raw, out _));
        if (appEncEvent is null || !TryParseAppEnc(appEncEvent.Raw, out var appEnc)) {
            throw new InvalidOperationException("could not find app_enc before language-change response");
        }

        logger.WriteLine($"extract.app_enc.source={appEncEvent.FileName}");
        logger.WriteLine($"extract.app_enc.key.hex={Hex.Format(appEnc.Key)}");
        logger.WriteLine($"extract.app_enc.nonce.hex={Hex.Format(appEnc.Nonce)}");

        var languageEvents = events
            .Skip(firstLanguageIndex)
            .Where(item => string.Equals(item.Kind, "resp", StringComparison.OrdinalIgnoreCase) &&
                           item.Endpoint.StartsWith("end_to_end/change_language", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var packets = languageEvents
            .SelectMany(item => ParsePackets(item.Raw).Select(packet => packet with {
                Endpoint = item.Endpoint,
                SourceFile = item.FileName
            }))
            .ToArray();
        var encryptedPackets = packets.Where(packet => packet.Encrypted).ToArray();
        var authenticatedPackets = encryptedPackets.Where(packet => packet.Authenticated).ToArray();

        logger.WriteLine($"extract.language_response_count={languageEvents.Length}");
        logger.WriteLine($"extract.packet_count={packets.Length}");
        logger.WriteLine($"extract.encrypted_packet_count={encryptedPackets.Length}");
        logger.WriteLine($"extract.authenticated_packet_count={authenticatedPackets.Length}");

        var bruteResults = ProbeAuthenticatedCounters(appEnc, authenticatedPackets.Take(16).ToArray()).ToArray();
        foreach (var result in bruteResults.Take(20)) {
            logger.WriteLine(
                $"extract.auth_probe.hit direction={result.Direction} start_counter={result.StartCounter} verified={result.VerifiedCount} first_plain_hex={result.FirstPlainHex}");
        }

        if (bruteResults.Length == 0) {
            logger.WriteLine("extract.auth_probe.result=no_authenticated_packets_verified_for_counters_0_to_4096");
        }

        var replayResults = new List<ReplayResult>();
        replayResults.Add(Replay("decrypt_counter_start_1", appEnc, encryptedPackets, useEncryptCounter: false, counter: 1, outputDirectory));
        replayResults.Add(Replay("encrypt_counter_start_1", appEnc, encryptedPackets, useEncryptCounter: true, counter: 1, outputDirectory));

        foreach (var result in replayResults) {
            logger.WriteLine(
                $"extract.replay.name={result.Name} auth_ok={result.AuthOk} auth_fail={result.AuthFail} unauth_bytes={result.UnauthBytes} unauth_sha256={result.UnauthSha256} printable_ratio={result.PrintableRatio:F4}");
            logger.WriteLine($"extract.replay.unauth_path={result.UnauthPath}");
        }

        var manifest = new ProbeManifest(
            Folder: folder,
            OutputDirectory: outputDirectory,
            AppEncSource: appEncEvent.FileName,
            KeyHex: Hex.Format(appEnc.Key),
            NonceHex: Hex.Format(appEnc.Nonce),
            LanguageResponses: languageEvents.Select(item => item.FileName).ToArray(),
            PacketCount: packets.Length,
            EncryptedPacketCount: encryptedPackets.Length,
            AuthenticatedPacketCount: authenticatedPackets.Length,
            AuthProbeHits: bruteResults,
            Replays: replayResults);

        var manifestPath = Path.Combine(outputDirectory, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        logger.WriteLine($"extract.manifest_path={manifestPath}");

        return 0;
    }

    private static FridaApiEvent? ReadEvent(string path) {
        try {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            return new FridaApiEvent(
                FileName: Path.GetFileName(path),
                Path: path,
                Kind: root.TryGetProperty("kind", out var kind) ? kind.GetString() ?? string.Empty : string.Empty,
                Endpoint: root.TryGetProperty("endpoint", out var endpoint) ? endpoint.GetString() ?? string.Empty : string.Empty,
                Raw: root.TryGetProperty("raw", out var raw) ? raw.GetString() ?? string.Empty : string.Empty);
        } catch (JsonException) {
            return null;
        }
    }

    private static bool TryParseAppEnc(string raw, out AppEncContext context) {
        var match = AppEncArrayRegex.Match(raw);
        if (!match.Success) {
            context = default!;
            return false;
        }

        context = new AppEncContext(
            ParseByteArray(match.Groups[1].Value),
            ParseByteArray(match.Groups[2].Value));
        return true;
    }

    private static IReadOnlyList<PacketRecord> ParsePackets(string raw) {
        var packets = new List<PacketRecord>();
        foreach (Match match in PacketRegex.Matches(raw)) {
            packets.Add(new PacketRecord(
                Endpoint: string.Empty,
                SourceFile: string.Empty,
                Encrypted: string.Equals(match.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase),
                Authenticated: string.Equals(match.Groups[2].Value, "true", StringComparison.OrdinalIgnoreCase),
                Bytes: ParseByteArray(match.Groups[3].Value)));
        }

        return packets;
    }

    private static IEnumerable<AuthProbeHit> ProbeAuthenticatedCounters(
        AppEncContext appEnc,
        IReadOnlyList<PacketRecord> authenticatedPackets) {
        if (authenticatedPackets.Count == 0) {
            yield break;
        }

        foreach (var direction in new[] { "decrypt", "encrypt" }) {
            var useEncryptCounter = string.Equals(direction, "encrypt", StringComparison.Ordinal);
            for (uint startCounter = 0; startCounter <= 4096; startCounter++) {
                var crypto = new NuraSessionCrypto(appEnc.Key, appEnc.Nonce, startCounter, startCounter);
                var verified = 0;
                byte[]? firstPlain = null;

                foreach (var packet in authenticatedPackets) {
                    try {
                        var plain = useEncryptCounter
                            ? crypto.DecryptAuthenticatedFromEncryptCounter(packet.Bytes)
                            : crypto.DecryptAuthenticated(packet.Bytes);
                        verified++;
                        firstPlain ??= plain;
                    } catch (InvalidOperationException) {
                        break;
                    }
                }

                if (verified > 0) {
                    yield return new AuthProbeHit(
                        Direction: direction,
                        StartCounter: startCounter,
                        VerifiedCount: verified,
                        FirstPlainHex: firstPlain is null ? string.Empty : Hex.Format(firstPlain));
                }
            }
        }
    }

    private static ReplayResult Replay(
        string name,
        AppEncContext appEnc,
        IReadOnlyList<PacketRecord> encryptedPackets,
        bool useEncryptCounter,
        uint counter,
        string outputDirectory) {
        var crypto = new NuraSessionCrypto(appEnc.Key, appEnc.Nonce, counter, counter);
        var authOk = 0;
        var authFail = 0;
        using var unauthStream = new MemoryStream();

        foreach (var packet in encryptedPackets) {
            try {
                byte[] plain;
                if (packet.Authenticated) {
                    plain = useEncryptCounter
                        ? crypto.DecryptAuthenticatedFromEncryptCounter(packet.Bytes)
                        : crypto.DecryptAuthenticated(packet.Bytes);
                    authOk++;
                } else {
                    plain = useEncryptCounter
                        ? crypto.DecryptUnauthenticatedFromEncryptCounter(packet.Bytes)
                        : crypto.DecryptUnauthenticated(packet.Bytes);
                    unauthStream.Write(plain);
                }
            } catch (InvalidOperationException) {
                if (packet.Authenticated) {
                    authFail++;
                }
            }
        }

        var unauth = unauthStream.ToArray();
        var unauthPath = Path.Combine(outputDirectory, $"{name}.unauth_plain.bin");
        File.WriteAllBytes(unauthPath, unauth);

        return new ReplayResult(
            Name: name,
            AuthOk: authOk,
            AuthFail: authFail,
            UnauthBytes: unauth.Length,
            UnauthSha256: Convert.ToHexString(SHA256.HashData(unauth)),
            PrintableRatio: ComputePrintableRatio(unauth),
            UnauthPath: unauthPath);
    }

    private static double ComputePrintableRatio(byte[] bytes) {
        if (bytes.Length == 0) {
            return 0;
        }

        var printable = bytes.Count(value => value is >= 0x20 and <= 0x7e or 0x09 or 0x0a or 0x0d);
        return (double)printable / bytes.Length;
    }

    private static byte[] ParseByteArray(string raw) {
        if (string.IsNullOrWhiteSpace(raw)) {
            return [];
        }

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => checked((byte)int.Parse(value)))
            .ToArray();
    }

    private sealed record FridaApiEvent(
        string FileName,
        string Path,
        string Kind,
        string Endpoint,
        string Raw);

    private sealed record AppEncContext(byte[] Key, byte[] Nonce);

    private sealed record PacketRecord(
        string Endpoint,
        string SourceFile,
        bool Encrypted,
        bool Authenticated,
        byte[] Bytes);

    private sealed record AuthProbeHit(
        string Direction,
        uint StartCounter,
        int VerifiedCount,
        string FirstPlainHex);

    private sealed record ReplayResult(
        string Name,
        int AuthOk,
        int AuthFail,
        long UnauthBytes,
        string UnauthSha256,
        double PrintableRatio,
        string UnauthPath);

    private sealed record ProbeManifest(
        string Folder,
        string OutputDirectory,
        string AppEncSource,
        string KeyHex,
        string NonceHex,
        IReadOnlyList<string> LanguageResponses,
        int PacketCount,
        int EncryptedPacketCount,
        int AuthenticatedPacketCount,
        IReadOnlyList<AuthProbeHit> AuthProbeHits,
        IReadOnlyList<ReplayResult> Replays);
}
