using NuraDesktop.Models;
using NuraDesktop.ViewModels;

namespace NuraDesktop.Bootstrap;

public sealed record class PopupDemoSeedData(
    IReadOnlyDictionary<string, ProfileModel> Profiles,
    IReadOnlyList<NuraDeviceViewModel> Devices,
    bool HasCompletedAuthenticationGate,
    bool HasAuthenticatedWithNura,
    bool ConnectToNura,
    string AuthenticationEmail,
    string AuthenticationStatusText);
