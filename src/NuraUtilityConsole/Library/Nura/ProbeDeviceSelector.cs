using NuraDesktopConsole.Library.Transport;
using NuraDesktopConsole.Logging;

namespace NuraDesktopConsole.Library.Nura;

internal static class ProbeDeviceSelector {
    internal static BluetoothDeviceProbe.ProbedBluetoothDevice Select(SessionLogger logger) {
        var devices = BluetoothDeviceProbe.FindConnectedNuraphones();
        logger.WriteLine($"probe.devices.count={devices.Count}");

        if (devices.Count == 0) {
            logger.WriteLine("probe.result=no_connected_nuraphone_found");
            throw new InvalidOperationException("no connected Nuraphone device found");
        }

        for (var i = 0; i < devices.Count; i++) {
            var device = devices[i];
            logger.WriteLine($"probe.device.{i + 1}.name={device.Name}");
            logger.WriteLine($"probe.device.{i + 1}.address={device.Address}");
            logger.WriteLine($"probe.device.{i + 1}.connected={device.Connected}");
            logger.WriteLine($"probe.device.{i + 1}.authenticated={device.Authenticated}");
            logger.WriteLine($"probe.device.{i + 1}.remembered={device.Remembered}");
        }

        return devices.Count == 1
            ? devices[0]
            : PromptForDeviceSelection(devices, logger);
    }

    internal static void LogSelected(SessionLogger logger, BluetoothDeviceProbe.ProbedBluetoothDevice device) {
        logger.WriteLine($"probe.selected.name={device.Name}");
        logger.WriteLine($"probe.selected.address={device.Address}");
    }

    private static BluetoothDeviceProbe.ProbedBluetoothDevice PromptForDeviceSelection(
        IReadOnlyList<BluetoothDeviceProbe.ProbedBluetoothDevice> devices,
        SessionLogger logger) {
        while (true) {
            logger.WriteLine($"probe.selection.prompt=Multiple Nuraphone devices found. Enter 1-{devices.Count} to select one.");
            Console.Write($"Select Nuraphone device [1-{devices.Count}]: ");

            var input = Console.ReadLine();
            logger.WriteLine($"probe.selection.input={input ?? string.Empty}");

            if (int.TryParse(input, out var selectedIndex) &&
                selectedIndex >= 1 &&
                selectedIndex <= devices.Count) {
                return devices[selectedIndex - 1];
            }

            logger.WriteLine("probe.selection.invalid=true");
        }
    }
}
