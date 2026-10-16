using System.Globalization;

namespace SkyBrowser.Core.Astronomy;

public readonly record struct SkyVector(double X, double Y, double Z)
{
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public SkyVector Normalized => this / Length;

    // Record-generated formatting includes Normalized, recursively formatting another SkyVector.
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X:G6}, {Y:G6}, {Z:G6})");

    public static SkyVector operator +(SkyVector a, SkyVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static SkyVector operator -(SkyVector a, SkyVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static SkyVector operator *(SkyVector a, double scale) => new(a.X * scale, a.Y * scale, a.Z * scale);

    public static SkyVector operator /(SkyVector a, double scale) => a * (1 / scale);

    public static double Dot(SkyVector a, SkyVector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static SkyVector Cross(SkyVector a, SkyVector b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}

/// <summary>Equatorial J2000/ICRS direction. Angles are degrees.</summary>
public readonly record struct SkyCoordinate(double RightAscension, double Declination)
{
    public const double Radians = Math.PI / 180;

    public SkyVector Vector
    {
        get
        {
            var (sinRa, cosRa) = Math.SinCos(RightAscension * Radians);
            var (sinDec, cosDec) = Math.SinCos(Declination * Radians);
            return new(cosDec * cosRa, sinDec, -cosDec * sinRa);
        }
    }

    public SkyVector North => new(
        -Math.Sin(Declination * Radians) * Math.Cos(RightAscension * Radians),
        Math.Cos(Declination * Radians),
        Math.Sin(Declination * Radians) * Math.Sin(RightAscension * Radians)
    );

    public static SkyCoordinate FromVector(SkyVector vector) => new(
        Normalize(Math.Atan2(-vector.Z, vector.X) / Radians),
        Math.Asin(Math.Clamp(vector.Y / vector.Length, -1, 1)) / Radians
    );

    public double DistanceTo(SkyCoordinate other) => Math.Atan2(SkyVector.Cross(Vector, other.Vector).Length, SkyVector.Dot(Vector, other.Vector)) / Radians;

    public static double Normalize(double degrees) => (degrees % 360 + 360) % 360;

    public string RaText => TimeSpan.FromHours(Normalize(RightAscension) / 15).ToString(@"hh\h\ mm\m\ ss\s", CultureInfo.InvariantCulture);

    public string DecText => $"{(Declination < 0 ? "−" : "+")}{Math.Abs(Declination):00.000}°";

    public override string ToString() => $"{RaText} {DecText}";
}
