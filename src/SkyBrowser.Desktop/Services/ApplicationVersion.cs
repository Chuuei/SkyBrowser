using System.Reflection;

namespace SkyBrowser.Desktop.Services;

public static class ApplicationVersion
{
    // Use the desktop assembly: tests and the manual generator have their own entry assemblies.
    public static string InformationalVersion { get; } = typeof(App).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(App).Assembly.GetName().Version?.ToString(3)
        ?? "unknown";

    public static string DisplayVersion { get; } = $"v{InformationalVersion.Split('+')[0]}";

    public static string WindowTitle => $"SkyBrowser {DisplayVersion}";
}
