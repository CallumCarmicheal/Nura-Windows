using NuraLib.Devices;

namespace NuraPopupWpf.Models;

public sealed record class ButtonFunctionOption(string Label, NuraButtonFunction? Value) {
    public override string ToString() => Label;
}
