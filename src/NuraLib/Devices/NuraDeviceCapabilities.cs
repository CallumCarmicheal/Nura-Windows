namespace NuraLib.Devices;

/// <summary>
/// Resolves normalized device families and derived capability flags from raw type labels and firmware versions.
/// </summary>
public static class NuraDeviceCapabilities {
    /// <summary>
    /// Resolves a raw device type label to a normalized <see cref="NuraDeviceType"/>.
    /// </summary>
    public static NuraDeviceType ResolveType(string? rawType) {
        if (string.IsNullOrWhiteSpace(rawType)) {
            return NuraDeviceType.Unknown;
        }

        var normalized = rawType.Trim().ToLowerInvariant();
        return normalized switch {
            "nuraphone" => NuraDeviceType.Nuraphone,
            "nuraloop" => NuraDeviceType.NuraLoop,
            "nurabuds" or "nuralite" => NuraDeviceType.NuraBuds,
            "nurapro" or "nuratrue pro" or "denon perl pro" or "denon pro" or "nurapro_left" or "nuraproleft" or "nurapro_right" or "nuraproright" or "nurapro_base" or "nuraprobase" => NuraDeviceType.NuraTruePro,
            "nurasport" or "nuratrue sport" => NuraDeviceType.NuraTrueSport,
            "nuratrue" or "denon perl" or "tws" or "tws_left" or "twsleft" or "tws_right" or "twsright" or "tws_base" or "twsbase" => NuraDeviceType.NuraTrue,
            _ when normalized.Contains("nurapro") => NuraDeviceType.NuraTruePro,
            _ when normalized.Contains("nurabuds") || normalized.Contains("nuralite") => NuraDeviceType.NuraBuds,
            _ when normalized.Contains("nurasport") => NuraDeviceType.NuraTrueSport,
            _ when normalized.Contains("nuratrue") || normalized.Contains("denon perl") => NuraDeviceType.NuraTrue,
            _ when normalized.Contains("nuraloop") => NuraDeviceType.NuraLoop,
            _ when normalized.Contains("nuraphone") => NuraDeviceType.Nuraphone,
            _ => NuraDeviceType.Unknown
        };
    }

    /// <summary>
    /// Gets the normalized lowercase type tag for a device family.
    /// </summary>
    public static string GetTypeTag(NuraDeviceType deviceType) =>
        deviceType switch {
            NuraDeviceType.Nuraphone => "nuraphone",
            NuraDeviceType.NuraLoop => "nuraloop",
            NuraDeviceType.NuraTrue => "nuratrue",
            NuraDeviceType.NuraBuds => "nurabuds",
            NuraDeviceType.NuraTruePro => "nurapro",
            NuraDeviceType.NuraTrueSport => "nurasport",
            _ => "unknown"
        };

    /// <summary>
    /// Gets the human-readable debug name for a device family.
    /// </summary>
    public static string GetDebugName(NuraDeviceType deviceType) =>
        deviceType switch {
            NuraDeviceType.Nuraphone => "Nuraphone",
            NuraDeviceType.NuraLoop => "NuraLoop",
            NuraDeviceType.NuraTrue => "NuraTrue",
            NuraDeviceType.NuraBuds => "NuraBuds",
            NuraDeviceType.NuraTruePro => "NuraTrue Pro",
            NuraDeviceType.NuraTrueSport => "NuraTrue Sport",
            _ => "Unknown"
        };

    /// <summary>
    /// Determines whether the specified device family is true wireless.
    /// </summary>
    public static bool IsTws(NuraDeviceType deviceType) =>
        deviceType is NuraDeviceType.NuraTrue or
                     NuraDeviceType.NuraBuds or
                     NuraDeviceType.NuraTruePro or
                     NuraDeviceType.NuraTrueSport;

    /// <summary>
    /// Gets the minimum firmware version considered compatible for the specified device family.
    /// </summary>
    public static int GetMinimumFirmwareVersion(NuraDeviceType deviceType) =>
        deviceType switch {
            NuraDeviceType.Nuraphone => 600,
            NuraDeviceType.NuraLoop => 0,
            NuraDeviceType.NuraTrue => 400080,
            NuraDeviceType.NuraBuds => 600054,
            NuraDeviceType.NuraTruePro => 800020,
            NuraDeviceType.NuraTrueSport => 1200020,
            _ => 0
        };

    /// <summary>
    /// Gets the minimum firmware version required for the official-app offline policy for the specified device family.
    /// </summary>
    public static int GetMinimumFirmwareVersionForOfflineMode(NuraDeviceType deviceType) =>
        deviceType switch {
            NuraDeviceType.Nuraphone => 1000,
            NuraDeviceType.NuraLoop => 0,
            NuraDeviceType.NuraTrue => 0,
            NuraDeviceType.NuraBuds => 0,
            NuraDeviceType.NuraTruePro => 0,
            NuraDeviceType.NuraTrueSport => 0,
            _ => 0
        };

    /// <summary>
    /// Gets the default immersion level used by the library for the specified device family.
    /// </summary>
    public static NuraImmersionLevel GetDefaultImmersionLevel(NuraDeviceType deviceType) => NuraImmersionLevel.Positive4;

    /// <summary>
    /// Computes the supported audio capability set for a device family and firmware version.
    /// </summary>
    public static NuraAudioCapabilities GetAudioCapabilities(NuraDeviceType deviceType, int firmwareVersion) {
        var features = GetSupportedFeatures(deviceType, firmwareVersion);
        var capabilities = NuraAudioCapabilities.None;

        if (features.HasFlag(NuraSupportedFeatures.Anc)) capabilities |= NuraAudioCapabilities.Anc;
        if (features.HasFlag(NuraSupportedFeatures.AncLevel)) capabilities |= NuraAudioCapabilities.AncLevel;
        if (features.HasFlag(NuraSupportedFeatures.GlobalAncToggle)) capabilities |= NuraAudioCapabilities.GlobalAncToggle;
        if (features.HasFlag(NuraSupportedFeatures.Immersion)) capabilities |= NuraAudioCapabilities.Immersion;
        if (features.HasFlag(NuraSupportedFeatures.KickIt)) capabilities |= NuraAudioCapabilities.KickIt;
        if (features.HasFlag(NuraSupportedFeatures.Spatial)) capabilities |= NuraAudioCapabilities.Spatial;
        if (features.HasFlag(NuraSupportedFeatures.ProEq)) capabilities |= NuraAudioCapabilities.ProEq;
        if (features.HasFlag(NuraSupportedFeatures.PersonalisedMode)) capabilities |= NuraAudioCapabilities.PersonalisedMode;
        if (features.HasFlag(NuraSupportedFeatures.EuAttenuation)) capabilities |= NuraAudioCapabilities.EuAttenuation;
        if (features.HasFlag(NuraSupportedFeatures.AnalogDigitalEuAttenuation)) capabilities |= NuraAudioCapabilities.AnalogDigitalEuAttenuation;
        if (features.HasFlag(NuraSupportedFeatures.VisualisationData)) capabilities |= NuraAudioCapabilities.VisualisationData;

        return capabilities;
    }

    /// <summary>
    /// Computes the supported interaction capability set for a device family and firmware version.
    /// </summary>
    public static NuraInteractionCapabilities GetInteractionCapabilities(NuraDeviceType deviceType, int firmwareVersion) {
        var features = GetSupportedFeatures(deviceType, firmwareVersion);
        var capabilities = NuraInteractionCapabilities.None;

        if (features.HasFlag(NuraSupportedFeatures.TouchButtons)) capabilities |= NuraInteractionCapabilities.TouchButtons;
        if (features.HasFlag(NuraSupportedFeatures.Dial)) capabilities |= NuraInteractionCapabilities.Dial;
        if (features.HasFlag(NuraSupportedFeatures.HeadDetection)) capabilities |= NuraInteractionCapabilities.HeadDetection;
        if (features.HasFlag(NuraSupportedFeatures.ManualHeadDetection)) capabilities |= NuraInteractionCapabilities.ManualHeadDetection;
        if (features.HasFlag(NuraSupportedFeatures.DoubleTap)) capabilities |= NuraInteractionCapabilities.DoubleTap;
        if (features.HasFlag(NuraSupportedFeatures.TripleTap)) capabilities |= NuraInteractionCapabilities.TripleTap;
        if (features.HasFlag(NuraSupportedFeatures.VoicePromptGain)) capabilities |= NuraInteractionCapabilities.VoicePromptGain;
        if (features.HasFlag(NuraSupportedFeatures.ButtonPlayPauseAnswer)) capabilities |= NuraInteractionCapabilities.ButtonPlayPauseAnswer;
        if (features.HasFlag(NuraSupportedFeatures.ButtonVoiceAssistant)) capabilities |= NuraInteractionCapabilities.ButtonVoiceAssistant;
        if (features.HasFlag(NuraSupportedFeatures.ButtonVolumeUpDown)) capabilities |= NuraInteractionCapabilities.ButtonVolumeUpDown;
        if (features.HasFlag(NuraSupportedFeatures.ButtonPrevNextTrack)) capabilities |= NuraInteractionCapabilities.ButtonPrevNextTrack;
        if (features.HasFlag(NuraSupportedFeatures.ButtonToggleSocial)) capabilities |= NuraInteractionCapabilities.ButtonToggleSocial;
        if (features.HasFlag(NuraSupportedFeatures.ButtonKickItUpDown)) capabilities |= NuraInteractionCapabilities.ButtonKickItUpDown;

        return capabilities;
    }

    /// <summary>
    /// Computes the supported system capability set for a device family and firmware version.
    /// </summary>
    public static NuraSystemCapabilities GetSystemCapabilities(NuraDeviceType deviceType, int firmwareVersion) {
        var features = GetSupportedFeatures(deviceType, firmwareVersion);
        var capabilities = NuraSystemCapabilities.None;

        if (features.HasFlag(NuraSupportedFeatures.DeviceInfo)) capabilities |= NuraSystemCapabilities.DeviceInfo;
        if (features.HasFlag(NuraSupportedFeatures.Profiles)) capabilities |= NuraSystemCapabilities.Profiles;
        if (features.HasFlag(NuraSupportedFeatures.BulkCommands)) capabilities |= NuraSystemCapabilities.BulkCommands;
        if (features.HasFlag(NuraSupportedFeatures.UserId)) capabilities |= NuraSystemCapabilities.UserId;
        if (features.HasFlag(NuraSupportedFeatures.MspFirmwareVersion)) capabilities |= NuraSystemCapabilities.MspFirmwareVersion;
        if (features.HasFlag(NuraSupportedFeatures.InsertionDataV2)) capabilities |= NuraSystemCapabilities.InsertionDataV2;
        if (features.HasFlag(NuraSupportedFeatures.Multipoint)) capabilities |= NuraSystemCapabilities.Multipoint;

        return capabilities;
    }

    /// <summary>
    /// Computes the supported button-function families for a device family and firmware version.
    /// </summary>
    public static NuraButtonFunctionSupport GetSupportedButtonFunctions(NuraDeviceType deviceType, int firmwareVersion) {
        var features = GetSupportedFeatures(deviceType, firmwareVersion);
        var capabilities = NuraButtonFunctionSupport.None;

        if (features.HasFlag(NuraSupportedFeatures.ButtonPlayPauseAnswer)) capabilities |= NuraButtonFunctionSupport.PlayPauseAnswer;
        if (features.HasFlag(NuraSupportedFeatures.ButtonVoiceAssistant)) capabilities |= NuraButtonFunctionSupport.VoiceAssistant;
        if (features.HasFlag(NuraSupportedFeatures.ButtonVolumeUpDown)) capabilities |= NuraButtonFunctionSupport.VolumeUpDown;
        if (features.HasFlag(NuraSupportedFeatures.ButtonPrevNextTrack)) capabilities |= NuraButtonFunctionSupport.PrevNextTrack;
        if (features.HasFlag(NuraSupportedFeatures.ButtonToggleSocial)) capabilities |= NuraButtonFunctionSupport.ToggleSocial;
        if (features.HasFlag(NuraSupportedFeatures.ButtonKickItUpDown)) capabilities |= NuraButtonFunctionSupport.KickItUpDown;

        return capabilities;
    }

    /// <summary>
    /// Builds the grouped capability information for a device family and firmware version.
    /// </summary>
    public static NuraDeviceCapabilityInfo GetCapabilityInfo(NuraDeviceType deviceType, int firmwareVersion) {
        var features = GetSupportedFeatures(deviceType, firmwareVersion);
        return new NuraDeviceCapabilityInfo {
            Features = features,
            Audio = GetAudioCapabilities(deviceType, firmwareVersion),
            Interaction = GetInteractionCapabilities(deviceType, firmwareVersion),
            System = GetSystemCapabilities(deviceType, firmwareVersion),
            ButtonGestures = GetSupportedButtonGestures(deviceType, firmwareVersion),
            ButtonFunctions = GetSupportedButtonFunctions(deviceType, firmwareVersion)
        };
    }

    /// <summary>
    /// Computes the supported touch-button gesture slots for a device family and firmware version.
    /// </summary>
    public static NuraButtonGestureSupport GetSupportedButtonGestures(NuraDeviceType deviceType, int firmwareVersion) {
        var supportedFeatures = GetSupportedFeatures(deviceType, firmwareVersion);
        if (!supportedFeatures.HasFlag(NuraSupportedFeatures.TouchButtons)) {
            return NuraButtonGestureSupport.None;
        }

        var gestures = NuraButtonGestureSupport.SingleTap | NuraButtonGestureSupport.TapAndHold;

        if (supportedFeatures.HasFlag(NuraSupportedFeatures.DoubleTap)) {
            gestures |= NuraButtonGestureSupport.DoubleTap;
        }

        if (supportedFeatures.HasFlag(NuraSupportedFeatures.TripleTap)) {
            gestures |= NuraButtonGestureSupport.TripleTap;
        }

        return gestures;
    }

    /// <summary>
    /// Computes the supported-feature set for a device family and firmware version.
    /// </summary>
    public static NuraSupportedFeatures GetSupportedFeatures(NuraDeviceType deviceType, int firmwareVersion) {
        var features = NuraSupportedFeatures.DeviceInfo | NuraSupportedFeatures.Profiles;

        switch (deviceType) {
            case NuraDeviceType.Nuraphone:
                features |= NuraSupportedFeatures.TouchButtons |
                            NuraSupportedFeatures.Anc |
                            NuraSupportedFeatures.Immersion |
                            NuraSupportedFeatures.UserId |
                            NuraSupportedFeatures.PersonalisedMode;

                if (firmwareVersion >= 400) {
                    features |= NuraSupportedFeatures.KickIt |
                                NuraSupportedFeatures.ButtonPrevNextTrack;
                }

                if (firmwareVersion >= 500) {
                    features |= NuraSupportedFeatures.DoubleTap;
                }

                if (firmwareVersion > 510) {
                    features |= NuraSupportedFeatures.ButtonToggleSocial |
                                NuraSupportedFeatures.VisualisationData;
                }

                if (firmwareVersion >= 511) {
                    features |= NuraSupportedFeatures.ButtonVolumeUpDown;
                }

                break;

            case NuraDeviceType.NuraLoop:
                features |= NuraSupportedFeatures.TouchButtons |
                            NuraSupportedFeatures.Dial |
                            NuraSupportedFeatures.Anc |
                            NuraSupportedFeatures.AncLevel |
                            NuraSupportedFeatures.GlobalAncToggle |
                            NuraSupportedFeatures.Immersion |
                            NuraSupportedFeatures.UserId |
                            NuraSupportedFeatures.VisualisationData |
                            NuraSupportedFeatures.PersonalisedMode |
                            NuraSupportedFeatures.ButtonVolumeUpDown |
                            NuraSupportedFeatures.ButtonPrevNextTrack |
                            NuraSupportedFeatures.ButtonToggleSocial;

                if (firmwareVersion >= 100117) {
                    features |= NuraSupportedFeatures.KickIt;
                }

                if (firmwareVersion >= 100200) {
                    features |= NuraSupportedFeatures.InsertionDataV2;
                }

                if (firmwareVersion >= 100204) {
                    features |= NuraSupportedFeatures.ButtonPlayPauseAnswer;
                }

                if (firmwareVersion >= 100214) {
                    features |= NuraSupportedFeatures.MspFirmwareVersion;
                }

                if (firmwareVersion >= 100233) {
                    features |= NuraSupportedFeatures.DoubleTap;
                }

                break;

            case NuraDeviceType.NuraTrue:
                features |= GetTwsBaseFeatures() |
                            NuraSupportedFeatures.HeadDetection |
                            NuraSupportedFeatures.ManualHeadDetection |
                            NuraSupportedFeatures.Multipoint |
                            NuraSupportedFeatures.PersonalisedMode;

                if (firmwareVersion >= 200096) {
                    features |= NuraSupportedFeatures.ButtonVoiceAssistant;
                }

                if (firmwareVersion >= 200219) {
                    features |= NuraSupportedFeatures.TripleTap;
                }

                if (firmwareVersion >= 200223) {
                    features |= NuraSupportedFeatures.BulkCommands;
                }

                if (firmwareVersion >= 200231) {
                    features |= NuraSupportedFeatures.ButtonKickItUpDown;
                }

                if (firmwareVersion >= 200068) {
                    features |= NuraSupportedFeatures.HeadDetection |
                                NuraSupportedFeatures.ManualHeadDetection;
                }

                if (firmwareVersion >= 200236) {
                    features |= NuraSupportedFeatures.VoicePromptGain;
                }

                break;

            case NuraDeviceType.NuraBuds:
                features |= GetTwsBaseFeatures() |
                            NuraSupportedFeatures.HeadDetection |
                            NuraSupportedFeatures.ManualHeadDetection |
                            NuraSupportedFeatures.Multipoint |
                            NuraSupportedFeatures.PersonalisedMode;

                if (firmwareVersion >= 300086) {
                    features |= NuraSupportedFeatures.ButtonVoiceAssistant;
                }

                if (firmwareVersion >= 300180) {
                    features |= NuraSupportedFeatures.TripleTap;
                }

                if (firmwareVersion >= 300185) {
                    features |= NuraSupportedFeatures.BulkCommands;
                }

                if (firmwareVersion >= 300192) {
                    features |= NuraSupportedFeatures.ButtonKickItUpDown;
                }

                if (firmwareVersion >= 300054) {
                    features |= NuraSupportedFeatures.HeadDetection |
                                NuraSupportedFeatures.ManualHeadDetection;
                }

                if (firmwareVersion >= 300211) {
                    features |= NuraSupportedFeatures.VoicePromptGain;
                }

                break;

            case NuraDeviceType.NuraTruePro:
            case NuraDeviceType.NuraTrueSport:
                features |= GetTwsBaseFeatures() |
                            NuraSupportedFeatures.HeadDetection |
                            NuraSupportedFeatures.ManualHeadDetection |
                            NuraSupportedFeatures.Multipoint |
                            NuraSupportedFeatures.PersonalisedMode |
                            NuraSupportedFeatures.Spatial |
                            NuraSupportedFeatures.ProEq |
                            NuraSupportedFeatures.EuAttenuation |
                            NuraSupportedFeatures.AnalogDigitalEuAttenuation |
                            NuraSupportedFeatures.ButtonVoiceAssistant |
                            NuraSupportedFeatures.TripleTap |
                            NuraSupportedFeatures.BulkCommands |
                            NuraSupportedFeatures.ButtonKickItUpDown |
                            NuraSupportedFeatures.VoicePromptGain;
                break;

            case NuraDeviceType.Unknown:
            default:
                break;
        }

        return features;
    }

    private static NuraSupportedFeatures GetTwsBaseFeatures() =>
        NuraSupportedFeatures.TouchButtons |
        NuraSupportedFeatures.Anc |
        NuraSupportedFeatures.AncLevel |
        NuraSupportedFeatures.GlobalAncToggle |
        NuraSupportedFeatures.Immersion |
        NuraSupportedFeatures.VisualisationData |
        NuraSupportedFeatures.DoubleTap |
        NuraSupportedFeatures.ButtonPlayPauseAnswer |
        NuraSupportedFeatures.ButtonVolumeUpDown |
        NuraSupportedFeatures.ButtonPrevNextTrack |
        NuraSupportedFeatures.ButtonToggleSocial;
}
