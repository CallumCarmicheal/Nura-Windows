using NuraDesktopConsole.Library.Nura;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Actions;

internal sealed class ActionProbeDevices : IAction {
    public Task<int> HandleAsync(string[] args, SessionLogger logger) {
        var selectedDevice = ProbeDeviceSelector.Select(logger);
        ProbeDeviceSelector.LogSelected(logger, selectedDevice);
        logger.WriteLine("probe.selected.max_packet_length_hint=182");
        logger.WriteLine("probe.result=success");
        return Task.FromResult(0);
    }
}
