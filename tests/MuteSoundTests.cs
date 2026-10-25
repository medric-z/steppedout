using System.Runtime.InteropServices;

namespace SteppedOut.Tests;

[TestClass]
public class MuteSoundTests
{
    // A scratch folder, so the tests never touch the real marker or the real speakers.
    string dir = "";
    string marker = "";

    [TestInitialize]
    public void Setup()
    {
        dir = Path.Combine(Path.GetTempPath(), "SteppedOut.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        marker = Path.Combine(dir, "muted-by-SteppedOut");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(dir, recursive: true);

    [TestMethod]
    public void ALeftoverMuteIsUndoneOnTheSameDeviceAndForgotten()
    {
        // Written the way an earlier version did: one id and no line end.
        File.WriteAllText(marker, "{0.0.0.00000000}.{speakers}");
        var unmuted = new List<string>();

        MuteSound.UndoLeftover(marker, device =>
        {
            unmuted.Add(device);
            return true;
        });

        CollectionAssert.AreEqual(new[] { "{0.0.0.00000000}.{speakers}" }, unmuted);
        Assert.IsFalse(File.Exists(marker));
    }

    [TestMethod]
    public void SoundAlreadyBackOnStillForgetsTheMute()
    {
        MuteSound.WriteMarker(marker, ["{0.0.0.00000000}.{speakers}"]);

        MuteSound.UndoLeftover(marker, _ => false);

        Assert.IsFalse(File.Exists(marker));
    }

    [TestMethod]
    public void NoMarkerMeansNothingIsTouched()
    {
        bool called = false;

        MuteSound.UndoLeftover(marker, _ => called = true);

        Assert.IsFalse(called);
    }

    [TestMethod]
    public void AFailedUnmuteKeepsTheMarkerForTheNextStart()
    {
        MuteSound.WriteMarker(marker, ["{0.0.0.00000000}.{unplugged}"]);

        MuteSound.UndoLeftover(marker, _ => throw new COMException("device gone"));

        Assert.IsTrue(File.Exists(marker));
    }

    [TestMethod]
    public void OnlyTheDevicesThatWouldNotUnmuteStayInTheMarker()
    {
        MuteSound.WriteMarker(marker, ["{speakers}", "{headphones}"]);

        MuteSound.UndoLeftover(marker, device => device == "{speakers}" ? true : throw new COMException("device gone"));

        CollectionAssert.AreEqual(new[] { "{headphones}" }, File.ReadAllLines(marker));
    }

    [TestMethod]
    public void HeadphonesSwitchedBackOnAreUnmutedByTheRetryWhileYouAreHere()
    {
        MuteSound.WriteMarker(marker, ["{headphones}"]);
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(marker, stamp);
        bool headphonesBack = false;
        Func<string, bool> unmute = _ => headphonesBack ? true : throw new COMException("not connected");

        MuteSound.UndoLeftover(marker, unmute, quiet: true);
        Assert.AreEqual(stamp, File.GetLastWriteTimeUtc(marker), "a retry that changes nothing leaves the file alone");

        headphonesBack = true;
        MuteSound.UndoLeftover(marker, unmute, quiet: true);
        Assert.IsFalse(File.Exists(marker));
    }

    [TestMethod]
    public void WhenTheHeldConnectionBrokeTheDeviceIsUnmutedByItsId()
    {
        var unmuted = new List<string>();
        var mute = new MuteSound(marker, () => Held("{headphones}", works: false), device =>
        {
            unmuted.Add(device);
            return true;
        });

        mute.Leave(locking: true);
        mute.Return();

        CollectionAssert.AreEqual(new[] { "{headphones}" }, unmuted);
        Assert.IsFalse(File.Exists(marker));
    }

    [TestMethod]
    public void ADeviceThatWouldNotUnmuteIsKeptWhenTheNextOneIsMutedAndTriedFirst()
    {
        string defaultDevice = "{headphones}";
        bool headphonesBack = false;
        var unmuted = new List<string>();
        var mute = new MuteSound(marker, () => Held(defaultDevice, works: defaultDevice != "{headphones}"), device =>
        {
            if (device == "{headphones}" && !headphonesBack) throw new COMException("device invalidated");
            unmuted.Add(device);
            return true;
        });

        // The headphones go off while you are away.
        mute.Leave(locking: true);
        mute.Return();
        CollectionAssert.AreEqual(new[] { "{headphones}" }, File.ReadAllLines(marker));

        defaultDevice = "{speakers}";
        mute.Leave(locking: true);
        CollectionAssert.AreEqual(new[] { "{headphones}", "{speakers}" }, File.ReadAllLines(marker));
        mute.Return();
        CollectionAssert.AreEqual(new[] { "{headphones}" }, File.ReadAllLines(marker));

        headphonesBack = true;
        mute.Leave(locking: true);
        CollectionAssert.AreEqual(new[] { "{headphones}" }, unmuted);
        CollectionAssert.AreEqual(new[] { "{speakers}" }, File.ReadAllLines(marker));
        mute.Return();
        Assert.IsFalse(File.Exists(marker));
    }

    [TestMethod]
    public void SigningOutTriesADeviceThatWouldNotUnmuteBefore()
    {
        MuteSound.WriteMarker(marker, ["{headphones}"]);
        bool headphonesBack = false;
        var mute = new MuteSound(marker, () => Held("{speakers}", works: true), _ => headphonesBack ? true : throw new COMException("device invalidated"));

        mute.Leave(locking: true);
        headphonesBack = true;
        mute.Ending();

        Assert.IsFalse(File.Exists(marker));
    }

    static Wasapi.MutedOutput Held(string device, bool works) => new(device, () =>
    {
        if (!works) throw new COMException("device invalidated");
    });

    [TestMethod]
    public void AnEmptyMarkerIsDroppedWithoutTouchingAnyDevice()
    {
        File.WriteAllText(marker, "  \n");
        bool called = false;

        MuteSound.UndoLeftover(marker, _ => called = true);

        Assert.IsFalse(called);
        Assert.IsFalse(File.Exists(marker));
    }
}
