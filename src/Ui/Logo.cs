using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SteppedOut;

// The logo, drawn from shapes so it is sharp at every size: a padlock on a green tile with a shoe
// print for a keyhole, a curved sole tipped 16 degrees. The tile stays green whatever the button
// color, and the logo is never mirrored for Arabic. The smallest sizes leave the print out, since it
// would only be a smudge.
static class Logo
{
    public static readonly Color Tile = Color.FromArgb(0x0F, 0x6B, 0x54);
    public static readonly Color Glyph = Color.FromArgb(0xF4, 0xF1, 0xEA);

    // Below this many pixels there is no print.
    public const int SmallestPrint = 20;

    public static Bitmap Draw(int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.ScaleTransform(size / 128f, size / 128f);

        using (var tile = Shapes.Rounded(new RectangleF(0, 0, 128, 128), 28))
        using (var brush = new SolidBrush(Tile))
            g.FillPath(brush, tile);

        bool small = size < 24;
        bool medium = size < 48;
        using (var pen = new Pen(Glyph, small ? 14 : medium ? 11 : 10) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var shackle = new GraphicsPath())
        {
            shackle.AddLine(45, 58, 45, 45);
            shackle.AddArc(45, 26, 38, 38, 180, 180);
            shackle.AddLine(83, 45, 83, 58);
            g.DrawPath(pen, shackle);
        }

        var body = small ? new RectangleF(26, 54, 76, 56) : medium ? new RectangleF(29, 54, 70, 54) : new RectangleF(30, 54, 68, 52);
        using (var path = Shapes.Rounded(body, 12))
        using (var brush = new SolidBrush(Glyph))
            g.FillPath(brush, path);

        if (size < SmallestPrint) return bitmap;
        g.TranslateTransform(64, 82);
        g.RotateTransform(-16);
        g.TranslateTransform(-64, -82);
        using var print = new SolidBrush(Tile);
        using var sole = Sole();
        g.FillPath(print, sole);
        return bitmap;
    }

    // The print: a wide front, a narrow waist and a round heel, 19 wide and 38 tall, upright around
    // (64, 82) before it is tipped.
    static GraphicsPath Sole()
    {
        var path = new GraphicsPath();
        var at = new PointF(64, 62.5f);
        void Curve(float x1, float y1, float x2, float y2, float x, float y)
        {
            var to = new PointF(x, y);
            path.AddBezier(at, new PointF(x1, y1), new PointF(x2, y2), to);
            at = to;
        }
        Curve(70.8f, 62.5f, 74f, 68.5f, 73.2f, 75.5f);
        Curve(72.7f, 80f, 70.2f, 82.8f, 69.8f, 86.5f);
        Curve(69.5f, 89.5f, 71.2f, 92f, 70.6f, 95.2f);
        Curve(70f, 98.8f, 67.3f, 100.8f, 64f, 100.8f);
        Curve(60.4f, 100.8f, 57.6f, 98.4f, 57.6f, 94.8f);
        Curve(57.6f, 91.2f, 59.4f, 89.2f, 58.9f, 85.6f);
        Curve(58.4f, 82.4f, 55f, 79.6f, 55f, 73.6f);
        Curve(55f, 67.2f, 58.6f, 62.5f, 64f, 62.5f);
        path.CloseFigure();
        return path;
    }
}
