using System.Runtime.InteropServices;

using NuraLib.Logging;
using NuraLib.Protocol;

namespace NuraLib.Transport;

internal static class BluetoothDeviceProbe {
    private const string Source = nameof(BluetoothDeviceProbe);

    public static IReadOnlyList<ProbedBluetoothDevice> FindConnectedNuraDevices() {
        var results = new Dictionary<string, ProbedBluetoothDevice>(StringComparer.OrdinalIgnoreCase);

        var radioParams = new BluetoothFindRadioParams {
            Size = Marshal.SizeOf<BluetoothFindRadioParams>()
        };

        var radioFindHandle = NativeMethods.BluetoothFindFirstRadio(ref radioParams, out var radioHandle);
        if (radioFindHandle == IntPtr.Zero) {
            return [];
        }

        try {
            var keepGoing = true;
            while (keepGoing) {
                try {
                    EnumerateDevicesForRadio(radioHandle, results);
                } finally {
                    if (radioHandle != IntPtr.Zero) {
                        NativeMethods.CloseHandle(radioHandle);
                    }
                }

                keepGoing = NativeMethods.BluetoothFindNextRadio(radioFindHandle, out radioHandle);
            }
        } finally {
            NativeMethods.BluetoothFindRadioClose(radioFindHandle);
        }

        return results.Values
            .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.Address, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static async Task<ProbedDeviceDetails?> ProbeDeviceInfoAsync(
        ProbedBluetoothDevice device,
        NuraClientLogger logger,
        CancellationToken cancellationToken) {
        await using IHeadsetTransport transport = new RfcommHeadsetTransport(logger);
        try {
            await transport.ConnectAsync(device.Address, cancellationToken);

            var deviceInfoResponse = await transport.ExchangeAsync(
                NuraQueryFactory.CreateGetDeviceInfo(),
                GaiaCommandId.GetDeviceInfo,
                cancellationToken);

            var deviceInfoPayload = deviceInfoResponse.PayloadExcludingStatus;
            if (!NuraResponseParsers.TryDecodeDeviceInfo(deviceInfoPayload, out var deviceInfo)) {
                logger.Warning(Source, $"Failed to decode basic device info for {device.Address}.");
                return null;
            }

            ExtendedDeviceInfo? extendedInfo = null;
            try {
                var extendedInfoResponse = await transport.ExchangeAsync(
                    NuraQueryFactory.CreateGetExtendedDeviceInfo(),
                    GaiaCommandId.GetExtendedDeviceInfo,
                    cancellationToken);
                var extendedInfoPayload = extendedInfoResponse.PayloadExcludingStatus;
                if (NuraResponseParsers.TryDecodeExtendedDeviceInfo(extendedInfoPayload, out var parsed)) {
                    extendedInfo = parsed;
                }
            } catch (Exception ex) {
                logger.Debug(Source, $"Extended device info probe failed for {device.Address}: {ex.Message}");
            }

            return new ProbedDeviceDetails(device, deviceInfo, extendedInfo);
        } catch (Exception ex) {
            logger.Warning(Source, $"Device probe failed for {device.Address}: {ex.Message}");
            return null;
        }
    }

    private static void EnumerateDevicesForRadio(
        IntPtr radioHandle,
        Dictionary<string, ProbedBluetoothDevice> results) {
        var searchParams = new BluetoothDeviceSearchParams {
            Size = Marshal.SizeOf<BluetoothDeviceSearchParams>(),
            ReturnAuthenticated = true,
            ReturnRemembered = true,
            ReturnUnknown = false,
            ReturnConnected = true,
            IssueInquiry = false,
            TimeoutMultiplier = 0,
            RadioHandle = radioHandle
        };

        var deviceInfo = BluetoothDeviceInfo.Create();
        var deviceFindHandle = NativeMethods.BluetoothFindFirstDevice(ref searchParams, ref deviceInfo);
        if (deviceFindHandle == IntPtr.Zero) {
            return;
        }

        try {
            var keepGoing = true;
            while (keepGoing) {
                if (deviceInfo.Connected &&
                    !string.IsNullOrWhiteSpace(deviceInfo.Name)) {
                    var trimmedName = deviceInfo.Name.TrimEnd('\0');
                    if (trimmedName.StartsWith("Nura", StringComparison.OrdinalIgnoreCase) ||
                        trimmedName.StartsWith("Nuraphone", StringComparison.OrdinalIgnoreCase)) {
                        var address = BluetoothAddress.Format(deviceInfo.Address);
                        results[address] = new ProbedBluetoothDevice(
                            trimmedName,
                            address,
                            deviceInfo.Connected,
                            deviceInfo.Authenticated,
                            deviceInfo.Remembered);
                    }
                }

                deviceInfo = BluetoothDeviceInfo.Create();
                keepGoing = NativeMethods.BluetoothFindNextDevice(deviceFindHandle, ref deviceInfo);
            }
        } finally {
            NativeMethods.BluetoothFindDeviceClose(deviceFindHandle);
        }
    }

    internal sealed record ProbedBluetoothDevice(
        string Name,
        string Address,
        bool Connected,
        bool Authenticated,
        bool Remembered);

    internal sealed record ProbedDeviceDetails(
        ProbedBluetoothDevice Device,
        DeviceInfo DeviceInfo,
        ExtendedDeviceInfo? ExtendedInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothFindRadioParams {
        public int Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BluetoothDeviceSearchParams {
        public int Size;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)] public bool ReturnConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool IssueInquiry;
        public byte TimeoutMultiplier;
        public IntPtr RadioHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BluetoothDeviceInfo {
        public int Size;
        public ulong Address;
        public uint ClassOfDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool Connected;
        [MarshalAs(UnmanagedType.Bool)] public bool Remembered;
        [MarshalAs(UnmanagedType.Bool)] public bool Authenticated;
        public SystemTime LastSeen;
        public SystemTime LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;

        public static BluetoothDeviceInfo Create() {
            return new BluetoothDeviceInfo {
                Size = Marshal.SizeOf<BluetoothDeviceInfo>(),
                Name = string.Empty
            };
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime {
        public ushort Year;
        public ushort Month;
        public ushort DayOfWeek;
        public ushort Day;
        public ushort Hour;
        public ushort Minute;
        public ushort Second;
        public ushort Milliseconds;
    }

    private static class NativeMethods {
        [DllImport("bthprops.cpl", SetLastError = true)]
        public static extern IntPtr BluetoothFindFirstRadio(ref BluetoothFindRadioParams searchParams, out IntPtr radioHandle);

        [DllImport("bthprops.cpl", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothFindNextRadio(IntPtr findHandle, out IntPtr radioHandle);

        [DllImport("bthprops.cpl", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothFindRadioClose(IntPtr findHandle);

        [DllImport("bthprops.cpl", SetLastError = true)]
        public static extern IntPtr BluetoothFindFirstDevice(ref BluetoothDeviceSearchParams searchParams, ref BluetoothDeviceInfo deviceInfo);

        [DllImport("bthprops.cpl", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothFindNextDevice(IntPtr findHandle, ref BluetoothDeviceInfo deviceInfo);

        [DllImport("bthprops.cpl", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothFindDeviceClose(IntPtr findHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
