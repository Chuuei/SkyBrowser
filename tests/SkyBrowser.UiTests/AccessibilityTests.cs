using System.Runtime.CompilerServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;
using Xunit.Abstractions;
using static SkyBrowser.UiTests.DesktopSession;

namespace SkyBrowser.UiTests;

[Trait("Category", "Accessibility")]
public sealed class AccessibilityTests(ITestOutputHelper output)
{
    [Fact]
    public void FirstRunChoiceCanBeSavedWithTheKeyboardAndSurvivesRestart()
    {
        Run(app =>
        {
            var welcome = app.Window("Welcome to SkyBrowser");
            Assert.True(welcome.IsModal);
            var stretch = TabTo(welcome, ControlType.ComboBox, "Default image stretch");
            Assert.True(stretch.Patterns.ExpandCollapse.IsSupported);
            Assert.True(stretch.Patterns.Selection.IsSupported);
            Press(VirtualKeyShort.HOME, VirtualKeyShort.DOWN);
            Eventually(() => stretch.AsComboBox().SelectedItem?.Text == "Linear / already stretched",
                "Arrow keys did not change the default stretch.");
            ActivateButton(welcome, "Save & start");
            Eventually(() => app.FindWindow("Welcome to SkyBrowser") is null && app.MainWindow.IsEnabled, "Setup did not finish.");

            using (var settings = JsonDocument.Parse(File.ReadAllText(app.SettingsPath)))
            {
                Assert.Equal("Linear", settings.RootElement.GetProperty("DefaultDisplayMode").GetString());
                Assert.Equal(Path.Combine(app.DataDirectory, "Cache"), settings.RootElement.GetProperty("CacheDirectory").GetString());
            }

            app.Close();
            app.Launch();
            var options = app.OpenOptions();
            Assert.Null(app.FindWindow("Welcome to SkyBrowser"));
            Assert.Equal("Linear / already stretched", GetByRole(options, ControlType.ComboBox, "Default image stretch").AsComboBox().SelectedItem?.Text);
        });
    }

    [Fact]
    public void EscapeDefersFirstRunWithoutWritingSettings()
    {
        Run(app =>
        {
            var welcome = app.Window("Welcome to SkyBrowser");
            TabTo(welcome, ControlType.ComboBox, "Default image stretch");
            Press(VirtualKeyShort.ESCAPE);
            Eventually(() => app.FindWindow("Welcome to SkyBrowser") is null && app.MainWindow.IsEnabled, "Escape did not dismiss setup.");
            Assert.False(File.Exists(app.SettingsPath));
            Assert.False(File.Exists(app.SettingsPath + ".bak"));

            app.Close();
            app.Launch();
            Assert.True(app.Window("Welcome to SkyBrowser").IsModal);
        });
    }

    [Fact]
    public void OptionsCanBeEditedSavedAndCancelledWithTheKeyboard()
    {
        Run(app =>
        {
            app.CompleteWelcome();
            var options = app.OpenOptions();
            ReplaceText(options, "Sky background (%)", "12");
            ActivateButton(options, "Save options");
            Eventually(() => app.FindWindow("SkyBrowser · Options") is null && app.MainWindow.IsEnabled, "Options did not save.");
            string saved = File.ReadAllText(app.SettingsPath);
            using (var settings = JsonDocument.Parse(saved))
            {
                Assert.Equal(0.12, settings.RootElement.GetProperty("DefaultFinishSettings").GetProperty("TargetBackground").GetDouble());
            }

            options = app.OpenOptions();
            Assert.Equal("12", GetByRole(options, ControlType.Edit, "Sky background (%)").AsTextBox().Text);
            ReplaceText(options, "Sky background (%)", "15");
            Press(VirtualKeyShort.ESCAPE);
            Eventually(() => app.FindWindow("SkyBrowser · Options") is null && app.MainWindow.IsEnabled, "Escape did not cancel Options.");
            Assert.Equal(saved, File.ReadAllText(app.SettingsPath));
            options = app.OpenOptions();
            Assert.Equal("12", GetByRole(options, ControlType.Edit, "Sky background (%)").AsTextBox().Text);
        });
    }

    [Fact]
    public void ImageLibraryCanBeOpenedAndCancelledWithTheMouse()
    {
        Run(app =>
        {
            app.CompleteWelcome();
            GetByRole(app.MainWindow, ControlType.Button, "Image library…").AsButton().Click();
            var library = app.Window("SkyBrowser · Image library");
            Assert.False(library.IsModal);
            Assert.True(GetByRole(library, ControlType.List, "Library folders").Patterns.Selection.IsSupported);
            Assert.False(GetByRole(library, ControlType.Button, "Remove selected").IsEnabled);
            Assert.False(GetByRole(library, ControlType.Button, "Remove all").IsEnabled);
            ReplaceText(library, "Search library folders", "empty library");
            GetByRole(library, ControlType.Button, "Cancel").AsButton().Click();
            Eventually(() => app.FindWindow("SkyBrowser · Image library") is null, "Cancel did not close the library.");
            TabTo(app.MainWindow, ControlType.Button, "Options");
        });
    }

    [Fact]
    public void InvalidOptionsExposeDisabledSaveAndAllowKeyboardRecovery()
    {
        Run(app =>
        {
            app.CompleteWelcome();
            var options = app.OpenOptions();
            ReplaceText(options, "Sky background (%)", "99");
            var save = GetByRole(options, ControlType.Button, "Save options");
            Eventually(() => !save.IsEnabled, "An invalid background should disable Save options.");
            Assert.NotNull(app.FindWindow("SkyBrowser · Options"));
            ReplaceText(options, "Sky background (%)", "10");
            Eventually(() => save.IsEnabled, "A valid background should enable Save options again.");
            ActivateButton(options, "Save options");
            Eventually(() => app.FindWindow("SkyBrowser · Options") is null && app.MainWindow.IsEnabled, "Corrected options did not save.");
        });
    }

    [Theory]
    [InlineData("Images", 0, "Default image stretch", ControlType.ComboBox)]
    [InlineData("Observer", 1, "Use an observer location on startup", ControlType.CheckBox)]
    [InlineData("Cache", 2, "Preview cache folder", ControlType.Edit)]
    [InlineData("Plate solving", 3, "ASTAP executable", ControlType.Edit)]
    [InlineData("Sky", 4, "Coordinate grid", ControlType.CheckBox)]
    [InlineData("Accessibility", 5, "Interface size", ControlType.ComboBox)]
    public void SettingsTabsExposeNamedKeyboardControls(string tabName, int index, string controlName, ControlType role)
    {
        Run(app =>
        {
            app.CompleteWelcome();
            var options = app.OpenOptions();
            TabTo(options, ControlType.TabItem, "Images");
            for (int step = 0; step < index; step++)
            {
                Press(VirtualKeyShort.RIGHT);
            }

            var tab = GetByRole(options, ControlType.TabItem, tabName);
            Eventually(() => tab.Patterns.SelectionItem.Pattern.IsSelected.Value, $"Keyboard navigation did not select {tabName}.");
            var control = TabTo(options, role, controlName);
            if (role == ControlType.CheckBox)
            {
                var initialState = control.Patterns.Toggle.Pattern.ToggleState.Value;
                Press(VirtualKeyShort.SPACE);
                Eventually(() => control.Patterns.Toggle.Pattern.ToggleState.Value != initialState,
                    $"Space did not toggle '{controlName}'.");
                Press(VirtualKeyShort.SPACE);
                Eventually(() => control.Patterns.Toggle.Pattern.ToggleState.Value == initialState,
                    $"Space did not restore '{controlName}'.");
            }

            // Inspect the control view, excluding implementation parts such as a
            // ComboBox's internal dropdown toggle and a TextBox's scroll buttons.
            var walker = tab.Automation.TreeWalkerFactory.GetControlViewWalker();
            Assert.True(AuditControls(tab, walker) > 0, $"No accessible controls were audited in {tabName}.");
            Assert.True(GetByRole(options, ControlType.Button, "Cancel").Patterns.Invoke.IsSupported);
            Assert.True(GetByRole(options, ControlType.Button, "Save options").Patterns.Invoke.IsSupported);
        }, $"{nameof(SettingsTabsExposeNamedKeyboardControls)}-{index}");
    }

    [Fact]
    public void SkyHasReadableStateAndKeyboardRegionNavigation()
    {
        Run(app =>
        {
            app.CompleteWelcome();
            TabTo(app.MainWindow, ControlType.Edit, "Search image collection");
            Press(VirtualKeyShort.F6);
            var search = GetByRole(app.MainWindow, ControlType.Edit, "Filter sky targets");
            Assert.True(search.Properties.HasKeyboardFocus.Value);
            Press(VirtualKeyShort.F6);
            var sky = GetByRole(app.MainWindow, ControlType.Pane, "Sky atlas");
            Assert.True(sky.Properties.HasKeyboardFocus.Value);
            Assert.True(sky.Patterns.Value.IsSupported);
            Assert.True(sky.Patterns.Value.Pattern.IsReadOnly.Value);
            string before = sky.Patterns.Value.Pattern.Value.Value;
            Assert.Contains("right ascension", before);
            Press(VirtualKeyShort.RIGHT);
            Eventually(() => sky.Patterns.Value.Pattern.Value.Value != before, "Sky coordinates did not update accessibly.");
            Press(VirtualKeyShort.F6);
            Assert.True(GetByRole(app.MainWindow, ControlType.Edit, "Search image collection").Properties.HasKeyboardFocus.Value);
            Press(VirtualKeyShort.F1);
            var help = app.Window("SkyBrowser · Accessibility help");
            Assert.Contains("Keyboard and accessibility guide", GetByRole(help, ControlType.Edit, "Keyboard and accessibility guide").AsTextBox().Text);
            Press(VirtualKeyShort.ESCAPE);
            Eventually(() => app.FindWindow("SkyBrowser · Accessibility help") is null, "Help did not close with Escape.");
        });
    }

    [Fact]
    public void ClosingTheLibraryRestoresFocusWithoutAutomationForcingIt()
    {
        Run(app =>
        {
            app.CompleteWelcome();
            ActivateButton(app.MainWindow, "Image library…");
            var library = app.Window("SkyBrowser · Image library");
            ActivateButton(library, "Cancel");
            Eventually(() => GetByRole(app.MainWindow, ControlType.Button, "Image library…").Properties.HasKeyboardFocus.Value,
                "Closing the library did not restore focus to its opener.");
        });
    }

    [Fact]
    public void ObserverMapHasReadablePositionAndNamedInputs()
    {
        Run(app =>
        {
            var welcome = app.Window("Welcome to SkyBrowser");
            ActivateButton(welcome, "Choose on map…");
            var observer = app.Window("SkyBrowser · Location & time");
            var map = TabTo(observer, ControlType.Pane, "Choose observer location on the world map");
            Assert.True(map.Patterns.Value.IsSupported);
            Press(VirtualKeyShort.RIGHT);
            Eventually(() => map.Patterns.Value.Pattern.Value.Value.Contains("longitude"), "The map did not expose the selected coordinates.");
            ReplaceText(observer, "Observer latitude", "0");
            ReplaceText(observer, "Observer longitude", "0");
            Assert.True(GetByRole(observer, ControlType.Button, "Apply & face south").IsEnabled);
            Press(VirtualKeyShort.ESCAPE);
            Eventually(() => app.FindWindow("SkyBrowser · Location & time") is null, "Observer dialog did not cancel.");
        });
    }

    [Fact]
    public void SkyMeasurementsAndPlanningAreUsableWithoutPointingAtGraphics()
    {
        Run(app =>
        {
            app.CompleteWelcome();
            ActivateButton(app.MainWindow, "Sky tools…");
            var tools = app.Window("SkyBrowser · Sky tools");
            TabTo(tools, ControlType.TabItem, "Survey background");
            Press(VirtualKeyShort.RIGHT);
            ReplaceText(tools, "Planning latitude (degrees north)", "0");
            ReplaceText(tools, "Planning longitude (degrees east)", "0");
            ActivateButton(tools, "Calculate visibility");
            var chart = TabTo(tools, ControlType.Pane, "Visibility altitude chart");
            Eventually(() => chart.Patterns.Value.Pattern.Value.Value.Contains("Target altitude"), "Planning did not expose readable chart samples.");
            string before = chart.Patterns.Value.Pattern.Value.Value;
            Press(VirtualKeyShort.RIGHT);
            Eventually(() => chart.Patterns.Value.Pattern.Value.Value != before, "Arrow keys did not move the readable chart sample.");
            TabTo(tools, ControlType.TabItem, "Target visibility");
            Press(VirtualKeyShort.RIGHT);
            ReplaceText(tools, "Measurement A right ascension (degrees)", "0");
            ReplaceText(tools, "Measurement A declination (degrees)", "0");
            ReplaceText(tools, "Measurement B right ascension (degrees)", "90");
            ReplaceText(tools, "Measurement B declination (degrees)", "0");
            ActivateButton(tools, "Measure coordinates");
            Eventually(() => tools.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                .Any(element => element.Name.StartsWith("Angular distance") && element.Name.Contains("90")),
                "Exact coordinate measurement did not expose its result.");
        });
    }

    [Fact]
    public void LargeInterfaceContrastAndMotionPreferencesPersistAndRemainKeyboardUsable()
    {
        Run(app =>
        {
            app.CompleteWelcome();
            var options = app.OpenOptions();
            TabTo(options, ControlType.TabItem, "Images");
            Press(VirtualKeyShort.RIGHT, VirtualKeyShort.RIGHT, VirtualKeyShort.RIGHT, VirtualKeyShort.RIGHT, VirtualKeyShort.RIGHT);
            TabTo(options, ControlType.ComboBox, "Interface size");
            Press(VirtualKeyShort.END);
            TabTo(options, ControlType.CheckBox, "Use high contrast colors");
            Press(VirtualKeyShort.SPACE);
            TabTo(options, ControlType.CheckBox, "Reduce motion and disable automatic blinking");
            Press(VirtualKeyShort.SPACE);
            ActivateButton(options, "Save options");
            Eventually(() => app.FindWindow("SkyBrowser · Options") is null, "Accessibility preferences did not save.");
            using (var settings = JsonDocument.Parse(File.ReadAllText(app.SettingsPath)))
            {
                Assert.Equal(2, settings.RootElement.GetProperty("InterfaceScale").GetDouble());
                Assert.True(settings.RootElement.GetProperty("UseHighContrast").GetBoolean());
                Assert.True(settings.RootElement.GetProperty("ReduceMotion").GetBoolean());
            }
            app.Close();
            app.Launch();
            options = app.OpenOptions();
            TabTo(options, ControlType.TabItem, "Images");
            Press(VirtualKeyShort.RIGHT, VirtualKeyShort.RIGHT, VirtualKeyShort.RIGHT, VirtualKeyShort.RIGHT, VirtualKeyShort.RIGHT);
            var size = TabTo(options, ControlType.ComboBox, "Interface size");
            Assert.Equal("200%", size.AsComboBox().SelectedItem?.Text);
            Assert.Equal(true, GetByRole(options, ControlType.CheckBox, "Use high contrast colors").AsCheckBox().IsChecked);
            ActivateButton(options, "Cancel");
        });
    }

    [Fact]
    public void ValidationErrorsRaiseScreenReaderLiveRegionEvents()
    {
        Run(app =>
        {
            app.CompleteWelcome();
            var options = app.OpenOptions();
            var announcements = new System.Collections.Concurrent.ConcurrentQueue<string>();
            using var handler = options.RegisterAutomationEvent(options.Automation.EventLibrary.Element.LiveRegionChangedEvent,
                FlaUI.Core.Definitions.TreeScope.Subtree, (element, _) => announcements.Enqueue(element.Name));
            ReplaceText(options, "Sky background (%)", "99");
            Eventually(() => announcements.Any(message => message.Contains("background", StringComparison.OrdinalIgnoreCase)),
                "Validation did not raise an accessible live-region announcement.");
            Assert.False(GetByRole(options, ControlType.Button, "Save options").IsEnabled);
        });
    }

    private static int AuditControls(AutomationElement parent, FlaUI.Core.ITreeWalker walker)
    {
        int count = 0;
        for (var element = walker.GetFirstChild(parent); element is not null; element = walker.GetNextSibling(element))
        {
            if (element.IsOffscreen)
            {
                continue;
            }

            if (element.ControlType == ControlType.ScrollBar)
            {
                // A scrollbar is a composite RangeValue control. Its track's
                // page buttons deliberately do not participate in tab navigation.
                Assert.True(element.Patterns.RangeValue.IsSupported);
                continue;
            }

            bool interactive = element.ControlType is ControlType.Edit or ControlType.ComboBox or ControlType.CheckBox or ControlType.Button;
            if (interactive)
            {
                count++;
                Assert.False(string.IsNullOrWhiteSpace(element.Name), $"Unnamed {element.ControlType} ({element.AutomationId}).");
                if (element.IsEnabled)
                {
                    Assert.True(element.Properties.IsKeyboardFocusable.Value, $"'{element.Name}' cannot receive keyboard focus.");
                }

                Assert.True(element.ControlType switch
                {
                    ControlType.Edit => element.Patterns.Value.IsSupported,
                    ControlType.ComboBox => element.Patterns.Selection.IsSupported && element.Patterns.ExpandCollapse.IsSupported,
                    ControlType.CheckBox => element.Patterns.Toggle.IsSupported,
                    ControlType.Button => element.Patterns.Invoke.IsSupported,
                    _ => false
                }, $"'{element.Name}' lacks its standard accessibility pattern.");
            }
            else
            {
                count += AuditControls(element, walker);
            }
        }

        return count;
    }

    private void Run(Action<DesktopSession> scenario, [CallerMemberName] string testName = "")
    {
        using var app = new DesktopSession(testName);
        output.WriteLine($"Isolated profile and failure artifacts: {app.RunDirectory}");
        try
        {
            app.Launch();
            scenario(app);
        }
        catch
        {
            app.CaptureFailure();
            throw;
        }
    }
}
