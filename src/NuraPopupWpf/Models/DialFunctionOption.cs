using NuraLib.Devices;

namespace NuraPopupWpf.Models;

public sealed record class DialFunctionOption(string Label, NuraDialFunction Value) {
    public override string ToString() => Label;
}
