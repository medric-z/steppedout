using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SteppedOut;

// The keyboard layouts installed for this session, and what a shortcut types on one of them.
interface IKeyboardLayouts
{
    // Every installed layout, the one in use among them.
    IReadOnlyList<IntPtr> Installed();

    // What pressing the shortcut types on this layout: "" when it types nothing or only a control
    // character, as Ctrl with a letter does.
    string Typed(IntPtr layout, Shortcut shortcut);
}

// RegisterHotKey and UnregisterHotKey, behind a seam so tests can register nothing for real.
interface IHotKeyApi
{
    // 0 when it is registered, or the Windows error code.
    int Register(IntPtr window, int id, uint modifiers, uint virtualKey);

    void Unregister(IntPtr window, int id);
}

// The lock shortcut, registered with RegisterHotKey on a hidden window, so no keyboard hook is
// needed. Pressed is raised on the thread that made this object, which must have a message loop
// (the tray's). Use it from that thread only.
//
// A shortcut that types a character on any installed keyboard layout is never registered: Ctrl+Alt
// is AltGr, so Ctrl+Alt+L would stop a Polish keyboard typing ł. Layouts can be added at any time,
// so the check runs again on every Apply and Recheck. Nothing here throws.
sealed class HotKey : IDisposable
{
    public const int WmHotKey = 0x0312;
    const uint ModNoRepeat = 0x4000;
    const int ErrorHotKeyAlreadyRegistered = 1409;
    const int Id = 0x5E01;

    readonly IKeyboardLayouts layouts;
    readonly IHotKeyApi api;
    MessageWindow? window;
    Shortcut? registered;
    bool disposed;

    // While SessionLock.Blocked is set (the tests and the screenshot tool, on a PC someone is using),
    // this registers nothing with Windows, so a stray real copy can never take the lock shortcut.
    public HotKey() : this(KeyboardLayouts.Instance, SessionLock.Blocked ? RefusingHotKeyApi.Instance : HotKeyApi.Instance)
    {
    }

    internal HotKey(IKeyboardLayouts layouts, IHotKeyApi api)
    {
        this.layouts = layouts;
        this.api = api;
    }

    // The shortcut was pressed.
    public event Action? Pressed;

    // Result or Shortcut changed.
    public event Action? ResultChanged;

    // How the last Apply went. Ok also when the shortcut is off.
    public HotKeyResult Result { get; private set; } = HotKeyResult.Ok;

    // The shortcut text last applied, "" for off.
    public string Shortcut { get; private set; } = "";

    // The shortcut is registered and Pressed will be raised.
    public bool IsRegistered => registered is not null;

    // The layout a shortcut types a character on, as its handle in hex, for the log.
    public string? TypingLayout { get; private set; }

    // Registers this shortcut in place of the one before, or with "" registers none. Calling it
    // again with the same shortcut checks the layouts again, and tries again if another app had it.
    public HotKeyResult Apply(string? shortcut)
    {
        string text = shortcut?.Trim() ?? "";
        HotKeyResult result;
        try
        {
            result = Register(text);
        }
        catch (Exception e)
        {
            Log.Write("lock shortcut: " + e.Message);
            Release();
            result = HotKeyResult.Invalid;
        }
        bool changed = result != Result || text != Shortcut;
        Result = result;
        Shortcut = text;
        if (changed) RaiseResultChanged();
        return result;
    }

    // Checks the layouts again for the shortcut in use: when the config is applied or the settings
    // window opens, since a layout may have been added meanwhile.
    public HotKeyResult Recheck() => Apply(Shortcut);

    // Whether a shortcut could be used, without registering it: Invalid, TypesALetter or Ok. Only
    // registering it shows whether another app has it.
    public static HotKeyResult Check(string? shortcut) => Check(shortcut, KeyboardLayouts.Instance, out _);

    internal static HotKeyResult Check(string? shortcut, IKeyboardLayouts layouts, out string? typingLayout)
    {
        typingLayout = null;
        if (!SteppedOut.Shortcut.TryParse(shortcut, out Shortcut s)) return HotKeyResult.Invalid;
        try
        {
            typingLayout = LayoutThatTypes(s, layouts);
        }
        catch (Exception e)
        {
            Log.Write("lock shortcut: could not read the keyboard layouts: " + e.Message);
        }
        return typingLayout is null ? HotKeyResult.Ok : HotKeyResult.TypesALetter;
    }

    // The first installed layout on which the shortcut types something, as hex, or null.
    internal static string? LayoutThatTypes(Shortcut shortcut, IKeyboardLayouts layouts)
    {
        foreach (IntPtr layout in layouts.Installed())
        {
            string typed;
            try
            {
                typed = layouts.Typed(layout, shortcut);
            }
            catch (Exception e)
            {
                Log.Write("lock shortcut: could not check layout " + Hex(layout) + ": " + e.Message);
                continue;
            }
            if (typed.Length > 0) return Hex(layout);
        }
        return null;
    }

    static string Hex(IntPtr layout) => ((ulong)layout.ToInt64()).ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

    HotKeyResult Register(string text)
    {
        TypingLayout = null;
        if (disposed) return HotKeyResult.Invalid;
        if (text.Length == 0)
        {
            Release();
            return HotKeyResult.Ok;
        }
        HotKeyResult check = Check(text, layouts, out string? typing);
        if (check != HotKeyResult.Ok)
        {
            Release();
            TypingLayout = typing;
            if (check == HotKeyResult.TypesALetter)
                Log.Write("lock shortcut " + text + " is off: it types a character on keyboard layout " + typing);
            return check;
        }
        Shortcut s = SteppedOut.Shortcut.Parse(text)!.Value;
        if (registered == s) return HotKeyResult.Ok;
        Release();
        window ??= new MessageWindow(this);
        int error = api.Register(window.Handle, Id, (uint)s.Modifiers | ModNoRepeat, s.VirtualKey);
        if (error == 0)
        {
            registered = s;
            return HotKeyResult.Ok;
        }
        if (error == ErrorHotKeyAlreadyRegistered)
        {
            Log.Write("lock shortcut " + s + " is taken by another app");
            return HotKeyResult.InUse;
        }
        Log.Write("lock shortcut " + s + " could not be registered: " + new Win32Exception(error).Message);
        return HotKeyResult.Invalid;
    }

    void Release()
    {
        if (registered is null || window is null) return;
        registered = null;
        try
        {
            api.Unregister(window.Handle, Id);
        }
        catch (Exception e)
        {
            Log.Write("lock shortcut: " + e.Message);
        }
    }

    void RaiseResultChanged()
    {
        try
        {
            ResultChanged?.Invoke();
        }
        catch (Exception e)
        {
            Log.Write("lock shortcut: " + e.Message);
        }
    }

    void OnHotKey()
    {
        if (registered is null) return;
        try
        {
            Pressed?.Invoke();
        }
        catch (Exception e)
        {
            Log.Write("lock shortcut: " + e.Message);
        }
    }

    internal IntPtr WindowHandle => window?.Handle ?? IntPtr.Zero;

    public void Dispose()
    {
        if (disposed) return;
        Release();
        disposed = true;
        try
        {
            window?.DestroyHandle();
        }
        catch (Exception e)
        {
            Log.Write("lock shortcut: " + e.Message);
        }
        window = null;
    }

    // A message-only window: never shown, never in the taskbar or Alt+Tab, it only gets WM_HOTKEY.
    sealed class MessageWindow : NativeWindow
    {
        static readonly IntPtr HwndMessage = new(-3);
        readonly HotKey owner;

        public MessageWindow(HotKey owner)
        {
            this.owner = owner;
            CreateHandle(new CreateParams { Parent = HwndMessage });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotKey && m.WParam == Id)
            {
                owner.OnHotKey();
                return;
            }
            base.WndProc(ref m);
        }
    }
}

sealed class HotKeyApi : IHotKeyApi
{
    public static HotKeyApi Instance { get; } = new();

    public int Register(IntPtr window, int id, uint modifiers, uint virtualKey) =>
        RegisterHotKey(window, id, modifiers, virtualKey) ? 0 : Marshal.GetLastPInvokeError();

    public void Unregister(IntPtr window, int id) => UnregisterHotKey(window, id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool UnregisterHotKey(IntPtr window, int id);
}

// Turns every registration down, as access denied, and so never holds a key.
sealed class RefusingHotKeyApi : IHotKeyApi
{
    const int ErrorAccessDenied = 5;

    public static RefusingHotKeyApi Instance { get; } = new();

    public int Register(IntPtr window, int id, uint modifiers, uint virtualKey) => ErrorAccessDenied;

    public void Unregister(IntPtr window, int id)
    {
    }
}

sealed class KeyboardLayouts : IKeyboardLayouts
{
    public static KeyboardLayouts Instance { get; } = new();

    const uint MapVkToVsc = 0;
    // ToUnicodeEx flag: leave the keyboard state alone, so a dead key waiting to be typed stays.
    const uint DontChangeKeyboardState = 0x4;
    const byte Down = 0x80;
    const int VkShift = 0x10;
    const int VkControl = 0x11;
    const int VkMenu = 0x12;
    const int VkLShift = 0xA0;
    const int VkLControl = 0xA2;
    const int VkRMenu = 0xA5;

    public IReadOnlyList<IntPtr> Installed()
    {
        var list = new List<IntPtr>();
        int count = GetKeyboardLayoutList(0, null);
        if (count > 0)
        {
            var layouts = new IntPtr[count];
            count = GetKeyboardLayoutList(count, layouts);
            for (int i = 0; i < count; i++) list.Add(layouts[i]);
        }
        IntPtr active = GetKeyboardLayout(0);
        if (active != IntPtr.Zero && !list.Contains(active)) list.Add(active);
        return list;
    }

    public string Typed(IntPtr layout, Shortcut shortcut) =>
        Typed(layout, shortcut.VirtualKey, shortcut.HasCtrl, shortcut.HasAlt, shortcut.HasShift);

    // What a key types on a layout with these modifiers held, leaving out control characters.
    // Ctrl and Alt together are what AltGr sends, so this sees AltGr characters too.
    // Alt without Ctrl never types in an app (the key goes to the menu instead), even though
    // ToUnicodeEx still gives the plain character for it, so that is nothing.
    internal static string Typed(IntPtr layout, uint virtualKey, bool ctrl, bool alt, bool shift)
    {
        if (alt && !ctrl) return "";
        var state = new byte[256];
        if (ctrl)
        {
            state[VkControl] = Down;
            state[VkLControl] = Down;
        }
        if (alt)
        {
            state[VkMenu] = Down;
            state[VkRMenu] = Down;
        }
        if (shift)
        {
            state[VkShift] = Down;
            state[VkLShift] = Down;
        }
        uint scanCode = MapVirtualKeyEx(virtualKey, MapVkToVsc, layout);
        var buffer = new char[8];
        int n = ToUnicodeEx(virtualKey, scanCode, state, buffer, buffer.Length, DontChangeKeyboardState, layout);
        // Below zero is a dead key: it types its accent with the next key, so it types too.
        if (n < 0) n = 1;
        n = Math.Min(n, buffer.Length);
        var typed = new System.Text.StringBuilder(n);
        for (int i = 0; i < n; i++)
        {
            if (!char.IsControl(buffer[i])) typed.Append(buffer[i]);
        }
        return typed.ToString();
    }

    [DllImport("user32.dll")]
    static extern int GetKeyboardLayoutList(int count, [Out] IntPtr[]? layouts);

    [DllImport("user32.dll")]
    static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyExW")]
    static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr layout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyState,
        [Out] char[] buffer, int bufferSize, uint flags, IntPtr layout);
}
