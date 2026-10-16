using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SkyBrowser.Core.Imaging;

namespace SkyBrowser.Desktop;

/// <summary>One operation's progress, kept separate from a later folder selection.</summary>
public partial class ImageLoadProgressWindow : Window
{
    private readonly Action _cancel;
    private readonly string _unit;
    private readonly DispatcherTimer _showTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(350)
    };
    private bool _finished, _cancelling;
    private int? _total;
    private int _completed, _images, _cached, _failed, _skipped;

    public int ItemVersion { get; private set; }

    public string StatusPrefix => _total is { } total ? $"{Math.Min(_completed + 1, total):N0}/{total:N0} {_unit}s" : "Finding images";

    public ImageLoadProgressWindow(string heading, string unit, string cancellationNote, Action cancel)
    {
        InitializeComponent();
        Heading.Text = heading;
        Title = $"SkyBrowser · {heading}";
        _unit = unit;
        _cancel = cancel;
        CancellationNote.Text = cancellationNote;
        _showTimer.Tick += (_, _) =>
        {
            // Cached folder visits usually finish before this delay; avoid flashing a popup.
            _showTimer.Stop();
            if (!_finished && Owner is { IsVisible: true })
            {
                Show();
            }
        };
        _showTimer.Start();
    }

    public void SetTotal(int total)
    {
        _total = total;
        OverallProgress.IsIndeterminate = false;
        OverallProgress.Maximum = Math.Max(1, total);
        UpdateCounts();
    }

    public void BeginItem(string path)
    {
        ItemVersion++;
        CurrentItemText.Text = $"{char.ToUpperInvariant(_unit[0])}{_unit[1..]} {_completed + 1:N0} of {_total:N0}";
        FileNameText.Text = Path.GetFileName(path);
        FileNameText.ToolTip = path;
        Report(new("Checking saved preview…"), ItemVersion);
    }

    public void FinishItem(int images = 0, int cached = 0, bool failed = false, bool skipped = false)
    {
        // Invalidate callbacks already posted by the completed file's worker thread.
        ItemVersion++;
        _completed++;
        _images += images;
        _cached += cached;
        if (failed)
        {
            _failed++;
        }

        if (skipped)
        {
            _skipped++;
        }

        UpdateCounts();
    }

    private void UpdateCounts()
    {
        CountText.Text = $"{_completed:N0} of {_total:N0} {_unit}{(_total == 1 ? "" : "s")} finished · {_total - _completed:N0} remaining";
        OverallProgress.Value = _completed;
        ResultsText.Text = $"{_images:N0} image{(_images == 1 ? "" : "s")} ready · {_cached:N0} cached · {_failed:N0} failed · {_skipped:N0} already loaded";
    }

    public void Report(ImageProcessingProgress update, int itemVersion)
    {
        if (_finished || _cancelling || itemVersion != ItemVersion)
        {
            return;
        }

        StageText.Text = update.Stage;
        double? percentage = update.Percentage is { } value && double.IsFinite(value) ? Math.Clamp(value, 0, 100) : null;
        StageProgress.IsIndeterminate = percentage is null;
        StageProgress.Value = percentage ?? 0;
        StagePercent.Text = percentage is { } percent ? $"{percent:0}%" : "";
    }

    public void ShowCancelling()
    {
        if (_finished || _cancelling)
        {
            return;
        }

        _cancelling = true;
        Heading.Text = "Stopping…";
        CancelLoadButton.IsEnabled = false;
        CancelLoadButton.Content = "Cancelling…";
        StageText.Text = "Waiting for the current processing step to stop…";
        StageProgress.IsIndeterminate = true;
        StagePercent.Text = "";
        CancellationNote.Text += " Some decoding and cleanup steps must finish before cancellation completes.";
    }

    public void Finish()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _showTimer.Stop();
        Close();
    }

    private void RequestCancellation()
    {
        if (_finished || _cancelling)
        {
            return;
        }

        ShowCancelling();
        _cancel();
    }

    private void CancelClicked(object sender, RoutedEventArgs e) => RequestCancellation();

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_finished)
        {
            e.Cancel = true;
            RequestCancellation();
        }
    }

    private void WindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            RequestCancellation();
            e.Handled = true;
        }
    }
}
