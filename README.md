# NuraPopupWpf

A WPF MVVM prototype of the compact-to-expanded Nura-style popup UI.

## What is included

- Compact popup shell and expanded shell animation.
- MVVM data binding for device selection, profiles, mode, settings, and toggles.
- A custom C# hearing-profile renderer that recreates the Nura-style profile image using a CPU-generated bitmap.
- Animated transitions when switching between Neutral and Personalised, and when switching profiles.

## Project structure

- `ViewModels/MainViewModel.cs` — app state, commands, and animation orchestration.
- `Services/NuraProfileRenderer.cs` — hearing-profile rendering helpers.
- `Models` — device and profile models.
- `Infrastructure` — base MVVM helpers.
- `MainWindow.xaml` — popup layout and state-driven view composition.

## Build

```bash
dotnet build
```

## Notes

- The project targets `net9.0-windows`.
- The renderer is a practical WPF recreation, not a byte-for-byte port of the original native implementation.
