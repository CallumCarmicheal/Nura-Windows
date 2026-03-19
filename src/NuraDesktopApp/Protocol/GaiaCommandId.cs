namespace desktop_app.Protocol;

internal enum GaiaCommandId : ushort {
    GetAddress = 0x0000,
    GetDeviceInfo = 0x0001,
    CryptoAppGenerateChallenge = 0x0002,
    CryptoAppValidateChallengeResponse = 0x0003,
    EntryAppEncryptedAuthenticated = 0x0006,
    EntryAppEncryptedUnauthenticated = 0x0007,
    ResponseAppEncryptedAuthenticated = 0x000A,
    ResponseAppEncryptedUnauthenticated = 0x000B,
    IndicationFromHeadset = 0x000E
}
