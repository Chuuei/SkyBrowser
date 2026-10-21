using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Desktop.Controls;

public sealed partial class SkyViewport
{
    private sealed class SkyDrawing(SkyViewport owner, bool foreground) : FrameworkElement
    {
        private readonly (CatalogStar Star, SkyVector Direction)[] _stars = foreground ? [] : owner._catalog.Stars.Select(star => (star, star.Position.Vector)).ToArray();
        private readonly CatalogStar? _polaris = foreground ? owner._catalog.Stars.FirstOrDefault(star => star.Id == 11767) : null;
        private readonly Dictionary<(SkyCoordinate Start, SkyCoordinate End), SkyVector[]> _arcs = [];
        private readonly Dictionary<(string Text, string Color, double Size, double Dpi), FormattedText> _text = [];
        private readonly Dictionary<string, Brush> _textBrushes = [];
        private double _pixelsPerDip;
        private static readonly Typeface Font = new("Segoe UI");
        private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(5, 10, 18));
        private static readonly Pen GridPen = FrozenPen("#15283C", 0.65);
        private static readonly Pen EquatorPen = FrozenPen("#284857", 0.8);
        private static readonly Pen ConstellationPen = FrozenPen("#31405B", 0.85);
        private static readonly Pen FootprintPen = FrozenPen("#558C82", 0.8);
        private static readonly Pen SelectedPen = FrozenPen("#98EBD0", 1.3);
        private static readonly Pen HorizonPen = FrozenPen("#B6A077", 1.2);
        private readonly Brush[] _starBrushes = Enumerable.Range(0, 24)
            .Select(i =>
        {
            double temperature = (i % 3) / 2.0;
            byte brightness = (byte)(85 + (i / 3) * 23);
            var brush = new SolidColorBrush(Color.FromRgb(
                (byte)(brightness * (0.85 + 0.15 * temperature)),
                (byte)(brightness * 0.94),
                (byte)(brightness * (1 - 0.25 * temperature))
            ));
            brush.Freeze();
            return (Brush)brush;
        })
            .ToArray();

        private static Pen FrozenPen(string color, double thickness)
        {
            var pen = new Pen((Brush)new BrushConverter().ConvertFromInvariantString(color)!, thickness);
            pen.Freeze();
            return pen;
        }

        private Point? Project(SkyCoordinate c) => ProjectVector(c.Vector);

        private Point? ProjectVector(SkyVector direction) => owner.Camera.Project(direction, ActualWidth, ActualHeight) is { } p ? new(p.X, p.Y) : null;

        private bool Inside(Point p, double margin = 40) => p.X > -margin && p.Y > -margin && p.X < ActualWidth + margin && p.Y < ActualHeight + margin;

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (ActualWidth <= 0 || ActualHeight <= 0)
            {
                return;
            }

            _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (foreground)
            {
                DrawAnnotations(dc);
                return;
            }

            dc.DrawRectangle(BackgroundBrush, null, new Rect(RenderSize));
            if (owner.ShowGrid)
            {
                DrawGrid(dc);
            }

            if (owner.ShowConstellations)
            {
                foreach (var line in owner._catalog.ConstellationLines)
                {
                    for (int i = 1; i < line.Points.Count; i++)
                    {
                        DrawArc(dc, line.Points[i - 1], line.Points[i], ConstellationPen);
                    }
                }
            }

            foreach (var (star, direction) in _stars)
            {
                if (ProjectVector(direction) is not { } p || !Inside(p, 5))
                {
                    continue;
                }

                int brightness = (int)Math.Clamp((6.5 - star.Magnitude) * 1.35, 0, 7);
                int color = star.ColorIndex < 0.2 ? 0 : star.ColorIndex > 0.9 ? 2 : 1;
                double radius = Math.Clamp(2.6 - star.Magnitude * 0.32, 0.48, 3.1);
                var brush = _starBrushes[brightness * 3 + color];
                if (star.Magnitude < 1.5)
                {
                    dc.PushOpacity(0.11);
                    dc.DrawEllipse(brush, null, p, radius * 3.5, radius * 3.5);
                    dc.Pop();
                }

                dc.DrawEllipse(brush, null, p, radius, radius);
            }
        }

        private void DrawGrid(DrawingContext dc)
        {
            if (owner.Camera.Observer is { } observer)
            {
                DrawHorizontalGrid(dc, observer);
                return;
            }

            double spacing = owner.Camera.FieldOfView < 2
                ? 0.25
                : owner.Camera.FieldOfView < 8 ? 1 : owner.Camera.FieldOfView < 25 ? 5 : 15;
            // Only meridians/parallels near the view need dense sampling at high zoom.
            for (double ra = 0; ra < 360; ra += spacing)
            {
                double delta = Math.Abs((ra - owner.Camera.Center.RightAscension + 540) % 360 - 180);
                if (owner.Camera.FieldOfView < 25 &&
                    Math.Abs(owner.Camera.Center.Declination) < 75 &&
                    delta > owner.Camera.FieldOfView * 2)
                {
                    continue;
                }

                Point? previous = null;
                for (double dec = -89.9; dec <= 90; dec += 1)
                {
                    DrawSegment(dc, ref previous, Project(new(ra, dec)), GridPen);
                }
            }

            for (double dec = -90 + spacing; dec < 90; dec += spacing)
            {
                if (Math.Abs(dec - owner.Camera.Center.Declination) > owner.Camera.FieldOfView * 1.5)
                {
                    continue;
                }

                Point? previous = null;
                for (double ra = 0; ra <= 360; ra += 1)
                {
                    DrawSegment(dc, ref previous, Project(new(ra, dec)), Math.Abs(dec) < 0.01 ? EquatorPen : GridPen);
                }
            }
        }

        private void DrawHorizontalGrid(DrawingContext dc, ObserverFrame observer)
        {
            double spacing = owner.Camera.FieldOfView < 2
                ? 0.25
                : owner.Camera.FieldOfView < 8 ? 1 : owner.Camera.FieldOfView < 25 ? 5 : 15;
            var center = observer.ToHorizontal(owner.Camera.Center);

            Point? ProjectHorizontal(double azimuth, double altitude)
            {
                var (sinAz, cosAz) = Math.SinCos(azimuth * SkyCoordinate.Radians);
                var (sinAlt, cosAlt) = Math.SinCos(altitude * SkyCoordinate.Radians);
                // Project the horizon axes directly, avoiding an unnecessary RA/Dec
                // conversion for each sample. This grid stays fixed as the stars rotate.
                return ProjectVector(observer.North * (cosAlt * cosAz) + observer.East * (cosAlt * sinAz) + observer.Zenith * sinAlt);
            }

            for (double azimuth = 0; azimuth < 360; azimuth += spacing)
            {
                double delta = Math.Abs((azimuth - center.Azimuth + 540) % 360 - 180);
                if (owner.Camera.FieldOfView < 25 && Math.Abs(center.Altitude) < 75 && delta > owner.Camera.FieldOfView * 2)
                {
                    continue;
                }

                Point? previous = null;
                for (double altitude = -90; altitude <= 90; altitude += 1)
                {
                    DrawSegment(dc, ref previous, ProjectHorizontal(azimuth, altitude), GridPen);
                }
            }

            for (double altitude = -90 + spacing; altitude < 90; altitude += spacing)
            {
                if (Math.Abs(altitude - center.Altitude) > owner.Camera.FieldOfView * 1.5 || Math.Abs(altitude) < 0.001)
                {
                    continue; // The horizon is drawn separately with its compass labels.
                }

                Point? previous = null;
                for (double azimuth = 0; azimuth <= 360; azimuth++)
                {
                    DrawSegment(dc, ref previous, ProjectHorizontal(azimuth, altitude), GridPen);
                }
            }
        }

        private void DrawAnnotations(DrawingContext dc)
        {
            DrawSkyTools(dc);
            if (owner.Camera.Observer is { } observer)
            {
                Point? previous = null;
                for (int azimuth = 0; azimuth <= 360; azimuth++)
                {
                    DrawSegment(dc, ref previous, Project(observer.FromHorizontal(azimuth, 0)), HorizonPen);
                }

                foreach (var (azimuth, name) in new[]
                {
                    (0, "NORTH"),
                    (90, "EAST"),
                    (180, "SOUTH"),
                    (270, "WEST")
                }

                )
                {
                    if (Project(observer.FromHorizontal(azimuth, 1.5)) is { } point && Inside(point))
                    {
                        Text(dc, name, point + new Vector(-18, -18), "#D8C398", 11);
                    }
                }

                if (owner.ShowLabels)
                {
                    if (ProjectVector(observer.Zenith) is { } zenith && Inside(zenith))
                    {
                        dc.DrawEllipse(null, HorizonPen, zenith, 5, 5);
                        Text(dc, "ZENITH · Alt 90°", zenith + new Vector(10, -8), "#D8C398", 11);
                    }

                    if (_polaris is { } polaris && Project(polaris.Position) is { } pole && Inside(pole))
                    {
                        double altitude = observer.ToHorizontal(polaris.Position).Altitude;
                        dc.DrawEllipse(null, FootprintPen, pole, 5, 5);
                        Text(dc, $"Polaris · Alt {altitude:0.0}°", pole + new Vector(10, -8), "#A5E8D1", 11);
                    }
                }
            }

            if (owner.ShowLabels)
            {
                if (owner.ShowConstellations)
                {
                    foreach (var label in owner._catalog.ConstellationNames)
                    {
                        if (Project(label.Position) is { } p && Inside(p))
                        {
                            Text(dc, label.Name.ToUpperInvariant(), p, "#67778E", 11);
                        }
                    }
                }

                foreach (var target in DeepSkyCatalog.PopularTargets)
                {
                    if (!owner._addedTargets.ContainsKey(target.Id) && Project(target.Position) is { } p && Inside(p))
                    {
                        dc.DrawEllipse(null, FootprintPen, p, 3, 3);
                        Text(dc, target.Name, p + new Vector(9, -8), "#8AABA9", 10);
                    }
                }
            }

            foreach (var target in owner.AddedTargets)
            {
                DrawAddedTarget(dc, target);
            }

            if (owner.ShowFootprints)
            {
                foreach (var layer in owner.Layers.Where(l => l.IsVisible && l.Placement is not null))
                {
                    var placement = layer.Placement!;
                    var points = owner._meshes.TryGetValue(layer, out var cached) && cached.Placement == placement
                        ? cached.Mesh.Positions
                        : null;
                    var pen = layer == owner.SelectedLayer || layer == owner.HoveredLayer ? SelectedPen : FootprintPen;
                    for (int edge = 0; edge < 4; edge++)
                    {
                        Point? previous = null;
                        for (int i = 0; i <= 32; i++)
                        {
                            double t = i / 32.0;
                            SkyVector direction;
                            if (points is not null)
                            {
                                int index = edge switch
                                {
                                    0 => i,
                                    1 => i * 33 + 32,
                                    2 => 32 * 33 + 32 - i,
                                    _ => (32 - i) * 33
                                };
                                var point = points[index];
                                direction = new(point.X, point.Y, point.Z);
                            }
                            else
                            {
                                direction = (edge switch
                                {
                                    0 => placement.At(t, 0),
                                    1 => placement.At(1, t),
                                    2 => placement.At(1 - t, 1),
                                    _ => placement.At(0, 1 - t)
                                }).Vector;
                            }

                            DrawSegment(dc, ref previous, ProjectVector(direction), pen);
                        }
                    }

                    var topLeft = points is not null ? new SkyVector(points[0].X, points[0].Y, points[0].Z) : placement.At(0, 0).Vector;
                    if (owner.ShowLabels && ProjectVector(topLeft) is { } anchor && Inside(anchor))
                    {
                        Text(dc, layer.Name, anchor + new Vector(5, -20), "#A5E8D1", 11);
                    }
                }
            }

            // Small center reticle remains useful when all catalogue labels are hidden.
            Point center = new(ActualWidth / 2, ActualHeight / 2);
            dc.PushOpacity(0.35);
            dc.DrawLine(FootprintPen, center + new Vector(-8, 0), center + new Vector(-3, 0));
            dc.DrawLine(FootprintPen, center + new Vector(3, 0), center + new Vector(8, 0));
            dc.DrawLine(FootprintPen, center + new Vector(0, -8), center + new Vector(0, -3));
            dc.DrawLine(FootprintPen, center + new Vector(0, 3), center + new Vector(0, 8));
            dc.Pop();
            if (owner.ShowMoon) { DrawMoon(dc); }
        }

        private void DrawSkyTools(DrawingContext dc)
        {
            if (owner.FramingPlan is { } plan)
            {
                foreach (var panel in plan.Panels)
                {
                    var pen = panel.Number == owner.SelectedFramingPanel ? SelectedPen : FootprintPen;
                    for (int edge = 0; edge < 4; edge++)
                    {
                        Point? previous = null;
                        for (int step = 0; step <= 16; step++)
                        {
                            double t = step / 16d;
                            var coordinate = edge switch
                            {
                                0 => panel.At(t, 0), 1 => panel.At(1, t),
                                2 => panel.At(1 - t, 1), _ => panel.At(0, 1 - t)
                            };
                            DrawSegment(dc, ref previous, Project(coordinate), pen);
                        }
                    }
                    if (Project(panel.Center) is { } center && Inside(center))
                    {
                        dc.DrawEllipse(Brushes.Black, pen, center, 12, 12);
                        Text(dc, panel.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), center + new Vector(-6, -8), "#8BE2C3", 12);
                    }
                }
            }
            if (owner.ShowCatalogTargets)
            {
                int count = 0;
                var candidates = owner.Camera.FieldOfView <= 15 ? DeepSkyCatalog.Targets : DeepSkyCatalog.PopularTargets;
                foreach (var target in candidates)
                {
                    if (Project(target.Position) is not { } point || !Inside(point, 0)) { continue; }
                    dc.DrawEllipse(null, FootprintPen, point, 4, 4);
                    if (owner.ShowLabels) { Text(dc, target.PreferredDesignation, point + new Vector(7, -12), "#829FB4", 10); }
                    if (++count >= 250) { break; }
                }
            }
            if (owner.Measurement is { } measurement)
            {
                Point? previous = null;
                for (int i = 0; i <= 128; i++)
                {
                    DrawSegment(dc, ref previous, Project(SkyGeometry.Interpolate(measurement.Start, measurement.End, i / 128d)), SelectedPen);
                }
                foreach (var coordinate in new[] { measurement.Start, measurement.End })
                {
                    if (Project(coordinate) is { } point && Inside(point)) { dc.DrawEllipse(null, SelectedPen, point, 4, 4); }
                }
                if (Project(SkyGeometry.Interpolate(measurement.Start, measurement.End, 0.5)) is { } midpoint && Inside(midpoint))
                {
                    Text(dc, measurement.DisplayLabel, midpoint + new Vector(8, -20), "#8BE2C3", 12);
                }
            }
            if (owner.ShowNorthEast && Math.Abs(owner.Camera.Center.Declination) < 89.9999)
            {
                Point origin = new(ActualWidth - 65, 65);
                double step = Math.Min(1, owner.Camera.FieldOfView / 10);
                var center = Project(owner.Camera.Center)!.Value;
                foreach (var (angle, label) in new[] { (0d, "N"), (90d, "E") })
                {
                    if (Project(SkyGeometry.Offset(owner.Camera.Center, angle, step)) is not { } endpoint) { continue; }
                    var direction = endpoint - center;
                    if (direction.Length < 1e-6) { continue; }
                    direction.Normalize();
                    var tip = origin + direction * 30;
                    dc.DrawLine(SelectedPen, origin, tip);
                    Text(dc, label, tip + direction * 8 + new Vector(-4, -7), "#8BE2C3", 12);
                }
            }
            if (owner.ShowScaleBar && ActualWidth > 250 && ActualHeight > 120)
            {
                double x = ActualWidth - 180, y = ActualHeight - 65;
                var start = owner.Camera.Unproject(x, y, ActualWidth, ActualHeight);
                double reference = start.DistanceTo(owner.Camera.Unproject(x + 120, y, ActualWidth, ActualHeight));
                double power = Math.Pow(10, Math.Floor(Math.Log10(reference)));
                double length = new[] { 1d, 2d, 5d, 10d }.Select(n => n * power).Last(n => n <= reference);
                double low = 0, high = 120;
                for (int i = 0; i < 32; i++)
                {
                    double middle = (low + high) / 2;
                    if (start.DistanceTo(owner.Camera.Unproject(x + middle, y, ActualWidth, ActualHeight)) < length) { low = middle; }
                    else { high = middle; }
                }
                double width = (low + high) / 2;
                dc.DrawLine(SelectedPen, new(x, y), new(x + width, y));
                dc.DrawLine(SelectedPen, new(x, y - 4), new(x, y + 4));
                dc.DrawLine(SelectedPen, new(x + width, y - 4), new(x + width, y + 4));
                Text(dc, SkyGeometry.FormatAngle(length), new(x, y - 22), "#8BE2C3", 11);
            }
            if (owner._surveyFrames.Length > 0 && owner.SurveyOpacity > 0)
            {
                string attribution = string.Join("\n", owner._surveyFrames.Select(frame => frame.Request.Survey.Attribution).Distinct());
                var credit = new FormattedText(attribution, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 9, Brushes.LightSlateGray, _pixelsPerDip)
                {
                    MaxTextWidth = Math.Max(40, ActualWidth - 20)
                };
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(215, 5, 10, 18)), null,
                    new Rect(5, ActualHeight - credit.Height - 9, Math.Max(40, ActualWidth - 10), credit.Height + 6));
                dc.DrawText(credit, new(10, ActualHeight - credit.Height - 6));
            }
        }

        private void DrawAddedTarget(DrawingContext dc, DeepSkyTarget target)
        {
            if (Project(target.Position) is not { } center || !Inside(center))
            {
                return;
            }

            dc.DrawEllipse(null, SelectedPen, center, 5, 5);
            dc.DrawLine(SelectedPen, center + new Vector(-9, 0), center + new Vector(-6, 0));
            dc.DrawLine(SelectedPen, center + new Vector(6, 0), center + new Vector(9, 0));
            dc.DrawLine(SelectedPen, center + new Vector(0, -9), center + new Vector(0, -6));
            dc.DrawLine(SelectedPen, center + new Vector(0, 6), center + new Vector(0, 9));
            if (target.MajorArcMinutes is > 0 and < 1800)
            {
                double major = Math.Tan(target.MajorArcMinutes.Value / 120 * SkyCoordinate.Radians);
                double minor = Math.Tan((target.MinorArcMinutes ?? target.MajorArcMinutes.Value) / 120 * SkyCoordinate.Radians);
                double angle = (target.PositionAngleDegrees ?? 0) * SkyCoordinate.Radians;
                var east = SkyVector.Cross(target.Position.North, target.Position.Vector);
                var majorAxis = target.Position.North * Math.Cos(angle) + east * Math.Sin(angle);
                var minorAxis = east * Math.Cos(angle) - target.Position.North * Math.Sin(angle);
                Point? previous = null;
                for (int i = 0; i <= 64; i++)
                {
                    double t = i * Math.Tau / 64;
                    var point = SkyCoordinate.FromVector(target.Position.Vector + majorAxis * (major * Math.Cos(t)) + minorAxis * (minor * Math.Sin(t)));
                    DrawSegment(dc, ref previous, Project(point), FootprintPen);
                }
            }

            if (owner.ShowLabels)
            {
                Text(dc, target.Name, center + new Vector(12, -10), "#A5E8D1", 11);
            }
        }

        private void DrawMoon(DrawingContext dc)
        {
            var moon = owner.CurrentMoon;
            if (Project(moon.Position) is not { } center || !Inside(center, MoonGlyph.Diameter)) { return; }
            double angle = 0;
            if (ProjectVector(moon.Position.Vector + moon.Position.North * .001) is { } north)
            {
                var direction = north - center;
                angle = Math.Atan2(direction.X, -direction.Y) / SkyCoordinate.Radians;
            }
            double radius = MoonGlyph.Diameter / 2;
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(24, 174, 193, 222)), null, center, radius + 3, radius + 3);
            dc.PushTransform(new RotateTransform(angle, center.X, center.Y));
            dc.DrawImage(owner.MoonImage, new Rect(center.X - radius, center.Y - radius, MoonGlyph.Diameter, MoonGlyph.Diameter));
            dc.Pop();
            if (owner.ShowLabels)
            {
                Text(dc, string.Create(CultureInfo.InvariantCulture, $"Moon · {moon.PhaseName} · {moon.IlluminatedFraction:P0}"),
                    center + new Vector(radius + 8, -8), "#D8E4F0", 12);
            }
        }

        private void DrawArc(DrawingContext dc, SkyCoordinate start, SkyCoordinate end, Pen pen)
        {
            if (!_arcs.TryGetValue((start, end), out var points))
            {
                int steps = Math.Max(2, (int)Math.Ceiling(start.DistanceTo(end)));
                points = new SkyVector[steps + 1];
                var a = start.Vector;
                var b = end.Vector;
                for (int i = 0; i <= steps; i++)
                {
                    points[i] = SkyCoordinate.FromVector(a * (1 - i / (double)steps) + b * (i / (double)steps)).Vector;
                }

                _arcs.Add((start, end), points);
            }

            Point? previous = null;
            foreach (var point in points)
            {
                DrawSegment(dc, ref previous, ProjectVector(point), pen);
            }
        }

        private void DrawSegment(DrawingContext dc, ref Point? previous, Point? point, Pen pen)
        {
            if (previous is { } a &&
                point is { } b &&
                (Inside(a, ActualWidth) ||
                Inside(b, ActualWidth)) &&
                (a - b).Length < ActualWidth * 2)
            {
                dc.DrawLine(pen, a, b);
            }

            previous = point;
        }

        private void Text(DrawingContext dc, string text, Point point, string color, double size)
        {
            var key = (text, color, size, _pixelsPerDip);
            if (!_text.TryGetValue(key, out var formatted))
            {
                if (!_textBrushes.TryGetValue(color, out var brush))
                {
                    brush = (Brush)new BrushConverter().ConvertFromInvariantString(color)!;
                    brush.Freeze();
                    _textBrushes.Add(color, brush);
                }

                formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, brush, _pixelsPerDip);
                // Image/target names can grow while browsing; retain a bounded set of layouts.
                if (_text.Count >= 1024)
                {
                    _text.Clear();
                }

                _text.Add(key, formatted);
            }

            dc.DrawText(formatted, point);
        }
    }
}
