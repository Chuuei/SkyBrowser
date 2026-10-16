namespace SkyBrowser.Core.Imaging;

/// <summary>Aesthetic display settings only; scientific samples and sky coordinates are unchanged.</summary>
public sealed record SkyFinishSettings
{
    public static SkyFinishSettings Default { get; } = new();

    public double TargetBackground { get; init; } = 0.10;

    public bool BalanceBackground { get; init; } = true;

    public bool ReduceGradients { get; init; } = true;

    public bool ReduceNoise { get; init; } = true;

    public bool EnhanceDetail { get; init; } = true;

    public void Validate()
    {
        if (!double.IsFinite(TargetBackground) || TargetBackground is < 0.02 or > 0.25)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetBackground), "Sky background must be between 2% and 25%.");
        }
    }
}

public sealed record ImageProcessingProgress(string Stage, double? Percentage = null);
