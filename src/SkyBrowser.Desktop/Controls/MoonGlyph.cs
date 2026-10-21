using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkyBrowser.Core.Astronomy;

namespace SkyBrowser.Desktop.Controls;

/// <summary>Small procedural lunar illustration. Crater texture is decorative, not a libration map.</summary>
public static class MoonGlyph
{
    public const double Diameter = 32;
    private const int Resolution = 96;
    private static readonly double[] Texture = CreateTexture();

    private static double[] CreateTexture()
    {
        var result = new double[Resolution * Resolution];
        // Stylised maria and crater rims, fixed across phases; no external bitmap or attribution dependency.
        (double X, double Y, double Radius, double Depth)[] maria =
        [(-.35, -.34, .39, .24), (.15, -.46, .31, .19), (.49, -.12, .24, .25), (.08, .15, .31, .14), (-.48, .18, .24, .16)];
        var random = new Random(1969);
        var craters = Enumerable.Range(0, 55).Select(_ =>
            (X: random.NextDouble() * 1.8 - .9, Y: random.NextDouble() * 1.8 - .9, Radius: .015 + random.NextDouble() * .07)).ToArray();
        for (int y = 0; y < Resolution; y++)
        {
            for (int x = 0; x < Resolution; x++)
            {
                double u = 2 * (x + .5) / Resolution - 1, v = 2 * (y + .5) / Resolution - 1;
                double albedo = .88 + .035 * Math.Sin(u * 37 + Math.Sin(v * 23)) * Math.Cos(v * 41);
                foreach (var mare in maria)
                {
                    double r2 = ((u - mare.X) * (u - mare.X) + (v - mare.Y) * (v - mare.Y)) / (mare.Radius * mare.Radius);
                    albedo -= mare.Depth * Math.Exp(-r2 * r2 * 2);
                }
                foreach (var crater in craters)
                {
                    double r = Math.Sqrt((u - crater.X) * (u - crater.X) + (v - crater.Y) * (v - crater.Y)) / crater.Radius;
                    albedo += .1 * Math.Exp(-Math.Pow((r - 1) * 7, 2)) - .08 * Math.Exp(-r * r * 3);
                }
                result[y * Resolution + x] = Math.Clamp(albedo, .35, 1);
            }
        }
        return result;
    }

    public static BitmapSource Create(LunarState moon)
    {
        byte[] pixels = new byte[Resolution * Resolution * 4];
        var light = moon.DiskLight;
        for (int y = 0; y < Resolution; y++)
        {
            for (int x = 0; x < Resolution; x++)
            {
                double u = 2 * (x + .5) / Resolution - 1, v = 2 * (y + .5) / Resolution - 1;
                double r2 = u * u + v * v;
                if (r2 >= 1) { continue; }
                double z = Math.Sqrt(1 - r2);
                double incidence = u * light.X + v * light.Y + z * light.Z;
                // Low earthshine keeps the unlit limb visible; soft sunlight reveals the terminator.
                double brightness = Texture[y * Resolution + x] * (.075 + .925 * Math.Pow(Math.Max(0, incidence), .36));
                int i = (y * Resolution + x) * 4;
                pixels[i] = (byte)Math.Clamp(brightness * 235, 0, 255);
                pixels[i + 1] = (byte)Math.Clamp(brightness * 242, 0, 255);
                pixels[i + 2] = (byte)Math.Clamp(brightness * 250, 0, 255);
                pixels[i + 3] = (byte)(255 * Math.Clamp((1 - Math.Sqrt(r2)) * Resolution / 2, 0, 1));
            }
        }
        var image = BitmapSource.Create(Resolution, Resolution, 96, 96, PixelFormats.Bgra32, null, pixels, Resolution * 4);
        image.Freeze();
        return image;
    }
}
