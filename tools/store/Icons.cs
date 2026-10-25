using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;

// Draws every icon the Store package needs from the app's own logo code, so a new logo shows up
// here without touching this file.
//
//   dotnet run --project tools/store -- <folder>
//
// <folder>/Assets gets the files the manifest names, at each scale and taskbar size.
// <folder>/listing gets the square logos the Store listing can use.
static class StoreIcons
{
    static Func<int, Bitmap> draw = null!;

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.WriteLine("usage: dotnet run --project tools/store -- <folder>");
            return 2;
        }
        // Loaded by name: no type of the app is used at compile time, so it is not a metadata reference.
        var steppedOut = Assembly.Load(new AssemblyName("SteppedOut"));
        BlockLocking(steppedOut);
        draw = FindLogo(steppedOut);

        string root = Path.GetFullPath(args[0]);
        string assets = Path.Combine(root, "Assets");
        string listing = Path.Combine(root, "listing");
        Directory.CreateDirectory(assets);
        Directory.CreateDirectory(listing);

        int[] scales = [100, 125, 150, 200, 400];
        int count = 0;
        foreach (int scale in scales)
        {
            int S(int px) => (int)Math.Round(px * scale / 100.0, MidpointRounding.AwayFromZero);
            string q = $"scale-{scale}";
            count += Save(Square(S(44), 1.0), assets, $"Square44x44Logo.{q}.png");
            count += Save(Square(S(50), 1.0), assets, $"StoreLogo.{q}.png");
            count += Save(Square(S(71), 0.6), assets, $"SmallTile.{q}.png");
            count += Save(Square(S(150), 0.5), assets, $"Square150x150Logo.{q}.png");
            count += Save(Square(S(310), 0.4), assets, $"LargeTile.{q}.png");
            count += Save(Wide(S(310), S(150), 0.5), assets, $"Wide310x150Logo.{q}.png");
            count += Save(Wide(S(620), S(300), 0.4), assets, $"SplashScreen.{q}.png");
        }

        // Taskbar, Start and file-list sizes. Unplated is what Windows uses for desktop apps; the
        // logo carries its own tile, so the plated, unplated and light-unplated files are the same.
        foreach (int size in new[] { 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256 })
        {
            count += Save(Square(size, 1.0), assets, $"Square44x44Logo.targetsize-{size}.png");
            count += Save(Square(size, 1.0), assets, $"Square44x44Logo.targetsize-{size}_altform-unplated.png");
            count += Save(Square(size, 1.0), assets, $"Square44x44Logo.targetsize-{size}_altform-lightunplated.png");
        }

        foreach (int size in new[] { 300, 1080, 2160 })
            count += Save(Square(size, 1.0), listing, $"logo-{size}.png");

        Console.WriteLine($"{count} icons written to {root}");
        return 0;
    }

    // The logo at a share of the image's side, centred on a transparent square.
    static Bitmap Square(int size, double share) => Wide(size, size, share);

    // The logo at a share of the image's height, centred. Drawn at its final size, never scaled,
    // so the small sizes get the logo's own simpler shapes.
    static Bitmap Wide(int width, int height, double share)
    {
        int side = Math.Max(1, (int)Math.Round(height * share, MidpointRounding.AwayFromZero));
        var image = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(image);
        g.Clear(Color.Transparent);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        using var logo = draw(side);
        g.DrawImageUnscaled(logo, (width - side) / 2, (height - side) / 2);
        return image;
    }

    static int Save(Bitmap image, string folder, string name)
    {
        using (image) image.Save(Path.Combine(folder, name), ImageFormat.Png);
        return 1;
    }

    // Logo.Draw is internal to the app, so it is found by name. Any extra parameters a later logo
    // adds must have defaults; if not, this stops with a message rather than guessing.
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
                "Logo.Draw(int size, ...) with defaults for every other parameter was not found; update tools/store/Icons.cs");
        var rest = method.GetParameters().Skip(1).Select(p => p.DefaultValue).ToArray();
        return size =>
        {
            var image = (Image)method.Invoke(null, [size, .. rest])!;
            return image as Bitmap ?? new Bitmap(image);
        };
    }

    // Nothing here locks the session, but the guard costs nothing.
    static void BlockLocking(Assembly app)
    {
        var guard = app.GetType("SteppedOut.SessionLock")?.GetProperty("Blocked",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        guard?.SetValue(null, true);
    }
}
