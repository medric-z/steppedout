using System.Reflection;
using System.Runtime.InteropServices;

namespace SteppedOut.Tests;

[TestClass]
public class GlyphsTests
{
    // GGI_MARK_NONEXISTING_GLYPHS: a character the font does not have reads 0xFFFF.
    const uint MarkMissing = 1;
    const uint GdiError = 0xFFFFFFFF;

    // Windows 10 has no Segoe Fluent Icons, so every icon is drawn from Segoe MDL2 Assets there. One
    // only the newer font has would be an empty box.
    // This asks the fonts installed on the PC that runs it. On Windows 11 that is Windows 11's copy of
    // Segoe MDL2 Assets, which has more icons than Windows 10's, so a pass here does not prove
    // Windows 10: that needs this test run once on Windows 10 before a release.
    [TestMethod]
    [DataRow("Segoe MDL2 Assets")]
    [DataRow("Segoe Fluent Icons")]
    public void EveryIconIsInBothIconFonts(string family)
    {
        using var font = new Font(family, 12f);
        if (font.Name != family) Assert.Inconclusive($"{family} is not installed here.");
        var missing = new List<string>();
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        IntPtr handle = font.ToHfont();
        try
        {
            SelectObject(dc, handle);
            foreach (var field in typeof(Glyphs).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                string glyph = (string)field.GetValue(null)!;
                var indices = new ushort[glyph.Length];
                if (GetGlyphIndicesW(dc, glyph, glyph.Length, indices, MarkMissing) == GdiError || indices.Contains((ushort)0xFFFF))
                    missing.Add($"{field.Name} U+{(int)glyph[0]:X4}");
            }
        }
        finally
        {
            DeleteObject(handle);
            DeleteDC(dc);
        }
        Assert.IsEmpty(missing, $"missing from {family}: {string.Join(", ", missing)}");
    }

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetGlyphIndicesW(IntPtr dc, string text, int count, ushort[] indices, uint flags);
}
