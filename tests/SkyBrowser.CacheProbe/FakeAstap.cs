using XISFLib;
using SkyBrowser.Core.Imaging;
using System.Globalization;

internal sealed class SolverProgress : IProgress<ImageProcessingProgress>
{
    public void Report(ImageProcessingProgress update) => Console.WriteLine(update.Stage);
}

internal static class FakeAstap
{
    public static async Task<int> RunAsync(string[] arguments)
    {
        string Value(string option) => arguments[Array.IndexOf(arguments, option) + 1];
        bool nearby = Value("-r") == "5";
        if (arguments.Contains("-update") || (!nearby && Value("-r") != "180") ||
            Value("-s") != (nearby ? "1000" : "500") ||
            !double.TryParse(Value("-ra"), NumberStyles.Float, CultureInfo.InvariantCulture, out double raHours) || !double.IsFinite(raHours) || raHours is < 0 or >= 24 ||
            !double.TryParse(Value("-spd"), NumberStyles.Float, CultureInfo.InvariantCulture, out double southPoleDistance) || !double.IsFinite(southPoleDistance) || southPoleDistance is < 0 or > 180 ||
            Value("-z") != "0" || Value("-sip") != "n")
        {
            return 99;
        }
        var input = FitsReader.Load(Value("-f"));
        if (input.Images.Count() != 1 || input.Hdus[0].Header.Any(card => card.Keyword is "CRVAL1" or "RA" or "DEC" or "OBJCTRA" or "FOCALLEN"))
        {
            return 98;
        }
        var image = input.Images.Single();
        double first = image.GetPhysicalSamples()[0];
        if (Math.Abs(first - 0.4) < 0.001 &&
            (Math.Abs(raHours * 15 - 303.30834) > 1e-8 || Math.Abs(southPoleDistance - 128.425) > 1e-8))
        {
            return 97; // Metadata-hint fixtures must reach the process in ASTAP's units.
        }
        if (Math.Abs(first - 0.5) < 0.001 || Math.Abs(first - 0.6) < 0.001)
        {
            string marker = Path.Combine(Path.GetDirectoryName(Value("-f"))!, "nearby-attempt.marker");
            if (nearby)
            {
                double expectedField = 2 * Math.Atan(48 * 2.9 / 1000 / 500) * 180 / Math.PI;
                if (Math.Abs(double.Parse(Value("-fov"), CultureInfo.InvariantCulture) - expectedField) > 1e-10)
                {
                    return 96;
                }
                File.WriteAllText(marker, "A failed nearby solve must be followed by a fresh blind attempt.");
                if (Math.Abs(first - 0.6) < 0.001)
                {
                    await Task.Delay(TimeSpan.FromSeconds(30));
                }
                return 1;
            }
            if (!File.Exists(marker) || Value("-fov") != "0" || raHours != 0 || southPoleDistance != 90)
            {
                return 95;
            }
        }
        if (Math.Abs(first - 0.2) < 0.001)
        {
            Console.Error.WriteLine("Fixture: missing database");
            return 32;
        }
        if (Math.Abs(first - 0.3) < 0.001)
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
        }
        string output = Value("-o") + ".ini";
        File.WriteAllText(output, """
            PLTSOLVD=T
            CRPIX1=32.5
            CRPIX2=24.5
            CRVAL1=83.822
            CRVAL2=-5.391
            CD1_1=-0.01
            CD1_2=0
            CD2_1=0
            CD2_2=0.01
            WARNING=Warning inexact scale! Set FOV=0,48d or scale=36,0"/pix Fixture solution
            """);
        return 0;
    }
}
