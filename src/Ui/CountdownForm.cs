namespace SteppedOut;

sealed class CountdownForm : Form
{
    const int WsExToolWindow = 0x00000080;
    const int WsExNoActivate = 0x08000000;

    readonly Label headline = new();
    readonly Label hint = new();
    readonly Button lockNow = new();

    public event Action? LockNowClicked;

    public CountdownForm()
    {
        Text = AppInfo.Name;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.FromArgb(30, 34, 42);
        ForeColor = Color.White;
        Padding = new Padding(18, 14, 18, 14);
        Font = new Font("Segoe UI", 10f);

        headline.AutoSize = true;
        headline.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
        headline.Margin = new Padding(0, 0, 0, 4);

        hint.AutoSize = true;
        hint.Text = "Move the mouse or press a key to stay signed in.";
        hint.ForeColor = Color.FromArgb(190, 196, 206);
        hint.Margin = new Padding(0, 0, 0, 10);

        lockNow.Text = "Lock now";
        lockNow.AutoSize = true;
        lockNow.FlatStyle = FlatStyle.Flat;
        lockNow.FlatAppearance.BorderColor = Color.FromArgb(90, 96, 108);
        lockNow.Padding = new Padding(8, 2, 8, 2);
        lockNow.Click += (_, _) => LockNowClicked?.Invoke();

        var stack = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
        };
        stack.Controls.Add(headline);
        stack.Controls.Add(hint);
        stack.Controls.Add(lockNow);
        Controls.Add(stack);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            // Never take focus. Activating the toast would change the foreground window, which is
            // both an interruption and a false signal to the fullscreen and jitter checks.
            var cp = base.CreateParams;
            cp.ExStyle |= WsExNoActivate | WsExToolWindow;
            return cp;
        }
    }

    public void ShowCountdown(int seconds)
    {
        SetSeconds(seconds);
        if (!Visible) Show();
        // Twice: moving onto a monitor with a different DPI rescales the form after the first pass.
        Place();
        Place();
    }

    public void SetSeconds(int seconds)
    {
        headline.Text = $"Locking in {seconds} s";
        if (Visible) Place();
    }

    void Place()
    {
        PerformLayout();
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);
    }

    public void HideCountdown()
    {
        if (Visible) Hide();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(90, 96, 108));
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
