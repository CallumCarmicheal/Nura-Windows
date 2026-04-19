using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Nura.Auth;
using NuraDesktopConsole.Library.Transport;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionAuthSessionStartNext : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        var readTimeoutMs = ParseReadTimeoutMs(args);
        var preRunCaptureIdleTimeoutMs = ParseIntArgument(args, "--pre-r-capture-ms", 0);
        var preRunCaptureMaxFrames = ParseIntArgument(args, "--pre-r-max-frames", 0);
        var postTransitionCaptureDelayMs = ParseIntArgument(args, "--post-transition-delay-ms", 4000);
        var postTransitionCaptureIdleTimeoutMs = ParseIntArgument(args, "--post-transition-capture-ms", 12000);
        var postTransitionCaptureMaxFrames = ParseIntArgument(args, "--post-transition-max-frames", 12);
        var computedMinimumOverallTimeoutMs = Math.Max(
            30000,
            readTimeoutMs + preRunCaptureIdleTimeoutMs + postTransitionCaptureDelayMs + postTransitionCaptureIdleTimeoutMs + 10000);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(
            ParseIntArgument(args, "--overall-timeout-ms", computedMinimumOverallTimeoutMs)));
        var authPath = LocalStateFiles.LoadAuthPath(logger);
        var state = NuraAuthState.LoadOrCreate(authPath);
        if (!state.HasAuthenticatedSession) {
            throw new InvalidOperationException("no authenticated session found in auth state");
        }

        var details = SessionStartResponseParser.Parse(state.LastResponseBody ?? [])
            ?? throw new InvalidOperationException("last auth response does not contain a parsed session/start continuation");
        var endpoint = ArgumentReader.OptionalValue(args, "--endpoint")
            ?? details.FinalEvent
            ?? throw new InvalidOperationException("no continuation endpoint found in last auth response");
        var sessionId = AuthStateSupport.ParseOptionalInt32(args, "--session") ?? state.BluetoothSessionId
            ?? details.SessionId
            ?? throw new InvalidOperationException("session is required; run `auth session-start` first or pass --session explicitly");
        var config = LocalStateFiles.LoadConfig(logger);
        var unencryptedGaiaVersion = ParseByteArgument(
            args,
            "--u-gaia-version",
            ParseByteArgument(args, "--gaia-version", 0x01));
        var unencryptedGaiaFlags = ParseByteArgument(
            args,
            "--u-gaia-flags",
            ParseByteArgument(args, "--gaia-flags", 0x00));
        var runGaiaVersion = ParseByteArgument(
            args,
            "--r-gaia-version",
            ParseByteArgument(args, "--gaia-version", unencryptedGaiaVersion));
        var runGaiaFlags = ParseByteArgument(
            args,
            "--r-gaia-flags",
            ParseByteArgument(args, "--gaia-flags", unencryptedGaiaFlags));
        preRunCaptureIdleTimeoutMs = ParseIntArgument(args, "--pre-r-capture-ms", details.RunPackets.Count > 0 ? 1500 : 0);
        preRunCaptureMaxFrames = ParseIntArgument(args, "--pre-r-max-frames", details.RunPackets.Count > 0 ? 8 : 0);

        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.auth_uid={state.AuthUid ?? string.Empty}");
        logger.WriteLine($"auth.session_next.endpoint={endpoint}");
        logger.WriteLine($"auth.session_next.session={sessionId}");
        logger.WriteLine($"auth.session_next.read_timeout_ms={readTimeoutMs}");
        logger.WriteLine($"auth.session_next.overall_timeout_ms={ParseIntArgument(args, "--overall-timeout-ms", computedMinimumOverallTimeoutMs)}");
        logger.WriteLine($"auth.session_next.u_gaia_version=0x{unencryptedGaiaVersion:x2}");
        logger.WriteLine($"auth.session_next.u_gaia_flags=0x{unencryptedGaiaFlags:x2}");
        logger.WriteLine($"auth.session_next.r_gaia_version=0x{runGaiaVersion:x2}");
        logger.WriteLine($"auth.session_next.r_gaia_flags=0x{runGaiaFlags:x2}");
        logger.WriteLine($"auth.session_next.pre_r_capture_ms={preRunCaptureIdleTimeoutMs}");
        logger.WriteLine($"auth.session_next.pre_r_max_frames={preRunCaptureMaxFrames}");
        logger.WriteLine($"auth.session_next.post_transition_delay_ms={postTransitionCaptureDelayMs}");
        logger.WriteLine($"auth.session_next.post_transition_capture_ms={postTransitionCaptureIdleTimeoutMs}");
        logger.WriteLine($"auth.session_next.post_transition_max_frames={postTransitionCaptureMaxFrames}");
        SessionStartExecutionSupport.LogParsedDetails("auth.session_next.source", details, logger);
        AuthStateSupport.LogSessionState(state, logger);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> continuationPackets;
        try {
            await using IHeadsetTransport transport = new RfcommHeadsetTransport();
            logger.WriteLine($"transport={transport.Describe()}");
            logger.WriteLine("auth.session_next.connecting=true");
            await transport.ConnectAsync(config.DeviceAddress, cts.Token);
            logger.WriteLine("auth.session_next.connected=true");
            continuationPackets = await SessionStartExecutionSupport.ExecuteLocalActionsAsync(
                details,
                transport,
                logger,
                unencryptedGaiaVersion,
                unencryptedGaiaFlags,
                runGaiaVersion,
                runGaiaFlags,
                preRunCaptureIdleTimeoutMs,
                preRunCaptureMaxFrames,
                cts.Token);
        } catch (SessionStartLocalTransitionException ex) {
            logger.WriteLine("auth.session_next.local_transition_detected=true");
            logger.WriteLine($"auth.session_next.local_transition.packet_index={ex.PacketIndex}");
            logger.WriteLine($"auth.session_next.local_transition.command_raw=0x{ex.RawCommandId:x4}");
            logger.WriteLine($"auth.session_next.local_transition.request_payload.hex={Convert.ToHexString(ex.RequestPayload).ToLowerInvariant()}");
            logger.WriteLine("auth.session_next.local_transition.hint=likely_headset_disconnect_or_restart");
            await TryCaptureAfterTransitionAsync(
                config.DeviceAddress,
                logger,
                postTransitionCaptureDelayMs,
                postTransitionCaptureIdleTimeoutMs,
                postTransitionCaptureMaxFrames,
                cts.Token);
            logger.WriteLine("auth.session_next.local_transition.next_step=inspect_post_transition_frames_or_reconnect_state_before_calling_home");
            return 1;
        } catch (SessionStartNoResponseException ex) {
            logger.WriteLine("auth.session_next.no_response_detected=true");
            logger.WriteLine($"auth.session_next.no_response.packet_index={ex.PacketIndex}");
            logger.WriteLine($"auth.session_next.no_response.command_raw=0x{ex.RawCommandId:x4}");
            logger.WriteLine($"auth.session_next.no_response.request_payload.hex={Convert.ToHexString(ex.RequestPayload).ToLowerInvariant()}");
            logger.WriteLine("auth.session_next.no_response.hint=packet_sent_but_no_frame_arrived_before_timeout");
            logger.WriteLine("auth.session_next.no_response.next_step=try_probe_server_entry_or_capture_async_frames");
            return 1;
        }

        using var client = new NuraAuthApiClient(logger);
        var result = await client.AutomatedEntryAsync(state, endpoint, sessionId, continuationPackets, cts.Token);
        var updatedState = AuthStateSupport.ApplyAuthResultToState(state, result, state.EmailAddress);
        updatedState.Save(authPath);

        if (result.DecodedBody is not null) {
            logger.WriteLine($"auth.session_next.response.summary={AuthStateSupport.SummarizeSessionStartResponse(result.DecodedBody)}");
            AutomatedActionTraceLogging.LogTrace("auth.session_next.trace", result.DecodedBody, logger);
            LogAutomatedEntryDetails(result.DecodedBody, logger);
        }

        logger.WriteLine($"auth.session_next.result_status={result.StatusCode}");
        logger.WriteLine($"auth.session_next.success={result.IsSuccessStatusCode}");
        AuthStateSupport.LogSessionState(updatedState, logger);
        return result.IsSuccessStatusCode ? 0 : 1;
    }

    private static void LogAutomatedEntryDetails(Dictionary<string, object?> responseBody, SessionLogger logger) {
        var details = SessionStartResponseParser.Parse(responseBody);
        if (details is null) {
            return;
        }
        SessionStartExecutionSupport.LogParsedDetails("auth.session_next", details, logger);
    }

    private static int ParseReadTimeoutMs(string[] args) {
        var raw = ArgumentReader.OptionalValue(args, "--read-timeout-ms");
        return raw is null ? 10000 : int.Parse(raw);
    }

    private static byte ParseByteArgument(string[] args, string flag, byte defaultValue) {
        var raw = ArgumentReader.OptionalValue(args, flag);
        if (string.IsNullOrWhiteSpace(raw)) {
            return defaultValue;
        }

        return raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToByte(raw[2..], 16)
            : Convert.ToByte(raw);
    }

    private static int ParseIntArgument(string[] args, string flag, int defaultValue) {
        var raw = ArgumentReader.OptionalValue(args, flag);
        return string.IsNullOrWhiteSpace(raw) ? defaultValue : int.Parse(raw);
    }

    private static async Task TryCaptureAfterTransitionAsync(
        string deviceAddress,
        SessionLogger logger,
        int reconnectDelayMs,
        int idleTimeoutMs,
        int maxFrames,
        CancellationToken cancellationToken) {
        if (idleTimeoutMs <= 0 || maxFrames <= 0) {
            logger.WriteLine("auth.session_next.post_transition_capture.enabled=false");
            return;
        }

        logger.WriteLine("auth.session_next.post_transition_capture.enabled=true");
        logger.WriteLine($"auth.session_next.post_transition_capture.reconnect_delay_ms={reconnectDelayMs}");

        if (reconnectDelayMs > 0) {
            await Task.Delay(reconnectDelayMs, cancellationToken);
        }

        try {
            await using IHeadsetTransport transport = new RfcommHeadsetTransport();
            logger.WriteLine($"transport={transport.Describe()}");
            logger.WriteLine("auth.session_next.post_transition_capture.connecting=true");
            await transport.ConnectAsync(deviceAddress, cancellationToken);
            logger.WriteLine("auth.session_next.post_transition_capture.connected=true");
            var frames = await transport.CollectAsync(logger, idleTimeoutMs, maxFrames, cancellationToken);
            logger.WriteLine($"auth.session_next.post_transition_capture.frame_count={frames.Count}");
            for (var index = 0; index < frames.Count; index++) {
                var frame = frames[index];
                logger.WriteLine($"auth.session_next.post_transition_capture.frame.{index}.command_raw=0x{frame.RawCommandId:x4}");
                logger.WriteLine($"auth.session_next.post_transition_capture.frame.{index}.data.hex={Hex.Format(frame.Data)}");
                logger.WriteLine($"auth.session_next.post_transition_capture.frame.{index}.payload_ex_status.hex={Hex.Format(frame.PayloadExcludingStatus)}");
            }
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            logger.WriteLine("auth.session_next.post_transition_capture.timeout=true");
        } catch (Exception ex) {
            logger.WriteLine($"auth.session_next.post_transition_capture.error={ex.Message}");
        }
    }
}
