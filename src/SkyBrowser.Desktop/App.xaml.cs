using System.Windows;
using SkyBrowser.Core.Configuration;

namespace SkyBrowser.Desktop;

public partial class App : Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        // Release screen-reader connections before WPF's dispatcher/runtime exits.
        // https://learn.microsoft.com/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiadisconnectallproviders
        Services.Accessibility.ReleaseAutomationProviders();
        base.OnExit(e);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Tests and the anonymous screenshot tool create their own window before
        // the dispatcher starts. Do not open another one with the user's settings.
        if (MainWindow is not null)
        {
            return;
        }

        // A separate profile keeps automated UI runs and portable sessions away
        // from the user's settings, image library and default preview cache.
        string? dataDirectory = Environment.GetEnvironmentVariable("SKYBROWSER_DATA_DIRECTORY");
        var store = string.IsNullOrWhiteSpace(dataDirectory)
            ? ApplicationOptionsStore.Default
            : new ApplicationOptionsStore(
                System.IO.Path.Combine(dataDirectory, "settings.json"),
                new() { CacheDirectory = System.IO.Path.GetFullPath(System.IO.Path.Combine(dataDirectory, "Cache")) });
        var window = new MainWindow(store);
        MainWindow = window;
        window.Loaded += async (_, _) => await window.OpenStartupAsync(e.Args);
        window.Show();
    }
}
