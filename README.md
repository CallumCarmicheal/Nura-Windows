# Nura Desktop App

This folder contains two related .NET projects:

- `NuraDesktopConsole`
  - the Windows reverse-engineering and validation harness
- `NuraLib`
  - the reusable library being written for use with a graphical interface later or for use with other applications.

The practical goal is to recreate enough of the Nura app for Windows use:

- recover the Bluetooth and crypto behavior used by the official app
- automate the backend-assisted bootstrap while the API still exists
- recover and persist the long-lived per-device key
- use that key for ongoing local encrypted control without depending on the official app

## Current State

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

### NuraLib

Implemented so far:

- config/auth/device models
- session crypto/runtime helpers
- generic device model with:
  - `DeviceType`
  - capability rules
  - cached device state
  - cached device configuration
  - cached profile state
- device-owned change events
- development-only `[BluetoothImplementationRequired]` markers on Bluetooth-facing stubs

Current limitation:

- most Bluetooth-facing `NuraLib` operations are still scaffolded and intentionally throw `NotImplementedException`
- the real transport and command implementations still live in `NuraDesktopConsole`

## Repository Layout

- `src/NuraDesktopApp`
  - `NuraDesktopConsole` source
- `src/NuraLib`
  - reusable library source
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

## Build

Build the console app:

```powershell
dotnet build .\src\NuraDesktopApp\NuraDesktopConsole.csproj -v minimal
```

Build the library:

```powershell
dotnet build .\src\NuraLib\NuraLib.csproj -v minimal
```

Build both through the solution wrapper:

```powershell
dotnet build .\NuraDesktopApp.slnx --configfile .\NuGet.Config
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

Older console flows used this more directly. The long-term direction is:

- store durable device information here
- especially the persistent per-device key
- do not treat bootstrap session state as durable config

Example:

```json
{
  "deviceAddress": "00:00:00:00:00:00",
  "serialNumber": 0,
  "currentProfileId": 0,
  "deviceKeyHex": "REDACTED_DEVICE_KEY",
  "sessionNonceHex": "REDACTED_SESSION_NONCE"
}
```

Notes:

- `deviceKeyHex` is the persistent per-headset key
- long-term local control depends on this key, not on a backend-provided session nonce
- a fresh nonce can be generated locally when opening a new encrypted local session

### `nura-auth.json`

This stores auth and bootstrap state used by the console harness.

It can contain things like:

- auth headers
- `asid`
- `usid`
- active backend bootstrap state
- recovered `app_enc.key` and `app_enc.nonce`

For `NuraLib`, this file is not the intended final public config model. It is primarily part of the current test harness workflow.

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

Treat log files as sensitive. They may contain:

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
- keep backend use limited to one-time bootstrap or recovery while the API still exists
- rely on the recovered persistent device key for normal ongoing local control

## License

Licensed under Apache License 2.0. See [LICENSE.md](./LICENSE.md) and [NOTICE](./NOTICE).
