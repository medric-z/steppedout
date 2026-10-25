using System.Runtime.InteropServices;

namespace SteppedOut;

// "Choose another shortcut…": press the keys, and the box shows them, always left to right. Save
// stays off until they make a shortcut that can work on every keyboard here; a letter on its own,
// Alt+F4 and anything with the Windows key never do. While the window is open the app holds no
// shortcut, so the one in use types into the box instead of locking the PC (ISettingsHost.
// ChoosingShortcut). Save registers the new one through the app, and when another app already has
// it, says so, lets go of it and stays open. However the window closes without a new one, the
// shortcut in use is put back.
sealed class ShortcutDialog : Form
{
    [DllImport("user32.dll")] static extern short GetKeyState(int virtualKey);

    const int LeftWin = 0x5B;
    const int RightWin = 0x5C;

    readonly ISettingsHost host;
    readonly string old;
    readonly Strings text;
    readonly Func<string, HotKeyResult> check;
    readonly Func<bool> winHeld;
    readonly Label prompt;
    readonly KeyBox box;
    readonly Label message;
    readonly KitButton save;
    readonly KitButton cancel;
    string? candidate;

    // The line under the box, "" when there is none.
    string said = "";

    // The app does not hold the shortcut in use: let go of while the window is open, or swapped for
    // one from Save that has not worked out. It goes back to the old one as the window closes,
    // whichever way it closes.
    bool tried;

    // The app was told the window is open, and has not yet been told it closed.
    bool open;

    public ShortcutDialog(ISettingsHost host, string current, Strings text, Func<string, HotKeyResult>? check = null, Func<bool>? winHeld = null)
    {
        this.host = host;
        old = Shortcut.Normalize(current);
        this.text = text;
        this.check = check ?? HotKey.Check;
        this.winHeld = winHeld ?? WindowsKeyDown;
        var v = text.V1;
        var t = Theme.Current;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        RightToLeft = text.RightToLeft ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = text.RightToLeft;
        Font = Theme.Body(9.75f);
        Text = v.ShortcutTitle;
        t.Dress(this);

        prompt = new Label { Text = v.ShortcutPrompt, AutoSize = false, UseMnemonic = false, ForeColor = t.Text, BackColor = Color.Transparent };
        box = new KeyBox { AccessibleName = v.ShortcutTitle, AccessibleDescription = v.ShortcutPrompt, TabIndex = 0 };
        message = new Label
        {
            AutoSize = false,
            UseMnemonic = false,
            ForeColor = t.Warning,
            BackColor = Color.Transparent,
            Font = Theme.Body(9f),
            Visible = false,
            AccessibleRole = AccessibleRole.StaticText,
        };
        save = new KitButton(v.Save) { Enabled = false, TabIndex = 1, Name = "save" };
        cancel = new KitButton(v.Cancel) { TabIndex = 2, Name = "cancel", DialogResult = DialogResult.Cancel };
        Controls.AddRange([prompt, box, message, save, cancel]);
        AcceptButton = save;
        CancelButton = cancel;
        ActiveControl = box;

        box.Taken += Take;
        save.Click += (_, _) => Save();
        Arrange();
    }

    // The shortcut saved, in its usual form, once the window closes with OK.
    public string? Chosen { get; private set; }

    // For tests and the render tool.
    internal KeyBox Box => box;

    internal KitButton SaveButton => save;

    internal KitButton Dismiss => cancel;

    internal string Message => said;

    static bool WindowsKeyDown() => GetKeyState(LeftWin) < 0 || GetKeyState(RightWin) < 0;

    // A key went down in the box, with the keys held with it.
    internal void Take(Keys keyData)
    {
        var v = text.V1;
        Keys key = keyData & Keys.KeyCode;
        bool win = winHeld();
        if (KeyBox.IsModifier(key))
        {
            // Only Ctrl, Alt, Shift or Windows so far: show them and wait for the key.
            box.Shown = Held(keyData, win) + "+";
            return;
        }
        if (!Shortcut.TryFromKeys(keyData, out Shortcut s, win))
        {
            candidate = null;
            box.Shown = Held(keyData, win) is { Length: > 0 } held ? held + "+" + KeyName(key) : KeyName(key);
            Say(v.ShortcutInvalid);
            Ready(false);
            return;
        }
        candidate = s.ToString();
        box.Shown = candidate;
        HotKeyResult result;
        try
        {
            result = check(candidate);
        }
        catch (Exception e)
        {
            Log.Write("lock shortcut: could not check " + candidate + ": " + e.Message);
            result = HotKeyResult.Invalid;
        }
        if (result == HotKeyResult.Ok)
        {
            Say(null);
            Ready(true);
            return;
        }
        candidate = null;
        Say(result == HotKeyResult.TypesALetter ? v.ShortcutTypesALetter : v.ShortcutInvalid);
        Ready(false);
    }

    // Save: the app registers the shortcut. When it cannot, the window says why, has the app let go
    // of it again, and stays open for another; the shortcut it had comes back as the window closes.
    internal void Save()
    {
        if (candidate is not { } wanted) return;
        if (wanted == old)
        {
            Chosen = old;
            DialogResult = DialogResult.OK;
            return;
        }
        tried = true;
        HotKeyResult result;
        try
        {
            host.ShortcutChanged(wanted);
            result = host.ShortcutResult;
        }
        catch (Exception e)
        {
            Log.Write("lock shortcut: could not register " + wanted + ": " + e.Message);
            result = HotKeyResult.Invalid;
        }
        if (result == HotKeyResult.Ok)
        {
            tried = false;
            Chosen = wanted;
            host.ShortcutKept(wanted);
            DialogResult = DialogResult.OK;
            return;
        }
        LetGo();
        var v = text.V1;
        Say(result switch
        {
            HotKeyResult.InUse => v.ShortcutInUse,
            HotKeyResult.TypesALetter => v.ShortcutTypesALetter,
            _ => v.ShortcutInvalid,
        });
        candidate = null;
        Ready(false);
        box.Focus();
    }

    // Save is the window's main button only while it can save: off, it looks like any button that is off.
    void Ready(bool on)
    {
        save.Enabled = on;
        save.Primary = on;
        save.Invalidate();
    }

    // As the window shows, before a key can reach it: the app lets go of the shortcut in use.
    protected override void OnLoad(EventArgs e)
    {
        Opened();
        base.OnLoad(e);
    }

    void Opened()
    {
        if (open) return;
        open = true;
        tried = true;
        try
        {
            host.ChoosingShortcut(true);
        }
        catch (Exception e)
        {
            Log.Write("lock shortcut: could not let go of " + old + ": " + e.Message);
        }
    }

    // A shortcut from Save that another app has, or that failed: the app holds none again.
    void LetGo()
    {
        try
        {
            host.ShortcutChanged("");
        }
        catch (Exception e)
        {
            Log.Write("lock shortcut: could not let go of a shortcut tried: " + e.Message);
        }
    }

    // The old shortcut back with the app, then the app told the window is gone.
    void PutBack()
    {
        if (tried)
        {
            tried = false;
            try
            {
                host.ShortcutChanged(old);
            }
            catch (Exception e)
            {
                Log.Write("lock shortcut: could not put back " + old + ": " + e.Message);
            }
        }
        if (!open) return;
        open = false;
        try
        {
            host.ChoosingShortcut(false);
        }
        catch (Exception e)
        {
            Log.Write("lock shortcut: " + e.Message);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        PutBack();
        base.OnFormClosed(e);
    }

    // Disposed without closing (the app quitting): the same.
    protected override void Dispose(bool disposing)
    {
        if (disposing) PutBack();
        base.Dispose(disposing);
    }

    void Say(string? words)
    {
        bool show = words is not null;
        if (said == (words ?? "")) return;
        said = words ?? "";
        message.Text = said;
        message.Visible = show;
        Arrange();
        if (show && message.IsHandleCreated)
        {
            message.AccessibilityObject.RaiseAutomationNotification(
                System.Windows.Forms.Automation.AutomationNotificationKind.Other,
                System.Windows.Forms.Automation.AutomationNotificationProcessing.ImportantMostRecent,
                words!);
        }
    }

    // The keys held, in the order Windows writes them.
    static string Held(Keys keyData, bool win)
    {
        var parts = new List<string>(4);
        if (win) parts.Add("Win");
        if ((keyData & Keys.Control) != 0) parts.Add("Ctrl");
        if ((keyData & Keys.Alt) != 0) parts.Add("Alt");
        if ((keyData & Keys.Shift) != 0) parts.Add("Shift");
        return string.Join("+", parts);
    }

    static string KeyName(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        >= Keys.NumPad0 and <= Keys.NumPad9 => "Num " + (char)('0' + (key - Keys.NumPad0)),
        _ => key.ToString(),
    };

    int Px(int logical) => LogicalToDeviceUnits(logical);

    // Everything in one column, 20 px in from the edges: the words, the box, the line that says
    // why a shortcut does not work, and Save and Cancel at the end.
    void Arrange()
    {
        int width = Px(380);
        int pad = Px(20);
        int inner = width - pad * 2;
        int y = pad;
        prompt.Bounds = new Rectangle(pad, y, inner, Shapes.WrappedHeight(prompt.Text, prompt.Font, inner));
        y = prompt.Bottom + Px(10);
        box.Bounds = new Rectangle(pad, y, inner, box.GetPreferredSize(Size.Empty).Height);
        y = box.Bottom;
        if (said.Length > 0)
        {
            message.Bounds = new Rectangle(pad, y + Px(8), inner, Shapes.WrappedHeight(message.Text, message.Font, inner));
            y = message.Bottom;
        }
        y += Px(20);
        var saveSize = save.GetPreferredSize(Size.Empty);
        var cancelSize = cancel.GetPreferredSize(Size.Empty);
        int buttons = Math.Max(Px(80), Math.Max(saveSize.Width, cancelSize.Width));
        int height = Math.Max(saveSize.Height, cancelSize.Height);
        cancel.Bounds = new Rectangle(width - pad - buttons, y, buttons, height);
        save.Bounds = new Rectangle(cancel.Left - Px(8) - buttons, y, buttons, height);
        ClientSize = new Size(width, save.Bottom + pad);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Arrange();
    }

    // The box the keys are pressed in. It takes every key that holds Ctrl or Alt, and F10, before the
    // window can (so Alt+F4 does not close it and Alt or F10 do not open the window menu), and every
    // plain key; Tab, Shift+Tab, Enter and Esc still move on, save and cancel. What it shows always
    // reads left to right.
    internal sealed class KeyBox : KitControl
    {
        string shown = "";

        public KeyBox()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.Text;
            RightToLeft = RightToLeft.No;
        }

        // A key went down, with the keys held: KeyEventArgs.KeyData.
        public event Action<Keys>? Taken;

        public string Shown
        {
            get => shown;
            set
            {
                if (shown == value) return;
                shown = value;
                Invalidate();
                Automation.Notify(this, AccessibleEvents.ValueChange);
            }
        }

        internal static bool IsModifier(Keys key) => key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or Keys.LWin or Keys.RWin;

        // The keys the box takes before the window: anything with Ctrl or Alt held, F10, and Ctrl,
        // Alt, Shift or Windows on their own. Not Tab or Shift+Tab, Enter or Esc on their own.
        internal static bool Captures(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            Keys held = keyData & Keys.Modifiers;
            if (key is Keys.Tab or Keys.Enter or Keys.Escape && (held & (Keys.Control | Keys.Alt)) == 0) return false;
            return (held & (Keys.Control | Keys.Alt)) != 0 || key == Keys.F10 || IsModifier(key);
        }

        protected override bool IsInputKey(Keys keyData) => Captures(keyData) || base.IsInputKey(keyData);

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Captures(keyData))
            {
                Taken?.Invoke(keyData);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!(e.KeyCode is Keys.Tab or Keys.Enter or Keys.Escape && !e.Control && !e.Alt))
            {
                Taken?.Invoke(e.KeyData);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        // Letting go of Alt or F10 on its own would open the window menu.
        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.F10 || e.Alt) e.Handled = true;
            base.OnKeyUp(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            e.Handled = true;
            base.OnKeyPress(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnClick(EventArgs e)
        {
            Focus();
            base.OnClick(e);
        }

        Font Face => Strong(12f);

        public override Size GetPreferredSize(Size proposedSize) =>
            new(proposedSize.Width > 0 ? proposedSize.Width : Width, Math.Max(Px(44), Shapes.Measure("Ag", Face).Height + Px(16)));

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            Smooth(g);
            using (var path = Shapes.Rounded(Shapes.Stroke(ClientRectangle, Focused ? Px(2) : 1), Px(6)))
            {
                using var brush = new SolidBrush(t.Field);
                g.FillPath(brush, path);
                using var pen = new Pen(Focused ? t.Accent : t.FieldLine, Focused ? Px(2) : 1);
                g.DrawPath(pen, path);
            }
            // Centered, so it reads the same in a window laid out either way.
            TextDrawn?.Invoke(this, shown, Face, ClientRectangle, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, shown, Face, ClientRectangle, t.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new KeyBoxAccessible(this);

        // Read only: the keys pressed in it set what it shows, and a screen reader cannot type them.
        sealed class KeyBoxAccessible(KeyBox owner) : ControlAccessibleObject(owner)
        {
            public override string? Value => owner.shown;

            public override AccessibleStates State => base.State | AccessibleStates.ReadOnly;
        }
    }
}
