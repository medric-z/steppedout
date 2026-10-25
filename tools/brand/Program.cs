using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

// Writes the app icon and the README logo from the app's own logo code (Logo.Draw), so they always
// match what the app draws.
//
//   dotnet run --project tools/brand                     write src/icon.ico and docs/logo.png
//   dotnet run --project tools/brand -- --check          only say whether both are up to date (exit 1 if not)
//   dotnet run --project tools/brand -- --exe <file>     check that an exe's icon holds every size of src/icon.ico
//   dotnet run --project tools/brand -- --sheet <folder> [<image>...]
//                                                        draw every size of src/icon.ico and docs/logo.png
//                                                        zoomed on light and dark, with the images given
//                                                        beside them for comparison
//
// Both files are written by this code alone (no image encoder of the system), so running it twice
// gives the same bytes.
static class Brand
{
    // The icon sizes. Each is drawn at its own size, never scaled, so the small ones get the logo's
    // simpler shapes. The 256 entry is stored as PNG, the others as plain bitmaps, which every
    // Windows reader takes.
    static readonly int[] IconSizes = [16, 20, 24, 32, 48, 256];

    // The README shows the logo 80 px wide; twice that keeps it sharp on high-density screens.
    const int ReadmeLogo = 160;

    static Func<int, Bitmap> draw = null!;
    static Color tile, glyph;

    [STAThread]
    static int Main(string[] args)
    {
        var app = Assembly.Load(new AssemblyName("SteppedOut"));
        BlockLocking(app);
        draw = FindLogo(app);
        (tile, glyph) = FindColors(app);

        string root = FindRoot();
        string icoPath = Path.Combine(root, "src", "icon.ico");
        string logoPath = Path.Combine(root, "docs", "logo.png");

        switch (args)
        {
            case []:
                File.WriteAllBytes(icoPath, Icon());
                File.WriteAllBytes(logoPath, Png(draw(ReadmeLogo)));
                Console.WriteLine($"wrote src/icon.ico ({string.Join(", ", IconSizes)} px) and docs/logo.png ({ReadmeLogo} px)");
                return 0;
            case ["--check"]:
                return Check(icoPath, logoPath);
            case ["--exe", var exe]:
                return CheckExe(Path.GetFullPath(exe), icoPath);
            case ["--sheet", var folder, .. var refs]:
                Sheets.Write(Path.GetFullPath(folder), File.ReadAllBytes(icoPath), logoPath, refs.Select(Path.GetFullPath).ToArray(), draw);
                return 0;
            default:
                Console.WriteLine("usage: dotnet run --project tools/brand [-- --check | --exe <file> | --sheet <folder> [<image>...]]");
                return 2;
        }
    }

    static int Check(string icoPath, string logoPath)
    {
        bool icoOk = File.Exists(icoPath) && File.ReadAllBytes(icoPath).AsSpan().SequenceEqual(Icon());
        bool logoOk = File.Exists(logoPath) && File.ReadAllBytes(logoPath).AsSpan().SequenceEqual(Png(draw(ReadmeLogo)));
        Console.WriteLine($"src/icon.ico: {(icoOk ? "up to date" : "STALE")}");
        Console.WriteLine($"docs/logo.png: {(logoOk ? "up to date" : "STALE")}");
        if (icoOk)
            foreach (var entry in Ico.Read(File.ReadAllBytes(icoPath)))
                Console.WriteLine($"  {entry.Size} px, {(entry.IsPng ? "PNG" : "bitmap")}, {entry.Data.Length} bytes");
        return icoOk && logoOk ? 0 : 1;
    }

    // The icon file: a directory, then one image per size.
    public static byte[] Icon()
    {
        var images = IconSizes.Select(size =>
        {
            using var bitmap = Small.Has(size) ? Small.Draw(size, draw, tile, glyph) : draw(size);
            return (size, data: size >= 256 ? Png(bitmap) : Dib(bitmap));
        }).ToList();

        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)images.Count);
        int offset = 6 + 16 * images.Count;
        foreach (var (size, data) in images)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write(data.Length);
            w.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in images) w.Write(data);
        w.Flush();
        return stream.ToArray();
    }

    // A 32-bit bitmap as an icon stores it: the header with double height, the colour rows bottom
    // up, then the one-bit mask (set where the image is fully clear).
    static byte[] Dib(Bitmap bitmap)
    {
        int n = bitmap.Width;
        var pixels = Pixels(bitmap);
        int maskStride = (n + 31) / 32 * 4;
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(40);
        w.Write(n);
        w.Write(n * 2);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(0);
        w.Write(n * n * 4 + maskStride * n);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        for (int y = n - 1; y >= 0; y--)
            w.Write(pixels, y * n * 4, n * 4);
        for (int y = n - 1; y >= 0; y--)
        {
            var row = new byte[maskStride];
            for (int x = 0; x < n; x++)
                if (pixels[(y * n + x) * 4 + 3] == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
            w.Write(row);
        }
        w.Flush();
        return stream.ToArray();
    }

    // Blue, green, red, alpha per pixel, top row first, not premultiplied.
    public static byte[] Pixels(Bitmap bitmap)
    {
        int width = bitmap.Width, height = bitmap.Height;
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * width * 4, width * 4);
            return pixels;
        }
        finally { bitmap.UnlockBits(data); }
    }

    // A plain RGBA PNG with no time or text chunks. Each row takes the filter that gives the
    // smallest sum, a fixed rule, so the bytes depend only on the pixels.
    public static byte[] Png(Bitmap bitmap)
    {
        int width = bitmap.Width, height = bitmap.Height;
        var bgra = Pixels(bitmap);
        int stride = width * 4;
        var rgba = new byte[bgra.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            rgba[i] = bgra[i + 2];
            rgba[i + 1] = bgra[i + 1];
            rgba[i + 2] = bgra[i];
            rgba[i + 3] = bgra[i + 3];
        }

        var raw = new MemoryStream();
        var prev = new byte[stride];
        var cur = new byte[stride];
        var best = new byte[stride];
        var trial = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            Array.Copy(rgba, y * stride, cur, 0, stride);
            long bestSum = long.MaxValue;
            byte bestType = 0;
            for (byte type = 0; type <= 4; type++)
            {
                long sum = 0;
                for (int i = 0; i < stride; i++)
                {
                    int a = i >= 4 ? cur[i - 4] : 0, b = prev[i], c = i >= 4 ? prev[i - 4] : 0;
                    int predictor = type switch
                    {
                        1 => a,
                        2 => b,
                        3 => (a + b) / 2,
                        4 => Paeth(a, b, c),
                        _ => 0,
                    };
                    trial[i] = (byte)(cur[i] - predictor);
                    sum += Math.Abs((int)(sbyte)trial[i]);
                }
                if (sum < bestSum)
                {
                    bestSum = sum;
                    bestType = type;
                    Array.Copy(trial, best, stride);
                }
            }
            raw.WriteByte(bestType);
            raw.Write(best);
            (prev, cur) = (cur, prev);
        }

        var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
            z.Write(raw.GetBuffer(), 0, (int)raw.Length);

        var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BigEndian(header, 0, (uint)width);
        BigEndian(header, 4, (uint)height);
        header[8] = 8;
        header[9] = 6;
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", packed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    static void Chunk(Stream png, string type, byte[] data)
    {
        var head = new byte[8];
        BigEndian(head, 0, (uint)data.Length);
        Encoding.ASCII.GetBytes(type, 0, 4, head, 4);
        png.Write(head);
        png.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFF, head.AsSpan(4)), data) ^ 0xFFFFFFFF;
        var tail = new byte[4];
        BigEndian(tail, 0, crc);
        png.Write(tail);
    }

    static uint Crc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        return crc;
    }

    static void BigEndian(byte[] to, int at, uint value)
    {
        to[at] = (byte)(value >> 24);
        to[at + 1] = (byte)(value >> 16);
        to[at + 2] = (byte)(value >> 8);
        to[at + 3] = (byte)value;
    }

    // Reads the exe's icon group (the one Windows shows for the file) and checks that it lists every
    // size of the icon file, each with the same bytes.
    static int CheckExe(string exe, string icoPath)
    {
        var expected = Ico.Read(File.ReadAllBytes(icoPath));
        var module = Native.LoadLibraryEx(exe, IntPtr.Zero, Native.AsDataFile | Native.AsImageResource);
        if (module == IntPtr.Zero)
        {
            Console.WriteLine($"FAIL could not open {exe} (error {Marshal.GetLastWin32Error()})");
            return 1;
        }
        try
        {
            var groups = new List<IntPtr>();
            Native.EnumResourceNames(module, Native.GroupIcon, (_, _, name, _) => { groups.Add(name); return true; }, IntPtr.Zero);
            if (groups.Count == 0)
            {
                Console.WriteLine("FAIL the exe has no icon group");
                return 1;
            }
            var group = Native.Resource(module, groups[0], Native.GroupIcon);
            int count = BitConverter.ToUInt16(group, 4);
            bool ok = count == expected.Count;
            Console.WriteLine($"icon group {(IsId(groups[0]) ? "#" + groups[0] : Marshal.PtrToStringUni(groups[0]))}: {count} images (icon file: {expected.Count})");
            for (int i = 0; i < count; i++)
            {
                int at = 6 + i * 14;
                int size = group[at] == 0 ? 256 : group[at];
                int bits = BitConverter.ToUInt16(group, at + 6);
                int length = BitConverter.ToInt32(group, at + 8);
                int id = BitConverter.ToUInt16(group, at + 12);
                var image = Native.Resource(module, id, Native.IconImage);
                var match = expected.FirstOrDefault(e => e.Size == size);
                bool same = match is not null && image.AsSpan().SequenceEqual(match.Data) && length == image.Length;
                ok &= same;
                Console.WriteLine($"  {(same ? "ok  " : "FAIL")} {size} px, {bits}-bit, {image.Length} bytes{(match is null ? ", not in the icon file" : same ? ", same bytes as the icon file" : ", differs from the icon file")}");
            }
            foreach (var e in expected.Where(e => !Enumerable.Range(0, count).Any(i => (group[6 + i * 14] == 0 ? 256 : group[6 + i * 14]) == e.Size)))
            {
                ok = false;
                Console.WriteLine($"  FAIL {e.Size} px is missing from the exe");
            }
            Console.WriteLine(ok ? "exe icon: every size present" : "exe icon: FAIL");
            return ok ? 0 : 1;
        }
        finally { Native.FreeLibrary(module); }
    }

    static bool IsId(IntPtr name) => (ulong)name < 0x10000;

    static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src", "SteppedOut.csproj"))) return dir.FullName;
        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "src", "SteppedOut.csproj"))) return dir.FullName;
        throw new InvalidOperationException("the repository root (with src/SteppedOut.csproj) was not found");
    }

    // Logo.Draw is internal to the app, so it is found by name, as tools/store does.
    static Func<int, Bitmap> FindLogo(Assembly app)
    {
        var type = app.GetType("SteppedOut.Logo")
            ?? throw new InvalidOperationException("SteppedOut.Logo was not found in the app");
        var method = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name == "Draw" && typeof(Image).IsAssignableFrom(m.ReturnType))
            .Where(m => m.GetParameters() is [{ ParameterType: var first }, ..] && first == typeof(int))
            .Where(m => m.GetParameters().Skip(1).All(p => p.HasDefaultValue))
            .OrderBy(m => m.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Logo.Draw(int size, ...) with defaults for every other parameter was not found; update tools/brand/Program.cs");
        var rest = method.GetParameters().Skip(1).Select(p => p.DefaultValue).ToArray();
        return size =>
        {
            var image = (Image)method.Invoke(null, [size, .. rest])!;
            return image as Bitmap ?? new Bitmap(image);
        };
    }

    // The tile and padlock colours, from Logo, for the hand-tuned sizes.
    static (Color, Color) FindColors(Assembly app)
    {
        var type = app.GetType("SteppedOut.Logo")!;
        Color Field(string name) => type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as Color?
            ?? throw new InvalidOperationException($"Logo.{name} was not found; update tools/brand/Program.cs");
        return (Field("Tile"), Field("Glyph"));
    }

    // Nothing here locks the session, but the guard costs nothing.
    static void BlockLocking(Assembly app)
    {
        var guard = app.GetType("SteppedOut.SessionLock")?.GetProperty("Blocked",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        guard?.SetValue(null, true);
    }
}

// One image of an icon file: its size and its bytes as stored.
sealed record IcoEntry(int Size, bool IsPng, byte[] Data);

static class Ico
{
    public static List<IcoEntry> Read(byte[] file)
    {
        if (BitConverter.ToUInt16(file, 0) != 0 || BitConverter.ToUInt16(file, 2) != 1)
            throw new InvalidDataException("not an icon file");
        int count = BitConverter.ToUInt16(file, 4);
        var entries = new List<IcoEntry>();
        for (int i = 0; i < count; i++)
        {
            int at = 6 + i * 16;
            int size = file[at] == 0 ? 256 : file[at];
            int length = BitConverter.ToInt32(file, at + 8);
            int offset = BitConverter.ToInt32(file, at + 12);
            var data = file.AsSpan(offset, length).ToArray();
            entries.Add(new IcoEntry(size, data.AsSpan(0, 4).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }), data));
        }
        return entries;
    }

    // An entry as a picture, read the way Windows reads it: PNG as is; a bitmap from its colour
    // rows, with the mask clearing pixels when the colours carry no alpha.
    public static Bitmap Decode(IcoEntry entry)
    {
        if (entry.IsPng)
        {
            using var stream = new MemoryStream(entry.Data);
            using var png = new Bitmap(stream);
            return new Bitmap(png);
        }
        var d = entry.Data;
        int width = BitConverter.ToInt32(d, 4), height = BitConverter.ToInt32(d, 8) / 2;
        int bits = BitConverter.ToUInt16(d, 14);
        if (bits != 32) throw new InvalidDataException($"{bits}-bit icon images are not read here");
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        int header = BitConverter.ToInt32(d, 0);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int at = header + ((height - 1 - y) * width + x) * 4;
                bitmap.SetPixel(x, y, Color.FromArgb(d[at + 3], d[at + 2], d[at + 1], d[at]));
            }
        return bitmap;
    }
}

static class Native
{
    public const uint AsDataFile = 0x2;
    public const uint AsImageResource = 0x20;
    public static readonly IntPtr IconImage = 3;
    public static readonly IntPtr GroupIcon = 14;

    public delegate bool EnumName(IntPtr module, IntPtr type, IntPtr name, IntPtr param);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);

    [DllImport("kernel32.dll")]
    public static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumName callback, IntPtr param);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);

    [DllImport("kernel32.dll")]
    static extern IntPtr LoadResource(IntPtr module, IntPtr info);

    [DllImport("kernel32.dll")]
    static extern IntPtr LockResource(IntPtr data);

    [DllImport("kernel32.dll")]
    static extern uint SizeofResource(IntPtr module, IntPtr info);

    public static byte[] Resource(IntPtr module, IntPtr name, IntPtr type)
    {
        var info = FindResource(module, name, type);
        if (info == IntPtr.Zero) throw new InvalidDataException($"resource {name} of type {type} was not found");
        var bytes = new byte[SizeofResource(module, info)];
        Marshal.Copy(LockResource(LoadResource(module, info)), bytes, 0, bytes.Length);
        return bytes;
    }
}
