using System.Globalization;
using System.Text.Json;

namespace SteppedOut;

// "Locked today" and "Away today" (D11). A lock counts when the session locks while this runs,
// however it was locked, so a dry run's "would lock" never counts: nothing locks. Away time is the
// union of locked time and time away with locking turned off. Everything starts over at local
// midnight, and a stretch that crosses it counts only from midnight. Each stretch is the time that
// really passed, the clock times carrying their offset from UTC, so one across a daylight saving
// change counts what it lasted, not the difference the wall clock shows.
//
// Kept in today.json next to the settings, holding only the date and the two numbers, overwritten
// in place. At start it is used only if its date is today; a missing or unreadable file is ignored
// and replaced, never an error.
sealed class DailyStats
{
    public const string FileName = "today.json";
    public static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(1);
    // While saving keeps failing (a full disk, a read-only profile, antivirus or a backup holding the
    // file), it is tried less and less often, at most this far apart, and the log says so once, then
    // again only when the reason changes or once an hour, so it never fills the log.
    public static readonly TimeSpan RetryAtMost = TimeSpan.FromMinutes(15);
    static readonly TimeSpan SayAgainAfter = TimeSpan.FromHours(1);

    // Bigger values than these are not something this wrote, so the file is set aside.
    const int MaxLocks = 1_000_000;
    const double MaxAwaySeconds = 25 * 60 * 60;

    readonly string? path;
    readonly Action<string> write;
    DateTime day;
    int locks;
    TimeSpan away;
    DateTimeOffset? awaySince;
    bool dirty;
    DateTimeOffset? lastSave;
    // Saves that failed in a row, and the reason the log gave last and when.
    int failures;
    string? failure;
    DateTimeOffset saidAt;

    // A null path keeps the counts in memory only.
    DailyStats(string? path, DateTimeOffset now, Action<string> write)
    {
        this.path = path;
        this.write = write;
        day = now.Date;
    }

    public static string DefaultPath => Path.Combine(AppInfo.ConfigDir, FileName);

    // Failed saves for the tests.
    internal int Failures => failures;

    // write: where its log lines go; the log by default.
    public static DailyStats Load(string? path, DateTimeOffset now, Action<string>? write = null)
    {
        var stats = new DailyStats(path, now, write ?? Log.Write);
        if (path is null) return stats;
        if (!File.Exists(path))
        {
            stats.dirty = true;
            return stats;
        }

        string why;
        try
        {
            if (Parse(File.ReadAllText(path)) is not { } saved)
            {
                why = "it is not in the expected shape";
            }
            else if (saved.Date != now.Date)
            {
                // Yesterday's counts, or a clock set back: today starts at zero.
                stats.dirty = true;
                return stats;
            }
            else
            {
                stats.locks = saved.Locks;
                stats.away = TimeSpan.FromSeconds(saved.AwaySeconds);
                return stats;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            why = e.Message;
        }
        stats.write($"{FileName} could not be read, today's counts start at zero: {why}");
        stats.dirty = true;
        return stats;
    }

    // The session locked: by this app, by Win+L or by anything else.
    public void Locked(DateTimeOffset now)
    {
        Roll(now);
        locks++;
        StartAway(now);
        dirty = true;
        Save(now);
    }

    public void Unlocked(DateTimeOffset now)
    {
        Roll(now);
        EndAway(now);
        Save(now);
    }

    // Stepped out, with or without a lock to follow. since is when the idle stretch began, so the
    // count matches "You were away" when you come back. Saved within a minute, not at once.
    public void WentAway(DateTimeOffset since, DateTimeOffset now)
    {
        Roll(now);
        StartAway(since < now ? since : now);
    }

    public void CameBack(DateTimeOffset now)
    {
        Roll(now);
        EndAway(now);
    }

    // The away under way counts from now and not before: a lock that came as the PC went to sleep
    // counts from the wake, since a sleep from ordinary use is not time away.
    public void AwayFrom(DateTimeOffset now)
    {
        Roll(now);
        if (awaySince is not DateTimeOffset since || now <= since) return;
        awaySince = now;
        dirty = true;
    }

    // Once a second from the tick: starts the day over at midnight and saves at most once a minute,
    // or less often while saving keeps failing.
    public void Tick(DateTimeOffset now)
    {
        Roll(now);
        if (!dirty && awaySince is null) return;
        if (lastSave is DateTimeOffset last && now >= last && now - last < Wait()) return;
        Save(now);
    }

    // A minute, doubled for each save that failed in a row, up to RetryAtMost.
    TimeSpan Wait() =>
        failures == 0 ? SaveEvery : TimeSpan.FromTicks(Math.Min(RetryAtMost.Ticks, SaveEvery.Ticks << Math.Min(failures, 10)));

    // On quit, so the counts survive a restart.
    public void Quit(DateTimeOffset now) => Save(now);

    public (int Locks, TimeSpan Away) Today(DateTimeOffset now)
    {
        Roll(now);
        var open = awaySince is DateTimeOffset since && now > since ? now - since : TimeSpan.Zero;
        return (locks, away + open);
    }

    void StartAway(DateTimeOffset since)
    {
        // Already away keeps the earlier start, so a lock after stepping out counts its time once.
        if (awaySince is not null) return;
        var midnight = Midnight;
        awaySince = since < midnight ? midnight : since;
        dirty = true;
    }

    void EndAway(DateTimeOffset now)
    {
        if (awaySince is not DateTimeOffset since) return;
        if (now > since) away += now - since;
        awaySince = null;
        dirty = true;
    }

    void Roll(DateTimeOffset now)
    {
        if (now.Date == day) return;
        day = now.Date;
        locks = 0;
        away = TimeSpan.Zero;
        // The part before midnight was yesterday's.
        var midnight = Midnight;
        if (awaySince is DateTimeOffset since && since < midnight) awaySince = midnight;
        dirty = true;
    }

    // The moment today began, by this PC's time zone: on a day the clocks change, its offset from
    // UTC is the one midnight had.
    DateTimeOffset Midnight => new(day, TimeZoneInfo.Local.GetUtcOffset(day));

    void Save(DateTimeOffset now)
    {
        lastSave = now;
        dirty = false;
        if (path is null) return;
        var (count, total) = Today(now);
        string text = Format(day, count, total);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Counts that cannot be saved are kept in memory and tried again later.
            dirty = true;
            failures++;
            if (e.Message != failure || now < saidAt || now - saidAt >= SayAgainAfter)
            {
                write($"could not save {FileName}" + (failures > 1 ? $" ({failures} tries in a row)" : "") + $": {e.Message}");
                failure = e.Message;
                saidAt = now;
            }
            return;
        }
        if (failures > 0) write($"saved {FileName} again after {failures} failed tries");
        failures = 0;
        failure = null;
    }

    internal static string Format(DateTime day, int locks, TimeSpan away)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("date", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            json.WriteNumber("locks", locks);
            json.WriteNumber("awaySeconds", (long)Math.Round(Math.Clamp(away.TotalSeconds, 0, MaxAwaySeconds)));
            json.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray()) + Environment.NewLine;
    }

    internal static (DateTime Date, int Locks, double AwaySeconds)? Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("date", out var d) || d.ValueKind != JsonValueKind.String
                || !DateTime.TryParseExact(d.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return null;
            if (!root.TryGetProperty("locks", out var l) || l.ValueKind != JsonValueKind.Number
                || !l.TryGetInt32(out int count) || count < 0 || count > MaxLocks) return null;
            if (!root.TryGetProperty("awaySeconds", out var a) || a.ValueKind != JsonValueKind.Number
                || !a.TryGetDouble(out double seconds) || seconds < 0 || seconds > MaxAwaySeconds) return null;
            return (date, count, seconds);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
