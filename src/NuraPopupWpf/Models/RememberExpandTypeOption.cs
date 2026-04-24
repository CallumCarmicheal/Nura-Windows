namespace NuraPopupWpf.Models;

public sealed class RememberExpandTypeOption {
    public RememberExpandTypeOption(RememberExpandType expandType, string label, string subtitle) {
        ExpandType = expandType;
        Label = label;
        Subtitle = subtitle;
    }

    public RememberExpandType ExpandType { get; }

    public string Label { get; }

    public string Subtitle { get; }

    public override string ToString() => Label;
}
