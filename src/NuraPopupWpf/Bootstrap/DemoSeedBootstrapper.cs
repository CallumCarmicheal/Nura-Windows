using NuraPopupWpf.ViewModels;

namespace NuraPopupWpf.Bootstrap;

public sealed class DemoSeedBootstrapper : IPopupAppBootstrapper {
    public Task<PopupAppContext> BootstrapAsync(string[] args, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        var storagePaths = PopupAppStoragePaths.Create(PopupAppBootstrapMode.Demo);
        var seedData = PopupDemoSeedFactory.Create();
        var viewModel = MainViewModel.CreateDemo(seedData, storagePaths);
        return Task.FromResult(new PopupAppContext(PopupAppBootstrapMode.Demo, viewModel, storagePaths));
    }
}
