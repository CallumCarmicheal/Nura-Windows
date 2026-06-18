# NuraLib SDK Guide

## What NuraLib is

`NuraLib` is the host-side library for:

- discovering connected Nura devices over Windows Bluetooth
- persisting device inventory and authentication state
- logging into the Nura backend when a device needs provisioning
- provisioning a persistent device key for local encrypted control
- opening a local RFCOMM session to a headset
- reading and updating supported device state, profile data, and selected configuration values
- monitoring connection changes and live headset indications

## Platform assumptions

`NuraLib` currently targets:

- `.NET 10`
- `net10.0-windows`
- Windows Bluetooth APIs and Winsock RFCOMM

That matters for two reasons:

1. Device discovery uses Windows Bluetooth enumeration.
2. Live device control uses a Windows RFCOMM transport internally.

The current transport implementation is Windows-only.

## Package shape

The main entry point is `src/NuraLib/NuraClient.cs`.

It gives you three top-level services:

- `Auth`
- `Devices`
- `Monitoring`

It also exposes:

- `State`
- `RequestStateSave`
- `OnLog`

`NuraLib` does not persist state. It updates the in-memory configuration snapshot and raises save notifications for the host.

## Mental model

Typical host flow:

1. Load or create a `NuraConfig`.
2. Wrap it in `NuraConfigState`.
3. Create a `NuraClient`.
4. Subscribe to `RequestStateSave` and persist `client.State.Configuration` whenever the library asks.
5. Subscribe to `OnLog` for diagnostics.
6. Call `Devices.RefreshAsync()` to discover currently connected Nura headsets.
7. Pick a `ConnectedNuraDevice`.
8. If needed, authenticate and provision it.
9. Open a local session and start using `State`, `Profiles`, and `Configuration`.

## Minimal bootstrap

```csharp
using NuraLib;
using NuraLib.Configuration;

var configPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "MyApp",
    "nura-config.json");

var config = NuraConfigStore.LoadOrCreate(configPath);
var state = new NuraConfigState(config);
var client = new NuraClient(state);

client.RequestStateSave += (_, args) =>
{
    NuraConfigStore.Save(configPath, client.State.Configuration);
};

client.OnLog += (_, args) =>
{
    Console.WriteLine($"[{args.TimestampUtc:O}] {args.Level} {args.Source}: {args.Message}");
};
```

## Bootstrap expectations

In practice, most hosts benefit from two startup paths:

- a live bootstrap that loads persisted config, creates `NuraClient`, subscribes to `RequestStateSave`, optionally resumes auth, refreshes devices, and starts monitoring
- a seeded or demo bootstrap that bypasses `NuraLib` entirely and feeds sample data into the host UI or test harness

Keep the demo path outside the library. `NuraLib` is the live transport and auth surface, not the source of seeded sample data.

Typical live bootstrap:

```csharp
var config = NuraConfigStore.LoadOrCreate(configPath);
var client = new NuraClient(new NuraConfigState(config));

client.RequestStateSave += (_, _) =>
{
    NuraConfigStore.Save(configPath, client.State.Configuration);
};

client.OnLog += (_, args) =>
{
    Console.WriteLine($"[{args.Level}] {args.Source}: {args.Message}");
};

var resumed = false;

if (client.Auth.HasStoredCredentials && await client.Auth.HasValidSessionAsync())
{
    try
    {
        await client.Auth.ResumeAsync();
        resumed = true;
    }
    catch
    {
        resumed = false;
    }
}

await client.Devices.RefreshAsync();
await client.Monitoring.StartAsync();
```

## Configuration and persistence

The persisted root model is `src/NuraLib/Configuration/NuraConfig.cs`.

Important fields:

- `ApiBase`
- `Uuid`
- `Auth`
- `Devices`

`NuraLib` updates state for:

- account/session auth fields
- discovered device inventory
- recovered persistent device keys

### Load and save helpers

`NuraConfigStore` in `src/NuraLib/Configuration/NuraConfigStore.cs` is provided as a convenience:

```csharp
var config = NuraConfigStore.LoadOrCreate(path);
NuraConfigStore.Save(path, config);
```

### Save notifications

The library raises `NuraClient.RequestStateSave` whenever it mutates durable state.

The event payload is `src/NuraLib/NuraStateSaveRequestedEventArgs.cs`, which contains:

- `Reasons`
- `DeviceSerial`
- `Message`

Reasons are flags from `src/NuraLib/NuraStateSaveReason.cs`:

- `Configuration`
- `Authentication`
- `DeviceInventory`
- `DeviceKey`
- `Session`
- `Bootstrap`

Save requests indicate that the current in-memory state should be persisted by the host. The library never writes files on its own.

### Host-managed persistence

`NuraConfigStore` is optional. Any host-side persistence mechanism can be used as long as it can:

- construct a `NuraConfig`
- pass it into `NuraConfigState`
- persist `client.State.Configuration` when `RequestStateSave` is raised

Example using a custom JSON persistence layer:

```csharp
using System.Text.Json;
using NuraLib;
using NuraLib.Configuration;

static NuraConfig LoadConfig(string path)
{
    if (!File.Exists(path))
    {
        return new NuraConfig();
    }

    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<NuraConfig>(json, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    }) ?? new NuraConfig();
}

static void SaveConfig(string path, NuraConfig config)
{
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrWhiteSpace(directory))
    {
        Directory.CreateDirectory(directory);
    }

    var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    });

    File.WriteAllText(path, json);
}

var path = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "MyApp",
    "nura-state.json");

var state = new NuraConfigState(LoadConfig(path));
var client = new NuraClient(state);

client.RequestStateSave += (_, _) =>
{
    SaveConfig(path, client.State.Configuration);
};
```

The same pattern applies if state is stored in a database, encrypted secret store, roaming profile, or any other application-specific persistence layer.

### Example persisted config

The JSON serializer uses camelCase. A realistic file looks like this:

```json
{
  "apiBase": "https://api-p3.nuraphone.com/",
  "uuid": "0a927987-c8ed-4bda-af94-2fb6d4836798",
  "auth": {
    "userEmail": "user@example.com",
    "authUid": "user@example.com",
    "accessToken": "token",
    "clientKey": "client-key",
    "tokenType": "Bearer",
    "tokenExpiryUnix": 1770000000
  },
  "devices": [
    {
      "type": "Nuraphone",
      "deviceAddress": "00:11:22:33:44:55",
      "deviceSerial": "12345678",
      "firmwareVersion": 606,
      "maxPacketLengthHint": 182,
      "isNuraNowDevice": true,
      "lastProvisionedUtc": "2026-04-21T12:34:56.0000000+00:00",
      "deviceKey": "base64-16-byte-key"
    }
  ]
}
```

## Authentication flow

The auth surface is `src/NuraLib/Auth/NuraAuthManager.cs`.

Public members you care about:

- `HasStoredCredentials`
- `HasValidSessionAsync()`
- `RequestEmailCodeAsync(email)`
- `VerifyEmailCodeAsync(code, email?)`
- `ResumeAsync()`

### Typical email login flow

```csharp
await client.Auth.RequestEmailCodeAsync("user@example.com");

// prompt user for code
await client.Auth.VerifyEmailCodeAsync("123456", "user@example.com");
```

After verification succeeds, `NuraLib` updates in-memory auth state and raises a save request. Persist the configuration at that point.

### Session reuse

```csharp
if (await client.Auth.HasValidSessionAsync())
{
    await client.Auth.ResumeAsync();
}
```

`HasValidSessionAsync()` is a local check against stored auth fields and token expiry. `ResumeAsync()` performs the backend validation call and refreshes runtime session state.

### What auth is used for

Auth is not required for every operation.

Auth is required when a device still needs provisioning and the library has to recover a persistent device key via the backend-assisted bootstrap flow.

Once a device has a persistent key stored in config, local control works without doing login every time.

## Device discovery

The device manager is `src/NuraLib/Devices/NuraDeviceManager.cs`.

Public members:

- `All`
- `Connected`
- `RefreshAsync()`
- `FindBySerial(serial)`

### Refresh and inspect

```csharp
await client.Devices.RefreshAsync();

foreach (var device in client.Devices.All)
{
    Console.WriteLine($"{device.Info.TypeName} {device.Info.Serial} {device.Info.DeviceAddress}");
}
```

`All` contains:

- persisted devices from configuration
- currently connected devices discovered from Bluetooth

`Connected` contains only devices that are currently connected and support live operations.

### Find a connected device

```csharp
await client.Devices.RefreshAsync();

var connected = client.Devices.Connected.FirstOrDefault();
if (connected is null)
{
    throw new InvalidOperationException("No connected Nura device found.");
}
```

## Known device vs connected device

There are two primary device types:

- `src/NuraLib/Devices/NuraDevice.cs`
- `src/NuraLib/Devices/ConnectedNuraDevice.cs`

`NuraDevice` is a known device snapshot with identity and capabilities.

`ConnectedNuraDevice` is the live device surface. It adds:

- `State`
- `Configuration`
- `Profiles`
- `ConnectLocalAsync()`
- `EnsureProvisionedAsync()`
- `RefreshAsync()`
- `StartMonitoringAsync()`
- `StopMonitoringAsync()`
- `HeadsetIndicationReceived`

## Device info and capabilities

Every device has a `NuraDeviceInfo` model defined in `src/NuraLib/Devices/NuraDeviceInfo.cs`.

Important fields:

- `TypeTag`
- `TypeName`
- `DeviceType`
- `DeviceAddress`
- `Serial`
- `FirmwareVersion`
- `MaxPacketLengthHint`
- `IsTws`
- `SupportedFeatures`
- `Capabilities`
- `SupportedButtonGestures`

Capability checks:

```csharp
if (device.Info.Supports(NuraAudioCapabilities.Anc))
{
    // safe to use ANC APIs
}

if (device.Info.Supports(NuraInteractionCapabilities.TouchButtons))
{
    // safe to use button configuration APIs
}

if (device.Info.Supports(NuraSystemCapabilities.Profiles))
{
    // safe to use profile APIs
}
```

The library also enforces these checks internally and throws `NotSupportedException` if you call a feature that does not apply to the device or firmware.

Capability resolution lives in `src/NuraLib/Devices/NuraDeviceCapabilities.cs`.

That is where support is derived for:

- Nuraphone
- NuraLoop
- NuraTrue
- NuraBuds
- NuraTrue Pro
- NuraTrue Sport
- Denon Perl / Perl Pro aliases that map onto the Nura True families

## Provisioning

Provisioning is the step that turns:

- a connected headset
- plus a logged-in backend session

into:

- a stored persistent 16-byte device key

That key is saved back into `NuraConfig.Devices[*].DeviceKey` as base64.

### Check whether a device needs provisioning

```csharp
if (await connected.RequiresProvisioningAsync())
{
    Console.WriteLine("This device still needs provisioning.");
}
```

`RequiresProvisioningAsync()` returns `true` when:

- the device has no stored persistent key
- or the device is marked as a NuraNow device and its last successful provisioning is older than 30 days

`ConnectedNuraDevice.ProvisioningRequired` is evaluated from local configuration when the device object is created and refreshed when its config changes, so it should be `true` or `false` by the time a device is passed to `DeviceConnected`. Use `ProvisioningRequirementReason` to distinguish why:

- `MissingDeviceKey`: the host does not have the persistent device key, so local encrypted control cannot start until provisioning recovers it
- `NuraNowRefreshRequired`: the host has a key, but this is a host-marked NuraNow device that should phone home to refresh entitlement
- `None`: no backend-assisted provisioning is required for local control

Important limitation:

- `NuraLib` does not currently auto-detect NuraNow devices from device metadata or backend responses
- the host must persist that knowledge in `NuraDeviceConfig.IsNuraNowDevice`

### Provision a device

```csharp
var result = await connected.EnsureProvisionedAsync();

if (!result.Success)
{
    Console.WriteLine($"Provisioning failed: {result.Error}");
}
```

If you already know the device is a NuraNow device and want to phone home unconditionally, force it:

```csharp
var result = await connected.EnsureProvisionedAsync(forceProvision: true);
```

Result type: `src/NuraLib/Devices/NuraProvisioningResult.cs`

Known error values: `src/NuraLib/Devices/NuraProvisioningError.cs`

Current errors:

- `Unknown`
- `NotAuthenticated`

### Provisioning preconditions

Provisioning requires:

- the device is connected
- the device either does not already have a persistent key, or is a NuraNow device whose provisioning lease has expired, or provisioning was explicitly forced
- the auth manager has stored credentials
- the backend accepts the current session

If the device already has a persistent key and does not need a NuraNow refresh, `EnsureProvisionedAsync()` succeeds immediately.

## Local encrypted device session

Once the device has a persistent key, the library can create a local RFCOMM session and perform the app handshake.

Open the session explicitly:

```csharp
await connected.ConnectLocalAsync();
```

Most high-level operations will open the local session lazily if needed, so calling `ConnectLocalAsync()` up front is optional.

### Important note about session lifetime

There is no separate public `DisconnectAsync()` method right now.

`StopMonitoringAsync()` tears down the local session even if indication monitoring was not being used. To drop the RFCOMM session, call:

```csharp
await connected.StopMonitoringAsync();
```

The method name is broader than the actual effect, so host-side session lifetime should reflect that.

## State API

Live device state is exposed via `src/NuraLib/Devices/State/NuraDeviceState.cs`.

Properties:

- `Anc`
- `AncLevel`
- `AncEnabled`
- `PassthroughEnabled`
- `GlobalAncEnabled`
- `SpatialEnabled`
- `PersonalisationMode`
- `ImmersionLevel`
- `EffectiveImmersionLevel`
- `ProEqEnabled`
- `ProEq`
- `Battery`

Events:

- `AncChanged`
- `AncLevelChanged`
- `AncEnabledChanged`
- `PassthroughEnabledChanged`
- `GlobalAncEnabledChanged`
- `SpatialEnabledChanged`
- `PersonalisationModeChanged`
- `ImmersionLevelChanged`
- `EffectiveImmersionLevelChanged`
- `ProEqEnabledChanged`
- `ProEqChanged`
- `BatteryChanged`

### Read state

```csharp
await connected.RefreshStateAsync();

Console.WriteLine($"ANC: {connected.State.Anc?.Mode}");
Console.WriteLine($"ANC level: {connected.State.AncLevel}");
Console.WriteLine($"Spatial: {connected.State.SpatialEnabled}");
Console.WriteLine($"Mode: {connected.State.PersonalisationMode}");
Console.WriteLine($"Immersion: {connected.State.ImmersionLevel}");
Console.WriteLine($"Battery: {connected.State.Battery?.BatteryPercentage}%");
```

### Subscribe to state changes

```csharp
connected.State.AncChanged += (_, args) =>
{
    Console.WriteLine($"ANC changed: {args.Previous?.Mode} -> {args.Current?.Mode}");
};

connected.State.ImmersionLevelChanged += (_, args) =>
{
    Console.WriteLine($"Immersion changed: {args.Previous} -> {args.Current}");
};

connected.State.BatteryChanged += (_, args) =>
{
    Console.WriteLine($"Battery changed: {args.Current?.BatteryPercentage}%");
};
```

### Battery

Battery status is exposed as `NuraBatteryStatus` on `connected.State.Battery`.

`RefreshAsync()` reads battery status as part of the full device refresh. You can also request it directly:

```csharp
var battery = await connected.State.RetrieveBatteryAsync();
Console.WriteLine($"Battery: {battery?.BatteryPercentage}%");
```

The model includes the decoded percentage plus raw voltage, charger, and NTC fields for hosts that want to show more detailed diagnostics.

### ANC example

`NuraAncState`, defined in `src/NuraLib/Devices/State/NuraAncState.cs`, is the model used for ANC and passthrough.

```csharp
await connected.State.SetAncAsync(new NuraAncState
{
    AncEnabled = true,
    PassthroughEnabled = false
});
```

Shortcut operations also exist:

```csharp
await connected.State.SetAncEnabledAsync(true);
await connected.State.SetPassthroughEnabledAsync(false);
await connected.State.SetAncLevelAsync(4);
await connected.State.SetGlobalAncEnabledAsync(true);
```

Classic Nuraphone ANC state is primarily observed from headset indications. The library does not issue the direct `GetANCState` app-encrypted command for classic Nuraphone because some firmware returns a same-command GAIA error that can invalidate the local encrypted command stream. Treat null ANC values as "unknown until refreshed by a supported transport or indication" on that device family.

### Personalisation and immersion

```csharp
await connected.State.SetPersonalisationModeAsync(NuraPersonalisationMode.Personalised);
await connected.State.SetImmersionLevelAsync(NuraImmersionLevel.Positive3);
```

Important behavior:

- immersion changes are blocked when personalisation mode is `Neutral`
- some transport behavior differs for classic Nuraphone vs newer devices
- classic Nuraphone immersion transport is not fully wired for the newer Kickit-state path

Generic hosts can gate on capability and account for device-family-specific `NotImplementedException` cases.

### Spatial

```csharp
if (connected.Info.Supports(NuraAudioCapabilities.Spatial))
{
    var enabled = await connected.State.RetrieveSpatialEnabledAsync();
    await connected.State.SetSpatialEnabledAsync(!(enabled ?? false));
}
```

### ProEQ

ProEQ properties exist on `NuraDeviceState`, but the Bluetooth implementation is not wired yet.

These currently throw `NotImplementedException`:

- `RetrieveProEqEnabledAsync`
- `SetProEqEnabledAsync`
- `RetrieveProEqAsync`
- `SetProEqAsync`

## Profiles API

Profiles are exposed via `src/NuraLib/Devices/State/NuraProfiles.cs`.

Properties:

- `ProfileId`
- `Names`
- `Visualisations`
- `CurrentVisualisation`

Events:

- `ProfileIdChanged`
- `NamesChanged`
- `VisualisationsChanged`

### Read and change active profile

```csharp
var currentProfileId = await connected.Profiles.RetrieveProfileIdAsync();
Console.WriteLine($"Current profile: {currentProfileId}");

await connected.Profiles.SetProfileIdAsync(1);
```

### Read profile names

```csharp
var names = await connected.Profiles.RefreshNamesAsync(3);

foreach (var pair in names)
{
    Console.WriteLine($"Profile {pair.Key}: {pair.Value}");
}
```

### Profile visualisation data

Visualisation support is split into two sources:

- bulk authenticated profile metadata from the online session/bootstrap flow
- active-profile visualisation data read directly from Bluetooth

The online payload is useful for selector thumbnails because it can populate multiple profile slots without switching profiles on the headset. The Bluetooth path is the source of truth for the currently active profile after connect, refresh, or profile switch.

Read the current active-profile visualisation:

```csharp
var currentVisual = await connected.Profiles.RetrieveCurrentVisualisationAsync();

if (currentVisual is { Valid: true })
{
    Console.WriteLine($"Colour: {currentVisual.Colour}");
    Console.WriteLine($"Left points: {currentVisual.LeftData.Length}");
    Console.WriteLine($"Right points: {currentVisual.RightData.Length}");
}
```

Refresh the cached visualisation set:

```csharp
var visuals = await connected.Profiles.RefreshVisualisationsAsync(
    profileCount: 3,
    includeOnlineMetadata: true);

foreach (var pair in visuals)
{
    Console.WriteLine($"Profile {pair.Key} has cached visual data.");
}
```

The cache surface is:

- `connected.Profiles.Visualisations`
- `connected.Profiles.CurrentVisualisation`

Hosts should treat missing visual data as a normal state. If a profile has no cached visual yet, keep a fallback thumbnail or neutral placeholder rather than forcing hidden profile switches.

### Profile rename support

`SetNameAsync()` exists on `NuraProfiles`, but it is not wired yet and currently throws `NotImplementedException`.

## Device configuration API

Device configuration is exposed via `src/NuraLib/Devices/Configuration/NuraDeviceConfiguration.cs`.

Cached properties:

- `TouchButtons`
- `Dial`
- `HeadDetectionEnabled`
- `ManualHeadDetectionEnabled`
- `MultipointEnabled`
- `VoicePromptGain`

### Touch button configuration

Model: `src/NuraLib/Devices/Configuration/NuraButtonConfiguration.cs`

Functions: `src/NuraLib/Devices/Configuration/NuraButtonFunction.cs`

Gestures are validated against device capability. The library will reject configurations that assign unsupported gesture slots.

Read:

```csharp
var buttons = await connected.Configuration.RetrieveTouchButtonsAsync();
```

Write:

```csharp
var next = new NuraButtonConfiguration
{
    LeftSingleTap = NuraButtonFunction.PlayPauseOnly,
    RightSingleTap = NuraButtonFunction.NextTrack,
    LeftDoubleTap = NuraButtonFunction.VolumeDown,
    RightDoubleTap = NuraButtonFunction.VolumeUp,
    LeftTapAndHold = NuraButtonFunction.ToggleSocial,
    RightTapAndHold = NuraButtonFunction.VoiceAssistant
};

await connected.Configuration.SetTouchButtonsAsync(next);
```

Or modify a single binding:

```csharp
var current = await connected.Configuration.RetrieveTouchButtonsAsync() ?? new NuraButtonConfiguration();
var updated = current.WithBinding(
    NuraButtonSide.Left,
    NuraButtonGesture.SingleTap,
    NuraButtonFunction.PlayPauseOnly);

await connected.Configuration.SetTouchButtonsAsync(updated);
```

### Dial configuration

Model: `src/NuraLib/Devices/Configuration/NuraDialConfiguration.cs`

Functions: `src/NuraLib/Devices/Configuration/NuraDialFunction.cs`

Read:

```csharp
var dial = await connected.Configuration.RetrieveDialAsync();
```

Write:

```csharp
var dial = new NuraDialConfiguration
{
    Left = NuraDialFunction.Kickit,
    Right = NuraDialFunction.Volume
};

await connected.Configuration.SetDialAsync(dial);
```

### Voice prompt gain

Write is implemented:

```csharp
await connected.Configuration.SetVoicePromptGainAsync(NuraVoicePromptGain.Medium);
```

Read is not implemented yet:

- `RetrieveVoicePromptGainAsync()` throws `NotImplementedException`

### Other configuration surfaces not implemented yet

These public methods currently throw `NotImplementedException`:

- `RetrieveHeadDetectionEnabledAsync`
- `SetHeadDetectionEnabledAsync`
- `RetrieveManualHeadDetectionEnabledAsync`
- `SetManualHeadDetectionEnabledAsync`
- `RetrieveMultipointEnabledAsync`
- `SetMultipointEnabledAsync`
- `RetrieveVoicePromptGainAsync`

## Refresh strategy

The broad refresh methods populate everything the library currently knows how to retrieve:

```csharp
await connected.RefreshAsync();
```

Or pull only what you need:

```csharp
await connected.RefreshProfilesAsync();
await connected.RefreshConfigurationAsync();
await connected.RefreshStateAsync();
```

A typical pattern is:

1. `Devices.RefreshAsync()`
2. get a `ConnectedNuraDevice`
3. provision if needed
4. `connected.RefreshAsync()`
5. bind your UI or services to cached state

## Device identity and live status

`NuraDevice.Info.DisplayName` is the preferred host display name.

- If the last known Bluetooth friendly name is available, `DisplayName` uses it.
- Otherwise it falls back to the normalized device type name.

For live devices, `ConnectedNuraDevice` also exposes runtime status that is useful for hosts:

- `HasLocalSession`
- `IsMonitoring`
- `ProvisioningRequired`
- `ProvisioningRequirementReason`
- `OperationStatus`

Relevant events include:

- `Changed`
- `IsConnectedChanged`
- `InfoChanged`
- `HasPersistentDeviceKeyChanged`
- `LocalSessionChanged`
- `MonitoringChanged`
- `ProvisioningRequiredChanged`
- `ProvisioningRequirementReasonChanged`
- `OperationStatusChanged`

Use `Changed` when your host wants one broad invalidation signal and will refresh its bound view-model values itself. The more specific events are still available for fine-grained bindings and XAML-style updates.

`OperationStatus` is the host-facing progress surface for long-running device work. In particular, provisioning emits raw backend stage codes such as `session/start`, `session/start_1`, and `session/start_4`. Hosts can display those directly instead of scraping logger output.

## Monitoring

There are two monitoring layers:

### 1. Connection-level monitoring

`src/NuraLib/Monitoring/NuraMonitoringManager.cs`

This watches for device connect/disconnect events by periodically refreshing the device inventory.

Events:

- `DeviceConnected`
- `DeviceDisconnected`

Example:

```csharp
client.Monitoring.DeviceConnected += (_, args) =>
{
    Console.WriteLine($"Connected: {args.Device.Info.TypeName} {args.Device.Info.Serial}");
};

client.Monitoring.DeviceDisconnected += (_, args) =>
{
    Console.WriteLine($"Disconnected: {args.Device.Info.TypeName} {args.Device.Info.Serial}");
};

await client.Monitoring.StartAsync();
```

`StopAsync()` stops only the connection polling loop. It does not stop any per-device indication loops that were started with `ConnectedNuraDevice.StartMonitoringAsync()`.

When the monitoring loop observes an actual disconnect, `NuraLib` marks that device disconnected and tears down its local session. App shutdown is different: the devices may still be connected, so the host should stop each device-level monitor/session explicitly.

### 2. Device-level indication monitoring

`ConnectedNuraDevice.StartMonitoringAsync()` starts the headset indication loop for one device.

That loop updates cached state from indication frames such as:

- current profile changed
- ANC parameters changed
- ANC level changed
- Kickit enabled changed
- Kickit level changed

Example:

```csharp
connected.HeadsetIndicationReceived += (_, args) =>
{
    Console.WriteLine($"Indication: {args.Identifier} = 0x{args.Value:x2}");
};

await connected.StartMonitoringAsync();
```

To stop it:

```csharp
await connected.StopMonitoringAsync();
```

Again: stopping device monitoring also tears down the local RFCOMM session.

## Logging

Subscribe to `NuraClient.OnLog`.

Payload type: `src/NuraLib/Logging/NuraLogEventArgs.cs`

Levels: `src/NuraLib/Logging/NuraLogLevel.cs`

- `Trace`
- `Debug`
- `Information`
- `Warning`
- `Error`

Example:

```csharp
client.OnLog += (_, args) =>
{
    Console.WriteLine(
        $"[{args.TimestampUtc:O}] {args.Level,-11} {args.Source}: {args.Message}");

    if (args.Exception is not null)
    {
        Console.WriteLine(args.Exception);
    }
};
```

Use this when you are integrating provisioning, local handshake, or Bluetooth state retrieval. It will save time.

Do not log raw secrets. Treat these as sensitive:

- `NuraConfig.Auth.AccessToken`
- `NuraConfig.Auth.ClientKey`
- `NuraDeviceConfig.DeviceKey`
- `ConnectedNuraDevice.DeviceConfig.GetDeviceKeyHex()`

It is fine to log whether a key is present, which provisioning reason applies, and which high-level operation is running. Do not print the raw key/token values in sample apps, support logs, screenshots, or telemetry.

## Console sample app

`NuraApp` is a small console/TUI sample that demonstrates the live SDK integration path without WPF:

```powershell
dotnet run --project .\NuraApp\NuraApp.csproj
```

The sample intentionally keeps all UI code in the app layer. It uses `NuraLib` only for auth, device discovery, provisioning, refresh, and monitoring.

The selected-device display is capability-aware. It shows device family, profile, battery percentage, firmware/key/session status, and only the controls or feature chips that apply to the selected headset family and firmware.

The important sequence is:

1. load `nura-config.json` with `NuraConfigStore.LoadOrCreate`
2. construct `NuraClient`
3. persist `client.State.Configuration` whenever `RequestStateSave` fires
4. resume stored auth if possible, otherwise prompt for email-code login
5. subscribe to `client.Monitoring.DeviceConnected` and `DeviceDisconnected`
6. start connection monitoring with `client.Monitoring.StartAsync()`
7. for each connected device, provision if `ProvisioningRequired` is true
8. call `RefreshAsync()` once a persistent device key is available
9. call `StartMonitoringAsync()` to keep cached state updated from headset indications
10. on shutdown, call `client.Monitoring.StopAsync()`, then call `StopMonitoringAsync()` for every connected device with `IsMonitoring` or `HasLocalSession`

The sample uses `Environment.CurrentDirectory\nura-config.json` for convenience. Production apps should usually store config under an app-owned location such as `%LOCALAPPDATA%`, an encrypted settings store, or the host application's normal profile directory.

The sample also shows two event patterns:

- use fine-grained events such as `State.AncChanged` when the host wants targeted updates
- use `ConnectedNuraDevice.Changed` when the host wants one broad invalidation signal and will re-read cached values itself

If you copy the event-handler shape into a production app, wrap async event-handler bodies in a try/catch and log failures. .NET events do not observe returned tasks, so an uncaught exception from an `async void` event handler can become process-level failure.

## Host responsibilities

If you are integrating `NuraLib` into an application, the host owns:

- config-file path selection
- save timing and persistence
- account login UI
- error presentation
- device selection UI
- lifecycle management around refresh, monitoring, and shutdown

`NuraLib` owns:

- auth API calls
- Bluetooth discovery
- provisioning orchestration
- local session handshake
- protocol encoding/decoding
- capability resolution
- state/config/profile abstractions

## What is public API vs internal API

Use the public entry points:

- `NuraClient`
- `NuraAuthManager`
- `NuraDeviceManager`
- `ConnectedNuraDevice`
- `NuraDeviceState`
- `NuraProfiles`
- `NuraDeviceConfiguration`
- the public model and enum types under `NuraLib.Devices`, `NuraLib.Configuration`, and `NuraLib.Logging`

The low-level protocol classes under `src/NuraLib/Protocol/Commands` are internal implementation details, not supported SDK surface.

The same applies to transport classes such as:

- `RfcommHeadsetTransport`
- `BluetoothDeviceProbe`

They are how the library works today, not the official host integration surface.

## Common end-to-end example

This is the standard host flow:

```csharp
using NuraLib;
using NuraLib.Configuration;
using NuraLib.Devices;

var configPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "MyApp",
    "nura-config.json");

var config = NuraConfigStore.LoadOrCreate(configPath);
var client = new NuraClient(new NuraConfigState(config));

client.RequestStateSave += (_, _) =>
{
    NuraConfigStore.Save(configPath, client.State.Configuration);
};

client.OnLog += (_, args) =>
{
    Console.WriteLine($"[{args.Level}] {args.Source}: {args.Message}");
};

if (client.Auth.HasStoredCredentials && await client.Auth.HasValidSessionAsync())
{
    await client.Auth.ResumeAsync();
}

await client.Devices.RefreshAsync();

var device = client.Devices.Connected.FirstOrDefault()
    ?? throw new InvalidOperationException("No connected Nura headset found.");

if (await device.RequiresProvisioningAsync())
{
    var result = await device.EnsureProvisionedAsync();
    if (!result.Success)
    {
        throw new InvalidOperationException($"Provisioning failed: {result.Error}");
    }
}

await device.ConnectLocalAsync();
await device.RefreshAsync();

Console.WriteLine($"Device: {device.Info.TypeName}");
Console.WriteLine($"Serial: {device.Info.Serial}");
Console.WriteLine($"Profile: {device.Profiles.ProfileId}");
Console.WriteLine($"ANC: {device.State.Anc?.Mode}");

if (device.Info.Supports(NuraAudioCapabilities.Anc))
{
    await device.State.SetAncEnabledAsync(true);
}
```

## Practical gotchas

### 1. Persist state whenever the library asks

If `RequestStateSave` is ignored, the following state will be lost:

- auth session state
- device inventory
- provisioned device keys

In that case, failures will appear as missing session recovery or repeated provisioning, even though the root cause is host-side persistence.

### 2. Provisioning is not optional for fresh devices

If a connected device does not have a persistent key yet, local control will not work until the provisioning path completes successfully.

### 3. Capability-gate your UI and workflows

Not every device supports the same feature set.

Examples:

- NuraLoop supports dial configuration
- NuraTrue Pro supports spatial and ProEQ capability flags
- classic Nuraphone behavior differs around kickit/immersion transport

Use `device.Info.Supports(...)` before surfacing controls.

### 4. Some APIs are intentionally stubbed

Methods that are not wired yet throw `NotImplementedException`.

Plan for that explicitly.

### 5. StopMonitoringAsync also closes the local session

If you are treating monitoring as "just event subscription," this is easy to miss. Right now it is also the public session teardown path.

## Current implementation status

Implemented and usable:

- auth email-code request and verification
- auth session resume/validation
- Bluetooth discovery of connected Nura devices
- provisioning flow and persistent key storage
- local encrypted handshake
- profile read and profile selection
- profile-name read
- current-profile Bluetooth visualisation read
- cached multi-profile visual metadata from authenticated online session payloads
- ANC state read/write
- ANC level read/write
- global ANC read/write
- personalisation mode read/write
- immersion read/write on supported transport paths
- spatial read/write
- touch-button read/write
- dial read/write
- voice prompt gain write
- connection monitoring
- indication monitoring

Present but not implemented:

- profile rename
- head detection read/write
- manual head detection read/write
- multipoint read/write
- voice prompt gain read
- ProEQ read/write

## Suggested integration architecture

For most host applications, a clean structure is:

1. a persistence service around `NuraConfigStore`
2. a singleton `NuraClient`
3. an auth service that wraps `client.Auth`
4. a device service that wraps `client.Devices`
5. a selected-device model that holds one `ConnectedNuraDevice`
6. optional background monitoring using `client.Monitoring`

This keeps lifecycle management in the host while leaving protocol and device behavior inside `NuraLib`.

## GUI-ready architecture

For graphical clients, a practical structure is:

1. an app-level `NuraClient` singleton
2. a persistence service that loads and saves `NuraConfig`
3. an auth coordinator that wraps login, resume, and auth-status state
4. a device inventory view model backed by `client.Devices`
5. a selected-device session view model backed by one `ConnectedNuraDevice`
6. a log sink that subscribes to `client.OnLog`
7. a UI dispatcher helper that marshals SDK callbacks to the UI thread

The boundary is:

- `NuraLib` owns protocol, auth, provisioning, transport, and device operations
- your GUI layer owns view state, command enablement, threading, retries, and persistence timing

If the host also supports a non-live preview mode, keep that as a separate bootstrap path that constructs sample UI state without creating `NuraClient`.

### View-model split

For a desktop UI, these are the minimum useful models:

- `AppShellViewModel`
  - owns startup, shutdown, global busy state, and navigation
- `AuthViewModel`
  - owns email, verification code input, login state, and session resume
- `DevicesViewModel`
  - owns the discovered device list and selected device
- `DeviceSessionViewModel`
  - owns one `ConnectedNuraDevice`, its refresh cycle, and monitoring
- `AudioViewModel`
  - binds ANC, passthrough, immersion, personalisation, and spatial controls
- `ProfilesViewModel`
  - binds active profile and profile-name list
- `ConfigurationViewModel`
  - binds touch buttons, dial config, and voice prompt gain

This separation keeps SDK calls and UI state from collapsing into a single long-lived controller.

## UI threading guidance

`NuraLib` is async-first, but it is not a UI framework. Events may arrive on a non-UI thread.

Anything that updates bound UI state belongs on the framework dispatcher.

Typical rule:

- SDK call on background thread: fine
- event received from SDK: treat as background thread
- property update on a bound view model: dispatch to UI thread

Pseudo-pattern:

```csharp
connected.State.AncChanged += async (_, args) =>
{
    await _dispatcher.InvokeAsync(() =>
    {
        AncModeText = args.Current?.Mode.ToString() ?? "Unknown";
    });
};
```

Without this, cross-thread exceptions are likely in WPF, WinUI, Avalonia with strict bindings, or any equivalent observable-object layer.

## Startup sequence for a GUI app

Startup sequence:

1. load config with `NuraConfigStore.LoadOrCreate`
2. create `NuraClient`
3. subscribe to `RequestStateSave`
4. subscribe to `OnLog`
5. if stored credentials exist, call `HasValidSessionAsync()`
6. if valid, call `ResumeAsync()`
7. call `Devices.RefreshAsync()`
8. populate the device list UI
9. optionally start `client.Monitoring.StartAsync()` if you want background connect/disconnect updates

Concrete example:

```csharp
public async Task InitializeAsync()
{
    var config = NuraConfigStore.LoadOrCreate(_configPath);
    Client = new NuraClient(new NuraConfigState(config));

    Client.RequestStateSave += (_, _) =>
    {
        NuraConfigStore.Save(_configPath, Client.State.Configuration);
    };

    Client.OnLog += (_, args) =>
    {
        AppendLog(args);
    };

    if (Client.Auth.HasStoredCredentials && await Client.Auth.HasValidSessionAsync())
    {
        try
        {
            await Client.Auth.ResumeAsync();
        }
        catch
        {
            // Leave the UI signed out and let the user log in again.
        }
    }

    await Client.Devices.RefreshAsync();
    Devices.ReplaceFrom(Client.Devices.All);
}
```

## Device selection flow

For a GUI, a typical selection flow is:

1. user selects a device from `client.Devices.All`
2. if the device is not a `ConnectedNuraDevice`, show it as known-but-not-connected
3. if it is connected, create a `DeviceSessionViewModel`
4. if provisioning is required, prompt for login or run provisioning
5. once provisioned, connect locally
6. refresh state/config/profile values
7. start device monitoring

Pseudo-flow:

```csharp
public async Task SelectDeviceAsync(NuraDevice device)
{
    if (device is not ConnectedNuraDevice connected)
    {
        SelectedDevice = null;
        StatusText = "Device is known but not currently connected.";
        return;
    }

    var sessionVm = new DeviceSessionViewModel(connected, _dispatcher);
    await sessionVm.InitializeAsync();
    SelectedDevice = sessionVm;
}
```

Inside `DeviceSessionViewModel.InitializeAsync()`:

```csharp
public async Task InitializeAsync()
{
    if (await _device.RequiresProvisioningAsync())
    {
        if (!_client.Auth.HasStoredCredentials)
        {
            throw new InvalidOperationException("Device requires provisioning and no authenticated session is available.");
        }

        var provisioning = await _device.EnsureProvisionedAsync();
        if (!provisioning.Success)
        {
            throw new InvalidOperationException($"Provisioning failed: {provisioning.Error}");
        }
    }

    await _device.ConnectLocalAsync();
    await _device.RefreshAsync();
    HookEvents();
    await _device.StartMonitoringAsync();
    PullCachedValues();
}
```

## Refresh strategy for GUI apps

Refresh calls should not be issued from every button handler.

Pattern:

- on initial device selection: `RefreshAsync()`
- after a write operation: trust the write result first, then selectively re-read if needed
- while device monitoring is active: rely on indications for ongoing updates when possible
- on reconnect or resume: call `RefreshAsync()` again

A good compromise:

- `RefreshAsync()` once when entering the device page
- use specific reads for values that are known to be device-dependent after writes
- avoid a global refresh after every toggle

Examples:

- after `SetAncEnabledAsync(true)`, update your UI from the SDK state immediately
- after `SetProfileIdAsync(1)`, you may want to re-read ANC and personalisation state because the library itself already does that internally for some paths

## Error-handling guidance

The examples in this guide use exceptions directly. In a GUI, errors benefit from consistent categorization and presentation.

The common categories are:

### 1. Recoverable user-state errors

Examples:

- no device connected
- device requires provisioning
- user is not logged in
- requested feature is unsupported

UI treatment:

- show inline guidance or a dialog
- keep the app usable
- do not treat these as fatal

### 2. Expected implementation gaps

Examples:

- `NotImplementedException` from head detection, multipoint, ProEQ, profile rename, or voice-prompt-gain retrieval

UI treatment:

- disable the feature proactively using capability and implementation checks
- if reached anyway, show "Not implemented in this SDK build"

### 3. Operational failures

Examples:

- Bluetooth socket closed
- provisioning backend rejected the request
- session resume failed
- RFCOMM handshake failed

UI treatment:

- show a non-cryptic error
- log the underlying exception
- allow retry
- for device errors, reset the selected device session view model

### 4. Programming errors

Examples:

- null references in your host
- invalid binding assumptions
- thread-affinity violations

UI treatment:

- log and crash in development
- log and fail fast with a controlled error surface in production

### Wrapper pattern

Wrap each UI command in one error boundary:

```csharp
private async Task RunDeviceActionAsync(Func<Task> action)
{
    try
    {
        IsBusy = true;
        ErrorText = null;
        await action();
    }
    catch (NotSupportedException ex)
    {
        ErrorText = ex.Message;
    }
    catch (NotImplementedException ex)
    {
        ErrorText = $"Feature not implemented: {ex.Message}";
    }
    catch (InvalidOperationException ex)
    {
        ErrorText = ex.Message;
    }
    catch (Exception ex)
    {
        ErrorText = "Unexpected device error.";
        AppendExceptionToLog(ex);
    }
    finally
    {
        IsBusy = false;
    }
}
```

## Shutdown sequence

Shutdown sequence:

1. stop connection monitoring if it was started
2. stop monitoring/local sessions on every connected device that was started
3. save config one final time
4. let the process exit

Pseudo-code:

```csharp
public async Task ShutdownAsync()
{
    try
    {
        await Client.Monitoring.StopAsync();
    }
    catch
    {
    }

    foreach (var device in Client.Devices.All.OfType<ConnectedNuraDevice>().ToArray())
    {
        if (!device.IsMonitoring && !device.HasLocalSession)
        {
            continue;
        }

        try
        {
            await device.StopMonitoringAsync();
        }
        catch
        {
        }
    }

    NuraConfigStore.Save(_configPath, Client.State.Configuration);
}
```

`DeviceSessionViewModel.DisposeAsync()` can stop device monitoring:

```csharp
public async ValueTask DisposeAsync()
{
    try
    {
        await _device.StopMonitoringAsync();
    }
    catch
    {
    }
}
```

## GUI-specific command patterns

The following command patterns map well onto a UI layer:

### Toggle ANC

```csharp
public Task ToggleAncAsync() => RunDeviceActionAsync(async () =>
{
    var current = _device.State.AncEnabled ?? false;
    await _device.State.SetAncEnabledAsync(!current);
    AncEnabled = _device.State.AncEnabled ?? !current;
});
```

### Change active profile

```csharp
public Task SelectProfileAsync(int profileId) => RunDeviceActionAsync(async () =>
{
    await _device.Profiles.SetProfileIdAsync(profileId);
    CurrentProfileId = _device.Profiles.ProfileId;
});
```

### Change immersion

```csharp
public Task SetImmersionAsync(NuraImmersionLevel level) => RunDeviceActionAsync(async () =>
{
    await _device.State.SetImmersionLevelAsync(level);
    ImmersionLevel = _device.State.ImmersionLevel;
});
```

### Update button bindings

```csharp
public Task SetLeftSingleTapAsync(NuraButtonFunction function) => RunDeviceActionAsync(async () =>
{
    var current = await _device.Configuration.RetrieveTouchButtonsAsync()
        ?? new NuraButtonConfiguration();

    var updated = current.WithBinding(
        NuraButtonSide.Left,
        NuraButtonGesture.SingleTap,
        function);

    await _device.Configuration.SetTouchButtonsAsync(updated);
    TouchButtons = updated;
});
```

## Minimal MVVM-style integration example

This example is framework-neutral and shows the expected shape of a host-side device session model.

```csharp
public sealed class DeviceSessionViewModel
{
    private readonly ConnectedNuraDevice _device;
    private readonly IUiDispatcher _dispatcher;

    public DeviceSessionViewModel(ConnectedNuraDevice device, IUiDispatcher dispatcher)
    {
        _device = device;
        _dispatcher = dispatcher;
    }

    public string DeviceName => _device.Info.TypeName;
    public string Serial => _device.Info.Serial;

    public bool IsBusy { get; private set; }
    public string? ErrorText { get; private set; }

    public int? CurrentProfileId { get; private set; }
    public NuraAncMode? AncMode { get; private set; }
    public bool? SpatialEnabled { get; private set; }
    public NuraPersonalisationMode? PersonalisationMode { get; private set; }
    public NuraImmersionLevel? ImmersionLevel { get; private set; }

    public async Task InitializeAsync()
    {
        await _device.ConnectLocalAsync();
        await _device.RefreshAsync();

        await _dispatcher.InvokeAsync(() =>
        {
            PullFromCachedSdkState();
        });

        _device.Profiles.ProfileIdChanged += OnProfileIdChanged;
        _device.State.AncChanged += OnAncChanged;
        _device.State.SpatialEnabledChanged += OnSpatialChanged;
        _device.State.PersonalisationModeChanged += OnModeChanged;
        _device.State.ImmersionLevelChanged += OnImmersionChanged;

        await _device.StartMonitoringAsync();
    }

    private void PullFromCachedSdkState()
    {
        CurrentProfileId = _device.Profiles.ProfileId;
        AncMode = _device.State.Anc?.Mode;
        SpatialEnabled = _device.State.SpatialEnabled;
        PersonalisationMode = _device.State.PersonalisationMode;
        ImmersionLevel = _device.State.ImmersionLevel;
    }

    private async void OnProfileIdChanged(object? sender, NuraValueChangedEventArgs<int?> args)
    {
        await _dispatcher.InvokeAsync(() => CurrentProfileId = args.Current);
    }

    private async void OnAncChanged(object? sender, NuraValueChangedEventArgs<NuraAncState> args)
    {
        await _dispatcher.InvokeAsync(() => AncMode = args.Current?.Mode);
    }

    private async void OnSpatialChanged(object? sender, NuraValueChangedEventArgs<bool?> args)
    {
        await _dispatcher.InvokeAsync(() => SpatialEnabled = args.Current);
    }

    private async void OnModeChanged(object? sender, NuraValueChangedEventArgs<NuraPersonalisationMode?> args)
    {
        await _dispatcher.InvokeAsync(() => PersonalisationMode = args.Current);
    }

    private async void OnImmersionChanged(object? sender, NuraValueChangedEventArgs<NuraImmersionLevel?> args)
    {
        await _dispatcher.InvokeAsync(() => ImmersionLevel = args.Current);
    }
}
```

`IUiDispatcher` is your abstraction, not part of `NuraLib`.

Example shape:

```csharp
public interface IUiDispatcher
{
    Task InvokeAsync(Action action);
}
```

## Capability-driven UI guidance

The safest way to build the UI is to derive control visibility and enablement from `device.Info`.

Examples:

- show ANC toggle only if `device.Info.Supports(NuraAudioCapabilities.Anc)`
- show ANC level slider only if `device.Info.Supports(NuraAudioCapabilities.AncLevel)`
- show Spatial toggle only if `device.Info.Supports(NuraAudioCapabilities.Spatial)`
- show Dial config only if `device.Info.Supports(NuraInteractionCapabilities.Dial)`
- show button remapping only if `device.Info.Supports(NuraInteractionCapabilities.TouchButtons)`
- show profile list only if `device.Info.Supports(NuraSystemCapabilities.Profiles)`

Control visibility and enablement are best driven by capability checks rather than device-name string matching.

## Feature-completeness guidance for a GUI

For a predictable UI:

- expose implemented features normally
- hide or clearly mark not-yet-implemented features
- route unsupported features through capability checks before rendering controls
- log all operational failures to your in-app log surface

That baseline is usually enough for a usable front end.

## Reference files

Core entry point:

- `src/NuraLib/NuraClient.cs`

Persistence:

- `src/NuraLib/Configuration/NuraConfig.cs`
- `src/NuraLib/Configuration/NuraConfigStore.cs`
- `src/NuraLib/NuraConfigState.cs`

Auth:

- `src/NuraLib/Auth/NuraAuthManager.cs`

Devices:

- `src/NuraLib/Devices/NuraDeviceManager.cs`
- `src/NuraLib/Devices/ConnectedNuraDevice.cs`
- `src/NuraLib/Devices/NuraDeviceInfo.cs`
- `src/NuraLib/Devices/NuraDeviceCapabilities.cs`

State/config/profile surfaces:

- `src/NuraLib/Devices/State/NuraDeviceState.cs`
- `src/NuraLib/Devices/State/NuraProfiles.cs`
- `src/NuraLib/Devices/Configuration/NuraDeviceConfiguration.cs`

Monitoring:

- `src/NuraLib/Monitoring/NuraMonitoringManager.cs`

Tests that show the currently supported low-level command set:

- `tests/NuraLib.Tests/Program.cs`
