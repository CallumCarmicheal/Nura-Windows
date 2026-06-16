namespace NuraLib.Protocol;

internal sealed class GaiaCommandException : InvalidOperationException {
    public GaiaCommandException(
        string commandName,
        GaiaCommandId requestCommandId,
        GaiaCommandId expectedResponseCommandId,
        GaiaResponse response)
        : base(CreateMessage(commandName, requestCommandId, expectedResponseCommandId, response)) {
        CommandName = commandName;
        RequestCommandId = requestCommandId;
        ExpectedResponseCommandId = expectedResponseCommandId;
        ResponseCommandId = (GaiaCommandId)response.CommandId;
        Status = response.Status;
    }

    public string CommandName { get; }

    public GaiaCommandId RequestCommandId { get; }

    public GaiaCommandId ExpectedResponseCommandId { get; }

    public GaiaCommandId ResponseCommandId { get; }

    public byte Status { get; }

    private static string CreateMessage(
        string commandName,
        GaiaCommandId requestCommandId,
        GaiaCommandId expectedResponseCommandId,
        GaiaResponse response) {
        return $"{commandName} failed: request=0x{(ushort)requestCommandId:x4} expected_response=0x{(ushort)expectedResponseCommandId:x4} actual_response=0x{response.CommandId:x4} status=0x{response.Status:x2}";
    }
}
