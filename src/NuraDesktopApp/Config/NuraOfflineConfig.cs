using System.Text.Json;

namespace desktop_app.Config;

internal sealed class NuraOfflineConfig {
    public required string DeviceAddress { get; init; }

    public required int SerialNumber { get; init; }

    public int CurrentProfileId { get; init; }

    public required byte[] DeviceKey { get; init; }

    public byte[]? SessionNonce { get; init; }

    public static NuraOfflineConfig Load(string path) {
        if (!File.Exists(path)) {
            throw new FileNotFoundException($"config not found: {path}");
        }

        var json = File.ReadAllText(path);
        var raw = JsonSerializer.Deserialize<RawConfig>(json, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? throw new InvalidOperationException("failed to parse config json");

        return new NuraOfflineConfig {
            DeviceAddress = raw.DeviceAddress ?? throw new InvalidOperationException("deviceAddress is required"),
            SerialNumber = raw.SerialNumber ?? throw new InvalidOperationException("serialNumber is required"),
            CurrentProfileId = raw.CurrentProfileId ?? 0,
            DeviceKey = ParseLength(raw.DeviceKeyHex, 16, "deviceKeyHex"),
            SessionNonce = string.IsNullOrWhiteSpace(raw.SessionNonceHex)
                ? null
                : ParseLength(raw.SessionNonceHex, 12, "sessionNonceHex")
        };
    }

    private static byte[] ParseLength(string? hex, int length, string fieldName) {
        if (string.IsNullOrWhiteSpace(hex)) {
            throw new InvalidOperationException($"{fieldName} is required");
        }

        var bytes = Hex.Parse(hex);
        if (bytes.Length != length) {
            throw new InvalidOperationException($"{fieldName} must be {length} bytes");
        }

        return bytes;
    }

    private sealed class RawConfig {
        public string? DeviceAddress { get; init; }

        public int? SerialNumber { get; init; }

        public int? CurrentProfileId { get; init; }

        public string? DeviceKeyHex { get; init; }

        public string? SessionNonceHex { get; init; }
    }
}
