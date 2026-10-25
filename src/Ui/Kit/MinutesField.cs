using System.Globalization;
using System.Windows.Forms.Automation;

namespace SteppedOut;

// A small box to type a number of minutes into, with the word after it: "[ 45 ] minutes". It takes
// whole minutes from 1 to 1440. While it holds anything else, the last good time stays in use and
// Hint says which; leaving the box writes that time back in. Up and Down step a minute at a time.
// The typing itself is a borderless TextBox, so the caret, selection, paste and screen readers all
// work as in any Windows box; this draws the rounded field around it in the theme's colors.
// The box behind a Custom… row in a list is made with a unit and a range of its own instead: whole
// minutes, seconds, pixels, percent or KB from its shortest to its longest, with its own lines
// beside it.
sealed class MinutesField : KitControl
{
    readonly TypingBox box = new();
    double minutes = 15;
    string hint = "";
    bool filling;
    readonly bool ranged;
    readonly TimeUnit unit = TimeUnit.Minutes;
    readonly int shortest = TimeChoices.Shortest;
    readonly int longest = TimeChoices.Longest;

    // The time in use changed because of something typed.
    public event EventHandler? MinutesChanged;

    public event EventHandler? HintChanged;

    public MinutesField(string name)
    {
        var t = Theme.Current;
        // Tab goes straight to the box inside.
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Cursor = Cursors.IBeam;
        // The box is the one with a name. Left unnamed, the field around it would be named after a
        // nearby label, and a row would name it after its title.
        AccessibleName = "";
        box.AccessibleName = name;
        Automation.HelpFromDescription(box);
        box.BorderStyle = BorderStyle.None;
        box.TextAlign = HorizontalAlignment.Center;
        box.MaxLength = 4;
        box.Font = Theme.Body(10f);
        box.BackColor = t.Field;
        box.ForeColor = t.Text;
        box.TextChanged += (_, _) => Typed();
        box.KeyPress += OnBoxKeyPress;
        box.KeyDown += OnBoxKeyDown;
        box.Leave += (_, _) => Commit();
        box.GotFocus += (_, _) => Invalidate();
        box.LostFocus += (_, _) => Invalidate();
        Controls.Add(box);
        Fill(minutes);
    }

    // A box for a Custom… row: whole numbers of its unit from shortest to longest. name is what the
    // box is for, with the unit, as a screen reader says it: "Warn me before it locks, in seconds".
    public MinutesField(string name, TimeUnit unit, int shortest, int longest) : this(name)
    {
        ranged = true;
        // A Custom… box in a list's menu: Enter is the menu's, which uses the number.
        box.KeepsEnter = false;
        this.unit = unit;
        this.shortest = Math.Max(0, shortest);
        this.longest = Math.Max(this.shortest, longest);
        box.MaxLength = Math.Max(2, this.longest.ToString(CultureInfo.InvariantCulture).Length);
        Fill(Math.Clamp(15, this.shortest, this.longest));
    }

    public TimeUnit Unit => unit;

    // The time in use. Setting it shows it in the box without raising MinutesChanged.
    public double Minutes
    {
        get => minutes;
        set => Fill(value);
    }

    // What the time comes to, or why it is not what was typed.
    public string Hint => hint;

    // Puts the caret in the box with its number selected, so typing replaces it.
    public void StartTyping()
    {
        box.Focus();
        box.SelectAll();
    }

    int FieldWidth => Px(64);

    int Gap => Px(8);

    // As wide as the widest form of the word, so the hint beside it stays put while typing.
    int WordWidth => new[] { 1, 2, 3, 11 }.Max(n => TextRenderer.MeasureText(Word(n), Body(10f)).Width);

    // The word after the box, agreeing with the number in it.
    string Word(int n)
    {
        if (unit == TimeUnit.Minutes) return Lang.Current.MinutesWord(n);
        // "45 seconds", "4%" or "64 KB" without the number. Arabic puts no number before one or two.
        string counted = unit == TimeUnit.Seconds ? Lang.Current.SecondsCount(n) : Lang.Current.UnitCount(unit, n);
        string number = n.ToString(CultureInfo.InvariantCulture);
        return counted.StartsWith(number, StringComparison.Ordinal) ? counted[number.Length..].TrimStart() : counted;
    }

    // 34 px tall, or taller round a box whose text has grown with the text size (I21).
    public override Size GetPreferredSize(Size proposedSize) =>
        new(FieldWidth + Gap + WordWidth, Math.Max(Px(34), box.PreferredHeight + Px(8)));

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int inset = Px(8);
        int height = box.PreferredHeight;
        box.Bounds = Box(inset, (Height - height) / 2, FieldWidth - inset * 2, height);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        box.Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        Smooth(g);
        // A text box shows that it has focus whichever way it got it, as Windows' own do.
        bool focused = box.Focused;
        float border = focused ? Px(2) : 1;
        using (var path = Shapes.Rounded(Shapes.Stroke(Box(0, 0, FieldWidth, Height), border), Px(6)))
        {
            using var brush = new SolidBrush(t.Field);
            g.FillPath(brush, path);
            using var pen = new Pen(focused ? t.Accent : t.FieldLine, border);
            g.DrawPath(pen, path);
        }
        DrawText(g, Word(WordCount()), Body(10f), Box(FieldWidth + Gap, 0, WordWidth, Height), t.Text,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    // The number the word after the box agrees with: the one typed, else the time in use. A fraction
    // from the file takes the form for many, which suits "0.5 minutes" and its Arabic alike.
    int WordCount()
    {
        if (ranged) return Read(box.Text) is { } number ? (int)Math.Min(number, int.MaxValue) : (int)minutes;
        var typed = TimeChoices.Read(box.Text);
        if (typed.IsNumber) return (int)Math.Min(typed.Number, int.MaxValue);
        return minutes == Math.Floor(minutes) ? (int)minutes : 0;
    }

    void Fill(double value)
    {
        if (ranged)
        {
            FillRanged(value);
            return;
        }
        minutes = value;
        string shown = TimeChoices.Show(value);
        if (box.Text != shown)
        {
            filling = true;
            box.Text = shown;
            filling = false;
        }
        SetHint(TimeChoices.Describe(Lang.Current, value));
        Invalidate();
    }

    void Typed()
    {
        if (filling) return;
        if (ranged)
        {
            TypedRanged();
            return;
        }
        var typed = TimeChoices.Read(box.Text);
        SetHint(TimeChoices.Hint(Lang.Current, typed, minutes));
        Invalidate();
        if (typed.IsNumber && typed.Minutes != minutes)
        {
            minutes = typed.Minutes;
            MinutesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Leaving the box puts the time in use back in it: the nearest one in range, or the last good one
    // when what was typed is not a number.
    void Commit()
    {
        if (ranged)
        {
            if (box.Text != Show(minutes)) Fill(minutes);
            return;
        }
        if (box.Text != TimeChoices.Show(minutes)) Fill(minutes);
    }

    // The ranged box: what is shown, read and said beside it.
    static string Show(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    void FillRanged(double value)
    {
        minutes = value;
        string shown = Show(value);
        if (box.Text != shown)
        {
            filling = true;
            box.Text = shown;
            filling = false;
        }
        SetHint(Describe((int)Math.Min(value, int.MaxValue)));
        Invalidate();
    }

    void TypedRanged()
    {
        var number = Read(box.Text);
        int used = number is null ? (int)minutes : (int)Math.Clamp(number.Value, shortest, longest);
        SetHint(number is null ? NotANumber((int)minutes) : number != used ? Clamped(used) : Describe(used));
        Invalidate();
        if (number is not null && used != minutes)
        {
            minutes = used;
            MinutesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Only digits make a number, in whichever script they were typed; anything else, or nothing,
    // is none. Ten digits are far past any range, so a long paste stops there.
    static long? Read(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0) return null;
        long number = 0;
        foreach (char c in trimmed)
        {
            if (!char.IsDigit(c)) return null;
            if (number < 10_000_000_000) number = number * 10 + (long)char.GetNumericValue(c);
        }
        return number;
    }

    // An hour or more of minutes in words; otherwise the range, so it is known before typing.
    string Describe(int value)
    {
        var text = Lang.Current;
        return unit switch
        {
            TimeUnit.Minutes when value >= 60 => string.Format(text.MinutesThatIs, text.OptionDuration(value)),
            TimeUnit.Minutes => string.Format(text.V1.CustomRangeMinutes, shortest, longest),
            TimeUnit.Seconds => string.Format(text.V1.CustomRangeSeconds, shortest, longest),
            _ => string.Format(text.V1.CustomRangeOf, text.UnitCount(unit, shortest), text.UnitCount(unit, longest)),
        };
    }

    string Clamped(int used)
    {
        var text = Lang.Current;
        return unit switch
        {
            TimeUnit.Minutes => string.Format(text.V1.CustomClampedMinutes, shortest, longest, text.OptionDuration(used)),
            TimeUnit.Seconds => string.Format(text.V1.CustomClampedSeconds, shortest, longest, text.SecondsOption(used)),
            _ => string.Format(text.V1.CustomClampedOf, text.UnitCount(unit, shortest), text.UnitCount(unit, longest), text.UnitCount(unit, used)),
        };
    }

    string NotANumber(int used)
    {
        var text = Lang.Current;
        return unit switch
        {
            TimeUnit.Minutes => string.Format(text.MinutesNotANumber, text.OptionDuration(used)),
            TimeUnit.Seconds => string.Format(text.V1.CustomNotANumberSeconds, text.SecondsOption(used)),
            _ => string.Format(text.V1.CustomNotANumberOf, text.UnitCount(unit, used)),
        };
    }

    void SetHint(string value)
    {
        if (value == hint) return;
        hint = value;
        box.AccessibleDescription = value;
        Automation.Notify(box, AccessibleEvents.DescriptionChange);
        // UI Automation has no change of description to tell Narrator, so while someone types the
        // new hint is said out loud instead.
        if (box.Focused)
        {
            box.AccessibilityObject.RaiseAutomationNotification(
                AutomationNotificationKind.Other,
                AutomationNotificationProcessing.MostRecent,
                value);
        }
        HintChanged?.Invoke(this, EventArgs.Empty);
    }

    void OnBoxKeyPress(object? sender, KeyPressEventArgs e)
    {
        // Enter settles the time, and is never the window's default button (TypingBox).
        if (e.KeyChar == '\r')
        {
            Commit();
            box.SelectAll();
            e.Handled = true;
        }
        // Letters and signs are refused as they are typed. A paste with them in is caught by Read.
        else if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar))
        {
            e.Handled = true;
        }
    }

    void OnBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is not (Keys.Up or Keys.Down) || e.Modifiers != Keys.None) return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        if (ranged)
        {
            long from = Read(box.Text) ?? (long)minutes;
            box.Text = Show(Math.Clamp(from + (e.KeyCode == Keys.Up ? 1 : -1), shortest, longest));
            box.SelectionStart = box.Text.Length;
            return;
        }
        if (TimeChoices.Step(TimeChoices.Read(box.Text), minutes, up: e.KeyCode == Keys.Up) is not int to) return;
        // As if typed, so the time and the hint follow.
        box.Text = TimeChoices.Show(to);
        box.SelectionStart = box.Text.Length;
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new FieldAccessible(this);

    // The box typed in. It keeps Enter for itself, so Enter settles the time rather than pressing
    // the window's default button: in the welcome that is Done, which would end the window with the
    // questions below it unanswered (I2).
    sealed class TypingBox : TextBox
    {
        public bool KeepsEnter { get; set; } = true;

        protected override bool IsInputKey(Keys keyData) =>
            KeepsEnter && (keyData & ~Keys.Shift) == Keys.Enter || base.IsInputKey(keyData);
    }

    // Only the frame around the box: not a stop of its own, and with nothing of its own to say.
    sealed class FieldAccessible(MinutesField owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleStates State => base.State & ~AccessibleStates.Focusable;

        public override string? Help => null;
    }
}

// What a Custom… box counts in: a time, or for the Extras tab pixels, a percent of full volume or
// a size in KB.
enum TimeUnit
{
    Minutes,
    Seconds,
    Pixels,
    Percent,
    Kilobytes,
}
