namespace SteppedOut.Tests;

[TestClass]
public class LocalPathsTests
{
    // What is behind each letter here: Z: is mapped to another PC, Q: has no drive now, R: is a disc,
    // U: a USB stick, M: a RAM disk, X: Windows could not tell, and the rest are fixed drives.
    static DriveType Drives(char letter) => letter switch
    {
        'Z' => DriveType.Network,
        'Q' => DriveType.NoRootDirectory,
        'R' => DriveType.CDRom,
        'U' => DriveType.Removable,
        'M' => DriveType.Ram,
        'X' => DriveType.Unknown,
        _ => DriveType.Fixed,
    };

    [TestMethod]
    [DataRow(@"C:\Scripts\away.bat", nameof(PathPlace.ThisPC))]
    [DataRow(@"c:/Scripts/away.bat", nameof(PathPlace.ThisPC))]
    [DataRow(@"R:\away.bat", nameof(PathPlace.ThisPC))]
    [DataRow(@"u:\away.bat", nameof(PathPlace.ThisPC))]
    [DataRow(@"M:\away.bat", nameof(PathPlace.ThisPC))]
    [DataRow(@"Z:\Scripts\away.bat", nameof(PathPlace.NetworkDrive))]
    [DataRow(@"z:/Scripts/away.bat", nameof(PathPlace.NetworkDrive))]
    [DataRow(@"Q:\Scripts\away.bat", nameof(PathPlace.NoDrive))]
    [DataRow(@"X:\Scripts\away.bat", nameof(PathPlace.NoDrive))]
    [DataRow(@"\\192.0.2.10\share\away.bat", nameof(PathPlace.NetworkPath))]
    [DataRow("//192.0.2.10/share/away.bat", nameof(PathPlace.NetworkPath))]
    [DataRow(@"\\?\UNC\192.0.2.10\share\away.bat", nameof(PathPlace.NotAFullPath))]
    [DataRow(@"\\.\C:\away.bat", nameof(PathPlace.NotAFullPath))]
    [DataRow(@"C:away.bat", nameof(PathPlace.NotAFullPath))]
    [DataRow("C:", nameof(PathPlace.NotAFullPath))]
    [DataRow("", nameof(PathPlace.NotAFullPath))]
    public void OnlyAFileOnADriveOfThisPCIsOnThisPC(string path, string place)
    {
        Assert.AreEqual(place, LocalPaths.Place(path, Drives).ToString(), path);
        Assert.AreEqual(place == nameof(PathPlace.ThisPC), LocalPaths.IsFullPathOnThisPC(path, Drives), path);
    }

    // Windows' own drive is on this PC, and a letter with no drive behind it is no network drive.
    [TestMethod]
    public void WindowsOwnDriveIsOnThisPC()
    {
        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Assert.IsFalse(LocalPaths.IsNetworkDrive(cmd[0]));
        Assert.IsTrue(LocalPaths.IsFullPathOnThisPC(cmd));

        var used = DriveInfo.GetDrives().Select(drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        char free = Enumerable.Range('A', 26).Select(letter => (char)letter).Reverse().FirstOrDefault(letter => !used.Contains(letter));
        if (free != default) Assert.IsFalse(LocalPaths.IsNetworkDrive(free), free + ": has no drive");
    }

    // A letter with no drive behind it now holds no file on this PC: it may be a network drive
    // Windows remembers but has not connected again, which a start through Explorer would reach
    // out to connect, or a USB stick that is out.
    [TestMethod]
    public void ALetterWithNoDriveNowIsNotOnThisPC()
    {
        var used = DriveInfo.GetDrives().Select(drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        char free = Enumerable.Range('D', 23).Select(letter => (char)letter).Reverse().FirstOrDefault(letter => !used.Contains(letter));
        if (free == default)
        {
            Assert.Inconclusive("every drive letter is in use here");
            return;
        }
        Assert.IsFalse(LocalPaths.IsFullPathOnThisPC(free + @":\Scripts\away.bat"), free + ": has no drive");
    }
}
