namespace SteppedOut;

// The watcher's KeepLive leases the tray holds, one per name ("menu" while the tray menu is up,
// "settings" while the settings window is open), so the rules are asked every second while the
// status shows. Holding a name twice keeps one lease; letting go of one not held does nothing, so
// events that come twice or out of order never leave a lease behind or let go of another's.
sealed class TrayLive
{
    readonly Func<IDisposable> keepLive;
    readonly Dictionary<string, IDisposable> held = new();

    public TrayLive(Func<IDisposable> keepLive) => this.keepLive = keepLive;

    public bool Holds(string name) => held.ContainsKey(name);

    public void Hold(string name)
    {
        if (!held.ContainsKey(name)) held[name] = keepLive();
    }

    public void Release(string name)
    {
        if (held.Remove(name, out var lease)) lease.Dispose();
    }

    public void Set(string name, bool on)
    {
        if (on) Hold(name);
        else Release(name);
    }

    public void ReleaseAll()
    {
        foreach (var name in held.Keys.ToList()) Release(name);
    }
}
