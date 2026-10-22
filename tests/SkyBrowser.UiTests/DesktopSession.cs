using System.Diagnostics;
using System.Text;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace SkyBrowser.UiTests;

/// <summary>Drives the actual executable through Windows UI Automation and keyboard input.</summary>
internal sealed class DesktopSession : IDisposable
{
    private readonly UIA3Automation _automation = new();
    private Application? _application;

    public string RunDirectory { get; }
    public string DataDirectory => Path.Combine(RunDirectory, "profile");
    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public Window MainWindow { get; private set; } = null!;

    public DesktopSession(string testName)
    {
        RunDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "ui-tests", $"{testName}-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(DataDirectory);
    }

    public void Launch()
    {
        Assert.Null(_application);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Name;
        string executable = Environment.GetEnvironmentVariable("SKYBROWSER_UI_APP_PATH")
            ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "SkyBrowser.Desktop", configuration, "SkyBrowser.exe"));
        Assert.True(File.Exists(executable), $"Build SkyBrowser.Desktop first. Missing executable: {executable}");

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        start.Environment["SKYBROWSER_DATA_DIRECTORY"] = DataDirectory;
        _application = Application.Launch(start);
        MainWindow = Retry.WhileNull(
            () => _application.GetAllTopLevelWindows(_automation)
                .FirstOrDefault(window => window.Title.StartsWith("SkyBrowser v", StringComparison.Ordinal)),
            timeout: TimeSpan.FromSeconds(20),
            throwOnTimeout: true,
            timeoutMessage: "SkyBrowser did not expose its main window through UI Automation.").Result!;
    }

    public Window Window(string title)
    {
        return Retry.WhileNull(() => FindWindow(title),
            timeout: TimeSpan.FromSeconds(10),
            throwOnTimeout: true,
            timeoutMessage: $"The accessible window '{title}' did not open.").Result!;
    }

    public Window? FindWindow(string title)
    {
        return MainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Window).And(cf.ByName(title)))?.AsWindow()
            ?? _application!.GetAllTopLevelWindows(_automation).FirstOrDefault(window => window.Title == title);
    }

    public static AutomationElement GetByRole(Window window, ControlType role, string name, bool requireVisible = true)
    {
        var element = Retry.WhileNull(
            () => window.FindFirstDescendant(cf => cf.ByControlType(role).And(cf.ByName(name))),
            timeout: TimeSpan.FromSeconds(5),
            throwOnTimeout: true,
            timeoutMessage: $"No accessible {role} named '{name}' in '{window.Title}'.").Result!;
        if (requireVisible)
        {
            Assert.False(element.IsOffscreen, $"{role} '{name}' is offscreen.");
        }
        return element;
    }

    public static AutomationElement TabTo(Window window, ControlType role, string name)
    {
        var target = GetByRole(window, role, name, requireVisible: false);
        Assert.True(target.IsEnabled, $"{role} '{name}' is disabled.");
        Assert.True(target.Properties.IsKeyboardFocusable.Value, $"{role} '{name}' cannot receive keyboard focus.");
        window.SetForeground();
        window.FocusNative();

        // Only the top-level window is activated. Reaching each control must work
        // with Tab; setting focus directly would conceal broken tab navigation.
        for (int step = 0; step < 80; step++)
        {
            if (target.Properties.HasKeyboardFocus.Value)
            {
                Assert.False(target.IsOffscreen, $"Focused {role} '{name}' did not scroll into view.");
                return target;
            }

            Press(VirtualKeyShort.TAB);
        }

        throw new Xunit.Sdk.XunitException($"Tab could not reach {role} '{name}' in '{window.Title}'.");
    }

    public static void ActivateButton(Window window, string name)
    {
        var button = TabTo(window, ControlType.Button, name);
        Assert.True(button.Patterns.Invoke.IsSupported, $"Button '{name}' has no accessible Invoke pattern.");
        Press(VirtualKeyShort.SPACE);
    }

    public static void ReplaceText(Window window, string name, string value)
    {
        var input = TabTo(window, ControlType.Edit, name);
        Assert.True(input.Patterns.Value.IsSupported, $"Input '{name}' has no accessible Value pattern.");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(value);
        Wait.UntilInputIsProcessed();
        Eventually(() => input.AsTextBox().Text == value, $"Typing into '{name}' did not update its accessible value.");
    }

    public static void Press(params VirtualKeyShort[] keys)
    {
        Keyboard.Type(keys);
        Wait.UntilInputIsProcessed();
    }

    public static void Eventually(Func<bool> condition, string message)
    {
        Retry.WhileFalse(condition, timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(100), throwOnTimeout: true,
            ignoreException: true, timeoutMessage: message);
    }

    public void CompleteWelcome()
    {
        var welcome = Window("Welcome to SkyBrowser");
        ActivateButton(welcome, "Use defaults");
        Eventually(() => FindWindow("Welcome to SkyBrowser") is null && MainWindow.IsEnabled, "Setup did not return to the atlas.");
    }

    public Window OpenOptions()
    {
        ActivateButton(MainWindow, "Options");
        var options = Window("SkyBrowser · Options");
        Assert.True(options.IsModal);
        return options;
    }

    public void Close()
    {
        var application = _application;
        if (application is null)
        {
            return;
        }

        _application = null;
        try
        {
            if (!application.HasExited && MainWindow is not null)
            {
                foreach (var dialog in MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Window))
                    .Select(element => element.AsWindow()).Where(window => window.IsModal).Reverse())
                {
                    dialog.Close();
                }
            }

            application.Close();
        }
        finally
        {
            // Also release the child if startup or modal cleanup failed.
            application.Kill();
            application.Dispose();
        }
    }

    public void CaptureFailure()
    {
        // Capture only windows belonging to the test process, never the desktop.
        try
        {
            var tree = new StringBuilder();
            DumpTree(MainWindow, _automation.TreeWalkerFactory.GetControlViewWalker(), tree, 0);
            File.WriteAllText(Path.Combine(RunDirectory, "accessibility-tree.txt"), tree.ToString());
            var windows = _application!.GetAllTopLevelWindows(_automation);
            for (int i = 0; i < windows.Length; i++)
            {
                using var capture = Capture.Element(windows[i]);
                capture.ToFile(Path.Combine(RunDirectory, $"window-{i}.png"));
            }
        }
        catch (Exception exception)
        {
            File.WriteAllText(Path.Combine(RunDirectory, "capture-error.txt"), exception.ToString());
        }
    }

    private static void DumpTree(AutomationElement element, ITreeWalker walker, StringBuilder tree, int depth)
    {
        tree.Append(' ', depth * 2).Append(element.Properties.ControlType.ValueOrDefault)
            .Append(" name='").Append(element.Properties.Name.ValueOrDefault)
            .Append("' id='").Append(element.Properties.AutomationId.ValueOrDefault)
            .Append("' focused=").Append(element.Properties.HasKeyboardFocus.ValueOrDefault)
            .Append(" enabled=").Append(element.Properties.IsEnabled.ValueOrDefault)
            .Append(" offscreen=").Append(element.Properties.IsOffscreen.ValueOrDefault).AppendLine();
        if (depth >= 30)
        {
            return;
        }

        for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
        {
            DumpTree(child, walker, tree, depth + 1);
        }
    }

    public void Dispose()
    {
        try
        {
            Close();
        }
        catch (Exception exception)
        {
            // Preserve the original test failure; cleanup diagnostics are separate.
            File.WriteAllText(Path.Combine(RunDirectory, "cleanup-error.txt"), exception.ToString());
        }
        finally
        {
            _automation.Dispose();
        }
    }
}
