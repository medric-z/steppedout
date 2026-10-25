using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SteppedOut;

// Everything the rules ask Windows, in one place, so the tests can answer instead of the PC.
sealed record Sources
{
    public required Func<IReadOnlyList<RunningProcess>> Processes { get; init; }

    public required Func<IReadOnlyDictionary<int, int>> Parents { get; init; }

    public required Func<IReadOnlyList<TopWindow>> Windows { get; init; }

    // The programs that have the microphone open right now.
    public required Func<IReadOnlyList<CallApp>> Calls { get; init; }

    // Every program producing sound above the threshold.
    public required Func<float, IReadOnlyList<Wasapi.Session>> Sounds { get; init; }

    // An app asked Windows to keep the display on.
    public required Func<bool> DisplayRequired { get; init; }

    // The program whose window covers the screen right now, or null.
    public required Func<string?> FullScreen { get; init; }

    public required Func<PowerLineStatus> PowerLine { get; init; }

    public required Func<int, string?> ImagePath { get; init; }

    public required Func<int, string?> PackageFamily { get; init; }

    public static Sources Real { get; } = new()
    {
        Processes = Native.Processes,
        Parents = Native.ParentIds,
        Windows = Native.TopLevelWindows,
        Calls = CallRule.Scan,
        Sounds = Wasapi.Sessions,
        DisplayRequired = DisplayRule.Read,
        FullScreen = FullscreenRule.Read,
        PowerLine = Native.PowerLine,
        ImagePath = ProcessInfo.ImagePath,
        PackageFamily = ProcessInfo.PackageFamilyName,
    };
}

// A reading that costs a few milliseconds, kept for a few seconds. MaxAge is a little under the
// 5 seconds between the far polls, so each of those always reads afresh; a fresh look reads afresh
// whatever the age.
sealed class Scan<T>(Func<T> read, TimeSpan maxAge) where T : class
{
    T? value;
    TimeSpan takenAt;

    public T Get(TimeSpan now, bool fresh)
    {
        if (fresh || value is null || now - takenAt >= maxAge || now < takenAt)
        {
            // A read that throws leaves the old value, and the next look tries again.
            value = read();
            takenAt = now;
        }
        return value;
    }
}

// The readings that are worth keeping between looks: the process list, the windows, the call
// records and the process tree. And the sounds, the one reading that waits on another process (the
// audio service) and so could hang with it: a look waits at most SoundLimit for them.
//
// The other readings are asked of Windows itself (the process list, the windows, the registry, the
// power state) and do not wait on another program, so they are taken on the UI thread as they are.
sealed class Scans : IDisposable
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(4.5);
    public static readonly TimeSpan SoundLimit = TimeSpan.FromSeconds(2);

    public Scans(Sources sources)
        : this(sources, SoundLimit)
    {
    }

    internal Scans(Sources sources, TimeSpan soundLimit)
    {
        Sources = sources;
        Processes = new(sources.Processes, MaxAge);
        Parents = new(sources.Parents, MaxAge);
        Windows = new(sources.Windows, MaxAge);
        Calls = new(sources.Calls, MaxAge);
        Sounds = new(sources.Sounds, soundLimit);
    }

    public Sources Sources { get; }

    public Bounded<float, IReadOnlyList<Wasapi.Session>> Sounds { get; }

    public Scan<IReadOnlyList<RunningProcess>> Processes { get; }

    public Scan<IReadOnlyDictionary<int, int>> Parents { get; }

    public Scan<IReadOnlyList<TopWindow>> Windows { get; }

    public Scan<IReadOnlyList<CallApp>> Calls { get; }

    // The sounds' reading thread ends.
    public void Dispose() => Sounds.Dispose();
}

// One look at what is going on, shared by every rule asked in it: the same process list, windows
// and call records for all of them, read at most once per look. Fresh reads everything afresh, for
// the moments that decide: just before a countdown starts and just before the lock.
sealed class Scene(TimeSpan now, bool fresh, Scans scans)
{
    IReadOnlyList<RunningProcess>? processes;
    IReadOnlyDictionary<int, int>? parents;
    IReadOnlyList<TopWindow>? windows;
    IReadOnlyList<CallApp>? calls;

    public TimeSpan Now { get; } = now;

    public bool Fresh { get; } = fresh;

    // For the readings that are taken every time they are asked for.
    public Sources Read => scans.Sources;

    public IReadOnlyList<RunningProcess> Processes => processes ??= scans.Processes.Get(Now, Fresh);

    public IReadOnlyDictionary<int, int> Parents => parents ??= scans.Parents.Get(Now, Fresh);

    public IReadOnlyList<TopWindow> Windows => windows ??= scans.Windows.Get(Now, Fresh);

    public IReadOnlyList<CallApp> Calls => calls ??= scans.Calls.Get(Now, Fresh);

    // Every program producing sound above the threshold, read afresh each time. Throws a
    // TimeoutException when the audio service does not answer in time.
    public IReadOnlyList<Wasapi.Session> Sounds(float threshold) => scans.Sounds.Get(threshold, Fresh);
}

// A reading that could hang. It runs on a reading thread and the caller waits at most the limit;
// past that the caller gets a TimeoutException, as from any reading that failed, and no second read
// starts while that one is still out, so a hang costs one wait and one thread.
//
// A read that comes back after its caller gave up is an old answer. The next call takes it only when
// that call is no fresh one (fresh looks decide: just before a countdown and just before the lock)
// and it has been back no longer than maxAge, as the readings kept between looks are; otherwise the
// next call reads again.
//
// The reads take turns on one thread, which ends when none is asked for a while or the reading is
// disposed, and the caller waits on one event: a thread or an event for each read would leave
// handles behind until a garbage collection, a few for every look. Only the read the caller waits
// for sets the event, so a late read that comes back as the next is handed over does not wake the
// next caller before its own read is back.
//
// Get is for one caller at a time, and there is one: the watcher, on the UI thread. Only the newest
// caller's read wakes the event, and a read handed over replaces one not picked up yet, so a second
// caller at the same moment would wait out its whole limit and its read might never run. Two
// callers would need an event and a queue for each.
sealed class Bounded<TArg, TResult> : IDisposable
{
    static readonly TimeSpan IdleEnd = TimeSpan.FromSeconds(10);

    readonly Func<TArg, TResult> read;
    readonly TimeSpan limit;
    readonly TimeSpan maxAge;
    readonly Func<TimeSpan> clock;
    readonly object gate = new();
    // Set as the read waited for comes back, and cleared as the next is handed over.
    readonly ManualResetEvent back = new(initialState: true);
    // The read handed over last, the one its caller waits for; only it sets the event.
    Read? waitedFor;
    // The read handed to the reading thread and not picked up yet, and whether that thread is there.
    Read? handed;
    bool reading;
    bool disposed;
    Read? late;

    // clock tells when a late read came back; Clock.Now unless a test gives its own.
    public Bounded(Func<TArg, TResult> read, TimeSpan limit, Func<TimeSpan>? clock = null, TimeSpan? maxAge = null)
    {
        this.read = read;
        this.limit = limit;
        this.clock = clock ?? (() => Clock.Now);
        this.maxAge = maxAge ?? Scans.MaxAge;
    }

    // A read that timed out has come back since, for the tests.
    internal bool LateIsBack => late is { Done: true };

    // A read is handed over and the reading thread has not picked it up yet, for the tests.
    internal bool Handed
    {
        get
        {
            lock (gate) return handed is not null;
        }
    }

    // For the tests: runs on the reading thread as each read is back, before its caller is woken.
    internal Action? Returning { get; set; }

    // The reading thread is there, for the tests.
    internal bool Reading
    {
        get
        {
            lock (gate) return reading;
        }
    }

    // The reading thread ends once the read it may be on is over; nothing is read after this. The
    // event stays for that read to set.
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            handed = null;
            Monitor.PulseAll(gate);
        }
    }

    public TResult Get(TArg arg, bool fresh = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (late is { } waiting)
        {
            if (!waiting.Done) throw new TimeoutException("the last reading has not come back yet");
            late = null;
            var age = clock() - waiting.BackAt;
            if (!fresh && age >= TimeSpan.Zero && age <= maxAge) return waiting.Outcome();
        }
        var next = new Read(arg);
        Hand(next);
        if (!next.Done && !PlainWait.For(back.SafeWaitHandle, limit) && !next.Done)
        {
            late = next;
            throw new TimeoutException($"no answer within {limit.TotalSeconds:0.#} s");
        }
        return next.Outcome();
    }

    void Hand(Read next)
    {
        lock (gate)
        {
            waitedFor = next;
            back.Reset();
            handed = next;
            if (reading)
            {
                Monitor.Pulse(gate);
                return;
            }
            reading = true;
        }
        var thread = new Thread(Reads)
        {
            IsBackground = true,
            Name = "SteppedOut reading",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    // The reading thread: each read handed over, in turn, until none comes for a while.
    void Reads()
    {
        while (true)
        {
            Read next;
            lock (gate)
            {
                while (handed is null)
                {
                    if (disposed || (!Monitor.Wait(gate, IdleEnd) && handed is null) || disposed)
                    {
                        reading = false;
                        return;
                    }
                }
                next = handed;
                handed = null;
            }
            try
            {
                next.Result = read(next.Arg);
            }
            catch (Exception e)
            {
                next.Error = ExceptionDispatchInfo.Capture(e);
            }
            next.BackAt = clock();
            next.Done = true;
            Returning?.Invoke();
            lock (gate)
            {
                if (next == waitedFor) back.Set();
            }
        }
    }

    sealed class Read(TArg arg)
    {
        public TArg Arg { get; } = arg;

        public TResult? Result { get; set; }

        public ExceptionDispatchInfo? Error { get; set; }

        public TimeSpan BackAt { get; set; }

        // Written last, once the rest is in place.
        volatile bool done;

        public bool Done
        {
            get => done;
            set => done = value;
        }

        public TResult Outcome()
        {
            Error?.Throw();
            return Result!;
        }
    }
}

// A plain kernel wait. A managed wait on the UI thread pumps messages, which could run a handler for
// a Windows event in the middle of the tick.
static class PlainWait
{
    const uint WaitObject0 = 0;

    // True when the handle was signalled within the limit.
    public static bool For(SafeWaitHandle handle, TimeSpan limit) =>
        WaitForSingleObject(handle, (uint)Math.Max(0, limit.TotalMilliseconds)) == WaitObject0;

    [DllImport("kernel32.dll")]
    static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
}
