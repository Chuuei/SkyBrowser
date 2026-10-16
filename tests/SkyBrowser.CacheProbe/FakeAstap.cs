using XISFLib;
using SkyBrowser.Core.Imaging;

internal sealed class SolverProgress : IProgress<ImageProcessingProgress>
{
    public void Report(ImageProcessingProgress update) => Console.WriteLine(update.Stage);
}

internal static class FakeAstap
{
    public static async Task<int> RunAsync(string[] arguments)
    {
        string Value(string option) => arguments[Array.IndexOf(arguments, option) + 1];
        if (arguments.Contains("-update") || Value("-r") != "180" || Value("-ra") != "0" ||
            Value("-spd") != "90" || Value("-z") != "0" || Value("-sip") != "n")
        {
            return 99;
        }
        var input = FitsReader.Load(Value("-f"));
        if (input.Images.Count() != 1 || input.Hdus[0].Header.Any(card => card.Keyword == "CRVAL1"))
        {
            return 98;
        }
        var image = input.Images.Single();
        double first = image.GetPhysicalSamples()[0];
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
