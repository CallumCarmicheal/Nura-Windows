using NuraDesktopConsole.Library.Protocol;

namespace NuraDesktopConsole.Library.Nura;

internal static class ProtocolSupport {
    internal static string DescribeFrame(GaiaFrame frame) => $"{frame.CommandId} => {Hex.Format(frame.Bytes)}";
}
