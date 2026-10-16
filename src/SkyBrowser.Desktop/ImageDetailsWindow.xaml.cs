using System.IO;
using System.Windows;
using System.Windows.Controls;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Desktop.Models;

namespace SkyBrowser.Desktop;

public partial class ImageDetailsWindow : Window
{
    private readonly ImageMetadataSnapshot _metadata;

    public ImageDetailsWindow(SkyImageLayer layer)
    {
        var image = layer.Image;
        _metadata = image.Details;
        InitializeComponent();
        Title = $"SkyBrowser · {image.Name} · Image details";
        ImageName.Text = image.Name;
        FilePathText.Text = image.SourcePath;
        ImagePreview.Source = layer.Bitmap;
        ImageFacts.Text = $"{image.Metadata}\nImage {image.ImageIndex + 1} of {image.SourceImageCount} · Preview {image.Preview.Width:N0} × {image.Preview.Height:N0} pixels\nDisplay: {image.DisplayDescription}";
        if (layer.Placement is { } placement)
        {
            ImageFacts.Text += $"\n{placement.Center.RaText}  {placement.Center.DecText}\n{placement.AngularWidth:0.###}° × {placement.AngularHeight:0.###}° · {placement.Description}";
        }
        else
        {
            ImageFacts.Text += "\nNo sky placement available.";
        }

        if (layer.Manual is not null)
        {
            ImageFacts.Text += "\nManual placement overrides the file solution.";
        }
        else if (image.PlacementWarning is { } warning)
        {
            ImageFacts.Text += $"\n{warning}";
        }

        if (_metadata.IsTruncated)
        {
            FooterText.Text = "Large metadata values or lists are shortened. The source file retains the full metadata.";
        }

        RefreshMetadata();
        Loaded += async (_, _) =>
        {
            string details = await Task.Run(() => ReadFileFacts(image.SourcePath));
            if (IsVisible)
            {
                FileFacts.Text = details;
            }
        };
    }

    private static string ReadFileFacts(string path)
    {
        try
        {
            var file = new FileInfo(path);
            file.Refresh();
            if (!file.Exists)
            {
                return "Source file is unavailable. Loaded image details remain available.";
            }

            long bytes = file.Length;
            string size = bytes >= 1073741824
                ? $"{bytes / 1073741824d:N2} GiB"
                : bytes >= 1048576
                ? $"{bytes / 1048576d:N1} MiB"
                : bytes >= 1024 ? $"{bytes / 1024d:N1} KiB" : $"{bytes:N0} bytes";
            return $"Source file: {size} ({bytes:N0} bytes)\nModified: {file.LastWriteTime:yyyy-MM-dd HH:mm:ss} (computer local time)";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"Source file information unavailable: {exception.Message}";
        }
    }

    private void MetadataSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (MetadataGrid is not null)
        {
            RefreshMetadata();
        }
    }

    private void RefreshMetadata()
    {
        string query = MetadataSearch.Text.Trim();
        var rows = _metadata.Entries.Where(entry => query.Length == 0 ||
            entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            entry.Value.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            entry.Comment.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            entry.Source.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        MetadataGrid.ItemsSource = rows;
        MetadataCount.Text = _metadata.Entries.Length == 0
            ? "No additional metadata was retained for this image."
            : $"{rows.Length:N0} of {_metadata.Entries.Length:N0} metadata entries · Search keywords, values or comments";
    }

    private void MetadataSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SelectedMetadata.Text = MetadataGrid.SelectedItem is ImageMetadataEntry entry
            ? $"{entry.Source} · {entry.Name}\n{entry.Value}{(entry.Comment.Length > 0 ? $"\n{entry.Comment}" : "")}"
            : "Select a metadata row to read and copy its value.";
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
}
