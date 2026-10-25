namespace SteppedOut;

// A question asked before something that changes every setting or can't be undone: its title, what
// it means, and the button that goes ahead, beside Cancel.
sealed record Question(string Title, string Body, string GoAhead);

// The window that asks it, in the app's look, light or dark, right to left in Arabic. The keyboard
// starts on Cancel, and Escape and the window's X are Cancel too: only the button that goes ahead
// says yes.
sealed class QuestionWindow : Form
{
    readonly Label title;
    readonly Label body;
    readonly KitButton go;
    readonly KitButton cancel;

    public QuestionWindow(Question question, Strings text)
    {
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
        Text = AppInfo.Name;
        AccessibleName = question.Title;
        t.Dress(this);

        title = new Label
        {
            Text = question.Title,
            AutoSize = false,
            UseMnemonic = false,
            ForeColor = t.Text,
            BackColor = Color.Transparent,
            Font = Theme.Strong(11.25f),
            AccessibleRole = AccessibleRole.StaticText,
        };
        body = new Label
        {
            Text = question.Body,
            AutoSize = false,
            UseMnemonic = false,
            ForeColor = t.Subtle,
            BackColor = Color.Transparent,
            Font = Theme.Body(9.75f),
            AccessibleRole = AccessibleRole.StaticText,
        };
        // As Windows' own buttons do: the answer closes the window it was asked in.
        go = new KitButton(question.GoAhead, primary: true) { TabIndex = 1, Name = "goAhead", AccessibleDescription = question.Body, DialogResult = DialogResult.OK };
        cancel = new KitButton(text.V1.Cancel) { TabIndex = 2, Name = "cancel", DialogResult = DialogResult.Cancel };
        Controls.AddRange([title, body, go, cancel]);
        CancelButton = cancel;
        ActiveControl = cancel;
        Arrange();
    }

    // For tests and the render tool.
    internal Label Title => title;
    internal Label Body => body;
    internal KitButton GoAhead => go;
    internal KitButton Dismiss => cancel;

    int Px(int logical) => LogicalToDeviceUnits(logical);

    // The title and what it means, one under the other, and the two buttons at the end of the line
    // under them, 20 px in from the edges.
    void Arrange()
    {
        if (title is null || body is null || go is null || cancel is null) return;
        int pad = Px(20);
        var goSize = go.GetPreferredSize(Size.Empty);
        var cancelSize = cancel.GetPreferredSize(Size.Empty);
        int buttons = Math.Max(Px(88), Math.Max(goSize.Width, cancelSize.Width));
        int height = Math.Max(goSize.Height, cancelSize.Height);
        int goWidth = Math.Max(buttons, goSize.Width);
        int inner = Math.Max(Px(380), goWidth + Px(8) + buttons);
        int width = inner + pad * 2;
        int y = pad;
        title.Bounds = new Rectangle(pad, y, inner, Shapes.WrappedHeight(title.Text, title.Font, inner));
        y = title.Bottom + Px(8);
        body.Bounds = new Rectangle(pad, y, inner, Shapes.WrappedHeight(body.Text, body.Font, inner));
        y = body.Bottom + Px(24);
        cancel.Bounds = new Rectangle(width - pad - buttons, y, buttons, height);
        go.Bounds = new Rectangle(cancel.Left - Px(8) - goWidth, y, goWidth, height);
        ClientSize = new Size(width, go.Bottom + pad);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Arrange();
    }
}
