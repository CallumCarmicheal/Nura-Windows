# NuraPopupWpf

`NuraPopupWpf` is a WPF desktop companion-app for Nura devices. The project focuses on being popup shell that is opened from the tray area or a hotkey, hearing-profile visualisation, device/settings flows, anchored window behaviour, and some nice interactions.

## Current feature set

These are the implemented features at the time of writing this README as its not wired up to the in-dev version of NuraLib.

- Compact and expanded popup shells with animated transitions.
- Window placement modes for:
  - `Anchor Edge`
  - `Taskbar`
  - `Remember last position`
- A 3x3 anchor-edge picker and remembered-position expand behaviour (`Based on position`, `Left`, `Right`).
- MVVM-driven device, profile, auth, and settings state.
- Email + 6-digit authentication flow with an offline/local-key bypass path.
- Per-device listening state and controls, including:
  - profile selection
  - mode switching
  - immersion level
  - ANC
  - Social Mode
  - Connection and warning state
- Hearing-profile rendering with:
  - bitmap rendering
  - retained-mode shape rendering
  - animated profile morphing
  - compact and expanded background haze options
  - selectable expanded band layouts
- Expanded profile selector with animated pill movement and hover preview.
- Device hover previews and overflow drawer behaviour for larger device lists.
- Disconnected-device placeholder artwork with an optional profile preview mode.
- Transparent PNG export for all device/profile combinations.

## Project structure

- `MainWindow.xaml` / `MainWindow.xaml.cs`
  - top-level popup shell, sizing, placement, anchor behaviour, and window animation
- `ViewModels/MainViewModel.cs`
  - main application state, commands, animation coordination, auth flow, export flow, and persisted window preference integration
- `Controls/`
  - shell pages, profile selector controls, auth controls, custom sliders, and the profile visual control
- `Controls/ProfileVisualControl.cs`
  - live hearing-profile renderer host for bitmap and shape render paths
- `Services/HearingProfileExportService.cs`
  - export pipeline for transparent rendered PNGs
- `Services/WindowPreferencesService.cs`
  - load/save logic for persisted anchor and placement settings
- `Models/`
  - device, profile, and window placement models
- `Infrastructure/`
  - MVVM helpers, commands, and slider converters

## Build

```bash
dotnet build src/NuraPopupWpf/NuraPopupWpf.csproj
```

To run the app (from repo root):

```bash
dotnet run --project src/NuraPopupWpf/NuraPopupWpf.csproj
```

## Notes

- The project targets `net10.0-windows`.
