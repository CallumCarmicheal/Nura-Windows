using System.Windows.Media;

namespace NuraPopupWpf.Models;

public sealed class ProfileModel {
    public ProfileModel(string name, double colour, IReadOnlyList<double> leftData, IReadOnlyList<double> rightData) {
        Name = name;
        Colour = colour;
        LeftData = leftData;
        RightData = rightData;
    }

    public string Name { get; }

    public double Colour { get; }

    public IReadOnlyList<double> LeftData { get; }

    public IReadOnlyList<double> RightData { get; }

    public ImageSource? Thumbnail { get; set; }

    public override string ToString() => Name;
}
