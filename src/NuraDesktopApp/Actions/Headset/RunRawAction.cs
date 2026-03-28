using NuraDesktopConsole.Library;
using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Library.Transport;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionRunRaw : IAction {
    public async Task<int> HandleAsync(string[] args, SessionLogger logger) {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(ParseReadTimeoutMs(args)));
        var config = LocalStateFiles.LoadConfig(logger);
        var packetHex = ArgumentReader.RequiredValue(args, "--packet-hex");
        var gaiaVersion = ParseByteArgument(args, "--gaia-version", 0x01);
        var gaiaFlags = ParseByteArgument(args, "--gaia-flags", 0x00);
        var frame = RawPacketSupport.BuildFrameFromPacketHex(packetHex, out var frameMode, gaiaVersion, gaiaFlags);

        await using IHeadsetTransport transport = new RfcommHeadsetTransport();
        logger.WriteLine($"raw.packet.input.hex={packetHex}");
        logger.WriteLine($"raw.mode={frameMode}");
        logger.WriteLine($"raw.gaia_version=0x{gaiaVersion:x2}");
        logger.WriteLine($"raw.gaia_flags=0x{gaiaFlags:x2}");
        logger.WriteLine($"raw.command=0x{((ushort)frame.CommandId):x4}");
        logger.WriteLine($"transport={transport.Describe()}");
        logger.WriteLine("connecting...");
        await transport.ConnectAsync(config.DeviceAddress, cts.Token);
        logger.WriteLine("connected");

        var response = await transport.SendAsync(frame, logger, cts.Token);
        logger.WriteLine($"raw.response.command=0x{response.CommandId:x4}");
        logger.WriteLine($"raw.response.data.hex={Hex.Format(response.Data)}");
        logger.WriteLine($"raw.response.data.base64={Convert.ToBase64String(response.Data)}");
        logger.WriteLine("raw.result=success");
        return 0;
    }

    private static int ParseReadTimeoutMs(string[] args) {
        var raw = ArgumentReader.OptionalValue(args, "--read-timeout-ms");
        return raw is null ? 5000 : int.Parse(raw);
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
}
