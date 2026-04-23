using System.Buffers.Binary;

using NuraLib.Devices;
using NuraLib.Protocol;
using NuraLib.Transport;

namespace NuraLib.Auth;

internal static class NuraProvisioningSupport {
    private const ushort NuraVendorId = 0x6872;

    private static readonly HashSet<ushort> AuthenticatedResponseCommandIds = [
        0x0006, 0x1006,
        0x000A, 0x100A,
        0x0013, 0x1013,
        0x0008, 0x1008,
        0x000F, 0x100F,
        0x000C, 0x100C
    ];

    private static readonly HashSet<ushort> BulkResponseCommandIds = [
        0x1006, 0x100A, 0x1013,
        0x1007, 0x100B, 0x1014,
        0x1008, 0x100F, 0x100C,
        0x1009, 0x1010, 0x100D
    ];

    public static async Task<List<IReadOnlyDictionary<string, object?>>> ExecuteLocalActionsAsync(
        NuraSessionStartResponseDetails details,
        IHeadsetTransport transport,
        NuraDeviceInfo deviceInfo,
        CancellationToken cancellationToken) {
        var packets = new List<IReadOnlyDictionary<string, object?>>();
        var (gaiaVersion, gaiaFlags) = ResolveBootstrapGaia(deviceInfo.DeviceType);

        foreach (var packet in details.Packets) {
            if (packet.PayloadBytes is not { Length: > 0 }) {
                continue;
            }

            var frame = BuildFrameFromPacketBytes(packet.PayloadBytes, gaiaVersion, gaiaFlags);
            var response = await transport.SendAsync(frame, cancellationToken);
            packets.Add(new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["e"] = false,
                ["a"] = false,
                ["b"] = response.Data,
                ["m"] = false
            });
        }

        foreach (var packet in details.RunPackets) {
            if (packet.PayloadBytes is not { Length: > 0 }) {
                continue;
            }

            // The backend-issued start_3/run packets are server-encrypted GAIA families, not app_enc traffic.
            var rawCommandId = ResolveEntryServerEncryptedCommandId(packet.FlagA, packet.FlagM, appEncryptedResponse: false);
            var frame = GaiaPacketFactory.CreateRawCommand(rawCommandId, packet.PayloadBytes, gaiaVersion, gaiaFlags);
            var response = await transport.SendAsync(frame, cancellationToken);
            packets.Add(new Dictionary<string, object?>(StringComparer.Ordinal) {
                ["e"] = true,
                ["a"] = AuthenticatedResponseCommandIds.Contains(response.CommandId),
                ["b"] = response.PayloadExcludingStatus,
                ["m"] = BulkResponseCommandIds.Contains(response.CommandId)
            });
        }

        return packets;
    }

    private static GaiaFrame BuildFrameFromPacketBytes(byte[] bytes, byte version, byte flags) {
        if (bytes.Length >= 8 && bytes[0] == 0xFF) {
            var parsed = GaiaResponse.Parse(bytes);
            return new GaiaFrame {
                CommandId = (GaiaCommandId)parsed.CommandId,
                Bytes = bytes
            };
        }

        if (bytes.Length < 4) {
            throw new InvalidOperationException("Bootstrap packet must be either a full GAIA frame or vendor+command bytes.");
        }

        var vendorId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0, 2));
        if (vendorId != NuraVendorId) {
            throw new InvalidOperationException($"Unsupported raw vendor 0x{vendorId:x4}; expected 0x{NuraVendorId:x4}.");
        }

        var rawCommandId = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2, 2));
        var payload = bytes.Length == 4 ? Array.Empty<byte>() : bytes[4..];
        return GaiaPacketFactory.CreateRawCommand(rawCommandId, payload, version, flags);
    }

    private static ushort ResolveEntryServerEncryptedCommandId(bool flagA, bool flagM, bool appEncryptedResponse) {
        return (flagA, flagM, appEncryptedResponse) switch {
            (false, false, false) => 0x0009,
            (false, false, true) => 0x0010,
            (false, true, false) => 0x1009,
            (false, true, true) => 0x1010,
            (true, false, false) => 0x0008,
            (true, false, true) => 0x000F,
            (true, true, false) => 0x1008,
            (true, true, true) => 0x100F
        };
    }

    private static (byte Version, byte Flags) ResolveBootstrapGaia(NuraDeviceType deviceType) {
        return deviceType switch {
            NuraDeviceType.NuraTrue => (0x04, 0x00),
            NuraDeviceType.NuraBuds => (0x04, 0x02),
            NuraDeviceType.NuraTruePro => (0x04, 0x02),
            NuraDeviceType.NuraTrueSport => (0x04, 0x02),
            _ => (0x01, 0x00)
        };
    }
}
