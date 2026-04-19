using NuraLib.Crypto;
using NuraLib.Logging;
using NuraLib.Protocol;
using NuraLib.Transport;
using NuraLib.Utilities;

namespace NuraLib.Devices;

internal static class NuraLocalSessionSupport {
    private const string Source = nameof(NuraLocalSessionSupport);

    public static async Task PerformAppHandshakeAsync(
        NuraSessionRuntime runtime,
        IHeadsetTransport transport,
        NuraClientLogger logger,
        CancellationToken cancellationToken) {
        var challengeResponse = await transport.ExchangeAsync(
            NuraQueryFactory.CreateGenerateAppChallenge(),
            GaiaCommandId.CryptoAppGenerateChallenge,
            cancellationToken);

        var challenge = challengeResponse.PayloadExcludingStatus;
        if (challenge.Length != 16) {
            throw new InvalidOperationException($"unexpected challenge length: {challenge.Length}");
        }

        logger.Trace(Source, $"challenge.hex={HexEncoding.Format(challenge)}");
        var gmac = runtime.Crypto.GenerateChallengeResponse(challenge);
        logger.Trace(Source, $"response.gmac.hex={HexEncoding.Format(gmac)}");

        var validateResponse = await transport.ExchangeAsync(
            NuraQueryFactory.CreateValidateAppChallenge(runtime, gmac),
            GaiaCommandId.CryptoAppValidateChallengeResponse,
            cancellationToken);

        var headsetGmac = validateResponse.PayloadExcludingStatus;
        logger.Trace(Source, $"headset.gmac.hex={HexEncoding.Format(headsetGmac)}");
        var success = runtime.Crypto.ValidateResponse(headsetGmac);
        logger.Information(Source, $"handshake.success={success}");
        if (!success) {
            logger.Warning(Source, "Headset GMAC did not match local expectation; continuing because the headset accepted our challenge response.");
        }
    }

    public static async Task<int> ReadCurrentProfileAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        CancellationToken cancellationToken) {
        var profileResponse = await session.ExchangeAsync(
            NuraQueryFactory.CreateGetCurrentProfileId(session.Runtime),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            cancellationToken);

        var profilePlain = NuraResponseParsers.DecryptAuthenticatedPlainPayload(session.Runtime, profileResponse);
        logger.Trace(Source, $"current_profile.hex={HexEncoding.Format(profilePlain)}");
        return NuraResponseParsers.DecodeCurrentProfileId(profilePlain);
    }

    public static async Task<string> ReadProfileNameAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        int profileId,
        CancellationToken cancellationToken) {
        var profileNameResponse = await session.ExchangeAsync(
            NuraQueryFactory.CreateGetProfileName(session.Runtime, profileId),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            cancellationToken);

        var profileNamePlain = NuraResponseParsers.DecryptAuthenticatedPlainPayload(session.Runtime, profileNameResponse);
        logger.Trace(Source, $"profile_name.{profileId}.hex={HexEncoding.Format(profileNamePlain)}");
        return NuraResponseParsers.DecodeProfileName(profileNamePlain);
    }

    public static async Task<NuraAncState> ReadAncStateAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        int profileId,
        CancellationToken cancellationToken) {
        var ancStateResponse = await session.ExchangeAsync(
            NuraQueryFactory.CreateGetAncState(session.Runtime, profileId),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            cancellationToken);

        var ancStatePlain = NuraResponseParsers.DecryptAuthenticatedPlainPayload(session.Runtime, ancStateResponse);
        logger.Trace(Source, $"anc_state.hex={HexEncoding.Format(ancStatePlain)}");
        return NuraResponseParsers.DecodeAncState(ancStatePlain);
    }

    public static async Task SetAncStateAsync(
        ConnectedNuraDeviceSession session,
        NuraClientLogger logger,
        int profileId,
        NuraAncState state,
        CancellationToken cancellationToken) {
        var response = await session.ExchangeAsync(
            NuraQueryFactory.CreateSetAncState(session.Runtime, profileId, state),
            GaiaCommandId.ResponseAppEncryptedAuthenticated,
            cancellationToken);

        var plain = NuraResponseParsers.DecryptAuthenticatedPlainPayload(session.Runtime, response);
        logger.Trace(Source, $"anc_set.ack.hex={HexEncoding.Format(plain)}");
    }
}
