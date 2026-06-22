using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using NuraLib;
using NuraLib.Devices;
using NuraLib.Monitoring;
using NuraLib.Rendering;

using NuraDesktop.Bootstrap;
using NuraDesktop.Infrastructure;
using NuraDesktop.Models;

namespace NuraDesktop.ViewModels;

public sealed partial class MainViewModel {
    private readonly ObservableCollection<ButtonFunctionOption> _singleTapButtonOptions = [];
    private readonly ObservableCollection<ButtonFunctionOption> _doubleTapButtonOptions = [];
    private readonly ObservableCollection<ButtonFunctionOption> _tripleTapButtonOptions = [];
    private readonly ObservableCollection<ButtonFunctionOption> _tapAndHoldButtonOptions = [];
    private readonly ObservableCollection<DialFunctionOption> _dialFunctionOptions = [];
    private readonly Dictionary<string, NuraDeviceViewModel> _deviceModelsByIdentity = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _deviceSyncGate = new(1, 1);

    private NuraClient? _client;
    private bool _suppressProfileSelectionApply;
    private bool _isBluetoothMonitoringActive;
    private string _globalStatusText = "Demo devices ready.";
    private StatusTone _globalStatusTone = StatusTone.Neutral;
    private CancellationTokenSource? _globalStatusResetCts;

    public static MainViewModel CreateDemo(PopupDemoSeedData seedData, PopupAppStoragePaths storagePaths) {
        var viewModel = new MainViewModel(storagePaths);
        viewModel.LoadSeedData(seedData);
        return viewModel;
    }

    public static MainViewModel CreateLive(NuraClient client, PopupAppStoragePaths storagePaths) {
        var viewModel = new MainViewModel(storagePaths);
        viewModel._client = client;
        return viewModel;
    }

    public ObservableCollection<ButtonFunctionOption> SingleTapButtonOptions => _singleTapButtonOptions;

    public ObservableCollection<ButtonFunctionOption> DoubleTapButtonOptions => _doubleTapButtonOptions;

    public ObservableCollection<ButtonFunctionOption> TripleTapButtonOptions => _tripleTapButtonOptions;

    public ObservableCollection<ButtonFunctionOption> TapAndHoldButtonOptions => _tapAndHoldButtonOptions;

    public ObservableCollection<DialFunctionOption> DialFunctionOptions => _dialFunctionOptions;

    public ICommand RefreshDevicesCommand { get; private set; } = null!;

    public ICommand RefreshCurrentDeviceCommand { get; private set; } = null!;

    public ICommand ConnectCurrentDeviceCommand { get; private set; } = null!;

    public ICommand ProvisionCurrentDeviceCommand { get; private set; } = null!;

    public bool IsLiveMode => _client is not null;

    public bool IsCurrentDeviceBusy => CurrentDevice.IsBusy;

    public bool CurrentDeviceHasPendingChanges => CurrentDevice.HasPendingChanges;

    public string CurrentDeviceActionText => CurrentDevice.DisplayStatusText;

    public string CurrentDeviceStatusText => CurrentDevice.Id == "__empty__"
        ? "No Nura device selected."
        : $"{CurrentDevice.Name}: {CurrentDevice.DisplayStatusText}";

    public StatusTone CurrentDeviceStatusTone => CurrentDevice.DisplayStatusTone;

    public bool IsCurrentDeviceStatusVisible => CurrentDevice.IsDisplayStatusVisible;

    public string GlobalStatusText {
        get => _globalStatusText;
        private set => SetProperty(ref _globalStatusText, value);
    }

    public StatusTone GlobalStatusTone {
        get => _globalStatusTone;
        private set => SetProperty(ref _globalStatusTone, value);
    }

    public bool ShowCurrentDeviceActionPanel => CurrentDevice.IsLive;

    private void InitializeRuntimeExtensions() {
        RefreshDevicesCommand = new AsyncRelayCommand(async (_, _) => await RefreshDevicesAsync());
        RefreshCurrentDeviceCommand = new AsyncRelayCommand(async (_, _) => await RefreshCurrentDeviceAsync());
        ConnectCurrentDeviceCommand = new AsyncRelayCommand(async (_, _) => await ConnectCurrentDeviceAsync());
        ProvisionCurrentDeviceCommand = new AsyncRelayCommand(async (_, _) => await ProvisionCurrentDeviceAsync());
        RefreshCurrentDeviceBindings();
        OnPropertyChanged(nameof(CurrentDeviceActionText));
        OnPropertyChanged(nameof(CurrentDeviceStatusText));
        OnPropertyChanged(nameof(CurrentDeviceStatusTone));
        OnPropertyChanged(nameof(IsCurrentDeviceStatusVisible));
    }

    public async Task InitializeLiveAsync(bool resumedAuthenticatedSession, string? resumeError, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        if (_client is null) {
            return;
        }

        AttachClientEvents();
        SetGlobalStatus("Discovering paired Nura devices.", StatusTone.Information);

        AuthenticationEmail = _client.State.Configuration.Auth.UserEmail ?? string.Empty;
        HasAuthenticatedWithNura = resumedAuthenticatedSession;
        ConnectToNura = resumedAuthenticatedSession;
        HasCompletedAuthenticationGate = resumedAuthenticatedSession;

        if (resumedAuthenticatedSession) {
            AuthenticationStatusText = $"Connected to Nura as {AuthenticationEmail}.";
        } else if (!string.IsNullOrWhiteSpace(resumeError)) {
            AuthenticationStatusText = "Stored Nura session could not be resumed. Sign in again or continue offline.";
        } else {
            AuthenticationStatusText = string.IsNullOrWhiteSpace(AuthenticationEmail)
                ? "Sign in with your email, or skip if your device keys are already stored locally."
                : $"Continue signing in as {AuthenticationEmail}, or skip if your device keys are already stored locally.";
        }

    }

    public void NotifyBluetoothMonitoringStarted() {
        _isBluetoothMonitoringActive = true;
        UpdateGlobalDiscoveryStatus();
    }

    public Task SyncLiveDevicesFromClientAsync(bool preferFirstConnectedDevice, CancellationToken cancellationToken = default) {
        return SyncDevicesFromClientAsync(preferFirstConnectedDevice, cancellationToken);
    }

    public async ValueTask DisposeAsync() {
        CancelGlobalStatusReset();
        CancelScheduledImmersionApply();
        if (_client is not null) {
            DetachClientEvents();
        }

        foreach (var device in Devices.ToList()) {
            device.PropertyChanged -= OnDevicePropertyChanged;
            await device.DisposeAsync();
        }
    }

    private void LoadSeedData(PopupDemoSeedData seedData) {
        foreach (var existingDevice in Devices.ToList()) {
            existingDevice.PropertyChanged -= OnDevicePropertyChanged;
        }

        Devices.Clear();
        _deviceModelsByIdentity.Clear();
        _devicePriorityIds.Clear();

        foreach (var profile in seedData.Profiles.Values) {
            profile.RenderThumbnail();
        }

        foreach (var device in seedData.Devices) {
            RegisterDeviceModel(device);
            RenderProfileThumbnails(device.Profiles);
            Devices.Add(device);
            _devicePriorityIds.Add(device.Id);
        }

        HasCompletedAuthenticationGate = seedData.HasCompletedAuthenticationGate;
        HasAuthenticatedWithNura = seedData.HasAuthenticatedWithNura;
        ConnectToNura = seedData.ConnectToNura;
        AuthenticationEmail = seedData.AuthenticationEmail;
        AuthenticationStatusText = seedData.AuthenticationStatusText;
        UpdateAllDeviceAuthContexts();
        SetGlobalStatus($"Demo mode: {Devices.Count} device{(Devices.Count == 1 ? string.Empty : "s")} ready.", StatusTone.Neutral);

        if (Devices.Count > 0) {
            CurrentDevice = Devices[0];
        } else {
            ResetCurrentSelectionToEmpty();
        }

        OnPropertyChanged(nameof(CurrentDeviceActionText));
    }

    private async Task RefreshDevicesAsync() {
        if (_client is null) {
            return;
        }

        try {
            FlashGlobalStatus("Refreshing paired Nura devices.", StatusTone.Information);
            await _client.Devices.RefreshAsync();
            await SyncDevicesFromClientAsync(preferFirstConnectedDevice: false, CancellationToken.None);
        } catch (Exception ex) {
            FlashGlobalStatus($"Device discovery failed: {ex.Message}", StatusTone.Error);
            if (CurrentDevice.IsLive) {
                CurrentDevice.WarningText = ex.Message;
            }
        }
    }

    private async Task RefreshCurrentDeviceAsync() {
        await CurrentDevice.RefreshFromDeviceAsync();
    }

    private async Task ConnectCurrentDeviceAsync() {
        await CurrentDevice.ConnectAndPrepareAsync(ConnectToNura, HasAuthenticatedWithNura);
    }

    private async Task ProvisionCurrentDeviceAsync() {
        await CurrentDevice.ProvisionAndPrepareAsync(ConnectToNura, HasAuthenticatedWithNura);
    }

    private async Task SyncDevicesFromClientAsync(bool preferFirstConnectedDevice, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (_client is null) {
            return;
        }

        await _deviceSyncGate.WaitAsync(cancellationToken);
        try {
            var liveDevices = _client.Devices.All.OfType<ConnectedNuraDevice>().ToList();
            var liveKeys = liveDevices.Select(GetLiveDeviceKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var liveDevice in liveDevices) {
                if (!_deviceModelsByIdentity.TryGetValue(GetLiveDeviceKey(liveDevice), out var model)) {
                    model = NuraDeviceViewModel.CreateLive(liveDevice, Profiles, ConnectToNura, HasAuthenticatedWithNura);
                    RegisterDeviceModel(model);
                    Devices.Add(model);
                } else {
                    model.AttachLiveDevice(liveDevice);
                    model.SetAuthContext(ConnectToNura, HasAuthenticatedWithNura);
                }
            }

            var staleModels = Devices
                .Where(device => device.IsLive && !liveKeys.Contains(device.Id))
                .ToList();
            foreach (var staleModel in staleModels) {
                staleModel.PropertyChanged -= OnDevicePropertyChanged;
                Devices.Remove(staleModel);
                _deviceModelsByIdentity.Remove(staleModel.Id);
                _devicePriorityIds.RemoveAll(id => string.Equals(id, staleModel.Id, StringComparison.OrdinalIgnoreCase));
                await staleModel.DisposeAsync();
            }

            if (Devices.Count > 0) {
                if (preferFirstConnectedDevice && Devices.Any(device => device.IsConnected)) {
                    CurrentDevice = Devices.First(device => device.IsConnected);
                } else if (!_deviceModelsByIdentity.ContainsKey(CurrentDevice.Id)) {
                    CurrentDevice = Devices.First();
                }
            } else {
                ResetCurrentSelectionToEmpty();
            }

            RaiseDeviceListPropertiesChanged();

            await TryAutoSetupLiveDevicesAsync(cancellationToken);
        } finally {
            _deviceSyncGate.Release();
        }
    }

    private void RegisterDeviceModel(NuraDeviceViewModel device) {
        device.PropertyChanged -= OnDevicePropertyChanged;
        device.PropertyChanged += OnDevicePropertyChanged;
        device.SetAuthContext(ConnectToNura, HasAuthenticatedWithNura);
        _deviceModelsByIdentity[device.Id] = device;
    }

    private void UpdateAllDeviceAuthContexts() {
        foreach (var device in Devices) {
            device.SetAuthContext(ConnectToNura, HasAuthenticatedWithNura);
        }

        RefreshCurrentDeviceBindings();
    }

    private async Task TryAutoSetupLiveDevicesAsync(CancellationToken cancellationToken) {
        if (!AutoSetupDevices) {
            return;
        }

        try {
            var liveDevices = Devices
                .Where(device => device.IsLive && device.IsConnected)
                .ToList();

            foreach (var device in liveDevices) {
                cancellationToken.ThrowIfCancellationRequested();
                await device.TryAutoSetupAsync(ConnectToNura, HasAuthenticatedWithNura, cancellationToken);
            }

            RefreshCurrentDeviceBindings();
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
        } catch (Exception ex) {
            if (CurrentDevice.IsLive) {
                CurrentDevice.WarningText = ex.Message;
            }
        }
    }

    private void AttachClientEvents() {
        if (_client is null) {
            return;
        }

        _client.Monitoring.DeviceConnected -= OnClientDeviceConnected;
        _client.Monitoring.DeviceDisconnected -= OnClientDeviceDisconnected;
        _client.Monitoring.DeviceConnected += OnClientDeviceConnected;
        _client.Monitoring.DeviceDisconnected += OnClientDeviceDisconnected;
    }

    private void DetachClientEvents() {
        if (_client is null) {
            return;
        }

        _client.Monitoring.DeviceConnected -= OnClientDeviceConnected;
        _client.Monitoring.DeviceDisconnected -= OnClientDeviceDisconnected;
    }

    private async void OnClientDeviceConnected(object? sender, NuraDeviceConnectionEventArgs e) {
        await RunOnUiThreadAsync(async () => {
            FlashGlobalStatus($"Device connected: {e.Device.Info.DisplayName}.", StatusTone.Success);
            await SyncDevicesFromClientAsync(
                preferFirstConnectedDevice: CurrentDevice.Id == EmptyDeviceId || !CurrentDevice.IsConnected,
                CancellationToken.None);
        });
    }

    private async void OnClientDeviceDisconnected(object? sender, NuraDeviceConnectionEventArgs e) {
        await RunOnUiThreadAsync(async () => {
            FlashGlobalStatus($"Device disconnected: {e.Device.Info.DisplayName}.", StatusTone.Warning);
            await SyncDevicesFromClientAsync(preferFirstConnectedDevice: false, CancellationToken.None);
        });
    }

    private void SetGlobalStatus(string text, StatusTone tone) {
        var changed = !string.Equals(GlobalStatusText, text, StringComparison.Ordinal) || GlobalStatusTone != tone;
        GlobalStatusText = text;
        GlobalStatusTone = tone;

        if (changed) {
            Debug.WriteLine($"[NuraDesktop.Status] system tone={tone} text={text.ReplaceLineEndings(" ")}");
        }

    }

    private void FlashGlobalStatus(string text, StatusTone tone) {
        SetGlobalStatus(text, tone);
        CancelGlobalStatusReset();

        var cancellation = new CancellationTokenSource();
        _globalStatusResetCts = cancellation;
        _ = ResetGlobalStatusAfterDelayAsync(cancellation);
    }

    private async Task ResetGlobalStatusAfterDelayAsync(CancellationTokenSource cancellation) {
        try {
            await Task.Delay(TimeSpan.FromSeconds(4), cancellation.Token);
            await RunOnUiThreadAsync(() => {
                if (!cancellation.IsCancellationRequested && ReferenceEquals(_globalStatusResetCts, cancellation)) {
                    UpdateGlobalDiscoveryStatus();
                }
            });
        } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
        }
    }

    private void CancelGlobalStatusReset() {
        var cancellation = _globalStatusResetCts;
        _globalStatusResetCts = null;
        if (cancellation is null) {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }

    private void UpdateGlobalDiscoveryStatus() {
        if (!IsLiveMode) {
            SetGlobalStatus($"Demo mode: {Devices.Count} device{(Devices.Count == 1 ? string.Empty : "s")} ready.", StatusTone.Neutral);
            return;
        }

        var connectedCount = Devices.Count(device => device.IsLive && device.IsConnected);
        var deviceText = connectedCount == 1 ? "1 Nura device" : $"{connectedCount} Nura devices";
        var suffix = _isBluetoothMonitoringActive
            ? "listening for changes."
            : "Bluetooth monitoring is starting.";
        SetGlobalStatus($"Discovered {deviceText}, {suffix}", StatusTone.Information);
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> collection, IEnumerable<T> items) {
        collection.Clear();
        foreach (var item in items) {
            collection.Add(item);
        }
    }

    private void RaiseDeviceListPropertiesChanged() {
        OnPropertyChanged(nameof(PrioritizedDevices));
        OnPropertyChanged(nameof(VisibleDevices));
        OnPropertyChanged(nameof(OverflowDevices));
        OnPropertyChanged(nameof(HasOverflowDevices));
        OnPropertyChanged(nameof(MoreDevicesButtonText));
    }

    private async Task RunOnUiThreadAsync(Action action) {
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        if (dispatcher.CheckAccess()) {
            action();
            return;
        }

        await dispatcher.InvokeAsync(action);
    }

    private async Task RunOnUiThreadAsync(Func<Task> action) {
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        if (dispatcher.CheckAccess()) {
            await action();
            return;
        }

        await dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private void RenderProfileThumbnails(IEnumerable<ProfileModel> profiles) {
        foreach (var profile in profiles) {
            profile.RenderThumbnail();
        }
    }

    private ProfileModel GetFallbackProfile(string name, int profileId) {
        var template = Profiles.Values.ElementAt(profileId % Profiles.Count);
        return new ProfileModel(name, template.VisualisationData) {
            Thumbnail = template.Thumbnail
        };
    }

    private static string GetLiveDeviceKey(NuraDevice liveDevice) => $"{liveDevice.Info.Serial}|{liveDevice.Info.DeviceAddress}";
}
