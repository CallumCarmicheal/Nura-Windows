using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Protocol;
using NuraDesktopConsole.Library.Transport;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionProbeHardwareInfo : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var selectedDevice = ProbeDeviceSelector.Select(logger);

        await using IHeadsetTransport transport = new RfcommHeadsetTransport();
        ProbeDeviceSelector.LogSelected(logger, selectedDevice);
        logger.WriteLine($"transport={transport.Describe()}");
        logger.WriteLine("connecting...");
        await transport.ConnectAsync(selectedDevice.Address, cts.Token);
        logger.WriteLine("connected");

        var deviceInfoResponse = await transport.ExchangeAsync(
            GaiaPackets.BuildCommand(GaiaCommandId.GetDeviceInfo),
            GaiaCommandId.GetDeviceInfo,
            logger,
            cts.Token);
        var deviceInfoPayload = deviceInfoResponse.PayloadExcludingStatus;
        logger.WriteLine($"probe.hw_info.device_info.payload.hex={Hex.Format(deviceInfoPayload)}");
        if (HeadsetSupport.TryDecodeDeviceInfo(deviceInfoPayload, out var deviceInfo)) {
            logger.WriteLine($"probe.hw_info.serial={deviceInfo.SerialNumber}");
            logger.WriteLine($"probe.hw_info.firmware_version={deviceInfo.FirmwareVersion}");
            logger.WriteLine($"probe.hw_info.device_type={HeadsetSupport.DescribeDeviceType(deviceInfo.SerialNumber)}");
            logger.WriteLine($"probe.hw_info.max_packet_length_hint={HeadsetSupport.GetMaxPacketLengthHint(deviceInfo.SerialNumber)}");
        } else {
            logger.WriteLine("probe.hw_info.device_info.decode_failed=true");
        }

        var extendedInfoResponse = await transport.ExchangeAsync(
            GaiaPackets.BuildCommand(GaiaCommandId.GetExtendedDeviceInfo),
            GaiaCommandId.GetExtendedDeviceInfo,
            logger,
            cts.Token);
        var extendedInfoPayload = extendedInfoResponse.PayloadExcludingStatus;
        logger.WriteLine($"probe.hw_info.extended.payload.hex={Hex.Format(extendedInfoPayload)}");
        if (HeadsetSupport.TryDecodeExtendedDeviceInfo(extendedInfoPayload, out var extendedInfo)) {
            logger.WriteLine($"probe.hw_info.extended.serial={extendedInfo.SerialNumber}");
            logger.WriteLine($"probe.hw_info.extended.firmware_version={extendedInfo.FirmwareVersion}");
            logger.WriteLine($"probe.hw_info.extended.base_serial={extendedInfo.BaseSerial}");
            logger.WriteLine($"probe.hw_info.extended.number={extendedInfo.Number}");
            logger.WriteLine($"probe.hw_info.extended.peer_serial={extendedInfo.PeerSerial}");
            logger.WriteLine($"probe.hw_info.extended.peer_rssi={extendedInfo.PeerRssi}");
            logger.WriteLine($"probe.hw_info.extended.peer_connected={extendedInfo.PeerConnected}");
            logger.WriteLine($"probe.hw_info.extended.right_is_primary={extendedInfo.RightIsPrimary}");
            logger.WriteLine($"probe.hw_info.extended.left_is_primary={extendedInfo.LeftIsPrimary}");
            logger.WriteLine($"probe.hw_info.extended.right_connected={extendedInfo.RightConnected}");
            logger.WriteLine($"probe.hw_info.extended.left_connected={extendedInfo.LeftConnected}");
        } else {
            logger.WriteLine("probe.hw_info.extended.decode_failed=true");
        }

        logger.WriteLine("probe.hw_info.result=success");
        return 0;
    }
}
