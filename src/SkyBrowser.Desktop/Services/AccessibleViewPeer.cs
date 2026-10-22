using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;

namespace SkyBrowser.Desktop.Services;

/// <summary>Exposes a custom visualization's current text equivalent without losing its child controls.</summary>
internal sealed class AccessibleViewPeer(FrameworkElement owner, Func<string> readValue)
    : FrameworkElementAutomationPeer(owner), IValueProvider
{
    private string _previousValue = readValue();

    protected override string GetClassNameCore() => Owner.GetType().Name;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Pane;
    protected override bool IsControlElementCore() => true;
    protected override bool IsContentElementCore() => true;
    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);

    public bool IsReadOnly => true;
    public string Value => readValue();
    public void SetValue(string value) => throw new InvalidOperationException("Use the named navigation controls to change this view.");

    public void NotifyValueChanged()
    {
        string current = Value;
        if (current != _previousValue)
        {
            if (ListenerExists(AutomationEvents.PropertyChanged))
            {
                RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, _previousValue, current);
            }
            _previousValue = current;
        }
    }

    internal static void Notify(FrameworkElement element)
    {
        if (UIElementAutomationPeer.FromElement(element) is AccessibleViewPeer peer)
        {
            peer.NotifyValueChanged();
        }
    }
}
