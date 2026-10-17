using System.Globalization;
using System.Windows;
using SkyBrowser.Desktop.Services;

namespace SkyBrowser.Desktop;

public partial class ExportWindow : Window
{
    public ImageExportOptions? Result { get; private set; }
    public ExportWindow(int width, int height, int? previewWidth, int? previewHeight)
    {
        InitializeComponent();
        WidthInput.Text = Math.Clamp(width, 128, 8192).ToString(CultureInfo.InvariantCulture);
        HeightInput.Text = Math.Clamp(height, 128, 8192).ToString(CultureInfo.InvariantCulture);
        PreviewChoice.IsEnabled = previewWidth is not null;
        PreviewDimensions.Text = previewWidth is { } w ? $"Selected preview: {w:N0} × {previewHeight:N0} pixels. Its stored resolution and transparency are retained for PNG."
            : "Select an image in the collection to export its preview.";
        RefreshSettings();
    }

    private ImageExportOptions ReadOptions()
    {
        bool preview = TargetChoice.SelectedIndex == 1;
        bool jpeg = FormatChoice.SelectedIndex == 1;
        var options = new ImageExportOptions
        {
            SelectedPreview = preview, Jpeg = jpeg,
            Width = preview ? 1920 : int.Parse(WidthInput.Text, CultureInfo.InvariantCulture),
            Height = preview ? 1080 : int.Parse(HeightInput.Text, CultureInfo.InvariantCulture),
            JpegQuality = jpeg ? int.Parse(QualityInput.Text, CultureInfo.InvariantCulture) : 92,
            Labels = LabelsChoice.IsChecked == true, Outlines = OutlinesChoice.IsChecked == true,
            Grid = GridChoice.IsChecked == true, Coordinates = CoordinatesChoice.IsChecked == true
        };
        options.Validate();
        return options;
    }

    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (ExportButton is not null)
        {
            RefreshSettings();
        }
    }
    private void RefreshSettings()
    {
        DimensionsPanel.IsEnabled = SkyOptions.IsEnabled = TargetChoice.SelectedIndex == 0;
        DimensionsPanel.Visibility = SkyOptions.Visibility = TargetChoice.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        QualityPanel.Visibility = FormatChoice.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        try
        {
            _ = ReadOptions();
            ExportButton.IsEnabled = true;
            ValidationMessage.Text = "";
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            ExportButton.IsEnabled = false;
            ValidationMessage.Text = exception is FormatException or OverflowException ? "Enter whole numbers within the displayed limits." : exception.Message;
        }
    }
    private void ExportClicked(object sender, RoutedEventArgs e)
    {
        Result = ReadOptions();
        DialogResult = true;
    }
}
