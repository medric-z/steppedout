namespace SteppedOut;

// "Something is producing sound" is the best admin-free proxy for "a video is playing". The cleaner
// signal would be the power requests that players register (the ones powercfg /requests lists),
// but reading those needs administrator rights, which this tool refuses to ask for. The cost of
// the proxy: background music counts too, so leaving a player running keeps the PC unlocked.
sealed class AudioRule : ILockSuppressor
{
    readonly float threshold;
    readonly TimeSpan silenceHold;
    DateTime lastHeard = DateTime.MinValue;
    string lastProcess = "";

    public AudioRule(double peakThreshold, int silenceSeconds)
    {
        threshold = (float)peakThreshold;
        silenceHold = TimeSpan.FromSeconds(silenceSeconds);
    }

    public string Name => "audio";

    public string? Check()
    {
        if (Wasapi.LoudestSession(threshold) is Wasapi.Playing playing)
        {
            lastHeard = DateTime.Now;
            lastProcess = playing.Process;
            return "audio playing: " + playing.Process;
        }

        // Quiet scenes and gaps between tracks should not read as "the video ended".
        if (DateTime.Now - lastHeard < silenceHold) return $"audio stopped under {silenceHold.TotalSeconds:0} s ago: {lastProcess}";
        return null;
    }
}
