using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SkyBrowser.Core.Astronomy;

public sealed record EquipmentProfile(string Name, double WidthDegrees, double HeightDegrees, string Source = "", string Notes = "")
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || Name.Any(char.IsControl) ||
            !double.IsFinite(WidthDegrees) || !double.IsFinite(HeightDegrees) ||
            WidthDegrees is < 0.001 or > 100 || HeightDegrees is < 0.001 or > 100)
        {
            throw new ArgumentException("Enter a profile name and field width/height between 0.001° and 100°.");
        }
        if (Source is null || Notes is null || Source.Length > 2048 || Notes.Length > 2048)
        {
            throw new ArgumentException("Equipment source and notes must contain at most 2048 characters each.");
        }
    }

    public static EquipmentProfile FromSensor(string name, double sensorWidthMm, double sensorHeightMm, double focalLengthMm)
    {
        if (!double.IsFinite(focalLengthMm) || focalLengthMm <= 0 ||
            !double.IsFinite(sensorWidthMm) || sensorWidthMm <= 0 || !double.IsFinite(sensorHeightMm) || sensorHeightMm <= 0)
        {
            throw new ArgumentException("Sensor dimensions and effective focal length must be positive finite millimetres.");
        }
        var profile = new EquipmentProfile(name, 2 * Math.Atan(sensorWidthMm / (2 * focalLengthMm)) / SkyCoordinate.Radians,
            2 * Math.Atan(sensorHeightMm / (2 * focalLengthMm)) / SkyCoordinate.Radians);
        profile.Validate();
        return profile;
    }

    public override string ToString() => Name;
}

public sealed record SmartTelescopePreset(string Name, double Width, double Height, double Diagonal,
    double AspectWidth, double AspectHeight, string Source, string Notes)
{
    public EquipmentProfile ToProfile()
    {
        double width = Width, height = Height;
        if (Diagonal > 0)
        {
            // A rectilinear sensor's diagonal is the hypotenuse in tangent space, not in angles.
            double diagonal = Math.Tan(Diagonal * SkyCoordinate.Radians / 2);
            double length = Math.Sqrt(AspectWidth * AspectWidth + AspectHeight * AspectHeight);
            width = 2 * Math.Atan(diagonal * AspectWidth / length) / SkyCoordinate.Radians;
            height = 2 * Math.Atan(diagonal * AspectHeight / length) / SkyCoordinate.Radians;
        }
        var result = new EquipmentProfile(Name, width, height, Source, Notes);
        result.Validate();
        return result;
    }
}

public static class SmartTelescopeProfiles
{
    public static IReadOnlyList<EquipmentProfile> All { get; } = Load();

    private static IReadOnlyList<EquipmentProfile> Load()
    {
        using var stream = typeof(SmartTelescopeProfiles).Assembly.GetManifestResourceStream("SkyBrowser.Core.Data.smart-telescopes.json")!;
        return Array.AsReadOnly(JsonSerializer.Deserialize(stream, FramingJsonContext.Default.SmartTelescopePresetArray)!
            .Select(preset => preset.ToProfile()).OrderBy(profile => profile.Name, StringComparer.Ordinal).ToArray());
    }
}

public sealed class MosaicPanel
{
    internal MosaicPanel(int number, int row, int column, SkyCoordinate center, SkyVector eastAxis, SkyVector northAxis,
        double width, double height)
    {
        (Number, Row, Column, Center) = (number, row, column, center);
        _eastAxis = eastAxis;
        _northAxis = northAxis;
        _width = Math.Tan(width * SkyCoordinate.Radians / 2);
        _height = Math.Tan(height * SkyCoordinate.Radians / 2);
        RotationDegrees = SkyCoordinate.Normalize(Math.Atan2(SkyVector.Dot(northAxis, SkyVector.Cross(center.North, center.Vector)),
            SkyVector.Dot(northAxis, center.North)) / SkyCoordinate.Radians);
        Corners = Array.AsReadOnly(new[] { At(0, 0), At(1, 0), At(1, 1), At(0, 1) });
    }

    private readonly SkyVector _eastAxis, _northAxis;
    private readonly double _width, _height;
    public int Number { get; }
    public int Row { get; }
    public int Column { get; }
    public SkyCoordinate Center { get; }
    public double RotationDegrees { get; }
    public ReadOnlyCollection<SkyCoordinate> Corners { get; }
    public string Label => string.Create(CultureInfo.InvariantCulture,
        $"Panel {Number:00} · row {Row}, column {Column} · RA {Center.RightAscension:0.000000}° · Dec {Center.Declination:+0.000000;-0.000000;0.000000}° · rotation {RotationDegrees:0.00}°");

    public SkyCoordinate At(double u, double v) => SkyCoordinate.FromVector(Center.Vector +
        _eastAxis * ((1 - 2 * u) * _width) + _northAxis * ((1 - 2 * v) * _height));
    public override string ToString() => Label;
}

/// <summary>J2000 rectilinear fields. Requested overlap/spacing is measured in the central tangent plane.</summary>
public sealed class MosaicPlan
{
    private MosaicPlan(EquipmentProfile equipment, SkyCoordinate center, double rotation, int rows, int columns,
        double overlap, MosaicPanel[] panels) =>
        (Equipment, Center, RotationDegrees, Rows, Columns, OverlapPercent, Panels) =
        (equipment, center, rotation, rows, columns, overlap, Array.AsReadOnly(panels));

    public EquipmentProfile Equipment { get; }
    public SkyCoordinate Center { get; }
    public double RotationDegrees { get; }
    public int Rows { get; }
    public int Columns { get; }
    public double OverlapPercent { get; }
    public ReadOnlyCollection<MosaicPanel> Panels { get; }

    public static MosaicPlan Create(EquipmentProfile equipment, SkyCoordinate center, double rotationDegrees = 0,
        int rows = 1, int columns = 1, double overlapPercent = 20)
    {
        equipment.Validate();
        if (!double.IsFinite(center.RightAscension) || !double.IsFinite(center.Declination) || Math.Abs(center.Declination) > 90 ||
            !double.IsFinite(rotationDegrees) || rows is < 1 or > 10 || columns is < 1 or > 10 ||
            !double.IsFinite(overlapPercent) || overlapPercent is < 0 or > 80)
        {
            throw new ArgumentException("Use finite J2000 coordinates, declination −90° to +90°, 1–10 rows/columns and 0–80% overlap.");
        }
        center = center with { RightAscension = SkyCoordinate.Normalize(center.RightAscension) };
        double rotation = SkyCoordinate.Normalize(rotationDegrees);
        var (sin, cos) = Math.SinCos(rotation * SkyCoordinate.Radians);
        var east = SkyVector.Cross(center.North, center.Vector);
        var xAxis = east * cos - center.North * sin;
        var yAxis = east * sin + center.North * cos;
        double xStep = 2 * Math.Tan(equipment.WidthDegrees * SkyCoordinate.Radians / 2) * (1 - overlapPercent / 100);
        double yStep = 2 * Math.Tan(equipment.HeightDegrees * SkyCoordinate.Radians / 2) * (1 - overlapPercent / 100);
        var panels = new List<MosaicPanel>();
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                var target = SkyCoordinate.FromVector(center.Vector + xAxis * (((columns - 1) / 2d - column) * xStep) +
                    yAxis * (((rows - 1) / 2d - row) * yStep));
                // Parallel transport the grid axes by the shortest rotation to each panel center.
                // This remains well-defined across the RA seam and at the celestial poles.
                var axis = SkyVector.Cross(center.Vector, target.Vector);
                double dot = SkyVector.Dot(center.Vector, target.Vector);
                SkyVector Transport(SkyVector vector) => vector + SkyVector.Cross(axis, vector) +
                    SkyVector.Cross(axis, SkyVector.Cross(axis, vector)) / (1 + dot);
                var panel = new MosaicPanel(panels.Count + 1, row + 1, column + 1, target,
                    Transport(xAxis), Transport(yAxis), equipment.WidthDegrees, equipment.HeightDegrees);
                if (panel.Corners.Any(corner => center.DistanceTo(corner) > 55))
                {
                    throw new ArgumentException("This mosaic is too wide to frame reliably. Reduce the field size or number of panels (maximum 110° span).");
                }
                panels.Add(panel);
            }
        }
        return new(equipment, center, rotation, rows, columns, overlapPercent, panels.ToArray());
    }

    public string ToCsv()
    {
        static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        var text = new StringBuilder("Panel,Row,Column,RA_J2000_deg,Dec_J2000_deg,RA_J2000_hours,Rotation_CCW_deg,Width_deg,Height_deg,RequestedOverlap_percent,Equipment\r\n");
        foreach (var panel in Panels)
        {
            // Prefix formula-leading profile names in spreadsheet exports.
            string name = Equipment.Name.TrimStart();
            if (name.Length > 0 && "=+-@".Contains(name[0]))
            { name = "'" + name; }
            text.Append(CultureInfo.InvariantCulture,
                $"{panel.Number},{panel.Row},{panel.Column},{panel.Center.RightAscension:F8},{panel.Center.Declination:F8},{panel.Center.RightAscension / 15:F10},{panel.RotationDegrees:F6},{Equipment.WidthDegrees:F8},{Equipment.HeightDegrees:F8},{OverlapPercent:F2},{Quote(name)}\r\n");
        }
        return text.ToString();
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SmartTelescopePreset[]))]
[JsonSerializable(typeof(EquipmentProfile[]))]
internal partial class FramingJsonContext : JsonSerializerContext;
