using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// The small icon sizes, tuned by hand. Drawn straight, the logo's edges fall between pixels at
// these sizes and come out as soft half-tone rows and columns. These grids put the same shapes
// (Logo.cs's padlock, its proportions per size, and the shoe print from 20 px up, as Logo has it)
// on whole pixels. The tile, its rounded corners and its edge smoothing stay exactly as Logo.Draw
// gives them; only the cream padlock is placed by hand. Each grid marks, per pixel:
//   #  the padlock (cream)
//   +  half cream, half tile: a curve that falls in the middle of a pixel
//   -  the tile (or outside it, which Logo.Draw leaves clear)
// The 16 px padlock follows the tray icon's 16 px art, taller than a straight render, so the
// shackle reads as a shackle.
static class Small
{
    static readonly Dictionary<int, string[]> Grids = new()
    {
        [16] =
        [
            "----------------",
            "----------------",
            "------####------",
            "-----##--##-----",
            "----##----##----",
            "----##----##----",
            "----##----##----",
            "---+########+---",
            "---##########---",
            "---##########---",
            "---##########---",
            "---##########---",
            "---##########---",
            "---+########+---",
            "----------------",
            "----------------",
        ],
        [20] =
        [
            "--------------------",
            "--------------------",
            "--------------------",
            "-------+####+-------",
            "-------######-------",
            "------##+--+##------",
            "------##----##------",
            "------##----##------",
            "------##----##------",
            "----+##########+----",
            "----#####--#####----",
            "----#####--#####----",
            "----#####--#####----",
            "----#####+-#####----",
            "----######--####----",
            "----######-+####----",
            "----+##########+----",
            "--------------------",
            "--------------------",
            "--------------------",
        ],
        [24] =
        [
            "------------------------",
            "------------------------",
            "------------------------",
            "------------------------",
            "---------+####+---------",
            "--------+######+--------",
            "-------+##+--+##+-------",
            "-------##------##-------",
            "-------##------##-------",
            "-------##------##-------",
            "-----+############+-----",
            "-----##############-----",
            "-----#####+-+######-----",
            "-----#####---######-----",
            "-----#####---######-----",
            "-----#####+--######-----",
            "-----######+-+#####-----",
            "-----#######--#####-----",
            "-----#######--#####-----",
            "-----+############+-----",
            "------------------------",
            "------------------------",
            "------------------------",
            "------------------------",
        ],
        [32] =
        [
            "--------------------------------",
            "--------------------------------",
            "--------------------------------",
            "--------------------------------",
            "--------------------------------",
            "--------------+##+--------------",
            "------------+######+------------",
            "-----------+########+-----------",
            "-----------###+--+###-----------",
            "----------+##+----+##+----------",
            "----------###------###----------",
            "----------###------###----------",
            "----------###------###----------",
            "----------###------###----------",
            "--------+##############+--------",
            "-------+################+-------",
            "-------#######+-+########-------",
            "-------######+---+#######-------",
            "-------######+----#######-------",
            "-------######+----#######-------",
            "-------#######----#######-------",
            "-------########---#######-------",
            "-------########---#######-------",
            "-------########+--+######-------",
            "-------########+--#######-------",
            "-------+################+-------",
            "--------+##############+--------",
            "--------------------------------",
            "--------------------------------",
            "--------------------------------",
            "--------------------------------",
            "--------------------------------",
        ],
    };

    public static bool Has(int size) => Grids.ContainsKey(size);

    // The logo at a hand-tuned size: Logo.Draw's tile with the grid's padlock on it.
    public static Bitmap Draw(int size, Func<int, Bitmap> logo, Color tile, Color glyph)
    {
        var grid = Grids[size];
        if (grid.Length != size || grid.Any(row => row.Length != size))
            throw new InvalidOperationException($"the {size} px grid is not {size} by {size}");
        var bitmap = logo(size);
        var data = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * size * 4, size * 4);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int at = (y * size + x) * 4;
                    int share = grid[y][x] switch { '#' => 2, '+' => 1, _ => 0 };
                    if (share > 0 && pixels[at + 3] != 255)
                        throw new InvalidOperationException($"the {size} px grid puts the padlock outside the tile at ({x}, {y})");
                    pixels[at] = Mix(tile.B, glyph.B, share);
                    pixels[at + 1] = Mix(tile.G, glyph.G, share);
                    pixels[at + 2] = Mix(tile.R, glyph.R, share);
                }
            for (int y = 0; y < size; y++)
                Marshal.Copy(pixels, y * size * 4, data.Scan0 + y * data.Stride, size * 4);
        }
        finally { bitmap.UnlockBits(data); }
        return bitmap;
    }

    static byte Mix(byte from, byte to, int halves) => (byte)((from * (2 - halves) + to * halves + 1) / 2);
}
