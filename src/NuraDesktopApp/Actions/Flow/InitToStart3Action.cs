using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Library.Protocol;
using NuraDesktopConsole.Library.Transport;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionFlowInitToStart3 : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        var authPath = LocalStateFiles.LoadAuthPath(logger);
        var state = NuraAuthState.LoadOrCreate(authPath);
        logger.WriteLine("flow.name=init-to-start3");

        using var client = new NuraAuthApiClient(logger);

        state = await RunAppSessionAsync(state, authPath, client, logger);
        LogShowState(state, logger);
        state = await EnsureAuthenticatedAsync(state, authPath, client, logger);

        var hardwareInfo = await ProbeHardwareInfoAsync(logger);
        logger.WriteLine($"flow.hw_info.serial={hardwareInfo.SerialNumber}");
        logger.WriteLine($"flow.hw_info.firmware_version={hardwareInfo.FirmwareVersion}");
        logger.WriteLine($"flow.hw_info.max_packet_length={hardwareInfo.MaxPacketLength}");
        logger.WriteLine($"flow.hw_info.device_address={hardwareInfo.DeviceAddress}");

        state = await RunSessionStartAsync(state, authPath, client, hardwareInfo, logger);
        var startDetails = SessionStartResponseParser.Parse(state.LastResponseBody ?? [])
            ?? throw new InvalidOperationException("session/start did not produce a parsed continuation");

        state = await RunUnencryptedContinuationAsync(
            state,
            authPath,
            client,
            hardwareInfo.DeviceAddress,
            startDetails,
            "flow.start_1",
            logger);

        var start2Details = SessionStartResponseParser.Parse(state.LastResponseBody ?? [])
            ?? throw new InvalidOperationException("session/start_1 did not produce a parsed continuation");

        state = await RunUnencryptedContinuationAsync(
            state,
            authPath,
            client,
            hardwareInfo.DeviceAddress,
            start2Details,
            "flow.start_2",
            logger);

        LogStart3SourceIfPresent(state, logger);
        logger.WriteLine("flow.result=reached_start_3");
        LogShowState(state, logger);
        return 0;
    }

    private static async Task<NuraAuthState> RunAppSessionAsync(
        NuraAuthState state,
        string authPath,
        NuraAuthApiClient client,
        SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        logger.WriteLine("flow.step=app-session");
        var result = await client.AppSessionAsync(state, "app/session", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), cts.Token);
        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
        updatedState.Save(authPath);
        logger.WriteLine($"flow.app_session.status={result.StatusCode}");
        logger.WriteLine($"flow.app_session.success={result.IsSuccessStatusCode}");
        if (!result.IsSuccessStatusCode) {
            throw new InvalidOperationException("app/session failed");
        }

        AuthStateSupport.LogSessionState(updatedState, logger);
        return updatedState;
    }

    private static async Task<NuraAuthState> EnsureAuthenticatedAsync(
        NuraAuthState state,
        string authPath,
        NuraAuthApiClient client,
        SessionLogger logger) {
        logger.WriteLine("flow.step=auth-check");
        if (state.HasAuthenticatedSession) {
            var validation = await ValidateTokenAsync(state, authPath, client, logger);
            if (validation.Success && validation.State.HasAuthenticatedSession && validation.State.UserSessionId is not null) {
                logger.WriteLine("flow.auth.mode=resume");
                return validation.State;
            }

            state = validation.State;
            logger.WriteLine("flow.auth.resume_failed=true");
        }

        logger.WriteLine("flow.auth.mode=interactive_login");
        return await RunInteractiveLoginLoopAsync(state, authPath, client, logger);
    }

    private static async Task<NuraAuthState> SendEmailAsync(
        NuraAuthState state,
        string authPath,
        NuraAuthApiClient client,
        string emailAddress,
        SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        logger.WriteLine("flow.step=send-email");
        var result = await client.SendLoginEmailAsync(state, emailAddress, cts.Token);
        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, emailAddress);
        updatedState.Save(authPath);
        logger.WriteLine($"flow.send_email.status={result.StatusCode}");
        logger.WriteLine($"flow.send_email.success={result.IsSuccessStatusCode}");
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
        SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        logger.WriteLine("flow.step=verify-code");
        var result = await client.VerifyCodeAsync(state, emailAddress, code, cts.Token);
        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, emailAddress, result.AuthUid ?? emailAddress);
        updatedState.Save(authPath);
        logger.WriteLine($"flow.verify_code.status={result.StatusCode}");
        logger.WriteLine($"flow.verify_code.success={result.IsSuccessStatusCode}");
        if (!result.IsSuccessStatusCode) {
            throw new InvalidOperationException("verify-code failed");
        }

        AuthStateSupport.LogSessionState(updatedState, logger);
        return updatedState;
    }

    private static async Task<NuraAuthState> RunInteractiveLoginLoopAsync(
        NuraAuthState state,
        string authPath,
        NuraAuthApiClient client,
        SessionLogger logger) {
        var emailAddress = state.EmailAddress ?? Prompt("Email address");
        state = await SendEmailAsync(state, authPath, client, emailAddress, logger);

        while (true) {
            Console.Write("Enter 6-digit code, .email to resend/change email, or .quit to abort: ");
            var input = Console.ReadLine()?.Trim() ?? string.Empty;
            logger.WriteLine($"flow.auth.input={input}");

            if (string.Equals(input, ".quit", StringComparison.OrdinalIgnoreCase)) {
                throw new OperationCanceledException("interactive login aborted by user");
            }

            if (string.Equals(input, ".email", StringComparison.OrdinalIgnoreCase)) {
                emailAddress = Prompt("Email address");
                state = await SendEmailAsync(state, authPath, client, emailAddress, logger);
                continue;
            }

            if (!LooksLikeSixDigitCode(input)) {
                logger.WriteLine("flow.auth.input_result=invalid_code_format");
                Console.WriteLine("Code must be exactly 6 digits, or use .email / .quit.");
                continue;
            }

            try {
                state = await VerifyCodeAsync(state, authPath, client, emailAddress, input, logger);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                logger.WriteLine($"flow.auth.verify_code_error={ex.Message}");
                Console.WriteLine("Code was not accepted. Type a new code, .email to resend/change, or .quit.");
                continue;
            }

            var validation = await ValidateTokenAsync(state, authPath, client, logger);
            state = validation.State;
            if (validation.Success && state.HasAuthenticatedSession && state.UserSessionId is not null) {
                return state;
            }

            logger.WriteLine("flow.auth.validate_result=missing_user_session_id");
            Console.WriteLine("Login succeeded but no usable user session was returned. Type .email to request a new code or .quit.");
        }
    }

    private static async Task<AuthValidationResult> ValidateTokenAsync(
        NuraAuthState state,
        string authPath,
        NuraAuthApiClient client,
        SessionLogger logger) {
        if (!state.HasAuthenticatedSession) {
            return new AuthValidationResult(state, false);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        logger.WriteLine("flow.step=validate-token");
        var result = await client.ValidateTokenAsync(state, withAppContext: false, appStartTimeUnixMilliseconds: null, cts.Token);
        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
        updatedState.Save(authPath);
        logger.WriteLine($"flow.validate_token.status={result.StatusCode}");
        logger.WriteLine($"flow.validate_token.success={result.IsSuccessStatusCode}");
        AuthStateSupport.LogSessionState(updatedState, logger);
        return new AuthValidationResult(updatedState, result.IsSuccessStatusCode);
    }

    private static async Task<HardwareProbeResult> ProbeHardwareInfoAsync(SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        logger.WriteLine("flow.step=probe-hw-info");
        var selectedDevice = ProbeDeviceSelector.Select(logger);
        ProbeDeviceSelector.LogSelected(logger, selectedDevice);

        await using IHeadsetTransport transport = new RfcommHeadsetTransport();
        logger.WriteLine($"transport={transport.Describe()}");
        logger.WriteLine("flow.hw_info.connecting=true");
        await transport.ConnectAsync(selectedDevice.Address, cts.Token);
        logger.WriteLine("flow.hw_info.connected=true");

        var deviceInfoResponse = await transport.ExchangeAsync(
            GaiaPackets.BuildCommand(GaiaCommandId.GetDeviceInfo),
            GaiaCommandId.GetDeviceInfo,
            logger,
            cts.Token);
        var deviceInfoPayload = deviceInfoResponse.PayloadExcludingStatus;
        logger.WriteLine($"flow.hw_info.device_info.payload.hex={Hex.Format(deviceInfoPayload)}");
        if (!HeadsetSupport.TryDecodeDeviceInfo(deviceInfoPayload, out var deviceInfo)) {
            throw new InvalidOperationException("failed to decode headset device info");
        }

        return new HardwareProbeResult(
            selectedDevice.Address,
            deviceInfo.SerialNumber,
            deviceInfo.FirmwareVersion,
            HeadsetSupport.GetMaxPacketLengthHint(deviceInfo.SerialNumber));
    }

    private static async Task<NuraAuthState> RunSessionStartAsync(
        NuraAuthState state,
        string authPath,
        NuraAuthApiClient client,
        HardwareProbeResult hardwareInfo,
        SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        logger.WriteLine("flow.step=session-start");
        var userSessionId = state.UserSessionId
            ?? throw new InvalidOperationException("validate-token did not produce a usable userSessionId");
        var result = await client.SessionStartAsync(
            state,
            hardwareInfo.SerialNumber,
            hardwareInfo.FirmwareVersion,
            hardwareInfo.MaxPacketLength,
            maxBulkPacketLength: 0,
            userSessionId,
            cts.Token);
        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
        updatedState.Save(authPath);
        logger.WriteLine($"flow.session_start.status={result.StatusCode}");
        logger.WriteLine($"flow.session_start.success={result.IsSuccessStatusCode}");
        if (!result.IsSuccessStatusCode) {
            throw new InvalidOperationException("session-start failed");
        }

        if (result.DecodedBody is not null) {
            logger.WriteLine($"flow.session_start.summary={AuthStateSupport.SummarizeSessionStartResponse(result.DecodedBody)}");
        }

        AuthStateSupport.LogSessionState(updatedState, logger);
        return updatedState;
    }

    private static async Task<NuraAuthState> RunUnencryptedContinuationAsync(
        NuraAuthState state,
        string authPath,
        NuraAuthApiClient client,
        string deviceAddress,
        SessionStartResponseDetails details,
        string stepName,
        SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        logger.WriteLine($"flow.step={stepName}");
        logger.WriteLine($"flow.{stepName}.source.final_event={details.FinalEvent ?? string.Empty}");

        List<IReadOnlyDictionary<string, object?>> packets;
        await using (IHeadsetTransport transport = new RfcommHeadsetTransport()) {
            logger.WriteLine($"transport={transport.Describe()}");
            logger.WriteLine($"flow.{stepName}.connecting=true");
            await transport.ConnectAsync(deviceAddress, cts.Token);
            logger.WriteLine($"flow.{stepName}.connected=true");
            packets = await SessionStartExecutionSupport.ExecuteUnencryptedPacketsAsync(details, transport, logger, 0x01, 0x00, cts.Token);
        }

        var endpoint = details.FinalEvent
            ?? throw new InvalidOperationException("continuation endpoint missing from session/start response");
        var sessionId = state.BluetoothSessionId
            ?? details.SessionId
            ?? throw new InvalidOperationException("bluetooth session id missing before continuation");
        var result = await client.AutomatedEntryAsync(state, endpoint, sessionId, packets, cts.Token);
        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
        updatedState.Save(authPath);
        logger.WriteLine($"flow.{stepName}.status={result.StatusCode}");
        logger.WriteLine($"flow.{stepName}.success={result.IsSuccessStatusCode}");
        if (!result.IsSuccessStatusCode) {
            throw new InvalidOperationException($"{stepName} failed");
        }

        if (result.DecodedBody is not null) {
            logger.WriteLine($"flow.{stepName}.summary={AuthStateSupport.SummarizeSessionStartResponse(result.DecodedBody)}");
        }

        AuthStateSupport.LogSessionState(updatedState, logger);
        return updatedState;
    }

    private static void LogStart3SourceIfPresent(NuraAuthState state, SessionLogger logger) {
        var details = SessionStartResponseParser.Parse(state.LastResponseBody ?? []);
        if (details is null) {
            return;
        }

        if (!string.Equals(details.FinalEvent, "session/start_3", StringComparison.OrdinalIgnoreCase) &&
            details.RunPackets.Count == 0) {
            return;
        }

        logger.WriteLine("flow.step=start_3_source_verbose");
        SessionStartExecutionSupport.LogParsedDetails("flow.start_3.source", details, logger);
    }

    private static void LogShowState(NuraAuthState state, SessionLogger logger) {
        logger.WriteLine("flow.step=show-state");
        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.uuid={state.Uuid}");
        logger.WriteLine($"auth.email={state.EmailAddress ?? string.Empty}");
        logger.WriteLine($"auth.has_authenticated_session={state.HasAuthenticatedSession}");
        logger.WriteLine($"auth.auth_uid={state.AuthUid ?? string.Empty}");
        AuthStateSupport.LogSessionState(state, logger);
        if (state.TokenExpiryUnixSeconds is { } expiryUnixSeconds) {
            logger.WriteLine($"auth.token_expiry_unix={expiryUnixSeconds}");
            logger.WriteLine($"auth.token_expiry_utc={DateTimeOffset.FromUnixTimeSeconds(expiryUnixSeconds):O}");
        }
    }

    private static string Prompt(string label) {
        Console.Write($"{label}: ");
        return Console.ReadLine()?.Trim()
            ?? throw new InvalidOperationException($"{label} is required");
    }

    private static bool LooksLikeSixDigitCode(string input)
        => input.Length == 6 && input.All(char.IsDigit);

    private sealed record HardwareProbeResult(
        string DeviceAddress,
        int SerialNumber,
        int FirmwareVersion,
        int MaxPacketLength);

    private sealed record AuthValidationResult(NuraAuthState State, bool Success);
}
