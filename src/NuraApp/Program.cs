using System.Reflection.Metadata.Ecma335;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Win32;

using NuraApp;

using NuraLib;
using NuraLib.Configuration;
using NuraLib.Devices;
using NuraLib.Logging;
using NuraLib.Monitoring;

static class Program {
    private static readonly CancellationTokenSource AppCts = new();
    public static readonly AsyncConsoleLogger logger = new();

    private static NuraClient? Client;
    private static NuraDevice? SelectedDevice = null;
    private static bool IsAuthenticated = false;
    private static string AppSettingsPath = string.Empty;
    private static NuraAppSettings AppSettings = new();
    private static readonly JsonSerializerOptions AppSettingsJsonOptions = new() {
        WriteIndented = true
    };

    static async Task Main(string[] args) {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        Console.CancelKeyPress += (_, e) => {
            e.Cancel = true;
            AppCts.Cancel();
        };

        try {

            // Setup hoist sections
            /**
             * current device:status   temporary selected-device action/status flash
             * current device          selected device identity and active profile
             * current device:state    selected device state chips
             * status                  overall app/device discovery status
             */
            logger.SetHoistedSection("current device:status", "");
            logger.SetHoistedSection("current device", "");
            logger.SetHoistedSection("current device:state", "");
            logger.SetHoistedSection("status", "Loading...");

            logger.MoveHoistedSectionToBottom("current device:status");
            logger.MoveHoistedSectionToBottom("current device");
            logger.MoveHoistedSectionToBottom("current device:state");
            logger.MoveHoistedSectionToBottom("status");

            // Show we are starting up.
            await logger.WriteLineAndWaitAsync("Application starting...");

            // Run your app here.
            await RunAsync().ConfigureAwait(false);

            // Wait until the app is closing.
            try { await Task.Delay(Timeout.Infinite, AppCts.Token); } catch (OperationCanceledException) { }

            logger.WriteLine("Application exiting...");
        } finally {
            await ShutdownClientAsync().ConfigureAwait(false);

            // Do not use AppCts.Token here, because it may already be cancelled.
            await logger.WriteLineAndWaitAsync("Console logger is shutting down.");

            await logger.StopKeyListenerAsync();

            // This completes the queue and waits for all pending messages.
            await logger.DisposeAsync();
        }
    }

    private static async Task RunAsync() {
        var configPath = Path.Combine(
            Environment.CurrentDirectory,
            "nura-config.json");

        AppSettingsPath = Path.Combine(
            Environment.CurrentDirectory,
            "app-settings.json");

        AppSettings = LoadOrCreateAppSettings(AppSettingsPath, out var wasAppSettingsCreated);

        if (wasAppSettingsCreated) {
            var showDebug = await logger.PromptYesNoAsync("Do you want to show debug messages from the headset, this can be toggled at any time by pressing T [y/N] ", false);

            AppSettings.ShowNuraDebugMessages = showDebug;
            SaveAppSettings(AppSettingsPath, AppSettings);
        }

        var config = NuraConfigStore.LoadOrCreate(configPath);
        var state = new NuraConfigState(config);
        var client = Client = new NuraClient(state);

        client.RequestStateSave += (_, args) => {
            NuraConfigStore.Save(configPath, client.State.Configuration);
        };

        client.OnLog += (_, args) => {
            // Hide spam
            if (args.Message == "Frame collection stopped after idle timeout.") return;

            // Hide debug messages
            if (!AppSettings.ShowNuraDebugMessages && args.Level <= NuraLogLevel.Debug) 
                return;

            if (args.Level > NuraLogLevel.Debug) {
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
        };

        logger.SetHoistedSection("status", "Checking stored credentials.");

        // Check if we are authenticated.
        IsAuthenticated = client.Auth.HasStoredCredentials && (await client.Auth.HasValidSessionAsync().ConfigureAwait(false));

        if (IsAuthenticated) {
            try {
                logger.SetHoistedSection("status", "Attempting to resume existing authentication state");

                await client.Auth.ResumeAsync().ConfigureAwait(false);
                IsAuthenticated = await client.Auth.HasValidSessionAsync().ConfigureAwait(false);
            } catch (Exception ex) {
                IsAuthenticated = false;

                logger.WriteLine(
                    AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                    AnsiPart.Error("[NuraLib] "),
                    $"Failed to resume previous authentication: {ex.Message}");
            }
        } else {
            // Ask the user if they wish to login
            logger.SetHoistedSection("status", "No existing session or it is now invalid, authenticating...");
            IsAuthenticated = await AttemptLogin(client).ConfigureAwait(false);
        }

        // Print authentication status
        if (IsAuthenticated) {
            logger.WriteLine(
                AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                AnsiPart.Success("[Auth] "),
                NuraGradient.Text("✓ Authenticated with nura."));

            logger.SetHoistedSection("status", AnsiPart.Success("✓ Authenticated with nura."));
        } else {
            logger.SetHoistedSection("status", "Not Authenticated with Nura.");
            logger.WriteLine(
                AnsiPart.Warning("Not authenticated. "),
                "We cannot provision devices if they are required.");
        }

        // Handler for device connected
        client.Monitoring.DeviceConnected += NuraDeviceConnectedAsync;
        client.Monitoring.DeviceDisconnected += NuraDeviceDisconnectedAsync;

        // Handle the user input for controlling devices.
        logger.KeyPressedAsync += HandleKeyPressAsync;
        if (!logger.StartKeyListener()) {
            logger.WriteLine(
                AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                AnsiPart.Warning("[NuraApp] "),
                "Keyboard hotkeys are disabled because console input is redirected or unavailable.");
        }

        await client.Monitoring.StartAsync();
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

    private static async void NuraDeviceConnectedAsync(object? sender, NuraDeviceConnectionEventArgs e) {
        var device = e.Device;

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

            device.State.BatteryChanged += (_, data) => {
                logger.WriteLine(
                    AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                    AnsiPart.FgHex("[NuraDevice] ", 0x06B6D4),
                    $"{device.Info.DisplayName}: Battery : {FormatBattery(data.Current) ?? "<null>"}.");
            };

            device.Changed += (_, data) => {
                // We received data.
                // logger.WriteLine(
                //     AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                //     AnsiPart.FgHex("[NuraDevice] ", 0x06B6D4),
                //     $"{device.Info.DisplayName}: Device state changed.");

                // Update our current status
                if (SelectedDevice == e.Device) {
                    UpdateSelectedDeviceText();
                }
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

                if (IsAuthenticated) {
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
    }

    private static async void NuraDeviceDisconnectedAsync(object? sender, NuraDeviceConnectionEventArgs e) {
        var device = e.Device;

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

    private static void UpdateSelectedDeviceText() {
        if (SelectedDevice is null) {
            logger.SetHoistedSection(
                "current device",
                AnsiLine.From("[←/→ select · ? help]  ", AnsiPart.Dim("<no device selected>")));
            logger.SetHoistedSection("current device:state", "");
            return;
        }

        var device = SelectedDevice;
        var titleSegments = new List<object> {
            AnsiPart.Dim(BuildSelectedDeviceHotkeyHint(device)),
            NuraGradient.Text(device.Info.DisplayName),
            AnsiPart.FgHex("  ", 0x64748B),
            AnsiPart.FgHex(device.Info.TypeName, 0xCBD5E1)
        };
        var stateSegments = new List<object>();

        if (device is ConnectedNuraDevice live) {
            if (live.ProvisioningRequired == true) {
                AddChip(stateSegments, "provision", FormatProvisionReason(live.ProvisioningRequirementReason), 0xF59E0B, addPrefix: false);
            } else if (!live.HasPersistentDeviceKey) {
                AddChip(stateSegments, "key", "missing", 0xF59E0B, addPrefix: false);
            } else {
                if (live.Info.Supports(NuraSystemCapabilities.Profiles)) {
                    AddChip(titleSegments, "profile", FormatProfile(live), 0xA78BFA);
                }

                AddChip(titleSegments, "battery", FormatBattery(live.State.Battery), 0x22C55E);

                AddSupportedBoolChip(stateSegments, live, NuraAudioCapabilities.Anc, "ANC", live.State.AncEnabled, 0x22C55E, addPrefix: false);
                AddSupportedBoolChip(stateSegments, live, NuraAudioCapabilities.Anc, "pass", live.State.PassthroughEnabled, 0x06B6D4);
                AddSupportedChip(stateSegments, live, NuraAudioCapabilities.AncLevel, "ANC lvl", live.State.AncLevel?.ToString(), 0x22D3EE);
                AddSupportedBoolChip(stateSegments, live, NuraAudioCapabilities.GlobalAncToggle, "global ANC", live.State.GlobalAncEnabled, 0x22C55E);
                AddSupportedChip(stateSegments, live, NuraAudioCapabilities.Immersion, "imm", FormatImmersion(live.State.EffectiveImmersionLevel ?? live.State.ImmersionLevel), 0xF97316);
                AddSupportedChip(stateSegments, live, NuraAudioCapabilities.PersonalisedMode, "mode", FormatPersonalisation(live.State.PersonalisationMode), 0xEC4899);
                AddSupportedBoolChip(stateSegments, live, NuraAudioCapabilities.Spatial, "spatial", live.State.SpatialEnabled, 0x38BDF8);
                AddReadOnlyFeatureChip(stateSegments, live, NuraAudioCapabilities.ProEq, "ProEQ");
                AddReadOnlyFeatureChip(stateSegments, live, NuraAudioCapabilities.EuAttenuation, "EU limit");
                AddReadOnlyFeatureChip(stateSegments, live, NuraInteractionCapabilities.Dial, "dial");
                AddReadOnlyFeatureChip(stateSegments, live, NuraInteractionCapabilities.TouchButtons, "buttons");
                AddReadOnlyFeatureChip(stateSegments, live, NuraInteractionCapabilities.HeadDetection, "head detect");
                if (live.Info.Supports(NuraInteractionCapabilities.VoicePromptGain)) {
                    AddChip(stateSegments, "voice", live.Configuration.VoicePromptGain?.ToString().ToLowerInvariant() ?? "supported", 0x94A3B8, addPrefix: stateSegments.Count > 0);
                }
                AddReadOnlyFeatureChip(stateSegments, live, NuraSystemCapabilities.Multipoint, "multipoint");
                AddReadOnlyFeatureChip(stateSegments, live, NuraSystemCapabilities.BulkCommands, "bulk");
                if (live.Info.IsTws) {
                    AddChip(stateSegments, "TWS", "yes", 0x38BDF8, addPrefix: stateSegments.Count > 0);
                }

                AddChip(stateSegments, "enc-key", "ok", 0x22C55E);
                AddBoolChip(stateSegments, "local", live.HasLocalSession, 0x84CC16);
                AddBoolChip(stateSegments, "mon", live.IsMonitoring, 0x84CC16);
            }

            AddChip(stateSegments, "fw", live.Info.FirmwareVersion > 0 ? live.Info.FirmwareVersion.ToString() : null, 0xA3E635);
        } else {
            stateSegments.Add(AnsiPart.Error("ERROR - Not ConnectedNuraDevice"));
        }

        logger.SetHoistedSection("current device", AnsiLine.From(titleSegments.ToArray()));
        logger.SetHoistedSection("current device:state", AnsiLine.From(stateSegments.ToArray()));
    }

#region Current Device Actions / Keybindings

    private static async ValueTask HandleKeyPressAsync(ConsoleKeyInfo key, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        if (key.Key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow) {
            SelectAdjacentDevice(key.Key == ConsoleKey.RightArrow ? 1 : -1);
            return;
        }

        switch (key.Key) {
            case ConsoleKey.UpArrow:
            case ConsoleKey.Add:
            case ConsoleKey.OemPlus:
                await AdjustSelectedImmersionAsync(1, cancellationToken).ConfigureAwait(false);
                return;

            case ConsoleKey.DownArrow:
            case ConsoleKey.Subtract:
            case ConsoleKey.OemMinus:
                await AdjustSelectedImmersionAsync(-1, cancellationToken).ConfigureAwait(false);
                return;

            case ConsoleKey.A:
                await ToggleSelectedAncAsync(cancellationToken).ConfigureAwait(false);
                return;

            case ConsoleKey.P:
                await ToggleSelectedPassthroughAsync(cancellationToken).ConfigureAwait(false);
                return;

            case ConsoleKey.B:
                await RefreshSelectedBatteryAsync(cancellationToken).ConfigureAwait(false);
                return;

            case ConsoleKey.G:
                await ToggleSelectedGlobalAncAsync(cancellationToken).ConfigureAwait(false);
                return;

            case ConsoleKey.Oem4:
                await AdjustSelectedAncLevelAsync(-1, cancellationToken).ConfigureAwait(false);
                return;

            case ConsoleKey.Oem6:
                await AdjustSelectedAncLevelAsync(1, cancellationToken).ConfigureAwait(false);
                return;

            case ConsoleKey.N:
                await ToggleSelectedPersonalisationAsync(cancellationToken).ConfigureAwait(false);
                return;

            case ConsoleKey.V:
                await CycleSelectedVoicePromptGainAsync(cancellationToken).ConfigureAwait(false);
                return;

            case ConsoleKey.S:
                await ToggleSelectedSpatialAsync(cancellationToken).ConfigureAwait(false);
                return;

            case ConsoleKey.R:
                await RefreshSelectedDeviceAsync(cancellationToken).ConfigureAwait(false);
                return;

            case ConsoleKey.T:
                ToggleNuraDebugMessages();
                return;

            case ConsoleKey.D1:
            case ConsoleKey.NumPad1:
                await SelectProfileAsync(0, cancellationToken).ConfigureAwait(false);
                return;

            case ConsoleKey.D2:
            case ConsoleKey.NumPad2:
                await SelectProfileAsync(1, cancellationToken).ConfigureAwait(false);
                return;

            case ConsoleKey.D3:
            case ConsoleKey.NumPad3:
                await SelectProfileAsync(2, cancellationToken).ConfigureAwait(false);
                return;

            case ConsoleKey.H:
                PrintHotkeyHelp();
                return;

            case ConsoleKey.Escape:
            case ConsoleKey.Q:
                logger.WriteLine(
                    AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                    AnsiPart.Warning("[NuraApp] "),
                    "Exit requested.");
                Exit();
                return;
        }

        if (key.KeyChar == '?') {
            PrintHotkeyHelp();
        } else if (key.KeyChar == '[') {
            await AdjustSelectedAncLevelAsync(-1, cancellationToken).ConfigureAwait(false);
        } else if (key.KeyChar == ']') {
            await AdjustSelectedAncLevelAsync(1, cancellationToken).ConfigureAwait(false);
        }
    }


    private static void SelectAdjacentDevice(int direction) {
        var connectedDevices = Client?.Devices.Connected
            .Where(device => device.IsConnected)
            .ToArray() ?? [];

        if (connectedDevices.Length == 0) {
            SelectedDevice = null;
            UpdateSelectedDeviceText();
            FlashSelectedDeviceStatusText(AnsiPart.Warning("No connected Nura devices available."));
            return;
        }

        var currentIndex = Array.FindIndex(
            connectedDevices,
            device => IsSameDevice(device, SelectedDevice));

        var nextIndex = currentIndex < 0
            ? 0
            : (currentIndex + direction + connectedDevices.Length) % connectedDevices.Length;

        SelectedDevice = connectedDevices[nextIndex];
        UpdateSelectedDeviceText();

        FlashSelectedDeviceStatusText(
            AnsiLine.From(
                AnsiPart.Dim($"selected {nextIndex + 1}/{connectedDevices.Length}: "),
                NuraGradient.Text(SelectedDevice.Info.DisplayName)));
    }

    private static bool IsSameDevice(NuraDevice candidate, NuraDevice? selected) {
        if (selected is null) {
            return false;
        }

        if (ReferenceEquals(candidate, selected)) {
            return true;
        }

        return string.Equals(candidate.Info.Serial, selected.Info.Serial, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(candidate.Info.DeviceAddress, selected.Info.DeviceAddress, StringComparison.OrdinalIgnoreCase);
    }

    private static ConnectedNuraDevice? GetSelectedConnectedDevice() {
        if (SelectedDevice is not ConnectedNuraDevice selected || !selected.IsConnected) {
            return null;
        }

        return Client?.Devices.Connected.FirstOrDefault(device => IsSameDevice(device, selected));
    }

    private static async Task RunSelectedDeviceActionAsync(
        string actionName,
        Func<ConnectedNuraDevice, CancellationToken, Task> action,
        CancellationToken cancellationToken
    ) {
        cancellationToken.ThrowIfCancellationRequested();

        var device = GetSelectedConnectedDevice();
        if (device is null) {
            FlashSelectedDeviceStatusText(AnsiPart.Warning("No connected device selected."));
            return;
        }

        if (!device.HasPersistentDeviceKey) {
            FlashSelectedDeviceStatusText(
                AnsiLine.From(
                    NuraGradient.Text(device.Info.DisplayName),
                    AnsiPart.Warning(": missing device key.")));
            return;
        }

        try {
            FlashSelectedDeviceStatusText(
                AnsiLine.From(
                    NuraGradient.Text(device.Info.DisplayName),
                    AnsiPart.Dim($": {actionName}...")));

            await action(device, cancellationToken).ConfigureAwait(false);

            SelectedDevice = device;
            UpdateSelectedDeviceText();
        } catch (NotSupportedException ex) {
            FlashSelectedDeviceStatusText(AnsiLine.From(AnsiPart.Warning($"{actionName}: "), ex.Message));
        } catch (InvalidOperationException ex) {
            FlashSelectedDeviceStatusText(AnsiLine.From(AnsiPart.Warning($"{actionName}: "), ex.Message));
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw;
        } catch (Exception ex) {
            logger.WriteLine(
                AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                AnsiPart.Error("[NuraApp] "),
                $"{actionName} failed for {device.Info.DisplayName}: {ex.Message}");

            FlashSelectedDeviceStatusText(AnsiLine.From(AnsiPart.Error($"{actionName} failed: "), ex.Message));
        }
    }

    private static Task RefreshSelectedDeviceAsync(CancellationToken cancellationToken) {
        return RunSelectedDeviceActionAsync(
            "refresh",
            async (device, ct) => {
                await device.RefreshAsync(ct).ConfigureAwait(false);
                FlashSelectedDeviceStatusText(
                    AnsiLine.From(
                        NuraGradient.Text(device.Info.DisplayName),
                        AnsiPart.Success(": refreshed.")));
            },
            cancellationToken);
    }

    private static Task RefreshSelectedBatteryAsync(CancellationToken cancellationToken) {
        return RunSelectedDeviceActionAsync(
            "refresh battery",
            async (device, ct) => {
                var battery = await device.State.RetrieveBatteryAsync(ct).ConfigureAwait(false);
                FlashSelectedDeviceStatusText(
                    AnsiLine.From(
                        AnsiPart.Dim("battery "),
                        FormatBattery(battery) ?? "unknown"));
            },
            cancellationToken);
    }

    private static Task ToggleSelectedAncAsync(CancellationToken cancellationToken) {
        return RunSelectedDeviceActionAsync(
            "toggle ANC",
            async (device, ct) => {
                if (!EnsureSelectedDeviceSupports(device, NuraAudioCapabilities.Anc, "ANC")) {
                    return;
                }

                var current = device.State.AncEnabled ?? await device.State.RetrieveAncEnabledAsync(ct).ConfigureAwait(false) ?? false;
                var next = !current;
                await device.State.SetAncEnabledAsync(next, ct).ConfigureAwait(false);
                FlashSelectedDeviceStatusText(
                    AnsiLine.From(
                        AnsiPart.Dim("ANC "),
                        FormatBool(next)));
            },
            cancellationToken);
    }

    private static Task ToggleSelectedPassthroughAsync(CancellationToken cancellationToken) {
        return RunSelectedDeviceActionAsync(
            "toggle passthrough",
            async (device, ct) => {
                if (!EnsureSelectedDeviceSupports(device, NuraAudioCapabilities.Anc, "passthrough/social mode") ||
                    !EnsureSelectedDeviceSupportsPassthrough(device)) {
                    return;
                }

                var current = device.State.PassthroughEnabled ?? await device.State.RetrievePassthroughEnabledAsync(ct).ConfigureAwait(false) ?? false;
                var next = !current;
                await device.State.SetPassthroughEnabledAsync(next, ct).ConfigureAwait(false);
                FlashSelectedDeviceStatusText(
                    AnsiLine.From(
                        AnsiPart.Dim("passthrough "),
                        FormatBool(next)));
            },
            cancellationToken);
    }

    private static Task AdjustSelectedAncLevelAsync(int delta, CancellationToken cancellationToken) {
        return RunSelectedDeviceActionAsync(
            delta > 0 ? "ANC level up" : "ANC level down",
            async (device, ct) => {
                if (!EnsureSelectedDeviceSupports(device, NuraAudioCapabilities.AncLevel, "ANC level")) {
                    return;
                }

                var current = device.State.AncLevel ?? await device.State.RetrieveAncLevelAsync(ct).ConfigureAwait(false) ?? 0;
                var next = Math.Clamp(current + delta, 0, 6);
                if (next == current) {
                    FlashSelectedDeviceStatusText(
                        AnsiLine.From(
                            AnsiPart.Dim("ANC level already "),
                            current.ToString()));
                    return;
                }

                await device.State.SetAncLevelAsync(next, ct).ConfigureAwait(false);
                FlashSelectedDeviceStatusText(
                    AnsiLine.From(
                        AnsiPart.Dim("ANC level "),
                        next.ToString()));
            },
            cancellationToken);
    }

    private static Task ToggleSelectedGlobalAncAsync(CancellationToken cancellationToken) {
        return RunSelectedDeviceActionAsync(
            "toggle global ANC",
            async (device, ct) => {
                if (!EnsureSelectedDeviceSupports(device, NuraAudioCapabilities.GlobalAncToggle, "global ANC")) {
                    return;
                }

                var current = device.State.GlobalAncEnabled ?? await device.State.RetrieveGlobalAncEnabledAsync(ct).ConfigureAwait(false) ?? false;
                var next = !current;
                await device.State.SetGlobalAncEnabledAsync(next, ct).ConfigureAwait(false);
                FlashSelectedDeviceStatusText(
                    AnsiLine.From(
                        AnsiPart.Dim("global ANC "),
                        FormatBool(next)));
            },
            cancellationToken);
    }

    private static Task ToggleSelectedPersonalisationAsync(CancellationToken cancellationToken) {
        return RunSelectedDeviceActionAsync(
            "toggle personalisation",
            async (device, ct) => {
                if (!EnsureSelectedDeviceSupports(device, NuraAudioCapabilities.PersonalisedMode, "personalisation")) {
                    return;
                }

                var current = device.State.PersonalisationMode ??
                              await device.State.RetrievePersonalisationModeAsync(ct).ConfigureAwait(false) ??
                              NuraPersonalisationMode.Neutral;
                var next = current == NuraPersonalisationMode.Personalised
                    ? NuraPersonalisationMode.Neutral
                    : NuraPersonalisationMode.Personalised;
                await device.State.SetPersonalisationModeAsync(next, ct).ConfigureAwait(false);
                FlashSelectedDeviceStatusText(
                    AnsiLine.From(
                        AnsiPart.Dim("personalisation "),
                        next == NuraPersonalisationMode.Personalised ? "on" : "neutral"));
            },
            cancellationToken);
    }

    private static Task CycleSelectedVoicePromptGainAsync(CancellationToken cancellationToken) {
        return RunSelectedDeviceActionAsync(
            "cycle voice prompt gain",
            async (device, ct) => {
                if (!EnsureSelectedDeviceSupports(device, NuraInteractionCapabilities.VoicePromptGain, "voice prompt gain")) {
                    return;
                }

                var current = device.Configuration.VoicePromptGain ?? NuraVoicePromptGain.Medium;
                var next = current switch {
                    NuraVoicePromptGain.Low => NuraVoicePromptGain.Medium,
                    NuraVoicePromptGain.Medium => NuraVoicePromptGain.High,
                    NuraVoicePromptGain.High => NuraVoicePromptGain.Low,
                    _ => NuraVoicePromptGain.Medium
                };
                await device.Configuration.SetVoicePromptGainAsync(next, ct).ConfigureAwait(false);
                FlashSelectedDeviceStatusText(
                    AnsiLine.From(
                        AnsiPart.Dim("voice prompt "),
                        next.ToString().ToLowerInvariant()));
            },
            cancellationToken);
    }

    private static Task ToggleSelectedSpatialAsync(CancellationToken cancellationToken) {
        return RunSelectedDeviceActionAsync(
            "toggle spatial",
            async (device, ct) => {
                if (!EnsureSelectedDeviceSupports(device, NuraAudioCapabilities.Spatial, "spatial audio")) {
                    return;
                }

                var current = device.State.SpatialEnabled ?? await device.State.RetrieveSpatialEnabledAsync(ct).ConfigureAwait(false) ?? false;
                var next = !current;
                await device.State.SetSpatialEnabledAsync(next, ct).ConfigureAwait(false);
                FlashSelectedDeviceStatusText(
                    AnsiLine.From(
                        AnsiPart.Dim("spatial "),
                        FormatBool(next)));
            },
            cancellationToken);
    }

    private static Task AdjustSelectedImmersionAsync(int delta, CancellationToken cancellationToken) {
        return RunSelectedDeviceActionAsync(
            delta > 0 ? "immersion up" : "immersion down",
            async (device, ct) => {
                if (!EnsureSelectedDeviceSupports(device, NuraAudioCapabilities.Immersion, "immersion")) {
                    return;
                }

                var current = device.State.ImmersionLevel ??
                              await device.State.RetrieveImmersionLevelAsync(ct).ConfigureAwait(false) ??
                              device.Info.DefaultImmersionLevel;
                var nextValue = Math.Clamp((int)current + delta, (int)NuraImmersionLevel.Negative2, (int)NuraImmersionLevel.Positive4);
                var next = (NuraImmersionLevel)nextValue;

                if (next == current) {
                    FlashSelectedDeviceStatusText(
                        AnsiLine.From(
                            AnsiPart.Dim("immersion already "),
                            FormatImmersion(current)));
                    return;
                }

                await device.State.SetImmersionLevelAsync(next, ct).ConfigureAwait(false);
                FlashSelectedDeviceStatusText(
                    AnsiLine.From(
                        AnsiPart.Dim("immersion "),
                        FormatImmersion(next)));
            },
            cancellationToken);
    }

    private static Task SelectProfileAsync(int profileId, CancellationToken cancellationToken) {
        return RunSelectedDeviceActionAsync(
            $"select profile {profileId + 1}",
            async (device, ct) => {
                if (!EnsureSelectedDeviceSupports(device, NuraSystemCapabilities.Profiles, "profiles")) {
                    return;
                }

                await device.Profiles.SetProfileIdAsync(profileId, ct).ConfigureAwait(false);

                var profileName = device.Profiles.Names.TryGetValue(profileId, out var name) && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : $"profile {profileId + 1}";

                FlashSelectedDeviceStatusText(
                    AnsiLine.From(
                        AnsiPart.Dim("selected "),
                        profileName));
            },
            cancellationToken);
    }

    private static void ToggleNuraDebugMessages() {
        AppSettings.ShowNuraDebugMessages = !AppSettings.ShowNuraDebugMessages;
        SaveAppSettings(AppSettingsPath, AppSettings);

        var stateText = AppSettings.ShowNuraDebugMessages ? "shown" : "hidden";
        logger.WriteLine(
            AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
            AnsiPart.FgHex("[NuraApp] ", 0x06B6D4),
            $"Nura debug/trace messages are now {stateText}.");
        FlashSelectedDeviceStatusText(
            AnsiLine.From(
                AnsiPart.Dim("Nura debug/trace messages "),
                AppSettings.ShowNuraDebugMessages
                    ? AnsiPart.Success("shown")
                    : AnsiPart.Warning("hidden")));
    }

    private static void PrintHotkeyHelp() {
        var device = GetSelectedConnectedDevice();
        logger.WriteLine(
            AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
            AnsiPart.FgHex("[NuraApp] ", 0x06B6D4),
            device is null ? "Hotkeys:" : $"Hotkeys for {device.Info.DisplayName} ({device.Info.TypeName}):");
        logger.WriteLine("  ←/→ select device, when multiple devices are connected");
        if (device?.Info.Supports(NuraAudioCapabilities.Immersion) == true) logger.WriteLine("  ↑/↓ or +/- immersion up/down");
        if (device?.Info.Supports(NuraAudioCapabilities.Anc) == true) {
            logger.WriteLine("  a toggle ANC");
            if (SupportsPassthroughHotkey(device)) {
                logger.WriteLine("  p toggle passthrough/social mode");
            }
        }
        if (device?.Info.Supports(NuraAudioCapabilities.AncLevel) == true) logger.WriteLine("  [ / ] adjust ANC level");
        if (device?.Info.Supports(NuraAudioCapabilities.GlobalAncToggle) == true) logger.WriteLine("  g toggle global ANC");
        if (device?.Info.Supports(NuraAudioCapabilities.PersonalisedMode) == true) logger.WriteLine("  n toggle personalised/neutral");
        if (device?.Info.Supports(NuraAudioCapabilities.Spatial) == true) logger.WriteLine("  s toggle spatial");
        if (device?.Info.Supports(NuraInteractionCapabilities.VoicePromptGain) == true) logger.WriteLine("  v cycle voice prompt gain");
        if (device?.Info.Supports(NuraSystemCapabilities.Profiles) == true) logger.WriteLine("  1/2/3 select profile slot");
        logger.WriteLine("  b refresh battery");
        logger.WriteLine("  r refresh selected device");
        logger.WriteLine("  t toggle Nura debug/trace logging");
        logger.WriteLine("  h or ? show help");
        logger.WriteLine("  q or Esc exit");
    }


#endregion

#region Hoisted Status Lines

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

#endregion

// Helper methods

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

    private static string BuildSelectedDeviceHotkeyHint(NuraDevice selectedDevice) {
        var connectedDevices = Client?.Devices.Connected
            .Where(device => device.IsConnected)
            .ToArray() ?? [];
        var currentIndex = Array.FindIndex(
            connectedDevices,
            device => IsSameDevice(device, selectedDevice));

        var hasPrevious = connectedDevices.Length > 1 && currentIndex > 0;
        var hasNext = connectedDevices.Length > 1 && currentIndex >= 0 && currentIndex < connectedDevices.Length - 1;
        var selectHint = (hasPrevious, hasNext) switch {
            (true, true) => "←/→ dev",
            (true, false) => "← dev",
            (false, true) => "→ dev",
            _ => null
        };

        var parts = new List<string>();
        if (selectHint is not null) {
            parts.Add(selectHint);
        }

        if (selectedDevice.Info.Supports(NuraAudioCapabilities.Immersion)) {
            parts.Add("↑/↓ imm");
        }

        if (selectedDevice.Info.Supports(NuraAudioCapabilities.Anc)) {
            parts.Add("a ANC");
            if (SupportsPassthroughHotkey(selectedDevice)) {
                parts.Add("p pass");
            }
        }

        if (selectedDevice.Info.Supports(NuraAudioCapabilities.AncLevel)) {
            parts.Add("[/] ANC lvl");
        }

        if (selectedDevice.Info.Supports(NuraAudioCapabilities.GlobalAncToggle)) {
            parts.Add("g global");
        }

        if (selectedDevice.Info.Supports(NuraAudioCapabilities.PersonalisedMode)) {
            parts.Add("n mode");
        }

        if (selectedDevice.Info.Supports(NuraAudioCapabilities.Spatial)) {
            parts.Add("s spatial");
        }

        if (selectedDevice.Info.Supports(NuraInteractionCapabilities.VoicePromptGain)) {
            parts.Add("v voice");
        }

        parts.Add("b battery");
        parts.Add("r refresh");
        parts.Add("t trace");
        parts.Add("h help");

        return $"[{string.Join(" · ", parts)}]  ";
    }

    static void AddChip(List<object> segments, string label, string? value, int valueColour, bool addPrefix = true) {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (addPrefix)
            segments.Add(AnsiPart.FgHex(" / ", 0x64748B));

        segments.Add(AnsiPart.FgHex(label, 0xCBD5E1));

        //segments.Add(AnsiPart.FgHex(":", 0x64748B));
        segments.Add(" ");
        segments.Add(AnsiPart.FgHex(value, valueColour));
    }

    static void AddBoolChip(List<object> segments, string label, bool? value, int onColour, bool addPrefix = true) {
        if (value is null)
            return;

        AddChip(
            segments,
            label,
            value.Value ? "on" : "off",
            value.Value ? onColour : 0x94A3B8,
            addPrefix);
    }

    static void AddSupportedChip(List<object> segments, ConnectedNuraDevice device, NuraAudioCapabilities capability, string label, string? value, int valueColour) {
        if (!device.Info.Supports(capability)) {
            return;
        }

        AddChip(segments, label, value ?? "?", value is null ? 0x94A3B8 : valueColour, addPrefix: segments.Count > 0);
    }

    static void AddSupportedBoolChip(List<object> segments, ConnectedNuraDevice device, NuraAudioCapabilities capability, string label, bool? value, int onColour, bool addPrefix = true) {
        if (!device.Info.Supports(capability)) {
            return;
        }

        if (value is null) {
            AddChip(segments, label, "?", 0x94A3B8, addPrefix: addPrefix && segments.Count > 0);
            return;
        }

        AddBoolChip(segments, label, value, onColour, addPrefix: addPrefix && segments.Count > 0);
    }

    static void AddReadOnlyFeatureChip(List<object> segments, ConnectedNuraDevice device, NuraAudioCapabilities capability, string label) {
        if (device.Info.Supports(capability)) {
            AddChip(segments, label, "supported", 0x94A3B8, addPrefix: segments.Count > 0);
        }
    }

    static void AddReadOnlyFeatureChip(List<object> segments, ConnectedNuraDevice device, NuraInteractionCapabilities capability, string label) {
        if (device.Info.Supports(capability)) {
            AddChip(segments, label, "supported", 0x94A3B8, addPrefix: segments.Count > 0);
        }
    }

    static void AddReadOnlyFeatureChip(List<object> segments, ConnectedNuraDevice device, NuraSystemCapabilities capability, string label) {
        if (device.Info.Supports(capability)) {
            AddChip(segments, label, "supported", 0x94A3B8, addPrefix: segments.Count > 0);
        }
    }

    private static bool EnsureSelectedDeviceSupports(ConnectedNuraDevice device, NuraAudioCapabilities capability, string featureName) {
        if (device.Info.Supports(capability)) {
            return true;
        }

        FlashSelectedDeviceStatusText(
            AnsiLine.From(
                AnsiPart.Warning(featureName),
                $" is not supported on {device.Info.TypeName}."));
        return false;
    }

    private static bool EnsureSelectedDeviceSupports(ConnectedNuraDevice device, NuraInteractionCapabilities capability, string featureName) {
        if (device.Info.Supports(capability)) {
            return true;
        }

        FlashSelectedDeviceStatusText(
            AnsiLine.From(
                AnsiPart.Warning(featureName),
                $" is not supported on {device.Info.TypeName}."));
        return false;
    }

    private static bool EnsureSelectedDeviceSupports(ConnectedNuraDevice device, NuraSystemCapabilities capability, string featureName) {
        if (device.Info.Supports(capability)) {
            return true;
        }

        FlashSelectedDeviceStatusText(
            AnsiLine.From(
                AnsiPart.Warning(featureName),
                $" is not supported on {device.Info.TypeName}."));
        return false;
    }

    private static bool EnsureSelectedDeviceSupportsPassthrough(ConnectedNuraDevice device) {
        if (SupportsPassthroughHotkey(device)) {
            return true;
        }

        FlashSelectedDeviceStatusText(
            AnsiLine.From(
                AnsiPart.Warning("passthrough/social mode"),
                $" is not supported by the currently wired transport for {device.Info.TypeName}."));
        return false;
    }

    private static bool SupportsPassthroughHotkey(NuraDevice device) =>
        device.Info.Supports(NuraAudioCapabilities.Anc) &&
        !device.Info.Supports(NuraAudioCapabilities.GlobalAncToggle) &&
        (device.Info.DeviceType != NuraDeviceType.Nuraphone || device.Info.FirmwareVersion > 510);

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

    static string? FormatPersonalisation(NuraPersonalisationMode? mode) =>
        mode switch {
            NuraPersonalisationMode.Personalised => "personalized",
            NuraPersonalisationMode.Neutral => "neutral",
            null => null,
            _ => mode.ToString()
        };

    static string? FormatBattery(NuraBatteryStatus? battery) =>
        battery is null ? null : $"{battery.BatteryPercentage}%";

    static string? FormatProfile(ConnectedNuraDevice device) {
        if (device.Profiles.ProfileId is not { } profileId) {
            return null;
        }

        var displayProfileId = profileId + 1;
        if (device.Profiles.Names.TryGetValue(profileId, out var name) && !string.IsNullOrWhiteSpace(name)) {
            return $"({displayProfileId}) {name}";
        }

        return displayProfileId.ToString();
    }

    static string FormatProvisionReason(NuraProvisioningRequirementReason reason) =>
        reason switch {
            NuraProvisioningRequirementReason.MissingDeviceKey => "missing-key",
            NuraProvisioningRequirementReason.NuraNowRefreshRequired => "nuranow-refresh",
            _ => "required"
        };

    private static NuraAppSettings LoadOrCreateAppSettings(string path, out bool wasCreated) {
        try {
            if (File.Exists(path)) {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<NuraAppSettings>(json, AppSettingsJsonOptions);
                if (settings is not null) {
                    wasCreated = false;
                    return settings;
                }
            }
        } catch (Exception ex) {
            logger.WriteLine(
                AnsiPart.Dim($"[{DateTime.Now:HH:mm:ss}] "),
                AnsiPart.Warning("[NuraApp] "),
                $"Failed to read app settings, recreating defaults: {ex.Message}");
        }

        var defaultSettings = new NuraAppSettings();
        SaveAppSettings(path, defaultSettings);
        wasCreated = true;
        return defaultSettings;
    }

    private static void SaveAppSettings(string path, NuraAppSettings settings) {
        if (string.IsNullOrWhiteSpace(path)) {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(settings, AppSettingsJsonOptions));
    }

    public static void Exit() => AppCts.Cancel();
    public static async Task ExitAsync() => await AppCts.CancelAsync();
}

public sealed class NuraAppSettings {
    public bool ShowNuraDebugMessages { get; set; } = false;
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
