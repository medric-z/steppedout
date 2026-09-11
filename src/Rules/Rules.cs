namespace SteppedOut;

static class Rules
{
    public static List<ILockSuppressor> Build(Config config)
    {
        var rules = new List<ILockSuppressor>();
        if (config.SuppressWhileAudioPlaying) rules.Add(new AudioRule(config.AudioPeakThreshold, config.AudioSilenceSeconds));
        if (config.SuppressWhileFullscreen) rules.Add(new FullscreenRule());
        if (config.SuppressWhileDisplayRequested) rules.Add(new DisplayRule());
        if (config.NeverLockWhileRunning.Count > 0) rules.Add(new ProcessRule(config.NeverLockWhileRunning));
        return rules;
    }
}
