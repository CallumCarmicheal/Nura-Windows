using desktop_app.Auth;
using desktop_app.Config;
using desktop_app.Crypto;
using desktop_app.Logging;
using desktop_app.Protocol;
using desktop_app.Transport;

namespace desktop_app;

internal static class Program {
    private static int Main(string[] args) {
        using var logger = SessionLogger.CreateDefault();

        try {
            return Run(args, logger);
        } catch (Exception ex) {
            logger.Error($"fatal: {ex}");
            return 1;
        }
    }

    private static int Run(string[] args, SessionLogger logger) {
        if (args.Length == 0) {
            PrintUsage(logger);
            return 1;
        }

        var commandName = args[0].ToLowerInvariant();
        logger.WriteLine($"command.name={commandName}");
        logger.WriteLine($"log.path={logger.LogPath}");

        if (commandName == "auth") {
            var authPath = ResolveAuthStatePath();
            logger.WriteLine($"auth.path={authPath}");
            return RunAuthFlow(args, authPath, logger);
        }

        var configPath = ResolveConfigPath();
        logger.WriteLine($"config.path={configPath}");
        var config = NuraOfflineConfig.Load(configPath);

        return commandName switch {
            "tests/plan" => RunPlan(config, logger),
            "data/respond" => RunRespond(config, args, logger),
            "data/encrypt" => RunEncrypt(config, args, logger),
            "data/parse" => RunParse(args, logger),
            "tests/live-handshake" => RunLiveHandshake(config, logger),
            "tests/fresh-nonce-test" => RunFreshNonceTest(config, logger),
            "tests/anc-toggle-test" => RunAncToggleTest(config, logger),
            _ => ThrowUsage(commandName)
        };
    }

    private static int RunAuthFlow(string[] args, string authPath, SessionLogger logger) {
        if (args.Length < 2) {
            PrintAuthFlowUsage(logger);
            return 1;
        }

        return args[1].ToLowerInvariant() switch {
            "send-email" => RunAuthFlowSendEmail(args, authPath, logger),
            "verify-code" => RunAuthFlowVerifyCode(args, authPath, logger),
            "validate-token" => RunAuthFlowValidateToken(authPath, logger),
            "show-state" => RunAuthFlowShowState(authPath, logger),
            _ => ThrowUsage(string.Join(' ', args.Take(2)))
        };
    }

    private static int RunPlan(NuraOfflineConfig config, SessionLogger logger) {
        var runtime = SessionRuntime.Create(config);
        var transport = new NullTransport();

        PrintBanner(config, runtime, logger);
        logger.WriteLine("offline bootstrap plan:");
        logger.WriteLine($"  1. connect RFCOMM/SPP to {config.DeviceAddress} using {BluetoothConstants.SerialPortServiceClassUuid}");
        logger.WriteLine($"  2. send {DescribeFrame(GaiaPackets.BuildCommand(GaiaCommandId.CryptoAppGenerateChallenge))}");
        logger.WriteLine("  3. receive 16-byte headset app challenge");
        logger.WriteLine("  4. compute GMAC over that challenge with the app session crypto");
        logger.WriteLine("  5. send CryptoAppValidateChallengeResponse with nonce||gmac");
        logger.WriteLine("  6. receive headset validate-response GMAC");
        logger.WriteLine("  7. verify it against \"Kyle is awesome!\"");
        logger.WriteLine("  8. if valid, start safe authenticated reads");
        logger.WriteLine();

        logger.WriteLine("safe authenticated reads:");
        foreach (var query in NuraQueries.SafeStartupReads(config.CurrentProfileId)) {
            var frame = GaiaPackets.BuildAuthenticatedAppCommand(runtime.Crypto, query.Payload);
            logger.WriteLine($"  {query.Description}: {DescribeFrame(frame)}");
        }

        logger.WriteLine();
        logger.WriteLine($"transport stub: {transport.Describe()}");
        return 0;
    }

    private static int RunRespond(NuraOfflineConfig config, string[] args, SessionLogger logger) {
        var challengeHex = ArgumentReader.RequiredValue(args, "--challenge-hex");
        var runtime = SessionRuntime.Create(config);
        var challenge = Hex.Parse(challengeHex);

        if (challenge.Length != 16) {
            throw new InvalidOperationException("challenge must be exactly 16 bytes");
        }

        var gmac = runtime.Crypto.GenerateChallengeResponse(challenge);
        var validateFrame = GaiaPackets.BuildCommand(
            GaiaCommandId.CryptoAppValidateChallengeResponse,
            ByteArray.Combine(runtime.Nonce, gmac));

        PrintBanner(config, runtime, logger);
        logger.WriteLine($"challenge.hex={Hex.Format(challenge)}");
        logger.WriteLine($"response.gmac.hex={Hex.Format(gmac)}");
        logger.WriteLine($"validate.frame.hex={Hex.Format(validateFrame.Bytes)}");
        return 0;
    }

    private static int RunEncrypt(NuraOfflineConfig config, string[] args, SessionLogger logger) {
        var payloadHex = ArgumentReader.RequiredValue(args, "--payload-hex");
        var runtime = SessionRuntime.Create(config);
        var payload = Hex.Parse(payloadHex);
        var authenticate = !ArgumentReader.HasFlag(args, "--unauth");
        var frame = authenticate
            ? GaiaPackets.BuildAuthenticatedAppCommand(runtime.Crypto, payload)
            : GaiaPackets.BuildUnauthenticatedAppCommand(runtime.Crypto, payload);

        PrintBanner(config, runtime, logger);
        logger.WriteLine($"plain.hex={Hex.Format(payload)}");
        logger.WriteLine($"authenticate={authenticate}");
        logger.WriteLine($"frame.hex={Hex.Format(frame.Bytes)}");
        return 0;
    }

    private static int RunParse(string[] args, SessionLogger logger) {
        var frameHex = ArgumentReader.RequiredValue(args, "--frame-hex");
        var response = GaiaResponse.Parse(Hex.Parse(frameHex));
        logger.WriteLine(response.ToDisplayString());
        return 0;
    }

    private static int RunLiveHandshake(NuraOfflineConfig config, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        return RunLiveHandshakeAsync(config, logger, cts.Token).GetAwaiter().GetResult();
    }

    private static int RunFreshNonceTest(NuraOfflineConfig config, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        return RunFreshNonceTestAsync(config, logger, cts.Token).GetAwaiter().GetResult();
    }

    private static int RunAncToggleTest(NuraOfflineConfig config, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return RunAncToggleTestAsync(config, logger, cts.Token).GetAwaiter().GetResult();
    }

    private static int RunAuthFlowSendEmail(string[] args, string authPath, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return RunAuthFlowSendEmailAsync(args, authPath, logger, cts.Token).GetAwaiter().GetResult();
    }

    private static int RunAuthFlowVerifyCode(string[] args, string authPath, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return RunAuthFlowVerifyCodeAsync(args, authPath, logger, cts.Token).GetAwaiter().GetResult();
    }

    private static int RunAuthFlowValidateToken(string authPath, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return RunAuthFlowValidateTokenAsync(authPath, logger, cts.Token).GetAwaiter().GetResult();
    }

    private static async Task<int> RunLiveHandshakeAsync(
        NuraOfflineConfig config,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        var runtime = SessionRuntime.Create(config);

        await using IHeadsetTransport transport = new RfcommHeadsetTransport();
        PrintBanner(config, runtime, logger);
        logger.WriteLine($"transport={transport.Describe()}");
        logger.WriteLine("connecting...");
        await transport.ConnectAsync(config.DeviceAddress, cancellationToken);
        logger.WriteLine("connected");

        await PerformAppHandshakeAsync(runtime, transport, logger, cancellationToken);

        var deepSleepResponse = await transport.ExchangeAsync(
            GaiaPackets.BuildAuthenticatedAppCommand(runtime.Crypto, Hex.Parse("006c")),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            logger,
            cancellationToken);

        var deepSleepPlain = DecryptAuthenticatedResponse(runtime.Crypto, deepSleepResponse, logger, "deep_sleep");
        logger.WriteLine($"post_auth.deep_sleep.payload.hex={Hex.Format(deepSleepPlain)}");

        var profileResponse = await transport.ExchangeAsync(
            GaiaPackets.BuildAuthenticatedAppCommand(runtime.Crypto, Hex.Parse("0041")),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            logger,
            cancellationToken);

        var profilePlain = DecryptAuthenticatedResponse(runtime.Crypto, profileResponse, logger, "current_profile");
        logger.WriteLine($"post_auth.current_profile={profilePlain[0]}");

        var batteryResponse = await transport.ExchangeAsync(
            GaiaPackets.BuildAuthenticatedAppCommand(runtime.Crypto, Hex.Parse("007f")),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            logger,
            cancellationToken);

        var batteryPlain = DecryptAuthenticatedResponse(runtime.Crypto, batteryResponse, logger, "battery");
        logger.WriteLine($"post_auth.battery.payload.hex={Hex.Format(batteryPlain)}");
        if (TryDecodeBatteryStatus(batteryPlain, out var batteryStatus)) {
            logger.WriteLine($"post_auth.battery.voltage_mv={batteryStatus.BatteryVoltageMillivolts}");
            logger.WriteLine($"post_auth.battery.level_raw={batteryStatus.BatteryLevelRaw}");
            logger.WriteLine($"post_auth.battery.percent={batteryStatus.BatteryPercentage}");
            logger.WriteLine($"post_auth.battery.charger_state_raw={batteryStatus.ChargerStateRaw}");
            logger.WriteLine($"post_auth.battery.charger_voltage_mv={batteryStatus.ChargerVoltageMillivolts}");
            logger.WriteLine($"post_auth.battery.charger_level_raw={batteryStatus.ChargerLevelRaw}");
            logger.WriteLine($"post_auth.battery.ntc_voltage_mv={batteryStatus.NtcVoltageMillivolts}");
            logger.WriteLine($"post_auth.battery.ntc_level_raw={batteryStatus.NtcLevelRaw}");
        }

        return 0;
    }

    private static async Task<int> RunFreshNonceTestAsync(
        NuraOfflineConfig config,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        var runtime = SessionRuntime.CreateWithFreshNonce(config);

        await using IHeadsetTransport transport = new RfcommHeadsetTransport();
        PrintBanner(config, runtime, logger);
        logger.WriteLine("session.nonce_mode=fresh_random");
        logger.WriteLine($"transport={transport.Describe()}");
        logger.WriteLine("connecting...");
        await transport.ConnectAsync(config.DeviceAddress, cancellationToken);
        logger.WriteLine("connected");

        await PerformAppHandshakeAsync(runtime, transport, logger, cancellationToken);

        var deepSleepResponse = await transport.ExchangeAsync(
            GaiaPackets.BuildAuthenticatedAppCommand(runtime.Crypto, Hex.Parse("006c")),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            logger,
            cancellationToken);

        var deepSleepPlain = DecryptAuthenticatedResponse(runtime.Crypto, deepSleepResponse, logger, "fresh_nonce_deep_sleep");
        logger.WriteLine($"fresh_nonce.deep_sleep.payload.hex={Hex.Format(deepSleepPlain)}");

        var profileResponse = await transport.ExchangeAsync(
            GaiaPackets.BuildAuthenticatedAppCommand(runtime.Crypto, Hex.Parse("0041")),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            logger,
            cancellationToken);

        var profilePlain = DecryptAuthenticatedResponse(runtime.Crypto, profileResponse, logger, "fresh_nonce_current_profile");
        logger.WriteLine($"fresh_nonce.current_profile={(profilePlain.Length > 0 ? profilePlain[0] : 0)}");

        var batteryResponse = await transport.ExchangeAsync(
            GaiaPackets.BuildAuthenticatedAppCommand(runtime.Crypto, Hex.Parse("007f")),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            logger,
            cancellationToken);

        var batteryPlain = DecryptAuthenticatedResponse(runtime.Crypto, batteryResponse, logger, "fresh_nonce_battery");
        logger.WriteLine($"fresh_nonce.battery.payload.hex={Hex.Format(batteryPlain)}");
        if (TryDecodeBatteryStatus(batteryPlain, out var batteryStatus)) {
            logger.WriteLine($"fresh_nonce.battery.voltage_mv={batteryStatus.BatteryVoltageMillivolts}");
            logger.WriteLine($"fresh_nonce.battery.level_raw={batteryStatus.BatteryLevelRaw}");
            logger.WriteLine($"fresh_nonce.battery.percent={batteryStatus.BatteryPercentage}");
        }

        logger.WriteLine("fresh_nonce.result=success");
        return 0;
    }

    private static async Task<int> RunAncToggleTestAsync(
        NuraOfflineConfig config,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        var runtime = SessionRuntime.Create(config);

        await using IHeadsetTransport transport = new RfcommHeadsetTransport();
        PrintBanner(config, runtime, logger);
        logger.WriteLine($"transport={transport.Describe()}");
        logger.WriteLine("connecting...");
        await transport.ConnectAsync(config.DeviceAddress, cancellationToken);
        logger.WriteLine("connected");

        await PerformAppHandshakeAsync(runtime, transport, logger, cancellationToken);

        var currentProfile = await ReadCurrentProfileAsync(runtime, transport, logger, cancellationToken);
        logger.WriteLine($"anc_test.current_profile={currentProfile}");

        var originalState = await ReadAncStateAsync(runtime, transport, logger, currentProfile, "before", cancellationToken);
        LogAncState(logger, "anc_test.before", originalState);

        var toggledState = new AncState(
            PrimaryRaw: originalState.PrimaryRaw,
            SecondaryRaw: originalState.SecondaryRaw == 0 ? (byte)0x01 : (byte)0x00);
        LogAncState(logger, "anc_test.toggle.target", toggledState);
        await SetAncStateAsync(runtime, transport, logger, currentProfile, toggledState, "toggle", cancellationToken);

        var afterToggleState = await ReadAncStateAsync(runtime, transport, logger, currentProfile, "after_toggle", cancellationToken);
        LogAncState(logger, "anc_test.after_toggle", afterToggleState);

        logger.WriteLine("anc_test.waiting_ms=5000");
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

        await SetAncStateAsync(runtime, transport, logger, currentProfile, originalState, "restore", cancellationToken);

        var restoredState = await ReadAncStateAsync(runtime, transport, logger, currentProfile, "restored", cancellationToken);
        LogAncState(logger, "anc_test.restored", restoredState);

        return 0;
    }

    private static async Task<int> RunAuthFlowSendEmailAsync(
        string[] args,
        string authPath,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        var emailAddress = ArgumentReader.RequiredValue(args, "--email");
        var state = NuraAuthState.LoadOrCreate(authPath) with {
            EmailAddress = emailAddress
        };

        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.uuid={state.Uuid}");
        logger.WriteLine($"auth.email={emailAddress}");

        using var client = new NuraAuthApiClient(logger);
        var result = await client.SendLoginEmailAsync(state, emailAddress, cancellationToken);

        state.WithAuthHeaders(
                accessToken: null,
                clientKey: null,
                authUid: null,
                expiryUnixSeconds: null,
                responseBody: result.DecodedBody,
                emailAddress: emailAddress)
            .Save(authPath);

        logger.WriteLine($"auth.result=send_email_complete status={result.StatusCode}");
        return result.IsSuccessStatusCode ? 0 : 1;
    }

    private static async Task<int> RunAuthFlowVerifyCodeAsync(
        string[] args,
        string authPath,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        var state = NuraAuthState.LoadOrCreate(authPath);
        var emailAddress = ArgumentReader.OptionalValue(args, "--email") ?? state.EmailAddress
            ?? throw new InvalidOperationException("email is required for verify-code when auth state does not already contain one");
        var oneTimeCode = ArgumentReader.RequiredValue(args, "--code");

        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.uuid={state.Uuid}");
        logger.WriteLine($"auth.email={emailAddress}");
        logger.WriteLine($"auth.code_length={oneTimeCode.Length}");

        using var client = new NuraAuthApiClient(logger);
        var result = await client.VerifyCodeAsync(state, emailAddress, oneTimeCode, cancellationToken);

        var updatedState = state.WithAuthHeaders(
            result.AccessToken,
            result.ClientKey,
            result.AuthUid ?? emailAddress,
            result.ExpiryUnixSeconds,
            result.DecodedBody,
            emailAddress);
        updatedState.Save(authPath);

        logger.WriteLine($"auth.has_authenticated_session={updatedState.HasAuthenticatedSession}");
        if (updatedState.TokenExpiryUnixSeconds is { } expiryUnixSeconds) {
            logger.WriteLine($"auth.token_expiry_unix={expiryUnixSeconds}");
            logger.WriteLine($"auth.token_expiry_utc={DateTimeOffset.FromUnixTimeSeconds(expiryUnixSeconds):O}");
        }

        return result.IsSuccessStatusCode ? 0 : 1;
    }

    private static async Task<int> RunAuthFlowValidateTokenAsync(
        string authPath,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        var state = NuraAuthState.LoadOrCreate(authPath);
        if (!state.HasAuthenticatedSession) {
            throw new InvalidOperationException("no authenticated session found in auth state");
        }

        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.uuid={state.Uuid}");
        logger.WriteLine($"auth.auth_uid={state.AuthUid ?? string.Empty}");

        using var client = new NuraAuthApiClient(logger);
        var result = await client.ValidateTokenAsync(state, cancellationToken);

        var updatedState = state.WithAuthHeaders(
            result.AccessToken,
            result.ClientKey,
            result.AuthUid,
            result.ExpiryUnixSeconds,
            result.DecodedBody,
            state.EmailAddress);
        updatedState.Save(authPath);

        logger.WriteLine($"auth.validate_token.result_status={result.StatusCode}");
        logger.WriteLine($"auth.has_authenticated_session={updatedState.HasAuthenticatedSession}");
        return result.IsSuccessStatusCode ? 0 : 1;
    }

    private static int RunAuthFlowShowState(string authPath, SessionLogger logger) {
        var state = NuraAuthState.LoadOrCreate(authPath);
        logger.WriteLine($"auth.api_base={state.ApiBase}");
        logger.WriteLine($"auth.uuid={state.Uuid}");
        logger.WriteLine($"auth.email={state.EmailAddress ?? string.Empty}");
        logger.WriteLine($"auth.has_authenticated_session={state.HasAuthenticatedSession}");
        logger.WriteLine($"auth.auth_uid={state.AuthUid ?? string.Empty}");
        logger.WriteLine($"auth.access_token={state.AccessToken ?? string.Empty}");
        logger.WriteLine($"auth.client_key={state.ClientKey ?? string.Empty}");
        if (state.TokenExpiryUnixSeconds is { } expiryUnixSeconds) {
            logger.WriteLine($"auth.token_expiry_unix={expiryUnixSeconds}");
            logger.WriteLine($"auth.token_expiry_utc={DateTimeOffset.FromUnixTimeSeconds(expiryUnixSeconds):O}");
        }

        return 0;
    }

    private static void PrintBanner(NuraOfflineConfig config, SessionRuntime runtime, SessionLogger logger) {
        logger.WriteLine($"device.address={config.DeviceAddress}");
        logger.WriteLine($"device.serial={config.SerialNumber}");
        logger.WriteLine($"device.profile={config.CurrentProfileId}");
        logger.WriteLine($"device.key.hex={Hex.Format(config.DeviceKey)}");
        logger.WriteLine($"session.nonce.hex={Hex.Format(runtime.Nonce)}");
        logger.WriteLine($"session.enc_counter={runtime.Crypto.EncryptCounter}");
        logger.WriteLine($"session.dec_counter={runtime.Crypto.DecryptCounter}");
        logger.WriteLine();
    }

    private static string DescribeFrame(GaiaFrame frame)
        => $"{frame.CommandId} => {Hex.Format(frame.Bytes)}";

    private static int ThrowUsage(string command)
        => throw new InvalidOperationException($"unknown command: {command}");

    private static void PrintUsage(SessionLogger logger) {
        logger.WriteLine("Usage:");
        logger.WriteLine("  plan");
        logger.WriteLine("  respond --challenge-hex <16-byte-hex>");
        logger.WriteLine("  encrypt --payload-hex <hex> [--unauth]");
        logger.WriteLine("  parse --frame-hex <hex>");
        logger.WriteLine("  live-handshake");
        logger.WriteLine("  fresh-nonce-test");
        logger.WriteLine("  anc-toggle-test");
        logger.WriteLine("  auth send-email --email <address>");
        logger.WriteLine("  auth verify-code --code <6-digit-code> [--email <address>]");
        logger.WriteLine("  auth validate-token");
        logger.WriteLine("  auth show-state");
        logger.WriteLine();
        logger.WriteLine("Create nura-config.json beside the executable. A sample file is included in the project root.");
    }

    private static void PrintAuthFlowUsage(SessionLogger logger) {
        logger.WriteLine("Usage:");
        logger.WriteLine("  auth send-email --email <address>");
        logger.WriteLine("  auth verify-code --code <6-digit-code> [--email <address>]");
        logger.WriteLine("  auth validate-token");
        logger.WriteLine("  auth show-state");
        logger.WriteLine();
        logger.WriteLine("Auth state is stored beside the executable as nura-auth.json.");
    }

    private static string ResolveConfigPath() {

        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "nura-config.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "NuraDesktopApp", "nura-config.json"),
            Path.Combine(AppContext.BaseDirectory, "nura-config.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "nura-config.json"),
        };

        var normalizedCandidates = candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var match = normalizedCandidates.FirstOrDefault(File.Exists);
        if (match is null) {
            throw new FileNotFoundException("nura-config.json not found in the working directory or app directory");
        }

        return match;
    }

    private static string ResolveAuthStatePath() {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "nura-auth.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "NuraDesktopApp", "nura-auth.json"),
            Path.Combine(AppContext.BaseDirectory, "nura-auth.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "nura-auth.json")
        };

        return candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static byte[] DecryptAuthenticatedResponse(
        NuraSessionCrypto crypto,
        GaiaResponse response,
        SessionLogger logger,
        string label) {
        var plain = crypto.DecryptAuthenticated(response.PayloadExcludingStatus);
        logger.WriteLine($"rx.auth.plain.{label}.hex={Hex.Format(plain)}");
        return plain.Length <= 1 ? Array.Empty<byte>() : plain[1..];
    }

    private static async Task PerformAppHandshakeAsync(
        SessionRuntime runtime,
        IHeadsetTransport transport,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        var challengeResponse = await transport.ExchangeAsync(
            GaiaPackets.BuildCommand(GaiaCommandId.CryptoAppGenerateChallenge),
            GaiaCommandId.CryptoAppGenerateChallenge,
            logger,
            cancellationToken);

        var challenge = challengeResponse.PayloadExcludingStatus;
        if (challenge.Length != 16) {
            throw new InvalidOperationException($"unexpected challenge length: {challenge.Length}");
        }

        logger.WriteLine($"challenge.hex={Hex.Format(challenge)}");
        var gmac = runtime.Crypto.GenerateChallengeResponse(challenge);
        logger.WriteLine($"response.gmac.hex={Hex.Format(gmac)}");

        var validateFrame = GaiaPackets.BuildCommand(
            GaiaCommandId.CryptoAppValidateChallengeResponse,
            ByteArray.Combine(runtime.Nonce, gmac));

        var validateResponse = await transport.ExchangeAsync(
            validateFrame,
            GaiaCommandId.CryptoAppValidateChallengeResponse,
            logger,
            cancellationToken);

        var headsetGmac = validateResponse.PayloadExcludingStatus;
        logger.WriteLine($"headset.gmac.hex={Hex.Format(headsetGmac)}");
        var success = runtime.Crypto.ValidateResponse(headsetGmac);
        logger.WriteLine($"handshake.success={success}");
        if (!success) {
            logger.WriteLine("handshake.warning=Headset GMAC did not match local expectation; continuing because the headset accepted our challenge response with status=0x00");
        }
    }

    private static async Task<int> ReadCurrentProfileAsync(
        SessionRuntime runtime,
        IHeadsetTransport transport,
        SessionLogger logger,
        CancellationToken cancellationToken) {
        var profileResponse = await transport.ExchangeAsync(
            GaiaPackets.BuildAuthenticatedAppCommand(runtime.Crypto, Hex.Parse("0041")),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            logger,
            cancellationToken);

        var profilePlain = DecryptAuthenticatedResponse(runtime.Crypto, profileResponse, logger, "current_profile");
        if (profilePlain.Length < 1) {
            throw new InvalidOperationException("current profile response was empty");
        }

        return profilePlain[0];
    }

    private static async Task<AncState> ReadAncStateAsync(
        SessionRuntime runtime,
        IHeadsetTransport transport,
        SessionLogger logger,
        int profileId,
        string label,
        CancellationToken cancellationToken) {
        var ancStateResponse = await transport.ExchangeAsync(
            GaiaPackets.BuildAuthenticatedAppCommand(runtime.Crypto, BuildGetAncStatePayload(profileId)),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            logger,
            cancellationToken);

        var ancStatePlain = DecryptAuthenticatedResponse(runtime.Crypto, ancStateResponse, logger, $"anc_state_{label}");
        if (ancStatePlain.Length < 2) {
            throw new InvalidOperationException($"ANC state response '{label}' was too short: {ancStatePlain.Length}");
        }

        return new AncState(PrimaryRaw: ancStatePlain[0], SecondaryRaw: ancStatePlain[1]);
    }

    private static async Task SetAncStateAsync(
        SessionRuntime runtime,
        IHeadsetTransport transport,
        SessionLogger logger,
        int profileId,
        AncState state,
        string label,
        CancellationToken cancellationToken) {
        var payload = BuildSetAncStatePayload(profileId, state.PrimaryRaw, state.SecondaryRaw);
        logger.WriteLine($"anc_test.{label}.payload.hex={Hex.Format(payload)}");

        var setAncResponse = await transport.ExchangeAsync(
            GaiaPackets.BuildAuthenticatedAppCommand(runtime.Crypto, payload),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            logger,
            cancellationToken);

        var setAncPlain = DecryptAuthenticatedResponse(runtime.Crypto, setAncResponse, logger, $"anc_set_{label}");
        logger.WriteLine($"anc_test.{label}.ack.hex={Hex.Format(setAncPlain)}");
    }

    private static byte[] BuildGetAncStatePayload(int profileId) {
        return
        [
            0x00, 0x49,
            checked((byte)profileId)
        ];
    }

    private static byte[] BuildSetAncStatePayload(int profileId, byte primaryRaw, byte secondaryRaw) {
        return
        [
            0x00, 0x48,
            checked((byte)profileId),
            primaryRaw,
            secondaryRaw
        ];
    }

    private static void LogAncState(SessionLogger logger, string prefix, AncState state) {
        logger.WriteLine($"{prefix}.primary_raw={state.PrimaryRaw}");
        logger.WriteLine($"{prefix}.secondary_raw={state.SecondaryRaw}");
        logger.WriteLine($"{prefix}.mode={DescribeAncMode(state)}");
    }

    private static string DescribeAncMode(AncState state) {
        return state switch {
            { PrimaryRaw: 0x01, SecondaryRaw: 0x00 } => "ANC",
            { PrimaryRaw: 0x01, SecondaryRaw: 0x01 } => "Passthrough",
            _ => $"Unknown({state.PrimaryRaw:x2},{state.SecondaryRaw:x2})"
        };
    }

    private static bool TryDecodeBatteryStatus(byte[] payload, out BatteryStatus batteryStatus) {
        if (payload.Length < 11) {
            batteryStatus = default;
            return false;
        }

        batteryStatus = new BatteryStatus(
            BatteryVoltageMillivolts: (payload[0] << 8) | payload[1],
            BatteryLevelRaw: payload[2],
            BatteryPercentage: payload[3],
            ChargerStateRaw: payload[4],
            ChargerVoltageMillivolts: (payload[5] << 8) | payload[6],
            ChargerLevelRaw: payload[7],
            NtcVoltageMillivolts: (payload[8] << 8) | payload[9],
            NtcLevelRaw: payload[10]);
        return true;
    }

    private readonly record struct BatteryStatus(
        int BatteryVoltageMillivolts,
        int BatteryLevelRaw,
        int BatteryPercentage,
        int ChargerStateRaw,
        int ChargerVoltageMillivolts,
        int ChargerLevelRaw,
        int NtcVoltageMillivolts,
        int NtcLevelRaw);

    private readonly record struct AncState(
        byte PrimaryRaw,
        byte SecondaryRaw);
}
