using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

// Pictures for checking the icon and the logo by eye: every icon size read back from the file, at
// its real size and zoomed with whole pixels kept square, on a light and a dark background; the
// README logo at the size GitHub shows it; and both beside any reference images given.
static class Sheets
{
    static readonly (string Name, Color Back, Color Ink)[] Backgrounds =
    [
        ("light", Color.FromArgb(0xF3, 0xF3, 0xF3), Color.FromArgb(0x1A, 0x1A, 0x1A)),
        ("dark", Color.FromArgb(0x20, 0x20, 0x20), Color.FromArgb(0xE8, 0xE8, 0xE8)),
    ];

    public static void Write(string folder, byte[] ico, string logoPath, string[] refs, Func<int, Bitmap> straight)
    {
        Directory.CreateDirectory(folder);
        var entries = Ico.Read(ico);
        var icons = entries.Select(e => (e.Size, Image: Ico.Decode(e))).ToList();
        using var logo = new Bitmap(logoPath);

        foreach (var (name, back, ink) in Backgrounds)
        {
            Save(Icons(icons, back, ink), Path.Combine(folder, $"icon-sizes-{name}.png"));
            Save(Tuning(icons, straight, back, ink), Path.Combine(folder, $"tuning-{name}.png"));
            Save(Logo(logo, name == "light" ? Color.White : Color.FromArgb(0x0D, 0x11, 0x17), ink), Path.Combine(folder, $"logo-{name}.png"));
        }
        Save(Compare(icons.Last().Image, logo, refs), Path.Combine(folder, "compare.png"));
        Console.WriteLine($"sheets written to {folder}");
        foreach (var (_, image) in icons) image.Dispose();
    }

    static int Zoom(int size) => size switch { <= 24 => 8, <= 32 => 6, <= 48 => 4, _ => 1 };

    static Bitmap Icons(List<(int Size, Bitmap Image)> icons, Color back, Color ink)
    {
        const int pad = 24, gap = 32, label = 22;
        int zoomRow = icons.Max(i => i.Size * Zoom(i.Size));
        int width = pad * 2 + icons.Sum(i => Math.Max(i.Size * Zoom(i.Size), 60)) + gap * (icons.Count - 1);
        int height = pad + label + 256 + gap + label + zoomRow + pad;
        var sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Start(sheet, back);
        using var font = new Font("Segoe UI", 10f, GraphicsUnit.Pixel);
        using var font12 = new Font("Segoe UI", 12f, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(ink);

        g.DrawString("real size", font12, brush, pad, pad - 2);
        g.DrawString("zoomed, every pixel a square", font12, brush, pad, pad + label + 256 + gap - 2);
        int x = pad;
        foreach (var (size, image) in icons)
        {
            int zoom = Zoom(size);
            int column = Math.Max(size * zoom, 60);
            g.DrawImageUnscaled(image, x, pad + label + 256 - size);
            g.DrawString($"{size} px", font, brush, x, pad + label + 256 - size - 14);
            int zy = pad + label + 256 + gap + label;
            g.DrawImage(image, new Rectangle(x, zy, size * zoom, size * zoom), new Rectangle(0, 0, size, size), GraphicsUnit.Pixel);
            g.DrawString($"{size} px × {zoom}", font, brush, x, zy + size * zoom + 2);
            x += column + gap;
        }
        return Grow(sheet, back, 14);
    }

    // Each hand-tuned size beside Logo.Draw's straight render at the same size, both zoomed.
    static Bitmap Tuning(List<(int Size, Bitmap Image)> icons, Func<int, Bitmap> straight, Color back, Color ink)
    {
        const int pad = 24, gap = 24, label = 20, zoom = 12;
        var tuned = icons.Where(i => Small.Has(i.Size)).ToList();
        int width = pad * 2 + tuned.Sum(i => i.Size * zoom * 2 + gap * 3) - gap * 2;
        int height = pad * 2 + label + tuned.Max(i => i.Size) * zoom;
        var sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Start(sheet, back);
        using var font = new Font("Segoe UI", 12f, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(ink);
        int x = pad;
        foreach (var (size, image) in tuned)
        {
            int side = size * zoom;
            using var plain = straight(size);
            g.DrawImage(plain, new Rectangle(x, pad + label, side, side), new Rectangle(0, 0, size, size), GraphicsUnit.Pixel);
            g.DrawString($"{size} px drawn straight", font, brush, x, pad - 2);
            x += side + gap;
            g.DrawImage(image, new Rectangle(x, pad + label, side, side), new Rectangle(0, 0, size, size), GraphicsUnit.Pixel);
            g.DrawString($"{size} px tuned (in the icon)", font, brush, x, pad - 2);
            x += side + gap * 2;
        }
        return sheet;
    }

    static Bitmap Logo(Bitmap logo, Color back, Color ink)
    {
        const int pad = 24, label = 22;
        int width = pad * 3 + 80 + logo.Width;
        int height = pad * 2 + label + logo.Height;
        var sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Start(sheet, back);
        using var font = new Font("Segoe UI", 12f, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(ink);
        g.DrawString("as the README shows it (80 px)", font, brush, pad, pad - 2);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(logo, new Rectangle(pad, pad + label, 80, 80));
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.DrawImageUnscaled(logo, pad * 2 + 80, pad + label);
        g.DrawString($"docs/logo.png, {logo.Width} px", font, brush, pad * 2 + 80, pad + label + logo.Height + 2);
        return Grow(sheet, back, 16);
    }

    static Bitmap Compare(Bitmap icon256, Bitmap logo, string[] refs)
    {
        const int pad = 24, cell = 256, label = 34, gap = 24;
        var items = new List<(string Name, Image Image)>
        {
            ("src/icon.ico, 256 px entry", icon256),
            ($"docs/logo.png, {logo.Width} px", logo),
        };
        var loaded = refs.Select(r => (Name: Path.GetFileName(r), Image: (Image)new Bitmap(r))).ToList();
        items.AddRange(loaded);

        int perRow = 4;
        int rows = (items.Count + perRow - 1) / perRow;
        int width = pad * 2 + perRow * cell + (perRow - 1) * gap;
        int height = pad * 2 + rows * (cell + label) + (rows - 1) * gap;
        var back = Color.FromArgb(0xF3, 0xF3, 0xF3);
        var sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Start(sheet, back);
        using var font = new Font("Segoe UI", 11f, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.FromArgb(0x1A, 0x1A, 0x1A));
        using var frame = new Pen(Color.FromArgb(0xC8, 0xC8, 0xC8));
        for (int i = 0; i < items.Count; i++)
        {
            var (name, image) = items[i];
            int x = pad + i % perRow * (cell + gap), y = pad + i / perRow * (cell + label + gap);
            g.DrawRectangle(frame, x - 1, y - 1, cell + 1, cell + 1);
            double scale = Math.Min(1.0, Math.Min((double)cell / image.Width, (double)cell / image.Height));
            int w = (int)Math.Round(image.Width * scale), h = (int)Math.Round(image.Height * scale);
            g.InterpolationMode = scale < 1 ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
            g.DrawImage(image, new Rectangle(x + (cell - w) / 2, y + (cell - h) / 2, w, h));
            g.DrawString(name + (scale < 1 ? $" ({image.Width}×{image.Height}, scaled)" : ""), font, brush,
                new RectangleF(x, y + cell + 4, cell, label));
        }
        foreach (var (_, image) in loaded) image.Dispose();
        return sheet;
    }

    static Graphics Start(Bitmap sheet, Color back)
    {
        var g = Graphics.FromImage(sheet);
        g.Clear(back);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        return g;
    }

    // Room under the last labels.
    static Bitmap Grow(Bitmap sheet, Color back, int extra)
    {
        var bigger = new Bitmap(sheet.Width, sheet.Height + extra, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bigger))
        {
            g.Clear(back);
            g.DrawImageUnscaled(sheet, 0, 0);
        }
        sheet.Dispose();
        return bigger;
    }

    static void Save(Bitmap image, string path)
    {
        using (image) File.WriteAllBytes(path, Brand.Png(image));
    }
}
