using System.Text.Json;
using System.Text.RegularExpressions;

using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Crypto;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Library.Protocol;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionExtractLogcatAuth545LanguageChange : IAction {
    private static readonly Regex AppEncArrayRegex = new(
        @"app_enc.*?key:\s*\[([^\]]+)\].*?nonce:\s*\[([^\]]+)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AppEncJsonRegex = new(
        @"""app_enc""\s*:\s*\{.*?""key""\s*:\s*""([^""]+)""\s*,\s*""nonce""\s*:\s*""([^""]+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BtFrameDetailedRegex = new(
        @"\[net454\.bt\.(tx|rx)\]\s+len=(\d+)\s+hex=([0-9a-fA-F]+)(\.\.\.)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<ushort> AuthenticatedEncryptedCommands = [
        0x0006, 0x1006,
        0x0008, 0x1008,
        0x000f, 0x100f
    ];

    private static readonly HashSet<ushort> UnauthenticatedEncryptedCommands = [
        0x0007, 0x1007,
        0x0009, 0x1009,
        0x0010, 0x1010
    ];

    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        var logPath = ArgumentReader.RequiredValue(args, "--log-path");
        var outputRoot = ArgumentReader.OptionalValue(args, "--output-dir")
            ?? Path.Combine(
                "logs",
                $"logcat_auth545_language_change_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}");
        outputRoot = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(outputRoot);

        logger.WriteLine($"extract.log_path={Path.GetFullPath(logPath)}");
        logger.WriteLine($"extract.output_dir={outputRoot}");

        var result = await ExtractAsync(logPath, outputRoot, logger);
        var manifestPath = Path.Combine(outputRoot, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

        logger.WriteLine($"extract.manifest_path={manifestPath}");
        logger.WriteLine($"extract.stage_count={result.Stages.Count}");
        logger.WriteLine($"extract.encrypted_tx_count={result.EncryptedTxCount}");
        logger.WriteLine($"extract.truncated_tx_count={result.TruncatedTxCount}");
        logger.WriteLine($"extract.parse_failure_count={result.ParseFailureCount}");
        logger.WriteLine($"extract.auth_verify_failures.decrypt_direction={result.DecryptDirectionAuthFailures}");
        logger.WriteLine($"extract.auth_verify_failures.encrypt_direction={result.EncryptDirectionAuthFailures}");

        return 0;
    }

    private static async Task<ExtractionManifest> ExtractAsync(
        string logPath,
        string outputRoot,
        SessionLogger logger) {
        var stages = new List<StageBuilder>();
        var stageOccurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        StageBuilder? currentStage = null;
        CryptoAttempt? decryptDirection = null;
        CryptoAttempt? encryptDirection = null;
        AppEncContext? appEnc = null;
        var encryptedTxCount = 0;
        var truncatedTxCount = 0;
        var parseFailureCount = 0;

        await foreach (var rawLine in File.ReadLinesAsync(logPath)) {
            if (TryParseAppEnc(rawLine, out var parsedAppEnc)) {
                appEnc = parsedAppEnc;
                decryptDirection = new CryptoAttempt(
                    "decrypt_direction",
                    new NuraSessionCrypto(appEnc.Key, appEnc.Nonce, 1, 1),
                    useEncryptCounter: false);
                encryptDirection = new CryptoAttempt(
                    "encrypt_direction",
                    new NuraSessionCrypto(appEnc.Key, appEnc.Nonce, 1, 1),
                    useEncryptCounter: true);

                logger.WriteLine($"extract.app_enc.line={parsedAppEnc.LineHint}");
                logger.WriteLine($"extract.app_enc.key.hex={Hex.Format(appEnc.Key)}");
                logger.WriteLine($"extract.app_enc.nonce.hex={Hex.Format(appEnc.Nonce)}");
                continue;
            }

            if (StaticReplayCaptureSupport.TryParseBtCaptureStartReason(rawLine, out var reason)) {
                currentStage = null;
                if (reason.StartsWith("change_language", StringComparison.OrdinalIgnoreCase)) {
                    var occurrence = stageOccurrences.TryGetValue(reason, out var existingOccurrence)
                        ? existingOccurrence + 1
                        : 1;
                    stageOccurrences[reason] = occurrence;

                    var stageDirectory = Path.Combine(outputRoot, $"{occurrence:000}_{SanitizeFileName(reason)}");
                    Directory.CreateDirectory(stageDirectory);

                    currentStage = new StageBuilder(
                        reason,
                        occurrence,
                        stageDirectory,
                        appEnc?.Key,
                        appEnc?.Nonce);
                    stages.Add(currentStage);
                    logger.WriteLine($"extract.stage.start={reason}");
                    logger.WriteLine($"extract.stage.occurrence={occurrence}");
                    logger.WriteLine($"extract.stage.output_dir={stageDirectory}");
                }

                continue;
            }

            if (!TryParseBtFrame(rawLine, out var direction, out var expectedFrameLength, out var frameHex, out var truncated) ||
                !string.Equals(direction, "tx", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (truncated) {
                truncatedTxCount++;
                currentStage?.RegisterTruncatedFrame();
                continue;
            }

            byte[] frameBytes;
            try {
                frameBytes = Hex.Parse(frameHex);
            } catch (Exception ex) {
                parseFailureCount++;
                if (parseFailureCount <= 10) {
                    logger.WriteLine($"extract.warning.frame_hex_parse_failed={ex.Message}");
                }

                currentStage?.RegisterParseFailure();
                continue;
            }

            if (frameBytes.Length != expectedFrameLength) {
                truncatedTxCount++;
                currentStage?.RegisterTruncatedFrame();
                continue;
            }

            GaiaResponse frame;
            try {
                frame = GaiaResponse.Parse(frameBytes);
            } catch (Exception ex) {
                parseFailureCount++;
                if (parseFailureCount <= 10) {
                    logger.WriteLine($"extract.warning.frame_parse_failed={ex.Message}");
                }

                currentStage?.RegisterParseFailure();
                continue;
            }

            var rawCommandId = frame.RawCommandId;
            var encrypted = IsEncryptedCommand(rawCommandId);
            if (!encrypted) {
                currentStage?.RegisterPlainFrame(rawCommandId, frame.Payload.Length);
                continue;
            }

            encryptedTxCount++;
            var authenticated = IsAuthenticatedCommand(rawCommandId);
            var payload = frame.Payload;
            currentStage?.AppendRaw(rawCommandId, authenticated, payload);

            if (decryptDirection is not null) {
                var plain = decryptDirection.TryDecrypt(payload, authenticated);
                if (plain is not null) {
                    currentStage?.AppendDecrypted(decryptDirection.Name, authenticated, plain);
                }
            }

            if (encryptDirection is not null) {
                var plain = encryptDirection.TryDecrypt(payload, authenticated);
                if (plain is not null) {
                    currentStage?.AppendDecrypted(encryptDirection.Name, authenticated, plain);
                }
            }
        }

        var stageManifests = new List<StageManifest>();
        foreach (var stage in stages) {
            stageManifests.Add(await stage.FlushAsync());
        }

        return new ExtractionManifest(
            LogPath: Path.GetFullPath(logPath),
            OutputDirectory: outputRoot,
            EncryptedTxCount: encryptedTxCount,
            TruncatedTxCount: truncatedTxCount,
            ParseFailureCount: parseFailureCount,
            DecryptDirectionAuthFailures: decryptDirection?.AuthFailures ?? 0,
            EncryptDirectionAuthFailures: encryptDirection?.AuthFailures ?? 0,
            Stages: stageManifests);
    }

    private static bool TryParseBtFrame(
        string line,
        out string direction,
        out int expectedFrameLength,
        out string frameHex,
        out bool truncated) {
        var match = BtFrameDetailedRegex.Match(line);
        if (!match.Success) {
            direction = string.Empty;
            expectedFrameLength = 0;
            frameHex = string.Empty;
            truncated = false;
            return false;
        }

        direction = match.Groups[1].Value;
        expectedFrameLength = int.Parse(match.Groups[2].Value);
        frameHex = match.Groups[3].Value;
        truncated = match.Groups[4].Success || (frameHex.Length / 2) < expectedFrameLength;
        return true;
    }

    private static bool TryParseAppEnc(string line, out AppEncContext context) {
        var arrayMatch = AppEncArrayRegex.Match(line);
        if (arrayMatch.Success) {
            context = new AppEncContext(
                ParseByteArray(arrayMatch.Groups[1].Value),
                ParseByteArray(arrayMatch.Groups[2].Value),
                "array");
            return true;
        }

        var jsonMatch = AppEncJsonRegex.Match(line);
        if (jsonMatch.Success) {
            context = new AppEncContext(
                Convert.FromBase64String(jsonMatch.Groups[1].Value),
                Convert.FromBase64String(jsonMatch.Groups[2].Value),
                "json");
            return true;
        }

        context = default!;
        return false;
    }

    private static byte[] ParseByteArray(string raw) {
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => checked((byte)int.Parse(value)))
            .ToArray();
    }

    private static bool IsEncryptedCommand(ushort rawCommandId)
        => AuthenticatedEncryptedCommands.Contains(rawCommandId) ||
           UnauthenticatedEncryptedCommands.Contains(rawCommandId);

    private static bool IsAuthenticatedCommand(ushort rawCommandId)
        => AuthenticatedEncryptedCommands.Contains(rawCommandId);

    private static string SanitizeFileName(string value) {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private sealed record AppEncContext(byte[] Key, byte[] Nonce, string LineHint);

    private sealed class CryptoAttempt {
        public CryptoAttempt(string name, NuraSessionCrypto crypto, bool useEncryptCounter) {
            Name = name;
            Crypto = crypto;
            UseEncryptCounter = useEncryptCounter;
        }

        public string Name { get; }

        public int AuthFailures { get; private set; }

        private NuraSessionCrypto Crypto { get; }

        private bool UseEncryptCounter { get; }

        public byte[]? TryDecrypt(byte[] payload, bool authenticated) {
            try {
                if (authenticated) {
                    return UseEncryptCounter
                        ? Crypto.DecryptAuthenticatedFromEncryptCounter(payload)
                        : Crypto.DecryptAuthenticated(payload);
                }

                return UseEncryptCounter
                    ? Crypto.DecryptUnauthenticatedFromEncryptCounter(payload)
                    : Crypto.DecryptUnauthenticated(payload);
            } catch (InvalidOperationException) {
                if (authenticated) {
                    AuthFailures++;
                }

                return null;
            }
        }
    }

    private sealed class StageBuilder {
        private readonly MemoryStream _rawAll = new();
        private readonly MemoryStream _rawAuth = new();
        private readonly MemoryStream _rawUnauth = new();
        private readonly Dictionary<string, MemoryStream> _decryptedAll = new(StringComparer.Ordinal);
        private readonly Dictionary<string, MemoryStream> _decryptedUnauth = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _commandCounts = new(StringComparer.Ordinal);

        public StageBuilder(
            string name,
            int occurrence,
            string outputDirectory,
            byte[]? key,
            byte[]? nonce) {
            Name = name;
            Occurrence = occurrence;
            OutputDirectory = outputDirectory;
            KeyHex = key is null ? null : Hex.Format(key);
            NonceHex = nonce is null ? null : Hex.Format(nonce);
        }

        private string Name { get; }

        private int Occurrence { get; }

        private string OutputDirectory { get; }

        private string? KeyHex { get; }

        private string? NonceHex { get; }

        private int PlainFrameCount { get; set; }

        private int EncryptedFrameCount { get; set; }

        private int AuthenticatedFrameCount { get; set; }

        private int UnauthenticatedFrameCount { get; set; }

        private int TruncatedFrameCount { get; set; }

        private int ParseFailureCount { get; set; }

        public void RegisterTruncatedFrame() {
            TruncatedFrameCount++;
        }

        public void RegisterParseFailure() {
            ParseFailureCount++;
        }

        public void RegisterPlainFrame(ushort rawCommandId, int payloadLength) {
            PlainFrameCount++;
            IncrementCommand(rawCommandId);
        }

        public void AppendRaw(ushort rawCommandId, bool authenticated, byte[] payload) {
            EncryptedFrameCount++;
            IncrementCommand(rawCommandId);
            _rawAll.Write(payload);
            if (authenticated) {
                AuthenticatedFrameCount++;
                _rawAuth.Write(payload);
            } else {
                UnauthenticatedFrameCount++;
                _rawUnauth.Write(payload);
            }
        }

        public void AppendDecrypted(string attemptName, bool authenticated, byte[] payload) {
            Write(_decryptedAll, attemptName, payload);
            if (!authenticated) {
                Write(_decryptedUnauth, attemptName, payload);
            }
        }

        public async Task<StageManifest> FlushAsync() {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            await WriteFileAsync("raw_all.bin", _rawAll, files);
            await WriteFileAsync("raw_auth.bin", _rawAuth, files);
            await WriteFileAsync("raw_unauth.bin", _rawUnauth, files);

            foreach (var (attemptName, stream) in _decryptedAll) {
                await WriteFileAsync($"{attemptName}_all.bin", stream, files);
            }

            foreach (var (attemptName, stream) in _decryptedUnauth) {
                await WriteFileAsync($"{attemptName}_unauth.bin", stream, files);
            }

            var manifest = new StageManifest(
                Name,
                Occurrence,
                OutputDirectory,
                KeyHex,
                NonceHex,
                PlainFrameCount,
                EncryptedFrameCount,
                AuthenticatedFrameCount,
                UnauthenticatedFrameCount,
                TruncatedFrameCount,
                ParseFailureCount,
                _rawAll.Length,
                _rawUnauth.Length,
                new SortedDictionary<string, int>(_commandCounts, StringComparer.Ordinal),
                files);

            await File.WriteAllTextAsync(
                Path.Combine(OutputDirectory, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            return manifest;
        }

        private void IncrementCommand(ushort rawCommandId) {
            var key = $"0x{rawCommandId:x4}";
            _commandCounts[key] = _commandCounts.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        private static void Write(Dictionary<string, MemoryStream> streams, string key, byte[] payload) {
            if (!streams.TryGetValue(key, out var stream)) {
                stream = new MemoryStream();
                streams[key] = stream;
            }

            stream.Write(payload);
        }

        private async Task WriteFileAsync(string fileName, MemoryStream stream, Dictionary<string, string> files) {
            var path = Path.Combine(OutputDirectory, fileName);
            await File.WriteAllBytesAsync(path, stream.ToArray());
            files[fileName] = path;
        }
    }

    private sealed record ExtractionManifest(
        string LogPath,
        string OutputDirectory,
        int EncryptedTxCount,
        int TruncatedTxCount,
        int ParseFailureCount,
        int DecryptDirectionAuthFailures,
        int EncryptDirectionAuthFailures,
        IReadOnlyList<StageManifest> Stages);

    private sealed record StageManifest(
        string Name,
        int Occurrence,
        string OutputDirectory,
        string? KeyHex,
        string? NonceHex,
        int PlainFrameCount,
        int EncryptedFrameCount,
        int AuthenticatedFrameCount,
        int UnauthenticatedFrameCount,
        int TruncatedFrameCount,
        int ParseFailureCount,
        long RawAllBytes,
        long RawUnauthBytes,
        IReadOnlyDictionary<string, int> CommandCounts,
        IReadOnlyDictionary<string, string> Files);
}
