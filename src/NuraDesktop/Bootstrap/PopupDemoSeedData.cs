using NuraPopupWpf.Models;
using NuraPopupWpf.ViewModels;

namespace NuraPopupWpf.Bootstrap;

public sealed record class PopupDemoSeedData(
    IReadOnlyDictionary<string, ProfileModel> Profiles,
    IReadOnlyList<NuraDeviceViewModel> Devices,
    bool HasCompletedAuthenticationGate,
    bool HasAuthenticatedWithNura,
    bool ConnectToNura,
    string AuthenticationEmail,
    string AuthenticationStatusText);
