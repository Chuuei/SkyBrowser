using System.Windows;

namespace SkyBrowser.Desktop;

public partial class AccessibilityHelpWindow : Window
{
    public AccessibilityHelpWindow()
    {
        InitializeComponent();
        HelpText.Text = """
            Keyboard and accessibility guide

            Press F1 in any window to open this guide. Escape closes dialogs without saving, unless an operation is being cancelled.
            Tab and Shift+Tab move between controls. Arrow keys choose list, dropdown and tab items. Space toggles checkboxes and activates buttons. Enter activates a dialog's default action.

            Main window
            F6 / Shift+F6: cycle between image collection, target search and sky view.
            Ctrl+F: search the image collection. Ctrl+O: add images. Ctrl+L: image library. Ctrl+comma: Options.
            In Options → Accessibility, enlarge the entire interface up to 200%, use high contrast colors, or reduce motion. Enlarged windows can be scrolled; Tab brings focused controls into view. Windows contrast themes and animation preferences are also respected.

            Sky without a mouse
            Use Find in the sky to search named targets or enter right ascension and declination in degrees. The sky control exposes its center, field of view and selected image as readable text.
            With the sky focused: arrows pan; plus/minus zoom; Home shows the overview; F fits the selected image; Enter inspects it; Space selects the image at the center; Escape clears selection or cancels point picking.
            Select an image from the image collection to use its named visibility, opacity, placement and inspection controls. In Sky tools → Measurements, enter exact coordinates to measure without picking points on the sky.

            Location and planning
            Enter latitude and longitude directly; negative values mean south or west. On the world map, arrows move the selected position and plus/minus change zoom. Its readable value reports the position.
            Visibility results include written event times and imaging windows. Focus the altitude chart and use Left/Right to read successive samples; Home/End jump to the first/last sample.

            Full-resolution image inspection
            Focus the image: arrows move the sample cursor, Shift+arrows move ten pixels, Home/End move to the first/last pixel, and plus/minus zoom. The pixel readout provides stored coordinates, displayed color, original sample values and available sky coordinates. You can also enter exact X/Y pixel coordinates and choose Read pixel.

            Objects in 3D
            Search and select objects in the named list. Use Select second target and choose another object to measure a pair without Shift-clicking. Written details provide distances, measurement results and source information. The map exposes a readable view summary.

            Image comparison
            Use linked views or the keyboard-operated swipe slider for a still comparison. Reduce motion stops automatic blinking; Switch frame changes the image manually.

            Screen readers
            Controls expose accessible names and standard roles. Results, validation and operation status use polite announcements. Constant clock ticks and mouse movements do not trigger spoken interruptions. Custom map values can be read when focused.
            """;
        Loaded += (_, _) => HelpText.Focus();
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
}
