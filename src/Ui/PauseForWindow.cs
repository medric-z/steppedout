using System.Globalization;
using System.Windows.Forms.Automation;

namespace SteppedOut;

// What a pause length is counted in, in the Pause for window.
enum PauseUnit
{
    Minutes,
    Hours,
    Days,
}

// "Pause for": the small window Custom… in the pause list opens, from the tray menu or under Pause
// in the settings window. A number and its unit (minutes, hours or days), and Pause and Cancel. It
// takes a minute to a week; a number outside that is brought to the nearest end, and the line under
// the box says so. It only ever opens because Custom… was clicked, and one is open at a time: a
// second Custom… brings the open one to the front.
sealed class PauseForWindow : Form
{
    // The longest and shortest pause, in minutes.
    public const int Shortest = 1;
    public const int Longest = TimeChoices.LongestPause;

    static PauseForWindow? open;

    readonly Strings text;
    readonly Action<TimeSpan> pause;
    readonly NumberBox number;
    readonly DropDown unit;
    readonly Label line;
    readonly KitButton go;
    readonly KitButton cancel;
    string said = "";

    public PauseForWindow(Strings text, Action<TimeSpan> pause)
    {
        this.text = text;
        this.pause = pause;
        var v = text.V1;
        var t = Theme.Current;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        RightToLeft = text.RightToLeft ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = text.RightToLeft;
        Font = Theme.Body(9.75f);
        Text = v.PauseForTitle;
        AccessibleName = v.PauseForTitle;
        t.Dress(this);

        number = new NumberBox(v.PauseForTitle) { TabIndex = 0, Value = "3" };
        unit = new DropDown { ButtonWidth = 110, AccessibleName = v.PauseForUnit, TabIndex = 1, Name = "unit" }
            .Add(v.UnitMinutes, PauseUnit.Minutes)
            .Add(v.UnitHours, PauseUnit.Hours)
            .Add(v.UnitDays, PauseUnit.Days);
        unit.Value = PauseUnit.Hours;
        line = new Label
        {
            AutoSize = false,
            UseMnemonic = false,
            ForeColor = t.Subtle,
            BackColor = Color.Transparent,
            Font = Theme.Body(9f),
            AccessibleRole = AccessibleRole.StaticText,
        };
        go = new KitButton(v.Pause, primary: true) { TabIndex = 2, Name = "pause" };
        cancel = new KitButton(v.Cancel) { TabIndex = 3, Name = "cancel" };
        Controls.AddRange([number, unit, line, go, cancel]);
        AcceptButton = go;
        CancelButton = cancel;
        ActiveControl = number.Input;

        number.Changed += (_, _) => Check();
        unit.SelectedChanged += (_, _) => Check();
        go.Click += (_, _) => Pause();
        cancel.Click += (_, _) => Close();
        Check();
    }

    // Opens the window, or brings the one already open to the front. owner is the settings window
    // when Custom… was under its Pause button, or null from the tray menu, when the window opens in
    // the middle of the screen.
    public static PauseForWindow Open(IWin32Window? owner, Strings text, Action<TimeSpan> pause)
    {
        if (open is { IsDisposed: false } shown)
        {
            shown.Activate();
            return shown;
        }
        var window = new PauseForWindow(text, pause);
        if (owner is not null) window.StartPosition = FormStartPosition.CenterParent;
        window.FormClosed += (_, _) =>
        {
            if (open == window) open = null;
            window.BeginInvoke(window.Dispose);
        };
        open = window;
        if (owner is null) window.Show();
        else window.Show(owner);
        window.Activate();
        window.number.StartTyping();
        return window;
    }

    // Closes the open one, if any: the app quitting.
    public static void CloseOpen() => open?.Close();

    // For tests and the render tool.
    internal NumberBox Number => number;
    internal DropDown Unit => unit;
    internal KitButton PauseButton => go;
    internal KitButton Dismiss => cancel;
    internal string Line => said;

    // The length a number in a unit comes to, in minutes, brought into the range; null when the box
    // holds no number. Only digits count, in whichever script they were typed.
    internal static (int Minutes, bool Clamped)? Length(string typed, PauseUnit unit)
    {
        if (ReadNumber(typed) is not long n) return null;
        long minutes = n * unit switch { PauseUnit.Days => 24 * 60, PauseUnit.Hours => 60, _ => 1 };
        int used = (int)Math.Clamp(minutes, Shortest, Longest);
        return (used, used != minutes);
    }

    // Only digits make a number, in whichever script they were typed (an Arabic keyboard can give
    // ٣ for 3); anything else, or nothing, is none. Ten digits are far past a week already, so a
    // long paste stops growing there.
    internal static long? ReadNumber(string typed)
    {
        string trimmed = typed.Trim();
        if (trimmed.Length == 0) return null;
        long n = 0;
        foreach (char c in trimmed)
        {
            if (!char.IsDigit(c)) return null;
            if (n < 10_000_000_000) n = n * 10 + (long)char.GetNumericValue(c);
        }
        return n;
    }

    // The line under the box: the range, how a number out of it is used, or that a number is needed.
    internal static string LineFor(Strings text, (int Minutes, bool Clamped)? length) => length switch
    {
        null => text.V1.PauseForNotANumber,
        { Clamped: true } l => string.Format(text.V1.PauseForClamped, text.PauseLength(l.Minutes)),
        _ => text.V1.PauseForRange,
    };

    (int Minutes, bool Clamped)? Current => Length(number.Value, unit.Value is PauseUnit u ? u : PauseUnit.Minutes);

    void Check()
    {
        var length = Current;
        Say(LineFor(text, length));
        go.Enabled = length is not null;
        go.Primary = length is not null;
        go.Invalidate();
    }

    // Pause: for the length the box comes to, brought into the range.
    internal void Pause()
    {
        if (Current is not { } length) return;
        pause(TimeSpan.FromMinutes(length.Minutes));
        Close();
    }

    void Say(string words)
    {
        if (said == words) return;
        said = words;
        line.Text = words;
        number.Input.AccessibleDescription = words;
        Arrange();
        // Read out while someone types, as the Custom… boxes do.
        if (number.Input.Focused && number.Input.IsHandleCreated)
        {
            number.Input.AccessibilityObject.RaiseAutomationNotification(
                AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, words);
        }
    }

    int Px(int logical) => LogicalToDeviceUnits(logical);

    // The number and its unit side by side, the line under them, and Pause and Cancel at the end,
    // 20 px in from the edges.
    void Arrange()
    {
        if (number is null || unit is null || line is null || go is null || cancel is null) return;
        int pad = Px(20);
        var boxSize = number.GetPreferredSize(Size.Empty);
        var unitSize = unit.GetPreferredSize(Size.Empty);
        var goSize = go.GetPreferredSize(Size.Empty);
        var cancelSize = cancel.GetPreferredSize(Size.Empty);
        int buttons = Math.Max(Px(80), Math.Max(goSize.Width, cancelSize.Width));
        int height = Math.Max(goSize.Height, cancelSize.Height);
        int inner = Math.Max(Px(300), Math.Max(boxSize.Width + Px(8) + unitSize.Width, buttons * 2 + Px(8)));
        int width = inner + pad * 2;
        int row = Math.Max(boxSize.Height, unitSize.Height);
        int y = pad;
        number.Bounds = new Rectangle(pad, y + (row - boxSize.Height) / 2, boxSize.Width, boxSize.Height);
        unit.Bounds = new Rectangle(number.Right + Px(8), y + (row - unitSize.Height) / 2, unitSize.Width, unitSize.Height);
        y += row + Px(8);
        line.Bounds = new Rectangle(pad, y, inner, Shapes.WrappedHeight(line.Text, line.Font, inner));
        y = line.Bottom + Px(20);
        cancel.Bounds = new Rectangle(width - pad - buttons, y, buttons, height);
        go.Bounds = new Rectangle(cancel.Left - Px(8) - buttons, y, buttons, height);
        ClientSize = new Size(width, go.Bottom + pad);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Arrange();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (open == this) open = null;
        base.OnFormClosed(e);
    }

    // The number box: a kit field round a Windows text box, so the caret, selection, paste and
    // screen readers work as in any box. It takes digits only, in any script; Up and Down step by one.
    internal sealed class NumberBox : KitControl
    {
        readonly TextBox box = new();

        public NumberBox(string name)
        {
            var t = Theme.Current;
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Cursor = Cursors.IBeam;
            AccessibleName = "";
            box.AccessibleName = name;
            box.BorderStyle = BorderStyle.None;
            box.TextAlign = HorizontalAlignment.Center;
            box.MaxLength = 5;
            box.Font = Theme.Body(10f);
            box.BackColor = t.Field;
            box.ForeColor = t.Text;
            box.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            box.KeyPress += (_, e) =>
            {
                if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true;
            };
            box.KeyDown += OnBoxKeyDown;
            box.GotFocus += (_, _) => Invalidate();
            box.LostFocus += (_, _) => Invalidate();
            Controls.Add(box);
        }

        public event EventHandler? Changed;

        public TextBox Input => box;

        public string Value
        {
            get => box.Text;
            set => box.Text = value;
        }

        public void StartTyping()
        {
            box.Focus();
            box.SelectAll();
        }

        public override Size GetPreferredSize(Size proposedSize) => new(Px(80), Math.Max(Px(34), box.PreferredHeight + Px(8)));

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int inset = Px(8);
            int height = box.PreferredHeight;
            box.Bounds = new Rectangle(inset, (Height - height) / 2, Width - inset * 2, height);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            box.Focus();
        }

        void OnBoxKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode is not (Keys.Up or Keys.Down) || e.Modifiers != Keys.None) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            long now = ReadNumber(box.Text) ?? 0;
            box.Text = Math.Clamp(now + (e.KeyCode == Keys.Up ? 1 : -1), 1, 99999).ToString(CultureInfo.InvariantCulture);
            box.SelectionStart = box.Text.Length;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            Smooth(g);
            bool focused = box.Focused;
            float border = focused ? Px(2) : 1;
            using var path = Shapes.Rounded(Shapes.Stroke(new Rectangle(0, 0, Width, Height), border), Px(6));
            using var brush = new SolidBrush(t.Field);
            g.FillPath(brush, path);
            using var pen = new Pen(focused ? t.Accent : t.FieldLine, border);
            g.DrawPath(pen, path);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new FrameAccessible(this);

        // Only the frame around the box: not a stop of its own.
        sealed class FrameAccessible(NumberBox owner) : ControlAccessibleObject(owner)
        {
            public override AccessibleStates State => base.State & ~AccessibleStates.Focusable;
        }
    }
}
