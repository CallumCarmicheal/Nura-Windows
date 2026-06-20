using System.Windows;
using System.Windows.Media;

using NuraLib.Devices;
using NuraLib.Rendering;

namespace NuraPopupWpf.Controls;

public enum ProfileVisualBackgroundMode {
    Transparent,
    White
}

/// <summary>
/// Retained WPF contour renderer based on the Android native profile reference frame.
/// </summary>
internal sealed class NuraProfileShapeRenderer {
    private const int SegmentCount = NuraProfileReferenceCurve.TextureSampleCount;
    private const double TwoPi = Math.PI * 2.0;
    private const double GradientExtent = 0.72;
    private static readonly NuraProfileReferenceRgb NeutralCircleColour = new(0.31, 0.38, 0.47);

    private readonly DrawingGroup _drawing = new();
    private readonly RectangleGeometry _backgroundGeometry = new();
    private readonly GeometryDrawing _backgroundDrawing;
    private readonly RetainedContour[] _contours;
    private ShapeState? _state;

    internal NuraProfileShapeRenderer() {
        _backgroundDrawing = new GeometryDrawing(Brushes.Transparent, null, _backgroundGeometry);
        _drawing.Children.Add(_backgroundDrawing);
        _contours = Enumerable.Range(0, NuraProfileReferenceFrame.ContourCount)
            .Select(_ => CreateContour())
            .ToArray();

        foreach (var contour in _contours) {
            _drawing.Children.Add(contour.Drawing);
        }
    }

    internal DrawingGroup Render(
        NuraProfileVisualisationData fromProfile,
        NuraProfileVisualisationData toProfile,
        double profileBlendProgress,
        double modeProgress,
        double size,
        ProfileVisualBackgroundMode backgroundMode
    ) {
        var drawingSize = Math.Max(1.0, Math.Round(size));
        var blend = Math.Clamp(profileBlendProgress, 0.0, 1.0);
        var mode = Math.Clamp(modeProgress, 0.0, 1.0);

        if (_state is not null
            && ReferenceEquals(_state.FromProfile, fromProfile)
            && ReferenceEquals(_state.ToProfile, toProfile)
            && NearlyEqual(_state.BlendProgress, blend)
            && NearlyEqual(_state.ModeProgress, mode)
            && NearlyEqual(_state.Size, drawingSize)
            && _state.BackgroundMode == backgroundMode) {
            return _drawing;
        }

        var frame = NuraProfileReferenceFrameFactory.Create(toProfile, fromProfile, blend, mode);
        UpdateBackground(drawingSize, backgroundMode);
        UpdateContours(frame, drawingSize);
        _state = new ShapeState(fromProfile, toProfile, blend, mode, drawingSize, backgroundMode);
        return _drawing;
    }

    internal void Invalidate() => _state = null;

    private static RetainedContour CreateContour() {
        var figure = new PathFigure { IsClosed = true, IsFilled = true };
        var segment = new PolyLineSegment();
        for (var index = 1; index < SegmentCount; index++) {
            segment.Points.Add(default);
        }

        figure.Segments.Add(segment);
        var geometry = new PathGeometry([figure]);
        var brush = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute };
        for (var index = 0; index < 5; index++) {
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, index / 4.0));
        }

        return new RetainedContour(figure, segment, brush, new GeometryDrawing(brush, null, geometry));
    }

    private void UpdateBackground(double size, ProfileVisualBackgroundMode backgroundMode) {
        _backgroundGeometry.Rect = new Rect(0.0, 0.0, size, size);
        _backgroundDrawing.Brush = backgroundMode == ProfileVisualBackgroundMode.White
            ? Brushes.White
            : Brushes.Transparent;
    }

    private void UpdateContours(NuraProfileReferenceFrame frame, double size) {
        var centre = new Point(size * 0.5, size * 0.5);
        var gradientDirection = GetGradientDirection(frame);

        for (var contourIndex = 0; contourIndex < _contours.Length; contourIndex++) {
            var contour = _contours[contourIndex];
            var radiusScale = size * 0.25;

            for (var index = 0; index < SegmentCount; index++) {
                var angle = index / (double)SegmentCount;
                var radians = angle * TwoPi;
                var signature = frame.SampleSignature(angle);
                var radius = frame.GetContourRadius(contourIndex, signature) * radiusScale;
                var point = new Point(
                    centre.X + (Math.Sin(radians) * radius),
                    centre.Y - (Math.Cos(radians) * radius));

                if (index == 0) {
                    contour.Figure.StartPoint = point;
                } else {
                    contour.Segment.Points[index - 1] = point;
                }
            }

            UpdateBrush(contour.Brush, frame, contourIndex, centre, gradientDirection, size);
        }
    }

    private static Vector GetGradientDirection(NuraProfileReferenceFrame frame) {
        var direction = new Vector(
            frame.TotalCos + frame.TotalSin,
            -(frame.TotalCos - frame.TotalSin));

        if (direction.LengthSquared < 0.0000001) {
            return new Vector(1.0, -1.0);
        }

        direction.Normalize();
        return direction;
    }

    private static void UpdateBrush(
        LinearGradientBrush brush,
        NuraProfileReferenceFrame frame,
        int contourIndex,
        Point centre,
        Vector direction,
        double size
    ) {
        var extent = size * GradientExtent;
        brush.StartPoint = centre - (direction * extent);
        brush.EndPoint = centre + (direction * extent);

        var profileOpacity = frame.GetContourOpacity(contourIndex);
        var opacity = NuraProfileReferenceMath.Lerp(
            contourIndex == 0 ? 0.92 : 0.0,
            profileOpacity,
            frame.Personalisation);
        var baseColour = frame.GetContourColour(contourIndex);
        var slideAmounts = new[] { -0.105, -0.0525, 0.0, 0.0525, 0.105 };

        for (var index = 0; index < slideAmounts.Length; index++) {
            var contourColour = NuraProfileReferenceMath.HueRotate(baseColour, -slideAmounts[index]);
            var nativeNeutralColour = NuraProfileReferenceMath.Lerp(
                new NuraProfileReferenceRgb(1.0, 1.0, 1.0),
                contourColour,
                opacity);
            var profileColour = NuraProfileReferenceMath.Lerp(
                contourColour,
                nativeNeutralColour,
                1.0 - frame.Personalisation);
            var colour = NuraProfileReferenceMath.Lerp(NeutralCircleColour, profileColour, frame.Personalisation);
            brush.GradientStops[index].Color = ToColor(colour, opacity);
        }
    }

    private static Color ToColor(NuraProfileReferenceRgb colour, double opacity) => Color.FromArgb(
        ToByte(opacity),
        ToByte(colour.R),
        ToByte(colour.G),
        ToByte(colour.B));

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0.0, 1.0) * byte.MaxValue);

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) < 0.0001;

    private sealed record ShapeState(
        NuraProfileVisualisationData FromProfile,
        NuraProfileVisualisationData ToProfile,
        double BlendProgress,
        double ModeProgress,
        double Size,
        ProfileVisualBackgroundMode BackgroundMode);

    private sealed record RetainedContour(
        PathFigure Figure,
        PolyLineSegment Segment,
        LinearGradientBrush Brush,
        GeometryDrawing Drawing);
}
