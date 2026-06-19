using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using NuraLib;
using NuraLib.Devices;
using NuraLib.Monitoring;
using NuraLib.Rendering;

using NuraPopupWpf.Bootstrap;
using NuraPopupWpf.Infrastructure;
using NuraPopupWpf.Models;

namespace NuraPopupWpf.ViewModels;

public sealed partial class MainViewModel {
    private readonly ObservableCollection<ButtonFunctionOption> _singleTapButtonOptions = [];
    private readonly ObservableCollection<ButtonFunctionOption> _doubleTapButtonOptions = [];
    private readonly ObservableCollection<ButtonFunctionOption> _tripleTapButtonOptions = [];
    private readonly ObservableCollection<ButtonFunctionOption> _tapAndHoldButtonOptions = [];
    private readonly ObservableCollection<DialFunctionOption> _dialFunctionOptions = [];
    private readonly Dictionary<string, NuraDeviceViewModel> _deviceModelsByIdentity = new(StringComparer.OrdinalIgnoreCase);

    private NuraClient? _client;
    private bool _suppressProfileSelectionApply;

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

    public string CurrentDeviceActionText => CurrentDevice.OperationStatusText;

    public bool ShowCurrentDeviceActionPanel => CurrentDevice.IsLive;

    private void InitializeRuntimeExtensions() {
        RefreshDevicesCommand = new AsyncRelayCommand(async (_, _) => await RefreshDevicesAsync());
        RefreshCurrentDeviceCommand = new AsyncRelayCommand(async (_, _) => await RefreshCurrentDeviceAsync());
        ConnectCurrentDeviceCommand = new AsyncRelayCommand(async (_, _) => await ConnectCurrentDeviceAsync());
        ProvisionCurrentDeviceCommand = new AsyncRelayCommand(async (_, _) => await ProvisionCurrentDeviceAsync());
        RefreshCurrentDeviceBindings();
        OnPropertyChanged(nameof(CurrentDeviceActionText));
    }

    public async Task InitializeLiveAsync(bool resumedAuthenticatedSession, string? resumeError, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        if (_client is null) {
            return;
        }

        AttachClientEvents();

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

        await SyncDevicesFromClientAsync(preferFirstConnectedDevice: true, cancellationToken);
    }

    public Task SyncLiveDevicesFromClientAsync(bool preferFirstConnectedDevice, CancellationToken cancellationToken = default) {
        return SyncDevicesFromClientAsync(preferFirstConnectedDevice, cancellationToken);
    }

    public async ValueTask DisposeAsync() {
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
            profile.Thumbnail = _renderer.RenderThumbnail(profile, 20);
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
            await _client.Devices.RefreshAsync();
            await SyncDevicesFromClientAsync(preferFirstConnectedDevice: false, CancellationToken.None);
        } catch (Exception ex) {
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
            await SyncDevicesFromClientAsync(preferFirstConnectedDevice: false, CancellationToken.None);
        });
    }

    private async void OnClientDeviceDisconnected(object? sender, NuraDeviceConnectionEventArgs e) {
        await RunOnUiThreadAsync(async () => {
            await SyncDevicesFromClientAsync(preferFirstConnectedDevice: false, CancellationToken.None);
        });
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
            profile.Thumbnail = _renderer.RenderThumbnail(profile.VisualisationData, 20).ToBitmapSource();
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
