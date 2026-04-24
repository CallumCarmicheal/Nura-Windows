namespace NuraPopupWpf.Models;

public sealed class WindowAnchorEdgeOption {
    public WindowAnchorEdgeOption(WindowAnchorEdge edge, string label, int row, int column) {
        Edge = edge;
        Label = label;
        Row = row;
        Column = column;
    }

    public WindowAnchorEdge Edge { get; }

    public string Label { get; }

    public int Row { get; }

    public int Column { get; }
}
