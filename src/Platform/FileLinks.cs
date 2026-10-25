using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SteppedOut;

// Hard and symbolic links to a file, which a save has to write through rather than replace.
static class FileLinks
{
    // The file a symbolic link leads to, through every link on the way, or path itself when it is
    // no link or cannot be followed.
    public static string FinalTarget(string path)
    {
        try
        {
            return new FileInfo(path).LinkTarget is null ? path : File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    // How many names the file has on its disk: more than one when it is hard linked. 1 when there
    // is no file or it cannot be told.
    public static int Names(string path)
    {
        try
        {
            using SafeFileHandle file = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return GetFileInformationByHandle(file, out var information) ? (int)information.NumberOfLinks : 1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 1;
        }
    }

    // BY_HANDLE_FILE_INFORMATION. Its FILETIMEs are two DWORDs each, so 4-byte packing keeps the
    // longs where Windows puts them.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct FileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
}
