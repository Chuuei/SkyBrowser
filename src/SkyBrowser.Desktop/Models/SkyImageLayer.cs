using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Library;

namespace SkyBrowser.Desktop.Models;

public sealed class SkyImageLayer : INotifyPropertyChanged
{
    public ImageOrganization Organization { get; private set; } = new();

    public string OrganizationLabel => string.Join(" · ", new[]
    {
        Organization.IsFavorite ? "★ Favorite" : "", Organization.Group, Organization.Tags
    }.Where(value => value.Length > 0));

    public void SetOrganization(ImageOrganization organization)
    {
        Organization = organization;
        Changed(nameof(Organization));
        Changed(nameof(OrganizationLabel));
    }

    public LoadedSkyImage Image { get; private set; }

    public string Name => Image.Name;

    public string SourceLabel => $"{System.IO.Path.GetFileName(Image.SourcePath)} · image {Image.ImageIndex + 1}";

    public string PlacementLabel => Placement?.Description ?? "Needs placement";

    public ImagePlacement? Placement { get; private set; }

    public ManualPlacement? Manual { get; private set; }

    public BitmapSource Bitmap { get; private set; }

    public DisplayMode DisplayMode { get; set; }

    public bool IsVisible
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            Changed();
        }
    } = true;

    public double Opacity
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = Math.Clamp(value, 0, 1);
            Changed();
        }
    } = 1;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SkyImageLayer(LoadedSkyImage image, DisplayMode mode = DisplayMode.Automatic)
    {
        Image = image;
        Placement = image.Placement;
        DisplayMode = mode;
        Bitmap = CreateBitmap(image.Preview);
    }

    public void SetManual(ManualPlacement? manual)
    {
        var placement = manual is null ? Image.Placement : ImagePlacement.FromManual(manual, Image.Width, Image.Height);
        // Evaluate the entire footprint before accepting a new placement.
        if (placement is not null)
        {
            for (int y = 0; y <= 16; y++)
            {
                for (int x = 0; x <= 16; x++)
                {
                    _ = placement.At(x / 16.0, y / 16.0);
                }
            }
        }

        Manual = manual;
        Placement = placement;
        Changed(nameof(Placement));
        Changed(nameof(PlacementLabel));
    }

    public void ReplaceDisplay(LoadedSkyImage image, DisplayMode mode)
    {
        Image = image;
        Bitmap = CreateBitmap(image.Preview);
        DisplayMode = mode;
        Changed(nameof(Image));
        Changed(nameof(Name));
        Changed(nameof(SourceLabel));
        Changed(nameof(Bitmap));
        if (Manual is null)
        {
            Placement = image.Placement;
            Changed(nameof(Placement));
            Changed(nameof(PlacementLabel));
        }
    }

    private static BitmapSource CreateBitmap(DisplayPixels preview)
    {
        var bitmap = BitmapSource.Create(preview.Width, preview.Height, 96, 96, PixelFormats.Bgra32, null, preview.Bgra, preview.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
