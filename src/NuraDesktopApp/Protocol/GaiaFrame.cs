namespace desktop_app.Protocol;

internal sealed class GaiaFrame {
    public required GaiaCommandId CommandId { get; init; }

    public required byte[] Bytes { get; init; }
}
