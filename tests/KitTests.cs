using System.Reflection;

namespace SteppedOut.Tests;

// What the kit controls do with keys, values and typing, without showing anything: no window is made
// and nothing takes the keyboard. Keys go in through the controls' own key handlers. One at a time:
// the controls fill the shared font cache, which is not made for several threads.
[TestClass]
[DoNotParallelize]
public class KitTests
{
    static void Press(Control control, Keys key) =>
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(control, [new KeyEventArgs(key)]);

    static TextBox BoxOf(MinutesField field) => field.Controls.OfType<TextBox>().Single();

    // What the old settings window counts on: a value not in the list reads back as nothing, so it
    // can add the value as an extra item; and setting a value tells nobody.
    [TestMethod]
    public void AValueNotInTheListReadsBackAsNothing()
    {
        var list = new DropDown().Add("15 minutes", 15.0).Add("30 minutes", 30.0);
        int changed = 0;
        list.SelectedChanged += (_, _) => changed++;
        list.Value = 30.0;
        Assert.AreEqual(30.0, list.Value);
        Assert.AreEqual("30 minutes", list.Text);
        list.Value = 45.0;
        Assert.IsNull(list.Value);
        Assert.AreEqual("", list.Text);
        list.Value = 15;
        Assert.IsNull(list.Value, "an int 15 is not the double 15 in the list");
        list.Add("45 minutes", 45.0);
        list.Value = 45.0;
        Assert.AreEqual("45 minutes", list.Text);
        Assert.AreEqual(0, changed, "setting a value raises nothing");
    }

    [TestMethod]
    public void ArrowKeysChooseWithoutCountingAsPicked()
    {
        var list = new DropDown().Add("Off", 0).Add("10 seconds", 10).Add("30 seconds", 30);
        list.Value = 10;
        int changed = 0, picked = 0;
        list.SelectedChanged += (_, _) => changed++;
        list.Picked += (_, _) => picked++;
        Press(list, Keys.Down);
        Assert.AreEqual(30, list.Value);
        Press(list, Keys.Down);
        Assert.AreEqual(30, list.Value, "Down stops at the last");
        Press(list, Keys.Up);
        Press(list, Keys.Up);
        Press(list, Keys.Up);
        Assert.AreEqual(0, list.Value, "Up stops at the first");
        Assert.AreEqual(3, changed);
        Assert.AreEqual(0, picked);
    }

    [TestMethod]
    public void AListIsAsWideAsItsLongestChoice()
    {
        var list = new DropDown().Add("1 min", 1);
        int narrow = list.Width;
        list.Add("A much longer choice than any other in the list", 2);
        Assert.IsGreaterThan(narrow, list.Width);
    }

    [TestMethod]
    public void AMinutesBoxTakesWholeMinutesFromOneToADay()
    {
        var text = Lang.Current;
        var field = new MinutesField("Lock after, in minutes") { Minutes = 15 };
        var box = BoxOf(field);
        int changed = 0;
        field.MinutesChanged += (_, _) => changed++;
        Assert.AreEqual("15", box.Text);
        Assert.AreEqual(text.MinutesRange, field.Hint);
        Assert.AreEqual(0, changed, "setting the time raises nothing");
        box.Text = "45";
        Assert.AreEqual(45, field.Minutes);
        Assert.AreEqual(1, changed);
        box.Text = "";
        Assert.AreEqual(45, field.Minutes);
        Assert.AreEqual(string.Format(text.MinutesNotANumber, text.SettingDuration(45)), field.Hint);
        box.Text = "9999";
        Assert.AreEqual(1440, field.Minutes);
        Assert.AreEqual(string.Format(text.MinutesClamped, text.SettingDuration(1440)), field.Hint);
        Assert.AreEqual("Lock after, in minutes", box.AccessibilityObject.Name);
    }

    // At a large text size the box's text is taller than the 34 px field; the field grows round it
    // rather than cut the number off (I21).
    [TestMethod]
    public void AMinutesBoxGrowsWithItsText()
    {
        try
        {
            foreach (float scale in new[] { 1f, 1.5f, 2.25f })
            {
                Theme.UseForShots(false, scale);
                foreach (var field in new[] { new MinutesField("Lock after, in minutes"), new MinutesField("Warn me, in seconds", TimeUnit.Seconds, 5, 300) })
                {
                    var box = BoxOf(field);
                    field.Size = field.GetPreferredSize(Size.Empty);
                    field.PerformLayout();
                    Assert.IsGreaterThanOrEqualTo(34, field.Height, $"at {scale}");
                    Assert.IsTrue(box.Top >= 0 && box.Bottom <= field.Height, $"at {scale} the box {box.Bounds} is inside the field {field.Size}");
                    field.Dispose();
                }
            }
        }
        finally
        {
            Theme.UseForShots(false, 1f);
        }
    }

    // The settings' small buttons are the sketch's: their words in the face they are drawn in, 10 px
    // each side and 26 px tall, as Try it and Open folder are there. Add app's icon and words sit in
    // the middle, as far from one end as from the other. The welcome's buttons keep their size (I22).
    [TestMethod]
    public void ASmallButtonIsItsWordsAndTenPixelsEachSide()
    {
        const TextFormatFlags tight = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        try
        {
            Theme.UseForShots(false);
            foreach (string language in new[] { "en", "ar" })
            {
                Lang.Use(language);
                var v = Lang.Current.V1;
                foreach (string words in new[] { v.TryIt, v.OpenFolder, v.Show, v.Choose })
                {
                    using var button = new KitButton(words) { Small = true };
                    int width = TextRenderer.MeasureText(words, Theme.Body(9f, button), Size.Empty, tight).Width;
                    Assert.AreEqual(width + button.LogicalToDeviceUnits(20), button.Width, $"{language}: {words}");
                    Assert.AreEqual(button.LogicalToDeviceUnits(26), button.Height, $"{language}: {words}");
                }

                using var add = new KitButton(v.AddApp) { Small = true, Glyph = Glyphs.Plus, RightToLeft = language == "ar" ? RightToLeft.Yes : RightToLeft.No };
                using var picture = new Bitmap(add.Width, add.Height);
                add.DrawToBitmap(picture, new Rectangle(Point.Empty, picture.Size));
                int first = -1, last = -1;
                for (int x = 3; x < picture.Width - 3; x++)
                {
                    for (int y = picture.Height / 4; y < picture.Height * 3 / 4; y++)
                    {
                        if (picture.GetPixel(x, y).GetBrightness() >= 0.5f) continue;
                        if (first < 0) first = x;
                        last = x;
                        break;
                    }
                }
                int before = first, after = picture.Width - 1 - last;
                Assert.IsLessThanOrEqualTo(2, Math.Abs(before - after), $"{language}: Add app's icon and words are in the middle ({before} and {after} px from its ends)");
            }
            using var done = new KitButton("Done", primary: true);
            Assert.AreEqual(TextRenderer.MeasureText("Done", Theme.Strong(10f, done)).Width + done.LogicalToDeviceUnits(36), done.Width, "the welcome's buttons keep their size");
        }
        finally
        {
            Lang.Use("en");
        }
    }

    // A row's icon grows with the text size inside a box that grows with it, so it is never cut; at
    // the sizes where it fits the look's box, the box is the look's (I30).
    [TestMethod]
    public void ARowsIconIsNeverCutAtAnyTextSize()
    {
        try
        {
            foreach (float scale in new[] { 1f, 1.2f, 1.5f, 2.25f })
            {
                Theme.UseForShots(false, scale);
                foreach (var (look, size) in new[] { (RowLook.Flat, 24), (RowLook.LookC, 32) })
                {
                    using var row = new SettingRow(Glyphs.Clock, "Breaks", "Short breaks away from the screen", new Toggle()) { Look = look, Width = 600 };
                    row.Height = row.GetPreferredSize(new Size(600, 0)).Height;
                    row.PerformLayout();
                    var box = row.GlyphBox;
                    var icon = row.GlyphSize;
                    int room = look == RowLook.LookC ? row.LogicalToDeviceUnits(8) : 0;
                    string what = $"{look} at {scale}: icon {icon} in {box}";
                    Assert.IsLessThanOrEqualTo(box.Width, icon.Width + room, what);
                    Assert.IsLessThanOrEqualTo(box.Height, icon.Height + room, what);
                    Assert.IsTrue(box.Top >= 0 && box.Bottom <= row.Height, $"{what}: inside the row {row.Size}");
                    Assert.IsGreaterThanOrEqualTo(box.Right, TitleOf(row).Left, $"{what}: the title starts after the icon");
                    if (scale <= 1.2f) Assert.AreEqual(row.LogicalToDeviceUnits(size), box.Width, $"{what}: the look's own box while the icon fits it");
                }
            }
        }
        finally
        {
            Theme.UseForShots(false, 1f);
        }
    }

    // A list with headings over groups and a Custom… row at the end, like the battery menu.
    static DropDown Grouped(out CustomEntry custom)
    {
        custom = new CustomEntry
        {
            Name = "Always lock after",
            Value = n => "always " + n,
            Label = n => "Always lock after " + n,
            Number = v => v is string s && s.StartsWith("always ") ? int.Parse(s[7..]) : null,
        };
        return new DropDown()
            .Add("Lock as usual", "none")
            .AddHeading("Lock sooner")
            .Add("25% sooner", "sooner 25")
            .Add("50% sooner", "sooner 50")
            .AddHeading("Fixed time")
            .Add("Always lock after 5 minutes", "always 5")
            .Add("Always lock after 1 hour", "always 60")
            .AddCustom("Custom…", custom);
    }

    [TestMethod]
    public void TheKeysPassOverHeadingsAndCustomRows()
    {
        var list = Grouped(out _);
        CollectionAssert.AreEqual(new[] { "Lock as usual", "25% sooner", "50% sooner", "Always lock after 5 minutes", "Always lock after 1 hour" }, list.Choices.ToArray());
        list.Value = null;
        Assert.IsNull(list.Value, "nothing chosen is not a heading");
        list.Value = "none";
        var seen = new List<object?>();
        list.SelectedChanged += (_, _) => seen.Add(list.Value);
        for (int i = 0; i < 6; i++) Press(list, Keys.Down);
        CollectionAssert.AreEqual(new object[] { "sooner 25", "sooner 50", "always 5", "always 60" }, seen);
        Press(list, Keys.Up);
        Assert.AreEqual("always 5", list.Value);
    }

    [TestMethod]
    public void TheOpenMenuHasHeadingsTheKeysPassOverAndTheChoiceInTheAccent()
    {
        var list = Grouped(out _);
        list.Value = "sooner 50";
        using var menu = list.BuildMenu();
        menu.PerformLayout();
        var rows = menu.Items.Cast<ToolStripItem>().Where(r => r.Available).ToList();
        CollectionAssert.AreEqual(list.Rows.ToArray(), rows.Select(r => r.Text).ToArray());
        Assert.IsFalse(rows[1].CanSelect, "a heading is never chosen");
        Assert.AreSame(rows[2], menu.GetNextItem(rows[0], ArrowDirection.Down));
        Assert.AreSame(rows[5], menu.GetNextItem(rows[3], ArrowDirection.Down));
        var chosen = rows.OfType<ToolStripMenuItem>().Single(r => r.Checked);
        Assert.AreEqual("50% sooner", chosen.Text);
        Assert.AreEqual("Lock sooner", chosen.AccessibleDescription, "a choice says its heading to a screen reader");
        Assert.IsFalse(menu.ShowCheckMargin);
        Assert.IsGreaterThanOrEqualTo(list.Width, menu.Width);
    }

    [TestMethod]
    public void RelabelChangesTheWordsAndTellsNobody()
    {
        var list = Grouped(out _);
        list.Value = "sooner 50";
        int changed = 0;
        list.SelectedChanged += (_, _) => changed++;
        list.Relabel("sooner 50", "50% sooner (15 min)");
        list.Relabel("sooner 25", "25% sooner (23 min)");
        Assert.AreEqual("50% sooner (15 min)", list.Text);
        Assert.AreEqual("sooner 50", list.Value);
        Assert.AreEqual("25% sooner (23 min)", list.Choices[1]);
        Assert.AreEqual(0, changed);
        var value = (Automation.IValueProvider)((Automation.IRawElementProviderSimple)list.AutomationProvider!).GetPatternProvider(Automation.ValuePattern)!;
        Assert.AreEqual("50% sooner (15 min)", value.Value);
    }

    // A value from the file that is not in the list shows as itself only when the list says how;
    // otherwise it stays unchosen, as the old settings window needs.
    [TestMethod]
    public void AValueNotInTheListShowsAsItselfWhenAsked()
    {
        var list = Grouped(out _);
        list.Value = "always 45";
        Assert.IsNull(list.Value);
        list.Unlisted = v => v is string s && s.StartsWith("always ") ? "Always lock after " + s[7..] + " minutes" : null;
        list.Value = "always 45";
        Assert.AreEqual("always 45", list.Value);
        Assert.AreEqual("Always lock after 45 minutes", list.Text);
        list.Value = "always 50";
        Assert.AreEqual("Always lock after 50 minutes", list.Text);
        Assert.HasCount(6, list.Choices, "the new one takes the old one's place");
        list.Value = "sooner 99";
        Assert.IsNull(list.Value, "a value the list cannot say stays unchosen");
        list.ShowAs("odd", "Something else");
        Assert.AreEqual("Something else", list.Text);
        Assert.AreEqual("odd", list.Value);
    }

    [TestMethod]
    public void ACustomRowOpensABoxAndItsNumberBecomesTheValue()
    {
        var list = Grouped(out _);
        list.Value = "always 60";
        CustomBox? box = null;
        list.Presenter = b => box = b;
        int changed = 0, picked = 0;
        list.SelectedChanged += (_, _) => changed++;
        list.Picked += (_, _) => picked++;

        list.OpenCustom(0);
        Assert.IsNotNull(box);
        Assert.AreSame(box, list.Flyout);
        Assert.AreEqual("60", BoxOf(box.Field).Text, "the box opens on the number in the value");
        Assert.AreEqual("Always lock after, in minutes", BoxOf(box.Field).AccessibilityObject.Name);
        BoxOf(box.Field).Text = "45";
        Assert.IsTrue(box.Key(Keys.Enter));
        Assert.AreEqual("always 45", list.Value);
        Assert.AreEqual("Always lock after 45", list.Text);
        Assert.AreEqual(1, changed);
        Assert.AreEqual(1, picked);
        Assert.IsNull(list.Flyout, "the box is gone");
        CollectionAssert.AreEqual(new[] { "Lock as usual", "Lock sooner", "25% sooner", "50% sooner", "Fixed time", "Always lock after 5 minutes", "Always lock after 1 hour", "Always lock after 45", "Custom…" },
            list.Rows.ToArray(), "the typed time sits just above Custom…");

        // A number that is one of the choices chooses it; nothing is added.
        list.OpenCustom(0);
        Assert.AreEqual("45", BoxOf(box.Field).Text);
        BoxOf(box.Field).Text = "5";
        box.Key(Keys.Enter);
        Assert.AreEqual("always 5", list.Value);
        Assert.HasCount(6, list.Choices);
    }

    [TestMethod]
    public void EscapeLeavesTheListAsItWasAndClickingAwayUsesTheNumber()
    {
        var list = Grouped(out _);
        list.Value = "none";
        CustomBox? box = null;
        list.Presenter = b => box = b;
        int changed = 0;
        list.SelectedChanged += (_, _) => changed++;

        list.OpenCustom(0);
        Assert.AreEqual("15", BoxOf(box!.Field).Text, "a value of another kind opens on the row's own start");
        BoxOf(box.Field).Text = "90";
        Assert.IsTrue(box.Key(Keys.Escape));
        Assert.AreEqual("none", list.Value);
        Assert.AreEqual(0, changed);
        Assert.IsNull(list.Flyout);

        list.OpenCustom(0);
        BoxOf(box.Field).Text = "90";
        box.ClickedAway();
        Assert.AreEqual("always 90", list.Value);
        Assert.AreEqual(1, changed);
        Assert.IsFalse(box.ReturnFocus, "a click elsewhere keeps the focus where it went");
    }

    // The warning's Custom… row counts seconds, within the setting's own limits.
    [TestMethod]
    public void ACustomRowCanCountSeconds()
    {
        var v1 = Lang.Current.V1;
        var custom = new CustomEntry { Name = "Warn me before it locks", Unit = TimeUnit.Seconds, Shortest = 1, Longest = 600, Start = 15, Value = n => n, Label = n => n + " s" };
        var list = new DropDown().Add("Off", 0).Add("15 seconds", 15).AddCustom("Custom…", custom);
        list.Value = 15;
        CustomBox? box = null;
        list.Presenter = b => box = b;
        list.OpenCustom(0);
        var field = box!.Field;
        Assert.AreEqual(TimeUnit.Seconds, field.Unit);
        Assert.AreEqual(string.Format(v1.SecondsFieldName, "Warn me before it locks"), BoxOf(field).AccessibilityObject.Name);
        Assert.AreEqual(string.Format(v1.CustomRangeSeconds, 1, 600), field.Hint);
        BoxOf(field).Text = "900";
        Assert.AreEqual(600, box.Number);
        Assert.AreEqual(string.Format(v1.CustomClampedSeconds, 1, 600, Lang.Current.SecondsOption(600)), field.Hint);
        BoxOf(field).Text = "";
        Assert.AreEqual(string.Format(v1.CustomNotANumberSeconds, Lang.Current.SecondsOption(600)), field.Hint);
        BoxOf(field).Text = "٤٥";
        Assert.AreEqual(45, box.Number, "Arabic digits read as 45");
        box.Key(Keys.Enter);
        Assert.AreEqual(45, list.Value);
        Assert.AreEqual("45 s", list.Text);
    }

    [TestMethod]
    public void ARangedBoxKeepsItsOwnLimitsAndLeavesTheOldBoxAlone()
    {
        var v1 = Lang.Current.V1;
        var field = new MinutesField("Show a reminder after, in minutes", TimeUnit.Minutes, 1, 600) { Minutes = 60 };
        Assert.AreEqual(string.Format(Lang.Current.MinutesThatIs, Lang.Current.OptionDuration(60)), field.Hint);
        field.Minutes = 20;
        Assert.AreEqual(string.Format(v1.CustomRangeMinutes, 1, 600), field.Hint);
        BoxOf(field).Text = "1000";
        Assert.AreEqual(600, field.Minutes);
        Assert.AreEqual(string.Format(v1.CustomClampedMinutes, 1, 600, Lang.Current.OptionDuration(600)), field.Hint);
        var old = new MinutesField("Lock after, in minutes");
        BoxOf(old).Text = "1000";
        Assert.AreEqual(1000, old.Minutes, "the old box still takes up to 1440");
    }

    [TestMethod]
    public void ASmallListAndAFixedWidth()
    {
        var list = new DropDown().Add("Off", 0).Add("Chime", 2);
        int regular = list.Height;
        list.Small = true;
        Assert.AreEqual(list.LogicalToDeviceUnits(26), list.Height);
        Assert.IsLessThan(regular, list.Height);
        Assert.IsLessThan(list.LogicalToDeviceUnits(184), list.Width, "a small list fits its words");
        list.ButtonWidth = 240;
        Assert.AreEqual(list.LogicalToDeviceUnits(240), list.Width);
        list.Add("A choice far longer than the list is wide, so only the menu shows it all", 3);
        Assert.AreEqual(list.LogicalToDeviceUnits(240), list.Width, "the list keeps its width");
        list.ButtonHeight = 30;
        list.Small = false;
        Assert.AreEqual(list.LogicalToDeviceUnits(30), list.Height);
    }

    static TabStrip Tabs() => new TabStrip { Width = 600 }
        .Add("", "Basics", "When it locks")
        .Add("", "Sounds", "Calls and media")
        .Add("", "Apps", "Games and screens")
        .Add("", "Advanced", "Logs and startup")
        .Add("", "About", "Version");

    [TestMethod]
    public void TheTabKeysMoveWithoutChoosingAndEnterOrSpaceChooses()
    {
        var strip = Tabs();
        int changed = 0;
        strip.SelectedChanged += (_, _) => changed++;
        var tabs = strip.Parts;
        Assert.AreEqual(0, strip.Selected);
        Assert.AreEqual(0, strip.Current);
        Assert.IsTrue(tabs[0].TabStop, "Tab lands on the chosen tab");
        Assert.AreEqual(1, tabs.Count(tab => tab.TabStop), "and on no other");

        Press(tabs[0], Keys.Right);
        Assert.AreEqual(1, strip.Current);
        Assert.AreEqual(0, strip.Selected, "moving is not choosing");
        Assert.IsTrue(tabs[1].TabStop);
        Press(tabs[1], Keys.End);
        Assert.AreEqual(4, strip.Current);
        Press(tabs[4], Keys.Right);
        Assert.AreEqual(4, strip.Current, "Right stops at the last tab");
        Press(tabs[4], Keys.Home);
        Assert.AreEqual(0, strip.Current);
        Press(tabs[0], Keys.Left);
        Assert.AreEqual(0, strip.Current, "Left stops at the first tab");
        Press(tabs[0], Keys.Down);
        Press(tabs[1], Keys.Down);
        Assert.AreEqual(2, strip.Current);
        Assert.AreEqual(0, changed);

        Press(tabs[2], Keys.Enter);
        Assert.AreEqual(2, strip.Selected);
        Assert.AreEqual(1, changed);
        Press(tabs[2], Keys.Right);
        Press(tabs[3], Keys.Space);
        Assert.AreEqual(3, strip.Selected);
        Assert.AreEqual(2, changed);
        Press(tabs[3], Keys.Enter);
        Assert.AreEqual(2, changed, "choosing the chosen tab again tells nobody");

        strip.Selected = 1;
        Assert.AreEqual(2, changed, "setting Selected raises nothing");
        Assert.IsTrue(tabs[1].TabStop);
        Assert.IsTrue(tabs.All(tab => tab.WantsKey(Keys.Left) && tab.WantsKey(Keys.Right) && tab.WantsKey(Keys.Home) && tab.WantsKey(Keys.End) && tab.WantsKey(Keys.Enter) && tab.WantsKey(Keys.Space)),
            "the window does not take the arrows, Home, End, Enter or Space from a tab");
    }

    [TestMethod]
    public void TheTabsKeepTheirOrderInArabicWhileTheirWordsReadRightToLeft()
    {
        var strip = Tabs();
        strip.RightToLeft = RightToLeft.Yes;
        strip.Height = strip.GetPreferredSize(new Size(600, 0)).Height;
        strip.PerformLayout();
        var tabs = strip.Parts;
        for (int i = 1; i < tabs.Count; i++) Assert.IsGreaterThan(tabs[i - 1].Left, tabs[i].Left, "the tabs run left to right in Arabic too");
        Assert.AreEqual(RightToLeft.Yes, tabs[0].RightToLeft, "each tab's own words read right to left");
        Press(tabs[0], Keys.Right);
        Assert.AreEqual(1, strip.Current, "Right goes to the tab on the right, as in English");
        Press(tabs[1], Keys.Left);
        Assert.AreEqual(0, strip.Current);
    }

    [TestMethod]
    public void TheTabsShareTheWidthAndAreAsTallAsTheTallestNeeds()
    {
        var strip = Tabs();
        strip.Height = strip.GetPreferredSize(new Size(600, 0)).Height;
        strip.PerformLayout();
        var widths = strip.Parts.Select(tab => tab.Width).ToList();
        Assert.IsTrue(widths.Take(4).All(width => width == widths[0]), "the tabs are as wide as each other");
        Assert.IsLessThanOrEqualTo(4, Math.Abs(widths[4] - widths[0]), "the last takes what is left over");
        Assert.AreEqual(599, strip.Parts[^1].Right, "the last reaches the strip's edge");
        int roomy = strip.Height;
        Assert.IsGreaterThan(roomy, strip.GetPreferredSize(new Size(260, 0)).Height, "narrow tabs wrap their words and grow taller");
    }

    static Segmented Themes() => new Segmented().Add("Light", "light").Add("Dark", "dark").Add("System", "system");

    // A few choices are the sketch's size: the words as drawn, without Windows' room at their ends,
    // 10 px each side, and 28 px tall with the edge; Light | Dark | System is 153 by 28 there (I23).
    [TestMethod]
    public void AFewChoicesAreTheSketchsSize()
    {
        Theme.UseForShots(false);
        var choices = Themes();
        int words = choices.Parts.Sum(part => TextRenderer.MeasureText(part.Text, choices.Face, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width);
        Assert.AreEqual(words + 3 * (choices.LogicalToDeviceUnits(20) + 1) + 2, choices.Width);
        Assert.AreEqual(choices.LogicalToDeviceUnits(28), choices.Height);
        Assert.IsLessThanOrEqualTo(choices.LogicalToDeviceUnits(160), choices.Width, "close to the sketch's 153");
    }

    [TestMethod]
    public void TheArrowsChooseInAFewChoicesAndRunTheOtherWayInArabic()
    {
        var choices = Themes();
        choices.Value = "dark";
        int changed = 0;
        choices.SelectedChanged += (_, _) => changed++;
        var parts = choices.Parts;
        Assert.IsTrue(parts[1].TabStop, "Tab lands on the chosen part");
        Assert.AreEqual(1, parts.Count(part => part.TabStop));

        Press(parts[1], Keys.Right);
        Assert.AreEqual("system", choices.Value, "an arrow chooses as it moves");
        Assert.AreEqual(1, changed);
        Press(parts[2], Keys.Right);
        Assert.AreEqual("system", choices.Value, "and stops at the end");
        Assert.AreEqual(1, changed);
        Press(parts[2], Keys.Home);
        Assert.AreEqual("light", choices.Value);
        Assert.AreEqual(2, changed);
        choices.Value = "dark";
        Assert.AreEqual(2, changed, "setting a value raises nothing");

        var arabic = Themes();
        arabic.RightToLeft = RightToLeft.Yes;
        arabic.PerformLayout();
        Assert.IsGreaterThan(arabic.Parts[1].Left, arabic.Parts[0].Left, "the first choice is at the right in Arabic");
        arabic.Value = "light";
        Press(arabic.Parts[0], Keys.Left);
        Assert.AreEqual("dark", arabic.Value, "Left goes on to the next choice, which is to its left");
        Press(arabic.Parts[1], Keys.Right);
        Assert.AreEqual("light", arabic.Value);
        arabic.Value = "nothing like it";
        Assert.AreEqual("light", arabic.Value, "a value that is not a choice is ignored");
    }

    [TestMethod]
    public void TheLanguagesReadEnglishThenArabicAndOnlyEnterOrSpaceChooses()
    {
        foreach (var direction in new[] { RightToLeft.No, RightToLeft.Yes })
        {
            var pills = new LanguagePills("Language").Add("English", "en").Add("العربية", "ar");
            pills.RightToLeft = direction;
            pills.PerformLayout();
            pills.Value = "en";
            int changed = 0;
            pills.SelectedChanged += (_, _) => changed++;
            var parts = pills.Parts;
            Assert.IsGreaterThan(parts[0].Left, parts[1].Left, "English is on the left, Arabic on its right");
            Assert.AreEqual(RightToLeft.No, parts[0].RightToLeft);
            Assert.AreEqual(RightToLeft.Yes, parts[1].RightToLeft, "Arabic reads in its own direction");
            Press(parts[0], Keys.Right);
            Assert.AreEqual(1, pills.Current);
            Assert.AreEqual("en", pills.Value, "moving is not choosing: switching the language rebuilds the window");
            Press(parts[1], Keys.Enter);
            Assert.AreEqual("ar", pills.Value);
            Assert.AreEqual(1, changed);
            Press(parts[1], Keys.Left);
            Press(parts[0], Keys.Space);
            Assert.AreEqual("en", pills.Value);
            Assert.AreEqual(2, changed);
        }
    }

    static ChipList Apps(params string[] names)
    {
        var list = new ChipList { Width = 320 };
        foreach (var name in names) list.Add(name);
        return list;
    }

    [TestMethod]
    public void DeleteRemovesAChipAndFocusStaysInTheList()
    {
        var after = new Button();
        var list = Apps("Notepad", "Paint", "Calculator");
        list.FocusWhenEmpty = after;
        var removed = new List<ChipRemovedEventArgs>();
        list.Removed += (_, e) => removed.Add(e);

        Press(list.Parts[0], Keys.Right);
        Assert.AreEqual(1, list.Current);
        Press(list.Parts[1], Keys.Delete);
        CollectionAssert.AreEqual(new[] { "Notepad", "Calculator" }, list.Texts.ToArray());
        Assert.AreEqual("Paint", removed.Single().Text);
        Assert.AreEqual(1, removed.Single().Index);
        Assert.AreEqual(1, list.Current, "the chip after it is the one Tab lands on now");
        Assert.AreSame(list.Parts[1], list.FocusAfter, "and focus goes to it");
        Assert.IsTrue(list.Parts[1].TabStop);

        Assert.AreSame(list.Parts[1], list.FocusedAfterRemoving);
        Press(list.Parts[1], Keys.Back);
        Assert.AreEqual(0, list.Current, "with none after it, the chip before it");
        Assert.AreSame(list.Parts[0], list.FocusAfter);
        Assert.AreSame(list.Parts[0], list.FocusedAfterRemoving);
        Press(list.Parts[0], Keys.Delete);
        Assert.IsEmpty(list.Parts);
        Assert.AreSame(after, list.FocusedAfterRemoving, "removing the last chip sends focus to FocusWhenEmpty, chosen before the chip goes");
        Assert.AreSame(after, list.FocusAfter, "an empty list hands focus on");
        list.Add("Paint");
        list.FocusWhenEmpty = null;
        Press(list.Parts[0], Keys.Delete);
        Assert.AreSame(list, list.FocusedAfterRemoving, "without one, the empty list keeps focus");
        Assert.AreSame(list, list.FocusAfter, "or keeps it itself");
        CollectionAssert.AreEqual(new[] { "Paint", "Calculator", "Notepad", "Paint" }, removed.Select(e => e.Text).ToArray());
        Assert.IsGreaterThan(0, list.Height, "an empty list keeps room for its words");
    }

    [TestMethod]
    public void AHoveredCrossShowsInEveryPalette()
    {
        var high = (Theme)typeof(Theme).GetMethod("HighContrast", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
        foreach (var t in new[] { Theme.Light, Theme.Dark, high })
        {
            var (fill, ink) = ChipList.RemoveHoverColors(t);
            Assert.AreNotEqual(fill.ToArgb(), ink.ToArgb(), "the × never takes the color of the fill under it");
        }
        Assert.AreEqual((Theme.Light.Line, Theme.Light.Text), ChipList.RemoveHoverColors(Theme.Light), "light and dark keep the gray fill");
        Assert.AreEqual((Theme.Dark.Line, Theme.Dark.Text), ChipList.RemoveHoverColors(Theme.Dark));
        Assert.AreEqual((SystemColors.Highlight, SystemColors.HighlightText), ChipList.RemoveHoverColors(high), "high contrast takes the highlight colors");
    }

    [TestMethod]
    public void EnterAndSpaceDoNothingToAChipAndSettingTheChipsTellsNobody()
    {
        var list = Apps("Notepad", "Paint");
        int removed = 0;
        list.Removed += (_, _) => removed++;
        Assert.IsFalse(list.Parts[0].Key(Keys.Enter));
        Assert.IsFalse(list.Parts[0].Key(Keys.Space));
        Assert.IsTrue(list.Parts[0].WantsKey(Keys.Delete) && list.Parts[0].WantsKey(Keys.Back));
        list.SetItems([("Notepad", "notepad.exe"), ("Paint", "mspaint.exe"), ("Calculator", "calc.exe")]);
        CollectionAssert.AreEqual(new object[] { "notepad.exe", "mspaint.exe", "calc.exe" }, list.Values.ToArray());
        list.Remove(0);
        Assert.AreEqual(0, removed, "only a person's removing is told");
        var once = list.Parts[0];
        list.SetItems([("Paint", "mspaint.exe"), ("Calculator", "calc.exe")]);
        Assert.AreSame(once, list.Parts[0], "the same chips are kept, not made again");
    }

    [TestMethod]
    public void TheChipsWrapOntoMoreLinesWhenTheyDoNotFit()
    {
        var list = Apps("Notepad", "Paint");
        int one = list.Height;
        for (int i = 0; i < 8; i++) list.Add($"Another app {i}");
        Assert.IsGreaterThan(one, list.Height, "more lines, taller list");
        Assert.IsTrue(list.Parts.All(chip => chip.Right <= list.Width), "no chip runs past the edge");
    }

    static Label TitleOf(SettingRow row) => row.Controls.OfType<Label>().First();

    [TestMethod]
    public void ARowKeepsTheWelcomeLookUnlessToldOtherwise()
    {
        var row = new SettingRow("", "Lock my PC after", "Time without using the mouse or keyboard", new DropDown().Add("15 minutes", 15));
        Assert.AreEqual(RowLook.Classic, row.Look);
        Assert.AreEqual(8, row.Radius);
        Assert.IsNull(row.Outline);
        Assert.IsFalse(row.Flat);
        row.Width = 600;
        Assert.AreEqual(row.LogicalToDeviceUnits(62), row.GetPreferredSize(new Size(600, 0)).Height, "today's 62 px row");
        row.Height = 62;
        row.PerformLayout();
        Assert.AreEqual(row.LogicalToDeviceUnits(18 + 22 + 14), TitleOf(row).Left);
    }

    [TestMethod]
    public void ALookCRowHasItsCircleIconPaddingAndGap()
    {
        var list = new DropDown().Add("15 minutes", 15);
        var row = new SettingRow("", "Lock my PC after", "Time without using the mouse or keyboard", list) { Width = 600 };
        row.Look = RowLook.LookC;
        Assert.AreEqual(6, row.Radius);
        Assert.AreEqual(Theme.Current.Edge, row.Outline!(Theme.Current));
        Assert.AreEqual(row.LogicalToDeviceUnits(4), row.Margin.Top, "rows stand 4 px apart");
        Assert.AreEqual(row.LogicalToDeviceUnits(40 + 22) + 2, row.GetPreferredSize(new Size(600, 0)).Height, "40 px inside, 11 above and below");
        row.Height = row.GetPreferredSize(new Size(600, 0)).Height;
        row.PerformLayout();
        Assert.AreEqual(row.LogicalToDeviceUnits(16 + 32 + 16), TitleOf(row).Left, "the words start after the 32 px circle");
        Assert.AreEqual(600 - row.LogicalToDeviceUnits(16), list.Right, "the control ends 16 px from the edge");

        row.RightToLeft = RightToLeft.Yes;
        row.PerformLayout();
        Assert.AreEqual(600 - row.LogicalToDeviceUnits(16 + 32 + 16), TitleOf(row).Right, "in Arabic the words end before the circle on the right");
        Assert.AreEqual(row.LogicalToDeviceUnits(16), list.Left);
        row.Look = RowLook.Classic;
        Assert.AreEqual(8, row.Radius);
        Assert.IsNull(row.Outline);
    }

    [TestMethod]
    public void SomethingUnderARowsWordsSitsInTheirColumnAndTheRowGrowsWithIt()
    {
        var add = new KitButton("Add app") { Small = true, Glyph = "" };
        var row = new SettingRow(null, "Open apps", "Opens the apps you choose", add) { Width = 600 };
        row.Look = RowLook.LookC;
        int plain = row.GetPreferredSize(new Size(600, 0)).Height;
        var chips = new ChipList();
        row.Below = chips;
        Assert.AreEqual("Open apps", chips.AccessibleName, "the list is named after the row");
        chips.Add("Notepad");
        row.Height = row.GetPreferredSize(new Size(600, 0)).Height;
        Assert.IsGreaterThan(plain, row.Height);
        row.PerformLayout();
        var title = TitleOf(row);
        Assert.AreEqual(title.Left, chips.Left, "in the words' column");
        Assert.IsGreaterThan(title.Bottom, chips.Top);
        Assert.AreEqual(title.Top, add.Top, "the control lines up with the title");
        for (int i = 0; i < 12; i++) chips.Add($"Another app {i}");
        Assert.IsGreaterThan(row.Height, row.GetPreferredSize(new Size(600, 0)).Height, "more lines of chips, a taller row");
        row.Below = null;
        Assert.IsFalse(row.Controls.Contains(chips));
    }

    [TestMethod]
    public void APanelStacksItsRowsWithoutBoxesOfTheirOwn()
    {
        var panel = new FlatPanel { Width = 700 };
        var first = new SettingRow(null, "Mute the PC", "The sound comes back when you unlock the PC", new Toggle());
        var second = new FlatRow("Play a sound", "A short sound when the PC locks", new DropDown { Small = true }.Add("Off", 0));
        var third = new FlatRow("Show how long you were away", "A notification that goes away by itself", new Toggle());
        panel.Add(first).Add(second).Add(third, lineAbove: true);
        Assert.AreEqual(RowLook.Flat, first.Look, "a row added to a panel loses its own box");
        Assert.IsTrue(first.Flat && second.Flat && third.Flat);
        Assert.AreEqual(Theme.Current.Edge, panel.Outline!(Theme.Current));
        Assert.IsTrue(panel.HasLineAbove(third) && !panel.HasLineAbove(second));
        panel.Height = panel.GetPreferredSize(new Size(700, 0)).Height;
        panel.PerformLayout();
        int Px(int n) => panel.LogicalToDeviceUnits(n);
        Assert.AreEqual(Px(20), first.Left);
        Assert.AreEqual(700 - Px(40), first.Width);
        Assert.AreEqual(Px(16), first.Top);
        Assert.AreEqual(first.Bottom + Px(13), second.Top, "13 px between rows");
        Assert.AreEqual(second.Bottom + Px(13) + Px(12), third.Top, "and 12 more under a line");
        Assert.AreEqual(third.Bottom + Px(16), panel.Height);
        Assert.AreEqual(0, TitleOf(second).Left, "a flat row has no padding of its own");
    }

    [TestMethod]
    public void ASmallButtonAndAButtonWithAnIcon()
    {
        var plain = new KitButton("Add app");
        var small = new KitButton("Add app") { Small = true };
        Assert.AreEqual(plain.LogicalToDeviceUnits(34), plain.Height, "today's button is unchanged");
        Assert.AreEqual(small.LogicalToDeviceUnits(26), small.Height);
        Assert.IsLessThan(plain.Width, small.Width);
        int bare = small.Width;
        small.Glyph = "";
        Assert.IsGreaterThan(bare + small.LogicalToDeviceUnits(6), small.Width, "the icon and its gap make room");
        small.Glyph = null;
        Assert.AreEqual(bare, small.Width);
    }

    [TestMethod]
    public void ALookCListAndAFixedWidthThatMakesRoomForItsWords()
    {
        var list = new DropDown().Add("Off", 0).Add("Pause until you're back", 1);
        int classic = list.Height;
        list.LookC = true;
        Assert.AreEqual(list.LogicalToDeviceUnits(30), list.Height, "the settings window's 30 px list");
        Assert.IsLessThan(classic, list.Height);
        list.Small = true;
        Assert.AreEqual(list.LogicalToDeviceUnits(26), list.Height, "small wins over look C");
        list.Small = false;

        list.ButtonWidth = 120;
        list.Value = 0;
        Assert.AreEqual(list.LogicalToDeviceUnits(120), list.Width, "a fixed width, whatever the longest choice");
        list.Value = 1;
        Assert.IsGreaterThan(list.LogicalToDeviceUnits(120), list.Width, "but never so narrow it cuts off the chosen words");
        list.Value = 0;
        Assert.AreEqual(list.LogicalToDeviceUnits(120), list.Width, "and back to its width after");
        list.Relabel(0, "Off, and nothing else happens at all");
        Assert.IsGreaterThan(list.LogicalToDeviceUnits(120), list.Width, "new words for the chosen one count too");
    }

    [TestMethod]
    public void EveryRowOfAWideMenuSpansTheMenu()
    {
        foreach (var rtl in new[] { RightToLeft.No, RightToLeft.Yes })
        {
            var list = new DropDown { LookC = true, ButtonWidth = 190, RightToLeft = rtl }
                .Add("15 minutes", 15).Add("30 minutes", 30).AddHeading("Lock sooner").Add("50% sooner", 50).AddLine().Add("1 hour", 60);
            list.Value = 15;
            using var menu = list.BuildMenu();
            for (int layout = 0; layout < 2; layout++)
            {
                menu.PerformLayout();
                Assert.AreEqual(list.Width, menu.Width, $"{rtl}: the menu is as wide as its list");
                foreach (var row in menu.Items.OfType<ToolStripMenuItem>())
                {
                    Assert.IsLessThanOrEqualTo(1, row.Bounds.Left, $"{rtl}: '{row.Text}' starts at the menu's near side");
                    Assert.IsGreaterThanOrEqualTo(menu.Width - 2, row.Bounds.Right, $"{rtl}: '{row.Text}' reaches its far side, so its fill and its clicks do too");
                }
            }
            var shown = menu.Items.Cast<ToolStripItem>().Where(row => row.Available).Select(row => row is ToolStripSeparator ? "—" : row.Text).ToArray();
            CollectionAssert.AreEqual(list.Rows.ToArray(), shown, $"{rtl}: nothing else is shown, read or reached by the keys");
        }

        var narrow = new DropDown { ButtonWidth = 60 }.Add("Pause until you're back", 1);
        using var natural = narrow.BuildMenu();
        natural.PerformLayout();
        Assert.IsGreaterThanOrEqualTo(narrow.Width, natural.Width, "a list narrower than its words gets a menu as wide as its words");
        Assert.AreEqual(1, natural.Items.Count, "and nothing added to it");
    }

    [TestMethod]
    public void TheOpenMenuIsSizedForTheListsOwnScaling()
    {
        var list = new DropDown().Add("15 minutes", 15).AddHeading("Lock sooner").Add("50% sooner", 50);
        list.Value = 15;
        using var menu = list.BuildMenu();
        Assert.AreEqual(new Padding(0, list.LogicalToDeviceUnits(5), 0, list.LogicalToDeviceUnits(5)), menu.Items[0].Padding);
        Assert.AreEqual(Theme.Body(10f, list.DeviceDpi), menu.Font);
        Assert.AreEqual(Theme.Body(8.25f, list.DeviceDpi), menu.Items[1].Font, "the heading's small words");
        Assert.IsTrue(((ToolStripMenuItem)menu.Items[0]).Checked);
        Assert.AreEqual(Theme.Strong(10f, list.DeviceDpi), menu.Items[0].Font, "the chosen row's words are the menu's size, in semibold");
        Assert.AreEqual(menu.Font.SizeInPoints, menu.Items[2].Font.SizeInPoints, "the other rows take the menu's words");
        list.Small = true;
        using var small = list.BuildMenu();
        Assert.AreEqual(Theme.Body(9f, list.DeviceDpi), small.Font, "a small list's menu has its small words");
        Assert.AreEqual(Theme.Strong(9f, list.DeviceDpi), small.Items[0].Font, "and its chosen row the same size");
    }

    // A value read from the file that is not in the list sits just above Custom…, where the same
    // value typed in the box would be, so it does not move after a restart.
    [TestMethod]
    public void AValueNotInTheListSitsJustAboveCustomWhereverItCameFrom()
    {
        var list = Grouped(out _);
        list.Unlisted = v => v is string s && s.StartsWith("always ") ? "Always lock after " + s[7..] : null;
        list.Value = "always 45";
        var rows = list.Rows.ToList();
        Assert.AreEqual("Always lock after 45", rows[^2]);
        Assert.AreEqual("Custom…", rows[^1]);
        list.ShowAs("always 50", "Always lock after 50");
        rows = list.Rows.ToList();
        Assert.AreEqual("Always lock after 50", rows[^2], "shown as words of its own, the same place");
        Assert.DoesNotContain("Always lock after 45", rows, "and in place of the one before");

        CustomBox? box = null;
        var typed = Grouped(out _);
        typed.Presenter = b => box = b;
        typed.OpenCustom(0);
        BoxOf(box!.Field).Text = "45";
        Assert.IsTrue(box.Key(Keys.Enter));
        var read = Grouped(out _);
        read.Unlisted = list.Unlisted;
        read.Value = "always 45";
        CollectionAssert.AreEqual(typed.Rows.ToArray(), read.Rows.ToArray(), "typed or read, the same rows");

        var plain = new DropDown().Add("15 minutes", 15);
        plain.ShowAs(45, "45 minutes");
        CollectionAssert.AreEqual(new[] { "15 minutes", "45 minutes" }, plain.Rows.ToArray(), "with no Custom… row it goes at the end");
    }

    // A slant made up for Arabic smears its letters; English keeps its italic.
    [TestMethod]
    public void AnEmptyListIsItalicOnlyInEnglish()
    {
        try
        {
            Lang.Use("en");
            Assert.IsTrue(new ChipList().EmptyFace.Italic);
            Lang.Use("ar");
            var arabic = new ChipList();
            Assert.IsFalse(arabic.EmptyFace.Italic);
            Assert.AreEqual(Theme.Body(9f, arabic.DeviceDpi), arabic.EmptyFace);
        }
        finally
        {
            Lang.Use("auto");
        }
    }
}
