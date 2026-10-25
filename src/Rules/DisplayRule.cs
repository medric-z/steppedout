namespace SteppedOut;

// Video players, browsers playing video and presentation apps ask Windows to keep the display on,
// either through SetThreadExecutionState or a display power request. Windows folds every such
// request into one system-wide flag that can be read without administrator rights. The list of
// which process asked (what powercfg /requests prints) needs elevation, so this tool does without
// it and only reports that someone asked. A video call asks too, so during a call the flag does not
// count as music or video (D32); the evaluator sees to that, since it knows about the call.
sealed class DisplayRule : Rule
{
    const uint DisplayRequired = 0x2;

    public override string Name => "display";

    // The music or video setting's effect: the flag and sound are both music or video, which counts once.
    public Effect Effect { get; set; }

    public override void Check(Scene scene, List<WhyLine> lines)
    {
        if (scene.Read.DisplayRequired()) lines.Add(new WhyLine(Signal.Display, Effect));
    }

    public static bool Read() =>
        Native.CallNtPowerInformation(Native.SystemExecutionState, IntPtr.Zero, 0, out uint state, sizeof(uint)) == 0
        && (state & DisplayRequired) != 0;
}
