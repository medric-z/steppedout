using System.Drawing.Imaging;

namespace SteppedOut;

// The tray icon's pixel art (D23): the padlock on its rounded tile, drawn pixel by pixel with no
// smoothing so it stays sharp in the tray. The 16 px grid is the design sketch's; the 20 and 24 px
// grids follow the same shapes. Each grid marks, per pixel:
//   .  outside the tile (clear)
//   #  the tile
//   L  the padlock: its shackle and its body
//   K  the keyhole, a shoe print, cut out of the body (on and counting down)
//   P  a pause bar, cut out of the body (paused)
//   X  both a keyhole and a pause bar pixel
// Cut-outs show the tile's color. Minutes, when shown, replace the keyhole with digits cut out of
// the body the same way.
static class TrayArt
{
    internal sealed record Grid(int Size, string[] Rows, DigitFont Font, int DigitTop, int SingleLeft, int PairLeft);

    internal sealed record DigitFont(int Width, int Height, string[][] Glyphs);

    // Colors per state (D23): always the brand greens, whatever button color the settings use.
    // Counting down is the bright green on both taskbars, so it differs from On; paused is gray,
    // in the taskbar's own gray.
    internal static (Color Tile, Color Lock) Colors(TrayIconState state, bool darkTaskbar) => state switch
    {
        TrayIconState.CountingDown => (Rgb(0x4FC9A4), Rgb(0x0B2A22)),
        TrayIconState.Paused => darkTaskbar ? (Rgb(0x5A5A5A), Rgb(0xC5C5C5)) : (Rgb(0xC4C4C4), Rgb(0x5F5F5F)),
        _ => (Rgb(0x0F6B54), Rgb(0xF4F1EA)),
    };

    static Color Rgb(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    internal static readonly DigitFont Small = new(3, 5,
    [
        ["###", "#.#", "#.#", "#.#", "###"],
        [".#.", "##.", ".#.", ".#.", "###"],
        ["###", "..#", "###", "#..", "###"],
        ["###", "..#", "###", "..#", "###"],
        ["#.#", "#.#", "###", "..#", "..#"],
        ["###", "#..", "###", "..#", "###"],
        ["###", "#..", "###", "#.#", "###"],
        ["###", "..#", "..#", "..#", "..#"],
        ["###", "#.#", "###", "#.#", "###"],
        ["###", "#.#", "###", "..#", "###"],
    ]);

    internal static readonly DigitFont Medium = new(4, 6,
    [
        [".##.", "#..#", "#..#", "#..#", "#..#", ".##."],
        ["..#.", ".##.", "..#.", "..#.", "..#.", ".###"],
        [".##.", "#..#", "...#", "..#.", ".#..", "####"],
        ["###.", "...#", ".##.", "...#", "...#", "###."],
        ["#..#", "#..#", "#..#", "####", "...#", "...#"],
        ["####", "#...", "###.", "...#", "...#", "###."],
        [".##.", "#...", "###.", "#..#", "#..#", ".##."],
        ["####", "...#", "..#.", ".#..", ".#..", ".#.."],
        [".##.", "#..#", ".##.", "#..#", "#..#", ".##."],
        [".##.", "#..#", "#..#", ".###", "...#", ".##."],
    ]);

    internal static readonly DigitFont Large = new(5, 7,
    [
        [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
        ["..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###."],
        [".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####"],
        [".###.", "#...#", "....#", "..##.", "....#", "#...#", ".###."],
        ["...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#."],
        ["#####", "#....", "####.", "....#", "....#", "#...#", ".###."],
        ["..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###."],
        ["#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..."],
        [".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###."],
        [".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.."],
    ]);

    // The sketch's 16 px icon.
    internal static readonly Grid Grid16 = new(16,
    [
        "..############..",
        ".##############.",
        "######LLLL######",
        "#####LL##LL#####",
        "####LL####LL####",
        "####LL####LL####",
        "####LL####LL####",
        "###LLLLLLLLLL###",
        "###LLPPKKPPLL###",
        "###LLPPKKXPLL###",
        "###LLPPKKPPLL###",
        "###LLPXKLPPLL###",
        "###LLPXKLPPLL###",
        "###LLLLLLLLLL###",
        ".##############.",
        "..############..",
    ], Small, DigitTop: 8, SingleLeft: 6, PairLeft: 4);

    internal static readonly Grid Grid20 = new(20,
    [
        "..################..",
        ".##################.",
        "####################",
        "#######LLLLLL#######",
        "######LL####LL######",
        "#####LL######LL#####",
        "#####LL######LL#####",
        "#####LL######LL#####",
        "#####LL######LL#####",
        "####LLLLLLLLLLLL####",
        "####LLPPPKKPPPLL####",
        "####LLPPPKKXPPLL####",
        "####LLPPPKKXPPLL####",
        "####LLPPPKKPPPLL####",
        "####LLPPXKLPPPLL####",
        "####LLPPXKLPPPLL####",
        "####LLPPXKLPPPLL####",
        "####LLLLLLLLLLLL####",
        ".##################.",
        "..################..",
    ], Medium, DigitTop: 11, SingleLeft: 8, PairLeft: 5);

    internal static readonly Grid Grid24 = new(24,
    [
        "...##################...",
        ".######################.",
        ".######################.",
        "#########LLLLLL#########",
        "########LLLLLLLL########",
        "#######LLL####LLL#######",
        "######LLL######LLL######",
        "#####LLL########LLL#####",
        "#####LLL########LLL#####",
        "#####LLL########LLL#####",
        "####LLLLLLLLLLLLLLLL####",
        "####LLLLLLLLLLLLLLLL####",
        "####LLLPPPLKKKPPPLLL####",
        "####LLLPPPLKKKXPPLLL####",
        "####LLLPPPLKKKXPPLLL####",
        "####LLLPPPLKKKPPPLLL####",
        "####LLLPPPKKKLPPPLLL####",
        "####LLLPPXKKLLPPPLLL####",
        "####LLLPPXKKLLPPPLLL####",
        "####LLLLLLLLLLLLLLLL####",
        "####LLLLLLLLLLLLLLLL####",
        ".######################.",
        ".######################.",
        "...##################...",
    ], Large, DigitTop: 12, SingleLeft: 9, PairLeft: 6);

    internal static readonly Grid[] Grids = [Grid16, Grid20, Grid24];

    // How a size is drawn: from a grid, each of its pixels a square of Scale by Scale screen pixels,
    // Inset clear pixels in from the top and the left. Scale 0 is a size under the smallest grid,
    // which the 16 px art is shrunk to.
    internal readonly record struct Layout(Grid Grid, int Scale, int Inset)
    {
        public int Drawn => Grid.Size * Scale;
    }

    // The largest whole multiple of a grid that fits the size, centered with clear pixels around it,
    // so every stroke and digit is a whole number of pixels and all of them alike: 32 is the 16 px
    // art doubled and 48 the 24 px art doubled, 28 (175%) the 24 px art with 2 clear pixels each
    // side and 36 (225%) the 16 px art doubled with 2 each side. A tie goes to the bigger grid, which
    // has more detail.
    internal static Layout LayoutFor(int size)
    {
        Layout? best = null;
        foreach (var grid in Grids)
        {
            int scale = size / grid.Size;
            if (scale < 1) continue;
            int drawn = grid.Size * scale;
            if (best is not { } b || drawn > b.Drawn || drawn == b.Drawn && grid.Size > b.Grid.Size)
                best = new Layout(grid, scale, (size - drawn) / 2);
        }
        return best ?? new Layout(Grid16, 0, 0);
    }

    internal static Grid GridFor(int size) => LayoutFor(size).Grid;

    // The icon at this size. minutes is 1 to 99 or null; it is never drawn while paused.
    internal static Bitmap Draw(TrayIconState state, int size, bool darkTaskbar, int? minutes)
    {
        if (size < 1) throw new ArgumentOutOfRangeException(nameof(size));
        var layout = LayoutFor(size);
        var grid = layout.Grid;
        var cells = Cells(grid, state, state == TrayIconState.Paused ? null : minutes);
        var (tile, glyph) = Colors(state, darkTaskbar);
        // The grid cell under a screen pixel, or -1 in the clear border around it.
        int Cell(int at) => layout.Scale == 0 ? at * grid.Size / size
            : at < layout.Inset || at >= layout.Inset + layout.Drawn ? -1 : (at - layout.Inset) / layout.Scale;
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new int[size];
            for (int y = 0; y < size; y++)
            {
                int gy = Cell(y);
                for (int x = 0; x < size; x++)
                {
                    int gx = Cell(x);
                    row[x] = gx < 0 || gy < 0 ? 0 : cells[gy, gx] switch
                    {
                        TrayArt.Cell.Tile => tile.ToArgb(),
                        TrayArt.Cell.Lock => glyph.ToArgb(),
                        _ => 0,
                    };
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, size);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    internal enum Cell { Clear, Tile, Lock }

    // What each pixel of a grid is for a state, before any stretching.
    internal static Cell[,] Cells(Grid grid, TrayIconState state, int? minutes)
    {
        int n = grid.Size;
        var cells = new Cell[n, n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                char c = grid.Rows[y][x];
                bool hole = state == TrayIconState.Paused ? c is 'P' or 'X' : minutes is null && c is 'K' or 'X';
                cells[y, x] = c switch
                {
                    '.' => Cell.Clear,
                    '#' => Cell.Tile,
                    _ => hole ? Cell.Tile : Cell.Lock,
                };
            }
        }
        if (minutes is int m) Digits(grid, cells, m);
        return cells;
    }

    // Cuts the minutes out of the body, centered where the keyhole was.
    static void Digits(Grid grid, Cell[,] cells, int minutes)
    {
        var font = grid.Font;
        string digits = minutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int left = digits.Length == 1 ? grid.SingleLeft : grid.PairLeft;
        foreach (char d in digits)
        {
            var glyph = font.Glyphs[d - '0'];
            for (int y = 0; y < font.Height; y++)
                for (int x = 0; x < font.Width; x++)
                    if (glyph[y][x] == '#') cells[grid.DigitTop + y, left + x] = Cell.Tile;
            left += font.Width + 1;
        }
    }
}
