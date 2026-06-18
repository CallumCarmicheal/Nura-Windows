using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Win32;

using NuraApp;

using NuraLib;
using NuraLib.Configuration;
using NuraLib.Devices;
using NuraLib.Logging;

static class Program {
    private static readonly CancellationTokenSource AppCts = new();
    public static readonly AsyncConsoleLogger logger = new();

    private static NuraClient? Client;
    private static NuraDevice? SelectedDevice = null;

    static async Task Main(string[] args) {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        Console.CancelKeyPress += (_, e) => {
            e.Cancel = true;
            AppCts.Cancel();
        };

        try {
            logger.WriteLine("Application starting...");
            await logger.SetHoistedSectionAndWaitAsync("status", "Loading...");

            // Run your app here.
            await RunAsync().ConfigureAwait(false);

            // Wait until the app is closing.
            try { await Task.Delay(Timeout.Infinite, AppCts.Token); } catch (OperationCanceledException) { }

            logger.WriteLine("Application exiting...");
        } finally {
            await ShutdownClientAsync().ConfigureAwait(false);

            // Do not use AppCts.Token here, because it may already be cancelled.
            await logger.WriteLineAndWaitAsync("Console logger is shutting down.");

            // This completes the queue and waits for all pending messages.
            await logger.DisposeAsync();
        }
    }


    private static async Task RunAsync() {
        var configPath = Path.Combine(
            Environment.CurrentDirectory,
            "nura-config.json");

        var config = NuraConfigStore.LoadOrCreate(configPath);
        var state = new NuraConfigState(config);
        var client = Client = new NuraClient(state);

        client.RequestStateSave += (_, args) => {
            NuraConfigStore.Save(configPath, client.State.Configuration);
        };

        client.OnLog += (_, args) => {
            if (args.Message != "Frame collection stopped after idle timeout.") {
                if ((int)args.Level > (int)NuraLogLevel.Debug) {
                    logger.WriteLine(
                        AnsiPart.Dim($"[{args.TimestampUtc.ToLocalTime():HH:mm:ss}] "),
                        AnsiPart.FgHex("[NuraLib] ", 0x8B5CF6),
                        AnsiPart.FgHex($"{args.Level} ", GetLogLevelColour(args.Level)),
                        $"{args.Source}: {args.Message}");
                } else {
                    // Make it look darker / dimmer because its trace / debug.
                    logger.WriteLine(
                        AnsiPart.Dim($"[{args.TimestampUtc.ToLocalTime():HH:mm:ss}] [NuraLib] {args.Level} {args.Source}: {args.Message}"));
                }
            }
        };

        logger.SetHoistedSection("status", "Checking stored credentials.");

        // Check if we are authenticated.
        var authenticated = client.Auth.HasStoredCredentials && (await client.Auth.HasValidSessionAsync().ConfigureAwait(false));

        if (authenticated) {
            try {
                logger.SetHoistedSection("status", "Attempting to resume existing authentication state");

                await client.Auth.ResumeAsync().ConfigureAwait(false);
                authenticated = await client.Auth.HasValidSessionAsync().ConfigureAwait(false);
            } catch (Exception ex) {
                authenticated = false;

                logger.WriteLine(
                    AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                    AnsiPart.Error("[NuraLib] "),
                    $"Failed to resume previous authentication: {ex.Message}");
            }
        }

        if (!authenticated) {
            logger.SetHoistedSection("status", "No existing session or it is now invalid, authenticating...");
            authenticated = await AttemptLogin(client).ConfigureAwait(false);
        }

        if (!authenticated) {
            logger.SetHoistedSection("status", "Not Authenticated with Nura.");
            logger.WriteLine(
                AnsiPart.Warning("Not authenticated. "),
                "We cannot provision devices if they are required.");
        } else {
            logger.WriteLine(
                AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                AnsiPart.Success("[Auth] "),
                NuraGradient.Text("✓ Authenticated with nura."));
            
            logger.SetHoistedSection("status", AnsiPart.Success("✓ Authenticated with nura."));
        }

        logger.SetHoistedSection("current device:status", "");
        logger.SetHoistedSection("current device", "");
        logger.SetHoistedSection("status", "Searching for devices...");
        logger.MoveHoistedSectionToBottom("current device");
        logger.MoveHoistedSectionToBottom("status");

        client.Monitoring.DeviceConnected += async (_, args) => {
            var device = args.Device;

            try {
                FlashDevicesHoistText(AnsiLine.From("Found new device, retrieving data for ", NuraGradient.Text(device.Info.DisplayName), "."));

                if (SelectedDevice == null) {
                    SelectedDevice = device;
                    logger.SetHoistedSection("current device", AnsiLine.From("[Keys ←/→ ?] | "
                        , NuraGradient.Text(device.Info.DisplayName)
                        , " | Setting up events and hooks."));
                }

                logger.WriteLine(
                    AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                    AnsiPart.FgHex("[NuraLib] ", 0x8B5CF6),
                    $"Device connected: {device.Info.DisplayName}.");

                device.OperationStatusChanged += (_, op) => {
                    logger.WriteLine(
                        AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                        AnsiPart.FgHex("[NuraDevice] ", 0x06B6D4),
                        $"{device.Info.DisplayName}: {op?.Current?.Kind.ToString() ?? "NullType"} - {op?.Current?.Message ?? "NoMsg"}.");

                    FlashSelectedDeviceStatusText(AnsiLine.From(NuraGradient.Text(device.Info.DisplayName), $" => {op?.Current?.Kind.ToString() ?? "NullType"} - {op?.Current?.Message ?? "NoMsg"}."));
                };

                device.HeadsetIndicationReceived += (_, data) => {
                    // We received data.
                    logger.WriteLine(
                        AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                        AnsiPart.FgHex("[NuraDevice] ", 0x06B6D4),
                        $"{device.Info.DisplayName}: {data.Identifier.ToString()} : {data.Value:X2}.");
                };

                device.State.AncChanged += (_, data) => {
                    // We received data.
                    logger.WriteLine(
                        AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                        AnsiPart.FgHex("[NuraDevice] ", 0x06B6D4),
                        $"{device.Info.DisplayName}: AncEnabledChanged : {data.Current?.ToString() ?? "<null>"}.");
                };

                device.State.AncEnabledChanged += (_, data) => {
                    // We received data.
                    logger.WriteLine(
                        AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                        AnsiPart.FgHex("[NuraDevice] ", 0x06B6D4),
                        $"{device.Info.DisplayName}: AncEnabledChanged : {data.Current?.ToString() ?? "<null>"}.");
                };

                device.Changed += (_, data) => {
                    // We received data.
                    //logger.WriteLine(
                    //	AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                    //	AnsiPart.FgHex("[NuraDevice] ", 0x06B6D4),
                    //	$"{device.Info.DisplayName}: Device state changed.");
                };

                // If the device is not provisioned, we can attempt to provision it.
                if (device.ProvisioningRequired) {
                    logger.WriteLine(
                        AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                        AnsiPart.FgHex("[NuraDevice] ", 0x06B6D4),
                        $"{device.Info.DisplayName}: Device required provisioning - {device.ProvisioningRequirementReason}.");

                    if (SelectedDevice == device) {
                        logger.SetHoistedSection("current device", AnsiLine.From("[Keys ←/→ ?] | "
                            , NuraGradient.Text(device.Info.DisplayName)
                            , $" | Device required provisioning - {device.ProvisioningRequirementReason}."));
                    }

                    if (authenticated) {
                        logger.WriteLine(
                            AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                            AnsiPart.FgHex("[NuraDevice] ", 0x06B6D4),
                            $"{device.Info.DisplayName}: Authenticated with Nura, requesting provision to get keys.");

                        var provisioningResult = await device.EnsureProvisionedAsync();
                        if (!provisioningResult.Success) {
                            logger.WriteLine(
                                AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                                AnsiPart.Error("[NuraLib] ERROR : "),
                                $"{device.Info.DisplayName}: Provisioning failed - {provisioningResult.Error}.");
                        }
                    } else {
                        logger.WriteLine(
                            AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                            AnsiPart.Error("[NuraLib] ERROR : "),
                            $"We are unable to use this device ({device.Info.DisplayName}) without first logging into Nura to request its encryption keys.");

                        logger.SetHoistedSection("current device", AnsiLine.From("[Keys ←/→ ?] | "
                            , NuraGradient.Text(device.Info.DisplayName)
                            , $" | Unable to talk to device, please login - {device.ProvisioningRequirementReason}."));
                    }
                }

                // Start listening for changes if we have the encryption key.
                if (device.HasPersistentDeviceKey) {
                    logger.WriteLine(
                        AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                        AnsiPart.FgHex("[NuraDevice] ", 0x06B6D4),
                        $"{device.Info.DisplayName}: Device key exists, listening for device changes.");

                    // Show the user the device key.
                    logger.WriteLine(
                        AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                        AnsiPart.FgHex("[NuraDevice] ", 0x06B6D4),
                        $"{device.Info.DisplayName}: Device key = ",
                        AnsiPart.Fg(device.DeviceConfig.GetDeviceKeyHex(), 0, 255, 0),
                        ".");

                    await device.RefreshAsync();          // populates initial cached values
                    await device.StartMonitoringAsync();  // keeps them updated from indications

                    if (SelectedDevice == device) {
                        await Task.Delay(1000);
                        UpdateSelectedDeviceText();
                    }
                } else {
                    logger.WriteLine(
                        AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                        AnsiPart.Warning("[NuraDevice] "),
                        $"{device.Info.DisplayName}: Device key does not exist. Unable to communicate with device.");
                }

                FlashDevicesHoistText(AnsiLine.From("Finished initializing ", NuraGradient.Text(device.Info.DisplayName), "."));
            } catch (Exception ex) {
                logger.WriteLine(
                    AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                    AnsiPart.Error("[NuraLib] ERROR : "),
                    $"Device connection handler failed: {ex.Message}");

                FlashDevicesHoistText(AnsiLine.From(AnsiPart.Error("Failed to setup connection for "), NuraGradient.Text(device.Info.DisplayName), "."));
            }
        };

        client.Monitoring.DeviceDisconnected += async (_, args) => {
            var device = args.Device;

            try {
                if (SelectedDevice == device) {
                    SelectedDevice = null;
                    UpdateSelectedDeviceText();
                }

                var gradient = NuraGradient.Text(device.Info.DisplayName);

                logger.WriteLine(
                        AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                        "[NuraDevice] ", gradient, ": Disconnected.");

                FlashDevicesHoistText(AnsiLine.From("Device disconnected : ", gradient));

                await device.StopMonitoringAsync().ConfigureAwait(false);
            } catch (Exception ex) {
                logger.WriteLine(
                    AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                    AnsiPart.Error("[NuraLib] ERROR : "),
                    $"Device disconnect handler failed ({device.Info.DisplayName}): {ex.Message}.");
            }
        };

        await client.Monitoring.StartAsync();
    }

    private static async Task<bool> AttemptLogin(NuraClient client) {
        if (!await PromptYesNo("You are not authenticated with Nura, do you want to login? [Y/n] ", defaultYes: true))
            return await client.Auth.HasValidSessionAsync();

        client.Auth.ClearStoredSession();

        var emailValidation = new System.ComponentModel.DataAnnotations.EmailAddressAttribute();

        while (true) {
            var email = (await logger.PromptLineAsync("Enter email address (q = Cancel): "))?.Trim();

            if (string.Equals(email, "q", StringComparison.OrdinalIgnoreCase))
                break;

            if (string.IsNullOrWhiteSpace(email)) {
                logger.WriteLine(AnsiPart.Warning("Email address cannot be empty."));
                continue;
            }

            if (!emailValidation.IsValid(email)) {
                logger.WriteLine(AnsiPart.Warning("Invalid email address."));
                continue;
            }

            while (true) {
                if (!await PromptYesNo("Send code to email for login? [Y/n] ", defaultYes: true))
                    break;

                try {
                    await client.Auth.RequestEmailCodeAsync(email);

                    logger.SetHoistedSection("status", "Sent 6 digit code to email, waiting for input.");

                    logger.WriteLine(
                        AnsiPart.Success("Login code sent. "),
                        "It may take up to a minute to arrive.");
                } catch (Exception ex) {
                    logger.WriteLine(
                        AnsiPart.Error("Failed to request login code: "),
                        ex.Message);

                    break;
                }

                while (true) {
                    var stringDigitCode = (await logger.PromptLineAsync("Enter 6 digit code (q = Cancel): "))
                        ?.Trim()
                        ?.Replace(" ", "");

                    if (string.Equals(stringDigitCode, "q", StringComparison.OrdinalIgnoreCase))
                        break;

                    if (string.IsNullOrWhiteSpace(stringDigitCode)) {
                        logger.WriteLine(AnsiPart.Warning("Code cannot be empty."));
                        continue;
                    }

                    if (stringDigitCode.Length != 6 || !stringDigitCode.All(char.IsDigit)) {
                        logger.WriteLine(AnsiPart.Warning("Code must be exactly 6 digits."));
                        continue;
                    }

                    try {
                        await client.Auth.VerifyEmailCodeAsync(stringDigitCode, email);
                    } catch (Exception ex) {
                        logger.WriteLine(
                            AnsiPart.Error("Failed to verify login code: "),
                            ex.Message);

                        if (await PromptYesNo("Try another code? [Y/n] ", defaultYes: true))
                            continue;

                        break;
                    }

                    var authenticated = await client.Auth.HasValidSessionAsync();

                    if (authenticated) {
                        logger.SetHoistedSection("status", "Successfully logged in.");
                        logger.WriteLine(AnsiPart.Success("Authenticated successfully."));
                        return true;
                    }

                    logger.WriteLine(AnsiPart.Warning("The code was accepted, but a valid session was not created."));
                    logger.SetHoistedSection("status", "Successfully logged in, but had an issue creating a valid session.");

                    if (!await PromptYesNo("Try again? [Y/n] ", defaultYes: true))
                        break;
                }

                if (await client.Auth.HasValidSessionAsync())
                    return true;

                if (!await PromptYesNo("Request a new code? [Y/n] ", defaultYes: true))
                    break;
            }
        }

        return await client.Auth.HasValidSessionAsync();
    }

    private static async Task ShutdownClientAsync() {
        var client = Client;
        if (client is null) {
            return;
        }

        try {
            await client.Monitoring.StopAsync().ConfigureAwait(false);
        } catch (Exception ex) {
            logger.WriteLine(
                AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                AnsiPart.Warning("[NuraLib] "),
                $"Failed to stop connection monitoring cleanly: {ex.Message}");
        }

        var liveDevices = client.Devices.All
            .OfType<ConnectedNuraDevice>()
            .Where(device => device.IsMonitoring || device.HasLocalSession)
            .ToArray();

        foreach (var device in liveDevices) {
            try {
                await device.StopMonitoringAsync().ConfigureAwait(false);
            } catch (Exception ex) {
                logger.WriteLine(
                    AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                    AnsiPart.Warning("[NuraDevice] "),
                    $"{device.Info.DisplayName}: Failed to stop monitoring cleanly: {ex.Message}");
            }
        }
    }

    private static readonly AsyncDebouncer devicesStatusFlashDebouncer = new(TimeSpan.FromSeconds(4));
    private static void FlashDevicesHoistText(params AnsiPart[] parts) => FlashDevicesHoistText(AnsiLine.From(parts));
    private static void FlashDevicesHoistText(AnsiLine line) {
        logger.SetHoistedSection("status", line);
        _ = devicesStatusFlashDebouncer.DebounceAsync(() => UpdateDevicesHoistText());
    }

    private static void UpdateDevicesHoistText() {
        AnsiPart[] status;

        var devices = (Client?.Devices.Connected.Count ?? 0);
        if (devices > 0) {
            status = NuraGradient.Text(devices + " nura devices", false, NuraGradient.Gradient.HearingProfileBlueToCyan);
        } else {
            status = [ "0 devices" ];
        }

        logger.SetHoistedSection("status", AnsiLine.From($"Discovered ", status, ", listening for changes."));
    }

    private static readonly AsyncDebouncer selectedDeviceTextStatusFlashDebouncer = new(TimeSpan.FromSeconds(4));
    private static void FlashSelectedDeviceStatusText(params AnsiPart[] parts) => FlashSelectedDeviceStatusText(AnsiLine.From(parts));
    private static void FlashSelectedDeviceStatusText(AnsiLine line) {
        logger.SetHoistedSection("current device:status", line);
        _ = selectedDeviceTextStatusFlashDebouncer.DebounceAsync(() => logger.SetHoistedSection("current device:status", "") );
    }

    private static readonly AsyncDebouncer selectedDeviceTextFlashDebouncer = new(TimeSpan.FromSeconds(4));
    private static void FlashSelectedDeviceText(params AnsiPart[] parts) => FlashSelectedDeviceText(AnsiLine.From(parts));
    private static void FlashSelectedDeviceText(AnsiLine line) {
        logger.SetHoistedSection("current device", line);
        _  = selectedDeviceTextFlashDebouncer.DebounceAsync(() => UpdateSelectedDeviceText());
    }

    private static void UpdateSelectedDeviceText() {
        if (SelectedDevice is null) {
            logger.SetHoistedSection(
                "current device",
                AnsiLine.From("[←/→ select · ? help]  ", AnsiPart.Dim("<no device selected>")));
            return;
        }

        var device = SelectedDevice;
        var segments = new List<object> {
            AnsiPart.Dim("[←/→ select · ? help]  "),
            NuraGradient.Text(device.Info.DisplayName),
            " | "
        };

        if (device is ConnectedNuraDevice live) {
            if (live.ProvisioningRequired == true) {
                segments.Add("  ");
                segments.Add(AnsiPart.Warning($"provision:{FormatProvisionReason(live.ProvisioningRequirementReason)}"));
            } else if (!live.HasPersistentDeviceKey) {
                segments.Add("  ");
                segments.Add(AnsiPart.Warning("key:missing"));
            } else {
                AddValue(segments, "ANC", FormatBool(live.State.AncEnabled));
                AddValue(segments, "pass", FormatBool(live.State.PassthroughEnabled));
                AddValue(segments, "imm", FormatImmersion(live.State.ImmersionLevel));
            }
        } else {
            segments.Add(AnsiPart.Error("ERROR - Not ConnectedNuraDevice"));
        }

        logger.SetHoistedSection("current device", AnsiLine.From(segments.ToArray()));
    }

    private static Task<bool> PromptYesNo(string prompt, bool defaultYes) {
        return logger.PromptYesNoAsync(prompt, defaultYes);
    }

    private static int GetLogLevelColour(NuraLogLevel level) {
        return level switch {
            NuraLogLevel.Trace => 0x9CA3AF,
            NuraLogLevel.Debug => 0xA1A1AA,
            NuraLogLevel.Information => 0x60A5FA,
            NuraLogLevel.Warning => 0xFBBF24,
            NuraLogLevel.Error => 0xF87171,
            //NuraLogLevel.Critical => 0xEF4444,
            _ => 0xE5E7EB
        };
    }

    static void AddValue(List<object> segments, string label, string? value) {
        if (string.IsNullOrWhiteSpace(value))
            return;

        segments.Add("  ");
        segments.Add(AnsiPart.Dim($"{label}:"));
        segments.Add(value);
    }

    static string? FormatBool(bool? value) =>
        value switch {
            true => "on",
            false => "off",
            null => null
        };

    static string? FormatImmersion(NuraImmersionLevel? level) =>
        level?.ToString()
            .Replace("Positive", "+", StringComparison.Ordinal)
            .Replace("Negative", "-", StringComparison.Ordinal)
            .Replace("Neutral", "0", StringComparison.Ordinal);

    static string FormatProvisionReason(NuraProvisioningRequirementReason reason) =>
        reason switch {
            NuraProvisioningRequirementReason.MissingDeviceKey => "missing-key",
            NuraProvisioningRequirementReason.NuraNowRefreshRequired => "nuranow-refresh",
            _ => "required"
        };

    public static void Exit() => AppCts.Cancel();
    public static async Task ExitAsync() => await AppCts.CancelAsync();
}

public static class NuraGradient {
    public enum Gradient {
        PurpleBranding = 0,

        HearingProfileAll,
        HearingProfileOrangeToRed,
        HearingProfileRedToPink,
        HearingProfilePinkToPurple,
        HearingProfilePurpleToBlue,
        HearingProfileBlueToCyan,
        HearingProfileCyanToMint
    }

    private static readonly Rgb Start = Rgb.FromHex(0x804DC4);
    private static readonly Rgb Middle = Rgb.FromHex(0xE35CA9);
    private static readonly Rgb End = Rgb.FromHex(0xE07168);

    private static readonly GradientStop[] CurrentGradientStops = {
        new(0.00, Start),
        new(0.50, Middle),
        new(1.00, End),
    };

    private static readonly GradientStop[] HearingProfileAllGradientStops = {
        new(0.00, RgbFromRgb(240, 127, 87)),
        new(0.12, RgbFromRgb(236, 28, 36)),
        new(0.28, RgbFromRgb(238, 88, 149)),
        new(0.41, RgbFromRgb(160, 76, 156)),
        new(0.60, RgbFromRgb(61, 83, 163)),
        new(0.80, RgbFromRgb(8, 152, 205)),
        new(1.00, RgbFromRgb(141, 209, 199)),
    };

    private static readonly GradientStop[] HearingProfileOrangeToRed = {
        new(0.00, RgbFromRgb(240, 127, 87)),
        new(1.00, RgbFromRgb(236, 28, 36)),
    };

    private static readonly GradientStop[] HearingProfileRedToPink = {
        new(0.00, RgbFromRgb(236, 28, 36)),
        new(1.00, RgbFromRgb(238, 88, 149)),
    };

    private static readonly GradientStop[] HearingProfilePinkToPurple = {
        new(0.00, RgbFromRgb(238, 88, 149)),
        new(1.00, RgbFromRgb(160, 76, 156)),
    };

    private static readonly GradientStop[] HearingProfilePurpleToBlue = {
        new(0.00, RgbFromRgb(160, 76, 156)),
        new(1.00, RgbFromRgb(61, 83, 163)),
    };

    private static readonly GradientStop[] HearingProfileBlueToCyan = {
        new(0.00, RgbFromRgb(61, 83, 163)),
        new(1.00, RgbFromRgb(8, 152, 205)),
    };

    private static readonly GradientStop[] HearingProfileCyanToMint = {
        new(0.00, RgbFromRgb(8, 152, 205)),
        new(1.00, RgbFromRgb(141, 209, 199)),
    };

    public static AnsiPart[] Text(
        string text,
        bool colourWhitespace = false,
        Gradient gradient = Gradient.PurpleBranding) {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<AnsiPart>();

        GradientStop[] stops = GetStops(gradient);

        if (text.Length == 1) {
            return new[]
            {
                char.IsWhiteSpace(text[0]) && !colourWhitespace
                    ? new AnsiPart(text)
                    : new AnsiPart(text, new AnsiStyle(Foreground: stops[0].Colour))
            };
        }

        var parts = new AnsiPart[text.Length];

        for (int i = 0; i < text.Length; i++) {
            if (char.IsWhiteSpace(text[i]) && !colourWhitespace) {
                parts[i] = new AnsiPart(text[i].ToString());
                continue;
            }

            double t = i / (double)(text.Length - 1);
            Rgb colour = GetColourAt(stops, t);

            parts[i] = new AnsiPart(
                text[i].ToString(),
                new AnsiStyle(Foreground: colour));
        }

        return parts;
    }

    public static AnsiPart[] Text(
        string text,
        Gradient gradient,
        bool colourWhitespace = false) {
        return Text(text, colourWhitespace, gradient);
    }

    private static GradientStop[] GetStops(Gradient gradient) {
        return gradient switch {
            Gradient.PurpleBranding => CurrentGradientStops,
            Gradient.HearingProfileAll => HearingProfileAllGradientStops,
            Gradient.HearingProfileOrangeToRed => HearingProfileOrangeToRed,
            Gradient.HearingProfileRedToPink => HearingProfileRedToPink,
            Gradient.HearingProfilePinkToPurple => HearingProfilePinkToPurple,
            Gradient.HearingProfilePurpleToBlue => HearingProfilePurpleToBlue,
            Gradient.HearingProfileBlueToCyan => HearingProfileBlueToCyan,
            Gradient.HearingProfileCyanToMint => HearingProfileCyanToMint,
            _ => CurrentGradientStops,
        };
    }

    private static Rgb GetColourAt(GradientStop[] stops, double t) {
        t = Math.Clamp(t, 0, 1);

        if (t <= stops[0].Position)
            return stops[0].Colour;

        for (int i = 1; i < stops.Length; i++) {
            GradientStop previous = stops[i - 1];
            GradientStop current = stops[i];

            if (t <= current.Position) {
                double range = current.Position - previous.Position;
                double localT = range <= 0
                    ? 0
                    : (t - previous.Position) / range;

                return Lerp(previous.Colour, current.Colour, localT);
            }
        }

        return stops[^1].Colour;
    }

    private static Rgb Lerp(Rgb a, Rgb b, double t) {
        t = Math.Clamp(t, 0, 1);

        return new Rgb(
            (byte)Math.Round(a.R + ((b.R - a.R) * t)),
            (byte)Math.Round(a.G + ((b.G - a.G) * t)),
            (byte)Math.Round(a.B + ((b.B - a.B) * t)));
    }

    private static Rgb RgbFromRgb(int r, int g, int b) {
        return new Rgb((byte)r, (byte)g, (byte)b);
    }

    private readonly struct GradientStop {
        public GradientStop(double position, Rgb colour) {
            Position = position;
            Colour = colour;
        }

        public double Position { get; }
        public Rgb Colour { get; }
    }
}