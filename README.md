# Nura Desktop App

This folder contains four related .NET projects:

- `NuraLib`
  - the reusable Windows SDK surface for auth, discovery, provisioning, local encrypted control, state, profiles, configuration, and monitoring
- `NuraApp`
  - a small console/TUI sample app that demonstrates normal host integration with `NuraLib`
- `NuraDesktopConsole`
  - the Windows reverse-engineering and validation harness
- `NuraPopupWpf`
  - an experimental graphical client consuming the live SDK model

The practical goal is to recreate enough of the Nura app for Windows use:

- recover the Bluetooth and crypto behavior used by the official app
- automate the backend-assisted bootstrap while the API still exists
- recover and persist the long-lived per-device key
- use that key for ongoing local encrypted control without depending on the official app

## Current State

### NuraLib

Implemented and working:

- config/auth/device models with host-managed persistence
- email-code authentication and auth session resume
- Bluetooth discovery of connected Nura devices
- backend-assisted provisioning to recover and persist the long-lived per-device key
- local RFCOMM encrypted session setup using the persistent key
- cached device state, profile state, and configuration surfaces
- device-owned fine-grained events plus aggregate `Changed`
- connection-level monitoring through `NuraClient.Monitoring`
- per-device indication monitoring through `ConnectedNuraDevice.StartMonitoringAsync()`
- provisioning requirement reasons for missing device keys and host-marked NuraNow refreshes
- classic Nuraphone profile/state behavior aligned with the decompiled Android app where currently mapped

Current limitations:

- several public API surfaces are intentionally not implemented yet, including profile rename, head detection, multipoint, voice prompt gain readback, and ProEQ
- device-family coverage is still being expanded from the confirmed Nuraphone path
- backend-assisted provisioning remains necessary if the host does not already have a persistent device key

### NuraApp

`NuraApp` is the easiest live sample to run when validating SDK behavior as a host application.

It demonstrates:

- loading/saving `nura-config.json`
- auth resume and email-code login fallback
- connection monitoring
- provisioning devices when required
- refreshing initial cached state
- starting device-level indication monitoring
- shutting down connection polling and all active device sessions

Run it with:

```powershell
dotnet run --project .\src\NuraApp\NuraApp.csproj
```

### NuraDesktopConsole

Implemented and working:

- Windows RFCOMM/SPP connection to Nuraphone over-ears
- GAIA packet framing and parsing
- backend auth flow with:
  - `app/session`
  - `auth/validate_token`
  - email + code login fallback
- full backend bootstrap chain through:
  - `session/start`
  - `session/start_1`
  - `session/start_2`
  - `session/start_3`
  - `session/start_4`
- recovery of:
  - `asid`
  - `usid`
  - persistent device key via `app_enc.key`
- local encrypted control once the device key is known
- safe ANC toggle / restore test

Confirmed important result:

- `session/start_4` returns `app_enc.key`
- that key is the same long-lived persistent per-device key used for later offline/local control

### NuraPopupWpf

`NuraPopupWpf` is the experimental GUI client. It is useful for validating view-model and binding behavior, but `NuraApp` is the simpler reference for SDK host flow.

For SDK usage details, read `docs/SDK-Guide.md`.

## Repository Layout

- `NuraApp`
  - console/TUI sample app using `NuraLib`
- `src/NuraLib`
  - reusable library source
- `src/NuraDesktopApp`
  - `NuraDesktopConsole` source
- `src/NuraPopupWpf`
  - experimental WPF client source
- `tests/NuraLib.Tests`
  - lightweight packet and library behavior tests
- `docs/SDK-Guide.md`
  - public SDK integration guide
- `NuraDesktopApp.slnx`
  - solution file
- `NuGet.Config`
  - local NuGet config
- `logs`
  - session logs written by `NuraDesktopConsole`
- `nura-config.json`
  - local device config, not committed
- `nura-auth.json`
  - local auth/bootstrap state, not committed

## Build And Run

Build everything:

```powershell
dotnet build .\NuraDesktopApp.slnx --configfile .\NuGet.Config
```

Run the console/TUI sample app:

```powershell
dotnet run --project .\src\NuraApp\NuraApp.csproj
```

Build the console app:

```powershell
dotnet build .\src\NuraDesktopApp\NuraDesktopConsole.csproj -v minimal
```

Build the library:

```powershell
dotnet build .\src\NuraLib\NuraLib.csproj -v minimal
```

Run the library tests:

```powershell
dotnet run --project .\tests\NuraLib.Tests\NuraLib.Tests.csproj
```

If your environment needs local `dotnet` state inside the repo, use:

```powershell
$env:DOTNET_CLI_HOME="$PWD\.dotnet_home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:NUGET_PACKAGES="$PWD\.nuget\packages"
$env:APPDATA="$PWD\.appdata"
```

## Configuration

### `nura-config.json`

This stores durable local device information.

`NuraLib` and `NuraApp` use this as the durable host-owned config file.

It stores:

- auth state
- discovered device inventory
- persistent per-device keys
- host-managed metadata such as `IsNuraNowDevice` and `LastProvisionedUtc`

Do not treat transient bootstrap sessions as durable config.

Example:

```json
{
  "apiBase": "https://api-p3.nuraphone.com/",
  "uuid": "0a927987-c8ed-4bda-af94-2fb6d4836798",
  "auth": {
    "userEmail": "user@example.com",
    "authUid": "user@example.com",
    "accessToken": "REDACTED_ACCESS_TOKEN",
    "clientKey": "REDACTED_CLIENT_KEY",
    "tokenType": "Bearer",
    "tokenExpiryUnix": 1770000000
  },
  "devices": [
    {
      "type": "Nuraphone",
      "deviceAddress": "00:00:00:00:00:00",
      "deviceSerial": "12345678",
      "friendlyName": "nuraphone 123",
      "firmwareVersion": 606,
      "maxPacketLengthHint": 182,
      "isNuraNowDevice": false,
      "lastProvisionedUtc": "2026-06-17T12:00:00.0000000+00:00",
      "deviceKey": "REDACTED_BASE64_DEVICE_KEY"
    }
  ]
}
```

Notes:

- `deviceKey` is the persistent per-headset key
- long-term local control depends on this key, not on a backend-provided session nonce
- a fresh nonce can be generated locally when opening a new encrypted local session
- auth tokens and device keys are sensitive and should not be printed in logs or screenshots

### `nura-auth.json`

This stores auth and bootstrap state used by the console harness.

It can contain things like:

- auth headers
- `asid`
- `usid`
- active backend bootstrap state
- recovered `app_enc.key` and `app_enc.nonce`

For `NuraLib`, this file is not the intended final public config model. It is primarily part of the current test harness workflow.

## NuraApp Sample

Run the sample with:

```powershell
dotnet run --project .\src\NuraApp\NuraApp.csproj
```

Expected flow:

1. loads or creates `nura-config.json`
2. resumes stored auth if available
3. prompts for email-code login if needed
4. starts connection monitoring
5. provisions connected devices if required
6. refreshes cached state and starts device-level monitoring
7. shuts down connection polling and all active device sessions on exit

The sample stores config in the current working directory for convenience. Production hosts should use an app-owned location such as `%LOCALAPPDATA%` or an encrypted settings store.

## NuraDesktopConsole Commands

Run commands with:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopConsole.csproj -- <command> [subcommand]
```

### Main Command Groups

- `probe ...`
  - connected-device discovery and unencrypted hardware info
- `protocol ...`
  - packet and crypto helpers
- `headset ...`
  - live headset tests and local-control validation
- `auth ...`
  - backend auth and bootstrap helpers
- `flow ...`
  - higher-level chained workflows

### Common Commands

List connected Nuraphone devices:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopConsole.csproj -- probe devices
```

Read unencrypted serial / firmware metadata:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopConsole.csproj -- probe hw-info
```

Run the fresh startup/bootstrap flow to `session/start_3`:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopConsole.csproj -- flow init-to-start3
```

Continue the backend bootstrap:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopConsole.csproj -- auth session-start-next
```

Run the ANC toggle validation:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopConsole.csproj -- headset anc-toggle-test
```

## Logging

Every `NuraDesktopConsole` run creates a timestamped log file in `logs`.

`NuraApp` logs to the console.

Treat all logs as sensitive. They may contain:

- headset identifiers
- device keys
- access tokens
- email addresses
- user and app session identifiers

## Safety

The console app is intentionally a reverse-engineering harness, not yet a polished end-user controller.

Use caution with:

- unknown setters
- firmware-related commands
- unexplored command families
- anything that writes to the headset outside currently understood control flows

Current safe starting points:

- `NuraApp`
- `probe devices`
- `probe hw-info`
- `flow init-to-start3`
- `headset anc-toggle-test`

## Direction

Short-term:

- keep `NuraDesktopConsole` as the experimentation and packet-analysis harness
- continue porting stable pieces into `NuraLib`

Long-term:

- use `NuraLib` as the public Windows integration surface
- keep `NuraApp` as the minimal SDK sample and smoke-test host
- keep backend use limited to one-time bootstrap or recovery while the API still exists
- rely on the recovered persistent device key for normal ongoing local control

## License

Licensed under Apache License 2.0. See [LICENSE.md](./LICENSE.md) and [NOTICE](./NOTICE).
