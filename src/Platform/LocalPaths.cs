namespace SteppedOut;

// Where a path from the settings leads.
enum PathPlace
{
    // A file on this PC by its full path.
    ThisPC,
    // Not a file's full path: a web address, a bare command, a relative path, a device path.
    NotAFullPath,
    // On another PC, by a network path (\\server\share or //server/share).
    NetworkPath,
    // On a drive letter mapped to a shared folder on another PC.
    NetworkDrive,
    // On a drive letter with no drive behind it now: a USB stick that is out, or a network drive
    // Windows remembers but has not connected again.
    NoDrive,
}

static class LocalPaths
{
    // A file on this PC by its full path: a drive letter, a colon and a backslash, as Explorer's
    // "Copy as path" gives it, on a drive this PC has now. Not a file on another PC, by a network
    // path (\\server\share or //server) or on a drive letter mapped to a shared folder, which would
    // reach out to it; not one on a letter with no drive behind it, which may be a network drive
    // that a start through Explorer would connect again; not a device path (\\?\, \\.\ or \??\),
    // which goes past Windows' usual checks on a path; and not a path that depends on the current
    // folder or drive.
    public static bool IsFullPathOnThisPC(string path) => Place(path) == PathPlace.ThisPC;

    internal static bool IsFullPathOnThisPC(string path, Func<char, DriveType> drive) => Place(path, drive) == PathPlace.ThisPC;

    public static PathPlace Place(string path) => Place(path, DriveTypeOf);

    // drive tells what Windows says is behind a drive letter; the tests give their own.
    internal static PathPlace Place(string path, Func<char, DriveType> drive)
    {
        path ??= "";
        if (path.Length >= 2 && Slash(path[0]) && Slash(path[1]))
        {
            bool device = path.Length >= 4 && path[2] is '?' or '.' && Slash(path[3]);
            return device ? PathPlace.NotAFullPath : PathPlace.NetworkPath;
        }
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || !Slash(path[2])) return PathPlace.NotAFullPath;
        return drive(char.ToUpperInvariant(path[0])) switch
        {
            DriveType.Fixed or DriveType.Removable or DriveType.CDRom or DriveType.Ram => PathPlace.ThisPC,
            DriveType.Network => PathPlace.NetworkDrive,
            _ => PathPlace.NoDrive,
        };
    }

    // Why a file is out of this PC's own reach, in a few plain words that start with "it", for the
    // log; null for any other place.
    internal static string? Elsewhere(string path, PathPlace place) => place switch
    {
        PathPlace.NetworkPath => "it is on another PC, by a network path",
        PathPlace.NetworkDrive => $"it is on another PC: {char.ToUpperInvariant(path[0])}: is a network drive",
        PathPlace.NoDrive => $"it is on {char.ToUpperInvariant(path[0])}:, which is not connected to this PC now",
        _ => null,
    };

    // A drive letter mapped to a shared folder on another PC, as Map network drive makes one.
    internal static bool IsNetworkDrive(char letter) => DriveTypeOf(letter) == DriveType.Network;

    // What Windows says is behind a drive letter. It answers from the drive letters of this
    // sign-in, without asking another PC.
    internal static DriveType DriveTypeOf(char letter)
    {
        try
        {
            return new DriveInfo(letter.ToString()).DriveType;
        }
        catch (ArgumentException)
        {
            return DriveType.NoRootDirectory;
        }
    }

    static bool Slash(char c) => c is '\\' or '/';
}
