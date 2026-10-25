using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SteppedOut;

// Draws menus the way Windows 11 does, in the light or dark colors: the tray menu and the lists
// that open from a DropDown. A menu has rounded corners and an edge, rows with a rounded fill under
// the pointer, the chosen row in the accent color, and small gray headings over groups of rows.
sealed class MenuLook : ToolStripRenderer
{
    public static ContextMenuStrip NewMenu(RightToLeft rightToLeft)
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new MenuLook(),
            ShowImageMargin = false,
            // No room kept for a check mark: a menu that checks its rows turns it back on.
            ShowCheckMargin = false,
            RightToLeft = rightToLeft,
            Font = Theme.Body(10f),
            Padding = new Padding(4),
        };
        menu.HandleCreated += (_, _) => Corners(menu.Handle);
        return menu;
    }

    public static ToolStripMenuItem Item(string text, Action? click = null)
    {
        // Padding only makes the rows taller. A menu is as wide as its widest text and leaves its
        // items' padding out, so padding at the sides would push every item, and its hover, past the
        // menu's right edge.
        var item = new ToolStripMenuItem(text) { Padding = new Padding(0, 5, 0, 5) };
        if (click is not null) item.Click += (_, _) => click();
        return item;
    }

    // A small gray line over a group of rows, such as "Lock sooner". It cannot be chosen, so the
    // arrow keys pass over it.
    public static ToolStripItem Heading(string text) => new MenuHeading(text);

    // Marks the row that is the current choice: checked, which screen readers say, and drawn in the
    // accent color in semibold, the size of the menu's own words. A row not yet in a menu takes the
    // size a new menu has.
    public static void Choose(ToolStripMenuItem item) => Choose(item, 10f, Theme.SystemDpi);

    // The same, for a menu whose words were asked for at this size and scaling. It takes the size
    // that was asked for, not the menu font's own size, which already has the text size applied and
    // would get it a second time.
    public static void Choose(ToolStripMenuItem item, float points, int dpi)
    {
        item.Checked = true;
        item.Font = Theme.Strong(points, dpi);
    }

    // How long to pause for, in the tray menu and under Pause in the settings window: the one list
    // both use, so the two never differ. 15 and 30 minutes, 1 and 2 hours, 1 day, Until turned back
    // on, and Custom…, which asks custom to open the Pause for window.
    public static ToolStripItem[] PauseChoices(Strings text, Action<TimeSpan?> pause, Action custom)
    {
        var items = new List<ToolStripItem>();
        foreach (int minutes in TimeChoices.Pause) items.Add(Item(text.PauseChoice(minutes), () => pause(TimeSpan.FromMinutes(minutes))));
        items.Add(new ToolStripSeparator());
        items.Add(Item(text.PauseChoice(null), () => pause(null)));
        items.Add(Item(text.V1.Custom, custom));
        return items.ToArray();
    }

    // Opens a menu made for one use under a button, and throws it away once it closes.
    public static void ShowBelow(ContextMenuStrip menu, Control button)
    {
        menu.Closed += (_, _) => button.BeginInvoke(menu.Dispose);
        menu.Show(button, new Point(0, button.Height + button.LogicalToDeviceUnits(2)));
    }

    // Lets the tests draw in high contrast without changing Windows. Null follows Windows.
    internal static bool? HighContrastForTests;

    // High contrast takes the system's menu and selection colors: the soft gray fill would not show
    // which item Enter will run.
    static bool Hc => HighContrastForTests ?? SystemInformation.HighContrast;
    static Color Back => Hc ? SystemColors.Menu : Theme.Current.Card;
    static Color Hover => Hc ? SystemColors.Highlight : Theme.Current.FieldHover;
    static Color Fore => Hc ? SystemColors.MenuText : Theme.Current.Text;
    static Color Edge => Hc ? SystemColors.WindowFrame : Theme.Current.ControlEdge;
    static Color Muted => Hc ? SystemColors.GrayText : Theme.Current.Subtle;

    // What is drawn on the selected item sits on the Highlight fill in high contrast.
    static Color Ink(ToolStripItem? item, Color normal) => Hc && item is { Selected: true, Enabled: true } ? SystemColors.HighlightText : normal;

    // The color of an item's words: gray for a heading or a disabled line, the accent for the
    // current choice. High contrast keeps the menu's own text color and shows it by the semibold.
    internal static Color TextColor(ToolStripItem item)
    {
        if (item is MenuHeading || !item.Enabled) return Muted;
        if (item is ToolStripMenuItem { Checked: true } && !Hc) return Theme.Current.Accent;
        return Ink(item, Fore);
    }

    protected override void Initialize(ToolStrip toolStrip)
    {
        base.Initialize(toolStrip);
        toolStrip.BackColor = Back;
        toolStrip.ForeColor = Fore;
    }

    protected override void InitializeItem(ToolStripItem item)
    {
        base.InitializeItem(item);
        item.ForeColor = Fore;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Back);

    // Windows 11 rounds the menu's corners itself and draws its own edge over this one, in the same
    // color; Windows 10 keeps the square edge drawn here.
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Edge);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var box = new RectangleF(2, 1, e.Item.Width - 4, e.Item.Height - 2);
        using (var path = Shapes.Rounded(box, e.Item.Owner?.LogicalToDeviceUnits(5) ?? 5))
        using (var brush = new SolidBrush(Hover))
            g.FillPath(brush, path);
        Mark(g, box, e.Item.RightToLeft == RightToLeft.Yes, e.Item.Owner?.DeviceDpi ?? 96, Hc);
    }

    // The soft fill alone is too faint to find (1.1 to 1.3 to 1 against the menu), so the row Enter
    // runs also has a 3 px accent bar at its start, as Windows 11 marks the chosen row of a list:
    // 16 px tall in the middle of the row, at its right in Arabic. High contrast fills the whole row
    // with the selection color instead.
    internal static void Mark(Graphics g, RectangleF row, bool rightToLeft, int dpi, bool highContrast)
    {
        if (highContrast) return;
        float Px(float logical) => logical * dpi / 96f;
        float width = Px(3), height = Math.Min(Px(16), row.Height - Px(8));
        if (height <= 0) return;
        var bar = new RectangleF(rightToLeft ? row.Right - width : row.Left, row.Top + (row.Height - height) / 2, width, height);
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Shapes.Rounded(bar, width / 2))
        using (var brush = new SolidBrush(Theme.Current.Accent))
            g.FillPath(brush, path);
        g.SmoothingMode = smoothing;
    }

    // Drawn here, not by the base class, which draws a disabled item such as the tray menu's status
    // line in the system's gray whatever color it is given: too dark to read on the dark menu.
    // WinForms also places the text as if the row had no padding, high in it; it goes in the middle
    // of what the padding leaves, level with the arrow.
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var box = e.TextRectangle;
        var padding = e.Item.Padding;
        box.Y = padding.Top + (e.Item.Height - padding.Vertical - box.Height) / 2;
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, box, TextColor(e.Item), e.TextFormat);
    }

    // In the middle of the row too, beside the text.
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        var font = e.Item.Owner is { } menu ? Theme.Icons(9f, menu.DeviceDpi) : Theme.Icons(9f);
        var box = e.ImageRectangle;
        box.Y = (e.Item.Height - box.Height) / 2;
        TextRenderer.DrawText(e.Graphics, Glyphs.Check, font, box, Ink(e.Item, Theme.Current.Accent), flags);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Ink(e.Item, Fore);
        base.OnRenderArrow(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(Theme.Current.Line);
        e.Graphics.DrawLine(pen, 4, y, e.Item.Width - 4, y);
    }

    // Corners rounded as much as the settings window's (8 px at 100%), and Windows 11's own edge in
    // the menu's edge color. Earlier versions of Windows ignore both.
    static void Corners(IntPtr window)
    {
        int corner = DwmwcpRound;
        DwmSetWindowAttribute(window, DwmwaWindowCornerPreference, ref corner, sizeof(int));
        if (Hc) return;
        int edge = ColorTranslator.ToWin32(Edge);
        DwmSetWindowAttribute(window, DwmwaBorderColor, ref edge, sizeof(int));
    }

    const int DwmwaWindowCornerPreference = 33;
    const int DwmwaBorderColor = 34;
    const int DwmwcpRound = 2;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    // A heading over a group of rows: small, gray, and never chosen, so the menu's own arrow keys
    // pass over it. Its words sit low, close to the rows they head.
    sealed class MenuHeading : ToolStripLabel
    {
        public MenuHeading(string text) : base(text)
        {
            Font = Theme.Body(8.25f);
            Padding = new Padding(0, 7, 0, 2);
        }
    }
}
