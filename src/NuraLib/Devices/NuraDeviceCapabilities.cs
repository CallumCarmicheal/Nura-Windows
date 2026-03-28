namespace NuraLib.Devices;

public static class NuraDeviceCapabilities {
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

    public static bool IsTws(NuraDeviceType deviceType) =>
        deviceType is NuraDeviceType.NuraTrue or
                     NuraDeviceType.NuraBuds or
                     NuraDeviceType.NuraTruePro or
                     NuraDeviceType.NuraTrueSport;

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

    public static int GetDefaultImmersionLevel(NuraDeviceType deviceType) => 6;

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
