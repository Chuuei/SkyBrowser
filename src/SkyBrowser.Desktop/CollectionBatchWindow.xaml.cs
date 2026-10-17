using System.Windows;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Library;
using SkyBrowser.Desktop.Models;

namespace SkyBrowser.Desktop;

public sealed record CollectionBatchChange(OrganizationUpdate Organization, bool? IsVisible = null, double? Opacity = null, DisplayMode? DisplayMode = null);

public partial class CollectionBatchWindow : Window
{
    public CollectionBatchChange? Change { get; private set; }

    public CollectionBatchWindow(IReadOnlyList<SkyImageLayer> layers)
    {
        InitializeComponent();
        Heading.Text = $"Organize {layers.Count:N0} selected image{(layers.Count == 1 ? "" : "s")}";
        if (layers.Count > 0)
        {
            TagsInput.Text = layers.All(layer => layer.Organization.Tags == layers[0].Organization.Tags) ? layers[0].Organization.Tags : "";
            GroupInput.Text = layers.All(layer => layer.Organization.Group == layers[0].Organization.Group) ? layers[0].Organization.Group : "";
        }
    }

    private void ApplyClicked(object sender, RoutedEventArgs e)
    {
        Change = new(new(
            FavoriteChoice.SelectedIndex == 0 ? null : FavoriteChoice.SelectedIndex == 1,
            SetTags.IsChecked == true ? TagsInput.Text : null,
            SetGroup.IsChecked == true ? GroupInput.Text : null),
            VisibilityChoice.SelectedIndex == 0 ? null : VisibilityChoice.SelectedIndex == 1,
            SetOpacity.IsChecked == true ? BatchOpacity.Value : null,
            BatchDisplay.SelectedIndex == 0 ? null : (DisplayMode)(BatchDisplay.SelectedIndex - 1));
        DialogResult = true;
    }
}
