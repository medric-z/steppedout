using System.Diagnostics;

namespace SteppedOut;

sealed class SettingsForm : Form
{
    readonly NumericUpDown idleMinutes = new() { Minimum = 0.05m, Maximum = 1440, DecimalPlaces = 2, Increment = 1, Width = 80 };
    readonly NumericUpDown graceSeconds = new() { Minimum = 0, Maximum = 600, Width = 80 };
    readonly CheckBox jitterRule = new() { Text = "Ignore tiny mouse movements (sensor jitter)", AutoSize = true };
    readonly NumericUpDown jitterPixels = new() { Minimum = 1, Maximum = 100, Width = 80 };
    readonly CheckBox audioRule = new() { Text = "Don't lock while audio is playing", AutoSize = true };
    readonly CheckBox fullscreenRule = new() { Text = "Don't lock while an app is fullscreen", AutoSize = true };
    readonly CheckBox displayRule = new() { Text = "Don't lock while an app keeps the display on", AutoSize = true };
    readonly NumericUpDown audioThreshold = new() { Minimum = 0.001m, Maximum = 1, DecimalPlaces = 3, Increment = 0.005m, Width = 80 };
    readonly NumericUpDown audioSilence = new() { Minimum = 0, Maximum = 600, Width = 80 };
    readonly TextBox neverLock = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Width = 180, Height = 72 };
    readonly NumericUpDown logKilobytes = new() { Minimum = 64, Maximum = 65536, Increment = 64, Width = 80 };
    readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    readonly FlowLayoutPanel buttons;
    readonly Label version;

    public event Action<Config>? Applied;

    public SettingsForm(Config config, Icon icon)
    {
        Text = AppInfo.Name + " settings";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Font = new Font("Segoe UI", 9.5f);

        idleMinutes.Value = (decimal)Math.Clamp(config.IdleMinutes, 0.05, 1440);
        graceSeconds.Value = config.GraceSeconds;
        jitterRule.Checked = config.JitterFilter;
        jitterPixels.Value = config.JitterPixels;
        audioRule.Checked = config.SuppressWhileAudioPlaying;
        fullscreenRule.Checked = config.SuppressWhileFullscreen;
        displayRule.Checked = config.SuppressWhileDisplayRequested;
        audioThreshold.Value = (decimal)config.AudioPeakThreshold;
        audioSilence.Value = config.AudioSilenceSeconds;
        neverLock.Text = string.Join(Environment.NewLine, config.NeverLockWhileRunning);
        logKilobytes.Value = config.LogMaxKilobytes;

        var general = NewTab("General");
        var generalGrid = NewGrid();
        AddRow(generalGrid, "Lock after", idleMinutes, "minutes without input");
        AddRow(generalGrid, "Warn for", graceSeconds, "seconds before locking, 0 locks at once");
        AddRule(generalGrid, jitterRule);
        AddRule(generalGrid, audioRule);
        AddRule(generalGrid, fullscreenRule);
        AddRule(generalGrid, displayRule);
        general.Controls.Add(generalGrid);

        var advanced = NewTab("Advanced");
        var advancedGrid = NewGrid();
        AddRow(advancedGrid, "Jitter threshold", jitterPixels, "px, smaller mouse movements do not count as input");
        AddRow(advancedGrid, "Audio threshold", audioThreshold, "peak level that counts as sound, 0 to 1");
        AddRow(advancedGrid, "Silence hold", audioSilence, "seconds of quiet before audio counts as stopped");
        AddRow(advancedGrid, "Never lock while running", neverLock, "one program per line, for example obs64 or Zoom");
        AddRow(advancedGrid, "Log size limit", logKilobytes, "KB, older lines move to a .old.log file");
        var openFolder = new Button { Text = "Open folder", AutoSize = true };
        openFolder.Click += (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppInfo.ConfigDir}\"") { UseShellExecute = true });
        AddRow(advancedGrid, "Config file", openFolder, AppInfo.ConfigPath);
        advanced.Controls.Add(advancedGrid);

        tabs.TabPages.Add(general);
        tabs.TabPages.Add(advanced);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) => Apply();
        cancel.Click += (_, _) => Close();
        AcceptButton = ok;
        CancelButton = cancel;

        buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        version = new Label
        {
            Text = $"{AppInfo.Name} {AppInfo.Version}",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Dock = DockStyle.Bottom,
            Padding = new Padding(10, 0, 0, 0),
        };

        Controls.Add(tabs);
        Controls.Add(version);
        Controls.Add(buttons);
    }

    // Sized from the content once fonts and DPI are known, so no row is ever clipped.
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var needed = Size.Empty;
        foreach (TabPage page in tabs.TabPages)
        {
            var content = page.Controls[0].GetPreferredSize(Size.Empty);
            needed.Width = Math.Max(needed.Width, content.Width + page.Padding.Horizontal);
            needed.Height = Math.Max(needed.Height, content.Height + page.Padding.Vertical);
        }
        int chromeWidth = tabs.Width - tabs.DisplayRectangle.Width;
        int chromeHeight = tabs.Height - tabs.DisplayRectangle.Height;
        ClientSize = new Size(needed.Width + chromeWidth, needed.Height + chromeHeight + buttons.Height + version.Height);
    }

    void Apply()
    {
        var config = new Config
        {
            IdleMinutes = (double)idleMinutes.Value,
            GraceSeconds = (int)graceSeconds.Value,
            JitterFilter = jitterRule.Checked,
            JitterPixels = (int)jitterPixels.Value,
            SuppressWhileAudioPlaying = audioRule.Checked,
            SuppressWhileFullscreen = fullscreenRule.Checked,
            SuppressWhileDisplayRequested = displayRule.Checked,
            AudioPeakThreshold = (double)audioThreshold.Value,
            AudioSilenceSeconds = (int)audioSilence.Value,
            NeverLockWhileRunning = neverLock.Lines.Select(ProcessRule.Normalize).Where(n => n.Length > 0).ToList(),
            LogMaxKilobytes = (int)logKilobytes.Value,
        };
        Applied?.Invoke(config);
        Close();
    }

    static TabPage NewTab(string title) => new(title) { Padding = new Padding(12), UseVisualStyleBackColor = true };

    static TableLayoutPanel NewGrid()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true };
        for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return grid;
    }

    static void AddRule(TableLayoutPanel grid, CheckBox box)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        box.Margin = new Padding(0, 6, 0, 4);
        grid.Controls.Add(box, 0, row);
        grid.SetColumnSpan(box, 3);
    }

    static void AddRow(TableLayoutPanel grid, string label, Control control, string hint)
    {
        int row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 10, 8) }, 0, row);
        control.Anchor = AnchorStyles.Left;
        control.Margin = new Padding(0, 0, 10, 8);
        grid.Controls.Add(control, 1, row);
        grid.Controls.Add(new Label
        {
            Text = hint,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 0, 0, 8),
            MaximumSize = new Size(300, 0),
        }, 2, row);
    }
}
