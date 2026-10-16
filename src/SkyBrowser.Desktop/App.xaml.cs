using System.Windows;

namespace SkyBrowser.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Tests and the anonymous screenshot tool create their own window before
        // the dispatcher starts. Do not open another one with the user's settings.
        if (MainWindow is not null)
        {
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Loaded += async (_, _) => await window.OpenStartupAsync(e.Args);
        window.Show();
    }
}
