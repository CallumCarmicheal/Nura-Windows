# NuraDesktop

`NuraDesktop` is the WPF companion app for Nura devices. It keeps the existing compact/expanded popup shell, profile visualisation, window anchoring, and settings UI, while using `NuraLib` as the source of truth for live device state.

## Current architecture

- `MainViewModel` owns the shell state: selected device, page navigation, authentication, export actions, profile animation, and UI app settings.
- GUI state is saved to `settings.gui.json`; NuraLib auth/device state is saved separately to `settings.nura.json`.
- `NuraDeviceViewModel` wraps either a demo device or a live `ConnectedNuraDevice`.
- Live device state is refreshed from SDK events and copied into bindable presentation properties.
- Live WPF device wrappers subscribe to `ConnectedNuraDevice.Changed`; the fine-grained SDK events already raise `Changed`, so subscribing to both will duplicate UI refresh work.
- SDK writes must go through named async commands or explicit `NuraDeviceViewModel.Apply*Async` methods.
- XAML controls should not write directly to a live headset through generic `TwoWay` bindings.

## Live device flow

1. The app starts in demo or live mode through the bootstrap layer.
2. Live mode resumes a stored Nura session when possible, or shows the authentication gate.
3. Device discovery is handled by `NuraClient.Devices` and `NuraClient.Monitoring`.
4. Automatic device setup is enabled by default and can be toggled from the authentication and settings pages.
5. When automatic setup is enabled, connected devices are provisioned when allowed, opened locally, refreshed, and monitored.
6. The live-control panel still exposes manual connect, provision, refresh, and battery actions when automatic setup is disabled or fails.
7. Device state changes are received through SDK events and marshalled back onto the WPF dispatcher.

## Binding rules

- Display state can bind one-way to `CurrentDevice`.
- Mutating controls should use `ICommand` paths such as ANC, passthrough, spatial, immersion, battery refresh, and apply buttons.
- Sliders and ComboBoxes may edit draft state, but live SDK writes require an Apply button.
- Touch-button and dial remapping use draft/apply/reset behavior because immediate writes are too easy to trigger accidentally.
- Capability flags hide unsupported controls rather than showing disabled or misleading UI.
- Only disconnected control groups blur or disable through `ShouldBlurCurrentDeviceControls`. Connected controls remain interactive while operations queue and show pending feedback instead.

## Current feature set

- Compact and expanded popup shells with animated transitions.
- Window placement modes for anchor edge, taskbar, and remembered position.
- Email authentication with offline/local-key fallback.
- Live device discovery and connection status through `NuraLib`.
- Optional automatic setup for newly discovered devices, enabled by default.
- Provisioning prompts for devices that require a Nura session or missing local keys.
- Battery display and manual battery refresh where the device supports local reads.
- Capability-aware controls for ANC, passthrough, ANC level, spatial audio, immersion, personalisation, touch buttons, and dial bindings.
- Hearing-profile rendering with bitmap and retained-mode shape paths.
- Transparent PNG export for available device/profile combinations.
- Pending device controls: queued/applying changes update the UI immediately, then reconcile with confirmed headset state.
- Exit confirmation from the shell control, preventing accidental application closes.

## Build

```bash
dotnet build src/NuraDesktop/NuraDesktop.csproj
```

Run from the repository root:

```bash
dotnet run --project src/NuraDesktop/NuraDesktop.csproj
```

## Notes

- The project targets `net10.0-windows`.
- Firmware/language transfer operations are intentionally not exposed in WPF.
- New device controls should first be implemented and verified in `NuraLib`, then surfaced through capability-gated WPF commands.
