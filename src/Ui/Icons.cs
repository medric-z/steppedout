namespace SteppedOut;

// The logo from the icon file built into the exe, for title bars and the taskbar.
static class Icons
{
    static Icon? window;

    public static Icon Window()
    {
        if (window is not null) return window;
        using var stream = typeof(Icons).Assembly.GetManifestResourceStream("icon.ico")!;
        return window = new Icon(stream, SystemInformation.IconSize);
    }
}
