namespace NuraLib.Devices;

public enum NuraDeviceOperationKind {
    None = 0,
    ConnectLocal,
    Refresh,
    Provision,
    Monitoring,
    ProfileChange,
    UpdateAnc,
    UpdatePassthrough,
    UpdateAncLevel,
    UpdateSpatial,
    UpdatePersonalisationMode,
    UpdateImmersion,
    UpdateTouchButtons,
    UpdateDial
}
