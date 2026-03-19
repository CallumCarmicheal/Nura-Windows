namespace desktop_app.Protocol;

internal static class NuraQueries {
    public static IReadOnlyList<NuraQuery> SafeStartupReads(int currentProfileId) {
        return
        [
            new("GetDeepSleepTimeout", Hex.Parse("006c")),
            new("GetCurrentProfileID", Hex.Parse("0041")),
            new("GetProfileName(profile=0)", Hex.Parse("001a00")),
            new("GetProfileName(profile=1)", Hex.Parse("001a01")),
            new("GetProfileName(profile=2)", Hex.Parse("001a02")),
            new("IsGenericModeEnabled", Hex.Parse("0042")),
            new("GetANCState(profile=0)", Hex.Parse("004900")),
            new("GetANCState(profile=1)", Hex.Parse("004901")),
            new("GetANCState(profile=2)", Hex.Parse("004902")),
            new($"GetKickitParams(profile={currentProfileId})", Hex.Parse($"004d{currentProfileId:x2}")),
            new("ReadBattery", Hex.Parse("007f")),
            new("GetEUAttenuation", Hex.Parse("0087")),
            new($"GetKickitEnabled(profile={currentProfileId})", Hex.Parse("00b4"))
        ];
    }
}
