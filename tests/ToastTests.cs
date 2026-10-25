using System.Runtime.InteropServices;

namespace SteppedOut.Tests;

// The corner notes: where they go, what each says, Try it on its own timer, and Snooze apart from
// the countdown. A headless note does everything but make a window, so nothing appears on the
// screen. One at a time: the notes measure text with the shared fonts and switch the language.
[TestClass]
[DoNotParallelize]
public class ToastTests
{
    static readonly Rectangle Screen = new(0, 0, 1920, 1040);

    long now = 1_000_000;

    Toast Note(string language = "en", Config? config = null)
    {
        Lang.Use(language);
        var toast = new Toast(headless: true)
        {
            Clock = () => now,
            Pointer = () => new Point(5000, 5000),
            Area = _ => Screen,
        };
        if (config is not null) toast.Use(config);
        return toast;
    }

    [TestCleanup]
    public void Language() => Lang.Use("auto");

    // ---- Where the note goes ----

    [TestMethod]
    public void EachPlaceIsThatCornerOfTheWorkAreaOrItsMiddle()
    {
        var size = new Size(300, 40);
        Assert.AreEqual(new Rectangle(1920 - 16 - 300, 1040 - 16 - 40, 300, 40), NotePlace.For(NotePosition.BottomRight, Screen, size, 16));
        Assert.AreEqual(new Rectangle(1920 - 16 - 300, 16, 300, 40), NotePlace.For(NotePosition.TopRight, Screen, size, 16));
        Assert.AreEqual(new Rectangle(16, 1040 - 16 - 40, 300, 40), NotePlace.For(NotePosition.BottomLeft, Screen, size, 16));
        Assert.AreEqual(new Rectangle((1920 - 300) / 2, (1040 - 40) / 2, 300, 40), NotePlace.For(NotePosition.Center, Screen, size, 16));
    }

    [TestMethod]
    public void OnAnotherMonitorThePlaceIsInThatMonitorsWorkArea()
    {
        var size = new Size(300, 40);
        // Left of the main screen and higher, with the taskbar on top.
        var left = new Rectangle(-1280, -200, 1280, 984);
        Assert.AreEqual(new Rectangle(-16 - 300, -200 + 984 - 16 - 40, 300, 40), NotePlace.For(NotePosition.BottomRight, left, size, 16));
        Assert.AreEqual(new Rectangle(-16 - 300, -200 + 16, 300, 40), NotePlace.For(NotePosition.TopRight, left, size, 16));
        Assert.AreEqual(new Rectangle(-1280 + 16, -200 + 984 - 16 - 40, 300, 40), NotePlace.For(NotePosition.BottomLeft, left, size, 16));
        Assert.AreEqual(new Rectangle(-1280 + (1280 - 300) / 2, -200 + (984 - 40) / 2, 300, 40), NotePlace.For(NotePosition.Center, left, size, 16));
        // Right of it, at 150%.
        var right = new Rectangle(1920, 0, 2560, 1380);
        Assert.AreEqual(new Rectangle(1920 + 2560 - 24 - 450, 1380 - 24 - 60, 450, 60), NotePlace.For(NotePosition.BottomRight, right, new Size(450, 60), 24));
    }

    // Top left: the work area's own top left corner, inside the same margin as the others, wherever
    // the taskbar is and on any monitor.
    [TestMethod]
    [DataRow(0, 0, 1920, 1040, 16, 300, 40, DisplayName = "taskbar at the bottom")]
    [DataRow(48, 0, 1872, 1080, 16, 300, 40, DisplayName = "taskbar on the left")]
    [DataRow(0, 48, 1920, 1032, 16, 300, 40, DisplayName = "taskbar on top")]
    [DataRow(-1280, -200, 1280, 984, 16, 300, 40, DisplayName = "a second monitor up and to the left")]
    [DataRow(-2560, 300, 2560, 1380, 24, 450, 60, DisplayName = "a monitor on the left at 150%")]
    public void TopLeftIsTheWorkAreasTopLeftCorner(int x, int y, int width, int height, int margin, int noteWidth, int noteHeight)
    {
        var area = new Rectangle(x, y, width, height);
        var size = new Size(noteWidth, noteHeight);
        var box = NotePlace.For(NotePosition.TopLeft, area, size, margin);
        Assert.AreEqual(new Rectangle(x + margin, y + margin, noteWidth, noteHeight), box);
        Assert.IsTrue(area.Contains(box));
        // The same distance from the edges as the corner beside it.
        var topRight = NotePlace.For(NotePosition.TopRight, area, size, margin);
        Assert.AreEqual(topRight.Top, box.Top);
        Assert.AreEqual(box.Left - area.Left, area.Right - topRight.Right);
        var bottomLeft = NotePlace.For(NotePosition.BottomLeft, area, size, margin);
        Assert.AreEqual(bottomLeft.Left, box.Left);
    }

    // The Appearance tab's order, and a name for each place in both languages.
    [TestMethod]
    public void EveryPlaceIsListedOnceInTheTabsOrderWithItsName()
    {
        CollectionAssert.AreEqual(
            new[] { NotePosition.BottomRight, NotePosition.BottomLeft, NotePosition.TopRight, NotePosition.TopLeft, NotePosition.Center },
            NotePlace.All.ToArray());
        CollectionAssert.AreEquivalent(Enum.GetValues<NotePosition>(), NotePlace.All.ToArray());
        foreach (var text in new[] { English.Text, Arabic.Text })
        {
            var labels = NotePlace.All.Select(p => NotePlace.Label(p, text)).ToList();
            CollectionAssert.AllItemsAreUnique(labels);
        }
        Assert.AreEqual("Top left", NotePlace.Label(NotePosition.TopLeft, English.Text));
    }

    // A note top left is top left in Arabic too: corners are the screen's, not mirrored.
    [TestMethod]
    public void TopLeftStaysTopLeftInArabic()
    {
        foreach (string language in new[] { "en", "ar" })
        {
            var toast = Note(language, new Config { NotificationPlace = NotePosition.TopLeft });
            toast.ShowCountdown(15, locking: true);
            Assert.AreEqual(NotePosition.TopLeft, toast.Place, language);
            Assert.AreEqual(Screen.Left + NotePlace.Margin, toast.NoteBounds.Left, language);
            Assert.AreEqual(Screen.Top + NotePlace.Margin, toast.NoteBounds.Top, language);
            toast.Dispose();
        }
    }

    [TestMethod]
    public void ANoteWiderThanTheScreenStartsAtItsEdgeAndStaysOnIt()
    {
        var small = new Rectangle(100, 0, 400, 300);
        foreach (var place in NotePlace.All)
        {
            var box = NotePlace.For(place, small, new Size(600, 40), 16);
            Assert.AreEqual(100, box.X, place.ToString());
            Assert.IsTrue(box.Bottom <= small.Bottom && box.Top >= small.Top, place.ToString());
        }
    }

    [TestMethod]
    public void WithNoPlacePickedARightToLeftShellPutsNotesBottomLeftAndAnyOtherBottomRight()
    {
        Assert.AreEqual(NotePosition.BottomLeft, NotePlace.InUse(null, shellRightToLeft: true));
        Assert.AreEqual(NotePosition.BottomRight, NotePlace.InUse(null, shellRightToLeft: false));
        foreach (var place in NotePlace.All)
        {
            Assert.AreEqual(place, NotePlace.InUse(place, shellRightToLeft: true));
            Assert.AreEqual(place, NotePlace.InUse(place, shellRightToLeft: false));
        }
    }

    [TestMethod]
    public void TheNoteGoesWhereTheSettingsSay()
    {
        var unpicked = new Config();
        Assert.IsNull(unpicked.NotificationPlace);
        var toast = Note(config: unpicked);
        Assert.AreEqual(NotePlace.InUse(null, Lang.ShellIsRightToLeft()), toast.Place);
        toast.ShowCountdown(15, locking: true);
        var box = toast.NoteBounds;
        Assert.AreEqual(Screen.Bottom - NotePlace.Margin, box.Bottom);
        Assert.AreEqual(toast.Place == NotePosition.BottomLeft ? Screen.Left + NotePlace.Margin : Screen.Right - NotePlace.Margin,
            toast.Place == NotePosition.BottomLeft ? box.Left : box.Right);

        // Picking a place moves a note that is showing.
        toast.Use(new Config { NotificationPlace = NotePosition.TopRight });
        Assert.AreEqual(NotePosition.TopRight, toast.Place);
        Assert.AreEqual(Screen.Top + NotePlace.Margin, toast.NoteBounds.Top);
        Assert.AreEqual(Screen.Right - NotePlace.Margin, toast.NoteBounds.Right);
        toast.Dispose();
    }

    [TestMethod]
    public void CornersAreTheScreensOwnInArabicToo()
    {
        foreach (var language in new[] { "en", "ar" })
        {
            var toast = Note(language, new Config { NotificationPlace = NotePosition.BottomRight });
            toast.ShowCountdown(15, locking: true);
            Assert.AreEqual(Screen.Right - NotePlace.Margin, toast.NoteBounds.Right, language);
            toast.Use(new Config { NotificationPlace = NotePosition.BottomLeft });
            Assert.AreEqual(Screen.Left + NotePlace.Margin, toast.NoteBounds.Left, language);
            toast.Dispose();
        }
    }

    [TestMethod]
    public void ArabicMirrorsTheInsideOfThePill()
    {
        var en = Note("en");
        en.ShowCountdown(15, locking: true);
        var ltr = en.Drawn;
        Assert.IsTrue(ltr.Logo.Right <= ltr.Title.Left && ltr.Title.Right <= ltr.Detail.Left, "logo, title, detail from the left");

        var ar = Note("ar");
        ar.ShowCountdown(15, locking: true);
        var rtl = ar.Drawn;
        Assert.IsTrue(rtl.RightToLeft);
        Assert.IsTrue(rtl.Logo.Left >= rtl.Title.Right && rtl.Title.Left >= rtl.Detail.Right, "logo, title, detail from the right");
        Assert.AreEqual(rtl.Size.Width - rtl.Logo.Right, ltr.Logo.Left);
        en.Dispose();
        ar.Dispose();
    }

    // ---- What the notes say ----

    [TestMethod]
    public void TheCountdownSaysTheSecondsInBoldAndHowToCancelInMutedText()
    {
        var toast = Note();
        var text = Lang.Current;
        toast.ShowCountdown(15, locking: true);
        Assert.IsTrue(toast.IsCounting);
        Assert.AreEqual(text.CountdownNote(15), toast.Title + " " + toast.Detail);
        Assert.StartsWith("Locking in 15", toast.Title);
        Assert.StartsWith("·", toast.Detail);
        Assert.IsFalse(toast.SnoozeShown, "the countdown has no button");

        int wide = toast.NoteBounds.Width;
        int right = toast.NoteBounds.Right;
        toast.SetSeconds(9);
        Assert.AreEqual(text.CountdownNote(9), toast.Title + " " + toast.Detail);
        Assert.AreEqual(wide, toast.NoteBounds.Width, "a countdown never gets narrower");
        Assert.AreEqual(right, toast.NoteBounds.Right);

        toast.EndCountdown();
        Assert.IsFalse(toast.IsShowing);
        toast.Dispose();
    }

    [TestMethod]
    public void WithLockingOffTheCountdownIsForTheAwayActions()
    {
        var toast = Note();
        toast.ShowCountdown(15, locking: false);
        Assert.AreEqual(Lang.Current.CountdownNote(15, lockingOn: false), toast.Title + " " + toast.Detail);
        toast.Dispose();
    }

    [TestMethod]
    public void TimeAwaySaysWhenMusicOrVideoIsPlayingAgain()
    {
        var toast = Note();
        var text = Lang.Current;
        toast.ShowAway(TimeSpan.FromMinutes(42), playingAgain: false);
        Assert.AreEqual(text.YouWereAwayFor(TimeSpan.FromMinutes(42)), toast.Title);
        Assert.AreEqual("", toast.Detail);
        toast.ShowAway(TimeSpan.FromMinutes(42), playingAgain: true);
        Assert.AreEqual(text.V1.MediaPlayingAgain, toast.Detail);
        Assert.IsFalse(toast.SnoozeShown);
        toast.Dispose();
    }

    [TestMethod]
    public void BreaksAndEyeBreaksSayTheirLinesAndGetSnoozeOnlyWhenItIsOn()
    {
        var off = Note(config: new Config { SnoozeMinutes = 0 });
        var text = Lang.Current;
        off.ShowReminder(new Reminder(ReminderKind.Break, 3600));
        Assert.AreEqual(text.V1.TimeForABreak, off.Title);
        Assert.AreEqual(text.AtThePcFor(TimeSpan.FromHours(1)), off.Detail);
        Assert.IsFalse(off.SnoozeShown);
        Assert.IsTrue(off.SnoozeBox.IsEmpty);

        var on = Note(config: new Config { SnoozeMinutes = 10 });
        on.ShowReminder(new Reminder(ReminderKind.EyeBreak, 1200));
        Assert.AreEqual(text.V1.EyeBreakTitle, on.Title);
        Assert.AreEqual("· " + text.V1.EyeBreakText, on.Detail);
        Assert.IsTrue(on.SnoozeShown);
        Assert.IsFalse(on.SnoozeBox.IsEmpty);
        Assert.IsTrue(new Rectangle(Point.Empty, on.NoteBounds.Size).Contains(on.SnoozeBox), "Snooze is inside the pill");
        off.Dispose();
        on.Dispose();
    }

    [TestMethod]
    public void NothingIsCutShortInEitherLanguage()
    {
        foreach (var language in new[] { "en", "ar" })
        {
            var toast = Note(language, new Config { SnoozeMinutes = 10 });
            void Whole(string what)
            {
                var drawn = toast.Drawn;
                Assert.AreEqual(drawn.TitleNeeds.Width, drawn.Title.Width, $"{language} {what} title");
                Assert.AreEqual(drawn.DetailNeeds.Width, drawn.Detail.Width, $"{language} {what} detail");
            }
            toast.ShowCountdown(15, locking: true);
            Whole("countdown");
            toast.EndCountdown();
            Assert.IsTrue(toast.ShowPreview());
            Whole("preview");
            toast.ShowAway(TimeSpan.FromMinutes(42), playingAgain: true);
            Whole("away");
            toast.ShowReminder(new Reminder(ReminderKind.Break, 3600));
            Whole("break");
            toast.ShowReminder(new Reminder(ReminderKind.EyeBreak, 1200));
            Whole("eye break");
            toast.Dispose();
        }
    }

    [TestMethod]
    public void ANoteGoesAwayByItselfButNotWhileThePointerIsOnIt()
    {
        var toast = Note();
        toast.ShowAway(TimeSpan.FromMinutes(42), playingAgain: false);
        now += 7900;
        toast.Step();
        Assert.IsTrue(toast.IsShowing);

        var on = new Point(toast.NoteBounds.X + 5, toast.NoteBounds.Y + 20);
        toast.Pointer = () => on;
        for (int i = 0; i < 100; i++)
        {
            now += 50;
            toast.Step();
        }
        Assert.IsTrue(toast.IsShowing, "held while the pointer is on it");

        toast.Pointer = () => new Point(5000, 5000);
        now += 1100;
        toast.Step();
        Assert.IsFalse(toast.IsShowing);
        toast.Dispose();
    }

    [TestMethod]
    public void APointerRestingWhereTheNoteComesUpDoesNotKeepIt()
    {
        foreach (var place in new[] { NotePosition.Center, NotePosition.BottomRight })
        {
            var toast = Note(config: new Config { NotificationPlace = place, SnoozeMinutes = 10 });
            toast.ShowReminder(new Reminder(ReminderKind.Break, 3600));
            var middle = new Point(toast.NoteBounds.X + toast.NoteBounds.Width / 2, toast.NoteBounds.Y + toast.NoteBounds.Height / 2);
            toast.HideNote(toast.Title);
            Assert.IsFalse(toast.IsShowing);

            // Reading or watching: the pointer is parked where the note appears and never moves.
            toast.Pointer = () => middle;
            toast.ShowReminder(new Reminder(ReminderKind.Break, 3600));
            Assert.IsTrue(toast.NoteBounds.Contains(middle), place.ToString());
            for (int i = 0; i < 25 * 20; i++)
            {
                now += 50;
                toast.Step();
            }
            Assert.IsFalse(toast.IsShowing, $"{place}: gone after its 20 seconds");

            // Once the pointer moves onto it, it stays until the pointer leaves.
            toast.ShowReminder(new Reminder(ReminderKind.Break, 3600));
            var nudged = new Point(middle.X + 3, middle.Y);
            toast.Pointer = () => nudged;
            for (int i = 0; i < 25 * 20; i++)
            {
                now += 50;
                toast.Step();
            }
            Assert.IsTrue(toast.IsShowing, $"{place}: held once the pointer moved onto it");
            // Its time stood still while held; it runs again once the pointer leaves.
            toast.Pointer = () => new Point(5000, 5000);
            for (int i = 0; i < 21 * 20; i++)
            {
                now += 50;
                toast.Step();
            }
            Assert.IsFalse(toast.IsShowing, $"{place}: gone once the pointer has left");
            toast.Dispose();
        }
    }

    // A lasting note does not go by itself and its line does not run down: it stays until it is taken
    // back or clicked away, as the one saying the locking may not work.
    [TestMethod]
    public void ALastingNoteStaysUntilTakenBackOrClickedAway()
    {
        var toast = Note();
        toast.ShowLastingNote("Stays", "until it is taken back");
        now += (long)TimeSpan.FromHours(3).TotalMilliseconds;
        toast.Step();
        Assert.IsTrue(toast.IsShowing, "still there hours later");
        Assert.AreEqual(1, toast.LineLeft(), "its line full");
        toast.HideNote("Stays");
        Assert.IsFalse(toast.IsShowing, "taken back");

        toast.ShowLastingNote("Stays", "until it is clicked away");
        now += (long)TimeSpan.FromMinutes(5).TotalMilliseconds;
        toast.Step();
        toast.Clicked(new Point(10, 10));
        Assert.IsFalse(toast.IsShowing, "clicked away");

        // A note after it goes by itself as before, and the lasting one comes back: what it says is
        // still true.
        toast.ShowLastingNote("Stays", "until another takes its place");
        toast.ShowAway(TimeSpan.FromMinutes(42), playingAgain: false);
        Assert.AreNotEqual("Stays", toast.Title, "the note for coming back shows");
        now += 8100;
        toast.Step();
        Assert.IsTrue(toast.IsShowing, "the lasting note is back after the other's 8 seconds");
        Assert.AreEqual("Stays", toast.Title);
        Assert.AreEqual("· until another takes its place", toast.Detail);
        Assert.AreEqual(1, toast.LineLeft(), "lasting again, its line full");
        toast.Dispose();
    }

    // The note that the locking may not work stays true through a countdown, Try it or another note,
    // so it comes back after each of them, and also when it came while the countdown ran. Taken back
    // meanwhile, it does not come back.
    [TestMethod]
    public void ALastingNoteComesBackAfterWhatTookItsPlaceUnlessItWasTakenBack()
    {
        var toast = Note();
        toast.ShowLastingNote("Stays", "while it is true");
        toast.ShowCountdown(15, locking: true);
        Assert.IsTrue(toast.IsCounting);
        toast.EndCountdown();
        Assert.AreEqual("Stays", toast.Title, "back after the countdown");

        Assert.IsTrue(toast.ShowPreview());
        now += Toast.PreviewSeconds * 1000L + 100;
        toast.Step();
        Assert.AreEqual("Stays", toast.Title, "back after Try it");

        toast.ShowReminder(new Reminder(ReminderKind.Break, 3600));
        toast.Clicked(new Point(10, 10));
        Assert.AreEqual("Stays", toast.Title, "back after the reminder was clicked away");
        toast.Clicked(new Point(10, 10));
        Assert.IsFalse(toast.IsShowing, "clicked away itself, it stays away");

        toast.ShowCountdown(15, locking: true);
        toast.ShowLastingNote("Stays", "came during the countdown");
        Assert.IsTrue(toast.IsCounting, "the countdown keeps its place");
        toast.EndCountdown();
        Assert.AreEqual("· came during the countdown", toast.Detail, "shown once the countdown is over");

        toast.ShowNote("Something", "else", 8);
        toast.HideNote("Stays");
        now += 8100;
        toast.Step();
        Assert.IsFalse(toast.IsShowing, "taken back while it waited, so it does not come back");
        toast.Dispose();
    }

    [TestMethod]
    public void ANoteNeverReplacesTheCountdownAndHideNoteTakesAwayOnlyItsOwnNote()
    {
        var toast = Note();
        toast.ShowCountdown(15, locking: true);
        toast.ShowNote("Something", "else", 8);
        toast.ShowReminder(new Reminder(ReminderKind.Break, 3600));
        Assert.IsTrue(toast.IsCounting);
        toast.HideNote(toast.Title);
        Assert.IsTrue(toast.IsCounting, "HideNote never takes the countdown away");
        toast.EndCountdown();

        toast.ShowNote("First", "the old adapter", 8);
        Assert.AreEqual("First", toast.Title);
        Assert.AreEqual("· the old adapter", toast.Detail);
        toast.HideNote("Second");
        Assert.IsTrue(toast.IsShowing);
        toast.HideNote("First");
        Assert.IsFalse(toast.IsShowing);
        toast.Dispose();
    }

    // ---- Try it ----

    [TestMethod]
    public void TryItShowsTheCountdownNoteForFiveSecondsAndEndsByItself()
    {
        var toast = Note();
        int snoozed = 0;
        toast.SnoozeClicked += _ => snoozed++;

        Assert.IsTrue(toast.ShowPreview());
        Assert.IsTrue(toast.IsPreviewing);
        Assert.IsFalse(toast.IsCounting);
        Assert.AreEqual(Lang.Current.PreviewNote(5), toast.Title + " " + toast.Detail);
        Assert.AreEqual(1.0, toast.LineLeft(), 1e-9);

        now += 2100;
        toast.Step();
        Assert.AreEqual(Lang.Current.PreviewNote(2.9), toast.Title + " " + toast.Detail);
        // Clicking it does nothing; the countdown's own end does not end it.
        toast.Clicked(new Point(toast.NoteBounds.Width / 2, toast.NoteBounds.Height / 2));
        toast.EndCountdown();
        Assert.IsTrue(toast.IsPreviewing);

        now += 2899;
        toast.Step();
        Assert.IsTrue(toast.IsPreviewing);
        now += 1;
        toast.Step();
        Assert.IsFalse(toast.IsShowing);
        Assert.AreEqual(1, toast.PreviewsEnded);
        Assert.AreEqual(0, snoozed);
        toast.Dispose();
    }

    [TestMethod]
    public void TryItNeverReachesTheLockPath()
    {
        // The note knows nothing that locks: no watcher, no engine, no session lock.
        var fields = typeof(Toast).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        foreach (var field in fields)
        {
            Assert.AreNotEqual(typeof(Watcher), field.FieldType, field.Name);
            Assert.AreNotEqual(typeof(LockEngine), field.FieldType, field.Name);
        }

        bool blocked = SessionLock.Blocked;
        SessionLock.Blocked = true;
        try
        {
            var toast = Note();
            toast.ShowPreview();
            for (int i = 0; i < 120; i++)
            {
                now += 50;
                toast.Step();
                toast.Clicked(new Point(10, 10));
            }
            Assert.IsFalse(toast.IsShowing);
            toast.Dispose();
        }
        finally
        {
            SessionLock.Blocked = blocked;
        }
    }

    [TestMethod]
    public void ARealCountdownTakesOverFromTryIt()
    {
        var toast = Note();
        toast.ShowPreview();
        now += 1000;
        toast.ShowCountdown(15, locking: true);
        Assert.IsTrue(toast.IsCounting);
        Assert.AreEqual(1, toast.PreviewsEnded);
        Assert.AreEqual(Lang.Current.CountdownNote(15), toast.Title + " " + toast.Detail);

        // The preview's 5 seconds passing does nothing to the countdown.
        now += 5000;
        toast.Step();
        Assert.IsTrue(toast.IsCounting);
        Assert.AreEqual(1, toast.PreviewsEnded);

        // And Try it during a countdown does nothing.
        Assert.IsFalse(toast.ShowPreview());
        Assert.IsTrue(toast.IsCounting);
        Assert.AreEqual(1, toast.PreviewsEnded);
        toast.Dispose();
    }

    [TestMethod]
    public void ANoteTakesOverFromTryIt()
    {
        var toast = Note();
        toast.ShowPreview();
        toast.ShowAway(TimeSpan.FromMinutes(42), playingAgain: false);
        Assert.AreEqual(1, toast.PreviewsEnded);
        Assert.IsFalse(toast.IsPreviewing);
        Assert.IsTrue(toast.IsShowing);
        toast.Dispose();
    }

    // ---- Snooze ----

    [TestMethod]
    public void SnoozeSaysWhichReminderAndIsApartFromTheCountdown()
    {
        var toast = Note(config: new Config { SnoozeMinutes = 30 });
        var snoozed = new List<ReminderKind>();
        toast.SnoozeClicked += snoozed.Add;

        toast.ShowReminder(new Reminder(ReminderKind.EyeBreak, 1200));
        var box = toast.SnoozeBox;
        toast.Clicked(new Point(box.X + box.Width / 2, box.Y + box.Height / 2));
        Assert.IsFalse(toast.IsShowing);

        toast.ShowReminder(new Reminder(ReminderKind.Break, 3600));
        toast.PressSnooze();
        CollectionAssert.AreEqual(new[] { ReminderKind.EyeBreak, ReminderKind.Break }, snoozed);

        // The countdown has nothing to snooze, wherever it is clicked.
        toast.ShowCountdown(15, locking: true);
        toast.PressSnooze();
        toast.Clicked(new Point(toast.NoteBounds.Width - 20, toast.NoteBounds.Height / 2));
        Assert.IsTrue(toast.IsCounting);
        Assert.HasCount(2, snoozed);

        // The time-away note has no Snooze; a click puts it away.
        toast.EndCountdown();
        toast.ShowAway(TimeSpan.FromMinutes(5), playingAgain: false);
        toast.PressSnooze();
        Assert.IsTrue(toast.IsShowing);
        toast.Clicked(new Point(20, 20));
        Assert.IsFalse(toast.IsShowing);
        Assert.HasCount(2, snoozed);
        toast.Dispose();
    }

    [TestMethod]
    public void TurningSnoozeOnOrOffChangesTheReminderThatIsShowing()
    {
        var toast = Note(config: new Config { SnoozeMinutes = 0 });
        toast.ShowReminder(new Reminder(ReminderKind.Break, 3600));
        Assert.IsFalse(toast.SnoozeShown);
        toast.Use(new Config { SnoozeMinutes = 10 });
        Assert.IsTrue(toast.SnoozeShown);
        Assert.IsFalse(toast.SnoozeBox.IsEmpty);
        toast.Use(new Config { SnoozeMinutes = 0 });
        Assert.IsFalse(toast.SnoozeShown);
        Assert.IsTrue(toast.SnoozeBox.IsEmpty);

        // The time-away note never gets one.
        toast.ShowAway(TimeSpan.FromMinutes(5), playingAgain: false);
        toast.Use(new Config { SnoozeMinutes = 30 });
        Assert.IsFalse(toast.SnoozeShown);
        toast.Dispose();
    }

    [TestMethod]
    public void AScreenReaderFindsSnoozeAsAButtonAndCanPressIt()
    {
        var toast = Note(config: new Config { SnoozeMinutes = 10 });
        var snoozed = new List<ReminderKind>();
        toast.SnoozeClicked += snoozed.Add;
        toast.ShowReminder(new Reminder(ReminderKind.Break, 3600));

        var note = toast.AccessibilityObject;
        Assert.AreEqual(toast.Spoken, note.Name);
        Assert.AreEqual(1, note.GetChildCount());
        var button = note.GetChild(0)!;
        Assert.AreEqual(AccessibleRole.PushButton, button.Role);
        Assert.AreEqual(Lang.Current.V1.SnoozeButton, button.Name);
        button.DoDefaultAction();
        CollectionAssert.AreEqual(new[] { ReminderKind.Break }, snoozed);
        Assert.AreEqual(0, note.GetChildCount());
        toast.Dispose();
    }

    // ---- The look ----

    [TestMethod]
    public void TheCountdownLineIsTheAccentAndTheCornersSeeThrough()
    {
        var toast = Note();
        toast.ShowCountdown(15, locking: true);
        using var picture = toast.Picture();
        Assert.AreEqual(toast.NoteBounds.Size, picture.Size);
        Assert.AreEqual(0, picture.GetPixel(0, 0).A, "round ends");
        Assert.AreEqual(0, picture.GetPixel(picture.Width - 1, picture.Height - 1).A);
        var line = picture.GetPixel(picture.Width / 2, picture.Height - 2);
        Assert.AreEqual(Theme.Current.Accent.ToArgb(), line.ToArgb(), "the full line is the accent");
        Assert.AreEqual(Theme.Current.Edge.ToArgb(), picture.GetPixel(picture.Width / 2, 0).ToArgb(), "the card's thin edge");
        toast.Dispose();
    }

    static readonly (string What, Action<Toast> Show)[] Everything =
    [
        ("the countdown", t => { t.ShowCountdown(15, locking: true); t.SetSeconds(9); }),
        ("the away countdown", t => t.ShowCountdown(15, locking: false)),
        ("Try it", t => t.ShowPreview()),
        ("time away", t => t.ShowAway(TimeSpan.FromMinutes(42), playingAgain: true)),
        ("a break", t => t.ShowReminder(new Reminder(ReminderKind.Break, 3600))),
        ("an eye break", t => t.ShowReminder(new Reminder(ReminderKind.EyeBreak, 1200))),
        ("the settings file", t => t.ShowNote(Lang.Current.ConfigNotUsed, Lang.Current.ConfigDefaultsUntilFixed, 15)),
        ("the save", t => t.ShowNote(Lang.Current.V1.SaveFailedTitle, Lang.Current.V1.SaveFailedDetail, 15)),
        ("the lasting note", t => t.ShowLastingNote(Lang.Current.V1.TicksFailingTitle, Lang.Current.V1.TicksFailingDetail)),
    ];

    static bool Near(Color a, Color b, int within) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) <= within;

    static int Count(Bitmap picture, Rectangle box, Func<Color, bool> what)
    {
        int n = 0;
        for (int y = Math.Max(0, box.Top); y < Math.Min(picture.Height, box.Bottom); y++)
        {
            for (int x = Math.Max(0, box.Left); x < Math.Min(picture.Width, box.Right); x++)
            {
                var c = picture.GetPixel(x, y);
                if (c.A == 255 && what(c)) n++;
            }
        }
        return n;
    }

    // Every corner note looks the same, in light and dark: the card's thin neutral edge, the line in
    // the accent from the reading start on a soft accent track, and nothing in the warning color,
    // which is only for the shortcut dialog.
    [TestMethod]
    public void EveryNoteHasTheCardEdgeAndTheAccentLineAndNoWarningColor()
    {
        try
        {
            foreach (bool dark in new[] { false, true })
            {
                Theme.UseForShots(dark, 1f);
                var t = Theme.Current;
                foreach (var language in new[] { "en", "ar" })
                {
                    bool rtl = language == "ar";
                    foreach (var (what, show) in Everything)
                    {
                        var toast = Note(language, new Config { SnoozeMinutes = 10 });
                        show(toast);
                        now += toast.IsPreviewing ? 2000 : toast.IsCounting ? 0 : 3000;
                        toast.Step();
                        using var picture = toast.Picture();
                        string about = $"{what}, {language}, {(dark ? "dark" : "light")}";
                        Assert.AreEqual(t.Edge.ToArgb(), picture.GetPixel(picture.Width / 2, 0).ToArgb(), $"{about}: the card's edge at the top");
                        Assert.AreEqual(t.Edge.ToArgb(), picture.GetPixel(picture.Width / 2, picture.Height - 1).ToArgb(), $"{about}: the card's edge at the bottom");
                        int lineY = picture.Height - 2;
                        int start = rtl ? picture.Width - picture.Height / 2 - 2 : picture.Height / 2 + 2;
                        int end = rtl ? picture.Height / 2 + 2 : picture.Width - picture.Height / 2 - 2;
                        Assert.AreEqual(t.Accent.ToArgb(), picture.GetPixel(start, lineY).ToArgb(), $"{about}: the line from the reading start in the accent");
                        if (toast.LineLeft() < 0.9)
                            Assert.AreEqual(t.AccentSoft.ToArgb(), picture.GetPixel(end, lineY).ToArgb(), $"{about}: the line's soft track at the reading end");
                        Assert.AreEqual(0, Count(picture, new Rectangle(Point.Empty, picture.Size), c => Near(c, t.Warning, 12)), $"{about}: nothing in the warning color");
                        toast.Dispose();
                    }
                }
            }
        }
        finally
        {
            Theme.UseForShots(false, 1f);
        }
    }

    // The time is found from where {0} is in the line's template, never by looking for digits.
    [TestMethod]
    public void TheTimeIsFoundFromTheTemplate()
    {
        var (title, detail, time) = Toast.Timed("Locking in {0} · move the mouse", "15 sec");
        Assert.AreEqual("Locking in 15 sec", title);
        Assert.AreEqual("· move the mouse", detail);
        Assert.AreEqual((11, 6), time);
        // Digits before the time are not the time.
        Assert.AreEqual((7, 5), Toast.Timed("Step 2 {0} left", "3 min").Time);
        // Braces written twice before it are one brace in the line.
        Assert.AreEqual((4, 5), Toast.Timed("{{a}} {0}", "9 sec").Time);
        Assert.AreEqual("{a} 9 sec", Toast.Timed("{{a}} {0}", "9 sec").Title);
        // A time in the muted part, or none at all, leaves the title without one.
        Assert.AreEqual(default, Toast.Timed("Time for a break · {0} at the PC", "1 hr").Time);
        Assert.AreEqual(default, Toast.Timed("Time for a break", "1 hr").Time);

        var ar = Lang.For("ar");
        string seconds = ar.ShortLeft(9);
        var (arTitle, _, arTime) = Toast.Timed(ar.V1.CountdownNote, seconds);
        Assert.AreEqual(seconds, arTitle.Substring(arTime.At, arTime.Length));
        Assert.AreEqual(ar.V1.CountdownNote.IndexOf("{0}", StringComparison.Ordinal), arTime.At);
    }

    // The title is in the text color with only the time in the accent: at the end of the title in
    // English, at its left end in Arabic, which reads from the right. A title without a time has no
    // accent at all.
    [TestMethod]
    public void OnlyTheTimeInTheTitleIsInTheAccent()
    {
        try
        {
            foreach (bool dark in new[] { false, true })
            {
                Theme.UseForShots(dark, 1f);
                var t = Theme.Current;
                foreach (var language in new[] { "en", "ar" })
                {
                    bool rtl = language == "ar";
                    foreach (var (what, show, timed) in new (string, Action<Toast>, bool)[]
                    {
                        ("the countdown at 9", n => { n.ShowCountdown(15, locking: true); n.SetSeconds(9); }, true),
                        ("Try it", n => n.ShowPreview(), true),
                        ("time away", n => n.ShowAway(TimeSpan.FromMinutes(42), playingAgain: false), true),
                        ("a break", n => n.ShowReminder(new Reminder(ReminderKind.Break, 3600)), false),
                        ("the settings file", n => n.ShowNote(Lang.Current.ConfigNotUsed, Lang.Current.ConfigDefaultsUntilFixed, 15), false),
                    })
                    {
                        var toast = Note(language);
                        show(toast);
                        var drawn = toast.Drawn;
                        using var picture = toast.Picture();
                        string about = $"{what}, {language}, {(dark ? "dark" : "light")}";
                        var title = drawn.Title;
                        // Green: more green than red and blue, and more blue than red, as the accent and its
                        // blends with the card are, and well away from the card; the faint colored edges smoothed
                        // text has are not.
                        bool Accent(Color c) => !Near(c, t.Card, 150) && c.G > c.R + 20 && c.G > c.B + 8 && c.B > c.R + 15;
                        bool Text(Color c) => Near(c, t.Text, 60);
                        if (!timed)
                        {
                            Assert.IsTrue(drawn.Time.IsEmpty, $"{about}: no time in the title");
                            Assert.IsLessThan(Count(picture, title, Text) / 10 + 1, Count(picture, title, Accent), $"{about}: no accent in the title, only the odd green edge of smoothed text");
                            Assert.IsGreaterThan(0, Count(picture, title, Text), $"{about}: the title in the text color");
                            toast.Dispose();
                            continue;
                        }
                        var time = drawn.Time;
                        Assert.IsFalse(time.IsEmpty, $"{about}: the time is marked");
                        Assert.IsTrue(time.Left >= title.Left && time.Right <= title.Right && time.Width < title.Width, $"{about}: the time is part of the title");
                        if (rtl) Assert.AreEqual(title.Left, time.Left, $"{about}: the time ends the title, on its left");
                        else Assert.AreEqual(title.Right, time.Right, $"{about}: the time ends the title, on its right");
                        var inTime = Rectangle.FromLTRB(time.Left, title.Top, time.Right, title.Bottom);
                        var rest = rtl ? Rectangle.FromLTRB(time.Right, title.Top, title.Right, title.Bottom) : Rectangle.FromLTRB(title.Left, title.Top, time.Left, title.Bottom);
                        Assert.IsGreaterThan(9, Count(picture, inTime, Accent), $"{about}: the time in the accent");
                        Assert.AreEqual(0, Count(picture, inTime, Text), $"{about}: nothing of the time in the text color");
                        // Its columns from the top edge down to the line: every pixel is the card, the accent or
                        // between them, red, green and blue each, so a mark that hangs below the title box, as
                        // under the last letter of the Arabic word for seconds, is in the accent too and never
                        // darker or in the text color.
                        var columns = Rectangle.FromLTRB(time.Left, 1, time.Right, drawn.Size.Height - 3);
                        static bool Between(int c, int a, int b) => c >= Math.Min(a, b) - 2 && c <= Math.Max(a, b) + 2;
                        bool CardToAccent(Color c) => Between(c.R, t.Card.R, t.Accent.R) && Between(c.G, t.Card.G, t.Accent.G) && Between(c.B, t.Card.B, t.Accent.B);
                        Assert.AreEqual(0, Count(picture, columns, c => !CardToAccent(c)), $"{about}: the time's columns, above and below the title box, only the card and the accent");
                        Assert.AreEqual(0, Count(picture, columns, Text), $"{about}: nothing of the time in the text color, above or below the title box");
                        Assert.IsGreaterThan(0, Count(picture, rest, Text), $"{about}: the words before it in the text color");
                        Assert.IsLessThan(Count(picture, rest, Text) / 10 + 1, Count(picture, rest, Accent), $"{about}: nothing before it in the accent, only the odd green edge of smoothed text");
                        toast.Dispose();
                    }
                }
            }
        }
        finally
        {
            Theme.UseForShots(false, 1f);
        }
    }

    // A one-line note is about 30 px tall at 100% with 9 pt words, twice that at 200%, and taller
    // still with larger text; the words are never cut short or outside the pill, in English and in
    // Arabic.
    [TestMethod]
    public void TheNotesAreSmallAndScaleWithTheMonitorAndTheTextSize()
    {
        try
        {
            foreach (float scale in new[] { 1f, 1.5f })
            {
                Theme.UseForShots(false, scale);
                foreach (var language in new[] { "en", "ar" })
                {
                    foreach (int dpi in new[] { 96, 144, 192 })
                    {
                        foreach (var (what, show) in Everything)
                        {
                            var toast = Note(language, new Config { SnoozeMinutes = 10 });
                            toast.Dpi = _ => dpi;
                            show(toast);
                            var drawn = toast.Drawn;
                            string about = $"{what}, {language}, {dpi} dpi, text {scale}";
                            var inside = new Rectangle(Point.Empty, drawn.Size);
                            Assert.AreEqual(drawn.TitleNeeds.Width, drawn.Title.Width, $"{about}: the whole title");
                            if (!drawn.Wrapped) Assert.AreEqual(drawn.DetailNeeds.Width, drawn.Detail.Width, $"{about}: the whole detail");
                            Assert.IsTrue(inside.Contains(drawn.Title) && inside.Contains(drawn.Logo) && (drawn.Detail.IsEmpty || inside.Contains(drawn.Detail)), $"{about}: inside the pill");
                            if (!drawn.Snooze.IsEmpty) Assert.IsTrue(inside.Contains(drawn.Snooze), $"{about}: Snooze inside the pill");
                            Assert.AreEqual((int)Math.Round(18 * dpi / 96.0), drawn.Logo.Width, $"{about}: the logo in proportion");
                            if (!drawn.Wrapped && scale == 1f)
                            {
                                int expected = (int)Math.Round(30 * dpi / 96.0);
                                Assert.IsLessThanOrEqualTo(dpi / 96, Math.Abs(drawn.Size.Height - expected), $"{about}: about 30 px at 100% ({drawn.Size.Height}, want {expected})");
                            }
                            if (scale > 1f) Assert.IsGreaterThan((int)Math.Round(30 * dpi / 96.0), drawn.Size.Height, $"{about}: taller with larger text");
                            using var picture = toast.Picture();
                            Assert.AreEqual(drawn.Size, picture.Size, $"{about}: drawn at its size");
                            toast.Dispose();
                        }
                    }
                }
            }
        }
        finally
        {
            Theme.UseForShots(false, 1f);
        }
    }

    [TestMethod]
    public void ArabicMarksBelowTheLineAreDrawn()
    {
        // 9 seconds is ثوانٍ, whose kasratan reaches below the box the font reports for the title.
        var toast = Note("ar");
        toast.ShowCountdown(15, locking: true);
        toast.SetSeconds(9);
        StringAssert.Contains(toast.Title, "ثوانٍ");
        var drawn = toast.Drawn;
        using var picture = toast.Picture();
        var card = Theme.Current.Card;
        int lineTop = picture.Height - 4;
        int ink = 0;
        for (int y = drawn.Title.Bottom; y < lineTop; y++)
        {
            for (int x = drawn.Title.Left; x < drawn.Title.Right; x++)
            {
                var c = picture.GetPixel(x, y);
                if (Math.Abs(c.R - card.R) + Math.Abs(c.G - card.G) + Math.Abs(c.B - card.B) > 120) ink++;
            }
        }
        Assert.IsLessThan(lineTop, drawn.Title.Bottom, "there is room below the title");
        Assert.IsGreaterThan(0, ink, "the kasratan below the title is drawn, not cut off");
        toast.Dispose();
    }

    // The marks below Arabic words, such as the kasratan of ثوانٍ in Try it, keep a clear row of the
    // card between them and the line along the bottom, at every text size up to Windows' 225% (I38).
    // The words are looked at between the pill's round ends, whose outline curves down to the line.
    [TestMethod]
    public void ArabicMarksBelowTheWordsStayClearOfTheLine()
    {
        try
        {
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 1.75f, 2f, 2.25f })
            {
                Theme.UseForShots(false, scale);
                foreach (var (what, show) in new (string, Action<Toast>)[]
                {
                    ("Try it", t => t.ShowPreview()),
                    ("the countdown at 9", t => { t.ShowCountdown(15, locking: true); t.SetSeconds(9); }),
                })
                {
                    var toast = Note("ar");
                    show(toast);
                    var drawn = toast.Drawn;
                    using var picture = toast.Picture();
                    // A headless note is at 96 dpi: a 1 px border under a 2 px line.
                    int lineTop = picture.Height - 3;
                    var card = Theme.Current.Card;
                    int lowest = -1;
                    var words = Rectangle.Union(drawn.Title, drawn.Detail.Width > 0 ? drawn.Detail : drawn.Title);
                    int round = picture.Height / 2;
                    for (int y = words.Top; y < lineTop; y++)
                    {
                        for (int x = Math.Max(round, words.Left); x < Math.Min(picture.Width - round, words.Right); x++)
                        {
                            var c = picture.GetPixel(x, y);
                            if (Math.Abs(c.R - card.R) + Math.Abs(c.G - card.G) + Math.Abs(c.B - card.B) > 60) lowest = y;
                        }
                    }
                    Assert.IsGreaterThan(words.Bottom - 4, lowest, $"{what} at {scale}: the words are drawn");
                    Assert.IsLessThan(lineTop - 1, lowest, $"{what} at {scale}: a clear row above the line (lowest ink {lowest}, line from {lineTop})");
                    toast.Dispose();
                }
            }
        }
        finally
        {
            Theme.UseForShots(false, 1f);
        }
    }

    [TestMethod]
    public void TheSettingsFileNotesShowTheirWholeDetail()
    {
        foreach (var language in new[] { "en", "ar" })
        {
            var toast = Note(language);
            var text = Lang.Current;
            foreach (var (noteTitle, noteBody) in new[]
            {
                (text.ConfigNotUsed, text.ConfigDefaultsUntilFixed),
                (text.ConfigNotUsed, text.ConfigEarlierUntilFixed),
                (text.ConfigSkipped, text.ConfigSkippedDetail),
            })
            {
                toast.ShowNote(noteTitle, noteBody, seconds: 15);
                var drawn = toast.Drawn;
                string what = $"{language} {noteTitle}";
                // English runs to more lines; the shorter Arabic may fit on one, whole.
                Assert.IsTrue(drawn.Wrapped || language == "ar", $"{what}: the detail goes below the title");
                Assert.AreEqual(drawn.TitleNeeds.Width, drawn.Title.Width, $"{what}: whole title");
                if (!drawn.Wrapped)
                {
                    Assert.AreEqual(drawn.DetailNeeds.Width, drawn.Detail.Width, $"{what}: whole detail on the one line");
                    Assert.IsTrue(new Rectangle(Point.Empty, drawn.Size).Contains(drawn.Detail), $"{what}: inside the pill");
                    continue;
                }
                Assert.AreEqual(drawn.DetailNeeds, drawn.Detail.Size, $"{what}: whole detail");
                Assert.IsLessThanOrEqualTo(drawn.Detail.Height, toast.DetailLines(drawn.Detail.Width).Height, $"{what}: every line of the detail has room");
                Assert.IsGreaterThanOrEqualTo(drawn.Title.Bottom, drawn.Detail.Top, $"{what}: detail under the title");
                Assert.AreEqual(language == "ar" ? drawn.Title.Right : drawn.Title.Left, language == "ar" ? drawn.Detail.Right : drawn.Detail.Left, $"{what}: same start");
                var inside = new Rectangle(Point.Empty, drawn.Size);
                Assert.IsTrue(inside.Contains(drawn.Title) && inside.Contains(drawn.Detail) && inside.Contains(drawn.Logo), $"{what}: inside the pill");
                Assert.IsLessThanOrEqualTo(drawn.Size.Height - 4, drawn.Detail.Bottom, $"{what}: clear of the line");
                Assert.AreEqual(toast.NoteBounds.Size, drawn.Size);
                using var picture = toast.Picture();
                Assert.AreEqual(0, picture.GetPixel(0, 0).A, $"{what}: round corners");
                Assert.AreEqual(noteTitle + " · " + noteBody, toast.Spoken, $"{what}: spoken whole");
            }

            // The note that the settings could not be saved shows its words whole, on one line or more.
            toast.ShowNote(text.V1.SaveFailedTitle, text.V1.SaveFailedDetail, seconds: 15);
            var saved = toast.Drawn;
            Assert.AreEqual(saved.TitleNeeds.Width, saved.Title.Width, $"{language}: whole title");
            Assert.AreEqual(saved.DetailNeeds.Width, saved.Detail.Width, $"{language}: whole detail");
            Assert.IsTrue(new Rectangle(Point.Empty, saved.Size).Contains(saved.Detail), $"{language}: inside the pill");

            // Short notes stay on one line.
            toast.ShowNote("First", "the old adapter", seconds: 8);
            Assert.IsFalse(toast.Drawn.Wrapped);
            toast.Dispose();
        }
    }

    [TestMethod]
    public void TheLineRunsDownWithTheSecondsLeft()
    {
        var toast = Note();
        toast.ShowCountdown(10, locking: true);
        Assert.AreEqual(1.0, toast.LineLeft(), 1e-9);
        now += 500;
        Assert.AreEqual(0.95, toast.LineLeft(), 1e-9);
        // A late tick never lets it run past the second it is in.
        now += 3000;
        Assert.AreEqual(0.9, toast.LineLeft(), 1e-9);
        toast.SetSeconds(5);
        Assert.AreEqual(0.5, toast.LineLeft(), 1e-9);
        toast.Dispose();
    }

    // ---- The keyboard ----

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [TestMethod]
    public void TheNoteWindowNeverTakesTheKeyboard()
    {
        // The window is made but never shown.
        using var toast = new Toast();
        IntPtr handle = toast.Handle;
        int style = GetWindowLong(handle, -20);
        Assert.AreNotEqual(0, style & 0x08000000, "no activate");
        Assert.AreNotEqual(0, style & 0x80, "tool window, not in Alt+Tab");
        Assert.AreNotEqual(0, style & 0x80000, "layered");
        Assert.AreEqual(3, (int)SendMessage(handle, 0x0021, handle, IntPtr.Zero), "a click does not activate it");
        Assert.IsFalse(toast.CanFocus && toast.Controls.Count > 0, "nothing in it can hold the focus");
        Assert.IsFalse(toast.Visible);
    }
}
