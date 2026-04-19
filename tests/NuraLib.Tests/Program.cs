using System.Reflection;

using NuraLib.Crypto;
using NuraLib.Devices;
using NuraLib.Protocol;
using NuraLib.Utilities;

var tests = new CommandRoundTripTests();
tests.RunAll();
Console.WriteLine("All NuraLib packet round-trip tests passed.");

internal sealed class CommandRoundTripTests {
    private static readonly byte[] TestKey = HexEncoding.Parse("00112233445566778899aabbccddeeff");
    private static readonly byte[] TestNonce = HexEncoding.Parse("0102030405060708090a0b0c");
    private static readonly NuraDeviceInfo DoubleTapDevice = new() {
        DeviceType = NuraDeviceType.Nuraphone,
        TypeName = "Nuraphone",
        TypeTag = "nuraphone",
        DeviceAddress = "00:11:22:33:44:55",
        Serial = "TEST-DOUBLE",
        FirmwareVersion = 606,
        SupportedButtonGestures = NuraButtonGestureSupport.SingleTap | NuraButtonGestureSupport.DoubleTap | NuraButtonGestureSupport.TapAndHold
    };
    private static readonly NuraDeviceInfo TripleTapDevice = new() {
        DeviceType = NuraDeviceType.NuraTruePro,
        TypeName = "NuraTrue Pro",
        TypeTag = "nuratruepro",
        DeviceAddress = "AA:BB:CC:DD:EE:FF",
        Serial = "TEST-TRIPLE",
        FirmwareVersion = 1000,
        SupportedButtonGestures = NuraButtonGestureSupport.SingleTap | NuraButtonGestureSupport.DoubleTap | NuraButtonGestureSupport.TripleTap | NuraButtonGestureSupport.TapAndHold
    };

    public void RunAll() {
        GenerateAppChallenge_Command_RequestAndResponseRoundTrip();
        ValidateAppChallengeResponse_Command_RequestAndResponseRoundTrip();
        GetCurrentProfileId_Command_RoundTrip();
        GetProfileName_Command_RoundTrip();
        SelectProfile_Command_RoundTrip();
        AncCommands_RoundTrip();
        AncLevelAndGlobalAncCommands_RoundTrip();
        KickitAndSpatialCommands_RoundTrip();
        ButtonConfigurationCommands_RoundTrip();
        DialAndVoicePromptCommands_RoundTrip();
    }

    private static void GenerateAppChallenge_Command_RequestAndResponseRoundTrip() {
        var command = NuraCommandFactory.CreateGenerateAppChallenge();
        var frame = command.CreateFrame();

        AssertEqual("ff01000068720002", HexEncoding.Format(frame.Bytes), nameof(GenerateAppChallenge_Command_RequestAndResponseRoundTrip));

        var response = CreateResponse(0x8002, HexEncoding.Parse("00112233445566778899aabbccddeeff00"));
        var parsed = command.ParseResponse(null, response);

        AssertEqual("112233445566778899aabbccddeeff00", HexEncoding.Format(parsed), nameof(GenerateAppChallenge_Command_RequestAndResponseRoundTrip));
    }

    private static void ValidateAppChallengeResponse_Command_RequestAndResponseRoundTrip() {
        var runtime = CreateRuntime();
        var gmac = HexEncoding.Parse("00112233445566778899aabbccddeeff");
        var command = NuraCommandFactory.CreateValidateAppChallenge(runtime, gmac);
        var frame = command.CreateFrame();

        AssertEqual("ff01001c687200030102030405060708090a0b0c00112233445566778899aabbccddeeff", HexEncoding.Format(frame.Bytes), nameof(ValidateAppChallengeResponse_Command_RequestAndResponseRoundTrip));

        var response = CreateResponse(0x8003, HexEncoding.Parse("00ffeeddccbbaa99887766554433221100"));
        var parsed = command.ParseResponse(null, response);

        AssertEqual("ffeeddccbbaa99887766554433221100", HexEncoding.Format(parsed), nameof(ValidateAppChallengeResponse_Command_RequestAndResponseRoundTrip));
    }

    private static void GetCurrentProfileId_Command_RoundTrip() {
        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateGetCurrentProfileId(),
            CreateRuntime(),
            expectedPlainRequestHex: "0041",
            responsePlainPayloadHex: "0002",
            assertResponse: response => AssertEqual(2, response, nameof(GetCurrentProfileId_Command_RoundTrip)));
    }

    private static void GetProfileName_Command_RoundTrip() {
        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateGetProfileName(2),
            CreateRuntime(),
            expectedPlainRequestHex: "001a02",
            responsePlainPayloadHex: "00526f636b00",
            assertResponse: response => AssertEqual("Rock", response, nameof(GetProfileName_Command_RoundTrip)));
    }

    private static void SelectProfile_Command_RoundTrip() {
        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateSelectProfile(2),
            CreateRuntime(),
            expectedPlainRequestHex: "001b02",
            responsePlainPayloadHex: "00",
            assertResponse: response => AssertTrue(response.Length == 0, nameof(SelectProfile_Command_RoundTrip), "Expected empty ack payload.")); 
    }

    private static void AncCommands_RoundTrip() {
        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateGetAncState(1),
            CreateRuntime(),
            expectedPlainRequestHex: "004901",
            responsePlainPayloadHex: "000101",
            assertResponse: response => AssertEqual(NuraAncMode.Passthrough, response.Mode, nameof(AncCommands_RoundTrip)));

        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateSetAncState(1, new NuraAncState { AncEnabled = true, PassthroughEnabled = false }),
            CreateRuntime(),
            expectedPlainRequestHex: "0048010100",
            responsePlainPayloadHex: "00",
            assertResponse: response => AssertTrue(response.Length == 0, nameof(AncCommands_RoundTrip), "Expected empty ANC ack payload."));

        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateSetTemporaryAncState(ancEnabled: false, passthroughEnabled: true),
            CreateRuntime(),
            expectedPlainRequestHex: "004a0001",
            responsePlainPayloadHex: "00",
            assertResponse: response => AssertTrue(response.Length == 0, nameof(AncCommands_RoundTrip), "Expected empty temporary ANC ack payload."));
    }

    private static void AncLevelAndGlobalAncCommands_RoundTrip() {
        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateGetAncLevel(1),
            CreateRuntime(),
            expectedPlainRequestHex: "010201",
            responsePlainPayloadHex: "0003",
            assertResponse: response => AssertEqual(3, response, nameof(AncLevelAndGlobalAncCommands_RoundTrip)));

        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateSetAncLevel(1, 4),
            CreateRuntime(),
            expectedPlainRequestHex: "01010104",
            responsePlainPayloadHex: "00",
            assertResponse: response => AssertTrue(response.Length == 0, nameof(AncLevelAndGlobalAncCommands_RoundTrip), "Expected empty ANC level ack payload."));

        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateGetGlobalAncEnabled(1),
            CreateRuntime(),
            expectedPlainRequestHex: "011b01",
            responsePlainPayloadHex: "0001",
            assertResponse: response => AssertTrue(response, nameof(AncLevelAndGlobalAncCommands_RoundTrip), "Expected Global ANC enabled."));

        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateSetGlobalAncEnabled(1, true),
            CreateRuntime(),
            expectedPlainRequestHex: "011a0101",
            responsePlainPayloadHex: "00",
            assertResponse: response => AssertTrue(response.Length == 0, nameof(AncLevelAndGlobalAncCommands_RoundTrip), "Expected empty Global ANC ack payload."));
    }

    private static void KickitAndSpatialCommands_RoundTrip() {
        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateGetKickitEnabled(),
            CreateRuntime(),
            expectedPlainRequestHex: "00b4",
            responsePlainPayloadHex: "0001",
            assertResponse: response => AssertEqual(NuraPersonalisationMode.Personalised, response, nameof(KickitAndSpatialCommands_RoundTrip)));

        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateSetKickitEnabled(true),
            CreateRuntime(),
            expectedPlainRequestHex: "00b301",
            responsePlainPayloadHex: "00",
            assertResponse: response => AssertTrue(response.Length == 0, nameof(KickitAndSpatialCommands_RoundTrip), "Expected empty kickit ack payload."));

        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateGetKickitState(1),
            CreateRuntime(),
            expectedPlainRequestHex: "011e01",
            responsePlainPayloadHex: "000401",
            assertResponse: response => {
                AssertEqual(4, response.RawLevel, nameof(KickitAndSpatialCommands_RoundTrip));
                AssertTrue(response.Enabled, nameof(KickitAndSpatialCommands_RoundTrip), "Expected kickit enabled.");
            });

        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateSetKickitState(1, levelRaw: 4, enabled: true),
            CreateRuntime(),
            expectedPlainRequestHex: "011d010401",
            responsePlainPayloadHex: "00",
            assertResponse: response => AssertTrue(response.Length == 0, nameof(KickitAndSpatialCommands_RoundTrip), "Expected empty kickit state ack payload."));

        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateGetSpatialState(),
            CreateRuntime(),
            expectedPlainRequestHex: "017a",
            responsePlainPayloadHex: "0001",
            assertResponse: response => AssertTrue(response, nameof(KickitAndSpatialCommands_RoundTrip), "Expected spatial enabled."));

        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateSetSpatialState(true),
            CreateRuntime(),
            expectedPlainRequestHex: "017b01",
            responsePlainPayloadHex: "00",
            assertResponse: response => AssertTrue(response.Length == 0, nameof(KickitAndSpatialCommands_RoundTrip), "Expected empty spatial ack payload."));
    }

    private static void ButtonConfigurationCommands_RoundTrip() {
        var doubleTapConfiguration = new NuraButtonConfiguration {
            LeftSingleTap = NuraButtonFunction.PlayPauseOnly,
            RightSingleTap = NuraButtonFunction.NextTrack,
            LeftDoubleTap = NuraButtonFunction.VolumeDown,
            RightDoubleTap = NuraButtonFunction.VolumeUp,
            LeftTapAndHold = NuraButtonFunction.ToggleSocial,
            RightTapAndHold = NuraButtonFunction.VoiceAssistant
        };

        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateGetButtonConfiguration(DoubleTapDevice, 1),
            CreateRuntime(),
            expectedPlainRequestHex: "00b701",
            responsePlainPayloadHex: "00020b0d0c0813",
            assertResponse: response => AssertEqual(doubleTapConfiguration, response, nameof(ButtonConfigurationCommands_RoundTrip)));

        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateSetButtonConfiguration(DoubleTapDevice, 1, doubleTapConfiguration),
            CreateRuntime(),
            expectedPlainRequestHex: "00b601020b0d0c0813",
            responsePlainPayloadHex: "00",
            assertResponse: response => AssertTrue(response.Length == 0, nameof(ButtonConfigurationCommands_RoundTrip), "Expected empty double-tap button config ack payload."));

        var tripleTapConfiguration = new NuraButtonConfiguration {
            LeftSingleTap = NuraButtonFunction.PlayPauseOnly,
            RightSingleTap = NuraButtonFunction.NextTrack,
            LeftDoubleTap = NuraButtonFunction.VolumeDown,
            RightDoubleTap = NuraButtonFunction.VolumeUp,
            LeftTapAndHold = NuraButtonFunction.ToggleSocial,
            RightTapAndHold = NuraButtonFunction.VoiceAssistant,
            LeftTripleTap = NuraButtonFunction.ToggleSpatial,
            RightTripleTap = NuraButtonFunction.ToggleGamingMode
        };

        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateGetButtonConfiguration(TripleTapDevice, 1),
            CreateRuntime(),
            expectedPlainRequestHex: "007301",
            responsePlainPayloadHex: "00020b0d0c08131819",
            assertResponse: response => AssertEqual(tripleTapConfiguration, response, nameof(ButtonConfigurationCommands_RoundTrip)));

        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateSetButtonConfiguration(TripleTapDevice, 1, tripleTapConfiguration),
            CreateRuntime(),
            expectedPlainRequestHex: "007401020b0d0c08131819",
            responsePlainPayloadHex: "00",
            assertResponse: response => AssertTrue(response.Length == 0, nameof(ButtonConfigurationCommands_RoundTrip), "Expected empty triple-tap button config ack payload."));
    }

    private static void DialAndVoicePromptCommands_RoundTrip() {
        var dialConfiguration = new NuraDialConfiguration {
            Left = NuraDialFunction.Kickit,
            Right = NuraDialFunction.Volume
        };

        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateGetDialConfiguration(1),
            CreateRuntime(),
            expectedPlainRequestHex: "010601",
            responsePlainPayloadHex: "000103",
            assertResponse: response => AssertEqual(dialConfiguration, response, nameof(DialAndVoicePromptCommands_RoundTrip)));

        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateSetDialConfiguration(1, dialConfiguration),
            CreateRuntime(),
            expectedPlainRequestHex: "0105010103",
            responsePlainPayloadHex: "00",
            assertResponse: response => AssertTrue(response.Length == 0, nameof(DialAndVoicePromptCommands_RoundTrip), "Expected empty dial config ack payload."));

        AssertAuthenticatedRoundTrip(
            NuraCommandFactory.CreateSetVoicePromptGain(NuraVoicePromptGain.Low),
            CreateRuntime(),
            expectedPlainRequestHex: "0176ec",
            responsePlainPayloadHex: "00",
            assertResponse: response => AssertTrue(response.Length == 0, nameof(DialAndVoicePromptCommands_RoundTrip), "Expected empty voice prompt gain ack payload."));
    }

    private static NuraSessionRuntime CreateRuntime() => NuraSessionRuntime.Create(TestKey, TestNonce);

    private static GaiaResponse CreateResponse(ushort rawCommandId, byte[] payload) =>
        GaiaResponse.Parse(GaiaPacketFactory.CreateRawCommand(rawCommandId, payload).Bytes);

    private static void AssertAuthenticatedRoundTrip<TResponse>(
        NuraBluetoothCommand<TResponse> command,
        NuraSessionRuntime runtime,
        string expectedPlainRequestHex,
        string responsePlainPayloadHex,
        Action<TResponse> assertResponse) {
        var frame = command.CreateFrame(runtime);
        AssertEqual((ushort)GaiaCommandId.EntryAppEncryptedAuthenticated, (ushort)frame.CommandId, command.Name);
        AssertEqual(expectedPlainRequestHex, DecryptAuthenticatedRequestPayload(frame, TestKey, TestNonce), command.Name);

        var response = CreateAuthenticatedResponse(responsePlainPayloadHex);
        var parsed = command.ParseResponse(runtime, response);
        assertResponse(parsed);
    }

    private static string DecryptAuthenticatedRequestPayload(GaiaFrame frame, byte[] key, byte[] nonce) {
        var payload = GaiaResponse.Parse(frame.Bytes).Payload;
        var tag = payload[..16];
        var cipherText = payload[16..];
        var crypto = new NuraSessionCrypto(key.ToArray(), nonce.ToArray(), 1, 1);
        var decryptMethod = typeof(NuraSessionCrypto).GetMethod("DecryptAuthenticatedInternal", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DecryptAuthenticatedInternal was not found.");
        var encryptCounterBlockField = typeof(NuraSessionCrypto).GetField("_encryptCounterBlock", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_encryptCounterBlock was not found.");
        var counterBlock = encryptCounterBlockField.GetValue(crypto)
            ?? throw new InvalidOperationException("_encryptCounterBlock was null.");
        var plain = (byte[]?)decryptMethod.Invoke(crypto, [counterBlock, cipherText, tag])
            ?? throw new InvalidOperationException("DecryptAuthenticatedInternal returned null.");
        return HexEncoding.Format(plain);
    }

    private static GaiaResponse CreateAuthenticatedResponse(string responsePlainPayloadHex) {
        var plainPayload = HexEncoding.Parse(responsePlainPayloadHex);
        var crypto = new NuraSessionCrypto(TestKey.ToArray(), TestNonce.ToArray(), 1, 1);
        var encryptMethod = typeof(NuraSessionCrypto).GetMethod("EncryptAuthenticatedInternal", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("EncryptAuthenticatedInternal was not found.");
        var decryptCounterBlockField = typeof(NuraSessionCrypto).GetField("_decryptCounterBlock", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("_decryptCounterBlock was not found.");
        var counterBlock = decryptCounterBlockField.GetValue(crypto)
            ?? throw new InvalidOperationException("_decryptCounterBlock was null.");
        var encrypted = encryptMethod.Invoke(crypto, [counterBlock, plainPayload])
            ?? throw new InvalidOperationException("EncryptAuthenticatedInternal returned null.");

        var encryptedType = encrypted.GetType();
        var tag = (byte[]?)encryptedType.GetField("Item1")?.GetValue(encrypted)
            ?? throw new InvalidOperationException("EncryptAuthenticatedInternal did not return a tag.");
        var cipherText = (byte[]?)encryptedType.GetField("Item2")?.GetValue(encrypted)
            ?? throw new InvalidOperationException("EncryptAuthenticatedInternal did not return ciphertext.");
        var payload = new byte[1 + tag.Length + cipherText.Length];
        payload[0] = 0x00;
        Buffer.BlockCopy(tag, 0, payload, 1, tag.Length);
        Buffer.BlockCopy(cipherText, 0, payload, 1 + tag.Length, cipherText.Length);
        return CreateResponse(0x800A, payload);
    }

    private static void AssertEqual<T>(T expected, T actual, string testName) {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) {
            throw new InvalidOperationException($"{testName}: expected '{expected}' but got '{actual}'.");
        }
    }

    private static void AssertTrue(bool condition, string testName, string message) {
        if (!condition) {
            throw new InvalidOperationException($"{testName}: {message}");
        }
    }
}
