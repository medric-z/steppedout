namespace SteppedOut.Tests;

// The menus are only built and drawn into bitmaps here, never shown. They measure and draw their text
// with the shared font cache, which is not made for several threads at once.
[TestClass]
[DoNotParallelize]
public class MenuLookTests
{
    // The one list the tray menu and the status box share: 15 and 30 minutes, 1 and 2 hours, 1 day,
    // a line, until turned back on, and Custom…, which asks for the Pause for window.
    [TestMethod]
    public void EachPauseChoicePausesForItsTime()
    {
        foreach (var text in new[] { English.Text, Arabic.Text })
        {
            var asked = new List<TimeSpan?>();
            int custom = 0;
            var choices = MenuLook.PauseChoices(text, asked.Add, () => custom++);
            Assert.HasCount(8, choices);
            Assert.IsInstanceOfType<ToolStripSeparator>(choices[5]);
            var items = choices.Where(c => c is not ToolStripSeparator).ToArray();
            CollectionAssert.AreEqual(new[] { text.V1.Minutes15, text.V1.Minutes30, text.V1.Hour1, text.V1.Hours2, text.V1.Day1, text.V1.UntilTurnedBackOn, text.V1.Custom },
                items.Select(c => c.Text).ToArray());
            foreach (var choice in items) choice.PerformClick();
            CollectionAssert.AreEqual(new TimeSpan?[] { TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromDays(1), null }, asked);
            Assert.AreEqual(1, custom, "Custom… asks for the Pause for window and pauses nothing itself");
        }
        Assert.AreEqual("15 minutes", English.Text.PauseChoice(15));
        Assert.AreEqual("1 hour", English.Text.PauseChoice(60));
        Assert.AreEqual("2 hours", English.Text.PauseChoice(120));
        Assert.AreEqual("1 day", English.Text.PauseChoice(24 * 60));
        Assert.AreEqual("Until turned back on", English.Text.PauseChoice(null));
    }

    // The menu's own keys move from row to row; a heading is passed over, up and down.
    [TestMethod]
    public void AHeadingIsPassedOverByTheKeys()
    {
        using var menu = MenuLook.NewMenu(RightToLeft.No);
        var first = MenuLook.Heading("Lock sooner");
        var a = MenuLook.Item("25% sooner");
        var second = MenuLook.Heading("Fixed time");
        var b = MenuLook.Item("Always lock after 2 minutes");
        menu.Items.AddRange(new[] { first, a, second, b });
        menu.PerformLayout();
        Assert.IsFalse(first.CanSelect);
        Assert.AreSame(b, menu.GetNextItem(a, ArrowDirection.Down));
        Assert.AreSame(a, menu.GetNextItem(b, ArrowDirection.Up));
        Assert.AreSame(a, menu.GetNextItem(null, ArrowDirection.Down));
    }

    // The chosen row is checked, which screen readers say, and drawn in the accent in semibold; a
    // heading is gray. High contrast keeps the menu's text color.
    [TestMethod]
    public void TheChosenRowIsCheckedAndDrawnInTheAccent()
    {
        var plain = MenuLook.Item("10 seconds");
        var chosen = MenuLook.Item("15 seconds");
        MenuLook.Choose(chosen);
        var heading = MenuLook.Heading("Warning");
        Assert.IsTrue(chosen.Checked);
        Assert.AreEqual(Theme.Strong(10f), chosen.Font, "out of a menu, the size a new menu has, not the 9 pt default");
        // A menu asked for at another size takes that size, given as asked for, never read back
        // from the menu's font: that one already carries the text size, which would apply twice.
        using (var menu = MenuLook.NewMenu(RightToLeft.No))
        {
            menu.Font = Theme.Body(12f);
            var inside = MenuLook.Item("30 seconds");
            menu.Items.Add(inside);
            MenuLook.Choose(inside, 12f, Theme.SystemDpi);
            Assert.AreEqual(Theme.Strong(12f), inside.Font);
        }
        int dpi = Theme.SystemDpi * 3 / 2;
        var scaled = MenuLook.Item("30 minutes");
        MenuLook.Choose(scaled, 10f, dpi);
        Assert.AreEqual(Theme.Strong(10f, dpi), scaled.Font);
        Assert.AreEqual(Theme.Body(10f, dpi).SizeInPoints, scaled.Font.SizeInPoints, 0.01f);
        try
        {
            MenuLook.HighContrastForTests = false;
            Assert.AreEqual(Theme.Current.Accent, MenuLook.TextColor(chosen));
            Assert.AreEqual(Theme.Current.Text, MenuLook.TextColor(plain));
            Assert.AreEqual(Theme.Current.Subtle, MenuLook.TextColor(heading));
            MenuLook.HighContrastForTests = true;
            Assert.AreEqual(SystemColors.MenuText, MenuLook.TextColor(chosen));
            Assert.AreEqual(SystemColors.GrayText, MenuLook.TextColor(heading));
        }
        finally
        {
            MenuLook.HighContrastForTests = null;
        }
    }

    // The pause list, in the tray and under the status box, has rows as tall as a list's and keeps
    // no room for a check mark it never shows, so its words start where a list's do.
    [TestMethod]
    public void ThePauseListIsShapedLikeAList()
    {
        using var pause = MenuLook.NewMenu(RightToLeft.No);
        pause.Items.AddRange(MenuLook.PauseChoices(English.Text, _ => { }, () => { }));
        pause.PerformLayout();
        Assert.IsFalse(pause.ShowCheckMargin);
        var list = new DropDown().Add("15 minutes", 15).Add("30 minutes", 30);
        list.Value = 15;
        using var menu = list.BuildMenu();
        menu.PerformLayout();
        Assert.AreEqual(menu.Items[1].Height, pause.Items[0].Height, "a pause row is as tall as a list row");
        Assert.AreEqual(menu.Items[1].Padding, pause.Items[0].Padding);
        Assert.AreEqual(menu.DisplayRectangle.Left, pause.DisplayRectangle.Left);
    }

    // The tray menu as the app builds it, in English and in Arabic, paused and not, with the shortcut
    // beside Lock now: every row and the pause list stay inside their menu, and there is no
    // Language row (the language is chosen in the settings window).
    [TestMethod]
    public void ItemsStayInsideTheMenu()
    {
        var on = new LockStatus
        {
            Kind = LockState.On,
            Time = LockTime.Plain(15),
            IdleSeconds = 150,
            SecondsLeft = 750,
            Today = new TodayCounts(3, TimeSpan.FromMinutes(95)),
        };
        var paused = new LockStatus { Kind = LockState.Paused, Time = LockTime.Plain(15), Today = on.Today };
        foreach (var text in new[] { English.Text, Arabic.Text })
            foreach (var (status, isPaused) in new[] { (on, false), (paused, true) })
            {
                using var tray = new TrayMenu(text);
                tray.Apply(new TrayMenuState(status, isPaused, LockingOn: true, "Ctrl+Alt+L"));
                string what = $"{(text.RightToLeft ? "ar" : "en")}{(isPaused ? ", paused" : "")}";
                Assert.AreEqual(text.RightToLeft ? RightToLeft.Yes : RightToLeft.No, tray.Menu.RightToLeft, what);
                foreach (var menu in new ToolStripDropDown[] { tray.Menu, tray.PauseList })
                {
                    menu.PerformLayout();
                    var preferred = menu.GetPreferredSize(Size.Empty);
                    menu.Size = new Size(Math.Max(preferred.Width, menu.MinimumSize.Width), preferred.Height);
                    menu.PerformLayout();
                    foreach (var item in menu.Items.Cast<ToolStripItem>().Where(i => i.Available))
                    {
                        Assert.IsGreaterThanOrEqualTo(0, item.Bounds.Left, $"{what}: {item.Text} starts before the edge");
                        Assert.IsLessThanOrEqualTo(menu.Width - 1, item.Bounds.Right, $"{what}: {item.Text} reaches past the edge of a menu {menu.Width} wide");
                    }
                }
                var words = tray.Menu.Items.Cast<ToolStripItem>().Concat(tray.PauseChoices).Select(i => i.Text ?? "").ToList();
                foreach (var language in new[] { English.Text.V1.Language, Arabic.Text.V1.Language, "English", "العربية" })
                    CollectionAssert.DoesNotContain(words, language, what);
                Assert.AreEqual(text.Headline(status, true), tray.StatusItem.Headline, what);
            }
    }

    // WinForms places a menu's text as if the row had no padding, high in it.
    [TestMethod]
    public void TextSitsInTheMiddleOfTheRow()
    {
        using var menu = StatusMenu(out var status);
        int line = TextRenderer.MeasureText(status.Text, menu.Font).Height;
        using var drawn = Row(status, menu.BackColor, g => menu.Renderer.DrawItemText(new ToolStripItemTextRenderEventArgs(
            g, status, status.Text, new Rectangle(4, 0, status.Width - 8, line), Color.Red, menu.Font, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix)));
        using var centered = Row(status, menu.BackColor, g => TextRenderer.DrawText(
            g, status.Text, menu.Font, new Rectangle(4, 0, status.Width - 8, status.Height), Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix));
        var (top, bottom, _) = Ink(drawn, menu.BackColor);
        var (wantTop, wantBottom, _) = Ink(centered, menu.BackColor);
        Assert.IsLessThanOrEqualTo(2, Math.Abs(top + bottom - wantTop - wantBottom), $"the text spans {top}..{bottom}, centered it would span {wantTop}..{wantBottom}");
    }

    // The base renderer draws a disabled item, such as the tray menu's status line, in the system's
    // gray whatever color it is given.
    [TestMethod]
    [TestCategory("Desktop")]
    public void AStatusLineIsDrawnInTheThemesColor()
    {
        using var menu = StatusMenu(out var status);
        using var drawn = Row(status, menu.BackColor, g => menu.Renderer.DrawItemText(new ToolStripItemTextRenderEventArgs(
            g, status, status.Text, new Rectangle(4, 0, status.Width - 8, status.Height), Color.Red, menu.Font, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix)));
        var subtle = SystemInformation.HighContrast ? SystemColors.GrayText : Theme.Current.Subtle;
        var (_, _, far) = Ink(drawn, menu.BackColor);
        Assert.AreEqual(subtle.ToArgb(), far.ToArgb(), $"the status line is drawn in {far}");
    }

    static ContextMenuStrip StatusMenu(out ToolStripMenuItem status)
    {
        var menu = MenuLook.NewMenu(RightToLeft.No);
        status = MenuLook.Item("Locks in 13 minutes");
        status.Enabled = false;
        menu.Items.Add(status);
        menu.PerformLayout();
        return menu;
    }

    static Bitmap Row(ToolStripItem item, Color back, Action<Graphics> draw)
    {
        var bmp = new Bitmap(item.Width, item.Height);
        using var g = Graphics.FromImage(bmp);
        g.Clear(back);
        draw(g);
        return bmp;
    }

    // The rows the ink covers, and the pixel furthest from the background.
    static (int Top, int Bottom, Color Far) Ink(Bitmap bmp, Color back)
    {
        int top = int.MaxValue, bottom = -1, farthest = -1;
        Color far = back;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var p = bmp.GetPixel(x, y);
                int d = Math.Abs(p.R - back.R) + Math.Abs(p.G - back.G) + Math.Abs(p.B - back.B);
                if (d < 40) continue;
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
                if (d > farthest)
                {
                    farthest = d;
                    far = p;
                }
            }
        return (top, bottom, far);
    }
}
