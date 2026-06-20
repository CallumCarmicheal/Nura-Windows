using NuraLib;
using NuraDesktop.ViewModels;

namespace NuraDesktop.Bootstrap;

public sealed class PopupAppContext : IAsyncDisposable {
    private readonly Func<ValueTask>? _disposeAsync;

    public PopupAppContext(
        PopupAppBootstrapMode mode,
        MainViewModel rootViewModel,
        PopupAppStoragePaths storagePaths,
        NuraClient? client = null,
        Func<ValueTask>? disposeAsync = null
    ) {
        Mode = mode;
        RootViewModel = rootViewModel ?? throw new ArgumentNullException(nameof(rootViewModel));
        StoragePaths = storagePaths ?? throw new ArgumentNullException(nameof(storagePaths));
        Client = client;
        _disposeAsync = disposeAsync;
    }

    public PopupAppBootstrapMode Mode { get; }

    public MainViewModel RootViewModel { get; }

    public PopupAppStoragePaths StoragePaths { get; }

    public NuraClient? Client { get; }

    public async ValueTask DisposeAsync() {
        if (_disposeAsync is not null) {
            await _disposeAsync();
        }
    }
}
