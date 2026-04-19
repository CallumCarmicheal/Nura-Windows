using System.Text.Json;

using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Library.Nura.Auth;

internal static class AutomatedEntryDumpSupport {
    internal sealed record AutomatedEntryDumpResult(
        int ExitCode,
        string OutputDirectory,
        int ActionCount,
        int PacketCount);

    public static async Task<AutomatedEntryDumpResult> ExecuteAsync(
        string logPrefix,
        string outputPrefix,
        string endpoint,
        IReadOnlyDictionary<string, object?>? additionalPayload,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> packets,
        int? bluetoothSessionOverride,
        int? firmwareVersionOverride,
        string? outputRootOverride,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        var authPath = LocalStateFiles.LoadAuthPath(logger);
        var state = NuraAuthState.LoadOrCreate(authPath);

        logger.WriteLine($"{logPrefix}.endpoint={endpoint}");
        logger.WriteLine($"{logPrefix}.session_override={(bluetoothSessionOverride is null ? string.Empty : bluetoothSessionOverride.Value.ToString())}");
        logger.WriteLine($"{logPrefix}.firmware_version_override={(firmwareVersionOverride is null ? string.Empty : firmwareVersionOverride.Value.ToString())}");
        logger.WriteLine($"{logPrefix}.additional_payload_json={JsonSerializer.Serialize(additionalPayload ?? new Dictionary<string, object?>(), JsonOptions())}");
        logger.WriteLine($"{logPrefix}.request_packets={packets.Count}");
        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.uuid={state.Uuid}");
        logger.WriteLine($"auth.auth_uid={state.AuthUid ?? string.Empty}");
        AuthStateSupport.LogSessionState(state, logger);

        if (!state.HasAuthenticatedSession) {
            throw new InvalidOperationException($"{outputPrefix} requires an authenticated session");
        }

        if (firmwareVersionOverride is not null) {
            state = await RefreshSessionStartForFirmwareOverrideAsync(
                state,
                authPath,
                firmwareVersionOverride.Value,
                logPrefix,
                logger,
                cancellationToken);
        }

        var bluetoothSessionId = bluetoothSessionOverride ?? state.BluetoothSessionId;
        if (bluetoothSessionId is not { } resolvedBluetoothSessionId) {
            throw new InvalidOperationException($"{outputPrefix} requires a saved bluetooth session id");
        }

        using var client = new NuraAuthApiClient(logger);
        var result = await client.AutomatedEntryAsync(
            state,
            endpoint,
            resolvedBluetoothSessionId,
            packets,
            additionalPayload,
            cancellationToken);

        state = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
        state.Save(authPath);

        LogResult(logPrefix, result, logger);

        return await DumpResultAsync(
            logPrefix,
            outputPrefix,
            endpoint,
            result,
            outputRootOverride,
            logger,
            cancellationToken);
    }

    public static async Task<AutomatedEntryDumpResult> DumpResultAsync(
        string logPrefix,
        string outputPrefix,
        string endpoint,
        AuthCallResult result,
        string? outputRootOverride,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        logger.BeginSection($"dump {outputPrefix}:{endpoint}");
        try {
            if (result.DecodedBody is not null) {
                LogErrors(logPrefix, result.DecodedBody, logger);
                LogActionTrace($"{logPrefix}.trace", result.DecodedBody, logger);
            }

            var outputDir = ResolveOutputDirectory(logger, outputRootOverride, outputPrefix, endpoint);
            Directory.CreateDirectory(outputDir);
            logger.WriteLine($"{logPrefix}.output_dir={outputDir}");

            await File.WriteAllBytesAsync(Path.Combine(outputDir, "response.msgpack"), result.RawResponseBytes, cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(outputDir, "response.json"),
                JsonSerializer.Serialize(result.DecodedBody, JsonOptions()),
                cancellationToken);

            var dump = result.DecodedBody is null
                ? AutomatedActionDump.Empty
                : AutomatedActionDumpParser.Parse(result.DecodedBody);

            var actionsManifest = dump.Actions.Select(action => new {
                action.ActionIndex,
                action.Type,
                packet_count = action.Packets.Count,
                json = JsonSerializer.Deserialize<object?>(action.Json)
            }).ToArray();

            await File.WriteAllTextAsync(
                Path.Combine(outputDir, "actions.json"),
                JsonSerializer.Serialize(actionsManifest, JsonOptions()),
                cancellationToken);

            var packetsDir = Path.Combine(outputDir, "packets");
            Directory.CreateDirectory(packetsDir);

            var packetManifest = new List<object>();
            foreach (var packet in dump.Packets) {
                var baseName = $"action_{packet.ActionIndex:D3}_{packet.Role}_{packet.PacketIndex:D3}_{packet.ActionType}";
                var binaryPath = Path.Combine(packetsDir, $"{baseName}.bin");
                var metaPath = Path.Combine(packetsDir, $"{baseName}.json");

                await File.WriteAllBytesAsync(binaryPath, packet.Binary, cancellationToken);
                await File.WriteAllTextAsync(
                    metaPath,
                    JsonSerializer.Serialize(new {
                        packet.ActionIndex,
                        packet.PacketIndex,
                        packet.Role,
                        packet.ActionType,
                        packet.Encrypted,
                        packet.Authenticated,
                        packet.BinaryHex,
                        json = JsonSerializer.Deserialize<object?>(packet.Json)
                    }, JsonOptions()),
                    cancellationToken);

                packetManifest.Add(new {
                    packet.ActionIndex,
                    packet.PacketIndex,
                    packet.Role,
                    packet.ActionType,
                    packet.Encrypted,
                    packet.Authenticated,
                    packet.BinaryHex,
                    BinaryFile = Path.GetFileName(binaryPath),
                    MetaFile = Path.GetFileName(metaPath)
                });
            }

            await File.WriteAllTextAsync(
                Path.Combine(outputDir, "packet_manifest.json"),
                JsonSerializer.Serialize(packetManifest, JsonOptions()),
                cancellationToken);

            logger.WriteLine($"{logPrefix}.actions={dump.Actions.Count}");
            logger.WriteLine($"{logPrefix}.packets={dump.Packets.Count}");
            logger.WriteLine($"{logPrefix}.response_msgpack_path={Path.Combine(outputDir, "response.msgpack")}");
            logger.WriteLine($"{logPrefix}.response_json_path={Path.Combine(outputDir, "response.json")}");
            logger.WriteLine($"{logPrefix}.actions_json_path={Path.Combine(outputDir, "actions.json")}");
            logger.WriteLine($"{logPrefix}.packet_manifest_path={Path.Combine(outputDir, "packet_manifest.json")}");

            if (!dump.Packets.Any()) {
                logger.WriteLine($"{logPrefix}.result=no_packets_found");
                return new AutomatedEntryDumpResult(
                    result.IsSuccessStatusCode ? 0 : 1,
                    outputDir,
                    dump.Actions.Count,
                    dump.Packets.Count);
            }

            logger.WriteLine($"{logPrefix}.result=packets_dumped");
            return new AutomatedEntryDumpResult(0, outputDir, dump.Actions.Count, dump.Packets.Count);
        } finally {
            logger.EndSection($"dump {outputPrefix}:{endpoint}");
        }
    }

    public static void LogResult(string logPrefix, AuthCallResult result, SessionLogger logger) {
        logger.WriteLine($"{logPrefix}.result_status={result.StatusCode}");
        logger.WriteLine($"{logPrefix}.success={result.IsSuccessStatusCode}");
    }

    public static Task<AutomatedEntryDumpResult> ExecuteAsync(
        string logPrefix,
        string outputPrefix,
        string endpoint,
        string language,
        int? bluetoothSessionOverride,
        int? firmwareVersionOverride,
        string? outputRootOverride,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        return ExecuteAsync(
            logPrefix,
            outputPrefix,
            endpoint,
            additionalPayload: new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["language"] = language
            },
            packets: [],
            bluetoothSessionOverride,
            firmwareVersionOverride,
            outputRootOverride,
            logger,
            cancellationToken);
    }

    private static async Task<NuraAuthState> RefreshSessionStartForFirmwareOverrideAsync(
        NuraAuthState state,
        string authPath,
        int firmwareVersion,
        string logPrefix,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        var config = LocalStateFiles.LoadConfig(logger);
        var maxPacketLength = HeadsetSupport.GetMaxPacketLengthHint(config.SerialNumber);
        const int maxBulkPacketLength = 0;
        var userSessionId = state.UserSessionId
            ?? throw new InvalidOperationException("firmware override requires a saved user session id");

        logger.WriteLine($"{logPrefix}.session_start_override=true");
        logger.WriteLine($"{logPrefix}.session_start.serial={config.SerialNumber}");
        logger.WriteLine($"{logPrefix}.session_start.firmware_version={firmwareVersion}");
        logger.WriteLine($"{logPrefix}.session_start.max_packet_length={maxPacketLength}");
        logger.WriteLine($"{logPrefix}.session_start.max_bulk_packet_length={maxBulkPacketLength}");
        logger.WriteLine($"{logPrefix}.session_start.usid={userSessionId}");

        using var client = new NuraAuthApiClient(logger);
        var result = await client.SessionStartAsync(
            state,
            config.SerialNumber,
            firmwareVersion,
            maxPacketLength,
            maxBulkPacketLength,
            userSessionId,
            cancellationToken);

        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
        updatedState.Save(authPath);

        logger.WriteLine($"{logPrefix}.session_start.result_status={result.StatusCode}");
        logger.WriteLine($"{logPrefix}.session_start.success={result.IsSuccessStatusCode}");
        if (!result.IsSuccessStatusCode) {
            throw new InvalidOperationException($"session/start failed with status {result.StatusCode}");
        }

        if (result.DecodedBody is not null) {
            logger.WriteLine($"{logPrefix}.session_start.response.summary={AuthStateSupport.SummarizeSessionStartResponse(result.DecodedBody)}");
        }

        AuthStateSupport.LogSessionState(updatedState, logger);
        return updatedState;
    }

    private static string ResolveOutputDirectory(
        SessionLogger logger,
        string? overridePath,
        string outputPrefix,
        string endpoint) {
        if (!string.IsNullOrWhiteSpace(overridePath)) {
            return Path.GetFullPath(overridePath);
        }

        var logRoot = Path.GetDirectoryName(logger.LogPath)
            ?? Directory.GetCurrentDirectory();
        var safeEndpoint = string.Concat(endpoint.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        return Path.Combine(logRoot, $"{outputPrefix}_{safeEndpoint}_{timestamp}");
    }

    private static void LogActionTrace(string prefix, Dictionary<string, object?> responseBody, SessionLogger logger) {
        var trace = AutomatedActionTraceParser.Parse(responseBody);
        if (trace == AutomatedActionTraceSummary.Empty) {
            logger.WriteLine($"{prefix}.present=false");
            return;
        }

        logger.WriteLine($"{prefix}.present=true");
        logger.WriteLine($"{prefix}.action_count={trace.ActionCount}");
        logger.WriteLine($"{prefix}.run_count={trace.RunCount}");
        logger.WriteLine($"{prefix}.run_with_app_encrypted_response_count={trace.AppEncryptedResponseRunCount}");
        logger.WriteLine($"{prefix}.run_unencrypted_count={trace.UnencryptedRunCount}");
        logger.WriteLine($"{prefix}.run_app_encrypted_count={trace.AppEncryptedRunCount}");
        logger.WriteLine($"{prefix}.wait_count={trace.WaitCount}");
        logger.WriteLine($"{prefix}.enhanced_wait_count={trace.EnhancedWaitCount}");
        logger.WriteLine($"{prefix}.manual_wait_count={trace.ManualWaitCount}");
        logger.WriteLine($"{prefix}.call_home_count={trace.CallHomeCount}");
        logger.WriteLine($"{prefix}.app_trigger_count={trace.AppTriggerCount}");

        for (var i = 0; i < trace.CallHomeEndpoints.Count; i++) {
            logger.WriteLine($"{prefix}.call_home_endpoint.{i}={trace.CallHomeEndpoints[i]}");
        }

        for (var i = 0; i < trace.AppTriggers.Count; i++) {
            var trigger = trace.AppTriggers[i];
            logger.WriteLine($"{prefix}.app_trigger.{i}.trigger={trigger.Trigger}");
            logger.WriteLine($"{prefix}.app_trigger.{i}.data_json={trigger.DataJson}");
        }
    }

    private static void LogErrors(string prefix, Dictionary<string, object?>? responseBody, SessionLogger logger) {
        if (responseBody is null) {
            return;
        }

        var errors = ExtractErrors(responseBody);
        for (var i = 0; i < errors.Count; i++) {
            logger.WriteLine($"{prefix}.error.{i}={errors[i]}");
        }
    }

    private static IReadOnlyList<string> ExtractErrors(Dictionary<string, object?> responseBody) {
        var errors = new List<string>();
        CollectErrors(responseBody, errors);
        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static void CollectErrors(object? node, List<string> errors) {
        switch (node) {
        case null:
            return;
        case string text:
            if (!string.IsNullOrWhiteSpace(text)) {
                errors.Add(text);
            }
            return;
        case byte[] bytes:
            if (bytes.Length > 0) {
                errors.Add(System.Text.Encoding.UTF8.GetString(bytes));
            }
            return;
        case Dictionary<string, object?> map:
            foreach (var entry in map) {
                if (string.Equals(entry.Key, "error", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.Key, "errors", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(entry.Key, "message", StringComparison.OrdinalIgnoreCase)) {
                    CollectErrors(entry.Value, errors);
                }
            }
            return;
        case List<object?> list:
            foreach (var item in list) {
                CollectErrors(item, errors);
            }
            return;
        case JsonElement jsonElement:
            CollectErrors(ConvertJsonElement(jsonElement), errors);
            return;
        default:
            return;
        }
    }

    private static object? ConvertJsonElement(JsonElement element) {
        return element.ValueKind switch {
            JsonValueKind.Object => JsonSerializer.Deserialize<Dictionary<string, object?>>(element.GetRawText()),
            JsonValueKind.Array => JsonSerializer.Deserialize<List<object?>>(element.GetRawText()),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var i64) ? i64 : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static JsonSerializerOptions JsonOptions() {
        return new JsonSerializerOptions {
            WriteIndented = true
        };
    }
}
