using System.Globalization;
using XISFLib;

namespace SkyBrowser.Core.Astronomy;

// A compact, exact forward model is persisted alongside the display mesh. A mesh
// alone cannot retain high-order distortion between its 33 x 33 sample points.
internal sealed record FitsSolutionData(string Projection, double[] ReferencePixel, double[] ReferenceSky,
    double[] Matrix, double[]? SipA, double[]? SipB, double[]? Pv1, double[]? Pv2, bool RotateAtNorthPole = false);

/// <summary>FITS pixel-to-sky evaluation, including registered SIP and TPV distortion.</summary>
internal sealed class FitsSkySolution
{
    private readonly FitsSolutionData _data;
    public string Description => "FITS WCS · " + _data.Projection;

    public FitsSkySolution(FitsSolutionData data)
    {
        static bool Valid(double[]? a, int count) => a is not null && a.Length == count && a.All(double.IsFinite);
        if (data.Projection is not ("TAN" or "TAN-SIP" or "TPV" or "SIN" or "STG" or "ZEA" or "ARC") ||
            !Valid(data.ReferencePixel, 2) || !Valid(data.ReferenceSky, 2) || Math.Abs(data.ReferenceSky[1]) > 90 ||
            !Valid(data.Matrix, 4) || !double.IsFinite(data.Matrix[0] * data.Matrix[3] - data.Matrix[1] * data.Matrix[2]) ||
            Math.Abs(data.Matrix[0] * data.Matrix[3] - data.Matrix[1] * data.Matrix[2]) < 1e-30 ||
            (data.RotateAtNorthPole && data.ReferenceSky[1] != 90) ||
            (data.Projection != "TAN-SIP" && (data.SipA is not null || data.SipB is not null)) ||
            (data.Projection != "TPV" && (data.Pv1 is not null || data.Pv2 is not null)) ||
            (data.Projection == "TAN-SIP" && (!Valid(data.SipA, 100) || !Valid(data.SipB, 100))) ||
            (data.Projection == "TPV" && (!Valid(data.Pv1, 40) || !Valid(data.Pv2, 40))))
        {
            throw new InvalidDataException("Invalid or unsupported FITS WCS coefficients.");
        }
        _data = data with
        {
            ReferencePixel = data.ReferencePixel.ToArray(),
            ReferenceSky = data.ReferenceSky.ToArray(),
            Matrix = data.Matrix.ToArray(),
            SipA = data.SipA?.ToArray(),
            SipB = data.SipB?.ToArray(),
            Pv1 = data.Pv1?.ToArray(),
            Pv2 = data.Pv2?.ToArray()
        };
    }

    public FitsSolutionData Capture() => _data with
    {
        ReferencePixel = _data.ReferencePixel.ToArray(),
        ReferenceSky = _data.ReferenceSky.ToArray(),
        Matrix = _data.Matrix.ToArray(),
        SipA = _data.SipA?.ToArray(),
        SipB = _data.SipB?.ToArray(),
        Pv1 = _data.Pv1?.ToArray(),
        Pv2 = _data.Pv2?.ToArray()
    };

    public SkyCoordinate PixelToSky(double x, double y)
    {
        double u = x + 1 - _data.ReferencePixel[0], v = y + 1 - _data.ReferencePixel[1];
        if (_data.SipA is { } a && _data.SipB is { } b)
        {
            // SIP operates on offsets in pixels, before the linear CD transformation.
            double du = Sip(a, u, v), dv = Sip(b, u, v);
            u += du;
            v += dv;
        }
        double xi = _data.Matrix[0] * u + _data.Matrix[1] * v;
        double eta = _data.Matrix[2] * u + _data.Matrix[3] * v;
        if (_data.Pv1 is { } p && _data.Pv2 is { } q)
        {
            // TPV operates in projection-plane degrees after CD. The second axis
            // deliberately reverses its monomial arguments (PV2_1 multiplies eta).
            double correctedX = Tpv(p, xi, eta), correctedY = Tpv(q, eta, xi);
            xi = correctedX;
            eta = correctedY;
        }
        // FITS defaults LONPOLE to 0 at an exact north-pole reference, and to
        // 180 for the other supported zenithal reference points.
        if (_data.RotateAtNorthPole)
        {
            xi = -xi;
            eta = -eta;
        }
        return Deproject(xi, eta, _data.ReferenceSky[0], _data.ReferenceSky[1], _data.Projection);
    }

    private static double Sip(double[] coefficients, double u, double v)
    {
        double sum = 0;
        for (int i = 9; i >= 0; i--)
        {
            double row = 0;
            for (int j = 9; j >= 0; j--)
            {
                row = row * v + coefficients[i * 10 + j];
            }
            sum = sum * u + row;
        }
        return sum;
    }

    private static double Tpv(double[] coefficients, double x, double y)
    {
        double r = Math.Sqrt(x * x + y * y);
        double sum = coefficients[0] + coefficients[1] * x + coefficients[2] * y + coefficients[3] * r;
        int index = 4;
        for (int degree = 2; degree <= 7; degree++)
        {
            for (int j = 0; j <= degree; j++)
            {
                sum += coefficients[index++] * Math.Pow(x, degree - j) * Math.Pow(y, j);
            }
            if (degree % 2 == 1)
            {
                sum += coefficients[index++] * Math.Pow(r, degree);
            }
        }
        return sum;
    }

    internal static SkyCoordinate Deproject(double xi, double eta, double ra, double dec, string projection = "TAN")
    {
        double x = xi * SkyCoordinate.Radians, y = eta * SkyCoordinate.Radians;
        double radius = Math.Sqrt(x * x + y * y);
        if (!double.IsFinite(radius))
        {
            throw new InvalidDataException("Non-finite distorted sky coordinate.");
        }
        if (radius == 0)
        {
            return new(ra, dec);
        }
        if ((projection == "SIN" && radius > 1) || (projection == "ZEA" && radius > 2) || (projection == "ARC" && radius >= Math.PI))
        {
            throw new InvalidDataException("Image lies outside its celestial projection domain.");
        }
        double angle = projection switch
        {
            "SIN" => Math.Asin(radius),
            "STG" => 2 * Math.Atan(radius / 2),
            "ZEA" => 2 * Math.Asin(radius / 2),
            "ARC" => radius,
            _ => Math.Atan(radius)
        };
        var (sin, cos) = Math.SinCos(angle);
        var (sinDec, cosDec) = Math.SinCos(dec * SkyCoordinate.Radians);
        double latitude = cos * sinDec + y / radius * sin * cosDec;
        double east = x / radius * sin, north = cosDec * cos - y / radius * sinDec * sin;
        double longitude = Math.Atan2(east, north);
        return new(ra + longitude / SkyCoordinate.Radians, Math.Atan2(latitude, Math.Sqrt(east * east + north * north)) / SkyCoordinate.Radians);
    }

    public static FitsSkySolution? TryCreate(IEnumerable<XisfFitsKeyword> keywords, out string? warning)
    {
        warning = null;
        var values = keywords.GroupBy(k => k.Name.Trim().ToUpperInvariant()).ToDictionary(g => g.Key,
            g => g.Last().Value.Trim().Trim('\'').Trim());
        if (!values.ContainsKey("CRVAL1"))
        {
            return null;
        }
        string Get(string key) => values.GetValueOrDefault(key, "").ToUpperInvariant();
        double Number(string key, double? fallback = null)
        {
            if (!values.ContainsKey(key) && fallback is { } missing)
            {
                return missing;
            }
            if (!double.TryParse(Get(key).Replace('D', 'E'), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) || !double.IsFinite(number))
            {
                throw new InvalidDataException($"Missing or invalid {key}.");
            }
            return number;
        }
        try
        {
            bool swapped = Get("CTYPE1").StartsWith("DEC--", StringComparison.Ordinal) && Get("CTYPE2").StartsWith("RA---", StringComparison.Ordinal);
            string first = Get(swapped ? "CTYPE2" : "CTYPE1"), second = Get(swapped ? "CTYPE1" : "CTYPE2");
            string projection = first.StartsWith("RA---", StringComparison.Ordinal) ? first[5..] : "";
            if (second != "DEC--" + projection || projection is not ("TAN" or "TAN-SIP" or "TPV" or "SIN" or "STG" or "ZEA" or "ARC"))
            {
                throw new NotSupportedException("unsupported celestial projection or axis types");
            }
            string frame = Get("RADESYS") is { Length: > 0 } system ? system : Get("RADECSYS");
            double referenceDec = Number(swapped ? "CRVAL1" : "CRVAL2");
            double pole = Number("LONPOLE", referenceDec == 90 ? 0 : 180);
            double latitudePole = Number("LATPOLE", 90);
            // Zenithal CRVAL identifies the native pole. WCSLIB writes its solved
            // latitude (CRVAL declination); an omitted/default LATPOLE=90 is only
            // the pole-selection preference. Both describe the same rotation.
            if (frame is not ("" or "ICRS" or "FK5") || Number("EQUINOX", 2000) != 2000 ||
                Get("CUNIT1") is not ("" or "DEG") || Get("CUNIT2") is not ("" or "DEG") ||
                (pole != 180 && !(pole == 0 && referenceDec == 90)) ||
                (latitudePole != 90 && Math.Abs(latitudePole - referenceDec) > 1e-10))
            {
                throw new NotSupportedException("unsupported coordinate frame, units or pole orientation");
            }
            if (values.Keys.Any(k => k.StartsWith("CPDIS", StringComparison.Ordinal) || k.StartsWith("D2IM", StringComparison.Ordinal) ||
                k.StartsWith("DP1", StringComparison.Ordinal) || k.StartsWith("DP2", StringComparison.Ordinal) || k.StartsWith("DQ", StringComparison.Ordinal)))
            {
                throw new NotSupportedException("unsupported lookup-table distortion");
            }
            double[] matrix;
            if (values.Keys.Any(k => k is "CD1_1" or "CD1_2" or "CD2_1" or "CD2_2"))
            {
                matrix = [Number("CD1_1", 0), Number("CD1_2", 0), Number("CD2_1", 0), Number("CD2_2", 0)];
            }
            else
            {
                double dx = Number("CDELT1"), dy = Number("CDELT2");
                if (values.Keys.Any(k => k.StartsWith("PC1_", StringComparison.Ordinal) || k.StartsWith("PC2_", StringComparison.Ordinal)))
                {
                    matrix = [dx * Number("PC1_1", 1), dx * Number("PC1_2", 0), dy * Number("PC2_1", 0), dy * Number("PC2_2", 1)];
                }
                else
                {
                    var (sin, cos) = Math.SinCos(Number("CROTA2", Number("CROTA1", 0)) * SkyCoordinate.Radians);
                    matrix = [dx * cos, -dy * sin, dx * sin, dy * cos];
                }
            }
            bool hasSip = values.Keys.Any(k => k is "A_ORDER" or "B_ORDER" || k.StartsWith("A_", StringComparison.Ordinal) || k.StartsWith("B_", StringComparison.Ordinal));
            bool hasPv = values.Keys.Any(k => k.StartsWith("PV1_", StringComparison.Ordinal) || k.StartsWith("PV2_", StringComparison.Ordinal));
            if ((hasSip || projection == "TAN-SIP") && (hasPv || projection == "TPV"))
            {
                throw new NotSupportedException("unsupported mixed SIP/TPV distortion");
            }
            double[]? a = null, b = null, p = null, q = null;
            if (hasSip || projection == "TAN-SIP")
            {
                if (projection is not ("TAN" or "TAN-SIP"))
                {
                    throw new NotSupportedException("SIP requires a TAN projection");
                }
                a = ReadSip("A");
                b = ReadSip("B");
                projection = "TAN-SIP";
            }
            if (hasPv || projection == "TPV")
            {
                if (swapped)
                {
                    throw new NotSupportedException("TPV requires RA on the first celestial axis and Dec on the second");
                }
                if (projection is not ("TAN" or "TPV"))
                {
                    throw new NotSupportedException("TPV requires a tangent plane");
                }
                if (projection == "TAN" && (!values.Keys.Any(k => k.StartsWith("PV1_", StringComparison.Ordinal)) ||
                    !values.Keys.Any(k => k.StartsWith("PV2_", StringComparison.Ordinal))))
                {
                    throw new NotSupportedException("ambiguous TAN/PV metadata; legacy TPV needs coefficients for both axes");
                }
                p = ReadPv(swapped ? "PV2_" : "PV1_");
                q = ReadPv(swapped ? "PV1_" : "PV2_");
                projection = "TPV";
            }
            double[] sky = [Number("CRVAL1"), Number("CRVAL2")];
            if (swapped)
            {
                sky = [sky[1], sky[0]];
                matrix = [matrix[2], matrix[3], matrix[0], matrix[1]];
            }
            return new(new(projection, [Number("CRPIX1"), Number("CRPIX2")], sky, matrix, a, b, p, q,
                pole == 0));

            double[] ReadSip(string prefix)
            {
                double order = Number(prefix + "_ORDER");
                if (order is < 2 or > 9 || order != Math.Truncate(order))
                {
                    throw new InvalidDataException("SIP orders must be integers from 2 to 9.");
                }
                double[] coefficients = new double[100];
                foreach (string key in values.Keys.Where(k => k.StartsWith(prefix + "_", StringComparison.Ordinal)))
                {
                    if (key is "A_ORDER" or "B_ORDER" or "A_DMAX" or "B_DMAX")
                    {
                        continue;
                    }
                    string[] parts = key.Split('_');
                    if (parts.Length != 3 || !int.TryParse(parts[1], out int i) || !int.TryParse(parts[2], out int j) || i is < 0 or > 9 || j is < 0 or > 9 || i + j > order)
                    {
                        throw new InvalidDataException("Invalid SIP coefficient index.");
                    }
                    double value = Number(key);
                    if (i + j < 2 && value != 0)
                    {
                        throw new InvalidDataException("Forward SIP constant/linear terms must be zero.");
                    }
                    coefficients[i * 10 + j] = value;
                }
                return coefficients;
            }
            double[] ReadPv(string prefix)
            {
                double[] coefficients = new double[40];
                coefficients[1] = 1;
                foreach (string key in values.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    if (!int.TryParse(key[prefix.Length..], out int index) || index is < 0 or > 39)
                    {
                        throw new InvalidDataException("TPV coefficient indices must be from 0 to 39.");
                    }
                    coefficients[index] = Number(key);
                }
                return coefficients;
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or ArgumentException)
        {
            warning = "This FITS WCS is invalid or unsupported: " + exception.Message + " Use manual placement or a new plate solution.";
            return null;
        }
    }
}
