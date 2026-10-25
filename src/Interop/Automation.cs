using System.Runtime.InteropServices;

namespace SteppedOut;

// What screen readers are told about the hand-drawn controls beyond what WinForms tells them.
// WinForms answers MSAA for every control, and without a Provider Narrator would read a hand-drawn
// one through that. But MSAA cannot say that a button is on or that a list is open, and in testing
// no change or focus on a control read that way reached a UI Automation client. So each
// hand-drawn control answers UI Automation itself.
// The interfaces are declared by hand, as in Wasapi.cs. Method order in each interface must match
// the COM vtable exactly.
static class Automation
{
    const int WmGetObject = 0x003D;
    const int UiaRootObjectId = -25;
    const int ObjidClient = -4;
    const int ChildidSelf = 0;
    const int ServerSideProvider = 0x2;
    const int UseComThreading = 0x20;

    public const int ButtonType = 50000;
    public const int CheckBoxType = 50002;
    public const int ComboBoxType = 50003;
    public const int RadioButtonType = 50013;
    public const int TextType = 50020;
    public const int ListItemType = 50007;
    public const int ListType = 50008;
    public const int TabType = 50018;
    public const int TabItemType = 50019;
    public const int GroupType = 50026;
    public const int PaneType = 50033;

    public const int InvokePattern = 10000;
    public const int SelectionPattern = 10001;
    public const int ValuePattern = 10002;
    public const int ExpandCollapsePattern = 10005;
    public const int SelectionItemPattern = 10010;
    public const int TogglePattern = 10015;

    public const int ControlTypeProperty = 30003;
    public const int NameProperty = 30005;
    public const int HasKeyboardFocusProperty = 30008;
    public const int IsKeyboardFocusableProperty = 30009;
    public const int HelpTextProperty = 30013;
    public const int ValueProperty = 30045;
    public const int ExpandCollapseStateProperty = 30070;
    public const int IsSelectedProperty = 30079;
    public const int ToggleStateProperty = 30086;
    public const int HeadingLevelProperty = 30173;
    public const int NotAHeading = 80050;

    // Tells MSAA screen readers, such as NVDA, that something about the control changed.
    // AccessibilityNotifyClients is meant to, but in .NET 10 it sends nothing (dotnet/winforms#15176).
    public static void Notify(Control control, AccessibleEvents what)
    {
        if (control.IsHandleCreated) NotifyWinEvent((uint)what, control.Handle, ObjidClient, ChildidSelf);
    }

    // UI Automation has no place for MSAA's description; help text is the nearest it has. So what a
    // control explains reaches UI Automation readers too.
    public static void HelpFromDescription(Control control) =>
        control.QueryAccessibilityHelp += (_, e) => e.HelpString = control.AccessibleDescription;

    // UI Automation asking the control's window for its provider.
    public static bool IsAsking(Message m) => m.Msg == WmGetObject && (int)(long)m.LParam == UiaRootObjectId;

    public static IntPtr Answer(Message m, Provider provider) => UiaReturnRawElementProvider(m.HWnd, m.WParam, m.LParam, provider);

    // Once the window is gone, UI Automation lets go of it and of the provider, so a screen reader
    // holding on to it cannot keep the control alive.
    public static void Release(IntPtr window, Provider? provider)
    {
        UiaReturnRawElementProvider(window, IntPtr.Zero, IntPtr.Zero, null);
        if (provider is not null) UiaDisconnectProvider(provider);
    }

    // Whether a change is worth telling: only a control with a window can be heard, and only while a
    // UI Automation client, such as Narrator, is running.
    public static bool Heard(Control control) => control.IsHandleCreated && UiaClientsAreListening();

    public static void ToggleChanged(Provider? provider, bool on) => Changed(provider, ToggleStateProperty, on ? 0 : 1, on ? 1 : 0);

    public static void SelectedChanged(Provider? provider, bool selected) => Changed(provider, IsSelectedProperty, !selected, selected);

    public static void OpenChanged(Provider? provider, bool open) => Changed(provider, ExpandCollapseStateProperty, open ? 0 : 1, open ? 1 : 0);

    public static void ValueChanged(Provider? provider, string was, string now) => Changed(provider, ValueProperty, was, now);

    public static void NameChanged(Provider? provider, string was, string now) => Changed(provider, NameProperty, was, now);

    static void Changed(Provider? provider, int property, object was, object now)
    {
        if (provider is not null) UiaRaiseAutomationPropertyChangedEvent(provider, property, was, now);
    }

    // One hand-drawn control as UI Automation sees it. Its name and help text are the ones the
    // control gives MSAA; what else it can do depends on which of these are set. Deriving from
    // StandardOleMarshalObject brings every call onto the control's own thread, as WinForms'
    // providers do.
    public sealed class Provider(Control owner, int controlType) : StandardOleMarshalObject,
        IRawElementProviderSimple, IInvokeProvider, IToggleProvider, IExpandCollapseProvider, IValueProvider, ISelectionItemProvider,
        ISelectionProvider
    {
        // Pressing the control: a click, or Toggle on a button or switch that is on or off, or
        // Select on one of a few choices.
        public Action? Press { get; init; }

        // For a button or switch that is on or off.
        public Func<bool>? IsOn { get; init; }

        // For one of a few choices.
        public Func<bool>? IsSelected { get; init; }

        // For a control that opens and closes.
        public Func<bool>? IsOpen { get; init; }
        public Action<bool>? SetOpen { get; init; }

        // What a list shows chosen.
        public Func<string>? Value { get; init; }

        // For a heading: 1 for a page's title, 2 for a section.
        public int HeadingLevel { get; init; }

        // For one of a few choices that sits in a group, such as a tab in a row of tabs: the group.
        public Func<Provider?>? Container { get; init; }

        // For a group of choices, such as a row of tabs: the ones chosen now, at most one.
        public Func<IEnumerable<Provider>>? Selection { get; init; }

        // Whether the group always has one chosen, as a row of tabs does.
        public bool SelectionRequired { get; init; }

        int IRawElementProviderSimple.ProviderOptions => ServerSideProvider | UseComThreading;

        object? IRawElementProviderSimple.GetPatternProvider(int pattern) => pattern switch
        {
            InvokePattern when Press is not null && IsOn is null && IsSelected is null => this,
            TogglePattern when IsOn is not null => this,
            SelectionItemPattern when IsSelected is not null => this,
            ExpandCollapsePattern when IsOpen is not null => this,
            ValuePattern when Value is not null => this,
            SelectionPattern when Selection is not null => this,
            _ => null,
        };

        // Anything not answered here, such as where the control is, comes from its window.
        object? IRawElementProviderSimple.GetPropertyValue(int property) => property switch
        {
            ControlTypeProperty => controlType,
            NameProperty => owner.AccessibilityObject.Name,
            IsKeyboardFocusableProperty => owner.CanSelect,
            HasKeyboardFocusProperty => owner.Focused,
            HeadingLevelProperty when HeadingLevel > 0 => NotAHeading + HeadingLevel,
            HelpTextProperty => owner.AccessibilityObject.Help is { Length: > 0 } help ? help : null,
            _ => null,
        };

        IRawElementProviderSimple? IRawElementProviderSimple.HostRawElementProvider =>
            owner.IsHandleCreated && UiaHostProviderFromHwnd(owner.Handle, out var host) == 0 ? host : null;

        void IInvokeProvider.Invoke() => Press!();

        void IToggleProvider.Toggle() => Press!();

        int IToggleProvider.ToggleState => IsOn!() ? 1 : 0;

        void IExpandCollapseProvider.Expand() => SetOpen!(true);

        void IExpandCollapseProvider.Collapse() => SetOpen!(false);

        int IExpandCollapseProvider.ExpandCollapseState => IsOpen!() ? 1 : 0;

        void IValueProvider.SetValue(string value) => throw new InvalidOperationException();

        string IValueProvider.Value => Value!();

        bool IValueProvider.IsReadOnly => true;

        void ISelectionItemProvider.Select() => Press!();

        void ISelectionItemProvider.AddToSelection() => throw new InvalidOperationException();

        void ISelectionItemProvider.RemoveFromSelection() => throw new InvalidOperationException();

        bool ISelectionItemProvider.IsSelected => IsSelected!();

        IRawElementProviderSimple? ISelectionItemProvider.SelectionContainer => Container?.Invoke();

        IRawElementProviderSimple[] ISelectionProvider.GetSelection() => Selection!().ToArray<IRawElementProviderSimple>();

        bool ISelectionProvider.CanSelectMultiple => false;

        bool ISelectionProvider.IsSelectionRequired => SelectionRequired;
    }

    [ComImport, Guid("D6DD68D1-86FD-4332-8666-9ABEDEA2D24C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IRawElementProviderSimple
    {
        int ProviderOptions { get; }
        [return: MarshalAs(UnmanagedType.IUnknown)] object? GetPatternProvider(int pattern);
        [return: MarshalAs(UnmanagedType.Struct)] object? GetPropertyValue(int property);
        IRawElementProviderSimple? HostRawElementProvider { get; }
    }

    [ComImport, Guid("54FCB24B-E18E-47A2-B4D3-ECCBE77599A2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IInvokeProvider
    {
        void Invoke();
    }

    [ComImport, Guid("56D00BD0-C4F4-433C-A836-1A52A57E0892"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IToggleProvider
    {
        void Toggle();
        int ToggleState { get; }
    }

    [ComImport, Guid("D847D3A5-CAB0-4A98-8C32-ECB45C59AD24"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IExpandCollapseProvider
    {
        void Expand();
        void Collapse();
        int ExpandCollapseState { get; }
    }

    [ComImport, Guid("C7935180-6FB3-4201-B174-7DF73ADBF64A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IValueProvider
    {
        void SetValue([MarshalAs(UnmanagedType.LPWStr)] string value);
        string Value { [return: MarshalAs(UnmanagedType.BStr)] get; }
        bool IsReadOnly { [return: MarshalAs(UnmanagedType.Bool)] get; }
    }

    [ComImport, Guid("2ACAD808-B2D4-452D-A407-91FF1AD167B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISelectionItemProvider
    {
        void Select();
        void AddToSelection();
        void RemoveFromSelection();
        bool IsSelected { [return: MarshalAs(UnmanagedType.Bool)] get; }
        IRawElementProviderSimple? SelectionContainer { get; }
    }

    [ComImport, Guid("FB8B03AF-3BDF-48D4-BD36-1A65793BE168"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ISelectionProvider
    {
        [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_UNKNOWN)] IRawElementProviderSimple[] GetSelection();
        bool CanSelectMultiple { [return: MarshalAs(UnmanagedType.Bool)] get; }
        bool IsSelectionRequired { [return: MarshalAs(UnmanagedType.Bool)] get; }
    }

    [DllImport("user32.dll")]
    static extern void NotifyWinEvent(uint what, IntPtr window, int objectId, int childId);

    [DllImport("UIAutomationCore.dll")]
    static extern IntPtr UiaReturnRawElementProvider(IntPtr window, IntPtr wParam, IntPtr lParam, IRawElementProviderSimple? provider);

    [DllImport("UIAutomationCore.dll")]
    static extern int UiaHostProviderFromHwnd(IntPtr window, out IRawElementProviderSimple provider);

    [DllImport("UIAutomationCore.dll")]
    static extern int UiaDisconnectProvider(IRawElementProviderSimple provider);

    [DllImport("UIAutomationCore.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool UiaClientsAreListening();

    [DllImport("UIAutomationCore.dll")]
    static extern int UiaRaiseAutomationPropertyChangedEvent(IRawElementProviderSimple provider, int property,
        [MarshalAs(UnmanagedType.Struct)] object was, [MarshalAs(UnmanagedType.Struct)] object now);
}
