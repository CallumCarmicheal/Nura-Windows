# NuraDesktopApp

`NuraDesktopApp` is a Windows `.NET 8` console application for talking to Nuraphone over-ear headphones directly over RFCOMM/SPP.

Current scope:

- authenticate against the Nuraphone backend using the email + code flow
- perform the local app-crypto handshake from Windows
- read headset state after handshake
- run targeted protocol tests such as ANC mode toggling
- write detailed per-session logs to `logs/`

This folder is intentionally self-contained. It does not depend on the wider reverse-engineering workspace to build or run.

## Status

Implemented and working:

- Windows RFCOMM/SPP connection to the headset
- GAIA packet framing
- app challenge / validate handshake
- authenticated encrypted reads
- fresh random session nonce generation
- email login flow against the Nuraphone backend
- ANC mode read / toggle / restore test

Current known-safe command coverage:

- read deep sleep timeout
- read current profile
- read battery state
- read ANC state
- toggle ANC / passthrough and restore the original state

## Requirements

- Windows with Bluetooth support
- paired Nuraphone headset
- .NET 8 SDK

## Repository Layout

- [src/NuraDesktopApp](/src/NuraDesktopApp): application source
- [NuraDesktopApp.slnx](/NuraDesktopApp.slnx): solution file
- [NuGet.Config](/NuGet.Config): local NuGet config
- [src/NuraDesktopApp/nura-config.sample.json](/src/NuraDesktopApp/nura-config.sample.json): sample headset config
- [logs](/logs): session logs written by the app
- [nura-config.json](/nura-config.json): local headset config, not committed
- [nura-auth.json](/nura-auth.json): local auth state, not committed

## Build

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

dotnet build .\NuraDesktopApp.slnx --configfile .\NuGet.Config
```

## Configuration

Create [nura-config.json](/nura-config.json) in the root. Start from [src/NuraDesktopApp/nura-config.sample.json](/src/NuraDesktopApp/nura-config.sample.json).

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

- `deviceKeyHex` is the persistent per-headset app key.
- `sessionNonceHex` is used by the fixed-nonce handshake commands.
- `tests/fresh-nonce-test` ignores `sessionNonceHex` and generates a fresh random 12-byte nonce locally.

## Logging

Every run creates a timestamped log file in [logs](/logs).

The console output also prints:

- `command.name=...`
- `log.path=...`
- `config.path=...` or `auth.path=...`

Treat log files as sensitive. They may contain:

- headset identifiers
- app keys
- access tokens
- email addresses

## Commands

Run commands with:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopApp.csproj -- <command>
```

### Offline / Protocol Commands

Show the offline bootstrap plan:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopApp.csproj -- tests/plan
```

Build a challenge response from a known headset challenge:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopApp.csproj -- data/respond --challenge-hex 00112233445566778899aabbccddeeff
```

Encrypt a plaintext payload into an app packet:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopApp.csproj -- data/encrypt --payload-hex 0041
```

Parse a GAIA frame:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopApp.csproj -- data/parse --frame-hex ff01000068000000
```

### Live Headset Commands

Connect, handshake, and perform safe authenticated reads:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopApp.csproj -- tests/live-handshake
```

Connect using a fresh random nonce and perform safe authenticated reads:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopApp.csproj -- tests/fresh-nonce-test
```

Read current ANC mode, toggle it, wait 5 seconds, and restore the original state:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopApp.csproj -- tests/anc-toggle-test
```

### Backend Auth Commands

Send the email login code:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopApp.csproj -- auth send-email --email you@example.com
```

Verify the six-digit code:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopApp.csproj -- auth verify-code --email you@example.com --code 123456
```

Validate the stored auth token:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopApp.csproj -- auth validate-token
```

Show the currently stored auth state:

```powershell
dotnet run --project .\src\NuraDesktopApp\NuraDesktopApp.csproj -- auth show-state
```

## Safety

The implemented live commands are intentionally narrow. Do not add or run unknown setters against live hardware casually.

Current recommendation:

- start with `tests/live-handshake`
- use `tests/fresh-nonce-test` to confirm local bootstrap
- use `tests/anc-toggle-test` only after reads are working

Avoid using this tool for:

- firmware or upgrade commands
- unknown debug commands
- pairing database manipulation
- fuzzing unexplored command families

## License

Licensed under Apache License 2.0. See [LICENSE.md](/LICENSE.md) and [NOTICE](/NOTICE).
