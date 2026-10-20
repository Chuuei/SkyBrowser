namespace SkyBrowser.Core.Astronomy;

/// <summary>Shared sky coverage and perspective framing for image comparisons.</summary>
public static class ImageComparisonGeometry
{
    public static bool CanCompare(IReadOnlyList<ImagePlacement> placements)
    {
        if (placements.Count < 2) { return false; }
        var footprints = new SkyFootprint?[placements.Count];
        for (int i = 0; i < placements.Count; i++)
        {
            footprints[i] ??= new(placements[i]);
            for (int j = i + 1; j < placements.Count; j++)
            {
                footprints[j] ??= new(placements[j]);
                if (!footprints[i]!.Overlaps(footprints[j]!)) { return false; }
            }
        }
        return true;
    }

    public static (SkyCoordinate Center, double FieldOfView) FramePair(
        ImagePlacement first, ImagePlacement second, double aspectRatio)
    {
        if (!double.IsFinite(aspectRatio) || aspectRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(aspectRatio));
        }
        var camera = new SkyCamera
        {
            // Put the swipe boundary through shared sky, including an off-centre inset.
            Center = new SkyFootprint(first).OverlapCenter(new(second)) ??
                SkyCoordinate.FromVector(first.Center.Vector + second.Center.Vector)
        };
        double extent = 0;
        foreach (var point in first.CaptureSampleGrid().Concat(second.CaptureSampleGrid()))
        {
            var direction = point.Vector;
            double depth = SkyVector.Dot(direction, camera.Forward);
            if (depth <= 0.001)
            {
                return (camera.Center, 120);
            }
            extent = Math.Max(extent, Math.Max(Math.Abs(SkyVector.Dot(direction, camera.Right)),
                Math.Abs(SkyVector.Dot(direction, camera.Up)) * aspectRatio) / depth);
        }
        return (camera.Center, Math.Clamp(2 * Math.Atan(extent * 1.12) / SkyCoordinate.Radians, 0.05, 120));
    }
}
