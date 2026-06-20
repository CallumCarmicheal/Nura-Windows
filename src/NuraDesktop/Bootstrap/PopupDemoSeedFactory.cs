using NuraLib.Devices;
using NuraPopupWpf.Models;
using NuraPopupWpf.ViewModels;

namespace NuraPopupWpf.Bootstrap;

public static class PopupDemoSeedFactory {
    public static PopupDemoSeedData Create() {
        var profiles = CreateProfiles();
        var devices = CreateDevices(profiles);

        return new PopupDemoSeedData(
            Profiles: profiles,
            Devices: devices,
            HasCompletedAuthenticationGate: true,
            HasAuthenticatedWithNura: true,
            ConnectToNura: true,
            AuthenticationEmail: "demo@nura.local",
            AuthenticationStatusText: "Loaded demo bootstrap data.");
    }

    public static IReadOnlyDictionary<string, ProfileModel> CreateProfiles() {
        return new Dictionary<string, ProfileModel> {
            ["Callum"] = new ProfileModel(
                "Callum",
                new NuraProfileVisualisationData {
                    Valid = true,
                    Colour = 0.45,
                    LeftData = [13.688053, 1.567564, 9.559364, -15.043674, -1.586667, 0.576336, -6.451581, -4.674792, -1.964447, -1.165475, -2.944404, -3.545126],
                    RightData = [4.688053, 7.567564, 3.559364, 4.043674, 2.586667, 3.576336, -4.451581, -6.674792, 2.964447, -6.165475, -5.944404, 1.545126]
                }),
            ["Studio"] = new ProfileModel(
                "Studio",
                new NuraProfileVisualisationData {
                    Valid = true,
                    Colour = 0.10,
                    LeftData = [1.2, 0.8, 0.4, -0.3, -0.7, -0.4, 0.2, 0.5, 0.1, -0.2, -0.5, -0.3],
                    RightData = [1.0, 0.7, 0.5, -0.2, -0.6, -0.5, 0.1, 0.6, 0.2, -0.1, -0.4, -0.2]
                }),
            ["Travel"] = new ProfileModel(
                "Travel",
                new NuraProfileVisualisationData {
                    Valid = true,
                    Colour = 0.78,
                    LeftData = [2.0, 1.8, 1.4, 0.7, 0.1, -0.8, -2.2, -4.5, -6.8, -7.5, -5.6, -3.2],
                    RightData = [1.7, 1.5, 1.2, 0.5, -0.2, -1.2, -2.8, -5.0, -7.2, -7.8, -5.9, -3.5]
                }),
        };
    }

    private static IReadOnlyList<NuraDeviceViewModel> CreateDevices(IReadOnlyDictionary<string, ProfileModel> profiles) {
        var fallbackProfiles = profiles.Values.ToList();
        var devices = new[] {
            NuraDeviceViewModel.CreateDemo(
                "nuratrue-pro-523",
                "NuraTrue Pro 523",
                80,
                "NTP-523-84A2197",
                "3.2.1",
                [profiles["Callum"], profiles["Studio"], profiles["Travel"]],
                fallbackProfiles,
                isConnected: true,
                immersionIndex: 3,
                isPersonalised: true),
            NuraDeviceViewModel.CreateDemo(
                "nuraloop-564",
                "NuraLoop 564",
                62,
                "NLP-114-72C1901",
                "2.8.4",
                [profiles["Callum"], profiles["Travel"]],
                fallbackProfiles,
                isConnected: false,
                socialMode: true,
                ancEnabled: false,
                euVolumeLimiter: true,
                immersionIndex: 1,
                isPersonalised: false),
            NuraDeviceViewModel.CreateDemo(
                "nuraphone-123",
                "nuraphone 123",
                47,
                "NPH-008-44B9036",
                "4.1.0",
                [profiles["Callum"]],
                fallbackProfiles,
                isConnected: true,
                socialMode: false,
                ancEnabled: true,
                euVolumeLimiter: false,
                immersionIndex: 5,
                isPersonalised: true),
            NuraDeviceViewModel.CreateDemo(
                "nurabuds-521",
                "NuraBuds 521",
                91,
                "NBD-201-55F4412",
                "1.6.3",
                [profiles["Studio"], profiles["Travel"]],
                fallbackProfiles,
                isConnected: true,
                socialMode: false,
                ancEnabled: true,
                euVolumeLimiter: false,
                immersionIndex: 2,
                isPersonalised: false),
            NuraDeviceViewModel.CreateDemo(
                "nuraloop-796",
                "Nura Loop 796",
                34,
                "NLB-171-22D8034",
                "0.9.7",
                [profiles["Callum"], profiles["Studio"]],
                fallbackProfiles,
                isConnected: false,
                socialMode: false,
                ancEnabled: false,
                euVolumeLimiter: true,
                immersionIndex: 4,
                isPersonalised: true,
                warningText: "Provisioning required"),
        };

        ConfigureDemoCapabilities(devices[0], touchButtons: CreateTouchButtons(spatial: true), ancLevel: 2, spatialEnabled: true, supportsSpatial: true);
        ConfigureDemoCapabilities(devices[1], touchButtons: CreateTouchButtons(), ancLevel: 1, dial: new NuraDialConfiguration { Left = NuraDialFunction.Anc, Right = NuraDialFunction.Kickit }, supportsDial: true, supportsEuVolumeLimiter: true);
        ConfigureDemoCapabilities(devices[2], touchButtons: CreateTouchButtons(doubleTap: false), ancLevel: null, supportsAncLevel: false);
        ConfigureDemoCapabilities(devices[3], touchButtons: CreateTouchButtons(), ancLevel: 2);
        ConfigureDemoCapabilities(devices[4], touchButtons: CreateTouchButtons(), ancLevel: 1, dial: new NuraDialConfiguration { Left = NuraDialFunction.Volume, Right = NuraDialFunction.Kickit }, supportsDial: true, supportsEuVolumeLimiter: true);

        return devices;
    }

    private static void ConfigureDemoCapabilities(
        NuraDeviceViewModel device,
        NuraButtonConfiguration? touchButtons,
        int? ancLevel,
        NuraDialConfiguration? dial = null,
        bool spatialEnabled = false,
        bool supportsAncLevel = true,
        bool supportsSpatial = false,
        bool supportsDial = false,
        bool supportsEuVolumeLimiter = false) {
        device.SupportsAnc = true;
        device.SupportsAncLevel = supportsAncLevel;
        device.SupportsSpatial = supportsSpatial;
        device.SupportsTouchButtons = true;
        device.SupportsDial = supportsDial;
        device.SupportsEuVolumeLimiter = supportsEuVolumeLimiter;
        device.TouchButtons = touchButtons;
        device.Dial = dial;
        device.AncLevel = ancLevel;
        device.SpatialEnabled = spatialEnabled;
    }

    private static NuraButtonConfiguration CreateTouchButtons(bool doubleTap = true, bool spatial = false) {
        return new NuraButtonConfiguration {
            LeftSingleTap = NuraButtonFunction.PlayPauseAndCall,
            RightSingleTap = NuraButtonFunction.ToggleSocial,
            LeftDoubleTap = doubleTap ? NuraButtonFunction.PreviousTrack : null,
            RightDoubleTap = doubleTap ? NuraButtonFunction.NextTrack : null,
            LeftTapAndHold = NuraButtonFunction.HoldForPassthroughOnOneSide,
            RightTapAndHold = spatial ? NuraButtonFunction.ToggleSpatial : NuraButtonFunction.ToggleAnc
        };
    }
}
