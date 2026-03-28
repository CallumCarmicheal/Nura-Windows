using NuraDesktopConsole.Config;
using NuraDesktopConsole.Library.Crypto;
using NuraDesktopConsole.Library.Protocol;
using NuraDesktopConsole.Library.Transport;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Library.Nura;

internal static class HeadsetSupport {
    internal static void PrintBanner(NuraOfflineConfig config, SessionRuntime runtime, SessionLogger logger) {
        logger.WriteLine($"device.address={config.DeviceAddress}");
        logger.WriteLine($"device.serial={config.SerialNumber}");
        logger.WriteLine($"device.profile={config.CurrentProfileId}");
        logger.WriteLine($"device.key.hex={Hex.Format(config.DeviceKey)}");
        logger.WriteLine($"session.nonce.hex={Hex.Format(runtime.Nonce)}");
        logger.WriteLine($"session.enc_counter={runtime.Crypto.EncryptCounter}");
        logger.WriteLine($"session.dec_counter={runtime.Crypto.DecryptCounter}");
        logger.WriteLine();
    }

    internal static async Task PerformAppHandshakeAsync(SessionRuntime runtime, IHeadsetTransport transport, SessionLogger logger, CancellationToken cancellationToken) {
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

    internal static async Task<byte[]> ReadAuthenticatedPayloadAsync(SessionRuntime runtime, IHeadsetTransport transport, SessionLogger logger, byte[] payload, string label, CancellationToken cancellationToken) {
        var response = await transport.ExchangeAsync(
            GaiaPackets.BuildAuthenticatedAppCommand(runtime.Crypto, payload),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            logger,
            cancellationToken);

        return DecryptAuthenticatedResponse(runtime.Crypto, response, logger, label);
    }

    internal static async Task<int> ReadCurrentProfileAsync(SessionRuntime runtime, IHeadsetTransport transport, SessionLogger logger, CancellationToken cancellationToken) {
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

    internal static async Task<AncState> ReadAncStateAsync(
        SessionRuntime runtime,
        IHeadsetTransport transport,
        SessionLogger logger,
        int profileId,
        string label,
        CancellationToken cancellationToken
    ) {
        var ancStateResponse = await transport.ExchangeAsync(
            GaiaPackets.BuildAuthenticatedAppCommand(runtime.Crypto, BuildGetAncStatePayload(profileId)),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            logger,
            cancellationToken);

        var ancStatePlain = DecryptAuthenticatedResponse(runtime.Crypto, ancStateResponse, logger, $"anc_state_{label}");
        if (ancStatePlain.Length < 2) {
            throw new InvalidOperationException($"ANC state response '{label}' was too short: {ancStatePlain.Length}");
        }

        return new AncState(ancStatePlain[0], ancStatePlain[1]);
    }

    internal static async Task SetAncStateAsync(SessionRuntime runtime, IHeadsetTransport transport, SessionLogger logger, int profileId, AncState state, string label, CancellationToken cancellationToken) {
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

    internal static byte[] BuildGetAncStatePayload(int profileId) => [0x00, 0x49, checked((byte)profileId)];

    internal static byte[] BuildSetAncStatePayload(int profileId, byte primaryRaw, byte secondaryRaw) =>
        [0x00, 0x48, checked((byte)profileId), primaryRaw, secondaryRaw];

    internal static void LogAncState(SessionLogger logger, string prefix, AncState state) {
        logger.WriteLine($"{prefix}.primary_raw={state.PrimaryRaw}");
        logger.WriteLine($"{prefix}.secondary_raw={state.SecondaryRaw}");
        logger.WriteLine($"{prefix}.mode={DescribeAncMode(state)}");
    }

    internal static void LogBatteryStatus(SessionLogger logger, string prefix, BatteryStatus batteryStatus) {
        logger.WriteLine($"{prefix}.voltage_mv={batteryStatus.BatteryVoltageMillivolts}");
        logger.WriteLine($"{prefix}.level_raw={batteryStatus.BatteryLevelRaw}");
        logger.WriteLine($"{prefix}.percent={batteryStatus.BatteryPercentage}");
        logger.WriteLine($"{prefix}.charger_state_raw={batteryStatus.ChargerStateRaw}");
        logger.WriteLine($"{prefix}.charger_voltage_mv={batteryStatus.ChargerVoltageMillivolts}");
        logger.WriteLine($"{prefix}.charger_level_raw={batteryStatus.ChargerLevelRaw}");
        logger.WriteLine($"{prefix}.ntc_voltage_mv={batteryStatus.NtcVoltageMillivolts}");
        logger.WriteLine($"{prefix}.ntc_level_raw={batteryStatus.NtcLevelRaw}");
    }

    internal static string DescribeAncMode(AncState state) {
        return state switch {
            { PrimaryRaw: 0x01, SecondaryRaw: 0x00 } => "ANC",
            { PrimaryRaw: 0x01, SecondaryRaw: 0x01 } => "Passthrough",
            _ => $"Unknown({state.PrimaryRaw:x2},{state.SecondaryRaw:x2})"
        };
    }

    internal static int DecodeUInt16BigEndian(byte[] payload) => payload.Length < 2 ? 0 : (payload[0] << 8) | payload[1];

    internal static string DecodeProfileName(byte[] payload) {
        if (payload.Length == 0) {
            return string.Empty;
        }

        var terminatorIndex = Array.IndexOf(payload, (byte)0x00);
        var length = terminatorIndex >= 0 ? terminatorIndex : payload.Length;
        return System.Text.Encoding.UTF8.GetString(payload, 0, length);
    }

    internal static bool DecodeBoolean(byte[] payload) => payload.Length > 0 && payload[0] != 0;

    internal static bool TryDecodeKickitParams(byte[] payload, out KickitParams kickitParams) {
        if (payload.Length < 3) {
            kickitParams = default;
            return false;
        }

        kickitParams = new KickitParams(payload[0], payload[1], payload[2]);
        return true;
    }

    internal static bool TryDecodeBatteryStatus(byte[] payload, out BatteryStatus batteryStatus) {
        if (payload.Length < 11) {
            batteryStatus = default;
            return false;
        }

        batteryStatus = new BatteryStatus(
            (payload[0] << 8) | payload[1],
            payload[2],
            payload[3],
            payload[4],
            (payload[5] << 8) | payload[6],
            payload[7],
            (payload[8] << 8) | payload[9],
            payload[10]);
        return true;
    }

    internal static bool TryDecodeDeviceInfo(byte[] payload, out DeviceInfo deviceInfo) {
        if (payload.Length != 8) {
            deviceInfo = default;
            return false;
        }

        deviceInfo = new DeviceInfo(ReadInt32BigEndian(payload, 0), ReadInt32BigEndian(payload, 4));
        return true;
    }

    internal static bool TryDecodeExtendedDeviceInfo(byte[] payload, out ExtendedDeviceInfo extendedDeviceInfo) {
        if (payload.Length != 20) {
            extendedDeviceInfo = default;
            return false;
        }

        var serialNumber = ReadInt32BigEndian(payload, 0);
        var firmwareVersion = ReadInt32BigEndian(payload, 4);
        var baseSerial = ReadInt32BigEndian(payload, 8);
        var number = ReadInt32BigEndian(payload, 12);
        var peerSerial = ReadInt32BigEndian(payload, 16);
        var peerRssi = ReadInt16BigEndian(payload, 18);

        extendedDeviceInfo = new ExtendedDeviceInfo(
            serialNumber,
            firmwareVersion,
            baseSerial,
            number,
            peerSerial,
            peerRssi);
        return true;
    }

    internal static string DescribeDeviceType(int serialNumber) {
        return serialNumber switch {
            >= 0 and < 20000000 => "Nuraphone",
            >= 20000000 and < 30000000 => "NuraLoop",
            >= 30000000 and < 50000000 => "NuraTrue",
            >= 50000000 and < 70000000 => "NuraLite",
            _ => "Unknown"
        };
    }

    internal static int GetMaxPacketLengthHint(int serialNumber) {
        return DescribeDeviceType(serialNumber) switch {
            "Nuraphone" => 182,
            "NuraLoop" => 70,
            "NuraTrue" => 70,
            "NuraLite" => 70,
            _ => 182
        };
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

    private static int ReadInt32BigEndian(byte[] payload, int offset) {
        return
            (payload[offset] << 24) |
            (payload[offset + 1] << 16) |
            (payload[offset + 2] << 8) |
            payload[offset + 3];
    }

    private static int ReadUInt16BigEndian(byte[] payload, int offset) {
        return (payload[offset] << 8) | payload[offset + 1];
    }

    private static short ReadInt16BigEndian(byte[] payload, int offset) {
        return unchecked((short)ReadUInt16BigEndian(payload, offset));
    }
}

internal readonly record struct BatteryStatus(
    int BatteryVoltageMillivolts,
    int BatteryLevelRaw,
    int BatteryPercentage,
    int ChargerStateRaw,
    int ChargerVoltageMillivolts,
    int ChargerLevelRaw,
    int NtcVoltageMillivolts,
    int NtcLevelRaw);

internal readonly record struct AncState(
    byte PrimaryRaw,
    byte SecondaryRaw);

internal readonly record struct KickitParams(
    int Drc,
    int Lpf,
    int Gain);

internal readonly record struct DeviceInfo(
    int SerialNumber,
    int FirmwareVersion);

internal readonly record struct ExtendedDeviceInfo(
    int SerialNumber,
    int FirmwareVersion,
    int BaseSerial,
    int Number,
    int PeerSerial,
    int PeerRssi) {
    public bool PeerConnected => PeerRssi > short.MinValue;
    public bool RightIsPrimary => SerialNumber == BaseSerial;
    public bool LeftIsPrimary => !RightIsPrimary;
    public bool RightConnected => RightIsPrimary || (LeftIsPrimary && PeerConnected);
    public bool LeftConnected => LeftIsPrimary || (RightIsPrimary && PeerConnected);
}
