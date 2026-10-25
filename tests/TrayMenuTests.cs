using System.Reflection;

namespace SteppedOut.Tests;

// The tray menu, built and laid out but never shown. It measures and draws with the shared font
// cache, which is not made for several threads at once, so these run one at a time.
[TestClass]
[DoNotParallelize]
public class TrayMenuTests
{
    static LockStatus On(double minutesLeft = 28) => new()
    {
        Kind = LockState.On,
        Time = LockTime.Plain(30),
        IdleSeconds = (30 - minutesLeft) * 60,
        SecondsLeft = minutesLeft * 60,
        Today = new TodayCounts(2, TimeSpan.FromMinutes(80)),
    };

    static LockStatus PausedStatus() => new()
    {
        Kind = LockState.Paused,
        Time = LockTime.Plain(30),
        Today = new TodayCounts(2, TimeSpan.FromMinutes(80)),
    };

    static TrayMenuState State(LockStatus? status = null, bool paused = false, string? shortcut = "Ctrl+Alt+L") =>
        new(status ?? On(), paused, LockingOn: true, shortcut);

    static List<ToolStripItem> Shown(TrayMenu tray) => tray.Menu.Items.Cast<ToolStripItem>().Where(i => i.Available).ToList();

    // The sketch's rows, in its order: the status box, Pause, Lock now, Settings, a line, Quit
    // SteppedOut; Turn back on waits hidden in Pause's place. No Language row.
    [TestMethod]
    public void TheRowsAreTheSketchsInItsOrderWithNoLanguageRow()
    {
        foreach (var text in new[] { English.Text, Arabic.Text })
        {
            using var tray = new TrayMenu(text);
            var items = tray.Menu.Items.Cast<ToolStripItem>().ToList();
            CollectionAssert.AreEqual(new ToolStripItem[] { tray.StatusItem, tray.SnoozeRow, tray.PauseRow, tray.TurnBackOnRow, tray.LockNowRow, tray.SettingsRow, tray.Line, tray.QuitRow }, items);
            var v = text.V1;
            CollectionAssert.AreEqual(new[] { v.SnoozeReminder, v.Pause, v.TurnBackOn, v.LockNow, v.Settings, v.QuitSteppedOut }, new[] { tray.SnoozeRow, tray.PauseRow, tray.TurnBackOnRow, tray.LockNowRow, tray.SettingsRow, tray.QuitRow }.Select(r => r.Text).ToArray());
            CollectionAssert.AreEqual(new ToolStripItem[] { tray.StatusItem, tray.PauseRow, tray.LockNowRow, tray.SettingsRow, tray.Line, tray.QuitRow }, Shown(tray));
            Assert.IsFalse(items.Any(i => i.Text == v.Language || i.Text == v.LanguageEnglish || i.Text == v.LanguageArabic), "no Language row");
            Assert.IsInstanceOfType<ToolStripSeparator>(tray.Line);
            Assert.AreEqual(Glyphs.Pause, tray.PauseRow.Glyph);
            Assert.AreEqual(Glyphs.Lock, tray.LockNowRow.Glyph);
            Assert.AreEqual(Glyphs.Gear, tray.SettingsRow.Glyph);
            Assert.AreEqual(Glyphs.Exit, tray.QuitRow.Glyph);
            Assert.IsTrue(tray.QuitRow.Muted && !tray.PauseRow.Muted, "Quit's glyph is the gray one");
        }
    }

    // While paused, Turn back on takes Pause's place. It follows the paused flag (the watcher's
    // IsPaused, which is at once), not the status, which is up to a second behind.
    [TestMethod]
    public void TurnBackOnReplacesPauseWhilePaused()
    {
        using var tray = new TrayMenu(English.Text);
        tray.Apply(State(PausedStatus(), paused: true));
        CollectionAssert.AreEqual(new ToolStripItem[] { tray.StatusItem, tray.TurnBackOnRow, tray.LockNowRow, tray.SettingsRow, tray.Line, tray.QuitRow }, Shown(tray));
        Assert.AreEqual("Paused · until turned back on", tray.StatusItem.Headline);

        tray.Apply(State(On(), paused: true));
        Assert.IsTrue(tray.TurnBackOnRow.Available && !tray.PauseRow.Available, "paused a moment ago, the status not yet");

        tray.Apply(State(PausedStatus(), paused: false));
        Assert.IsTrue(tray.PauseRow.Available && !tray.TurnBackOnRow.Available, "turned back on a moment ago, the status not yet");

        tray.Apply(State());
        CollectionAssert.AreEqual(new ToolStripItem[] { tray.StatusItem, tray.PauseRow, tray.LockNowRow, tray.SettingsRow, tray.Line, tray.QuitRow }, Shown(tray));
    }

    // The pause list is the shared one (MenuLook.PauseChoices), made fresh for the menu, never a copy.
    [TestMethod]
    public void ThePauseListIsTheSharedChoices()
    {
        foreach (var text in new[] { English.Text, Arabic.Text })
        {
            using var tray = new TrayMenu(text);
            var asked = new List<TimeSpan?>();
            int custom = 0;
            tray.PauseChosen += asked.Add;
            tray.PauseForChosen += () => custom++;
            var mine = tray.PauseChoices.ToList();
            var shared = MenuLook.PauseChoices(text, _ => { }, () => { });
            CollectionAssert.AreEqual(shared.Select(i => i.GetType()).ToArray(), mine.Select(i => i.GetType()).ToArray());
            CollectionAssert.AreEqual(shared.Select(i => i.Text).ToArray(), mine.Select(i => i.Text).ToArray());
            foreach (var item in mine.Where(i => i is not ToolStripSeparator)) item.PerformClick();
            CollectionAssert.AreEqual(new TimeSpan?[] { TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromDays(1), null }, asked);
            Assert.AreEqual(1, custom, "Custom… asks the app for the Pause for window");
            foreach (var item in shared) item.Dispose();
            Assert.IsTrue(tray.PauseRow.HasDropDownItems);
        }
    }

    // Relabelled, the list is made again in the new language, and still raises the same event.
    [TestMethod]
    public void ANewLanguageRelabelsEveryRow()
    {
        using var tray = new TrayMenu(English.Text);
        tray.Apply(State());
        var asked = new List<TimeSpan?>();
        tray.PauseChosen += asked.Add;
        tray.Relabel(Arabic.Text);
        Assert.AreEqual(RightToLeft.Yes, tray.Menu.RightToLeft);
        Assert.AreEqual(RightToLeft.Yes, tray.PauseList.RightToLeft);
        Assert.AreEqual(Arabic.Text.V1.Pause, tray.PauseRow.Text);
        Assert.AreEqual(Arabic.Text.V1.QuitSteppedOut, tray.QuitRow.Text);
        Assert.AreEqual(Arabic.Text.Headline(On()), tray.StatusItem.Headline, "the status in the new language at once");
        CollectionAssert.AreEqual(MenuLook.PauseChoices(Arabic.Text, _ => { }, () => { }).Select(i => i.Text).ToArray(), tray.PauseChoices.Select(i => i.Text).ToArray());
        tray.PauseChoices.First().PerformClick();
        CollectionAssert.AreEqual(new TimeSpan?[] { TimeSpan.FromMinutes(15) }, asked);
        tray.Relabel(English.Text);
        Assert.AreEqual(RightToLeft.No, tray.Menu.RightToLeft);
        Assert.AreEqual(7 + 1, tray.PauseChoices.Count(), "the old list was replaced, not added to");
    }

    // The shortcut shows beside Lock now only while one is registered, always left to right.
    [TestMethod]
    public void TheShortcutShowsOnlyWhileItIsOnAndReadsLeftToRight()
    {
        foreach (var text in new[] { English.Text, Arabic.Text })
        {
            using var tray = new TrayMenu(text);
            var row = tray.LockNowRow;
            tray.Apply(State(shortcut: null));
            Assert.AreEqual("", row.ShortcutKeyDisplayString ?? "");
            Assert.AreEqual(0, row.TrailWidth);
            Assert.AreEqual(text.V1.LockNow, row.AccessibilityObject.Name);

            tray.Apply(State(shortcut: "ctrl + alt + l"));
            Assert.AreEqual("\u200ECtrl+Alt+L\u200E", row.ShortcutKeyDisplayString, "written the usual way, between left-to-right marks");
            Assert.IsGreaterThan(0, row.TrailWidth);
            Assert.AreEqual($"{text.V1.LockNow} Ctrl+Alt+L", row.AccessibilityObject.Name, "a screen reader hears it too");
            Assert.IsFalse(TrayMenuLook.ShortcutFlags.HasFlag(TextFormatFlags.RightToLeft), "drawn without the right-to-left flag");

            // Laid out, it is at the far end: the right in English, the left in Arabic.
            Lay(tray.Menu);
            var parts = row.Parts;
            if (text.RightToLeft) Assert.IsLessThanOrEqualTo(parts.Text.Left, parts.Trail.Right);
            else Assert.IsGreaterThanOrEqualTo(parts.Text.Right, parts.Trail.Left);

            tray.Apply(State(shortcut: "Ctrl+Alt+Shift+F24"));
            Assert.AreEqual("\u200ECtrl+Alt+Shift+F24\u200E", row.ShortcutKeyDisplayString);
            tray.Apply(State(shortcut: "not a shortcut"));
            Assert.AreEqual("", row.ShortcutKeyDisplayString ?? "", "nothing that could not be registered is shown");
        }
    }

    // Each row raises its own event, once, and nothing else; a hidden row raises nothing.
    [TestMethod]
    public void EachRowRaisesOnlyItsOwnEvent()
    {
        using var tray = new TrayMenu(English.Text);
        var heard = new List<string>();
        tray.PauseChosen += t => heard.Add($"pause {t?.TotalMinutes.ToString() ?? "until"}");
        tray.TurnBackOnChosen += () => heard.Add("turn back on");
        tray.LockNowChosen += () => heard.Add("lock now");
        tray.SettingsChosen += () => heard.Add("settings");
        tray.QuitChosen += () => heard.Add("quit");

        tray.Apply(State());
        tray.TurnBackOnRow.PerformClick();
        Assert.IsEmpty(heard, "Turn back on is hidden while not paused");
        tray.StatusItem.PerformClick();
        Assert.IsEmpty(heard, "the status box is not a command");

        tray.LockNowRow.PerformClick();
        CollectionAssert.AreEqual(new[] { "lock now" }, heard);
        tray.SettingsRow.PerformClick();
        tray.QuitRow.PerformClick();
        tray.PauseForChosen += () => heard.Add("pause for");
        tray.PauseChoices.Reverse().Skip(1).First().PerformClick();
        tray.PauseChoices.Last().PerformClick();
        CollectionAssert.AreEqual(new[] { "lock now", "settings", "quit", "pause until", "pause for" }, heard);

        heard.Clear();
        tray.Apply(State(PausedStatus(), paused: true));
        tray.TurnBackOnRow.PerformClick();
        tray.PauseRow.PerformClick();
        CollectionAssert.AreEqual(new[] { "turn back on" }, heard, "Pause is hidden while paused");
    }

    // The status box: the headline, the time away today, and the ring's share of the lock time.
    [TestMethod]
    public void TheStatusBoxShowsTheHeadlineAndTheTimeAway()
    {
        using var tray = new TrayMenu(English.Text);
        tray.Apply(State(On(28)));
        var status = tray.StatusItem;
        Assert.AreEqual("On · locks in 28 min", status.Headline);
        Assert.AreEqual("Away today:", status.AwayLabel);
        Assert.AreEqual("1 hr 20 min", status.AwayValue);
        Assert.AreEqual(2f / 30, status.Fraction, 0.001f);
        tray.Apply(new TrayMenuState(On(28) with { DryRun = true }, false, LockingOn: false, null));
        Assert.AreEqual(English.Text.Headline(On(28) with { DryRun = true }, lockingOn: false), status.Headline);
        tray.Apply(State(PausedStatus(), paused: true));
        Assert.AreEqual(0f, status.Fraction, "empty while paused (D19)");
    }

    // A screen reader hears every row by name, the status box as text, and the status again on the
    // first row the keys reach, since they pass over the box.
    [TestMethod]
    public void EveryRowHasANameAndTheStatusIsHeard()
    {
        foreach (var text in new[] { English.Text, Arabic.Text })
        {
            using var tray = new TrayMenu(text);
            tray.Apply(State());
            foreach (var item in Shown(tray).Where(i => i is not ToolStripSeparator))
                Assert.IsFalse(string.IsNullOrWhiteSpace(item.AccessibilityObject.Name), item.GetType().Name);
            var status = tray.StatusItem.AccessibilityObject;
            Assert.AreEqual(AccessibleRole.StaticText, status.Role);
            Assert.AreEqual(text.Headline(On()), status.Name);
            Assert.AreEqual($"{text.V1.AwayToday} {text.AwayToday(TimeSpan.FromMinutes(80))}", status.Description);
            Assert.IsFalse(tray.StatusItem.CanSelect);
            Lay(tray.Menu);
            Assert.AreSame(tray.PauseRow, tray.Menu.GetNextItem(null, ArrowDirection.Down), "the keys start on Pause");
            Assert.AreSame(tray.QuitRow, tray.Menu.GetNextItem(tray.PauseRow, ArrowDirection.Up), "and wrap past the status box");
            StringAssert.Contains(tray.PauseRow.AccessibilityObject.Description, text.Headline(On()));
            StringAssert.Contains(tray.PauseRow.AccessibilityObject.Description, text.AwayToday(TimeSpan.FromMinutes(80)));
            Assert.AreEqual(AppInfo.Name, tray.Menu.AccessibilityObject.Name);
            Assert.AreEqual(AccessibleRole.MenuItem, tray.LockNowRow.AccessibilityObject.Role);
        }
    }

    // Clicking the status box leaves the menu open: it does not ask to close, and the close Windows
    // starts for a clicked item anyway is turned back. A click on a row still closes the menu.
    [TestMethod]
    public void TheStatusBoxDoesNotCloseTheMenu()
    {
        var dismiss = typeof(ToolStripItem).GetProperty("DismissWhenClicked", BindingFlags.NonPublic | BindingFlags.Instance)!;
        using var tray = new TrayMenu(English.Text);
        Assert.IsFalse((bool)dismiss.GetValue(tray.StatusItem)!);
        Assert.IsTrue((bool)dismiss.GetValue(tray.LockNowRow)!);

        Assert.IsTrue(Closes(tray, null, ToolStripDropDownCloseReason.ItemClicked));
        Assert.IsFalse(Closes(tray, tray.StatusItem, ToolStripDropDownCloseReason.ItemClicked));
        Assert.IsTrue(Closes(tray, tray.StatusItem, ToolStripDropDownCloseReason.Keyboard));
        Assert.IsTrue(Closes(tray, null, ToolStripDropDownCloseReason.ItemClicked), "a click on the status box is forgotten after its close");
        Assert.IsTrue(Closes(tray, tray.LockNowRow, ToolStripDropDownCloseReason.ItemClicked));
    }

    // Whether the menu would close for this reason after this item was clicked (null: none was).
    static bool Closes(TrayMenu tray, ToolStripItem? clicked, ToolStripDropDownCloseReason reason)
    {
        if (clicked is not null) Raise(tray.Menu, "OnItemClicked", new ToolStripItemClickedEventArgs(clicked));
        var closing = new ToolStripDropDownClosingEventArgs(reason);
        Raise(tray.Menu, "OnClosing", closing);
        return !closing.Cancel;
    }

    [TestMethod]
    public void ArabicIsRightToLeftAndMirrored()
    {
        using var tray = new TrayMenu(Arabic.Text);
        tray.Apply(State());
        Lay(tray.Menu);
        Assert.AreEqual(RightToLeft.Yes, tray.Menu.RightToLeft);
        Assert.AreEqual(RightToLeft.Yes, tray.PauseList.RightToLeft);
        foreach (var row in new[] { tray.PauseRow, tray.LockNowRow, tray.SettingsRow, tray.QuitRow })
        {
            var p = row.Parts;
            Assert.IsGreaterThanOrEqualTo(p.Text.Right, p.Glyph.Left, $"{row.Text}: the glyph is on the right");
        }
        var status = tray.StatusItem.Parts;
        Assert.IsGreaterThan(status.Text.Right, status.Ring.Left + 1, "the ring is on the right");

        using var english = new TrayMenu(English.Text);
        english.Apply(State());
        Lay(english.Menu);
        Assert.IsLessThanOrEqualTo(english.PauseRow.Parts.Text.Left, english.PauseRow.Parts.Glyph.Right);
        Assert.IsLessThan(english.StatusItem.Parts.Text.Left, english.StatusItem.Parts.Ring.Right);
    }

    // The menu is the sketch's 252 px with every row and the status box across it; wider when the
    // words need it; at 150% one and a half times as wide.
    [TestMethod]
    public void TheMenuIsTheSketchsWidthWithRowsAcrossIt()
    {
        using var tray = new TrayMenu(English.Text);
        tray.Apply(State());
        Lay(tray.Menu);
        Assert.AreEqual(252, tray.Menu.Width);
        Assert.IsTrue(Shown(tray).OfType<ToolStripMenuItem>().All(i => i.Width == 251), "rows span the menu");
        Assert.AreEqual(32, tray.PauseRow.Height);
        Assert.AreEqual(9, tray.Line.Height);
        Assert.IsGreaterThanOrEqualTo(188, tray.PauseList.MinimumSize.Width);
        Lay(tray.PauseList);
        Assert.IsTrue(tray.PauseChoices.OfType<ToolStripMenuItem>().All(i => i.Width >= 187), "the pause rows span their list");

        tray.Dpi = 144;
        Lay(tray.Menu);
        Assert.AreEqual(378, tray.Menu.Width);
        Assert.AreEqual(48, tray.PauseRow.Height);

        // A very long app name is cut in the box; the menu does not grow past its limit.
        using var longName = new TrayMenu(English.Text);
        longName.Apply(State(new LockStatus
        {
            Kind = LockState.Never,
            Time = LockTime.Compute(30, [new ActiveEffect(Effect.Never, EffectSource.ForApp(new string('W', 200)))]),
        }));
        Lay(longName.Menu);
        Assert.IsLessThanOrEqualTo(420, longName.Menu.Width);
        Assert.IsGreaterThan(longName.StatusItem.Parts.Text.Width, TrayRow.Measure(longName.StatusItem.Headline, longName.StatusItem.Metrics.Headline), "the headline is cut to fit");
    }

    // The status follows the source once a second while the menu is open, and only then.
    [TestMethod]
    public void TheStatusRefreshesFromItsSourceWhileOpen()
    {
        using var tray = new TrayMenu(English.Text);
        double left = 28;
        bool paused = false;
        int asked = 0;
        tray.StateSource = () =>
        {
            asked++;
            return State(On(left), paused);
        };
        Raise(tray.Menu, "OnOpening", new System.ComponentModel.CancelEventArgs());
        Assert.AreEqual(1, asked, "asked as it opens");
        Assert.AreEqual("On · locks in 28 min", tray.StatusItem.Headline);
        Assert.IsFalse(Refreshing(tray), "not before it has opened");
        Raise(tray.Menu, "OnOpened", EventArgs.Empty);
        Assert.IsTrue(Refreshing(tray));
        left = 27;
        paused = true;
        Tick(tray);
        Assert.AreEqual(2, asked);
        Assert.AreEqual("On · locks in 27 min", tray.StatusItem.Headline);
        Assert.IsTrue(tray.TurnBackOnRow.Available, "Turn back on as soon as the flag says paused");
        Raise(tray.Menu, "OnClosed", new ToolStripDropDownClosedEventArgs(ToolStripDropDownCloseReason.AppClicked));
        Assert.IsFalse(Refreshing(tray), "stopped once closed");
    }

    // The pause list opens toward the middle of the screen the menu is on.
    [TestMethod]
    public void ThePauseListOpensTowardTheMiddleOfTheScreen()
    {
        var screen = new Rectangle(0, 0, 1920, 1032);
        Assert.AreEqual(ToolStripDropDownDirection.Left, TrayMenu.PauseDirection(new Rectangle(1600, 700, 252, 300), screen), "tray at the bottom right");
        Assert.AreEqual(ToolStripDropDownDirection.Right, TrayMenu.PauseDirection(new Rectangle(40, 700, 252, 300), screen), "tray at the bottom left, as in a right-to-left Windows");
        Assert.AreEqual(ToolStripDropDownDirection.Left, TrayMenu.PauseDirection(new Rectangle(1600, 10, 252, 300), new Rectangle(0, 0, 1860, 1080)), "taskbar on the right");
        Assert.AreEqual(ToolStripDropDownDirection.Right, TrayMenu.PauseDirection(new Rectangle(70, 10, 252, 300), new Rectangle(60, 0, 1860, 1080)), "taskbar on the left");
        Assert.AreEqual(ToolStripDropDownDirection.Left, TrayMenu.PauseDirection(new Rectangle(3500, 700, 252, 300), new Rectangle(1920, 0, 1920, 1032)), "on a second screen to the right");
        Assert.AreEqual(ToolStripDropDownDirection.Right, TrayMenu.PauseDirection(new Rectangle(-1800, 700, 252, 300), new Rectangle(-1920, 0, 1920, 1032)), "on a second screen to the left");
    }

    // The rows' look: the accent glyph (Quit's gray), the gray arrow and shortcut, and the system's
    // colors in high contrast.
    [TestMethod]
    public void TheRowsTakeTheSketchsColorsAndHighContrasts()
    {
        using var tray = new TrayMenu(English.Text);
        try
        {
            MenuLook.HighContrastForTests = false;
            Assert.AreEqual(Theme.Current.Accent, TrayMenuLook.GlyphColor(tray.LockNowRow));
            Assert.AreEqual(Theme.Current.Subtle, TrayMenuLook.GlyphColor(tray.QuitRow));
            Assert.AreEqual(Theme.Current.Subtle, TrayMenuLook.TrailColor(tray.LockNowRow));
            Assert.AreEqual(Theme.Current.Card, TrayMenuLook.Back);
            MenuLook.HighContrastForTests = true;
            Assert.AreEqual(SystemColors.MenuText, TrayMenuLook.GlyphColor(tray.LockNowRow));
            Assert.AreEqual(SystemColors.GrayText, TrayMenuLook.GlyphColor(tray.QuitRow));
            Assert.AreEqual(SystemColors.Menu, TrayMenuLook.Back);
            Assert.AreEqual(SystemColors.Highlight, TrayMenuLook.Hover);
        }
        finally
        {
            MenuLook.HighContrastForTests = null;
        }
    }

    // The row the keyboard is on has a 3 px accent bar at its start, the right in Arabic, besides the
    // soft fill, which alone is too faint to find; high contrast fills it with the selection color
    // instead, with no bar (I11). The pause list beside the menu is a MenuLook list and does the same.
    [TestMethod]
    public void TheChosenRowHasAnAccentBarAtItsStart()
    {
        try
        {
            foreach (bool dark in new[] { false, true })
            {
                Theme.UseForShots(dark);
                foreach (var text in new[] { English.Text, Arabic.Text })
                {
                    bool rtl = text == Arabic.Text;
                    string name = $"{(rtl ? "ar" : "en")} {(dark ? "dark" : "light")}";
                    foreach (bool hc in new[] { false, true })
                    {
                        MenuLook.HighContrastForTests = hc;
                        using var tray = new TrayMenu(text);
                        tray.Apply(State());
                        Lay(tray.Menu);
                        var row = tray.LockNowRow;
                        row.Select();
                        Assert.IsTrue(row.Selected);
                        using var picture = new Bitmap(tray.Menu.Width, tray.Menu.Height);
                        tray.Menu.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                        var fill = row.Parts.Fill;
                        fill.Offset(row.Bounds.Location);
                        int x = rtl ? fill.Right - 2 : fill.Left + 1;
                        int y = fill.Top + fill.Height / 2;
                        var expected = hc ? SystemColors.Highlight : Theme.Current.Accent;
                        Assert.AreEqual(expected.ToArgb(), picture.GetPixel(x, y).ToArgb(), $"{name}{(hc ? ", high contrast" : "")}: the start of Lock now");
                        var inside = picture.GetPixel(rtl ? fill.Right - 12 : fill.Left + 12, fill.Top + 2);
                        Assert.AreEqual((hc ? SystemColors.Highlight : TrayMenuLook.Hover).ToArgb(), inside.ToArgb(), $"{name}: the soft fill past the bar");
                    }
                }
            }
        }
        finally
        {
            MenuLook.HighContrastForTests = null;
            Theme.UseForShots(false);
        }
    }

    [TestMethod]
    public void AListsChosenRowHasAnAccentBarAtItsStart()
    {
        try
        {
            Theme.UseForShots(false);
            MenuLook.HighContrastForTests = false;
            foreach (var direction in new[] { RightToLeft.No, RightToLeft.Yes })
            {
                using var menu = MenuLook.NewMenu(direction);
                menu.Items.Add(MenuLook.Item("15 minutes"));
                menu.Items.Add(MenuLook.Item("30 minutes"));
                Lay(menu);
                var item = menu.Items[1];
                item.Select();
                using var picture = new Bitmap(menu.Width, menu.Height);
                menu.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                var bounds = item.Bounds;
                int x = direction == RightToLeft.Yes ? bounds.Right - 2 - 2 : bounds.Left + 2 + 1;
                Assert.AreEqual(Theme.Current.Accent.ToArgb(), picture.GetPixel(x, bounds.Top + bounds.Height / 2).ToArgb(), direction.ToString());
            }
        }
        finally
        {
            MenuLook.HighContrastForTests = null;
        }
    }

    // TrayMenuLook passes MenuLook what it does not draw itself, so every part MenuLook draws must be
    // handled here too; a part MenuLook starts drawing later would otherwise be drawn the plain way.
    [TestMethod]
    public void TheTrayLookHandlesEveryPartMenuLookDraws()
    {
        const BindingFlags declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var menuLook = typeof(MenuLook).GetMethods(declared).Where(m => m.GetBaseDefinition().DeclaringType == typeof(ToolStripRenderer)).Select(m => m.Name).ToHashSet();
        var trayLook = typeof(TrayMenuLook).GetMethods(declared).Where(m => m.GetBaseDefinition().DeclaringType == typeof(ToolStripRenderer)).Select(m => m.Name).ToHashSet();
        Assert.IsNotEmpty(menuLook);
        CollectionAssert.IsSubsetOf(menuLook.ToList(), trayLook.ToList(), $"missing: {string.Join(", ", menuLook.Except(trayLook))}");
    }

    // Drawn without being shown, at 100% and 150%, light and dark, in both languages: the status box
    // in its colors and nothing thrown.
    [TestMethod]
    public void ItDrawsWithoutBeingShown()
    {
        try
        {
            foreach (bool dark in new[] { false, true })
            {
                Theme.UseForShots(dark);
                foreach (var text in new[] { English.Text, Arabic.Text })
                {
                    foreach (int dpi in new[] { 96, 144 })
                    {
                        using var tray = new TrayMenu(text) { Dpi = dpi };
                        tray.Apply(State());
                        tray.PauseRow.Select();
                        Lay(tray.Menu);
                        using var picture = new Bitmap(tray.Menu.Width, tray.Menu.Height);
                        tray.Menu.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                        var box = tray.StatusItem.Parts.Box;
                        box.Offset(tray.StatusItem.Bounds.Location);
                        var inside = picture.GetPixel(box.Left + box.Width / 2, box.Top + 3);
                        Assert.AreEqual(Theme.Current.Hero.ToArgb(), inside.ToArgb(), $"{(dark ? "dark" : "light")} {dpi}: the status box is filled");
                        Assert.IsFalse(tray.Menu.Visible);
                    }
                }
            }
        }
        finally
        {
            Theme.Choose(ThemeChoice.System, TextSize.System);
        }
    }

    // The pause list is shown at the size it has, without a layout first; that size is the sketch's
    // 188 px at each scaling, in both languages, after a change of language and of scaling, and the
    // width its words need with Large text. Not laid out here, as Windows does not lay it out again
    // before showing it.
    [TestMethod]
    public void ThePauseListIsItsOwnWidthWithoutALayout()
    {
        try
        {
            foreach (float textScale in new[] { 1f, 1.2f })
            {
                Theme.UseForShots(false, textScale);
                foreach (var text in new[] { English.Text, Arabic.Text })
                {
                    foreach (int dpi in new[] { 96, 120, 144, 168, 192 })
                    {
                        using var tray = new TrayMenu(text) { Dpi = dpi };
                        tray.Apply(State());
                        var list = tray.PauseList;
                        string name = $"{(text.RightToLeft ? "ar" : "en")} {dpi} text x{textScale}";
                        Assert.AreEqual(list.GetPreferredSize(Size.Empty).Width, list.Width, $"{name}: shown at the width it would like");
                        int sketch = new TrayMetrics(dpi).PauseWidth;
                        if (textScale == 1f) Assert.AreEqual(sketch, list.Width, $"{name}: the sketch's 188 px");
                        else Assert.IsGreaterThanOrEqualTo(sketch, list.Width, name);
                    }
                }
            }

            Theme.UseForShots(false);
            using var switched = new TrayMenu(English.Text);
            switched.Relabel(Arabic.Text);
            switched.Relabel(English.Text);
            switched.Dpi = 144;
            switched.Dpi = 96;
            Assert.AreEqual(188, switched.PauseList.Width, "after a change of language and of scaling");
        }
        finally
        {
            Theme.Choose(ThemeChoice.System, TextSize.System);
        }
    }

    // The pause list goes beside the menu on the side it opens to, 5 px from the menu's edge as in the
    // sketch (scaled), with its first choice level with Pause, never over the menu, and on the screen.
    [TestMethod]
    public void ThePauseListSitsBesideTheMenuNotOverIt()
    {
        var work = new Rectangle(0, 0, 1920, 1032);
        var menu = new Rectangle(1660, 816, 252, 206);
        var list = new Size(188, 138);
        var left = TrayMenu.PauseListBounds(menu, 873, list, 3, toLeft: true, work, 5);
        Assert.AreEqual(new Rectangle(1467, 870, 188, 138), left, "tray at the bottom right: to the menu's left");
        Assert.AreEqual(5, menu.Left - left.Right);

        var onLeft = new Rectangle(10, 816, 252, 206);
        var right = TrayMenu.PauseListBounds(onLeft, 873, list, 3, toLeft: false, work, 5);
        Assert.AreEqual(onLeft.Right + 5, right.Left, "tray at the bottom left: to the menu's right");

        var low = TrayMenu.PauseListBounds(menu with { Y = 900 }, 1000, list, 3, toLeft: true, work, 5);
        Assert.AreEqual(work.Bottom, low.Bottom, "kept above the taskbar");
        Assert.AreEqual(8, new TrayMetrics(144).PauseGap, "the gap scales");

        // The real list, placed as it opens, from the size it has then: the sketch's width, beside
        // the menu with the gap, its first choice level with Pause.
        foreach (var text in new[] { English.Text, Arabic.Text })
        {
            using var tray = new TrayMenu(text);
            tray.Apply(State());
            Lay(tray.Menu);
            var screen = Screen.PrimaryScreen!.WorkingArea;
            // High enough on the screen for the whole list to sit level with Pause.
            tray.Menu.Location = new Point(screen.Right - tray.Menu.Width - 8, screen.Top + 8);
            // As WinForms opens it: DropDownOpening, the list's location set, then its Opening.
            typeof(ToolStripDropDownItem).GetMethod("OnDropDownShow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tray.PauseRow, [EventArgs.Empty]);
            tray.PauseList.Location = Point.Empty;
            Raise(tray.PauseList, "OnOpening", new System.ComponentModel.CancelEventArgs());
            var shown = tray.PauseList.Bounds;
            var at = tray.Menu.Bounds;
            string name = text.RightToLeft ? "ar" : "en";
            Assert.AreEqual(188, shown.Width, $"{name}: the sketch's width");
            Assert.AreEqual(at.Left - 5, shown.Right, $"{name}: 5 px left of the menu, not over it ({shown} beside {at})");
            var first = tray.PauseChoices.First(i => i.Available).Bounds.Top;
            Assert.AreEqual(at.Top + tray.PauseRow.Bounds.Top, shown.Top + first, $"{name}: the first choice is level with Pause");
        }
    }

    // The status box takes the settings box's tones (D20): green as drawn and while something keeps
    // it from locking, amber while the countdown runs, gray while paused, away or locked; a dry run
    // as what it shows; the system's menu colors in high contrast.
    [TestMethod]
    public void TheStatusBoxTakesTheToneOfTheState()
    {
        var plain = LockTime.Plain(30);
        var tones = new (LockState Kind, Tone Tone)[]
        {
            (LockState.Starting, Tone.On), (LockState.On, Tone.On), (LockState.Never, Tone.On), (LockState.LockingOff, Tone.On),
            (LockState.Countdown, Tone.Busy), (LockState.Paused, Tone.Off), (LockState.Away, Tone.Off), (LockState.SessionLocked, Tone.Off),
        };
        Assert.HasCount(Enum.GetValues<LockState>().Length, tones, "every state has a tone");
        foreach (var (kind, tone) in tones)
        {
            Assert.AreEqual(tone, TrayStatusItem.ToneOf(new LockStatus { Kind = kind, Time = plain }), kind.ToString());
            Assert.AreEqual(tone, TrayStatusItem.ToneOf(new LockStatus { Kind = kind, Time = plain, DryRun = true }), $"{kind}, dry run");
        }

        var countdown = new LockStatus { Kind = LockState.Countdown, Time = plain, IdleSeconds = 30 * 60 - 20, SecondsLeft = 20, Today = new TodayCounts(2, TimeSpan.FromMinutes(80)) };
        try
        {
            MenuLook.HighContrastForTests = false;
            foreach (bool dark in new[] { false, true })
            {
                Theme.UseForShots(dark);
                var t = Theme.Current;
                foreach (var (status, paused, fill, edge) in new[] { (On(), false, t.Hero, t.HeroEdge), (countdown, false, t.WarningSoft, t.Warning), (PausedStatus(), true, t.Window, t.Edge) })
                {
                    using var tray = new TrayMenu(English.Text);
                    tray.Apply(State(status, paused));
                    Assert.AreEqual((fill, edge), TrayStatusItem.Colors(tray.StatusItem.Tone), $"{status.Kind} {(dark ? "dark" : "light")}");
                    Lay(tray.Menu);
                    using var picture = new Bitmap(tray.Menu.Width, tray.Menu.Height);
                    tray.Menu.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                    var box = tray.StatusItem.Parts.Box;
                    box.Offset(tray.StatusItem.Bounds.Location);
                    Assert.AreEqual(fill.ToArgb(), picture.GetPixel(box.Left + box.Width / 2, box.Top + 3).ToArgb(), $"{status.Kind} {(dark ? "dark" : "light")}: the box's fill");
                }
            }
            MenuLook.HighContrastForTests = true;
            Assert.AreEqual((SystemColors.Menu, SystemColors.MenuText), TrayStatusItem.Colors(Tone.Busy));
            Assert.AreEqual((SystemColors.Menu, SystemColors.MenuText), TrayStatusItem.Colors(Tone.Off));
        }
        finally
        {
            MenuLook.HighContrastForTests = null;
            Theme.Choose(ThemeChoice.System, TextSize.System);
        }
    }

    // The marks Arabic sets under its letters, such as the two under the last letter of "seconds"
    // in "locks in 9 seconds", and the tails of letters such as p and y, fall inside the status box's
    // first line instead of being cut at its foot (I5).
    [TestMethod]
    public void TheStatusLineKeepsTheMarksUnderItsWords()
    {
        var countdown = new LockStatus { Kind = LockState.Countdown, Time = LockTime.Plain(30), IdleSeconds = 1791, SecondsLeft = 9, Today = new TodayCounts(2, TimeSpan.FromMinutes(80)) };
        var skype = new LockStatus { Kind = LockState.Never, Time = LockTime.Compute(30, [new ActiveEffect(Effect.Never, EffectSource.ForApp("Skype"))]), Today = new TodayCounts(2, TimeSpan.FromMinutes(80)) };
        try
        {
            foreach (float scale in new[] { 1f, 1.5f, 2.25f })
            {
                Theme.UseForShots(false, scale);
                foreach (int dpi in new[] { 96, 120, 144, 192 })
                {
                    foreach (var (text, status) in new[] { (English.Text, countdown), (English.Text, skype), (Arabic.Text, countdown), (Arabic.Text, skype) })
                    {
                        using var tray = new TrayMenu(text) { Dpi = dpi };
                        tray.Apply(State(status));
                        Lay(tray.Menu);
                        var item = tray.StatusItem;
                        var (headline, label, _) = item.Lines();
                        bool rtl = item.RightToLeft == RightToLeft.Yes;
                        Assert.AreEqual(text == Arabic.Text, rtl);
                        string name = $"{(rtl ? "ar" : "en")} at {dpi} dpi and {scale:P0} text";
                        int ink = InkBottom(item.Headline, item.Metrics.Headline, rtl);
                        Assert.IsLessThanOrEqualTo(headline.Height, ink, $"{name}, {item.Headline}: the words' lowest mark is inside the line");
                        Assert.IsGreaterThanOrEqualTo(headline.Bottom, label.Top, $"{name}: the time away is under the headline's marks");
                        Assert.IsLessThanOrEqualTo(item.Height, item.Parts.Box.Bottom, $"{name}: the box holds both lines");
                    }
                }
            }
        }
        finally
        {
            Theme.UseForShots(false, 1f);
        }
    }

    // How far below the top of its line a text's lowest drawn mark reaches.
    internal static int InkBottom(string words, Font font, bool rtl)
    {
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.Top | TextFormatFlags.NoClipping;
        var size = TextRenderer.MeasureText(words, font, Size.Empty, flags);
        using var picture = new Bitmap(size.Width + 20, size.Height * 2);
        using (var g = Graphics.FromImage(picture))
        {
            g.Clear(Color.White);
            TextRenderer.DrawText(g, words, font, new Rectangle(0, 0, picture.Width, picture.Height), Color.Black,
                flags | (rtl ? TextFormatFlags.RightToLeft | TextFormatFlags.Right : TextFormatFlags.Left));
        }
        for (int y = picture.Height - 1; y >= 0; y--)
        {
            for (int x = 0; x < picture.Width; x++)
            {
                if (picture.GetPixel(x, y).R < 192) return y + 1;
            }
        }
        return 0;
    }

    static void Lay(ToolStripDropDown menu)
    {
        menu.PerformLayout();
        var preferred = menu.GetPreferredSize(Size.Empty);
        menu.Size = new Size(Math.Max(preferred.Width, menu.MinimumSize.Width), preferred.Height);
        menu.PerformLayout();
    }

    static void Raise(ToolStripDropDown menu, string method, EventArgs args) =>
        typeof(ToolStripDropDown).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic, [args.GetType()])!.Invoke(menu, [args]);

    static System.Windows.Forms.Timer Timer(TrayMenu tray) =>
        (System.Windows.Forms.Timer)typeof(TrayMenu).GetField("refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tray)!;

    static bool Refreshing(TrayMenu tray) => Timer(tray).Enabled;

    static void Tick(TrayMenu tray) =>
        typeof(System.Windows.Forms.Timer).GetMethod("OnTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Timer(tray), [EventArgs.Empty]);
}
