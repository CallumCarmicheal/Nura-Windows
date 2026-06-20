using System.Diagnostics;
using System.IO;

using NuraLib;
using NuraLib.Configuration;
using NuraLib.Logging;
using NuraDesktop.ViewModels;

namespace NuraDesktop.Bootstrap;

public sealed class LiveSdkBootstrapper : IPopupAppBootstrapper {
    public async Task<PopupAppContext> BootstrapAsync(string[] args, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();

        var storagePaths = PopupAppStoragePaths.Create(PopupAppBootstrapMode.Live);
        var configPath = ResolveNuraConfigPath(storagePaths);
        var config = NuraConfigStore.LoadOrCreate(configPath);
        var client = new NuraClient(new NuraConfigState(config));
#if DEBUG
        client.MinimumLogLevel = NuraLogLevel.Trace;
#else
        client.MinimumLogLevel = NuraLogLevel.Information;
#endif
        client.RequestStateSave += (_, _) => NuraConfigStore.Save(configPath, client.State.Configuration);
        client.OnLog += (_, e) => Debug.WriteLine($"[{e.Level}] {e.Source}: {e.Message}");

        var resumedAuthenticatedSession = false;
        string? resumeError = null;

        if (client.Auth.HasStoredCredentials) {
            try {
                await client.Auth.ResumeAsync(cancellationToken);
                resumedAuthenticatedSession = true;
            } catch (Exception ex) {
                resumeError = ex.Message;
            }
        }

        var viewModel = MainViewModel.CreateLive(client, storagePaths);
        await viewModel.InitializeLiveAsync(resumedAuthenticatedSession, resumeError, cancellationToken);
        await client.Monitoring.StartAsync(cancellationToken);
        await viewModel.SyncLiveDevicesFromClientAsync(preferFirstConnectedDevice: true, cancellationToken);
        viewModel.NotifyBluetoothMonitoringStarted();

        return new PopupAppContext(
            PopupAppBootstrapMode.Live,
            viewModel,
            storagePaths,
            client,
            async () => {
                await viewModel.DisposeAsync();
                try {
                    await client.Monitoring.StopAsync();
                } catch {
                }

                foreach (var device in client.Devices.All.OfType<NuraLib.Devices.ConnectedNuraDevice>()) {
                    try {
                        await device.StopMonitoringAsync();
                    } catch {
                    }
                }
            });
    }

    private static string ResolveNuraConfigPath(PopupAppStoragePaths storagePaths) {
        if (File.Exists(storagePaths.NuraConfigPath)) {
            return storagePaths.NuraConfigPath;
        }

        return storagePaths.NuraConfigPath;
    }
}
