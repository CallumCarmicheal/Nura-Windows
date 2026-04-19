using NuraDesktopConsole.Config;
using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Library.Protocol;
using NuraDesktopConsole.Library.Transport;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionAuthLanguageDump : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        var endpoint = (ArgumentReader.OptionalValue(args, "--endpoint") ?? "change_language").Trim();
        var language = (ArgumentReader.OptionalValue(args, "--language") ?? "de").Trim().ToLowerInvariant();
        var bluetoothSessionOverride = AuthStateSupport.ParseOptionalInt32(args, "--session");
        var firmwareVersionOverride = AuthStateSupport.ParseOptionalInt32(args, "--firmware-version");
        var payloadPath = ArgumentReader.OptionalValue(args, "--payload-json");
        var packetsPath = ArgumentReader.OptionalValue(args, "--packets-json");
        var prepareLogPath = ArgumentReader.OptionalValue(args, "--prepare-log-path");
        var outputRootOverride = ArgumentReader.OptionalValue(args, "--output-dir");
        var manualMode = ArgumentReader.HasFlag(args, "--manual") ||
            bluetoothSessionOverride is not null ||
            firmwareVersionOverride is not null ||
            !string.IsNullOrWhiteSpace(packetsPath) ||
            !string.Equals(endpoint, "change_language", StringComparison.OrdinalIgnoreCase);

        if (!manualMode) {
            return await HandleFullSafeFlowAsync(args, language, payloadPath, prepareLogPath, outputRootOverride, logger);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        IReadOnlyDictionary<string, object?>? additionalPayload = null;
        if (!string.IsNullOrWhiteSpace(payloadPath)) {
            additionalPayload = AutomatedReplayInputParser.ParsePayloadFile(payloadPath);
        } else if (string.Equals(endpoint, "change_language", StringComparison.OrdinalIgnoreCase)) {
            additionalPayload = new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["language"] = language
            };
        }

        var packets = string.IsNullOrWhiteSpace(packetsPath)
            ? Array.Empty<IReadOnlyDictionary<string, object?>>()
            : AutomatedReplayInputParser.ParsePacketsFile(packetsPath);

        var dumpResult = await AutomatedEntryDumpSupport.ExecuteAsync(
            logPrefix: "auth.language_dump",
            outputPrefix: "language_dump",
            endpoint: endpoint,
            additionalPayload: additionalPayload,
            packets: packets,
            bluetoothSessionOverride: bluetoothSessionOverride,
            firmwareVersionOverride: firmwareVersionOverride,
            outputRootOverride: outputRootOverride,
            logger: logger,
            cancellationToken: cts.Token);

        return dumpResult.ExitCode;
    }

    private static async Task<int> HandleFullSafeFlowAsync(
        string[] args,
        string language,
        string? payloadPath,
        string? prepareLogPath,
        string? outputRootOverride,
        SessionLogger logger) {
        var overallTimeoutMs = ParseIntArgument(args, "--overall-timeout-ms", 180000);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(overallTimeoutMs));
        var authPath = LocalStateFiles.LoadAuthPath(logger);
        var state = NuraAuthState.LoadOrCreate(authPath);

        logger.WriteLine("auth.language_dump.mode=full_safe_flow");
        logger.WriteLine("auth.language_dump.safety=will_stop_before_bulk_transfer_packets_are_sent_to_headset");
        logger.WriteLine($"auth.language_dump.language={language}");
        logger.WriteLine($"auth.language_dump.overall_timeout_ms={overallTimeoutMs}");
        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.uuid={state.Uuid}");
        logger.WriteLine($"auth.auth_uid={state.AuthUid ?? string.Empty}");
        AuthStateSupport.LogSessionState(state, logger);

        using var client = new NuraAuthApiClient(logger);
        state = await RunAppSessionAsync(state, authPath, client, logger, cts.Token);
        state = await EnsureAuthenticatedAsync(state, authPath, client, logger, cts.Token);

        var hardwareInfo = await ProbeHardwareInfoAsync(logger, cts.Token);
        logger.WriteLine($"auth.language_dump.hw_info.serial={hardwareInfo.SerialNumber}");
        logger.WriteLine($"auth.language_dump.hw_info.firmware_version={hardwareInfo.FirmwareVersion}");
        logger.WriteLine($"auth.language_dump.hw_info.max_packet_length={hardwareInfo.MaxPacketLength}");
        logger.WriteLine($"auth.language_dump.hw_info.device_address={hardwareInfo.DeviceAddress}");

        state = await RunSessionStartAsync(state, authPath, client, hardwareInfo, logger, cts.Token);
        state = await CompleteSessionStartContinuationsAsync(state, authPath, client, hardwareInfo.DeviceAddress, logger, cts.Token);

        var bluetoothSessionId = state.BluetoothSessionId
            ?? throw new InvalidOperationException("full language-dump flow did not produce a bluetooth session id");
        if (string.IsNullOrWhiteSpace(state.AppEncKey) || string.IsNullOrWhiteSpace(state.AppEncNonce)) {
            throw new InvalidOperationException("full language-dump flow did not reach final session/start_4 app_enc state");
        }

        state = await SubmitAnalyticsAsync(state, authPath, client, bluetoothSessionId, logger, cts.Token);

        var preparePayload = !string.IsNullOrWhiteSpace(payloadPath)
            ? AutomatedReplayInputParser.ParsePayloadFile(payloadPath)
            : new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["language"] = language
            };

        AuthCallResult prepareResult;
        logger.BeginSection("auth language-dump change_language prepare request");
        try {
            logger.WriteLine("auth.language_dump.step=change_language_prepare_request");
            prepareResult = await client.AutomatedEntryAsync(
                state,
                "change_language",
                bluetoothSessionId,
                packets: [],
                additionalPayload: preparePayload,
                cancellationToken: cts.Token);
            state = AuthStateSupport.ApplyAuthResultToState(state, prepareResult, state.EmailAddress);
            state.Save(authPath);
            AutomatedEntryDumpSupport.LogResult("auth.language_dump.prepare", prepareResult, logger);
            await AutomatedEntryDumpSupport.DumpResultAsync(
                "auth.language_dump.prepare",
                "language_dump_prepare",
                "change_language",
                prepareResult,
                outputRootOverride,
                logger,
                cts.Token);
        } finally {
            logger.EndSection("auth language-dump change_language prepare request");
        }

        if (!prepareResult.IsSuccessStatusCode) {
            logger.WriteLine("auth.language_dump.result=prepare_failed");
            return 1;
        }

        var prepareDetails = SessionStartResponseParser.Parse(prepareResult.DecodedBody ?? [])
            ?? throw new InvalidOperationException("change_language response did not contain executable prepare actions");
        if (!string.Equals(prepareDetails.FinalEvent, "change_language_1", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"change_language response did not point to change_language_1; final={prepareDetails.FinalEvent ?? string.Empty}");
        }

        IReadOnlyList<IReadOnlyDictionary<string, object?>> prepareContinuationPackets;
        if (!string.IsNullOrWhiteSpace(prepareLogPath)) {
            logger.BeginSection("auth language-dump load captured prepare packets");
            try {
                logger.WriteLine("auth.language_dump.step=load_captured_prepare_packets");
                logger.WriteLine($"auth.language_dump.prepare_log_path={Path.GetFullPath(prepareLogPath)}");
                logger.WriteLine("auth.language_dump.prepare_packet_source=captured_log");
                logger.WriteLine("auth.language_dump.prepare_packets_executed=false");
                prepareContinuationPackets = LanguageDumpStaticCapture.LoadPackets("change_language_1", prepareLogPath, logger);
            } finally {
                logger.EndSection("auth language-dump load captured prepare packets");
            }
        } else {
            logger.BeginSection("auth language-dump execute prepare packets");
            try {
                logger.WriteLine("auth.language_dump.step=execute_prepare_packets");
                logger.WriteLine("auth.language_dump.prepare_packet_safety=only_prepare_packets_will_be_sent_no_bulk_transfer_packets");
                await using IHeadsetTransport transport = new RfcommHeadsetTransport();
                logger.WriteLine($"transport={transport.Describe()}");
                logger.WriteLine("auth.language_dump.prepare.connecting=true");
                await transport.ConnectAsync(hardwareInfo.DeviceAddress, cts.Token);
                logger.WriteLine("auth.language_dump.prepare.connected=true");
                prepareContinuationPackets = await SessionStartExecutionSupport.ExecuteLocalActionsAsync(
                    prepareDetails,
                    transport,
                    logger,
                    unencryptedGaiaVersion: 0x01,
                    unencryptedGaiaFlags: 0x00,
                    runGaiaVersion: 0x01,
                    runGaiaFlags: 0x00,
                    preRunCaptureIdleTimeoutMs: 0,
                    preRunCaptureMaxFrames: 0,
                    cts.Token);
            } finally {
                logger.EndSection("auth language-dump execute prepare packets");
            }
        }

        logger.WriteLine($"auth.language_dump.prepare.response_packets={prepareContinuationPackets.Count}");
        AuthCallResult transferResult;
        AutomatedEntryDumpSupport.AutomatedEntryDumpResult dump;
        logger.BeginSection("auth language-dump change_language_1 dump request");
        try {
            logger.WriteLine("auth.language_dump.step=change_language_1_dump_request");
            transferResult = await client.AutomatedEntryAsync(
                state,
                "change_language_1",
                bluetoothSessionId,
                prepareContinuationPackets,
                cts.Token);
            state = AuthStateSupport.ApplyAuthResultToState(state, transferResult, state.EmailAddress);
            state.Save(authPath);

            AutomatedEntryDumpSupport.LogResult("auth.language_dump", transferResult, logger);
            dump = await AutomatedEntryDumpSupport.DumpResultAsync(
                "auth.language_dump",
                "language_dump",
                "change_language_1",
                transferResult,
                outputRootOverride,
                logger,
                cts.Token);
        } finally {
            logger.EndSection("auth language-dump change_language_1 dump request");
        }

        logger.WriteLine("auth.language_dump.stop=before_bulk_transfer_execution");
        logger.WriteLine("auth.language_dump.bulk_packets_executed=false");
        logger.WriteLine("auth.language_dump.next_step=inspect_dumped_change_language_1_response_or_use_static_continuation");
        return transferResult.IsSuccessStatusCode && dump.PacketCount > 0 ? 0 : 1;
    }

    private static async Task<NuraAuthState> RunAppSessionAsync(
        NuraAuthState state,
        string authPath,
        NuraAuthApiClient client,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        logger.BeginSection("auth language-dump app-session");
        try {
            logger.WriteLine("auth.language_dump.step=app-session");
            var result = await client.AppSessionAsync(state, "app/session", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), cancellationToken);
            var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
            updatedState.Save(authPath);
            logger.WriteLine($"auth.language_dump.app_session.status={result.StatusCode}");
            logger.WriteLine($"auth.language_dump.app_session.success={result.IsSuccessStatusCode}");
            if (!result.IsSuccessStatusCode) {
                throw new InvalidOperationException("app/session failed");
            }

            AuthStateSupport.LogSessionState(updatedState, logger);
            return updatedState;
        } finally {
            logger.EndSection("auth language-dump app-session");
        }
    }

    private static async Task<NuraAuthState> EnsureAuthenticatedAsync(
        NuraAuthState state,
        string authPath,
        NuraAuthApiClient client,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        logger.BeginSection("auth language-dump auth-check");
        try {
            logger.WriteLine("auth.language_dump.step=auth-check");
            if (state.HasAuthenticatedSession) {
                var validation = await ValidateTokenAsync(state, authPath, client, logger, cancellationToken);
                if (validation.Success && validation.State.HasAuthenticatedSession && validation.State.UserSessionId is not null) {
                    logger.WriteLine("auth.language_dump.auth.mode=resume");
                    return validation.State;
                }

                state = validation.State;
                logger.WriteLine("auth.language_dump.auth.resume_failed=true");
            }

            logger.WriteLine("auth.language_dump.auth.mode=interactive_login");
            return await RunInteractiveLoginLoopAsync(state, authPath, client, logger, cancellationToken);
        } finally {
            logger.EndSection("auth language-dump auth-check");
        }
    }

    private static async Task<AuthValidationResult> ValidateTokenAsync(
        NuraAuthState state,
        string authPath,
        NuraAuthApiClient client,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        if (!state.HasAuthenticatedSession) {
            return new AuthValidationResult(state, false);
        }

        logger.BeginSection("auth language-dump validate-token");
        try {
            logger.WriteLine("auth.language_dump.step=validate-token");
            var result = await client.ValidateTokenAsync(state, withAppContext: false, appStartTimeUnixMilliseconds: null, cancellationToken);
            var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
            updatedState.Save(authPath);
            logger.WriteLine($"auth.language_dump.validate_token.status={result.StatusCode}");
            logger.WriteLine($"auth.language_dump.validate_token.success={result.IsSuccessStatusCode}");
            AuthStateSupport.LogSessionState(updatedState, logger);
            return new AuthValidationResult(updatedState, result.IsSuccessStatusCode);
        } finally {
            logger.EndSection("auth language-dump validate-token");
        }
    }

    private static async Task<NuraAuthState> RunInteractiveLoginLoopAsync(
        NuraAuthState state,
        string authPath,
        NuraAuthApiClient client,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        var emailAddress = state.EmailAddress ?? Prompt("Email address");
        state = await SendEmailAsync(state, authPath, client, emailAddress, logger, cancellationToken);

        while (true) {
            Console.Write("Enter 6-digit code, .email to resend/change email, or .quit to abort: ");
            var input = Console.ReadLine()?.Trim() ?? string.Empty;
            logger.WriteLine($"auth.language_dump.auth.input={input}");

            if (string.Equals(input, ".quit", StringComparison.OrdinalIgnoreCase)) {
                throw new OperationCanceledException("interactive login aborted by user");
            }

            if (string.Equals(input, ".email", StringComparison.OrdinalIgnoreCase)) {
                emailAddress = Prompt("Email address");
                state = await SendEmailAsync(state, authPath, client, emailAddress, logger, cancellationToken);
                continue;
            }

            if (!LooksLikeSixDigitCode(input)) {
                logger.WriteLine("auth.language_dump.auth.input_result=invalid_code_format");
                Console.WriteLine("Code must be exactly 6 digits, or use .email / .quit.");
                continue;
            }

            try {
                state = await VerifyCodeAsync(state, authPath, client, emailAddress, input, logger, cancellationToken);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                logger.WriteLine($"auth.language_dump.auth.verify_code_error={ex.Message}");
                Console.WriteLine("Code was not accepted. Type a new code, .email to resend/change, or .quit.");
                continue;
            }

            var validation = await ValidateTokenAsync(state, authPath, client, logger, cancellationToken);
            state = validation.State;
            if (validation.Success && state.HasAuthenticatedSession && state.UserSessionId is not null) {
                return state;
            }

            logger.WriteLine("auth.language_dump.auth.validate_result=missing_user_session_id");
            Console.WriteLine("Login succeeded but no usable user session was returned. Type .email to request a new code or .quit.");
        }
    }

    private static async Task<NuraAuthState> SendEmailAsync(
        NuraAuthState state,
        string authPath,
        NuraAuthApiClient client,
        string emailAddress,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        logger.WriteLine("auth.language_dump.step=send-email");
        var result = await client.SendLoginEmailAsync(state, emailAddress, cancellationToken);
        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, emailAddress);
        updatedState.Save(authPath);
        logger.WriteLine($"auth.language_dump.send_email.status={result.StatusCode}");
        logger.WriteLine($"auth.language_dump.send_email.success={result.IsSuccessStatusCode}");
        if (!result.IsSuccessStatusCode) {
            throw new InvalidOperationException("send-email failed");
        }

        return updatedState;
    }

    private static async Task<NuraAuthState> VerifyCodeAsync(
        NuraAuthState state,
        string authPath,
        NuraAuthApiClient client,
        string emailAddress,
        string code,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        logger.WriteLine("auth.language_dump.step=verify-code");
        var result = await client.VerifyCodeAsync(state, emailAddress, code, cancellationToken);
        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, emailAddress, result.AuthUid ?? emailAddress);
        updatedState.Save(authPath);
        logger.WriteLine($"auth.language_dump.verify_code.status={result.StatusCode}");
        logger.WriteLine($"auth.language_dump.verify_code.success={result.IsSuccessStatusCode}");
        if (!result.IsSuccessStatusCode) {
            throw new InvalidOperationException("verify-code failed");
        }

        AuthStateSupport.LogSessionState(updatedState, logger);
        return updatedState;
    }

    private static async Task<HardwareProbeResult> ProbeHardwareInfoAsync(
        SessionLogger logger,
        CancellationToken cancellationToken) {
        logger.BeginSection("auth language-dump probe hardware info");
        try {
            logger.WriteLine("auth.language_dump.step=probe-hw-info");
            var selectedDevice = ProbeDeviceSelector.Select(logger);
            ProbeDeviceSelector.LogSelected(logger, selectedDevice);

            await using IHeadsetTransport transport = new RfcommHeadsetTransport();
            logger.WriteLine($"transport={transport.Describe()}");
            logger.WriteLine("auth.language_dump.hw_info.connecting=true");
            await transport.ConnectAsync(selectedDevice.Address, cancellationToken);
            logger.WriteLine("auth.language_dump.hw_info.connected=true");

            var deviceInfoResponse = await transport.ExchangeAsync(
                GaiaPackets.BuildCommand(GaiaCommandId.GetDeviceInfo),
                GaiaCommandId.GetDeviceInfo,
                logger,
                cancellationToken);
            var deviceInfoPayload = deviceInfoResponse.PayloadExcludingStatus;
            logger.WriteLine($"auth.language_dump.hw_info.device_info.payload.hex={Hex.Format(deviceInfoPayload)}");
            if (!HeadsetSupport.TryDecodeDeviceInfo(deviceInfoPayload, out var deviceInfo)) {
                throw new InvalidOperationException("failed to decode headset device info");
            }

            return new HardwareProbeResult(
                selectedDevice.Address,
                deviceInfo.SerialNumber,
                deviceInfo.FirmwareVersion,
                HeadsetSupport.GetMaxPacketLengthHint(deviceInfo.SerialNumber));
        } finally {
            logger.EndSection("auth language-dump probe hardware info");
        }
    }

    private static async Task<NuraAuthState> RunSessionStartAsync(
        NuraAuthState state,
        string authPath,
        NuraAuthApiClient client,
        HardwareProbeResult hardwareInfo,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        logger.BeginSection("auth language-dump session-start");
        try {
            logger.WriteLine("auth.language_dump.step=session-start");
            var userSessionId = state.UserSessionId
                ?? throw new InvalidOperationException("validate-token did not produce a usable userSessionId");
            var result = await client.SessionStartAsync(
                state,
                hardwareInfo.SerialNumber,
                hardwareInfo.FirmwareVersion,
                hardwareInfo.MaxPacketLength,
                maxBulkPacketLength: 0,
                userSessionId,
                cancellationToken);
            var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
            updatedState.Save(authPath);
            logger.WriteLine($"auth.language_dump.session_start.status={result.StatusCode}");
            logger.WriteLine($"auth.language_dump.session_start.success={result.IsSuccessStatusCode}");
            if (!result.IsSuccessStatusCode) {
                throw new InvalidOperationException("session-start failed");
            }

            if (result.DecodedBody is not null) {
                logger.WriteLine($"auth.language_dump.session_start.summary={AuthStateSupport.SummarizeSessionStartResponse(result.DecodedBody)}");
            }

            AuthStateSupport.LogSessionState(updatedState, logger);
            return updatedState;
        } finally {
            logger.EndSection("auth language-dump session-start");
        }
    }

    private static async Task<NuraAuthState> CompleteSessionStartContinuationsAsync(
        NuraAuthState state,
        string authPath,
        NuraAuthApiClient client,
        string deviceAddress,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        for (var step = 0; step < 8; step++) {
            var details = SessionStartResponseParser.Parse(state.LastResponseBody ?? []);
            if (details is null || string.IsNullOrWhiteSpace(details.FinalEvent)) {
                logger.WriteLine($"auth.language_dump.bootstrap.complete_after_steps={step}");
                return state;
            }

            if (!details.FinalEvent.StartsWith("session/start_", StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"unexpected bootstrap continuation endpoint: {details.FinalEvent}");
            }

            state = await RunSessionStartContinuationAsync(
                state,
                authPath,
                client,
                deviceAddress,
                details,
                $"auth.language_dump.{details.FinalEvent.Replace('/', '_')}",
                logger,
                cancellationToken);
        }

        throw new InvalidOperationException("session/start continuation loop exceeded safety limit");
    }

    private static async Task<NuraAuthState> RunSessionStartContinuationAsync(
        NuraAuthState state,
        string authPath,
        NuraAuthApiClient client,
        string deviceAddress,
        SessionStartResponseDetails details,
        string stepPrefix,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        var endpoint = details.FinalEvent
            ?? throw new InvalidOperationException("continuation endpoint missing from session/start response");
        var sessionId = state.BluetoothSessionId
            ?? details.SessionId
            ?? throw new InvalidOperationException("bluetooth session id missing before continuation");

        logger.BeginSection($"auth language-dump {endpoint}");
        try {
            logger.WriteLine($"{stepPrefix}.endpoint={endpoint}");
            logger.WriteLine($"{stepPrefix}.session={sessionId}");
            SessionStartExecutionSupport.LogParsedDetails($"{stepPrefix}.source", details, logger);

            IReadOnlyList<IReadOnlyDictionary<string, object?>> packets;
            await using (IHeadsetTransport transport = new RfcommHeadsetTransport()) {
                logger.WriteLine($"transport={transport.Describe()}");
                logger.WriteLine($"{stepPrefix}.connecting=true");
                await transport.ConnectAsync(deviceAddress, cancellationToken);
                logger.WriteLine($"{stepPrefix}.connected=true");
                packets = await SessionStartExecutionSupport.ExecuteLocalActionsAsync(
                    details,
                    transport,
                    logger,
                    unencryptedGaiaVersion: 0x01,
                    unencryptedGaiaFlags: 0x00,
                    runGaiaVersion: 0x01,
                    runGaiaFlags: 0x00,
                    preRunCaptureIdleTimeoutMs: details.RunPackets.Count > 0 ? 1500 : 0,
                    preRunCaptureMaxFrames: details.RunPackets.Count > 0 ? 8 : 0,
                    cancellationToken);
            }

            var result = await client.AutomatedEntryAsync(state, endpoint, sessionId, packets, cancellationToken);
            var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
            updatedState.Save(authPath);
            logger.WriteLine($"{stepPrefix}.status={result.StatusCode}");
            logger.WriteLine($"{stepPrefix}.success={result.IsSuccessStatusCode}");
            if (!result.IsSuccessStatusCode) {
                throw new InvalidOperationException($"{endpoint} failed");
            }

            if (result.DecodedBody is not null) {
                logger.WriteLine($"{stepPrefix}.summary={AuthStateSupport.SummarizeSessionStartResponse(result.DecodedBody)}");
                AutomatedActionTraceLogging.LogTrace($"{stepPrefix}.trace", result.DecodedBody, logger);
            }

            AuthStateSupport.LogSessionState(updatedState, logger);
            return updatedState;
        } finally {
            logger.EndSection($"auth language-dump {endpoint}");
        }
    }

    private static async Task<NuraAuthState> SubmitAnalyticsAsync(
        NuraAuthState state,
        string authPath,
        NuraAuthApiClient client,
        int bluetoothSessionId,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        logger.BeginSection("auth language-dump analytics-submit");
        try {
            logger.WriteLine("auth.language_dump.step=analytics-submit");
            var payload = BuildDefaultAnalyticsPayload();
            payload["session"] = bluetoothSessionId;
            var result = await client.CallAuthenticatedEndpointAsync(state, "analytics/submit", payload, cancellationToken);
            var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
            updatedState.Save(authPath);
            logger.WriteLine($"auth.language_dump.analytics_submit.status={result.StatusCode}");
            logger.WriteLine($"auth.language_dump.analytics_submit.success={result.IsSuccessStatusCode}");
            if (!result.IsSuccessStatusCode) {
                throw new InvalidOperationException("analytics/submit failed");
            }

            AuthStateSupport.LogSessionState(updatedState, logger);
            return updatedState;
        } finally {
            logger.EndSection("auth language-dump analytics-submit");
        }
    }

    private static Dictionary<string, object?> BuildDefaultAnalyticsPayload() {
        return new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["active_slot"] = 0,
            ["generic_mode"] = false,
            ["using_manual_mode"] = null,
            ["eu_attenuation_mode"] = "off",
            ["voice_prompt_gain"] = null,
            ["silence_detection_params"] = null,
            ["head_detection_params"] = null,
            ["multipoint_config"] = null,
            ["generic_proeq"] = null,
            ["slot1_kickit_enabled"] = true,
            ["slot1_kickit_state"] = 2,
            ["slot1_button_left"] = 2,
            ["slot1_button_left_double"] = 11,
            ["slot1_button_left_triple"] = 0,
            ["slot1_button_left_long"] = 0,
            ["slot1_button_right"] = 8,
            ["slot1_button_right_double"] = 3,
            ["slot1_button_right_triple"] = 0,
            ["slot1_button_right_long"] = 0,
            ["slot1_dial_left"] = null,
            ["slot1_dial_right"] = null,
            ["slot1_global_anc_enabled"] = true,
            ["slot1_anc_level"] = null,
            ["slot1_spatial"] = null,
            ["slot1_proeq"] = null,
            ["slot2_kickit_enabled"] = null,
            ["slot2_kickit_state"] = 6,
            ["slot2_button_left"] = 2,
            ["slot2_button_left_double"] = 11,
            ["slot2_button_left_triple"] = 0,
            ["slot2_button_left_long"] = 0,
            ["slot2_button_right"] = 8,
            ["slot2_button_right_double"] = 3,
            ["slot2_button_right_triple"] = 0,
            ["slot2_button_right_long"] = 0,
            ["slot2_dial_left"] = null,
            ["slot2_dial_right"] = null,
            ["slot2_global_anc_enabled"] = true,
            ["slot2_anc_level"] = null,
            ["slot2_spatial"] = null,
            ["slot2_proeq"] = null,
            ["slot3_kickit_enabled"] = null,
            ["slot3_kickit_state"] = 6,
            ["slot3_button_left"] = 2,
            ["slot3_button_left_double"] = 11,
            ["slot3_button_left_triple"] = 0,
            ["slot3_button_left_long"] = 0,
            ["slot3_button_right"] = 8,
            ["slot3_button_right_double"] = 3,
            ["slot3_button_right_triple"] = 0,
            ["slot3_button_right_long"] = 0,
            ["slot3_dial_left"] = null,
            ["slot3_dial_right"] = null,
            ["slot3_global_anc_enabled"] = true,
            ["slot3_anc_level"] = null,
            ["slot3_spatial"] = null,
            ["slot3_proeq"] = null
        };
    }

    private static string Prompt(string label) {
        Console.Write($"{label}: ");
        return Console.ReadLine()?.Trim()
            ?? throw new InvalidOperationException($"{label} is required");
    }

    private static bool LooksLikeSixDigitCode(string input)
        => input.Length == 6 && input.All(char.IsDigit);

    private static int ParseIntArgument(string[] args, string flag, int defaultValue) {
        var raw = ArgumentReader.OptionalValue(args, flag);
        return string.IsNullOrWhiteSpace(raw) ? defaultValue : int.Parse(raw);
    }

    private sealed record HardwareProbeResult(
        string DeviceAddress,
        int SerialNumber,
        int FirmwareVersion,
        int MaxPacketLength);

    private sealed record AuthValidationResult(NuraAuthState State, bool Success);
}
