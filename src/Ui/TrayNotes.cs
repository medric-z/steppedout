namespace SteppedOut;

// The routes from the watcher to the corner notes and the away actions: the countdown, stepping out
// and coming back, the break and eye-break reminders with their sound, and Snooze. A break reminder
// comes only through ReminderDue, which carries both kinds; the older break-only event would
// show it a second time.
sealed class TrayNotes
{
    // How long the note for coming back waits to hear whether music or video is playing again. A
    // media app that hangs can't hold the note back longer; it then shows without the line.
    internal static readonly TimeSpan BackWait = TimeSpan.FromSeconds(1.5);

    readonly Watcher watcher;
    readonly Toast toast;
    readonly AwayActions actions;
    readonly Func<Config> config;
    // Runs an action on the UI thread later; it does nothing once the app is closing.
    readonly Action<Action> post;
    readonly Action<int> countdownStarted;
    readonly Action<int> countdownTick;
    readonly Action countdownEnded;
    readonly Action<bool, bool> wentAway;
    readonly Action<TimeSpan> cameBack;
    readonly Action<Reminder> reminderDue;
    readonly Action<ReminderKind> snooze;
    bool attached;

    public TrayNotes(Watcher watcher, Toast toast, AwayActions actions, Func<Config> config, Action<Action> post)
    {
        this.watcher = watcher;
        this.toast = toast;
        this.actions = actions;
        this.config = config;
        this.post = post;
        countdownStarted = seconds => toast.ShowCountdown(seconds, this.config().LockWhenAway);
        countdownTick = toast.SetSeconds;
        countdownEnded = toast.EndCountdown;
        wentAway = actions.Leave;
        cameBack = CameBack;
        reminderDue = Remind;
        snooze = watcher.Snooze;
    }

    // How many reminders were shown and how many times their sound was asked for, and how many notes
    // for coming back: for the tests, which can't tell one note from the same note shown twice.
    internal int Reminders { get; private set; }

    internal int ReminderSounds { get; private set; }

    internal int BackNotes { get; private set; }

    // How many notes for coming back were set to show once the away actions answer: each of them
    // shows within BackWait, so a test can tell at once that none is on its way.
    internal int BackNotesComing { get; private set; }

    public void Attach()
    {
        if (attached) return;
        attached = true;
        watcher.CountdownStarted += countdownStarted;
        watcher.CountdownTick += countdownTick;
        watcher.CountdownEnded += countdownEnded;
        watcher.WentAway += wentAway;
        watcher.CameBack += cameBack;
        watcher.ReminderDue += reminderDue;
        toast.SnoozeClicked += snooze;
    }

    public void Detach()
    {
        if (!attached) return;
        attached = false;
        watcher.CountdownStarted -= countdownStarted;
        watcher.CountdownTick -= countdownTick;
        watcher.CountdownEnded -= countdownEnded;
        watcher.WentAway -= wentAway;
        watcher.CameBack -= cameBack;
        watcher.ReminderDue -= reminderDue;
        toast.SnoozeClicked -= snooze;
    }

    void Remind(Reminder due)
    {
        Reminders++;
        toast.ShowReminder(due);
        ReminderSounds++;
        actions.PlayReminderSound();
    }

    // The back side of the away actions runs at once. The note waits for them to say whether music
    // or video is playing again, which they know off the UI thread, for a moment at most. Under a
    // minute away is a cancelled countdown or a quick unlock, not worth a note.
    void CameBack(TimeSpan away)
    {
        var back = actions.ComeBack();
        if (!config().ShowTimeAway || away < TimeSpan.FromMinutes(1)) return;
        BackNotesComing++;
        var wait = new CancellationTokenSource();
        Task.WhenAny(back, Task.Delay(BackWait, wait.Token)).ContinueWith(_ =>
        {
            wait.Cancel();
            wait.Dispose();
            bool playingAgain = back.IsCompletedSuccessfully && back.Result.AnyPlayedAgain;
            post(() =>
            {
                BackNotes++;
                toast.ShowAway(away, playingAgain);
            });
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }
}
