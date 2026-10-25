namespace SteppedOut;

static class ConsoleOutput
{
    static bool attached;

    // A WinExe has no console of its own. For --once, --help and --version, borrow the parent
    // terminal's so the text lands there; it fails harmlessly when double-clicked. The tray run
    // never attaches: a process attached to a terminal is killed when that terminal closes.
    public static bool TryAttach()
    {
        if (!attached) attached = Native.AttachConsole(Native.AttachParentProcess);
        return attached;
    }
}
