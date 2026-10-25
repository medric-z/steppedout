namespace SteppedOut;

// Only one copy runs per sign-in. The copy that runs owns a named mutex and waits on a named event;
// a second start finds the mutex taken and sets the event to ask that copy to open its settings.
// The names are AppInfo.MutexName and the same with ".Show"; the tests use names of their own, so
// they never stand in the way of a real copy.
sealed class InstanceChannel : IDisposable
{
    readonly Mutex mutex;

    InstanceChannel(Mutex mutex, EventWaitHandle showRequest)
    {
        this.mutex = mutex;
        ShowRequest = showRequest;
    }

    // Set by a second start. Made as soon as this copy is the one that runs, so a second start
    // during start-up is kept until the tray answers it.
    public EventWaitHandle ShowRequest { get; }

    public static string ShowName(string name) => name + ".Show";

    // This copy, when no other copy holds the name; null when one does, with the line for the log,
    // which is not open yet.
    public static InstanceChannel? Claim(string name, out string? notClaimed)
    {
        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: true, name, out bool first);
            if (!first)
            {
                mutex.Dispose();
                notClaimed = "another instance is already running, exiting";
                return null;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // A copy started with admin rights made the mutex, and this one is not allowed to open
            // it. That copy is still another instance running.
            notClaimed = "another instance is already running with admin rights, exiting";
            return null;
        }
        try
        {
            notClaimed = null;
            return new InstanceChannel(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, ShowName(name)));
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    // The copy that runs, for a second start to ask; null when it cannot be asked: it has admin
    // rights, it is an older version, or it is still starting.
    public static EventWaitHandle? OpenShowRequest(string name)
    {
        try
        {
            return EventWaitHandle.OpenExisting(ShowName(name));
        }
        catch (Exception e) when (e is WaitHandleCannotBeOpenedException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    // Lets the name go, so a later start is the one that runs.
    public void Dispose()
    {
        ShowRequest.Dispose();
        mutex.Dispose();
    }
}
