namespace SteppedOut;

// The name lives here and in the project file. Change both to rename the tool.
static class AppInfo
{
    public const string Name = "SteppedOut";
    public const string MutexName = @"Local\" + Name;

    public static string Version { get; } = typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public static string ConfigDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);
    public static string ConfigPath { get; } = Path.Combine(ConfigDir, "config.json");
    public static string LogPath { get; } = Path.Combine(ConfigDir, Name + ".log");
}
