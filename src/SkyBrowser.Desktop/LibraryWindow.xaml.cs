using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SkyBrowser.Core.Library;

namespace SkyBrowser.Desktop;

public sealed record LibraryFolder(string Path, bool Available)
{
    public string Name => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path)) is { Length: > 0 } name ? name : Path;
    public string Status => Available ? "Includes subfolders" : "Unavailable · kept in library";
}

public partial class LibraryWindow : Window
{
    private readonly ImageLibrary _library;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<string> _paths = [];
    private LibraryFolder[] _rows = [];
    private Task? _initialization;
    private bool _closed, _applying, _initialized;
    private int _refreshVersion;

    public Func<IReadOnlyList<string>, Task<bool>>? ApplyFoldersAsync { get; init; }
    public IReadOnlyList<string> PendingFolders => _paths.ToArray();

    public LibraryWindow(ImageLibrary library)
    {
        _library = library;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try
            {
                await InitializeAsync();
                if (!_closed)
                {
                    LibrarySearch.Focus();
                }
            }
            catch (Exception exception)
            {
                if (!_closed)
                {
                    LibraryStatus.Text = $"Library unavailable: {exception.Message}";
                }
            }
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _lifetime.Cancel();
        };
    }

    public Task InitializeAsync() => _initialization ??= LoadFoldersAsync();

    private async Task LoadFoldersAsync()
    {
        var folders = await Task.Run(_library.GetFolders, _lifetime.Token);
        if (_closed)
        {
            return;
        }
        _paths.AddRange(folders);
        _initialized = true;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_closed || !_initialized || _applying)
        {
            return;
        }
        string[] paths = _paths.ToArray();
        int version = ++_refreshVersion;
        var rows = await Task.Run(() => paths.Select(path => new LibraryFolder(path, Directory.Exists(path))).ToArray(), _lifetime.Token);
        if (!_closed && version == _refreshVersion && paths.SequenceEqual(_paths))
        {
            _rows = rows;
            RefreshView();
        }
    }

    private void RefreshView()
    {
        if (!_initialized || _closed)
        {
            return;
        }
        var selected = FolderList.SelectedItems.Cast<LibraryFolder>().Select(folder => folder.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] terms = LibrarySearch.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var visible = _rows.Where(folder => terms.All(term => folder.Path.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
        FolderList.ItemsSource = visible;
        foreach (var folder in visible.Where(folder => selected.Contains(folder.Path)))
        {
            FolderList.SelectedItems.Add(folder);
        }
        LibrarySearchPlaceholder.Visibility = LibrarySearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLibrary.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLibrary.Text = _paths.Count == 0 ? "Add a folder to start your library. Images load after you choose Load folders." : "No matching folders. Clear the search to see your full list.";
        LibraryStatus.Text = $"{_paths.Count:N0} folders · {_rows.Count(folder => !folder.Available):N0} unavailable" +
            (terms.Length == 0 ? "" : $" · {visible.Length:N0} shown; all {_paths.Count:N0} folders load when confirmed");
        UpdateButtons();
    }

    public async Task AddFolderAsync(string path)
    {
        await InitializeAsync();
        if (_closed || _applying)
        {
            return;
        }
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        bool available = await Task.Run(() => Directory.Exists(path), _lifetime.Token);
        if (_closed || _applying)
        {
            return;
        }
        if (!available)
        {
            throw new DirectoryNotFoundException("Choose an available image folder.");
        }
        if (!_paths.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            _paths.Add(path);
        }
        LibrarySearch.Clear();
        await RefreshAsync();
        if (!_closed)
        {
            FolderList.SelectedItem = _rows.FirstOrDefault(folder => folder.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void RemoveSelectedFolders()
    {
        if (_closed || _applying)
        {
            return;
        }
        var removed = FolderList.SelectedItems.Cast<LibraryFolder>().Select(folder => folder.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _paths.RemoveAll(removed.Contains);
        _rows = _rows.Where(folder => !removed.Contains(folder.Path)).ToArray();
        ++_refreshVersion;
        RefreshView();
    }

    public void ClearFolders()
    {
        if (_closed || _applying)
        {
            return;
        }
        _paths.Clear();
        _rows = [];
        ++_refreshVersion;
        RefreshView();
    }

    public async Task ConfirmAsync()
    {
        await InitializeAsync();
        if (_closed || _applying || ApplyFoldersAsync is not { } apply)
        {
            return;
        }
        _applying = true;
        UpdateButtons();
        Hide();
        try
        {
            if (await apply(_paths.ToArray()))
            {
                if (!_closed)
                {
                    Close();
                }
            }
            else if (!_closed)
            {
                Show();
                LibraryStatus.Text = "Loading did not finish. Folder changes already confirmed are retained; you can retry or close.";
            }
        }
        catch (Exception exception)
        {
            if (!_closed)
            {
                Show();
                LibraryStatus.Text = $"Could not apply folders: {exception.Message}";
            }
        }
        finally
        {
            _applying = false;
            if (!_closed)
            {
                UpdateButtons();
            }
        }
    }

    private void UpdateButtons()
    {
        if (ConfirmFoldersButton is null)
        {
            return;
        }
        AddArchiveButton.IsEnabled = LibrarySearch.IsEnabled = FolderList.IsEnabled = !_applying && _initialized;
        RemoveArchiveButton.IsEnabled = !_applying && FolderList.SelectedItems.Count > 0;
        ClearFoldersButton.IsEnabled = !_applying && _paths.Count > 0;
        // An empty list must be confirmable to save Remove all.
        ConfirmFoldersButton.IsEnabled = !_applying && _initialized && ApplyFoldersAsync is not null;
        CancelFoldersButton.IsEnabled = !_applying;
    }

    private async void AddFolderClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Add an image folder (including subfolders)", Multiselect = true };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }
        try
        {
            foreach (string folder in dialog.FolderNames)
            {
                await AddFolderAsync(folder);
            }
        }
        catch (Exception exception)
        {
            if (!_closed)
            {
                LibraryStatus.Text = exception.Message;
            }
        }
    }

    private void RemoveFolderClicked(object sender, RoutedEventArgs e) => RemoveSelectedFolders();
    private void ClearFoldersClicked(object sender, RoutedEventArgs e) => ClearFolders();
    private void SearchChanged(object sender, TextChangedEventArgs e) => RefreshView();
    private void SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();
    private async void ConfirmClicked(object sender, RoutedEventArgs e) => await ConfirmAsync();
    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
}
