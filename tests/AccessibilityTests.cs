namespace SteppedOut.Tests;

// What the hand-drawn controls tell UI Automation, which Narrator reads: what each one is, its state,
// and that a screen reader's own commands work it. Asked of the providers directly, with no window.
// One at a time: making controls fills the shared font cache, which is not made for several threads.
[TestClass]
[DoNotParallelize]
public class AccessibilityTests
{
    static Automation.IRawElementProviderSimple Asked(Automation.Provider? provider)
    {
        Assert.IsNotNull(provider);
        return provider;
    }

    static T Pattern<T>(Automation.IRawElementProviderSimple element, int pattern) where T : class
    {
        var found = element.GetPatternProvider(pattern) as T;
        Assert.IsNotNull(found);
        return found;
    }

    [TestMethod]
    public void ASwitchIsACheckBoxThatSaysWhetherItIsOnAndExplainsItself()
    {
        var toggle = new Toggle();
        _ = new SettingRow(null, "Mute the sound", "It comes back when you do", toggle);
        var element = Asked(toggle.AutomationProvider);

        Assert.AreEqual(Automation.CheckBoxType, element.GetPropertyValue(Automation.ControlTypeProperty));
        Assert.AreEqual("Mute the sound", element.GetPropertyValue(Automation.NameProperty));
        Assert.AreEqual("It comes back when you do", element.GetPropertyValue(Automation.HelpTextProperty));
        Assert.IsNull(element.GetPatternProvider(Automation.InvokePattern));
        var toggling = Pattern<Automation.IToggleProvider>(element, Automation.TogglePattern);
        Assert.AreEqual(0, toggling.ToggleState);
        toggling.Toggle();
        Assert.IsTrue(toggle.Checked);
        Assert.AreEqual(1, toggling.ToggleState);
    }

    // The welcome's pills: one click, Space or a screen reader picks one, and the chosen one says so.
    [TestMethod]
    public void APillIsARadioButtonThatSaysWhetherItIsChosen()
    {
        var chips = new ChoiceChips { AccessibleName = "Lock the PC after" }.Add("5 minutes", 5.0).Add("15 minutes", 15.0);
        chips.AddCustom("Custom…");
        chips.Value = 15.0;
        int changed = 0;
        chips.SelectedChanged += (_, _) => changed++;
        var group = Asked(chips.AutomationProvider);
        Assert.AreEqual(Automation.GroupType, group.GetPropertyValue(Automation.ControlTypeProperty));
        Assert.AreEqual("Lock the PC after", group.GetPropertyValue(Automation.NameProperty));
        Assert.AreSame(chips.Parts[1].AutomationProvider, Chosen(group).Single());

        var first = Asked(chips.Parts[0].AutomationProvider);
        Assert.AreEqual(Automation.RadioButtonType, first.GetPropertyValue(Automation.ControlTypeProperty));
        Assert.AreEqual("5 minutes", first.GetPropertyValue(Automation.NameProperty));
        Assert.IsNull(first.GetPatternProvider(Automation.InvokePattern));
        var item = Pattern<Automation.ISelectionItemProvider>(first, Automation.SelectionItemPattern);
        Assert.IsFalse(item.IsSelected);
        item.Select();
        Assert.AreEqual(5.0, chips.Value);
        Assert.AreEqual(1, changed, "a screen reader choosing a pill is a person choosing it");
        Assert.IsTrue(item.IsSelected);
        Assert.AreEqual(AccessibleRole.RadioButton, chips.Parts[0].AccessibilityObject.Role);
        Assert.IsTrue(chips.Parts[0].AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
    }

    // The welcome's toggles beside the drawn PC: each a check box of its own.
    [TestMethod]
    public void APreviewToggleIsACheckBox()
    {
        var chips = new ChoiceChips(multiple: true) { AccessibleName = "Preview a situation" }.Add("Music or video", Situation.Music).Add("A call", Situation.Call);
        var element = Asked(chips.Parts[1].AutomationProvider);
        Assert.AreEqual(Automation.CheckBoxType, element.GetPropertyValue(Automation.ControlTypeProperty));
        Assert.AreEqual("A call", element.GetPropertyValue(Automation.NameProperty));
        var toggling = Pattern<Automation.IToggleProvider>(element, Automation.TogglePattern);
        Assert.AreEqual(0, toggling.ToggleState);
        toggling.Toggle();
        Assert.IsTrue(chips.IsChecked(Situation.Call));
        Assert.IsFalse(chips.IsChecked(Situation.Music));
        Assert.AreEqual(1, toggling.ToggleState);
        Assert.AreEqual(AccessibleRole.CheckButton, chips.Parts[1].AccessibilityObject.Role);
    }

    // Expand is left alone: it would open the list's menu.
    [TestMethod]
    public void AListSaysItsChoiceAndThatItIsClosed()
    {
        var list = new DropDown().Add("15 minutes", 15).Add("2 hours", 120);
        list.Value = 120;
        _ = new SettingRow(null, "Lock my PC after", null, list);
        var element = Asked(list.AutomationProvider);

        Assert.AreEqual(Automation.ComboBoxType, element.GetPropertyValue(Automation.ControlTypeProperty));
        Assert.AreEqual("Lock my PC after", element.GetPropertyValue(Automation.NameProperty));
        Assert.IsNull(element.GetPatternProvider(Automation.InvokePattern));
        Assert.AreEqual(0, Pattern<Automation.IExpandCollapseProvider>(element, Automation.ExpandCollapsePattern).ExpandCollapseState);
        var value = Pattern<Automation.IValueProvider>(element, Automation.ValuePattern);
        Assert.AreEqual("2 hours", value.Value);
        Assert.IsTrue(value.IsReadOnly);
    }

    [TestMethod]
    public void APlainButtonKeepsWinFormsOwnAnswer()
    {
        Assert.IsNull(new KitButton("Done").AutomationProvider);
        Assert.IsNull(new TextBlock("A hint", Theme.Body(9.5f), Color.Gray).AutomationProvider);
    }

    [TestMethod]
    public void HeadingsSayTheirLevel()
    {
        var title = Asked(new TextBlock("Expert settings", Theme.Strong(14f), Color.Black) { Heading = 1 }.AutomationProvider);
        var section = Asked(new TextBlock("Mouse", Theme.Strong(10.5f), Color.Black) { Heading = 2 }.AutomationProvider);

        Assert.AreEqual(Automation.TextType, title.GetPropertyValue(Automation.ControlTypeProperty));
        Assert.AreEqual("Expert settings", title.GetPropertyValue(Automation.NameProperty));
        Assert.AreEqual(Automation.NotAHeading + 1, title.GetPropertyValue(Automation.HeadingLevelProperty));
        Assert.AreEqual(Automation.NotAHeading + 2, section.GetPropertyValue(Automation.HeadingLevelProperty));
        Assert.IsFalse((bool)title.GetPropertyValue(Automation.IsKeyboardFocusableProperty)!);
    }

    // Left unnamed, WinForms would name each after whatever label comes before it.
    [TestMethod]
    public void TheFramesAroundControlsHaveNoNameOfTheirOwn()
    {
        Assert.AreEqual("", new SettingRow(null, "Mute the sound", null, new Toggle()).AccessibleName);
        Assert.AreEqual("", new Column().AccessibleName);
        Assert.AreEqual("", new PageStack().AccessibleName);
        Assert.AreEqual("", new WelcomeFooter(new StepDots(), new KitButton("Done")).AccessibleName);
        var field = new MinutesField("Lock my PC after, in minutes");
        _ = new SettingRow(null, "Custom time", "That's 2 hours.", field);
        Assert.AreEqual("", field.AccessibleName);
        Assert.AreEqual("Lock my PC after, in minutes", field.Controls.OfType<TextBox>().Single().AccessibilityObject.Name);
    }

    static Automation.IRawElementProviderSimple[] Chosen(Automation.IRawElementProviderSimple group) =>
        Pattern<Automation.ISelectionProvider>(group, Automation.SelectionPattern).GetSelection();

    [TestMethod]
    public void TabsAreATabListWhoseChosenTabIsSelected()
    {
        var strip = new TabStrip().Add("", "Basics", "When it locks").Add("", "Sounds", "Calls and media");
        int changed = 0;
        strip.SelectedChanged += (_, _) => changed++;
        var list = Asked(strip.AutomationProvider);
        Assert.AreEqual(Automation.TabType, list.GetPropertyValue(Automation.ControlTypeProperty));
        Assert.AreEqual(AccessibleRole.PageTabList, strip.AccessibilityObject.Role);
        var selection = Pattern<Automation.ISelectionProvider>(list, Automation.SelectionPattern);
        Assert.IsFalse(selection.CanSelectMultiple);
        Assert.IsTrue(selection.IsSelectionRequired);
        Assert.AreSame(strip.Parts[0].AutomationProvider, selection.GetSelection().Single());

        var second = strip.Parts[1];
        var tab = Asked(second.AutomationProvider);
        Assert.AreEqual(Automation.TabItemType, tab.GetPropertyValue(Automation.ControlTypeProperty));
        Assert.AreEqual("Sounds", tab.GetPropertyValue(Automation.NameProperty));
        Assert.AreEqual("Calls and media", tab.GetPropertyValue(Automation.HelpTextProperty));
        Assert.AreEqual(AccessibleRole.PageTab, second.AccessibilityObject.Role);
        Assert.IsNull(tab.GetPatternProvider(Automation.InvokePattern), "a tab is selected, not pressed");
        var item = Pattern<Automation.ISelectionItemProvider>(tab, Automation.SelectionItemPattern);
        Assert.AreSame(list, item.SelectionContainer);
        Assert.IsFalse(item.IsSelected);
        Assert.IsFalse(second.AccessibilityObject.State.HasFlag(AccessibleStates.Selected));
        item.Select();
        Assert.AreEqual(1, strip.Selected);
        Assert.AreEqual(1, changed, "a screen reader choosing a tab is a person choosing it");
        Assert.IsTrue(item.IsSelected);
        Assert.IsTrue(second.AccessibilityObject.State.HasFlag(AccessibleStates.Selected));
        Assert.AreSame(second.AutomationProvider, Chosen(list).Single());
        second.AccessibilityObject.DoDefaultAction();
        Assert.AreEqual(1, changed);
        strip.Parts[0].AccessibilityObject.DoDefaultAction();
        Assert.AreEqual(0, strip.Selected);
    }

    [TestMethod]
    public void AFewChoicesAreAGroupOfRadioButtons()
    {
        var choices = new Segmented { AccessibleName = "Theme" }.Add("Light", "light").Add("Dark", "dark");
        choices.Value = "light";
        var group = Asked(choices.AutomationProvider);
        Assert.AreEqual(Automation.GroupType, group.GetPropertyValue(Automation.ControlTypeProperty));
        Assert.AreEqual("Theme", group.GetPropertyValue(Automation.NameProperty));
        Assert.AreSame(choices.Parts[0].AutomationProvider, Chosen(group).Single());

        var dark = choices.Parts[1];
        var radio = Asked(dark.AutomationProvider);
        Assert.AreEqual(Automation.RadioButtonType, radio.GetPropertyValue(Automation.ControlTypeProperty));
        Assert.AreEqual("Dark", radio.GetPropertyValue(Automation.NameProperty));
        Assert.AreEqual(AccessibleRole.RadioButton, dark.AccessibilityObject.Role);
        var item = Pattern<Automation.ISelectionItemProvider>(radio, Automation.SelectionItemPattern);
        Assert.AreSame(group, item.SelectionContainer);
        Assert.IsFalse(item.IsSelected);
        item.Select();
        Assert.AreEqual("dark", choices.Value);
        Assert.IsTrue(item.IsSelected);
        Assert.IsTrue(dark.AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
        Assert.IsFalse(choices.Parts[0].AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
    }

    [TestMethod]
    public void TheLanguagesAreAGroupNamedForWhatItChooses()
    {
        var pills = new LanguagePills("Language").Add("English", "en").Add("العربية", "ar");
        pills.Value = "en";
        var group = Asked(pills.AutomationProvider);
        Assert.AreEqual(Automation.GroupType, group.GetPropertyValue(Automation.ControlTypeProperty));
        Assert.AreEqual("Language", group.GetPropertyValue(Automation.NameProperty));
        var arabic = Asked(pills.Parts[1].AutomationProvider);
        Assert.AreEqual(Automation.RadioButtonType, arabic.GetPropertyValue(Automation.ControlTypeProperty));
        Assert.AreEqual("العربية", arabic.GetPropertyValue(Automation.NameProperty));
        var item = Pattern<Automation.ISelectionItemProvider>(arabic, Automation.SelectionItemPattern);
        Assert.AreSame(group, item.SelectionContainer);
        item.Select();
        Assert.AreEqual("ar", pills.Value);
        Assert.IsTrue(pills.Parts[1].AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
        Assert.AreSame(pills.Parts[1].AutomationProvider, Chosen(group).Single());
    }

    [TestMethod]
    public void TheAppsAreAListAndEachCrossIsAButtonThatRemovesItsApp()
    {
        var list = new ChipList { Width = 300, AccessibleName = "Open apps" };
        list.Add("Notepad");
        list.Add("Paint");
        var removed = new List<string>();
        list.Removed += (_, e) => removed.Add(e.Text);
        var element = Asked(list.AutomationProvider);
        Assert.AreEqual(Automation.ListType, element.GetPropertyValue(Automation.ControlTypeProperty));
        Assert.AreEqual("Open apps", element.GetPropertyValue(Automation.NameProperty));
        Assert.AreEqual(AccessibleRole.List, list.AccessibilityObject.Role);
        // Nothing is chosen among the apps, so the list has no selection and the chips are plain
        // items, not "selected" ones (I35).
        Assert.IsNull(element.GetPatternProvider(Automation.SelectionPattern));

        var paint = list.Parts[1];
        var chip = Asked(paint.AutomationProvider);
        Assert.AreEqual(Automation.ListItemType, chip.GetPropertyValue(Automation.ControlTypeProperty));
        Assert.AreEqual("Paint", chip.GetPropertyValue(Automation.NameProperty));
        Assert.AreEqual(AccessibleRole.ListItem, paint.AccessibilityObject.Role);
        foreach (var part in list.Parts)
        {
            var asked = Asked(part.AutomationProvider);
            Assert.IsNull(asked.GetPatternProvider(Automation.SelectionItemPattern), part.Text);
            Assert.IsNull(asked.GetPatternProvider(Automation.InvokePattern), part.Text);
            Assert.IsFalse(part.AccessibilityObject.State.HasFlag(AccessibleStates.Selected), part.Text);
            Assert.IsFalse(part.AccessibilityObject.State.HasFlag(AccessibleStates.Selectable), part.Text);
        }

        var cross = (KitControl)paint.Controls[0];
        var button = Asked(cross.AutomationProvider);
        Assert.AreEqual(Automation.ButtonType, button.GetPropertyValue(Automation.ControlTypeProperty));
        Assert.AreEqual("Remove Paint", button.GetPropertyValue(Automation.NameProperty));
        Assert.AreEqual("Remove Paint", cross.AccessibilityObject.Name);
        Assert.AreEqual(AccessibleRole.PushButton, cross.AccessibilityObject.Role);
        Assert.IsFalse(cross.TabStop, "Delete does the same from the chip, so Tab passes the cross by");
        Pattern<Automation.IInvokeProvider>(button, Automation.InvokePattern).Invoke();
        CollectionAssert.AreEqual(new[] { "Paint" }, removed);
        CollectionAssert.AreEqual(new[] { "Notepad" }, list.Texts.ToArray());
        list.Parts[0].Controls[0].AccessibilityObject.DoDefaultAction();
        Assert.IsEmpty(list.Parts);
        Assert.AreEqual(Lang.Current.V1.NoAppsAdded, list.AccessibilityObject.Value, "an empty list says so");
    }

    // The box that takes a new shortcut is read only to a screen reader: its keys come from the
    // keyboard, and a value set from outside would do nothing (I35).
    [TestMethod]
    public void TheShortcutBoxIsReadOnly()
    {
        using var box = new ShortcutDialog.KeyBox { Shown = "Ctrl+Alt+L" };
        Assert.AreEqual(AccessibleRole.Text, box.AccessibilityObject.Role);
        Assert.AreEqual("Ctrl+Alt+L", box.AccessibilityObject.Value);
        Assert.IsTrue(box.AccessibilityObject.State.HasFlag(AccessibleStates.ReadOnly));
    }

    // The row of tabs has a name, so the first Tab stop in the settings window makes sense when read
    // out (I35).
    [TestMethod]
    public void TheTabsHaveAName()
    {
        foreach (string language in new[] { "en", "ar" })
        {
            Lang.Use(language);
            try
            {
                using var form = new SettingsForm(new Config(), new NoHost());
                Assert.AreEqual(Lang.Current.V1.SettingsSections, form.Tabs.AccessibleName);
                Assert.AreEqual(Lang.Current.V1.SettingsSections, Asked(form.Tabs.AutomationProvider).GetPropertyValue(Automation.NameProperty));
            }
            finally
            {
                Lang.Use("en");
            }
        }
    }

    // Just enough of an app for a settings window built here.
    sealed class NoHost : ISettingsHost
    {
        public void PauseChosen(TimeSpan? length) { }
        public void ResumeChosen() { }
        public void LockNowChosen() { }
        public void TryItChosen() { }
        public void ShortcutChanged(string shortcut) { }
        public void LanguageChanged(string language) { }
        public HotKeyResult ShortcutResult => HotKeyResult.Ok;
        public event Action? ShortcutResultChanged { add { } remove { } }
        public IReadOnlyList<WhyLine> Explain() => [];
        public void ConfigEdited(Config config) { }
        public bool StartsWithWindows => false;
        public AutostartState StartWithWindowsState => AutostartState.Off;
        public event Action? StartWithWindowsChanged { add { } remove { } }
        public void StartWithWindowsChosen(bool on) { }
        public LockStatus Status => LockStatus.Starting(15);
        public event Action? StatusChanged { add { } remove { } }
    }
}
