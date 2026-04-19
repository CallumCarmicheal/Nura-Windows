using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Logging;
using System.Text.Json;
using NuraDesktopConsole.Config;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionAuthUpgradeInfo : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var authPath = LocalStateFiles.LoadAuthPath(logger);
        var state = NuraAuthState.LoadOrCreate(authPath);
        var source = (ArgumentReader.OptionalValue(args, "--source") ?? "auto").Trim().ToLowerInvariant();
        var upgradeEndpoint = (ArgumentReader.OptionalValue(args, "--upgrade-endpoint") ?? "upgrade").Trim();
        var language = (ArgumentReader.OptionalValue(args, "--language") ?? "en").Trim().ToLowerInvariant();
        var firmwareVersionOverride = AuthStateSupport.ParseOptionalInt32(args, "--firmware-version");
        var appStartTime = AuthStateSupport.ParseOptionalInt64(args, "--app-start-time")
            ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        logger.WriteLine($"auth.upgrade_info.source={source}");
        logger.WriteLine($"auth.upgrade_info.upgrade_endpoint={upgradeEndpoint}");
        logger.WriteLine($"auth.upgrade_info.language={language}");
        logger.WriteLine($"auth.upgrade_info.firmware_version_override={(firmwareVersionOverride is null ? string.Empty : firmwareVersionOverride.Value.ToString())}");
        logger.WriteLine($"auth.upgrade_info.app_start_time_unix_ms={appStartTime}");
        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.uuid={state.Uuid}");
        logger.WriteLine($"auth.auth_uid={state.AuthUid ?? string.Empty}");
        AuthStateSupport.LogSessionState(state, logger);

        UpgradeInfoSnapshot snapshot;
        string resolvedSource;
        Dictionary<string, object?>? responseBody;

        switch (source) {
        case "cached":
            snapshot = ParseSnapshot(state.LastResponseBody);
            resolvedSource = "cached";
            responseBody = state.LastResponseBody;
            break;
        case "app-session":
            (state, snapshot, responseBody) = await RefreshFromAppSessionAsync(state, authPath, appStartTime, logger, cts.Token);
            resolvedSource = "app-session";
            break;
        case "validate-token":
            if (!state.HasAuthenticatedSession) {
                throw new InvalidOperationException("validate-token source requires an authenticated session");
            }

            (state, snapshot, responseBody) = await RefreshFromValidateTokenAsync(state, authPath, appStartTime, logger, cts.Token);
            resolvedSource = "validate-token";
            break;
        case "upgrade-entry":
            if (!state.HasAuthenticatedSession) {
                throw new InvalidOperationException("upgrade-entry source requires an authenticated session");
            }

            (state, snapshot, responseBody) = await RefreshFromUpgradeEntryAsync(state, authPath, upgradeEndpoint, language, firmwareVersionOverride, logger, cts.Token);
            resolvedSource = "upgrade-entry";
            break;
        case "auto":
            snapshot = ParseSnapshot(state.LastResponseBody);
            resolvedSource = "cached";
            responseBody = state.LastResponseBody;
            if (!snapshot.HasAnyData) {
                try {
                    (state, snapshot, responseBody) = await RefreshFromAppSessionAsync(state, authPath, appStartTime, logger, cts.Token);
                    resolvedSource = "app-session";
                } catch (Exception ex) {
                    logger.WriteLine("auth.upgrade_info.refresh_error.source=app-session");
                    logger.WriteLine($"auth.upgrade_info.refresh_error.message={ex.Message}");
                }
            }

            if (!snapshot.HasAnyData && state.HasAuthenticatedSession) {
                try {
                    (state, snapshot, responseBody) = await RefreshFromValidateTokenAsync(state, authPath, appStartTime, logger, cts.Token);
                    resolvedSource = "validate-token";
                } catch (Exception ex) {
                    logger.WriteLine("auth.upgrade_info.refresh_error.source=validate-token");
                    logger.WriteLine($"auth.upgrade_info.refresh_error.message={ex.Message}");
                }
            }

            if (!snapshot.HasAnyData && state.HasAuthenticatedSession) {
                try {
                    (state, snapshot, responseBody) = await RefreshFromUpgradeEntryAsync(state, authPath, upgradeEndpoint, language, firmwareVersionOverride, logger, cts.Token);
                    resolvedSource = "upgrade-entry";
                } catch (Exception ex) {
                    logger.WriteLine("auth.upgrade_info.refresh_error.source=upgrade-entry");
                    logger.WriteLine($"auth.upgrade_info.refresh_error.message={ex.Message}");
                }
            }
            break;
        default:
            throw new InvalidOperationException("--source must be one of: auto, cached, app-session, validate-token, upgrade-entry");
        }

        logger.WriteLine($"auth.upgrade_info.resolved_source={resolvedSource}");
        logger.WriteLine($"auth.upgrade_info.found={snapshot.HasAnyData}");
        AutomatedActionTraceLogging.LogTrace("auth.upgrade_info.trace", responseBody, logger);
        LogErrors("auth.upgrade_info", responseBody, logger);

        if (!snapshot.HasAnyData) {
            var errors = ExtractErrors(responseBody);
            logger.WriteLine(errors.Any(error => string.Equals(error, "No upgrade available", StringComparison.OrdinalIgnoreCase))
                ? "auth.upgrade_info.result=no_upgrade_available"
                : "auth.upgrade_info.result=no_upgrade_metadata_found");
            return 1;
        }

        LogUpgrade("auth.upgrade.classic", snapshot.Classic, logger);
        LogTwsUpgrade("auth.upgrade.tws", snapshot.Tws, logger);
        LogTwsUpgrade("auth.upgrade.current_tws", snapshot.CurrentTws, logger);
        return 0;
    }

    private static UpgradeInfoSnapshot ParseSnapshot(Dictionary<string, object?>? responseBody) {
        return responseBody is null
            ? UpgradeInfoSnapshot.Empty
            : UpgradeInfoParser.Parse(responseBody);
    }

    private static async Task<(NuraAuthState State, UpgradeInfoSnapshot Snapshot, Dictionary<string, object?>? ResponseBody)> RefreshFromAppSessionAsync(
        NuraAuthState state,
        string authPath,
        long appStartTime,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        logger.WriteLine("auth.upgrade_info.refresh=app-session");
        using var client = new NuraAuthApiClient(logger);
        var result = await client.AppSessionAsync(state, "app/session", appStartTime, cancellationToken);
        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
        updatedState.Save(authPath);
        logger.WriteLine($"auth.upgrade_info.app_session.result_status={result.StatusCode}");
        logger.WriteLine($"auth.upgrade_info.app_session.success={result.IsSuccessStatusCode}");
        return (updatedState, ParseSnapshot(result.DecodedBody), result.DecodedBody);
    }

    private static async Task<(NuraAuthState State, UpgradeInfoSnapshot Snapshot, Dictionary<string, object?>? ResponseBody)> RefreshFromValidateTokenAsync(
        NuraAuthState state,
        string authPath,
        long appStartTime,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        logger.WriteLine("auth.upgrade_info.refresh=validate-token");
        using var client = new NuraAuthApiClient(logger);
        var result = await client.ValidateTokenAsync(state, withAppContext: true, appStartTime, cancellationToken);
        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
        updatedState.Save(authPath);
        logger.WriteLine($"auth.upgrade_info.validate_token.result_status={result.StatusCode}");
        logger.WriteLine($"auth.upgrade_info.validate_token.success={result.IsSuccessStatusCode}");
        return (updatedState, ParseSnapshot(result.DecodedBody), result.DecodedBody);
    }

    private static async Task<(NuraAuthState State, UpgradeInfoSnapshot Snapshot, Dictionary<string, object?>? ResponseBody)> RefreshFromUpgradeEntryAsync(
        NuraAuthState state,
        string authPath,
        string upgradeEndpoint,
        string language,
        int? firmwareVersionOverride,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        if (firmwareVersionOverride is not null) {
            state = await RefreshSessionStartForFirmwareOverrideAsync(state, authPath, firmwareVersionOverride.Value, logger, cancellationToken);
        }

        if (state.BluetoothSessionId is not { } bluetoothSessionId) {
            throw new InvalidOperationException("upgrade-entry source requires a saved bluetooth session id");
        }

        logger.WriteLine("auth.upgrade_info.refresh=upgrade-entry");
        logger.WriteLine($"auth.upgrade_info.upgrade_entry.session={bluetoothSessionId}");
        using var client = new NuraAuthApiClient(logger);
        var result = await client.AutomatedEntryAsync(
            state,
            upgradeEndpoint,
            bluetoothSessionId,
            packets: [],
            additionalPayload: new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["language"] = language
            },
            cancellationToken);
        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
        updatedState.Save(authPath);
        logger.WriteLine($"auth.upgrade_info.upgrade_entry.result_status={result.StatusCode}");
        logger.WriteLine($"auth.upgrade_info.upgrade_entry.success={result.IsSuccessStatusCode}");
        return (updatedState, ParseSnapshot(result.DecodedBody), result.DecodedBody);
    }

    private static async Task<NuraAuthState> RefreshSessionStartForFirmwareOverrideAsync(
        NuraAuthState state,
        string authPath,
        int firmwareVersion,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        var config = LocalStateFiles.LoadConfig(logger);
        var maxPacketLength = HeadsetSupport.GetMaxPacketLengthHint(config.SerialNumber);
        var userSessionId = state.UserSessionId
            ?? throw new InvalidOperationException("firmware override requires a saved user session id");

        logger.WriteLine("auth.upgrade_info.session_start_override=true");
        logger.WriteLine($"auth.upgrade_info.session_start.serial={config.SerialNumber}");
        logger.WriteLine($"auth.upgrade_info.session_start.firmware_version={firmwareVersion}");
        logger.WriteLine($"auth.upgrade_info.session_start.max_packet_length={maxPacketLength}");
        logger.WriteLine("auth.upgrade_info.session_start.max_bulk_packet_length=0");
        logger.WriteLine($"auth.upgrade_info.session_start.usid={userSessionId}");

        using var client = new NuraAuthApiClient(logger);
        var result = await client.SessionStartAsync(
            state,
            config.SerialNumber,
            firmwareVersion,
            maxPacketLength,
            maxBulkPacketLength: 0,
            userSessionId,
            cancellationToken);

        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
        updatedState.Save(authPath);

        logger.WriteLine($"auth.upgrade_info.session_start.result_status={result.StatusCode}");
        logger.WriteLine($"auth.upgrade_info.session_start.success={result.IsSuccessStatusCode}");
        if (!result.IsSuccessStatusCode) {
            throw new InvalidOperationException($"session/start failed with status {result.StatusCode}");
        }

        if (result.DecodedBody is not null) {
            logger.WriteLine($"auth.upgrade_info.session_start.response.summary={AuthStateSupport.SummarizeSessionStartResponse(result.DecodedBody)}");
        }

        AuthStateSupport.LogSessionState(updatedState, logger);
        return updatedState;
    }

    private static void LogUpgrade(string prefix, UpgradeMetadata? metadata, SessionLogger logger) {
        if (metadata is null) {
            logger.WriteLine($"{prefix}.present=false");
            return;
        }

        logger.WriteLine($"{prefix}.present=true");
        if (!string.IsNullOrWhiteSpace(metadata.Name)) {
            logger.WriteLine($"{prefix}.name={metadata.Name}");
        }

        if (!string.IsNullOrWhiteSpace(metadata.Description)) {
            logger.WriteLine($"{prefix}.description={metadata.Description}");
        }

        if (metadata.TargetFirmwareVersion is { } targetFirmwareVersion) {
            logger.WriteLine($"{prefix}.target_fw={targetFirmwareVersion}");
        }

        if (metadata.TargetPersistentStoreVersion is { } targetPersistentStoreVersion) {
            logger.WriteLine($"{prefix}.target_ps={targetPersistentStoreVersion}");
        }

        if (metadata.TargetFilesystemVersion is { } targetFilesystemVersion) {
            logger.WriteLine($"{prefix}.target_fs={targetFilesystemVersion}");
        }

        if (metadata.Blocking is { } blocking) {
            logger.WriteLine($"{prefix}.blocking={blocking}");
        }

        if (metadata.CreatedAtUnixSeconds is { } createdAtUnixSeconds) {
            logger.WriteLine($"{prefix}.created_at_unix={createdAtUnixSeconds}");
            logger.WriteLine($"{prefix}.created_at_utc={DateTimeOffset.FromUnixTimeSeconds(createdAtUnixSeconds):O}");
        }

        if (!string.IsNullOrWhiteSpace(metadata.InfoUrl)) {
            logger.WriteLine($"{prefix}.info_url={metadata.InfoUrl}");
        }

        if (!string.IsNullOrWhiteSpace(metadata.DesignJson)) {
            logger.WriteLine($"{prefix}.design.json={metadata.DesignJson}");
        }

        logger.WriteLine($"{prefix}.language_count={metadata.Languages.Count}");
        for (var index = 0; index < metadata.Languages.Count; index++) {
            logger.WriteLine($"{prefix}.language.{index}={metadata.Languages[index]}");
        }

        logger.WriteLine($"{prefix}.raw_keys={string.Join(",", metadata.Raw.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))}");
    }

    private static void LogTwsUpgrade(string prefix, TwsUpgradeMetadata? metadata, SessionLogger logger) {
        LogUpgrade(prefix, metadata, logger);
        if (metadata is null) {
            return;
        }

        logger.WriteLine($"{prefix}.file_count={metadata.Files.Count}");
        foreach (var fileEntry in metadata.Files.OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)) {
            logger.WriteLine($"{prefix}.file.{fileEntry.Key}.url={fileEntry.Value.Url}");
            logger.WriteLine($"{prefix}.file.{fileEntry.Key}.md5={fileEntry.Value.Md5}");
        }
    }

    private static void LogErrors(string prefix, Dictionary<string, object?>? responseBody, SessionLogger logger) {
        var errors = ExtractErrors(responseBody);
        logger.WriteLine($"{prefix}.error_count={errors.Count}");
        for (var index = 0; index < errors.Count; index++) {
            logger.WriteLine($"{prefix}.error.{index}={errors[index]}");
        }
    }

    private static IReadOnlyList<string> ExtractErrors(Dictionary<string, object?>? responseBody) {
        if (responseBody is null || !TryGetValue(responseBody, "e", out var errorNode)) {
            return [];
        }

        return EnumerateArray(errorNode)
            .Select(static error => error switch {
                string value => value,
                JsonElement { ValueKind: JsonValueKind.String } jsonString => jsonString.GetString(),
                _ => null
            })
            .Where(static error => !string.IsNullOrWhiteSpace(error))
            .Cast<string>()
            .ToArray();
    }

    private static bool TryGetValue(Dictionary<string, object?> map, string key, out object? value) {
        foreach (var entry in map) {
            if (string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)) {
                value = entry.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static IEnumerable<object?> EnumerateArray(object? node) {
        return node switch {
            List<object?> list => list,
            JsonElement { ValueKind: JsonValueKind.Array } jsonArray => JsonSerializer.Deserialize<List<object?>>(jsonArray.GetRawText()) ?? [],
            _ => []
        };
    }
}
