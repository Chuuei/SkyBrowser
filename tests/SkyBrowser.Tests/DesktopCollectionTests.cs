using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkyBrowser.Core.Astronomy;
using SkyBrowser.Core.Caching;
using SkyBrowser.Core.Imaging;
using SkyBrowser.Core.Library;
using SkyBrowser.Desktop;
using SkyBrowser.Desktop.Models;
using XISFLib;

namespace SkyBrowser.Tests;

public sealed partial class DesktopTests
{
    private static async Task VerifyCollectionOrganization(MainWindow window, string directory)
    {
        string firstPath = Path.Combine(directory, "organization-a.xisf");
        string secondPath = Path.Combine(directory, "organization-b.xisf");
        var a = TestImages.Create();
        var b = TestImages.Create();
        a.FitsKeywords.Add(XisfFitsKeyword.FromValue("FILTER", "H-alpha"));
        b.FitsKeywords.Add(XisfFitsKeyword.FromValue("FILTER", "OIII"));
        XisfWriter.Write(firstPath, [a, a]);
        XisfWriter.Write(secondPath, [b]);
        byte[] original = SHA256.HashData(File.ReadAllBytes(firstPath));
        await window.ImportAsync([firstPath, secondPath]);
        var layers = window.SkyView.Layers.ToArray();
        var list = (ListBox)window.FindName("LayerList");
        Assert.Equal(SelectionMode.Extended, list.SelectionMode);
        list.SelectAll();
        Assert.Equal(3, list.SelectedItems.Count);
        await window.ApplyCollectionBatchAsync(layers.Take(2), new(new(true, "Ha, Winter", "Campaign"), false, 0.4, DisplayMode.Linear));
        Assert.All(layers.Take(2), layer =>
        {
            Assert.False(layer.IsVisible);
            Assert.Equal(0.4, layer.Opacity);
            Assert.True(layer.Organization.IsFavorite);
            Assert.Equal("Campaign", layer.Organization.Group);
            Assert.Equal(DisplayMode.Linear, layer.DisplayMode);
        });
        Assert.True(layers[2].IsVisible);
        var sort = (ComboBox)window.FindName("CollectionSortChoice");
        sort.SelectedIndex = (int)CollectionSort.Filter;
        Assert.Equal(layers, window.SkyView.Layers); // Sorting the collection cannot reorder sky compositing.
        Assert.Same(layers[0], list.Items[0]);
        ((ComboBox)window.FindName("MetadataChoice")).SelectedIndex = (int)MetadataField.Filter;
        ((TextBox)window.FindName("MetadataQuery")).Text = "OIII";
        Assert.Same(layers[2], Assert.Single(list.Items.Cast<SkyImageLayer>()));
        ((TextBox)window.FindName("MetadataQuery")).Text = "H-alpha";
        ((TextBox)window.FindName("TagQuery")).Text = "HA";
        ((TextBox)window.FindName("GroupQuery")).Text = "campaign";
        var favorite = (CheckBox)window.FindName("FavoriteFilter");
        favorite.IsChecked = true;
        favorite.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(2, list.Items.Count);
        ((TextBox)window.FindName("TagQuery")).Text = "H";
        Assert.Empty(list.Items); // Tag filtering is exact, rather than matching arbitrary metadata.
        ((TextBox)window.FindName("TagQuery")).Text = "Ha";
        Assert.Equal(2, list.Items.Count);
        Assert.Equal(layers, window.SkyView.Layers);
        ((TextBox)window.FindName("MetadataQuery")).Clear();
        ((TextBox)window.FindName("TagQuery")).Clear();
        ((TextBox)window.FindName("GroupQuery")).Clear();
        favorite.IsChecked = false;
        favorite.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        sort.SelectedIndex = 0;
        var filter = new CollectionFilter { Field = MetadataField.Camera, MetadataValue = "H-alpha" };
        Assert.False(filter.Matches(layers[0])); // Matching must stay within the selected field.
        window.SkyView.Layers.Clear();
        await window.ImportAsync([firstPath, secondPath]);
        var restored = window.SkyView.Layers.ToArray();
        Assert.All(restored.Take(2), layer => Assert.Equal(new(true, "Ha, Winter", "Campaign"), layer.Organization));
        Assert.Equal(new(), restored[2].Organization);
        Assert.All(restored.Take(2), layer => Assert.Equal(DisplayMode.Linear, layer.DisplayMode));
        Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(firstPath)));

        // Hold a cold display job to verify cancellation retains the previous preview and annotations.
        var loaderField = typeof(MainWindow).GetField("_imageLoader", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var oldLoader = loaderField.GetValue(window);
        using var freshCache = new ProcessedImageCache(Path.Combine(directory, "batch-cancel-cache"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        loaderField.SetValue(window, new CachedSkyImageLoader(freshCache, (_, _, _, token, _, _) =>
        {
            entered.TrySetResult();
            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(10));
            token.ThrowIfCancellationRequested();
            throw new TimeoutException("Cancellation was not requested.");
        }));
        var previousImage = restored[0].Image;
        try
        {
            var pending = window.ApplyCollectionBatchAsync(restored, new(new(Group: "Retained on cancel"), DisplayMode: DisplayMode.Finished));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            ((Button)window.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await pending;
            Assert.Same(previousImage, restored[0].Image);
            Assert.All(restored, layer => Assert.Equal("Retained on cancel", layer.Organization.Group));
            Assert.StartsWith("Batch cancelled", window.Status);
            Assert.True(((UIElement)window.FindName("CollectionActions")).IsEnabled);
            Capture(window, "collection-organization.png");
            loaderField.SetValue(window, oldLoader);
            File.Delete(firstPath);
            await window.ApplyCollectionBatchAsync(restored, new(new(), DisplayMode: DisplayMode.AutoStretch));
            Assert.Contains("2 failed", window.Status);
            Assert.Same(previousImage, restored[0].Image);
            Assert.Equal(DisplayMode.AutoStretch, restored[2].DisplayMode);
        }
        finally
        {
            loaderField.SetValue(window, oldLoader);
            window.SkyView.Layers.Clear();
        }
    }

    private static async Task VerifyComparisons(MainWindow owner)
    {
        static SkyImageLayer TexturedLayer(string name, byte red, byte blue)
        {
            var solid = SolidLayer(name, 4, red, 0, blue);
            byte[] pixels = solid.Image.Preview.Bgra.ToArray();
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    pixels[(y * 64 + x) * 4 + 1] = (byte)((3 * x + 5 * y) % 192);
                }
            }
            return new(solid.Image with { Preview = new(64, 64, pixels) });
        }
        var a = TexturedLayer("Comparison A", 255, 0);
        var b = TexturedLayer("Comparison B", 0, 255);
        var alternateA = TexturedLayer("Comparison A alternate", 255, 0);
        a.IsVisible = false;
        a.Opacity = 0.2;
        b.SetManual(new(359.95, 70, 4, 37, true));
        var ownerCenter = owner.SkyView.Camera.Center;
        var comparison = new ComparisonWindow([a, b, alternateA]) { Owner = owner, ShowInTaskbar = false };
        try
        {
            comparison.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(ComparisonMode.Swipe, comparison.Mode);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)comparison.FindName("SwipeFrames")).Visibility);
            var choicesA = (ComboBox)comparison.FindName("ImageA");
            var choicesB = (ComboBox)comparison.FindName("ImageB");
            Assert.DoesNotContain(choicesA.SelectedItem, choicesB.Items.Cast<object>());
            comparison.LeftView.CenterOn(new(180, 0), 3);
            choicesA.SelectedItem = b;
            Assert.NotSame(choicesA.SelectedItem, choicesB.SelectedItem);
            Assert.True(comparison.LeftView.Camera.Center.DistanceTo(a.Placement!.Center) < 1e-8);
            choicesA.SelectedItem = a;
            choicesB.SelectedItem = b;
            ((ComboBox)comparison.FindName("ModeChoice")).SelectedIndex = (int)ComparisonMode.SideBySide;
            comparison.UpdateLayout();
            var left = comparison.LeftView;
            var right = comparison.RightView;
            Assert.NotSame(a, Assert.Single(left.Layers));
            Assert.True(left.Layers[0].IsVisible);
            Assert.Equal(1, left.Layers[0].Opacity);
            static IEnumerable<TextBlock> TextBlocks(DependencyObject element)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                {
                    var child = VisualTreeHelper.GetChild(element, i);
                    if (child is TextBlock text)
                    {
                        yield return text;
                    }
                    foreach (var nested in TextBlocks(child))
                    {
                        yield return nested;
                    }
                }
            }
            Assert.Contains(TextBlocks((ComboBox)comparison.FindName("ImageA")), text => text.Text == a.SourceLabel);
            left.CenterOn(new(359.95, 70), 6);
            comparison.UpdateLayout();
            Assert.Equal(left.Camera.Center, right.Camera.Center);
            Assert.Equal(left.Camera.FieldOfView, right.Camera.FieldOfView);
            var lp = new Point(left.ActualWidth / 2, left.ActualHeight / 2);
            var rp = new Point(right.ActualWidth / 2, right.ActualHeight / 2);
            Assert.True(PixelAt(left, lp)[2] > 240);
            Assert.True(PixelAt(right, rp)[0] > 240);
            Capture(comparison, "comparison-linked.png");
            foreach (var coordinate in new[] { new SkyCoordinate(0.02, 70), new(180, 89.5), new(359.98, -89.5) })
            {
                right.CenterOn(coordinate, 3);
                Assert.Equal(right.Camera.Center, left.Camera.Center);
                var point = new SkyCoordinate(coordinate.RightAscension + 0.05, coordinate.Declination);
                Assert.Equal(left.Camera.Project(point.Vector, 500, 500), right.Camera.Project(point.Vector, 500, 500));
            }
            left.CenterOn(new(359.95, 70), 6);
            ((CheckBox)comparison.FindName("BlinkRunning")).IsChecked = false;
            var mode = (ComboBox)comparison.FindName("ModeChoice");
            mode.SelectedIndex = (int)ComparisonMode.Blink;
            comparison.UpdateLayout();
            lp = new(left.ActualWidth / 2, left.ActualHeight / 2);
            Assert.True(PixelAt(left, lp)[2] > 240);
            comparison.ToggleFrame();
            Assert.True(PixelAt(left, lp)[0] > 240);
            Capture(comparison, "comparison-blink.png");
            mode.SelectedIndex = (int)ComparisonMode.Swipe;
            comparison.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var divider = (Slider)comparison.FindName("DividerPosition");
            divider.Value = 0.5;
            Assert.True(PixelAt(left, new(lp.X - 25, lp.Y))[2] > 240);
            Assert.True(PixelAt(left, new(lp.X + 25, lp.Y))[0] > 240);
            divider.Value = 0;
            Assert.True(PixelAt(left, lp)[0] > 240);
            divider.Value = 1;
            Assert.True(PixelAt(left, lp)[2] > 240);
            divider.Value = 0.5;
            await VerifySlowSwipe(comparison);
            Capture(comparison, "comparison-swipe.png");
            mode.SelectedIndex = 0;
            comparison.UpdateLayout();
            Assert.Null(right.Clip);
            Assert.Null(((Image)comparison.FindName("SwipeFrameA")).Source);
            Assert.Null(((Image)comparison.FindName("SwipeFrameB")).Source);
            Assert.True(right.IsHitTestVisible);
            Assert.Equal(ownerCenter, owner.SkyView.Camera.Center);
            Assert.False(a.IsVisible);
            Assert.Equal(0.2, a.Opacity);
            Assert.Equal(37, b.Manual!.RotationDegrees);
            // Closing with a queued swipe capture must release frames and cancel the callback.
            mode.SelectedIndex = (int)ComparisonMode.Swipe;
        }
        finally
        {
            comparison.Close();
        }
        Assert.Empty(comparison.LeftView.Layers);
        Assert.Empty(comparison.RightView.Layers);
        Assert.Null(((Image)comparison.FindName("SwipeFrameA")).Source);
        Assert.Null(((Image)comparison.FindName("SwipeFrameB")).Source);
        Assert.Throws<ArgumentException>(() => new ComparisonWindow([a]));
        var distant = TexturedLayer("Unrelated field", 100, 100);
        distant.SetManual(new(180, 0, 4, 0));
        Assert.Throws<ArgumentException>(() => new ComparisonWindow([a, distant]));
        Assert.Throws<ArgumentException>(() => new ComparisonWindow([a, b, distant]));
        owner.SkyView.Layers.Add(a);
        owner.SkyView.Layers.Add(distant);
        var list = (ListBox)owner.FindName("LayerList");
        list.UnselectAll();
        var compareButton = (Button)owner.FindName("CompareButton");
        Assert.False(compareButton.IsEnabled);
        list.SelectedItems.Add(a);
        Assert.False(compareButton.IsEnabled);
        list.SelectedItems.Add(distant);
        Assert.False(compareButton.IsEnabled);
        Assert.Contains("overlap", compareButton.ToolTip.ToString());
        compareButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Empty(owner.OwnedWindows.OfType<ComparisonWindow>());
        Assert.Contains("overlap", owner.Status);
        distant.SetManual(new(359.95, 70, 4, 0));
        Assert.True(compareButton.IsEnabled); // Placement changes update eligibility without reselecting.
        var unplaced = new SkyImageLayer(a.Image with { SourcePath = "Unplaced comparison", Placement = null });
        owner.SkyView.Layers.Add(unplaced);
        list.SelectedItems.Add(unplaced);
        Assert.False(compareButton.IsEnabled);
        Assert.Contains("Place or solve", compareButton.ToolTip.ToString());
        unplaced.SetManual(new(359.95, 70, 4, 0));
        Assert.True(compareButton.IsEnabled);
        unplaced.SetManual(null);
        Assert.False(compareButton.IsEnabled);
        list.SelectedItems.Remove(unplaced);
        Assert.True(compareButton.IsEnabled);
        compareButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Single(owner.OwnedWindows.OfType<ComparisonWindow>()).Close();
        owner.SkyView.ClearImageSelection();
        Assert.False(compareButton.IsEnabled);
        owner.SkyView.Layers.Remove(unplaced);
        owner.SkyView.Layers.Remove(a);
        owner.SkyView.Layers.Remove(distant);
    }

    private static async Task VerifySlowSwipe(ComparisonWindow comparison)
    {
        var left = comparison.LeftView;
        var right = comparison.RightView;
        var divider = (Slider)comparison.FindName("DividerPosition");
        var imageA = (Image)comparison.FindName("SwipeFrameA");
        var imageB = (Image)comparison.FindName("SwipeFrameB");
        var sourceA = Assert.IsAssignableFrom<BitmapSource>(imageA.Source);
        var sourceB = Assert.IsAssignableFrom<BitmapSource>(imageB.Source);
        Assert.True(sourceA.IsFrozen);
        Assert.True(sourceB.IsFrozen);
        Assert.Null(left.Clip);
        Assert.Null(right.Clip); // No live 3D mesh may receive the moving clip.
        const int width = 96, height = 96;
        int cropX = (int)(left.ActualWidth / 2) - width / 2;
        int cropY = (int)(left.ActualHeight / 2) - height / 2;
        byte[] CaptureRegion()
        {
            comparison.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(comparison.ActualWidth), (int)Math.Ceiling(comparison.ActualHeight),
                96, 96, PixelFormats.Pbgra32);
            bitmap.Render(comparison);
            var origin = left.TranslatePoint(new(cropX, cropY), comparison);
            byte[] pixels = new byte[width * height * 4];
            bitmap.CopyPixels(new Int32Rect((int)origin.X, (int)origin.Y, width, height), pixels, width * 4, 0);
            return pixels;
        }

        divider.Value = 1;
        byte[] baselineA = CaptureRegion();
        divider.Value = 0;
        byte[] baselineB = CaptureRegion();
        // Subpixel steps in both directions used to repeatedly clip the underlying mesh.
        // Assert full regions match the original frames, and divider motion never recaptures them.
        foreach (int step in Enumerable.Range(0, 24).Concat(Enumerable.Range(0, 24).Reverse()))
        {
            divider.Value = (left.ActualWidth / 2 + (step - 12) * 0.37) / left.ActualWidth;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Same(sourceA, imageA.Source);
            Assert.Same(sourceB, imageB.Source);
            byte[] actual = CaptureRegion();
            double boundary = Assert.IsType<RectangleGeometry>(imageB.Clip).Rect.X;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    double localX = cropX + x;
                    if (Math.Abs(localX - boundary) < 8)
                    {
                        continue; // Exclude the visible divider handle itself.
                    }
                    byte[] expected = localX < boundary ? baselineA : baselineB;
                    for (int channel = 0; channel < 4; channel++)
                    {
                        int index = (y * width + x) * 4 + channel;
                        Assert.InRange(Math.Abs(actual[index] - expected[index]), 0, 1);
                    }
                }
            }
        }

        left.Zoom(0.95);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.NotSame(sourceA, imageA.Source);
        Assert.NotSame(sourceB, imageB.Source);
        Assert.Equal(left.Camera.Center, right.Camera.Center);
        var beforeResize = imageB.Source;
        double originalWidth = comparison.Width;
        comparison.Width -= 37.5;
        comparison.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.NotSame(beforeResize, imageB.Source);
        var dpi = VisualTreeHelper.GetDpi(left);
        Assert.Equal((int)Math.Ceiling(left.ActualWidth * dpi.DpiScaleX), ((BitmapSource)imageB.Source).PixelWidth);
        Assert.Equal(left.RenderSize, right.RenderSize);

        var choice = (ComboBox)comparison.FindName("ImageB");
        var beforeSelection = imageB.Source;
        choice.SelectedIndex = 1;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.NotSame(beforeSelection, imageB.Source);
        var frameA = (BitmapSource)imageA.Source;
        var frameB = (BitmapSource)imageB.Source;
        byte[] a = new byte[frameA.PixelWidth * frameA.PixelHeight * 4];
        byte[] b = new byte[a.Length];
        frameA.CopyPixels(a, frameA.PixelWidth * 4, 0);
        frameB.CopyPixels(b, frameB.PixelWidth * 4, 0);
        Assert.Equal(a, b); // Identical processing versions and sky cameras must rasterize identically.
        for (int i = 3; i < a.Length; i += 4)
        {
            Assert.Equal((byte)255, a[i]); // Live 3D content behind the frames cannot show through.
        }
        choice.SelectedIndex = 0;
        comparison.Width = originalWidth;
        divider.Value = 0.5;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }
}
