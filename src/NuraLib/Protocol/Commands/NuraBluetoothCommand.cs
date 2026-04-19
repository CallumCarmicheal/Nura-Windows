using NuraLib.Crypto;
using NuraLib.Utilities;

namespace NuraLib.Protocol;

internal abstract class NuraBluetoothCommand<TResponse> {
    public abstract string Name { get; }

    public virtual string Source => GetType().Name;

    public abstract GaiaCommandId ExpectedResponseCommandId { get; }

    public abstract GaiaFrame CreateFrame(NuraSessionRuntime? runtime = null);

    public abstract TResponse ParseResponse(NuraSessionRuntime? runtime, GaiaResponse response);

    public virtual string DescribeRequest(GaiaFrame frame) =>
        $"tx.command=0x{(ushort)frame.CommandId:x4} tx.bytes={HexEncoding.Format(frame.Bytes)}";

    public virtual string DescribeResponse(GaiaResponse response) =>
        $"rx.command=0x{response.CommandId:x4} rx.status=0x{response.Status:x2} rx.payload={HexEncoding.Format(response.Payload)}";
}
