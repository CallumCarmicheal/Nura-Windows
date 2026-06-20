using NuraLib.Devices;
using NuraLib.Rendering;

using System.Windows.Media;

using static System.Runtime.InteropServices.JavaScript.JSType;

namespace NuraDesktop.Models;

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

    public void RenderThumbnail(NuraProfileBitmapRenderer renderer, bool onlyRenderIfNull = true) {
        // Skip rendering the thumbnail again if not required
        if (onlyRenderIfNull && Thumbnail != null)
            return;

        Thumbnail = renderer.Render(VisualisationData, 48, useTransparency: true).ToBitmapSource();
    }

    public override string ToString() => Name;
}