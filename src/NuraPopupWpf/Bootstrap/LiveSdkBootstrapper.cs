using System.Diagnostics;

using NuraLib;
using NuraLib.Configuration;
using NuraPopupWpf.ViewModels;

namespace NuraPopupWpf.Bootstrap;

public sealed class LiveSdkBootstrapper : IPopupAppBootstrapper {
    public async Task<PopupAppContext> BootstrapAsync(string[] args, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();

        var storagePaths = PopupAppStoragePaths.Create(PopupAppBootstrapMode.Live);
        var configPath = storagePaths.NuraConfigPath ?? throw new InvalidOperationException("Live mode requires a NuraLib configuration path.");
        var config = NuraConfigStore.LoadOrCreate(configPath);
        var client = new NuraClient(new NuraConfigState(config));
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

        await client.Devices.RefreshAsync(cancellationToken);
        await client.Monitoring.StartAsync(cancellationToken);

        var viewModel = MainViewModel.CreateLive(client, storagePaths);
        await viewModel.InitializeLiveAsync(resumedAuthenticatedSession, resumeError, cancellationToken);

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
}
