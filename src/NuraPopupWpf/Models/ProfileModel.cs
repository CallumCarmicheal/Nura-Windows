using NuraLib.Devices;

using System.Windows.Media;

namespace NuraPopupWpf.Models;

public sealed class ProfileModel {
    public string Name { get; init; }

    public NuraProfileVisualisationData VisualisationData { get; init; }

    public double Colour => VisualisationData.Colour;

    public IReadOnlyList<double> LeftData => VisualisationData.LeftData;

    public IReadOnlyList<double> RightData => VisualisationData.RightData;

    public ImageSource? Thumbnail { get; set; } = null;

    public ProfileModel(string name, NuraProfileVisualisationData visualisationData) {
        Name = name;
        VisualisationData = visualisationData;
    }

    public override string ToString() => Name;
}