namespace NuraDesktop.Bootstrap;

public interface IPopupAppBootstrapper {
    Task<PopupAppContext> BootstrapAsync(string[] args, CancellationToken cancellationToken = default);
}
