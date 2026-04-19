using NuraLib.Devices;

namespace NuraLib.Protocol;

internal static class HeadsetIndicationParser {
    public static HeadsetIndication? Parse(GaiaResponse response) {
        if ((GaiaCommandId)response.CommandId != GaiaCommandId.IndicationFromHeadset) {
            return null;
        }

        var payload = response.PayloadExcludingStatus;
        if (payload.Length != 2 || !Enum.IsDefined(typeof(HeadsetIndicationIdentifier), payload[0])) {
            return null;
        }

        return new HeadsetIndication((HeadsetIndicationIdentifier)payload[0], payload[1]);
    }

    public static NuraAncState DecodeNuraphoneAncState(byte value) {
        var ancEnabled = (value & 1) != 0;
        var passthroughEnabled = (value & 2) != 0;

        return new NuraAncState {
            AncEnabled = ancEnabled,
            PassthroughEnabled = passthroughEnabled
        };
    }
}
