namespace SteppedOut;

// Video players, browsers playing video and presentation apps ask Windows to keep the display on,
// either through SetThreadExecutionState or a display power request. Windows folds every such
// request into one system-wide flag that can be read without administrator rights. The list of
// which process asked (what powercfg /requests prints) needs elevation, so this tool does without
// it and only reports that someone asked.
sealed class DisplayRule : ILockSuppressor
{
    const uint DisplayRequired = 0x2;

    public string Name => "display";

    public string? Check()
    {
        if (Native.CallNtPowerInformation(Native.SystemExecutionState, IntPtr.Zero, 0, out uint state, sizeof(uint)) != 0) return null;
        return (state & DisplayRequired) != 0 ? "an app asked Windows to keep the display on" : null;
    }
}
