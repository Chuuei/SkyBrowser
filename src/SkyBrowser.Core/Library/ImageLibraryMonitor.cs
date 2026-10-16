using SkyBrowser.Core.Caching;

namespace SkyBrowser.Core.Library;

/// <summary>Refreshes the archive on startup, on request and every thirty seconds.</summary>
public sealed class ImageLibraryMonitor : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _requested = new(0, 1);
    private readonly object _gate = new();
    private CancellationTokenSource? _currentScan;
    public Task Completion { get; }
    public string? LastError { get; private set; }
    public event EventHandler? Changed;

    public ImageLibraryMonitor(ImageLibrary library, Func<CachedSkyImageLoader> loader)
    {
        Completion = Task.Run(async () =>
        {
            while (!_lifetime.IsCancellationRequested)
            {
                using var scan = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                lock (_gate)
                { _currentScan = scan; }
                try
                {
                    await library.ScanAsync(loader(), token: scan.Token).ConfigureAwait(false);
                    LastError = null;
                }
                catch (OperationCanceledException) when (scan.IsCancellationRequested)
                {
                    LastError = null;
                }
                catch (Exception exception)
                {
                    LastError = exception.Message;
                }
                finally
                {
                    lock (_gate)
                    { _currentScan = null; }
                }
                if (_lifetime.IsCancellationRequested)
                { break; }
                Changed?.Invoke(this, EventArgs.Empty);
                try
                {
                    await _requested.WaitAsync(TimeSpan.FromSeconds(30), _lifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    break;
                }
            }
        });
    }

    public void CancelCurrentScan()
    {
        lock (_gate)
        { _currentScan?.Cancel(); }
    }

    public void RequestScan()
    {
        try
        {
            _requested.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public void Dispose() => _lifetime.Cancel();
}
