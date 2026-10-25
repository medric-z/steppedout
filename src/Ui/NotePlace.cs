namespace SteppedOut;

// Where a corner note goes on a screen. The corners are the screen's own in both languages: bottom
// right is bottom right in Arabic too, and top left is top left. Center is the middle of the work area. The note always stays
// inside the work area, so a note wider than a small screen starts at its edge.
static class NotePlace
{
    // Logical pixels between the note and the edges of the work area.
    public const int Margin = 16;

    // Every place, in the order the Appearance tab lists them.
    public static readonly IReadOnlyList<NotePosition> All =
        [NotePosition.BottomRight, NotePosition.BottomLeft, NotePosition.TopRight, NotePosition.TopLeft, NotePosition.Center];

    // The place in use: the one picked, or until then the side the Windows shell keeps the clock on
    // (D37), so a right-to-left shell puts notes bottom left and any other bottom right.
    public static NotePosition InUse(NotePosition? picked, bool shellRightToLeft) => picked ?? Config.ShellSide(shellRightToLeft);

    public static Rectangle For(NotePosition place, Rectangle area, Size size, int margin)
    {
        int left = area.Left + margin;
        int right = area.Right - margin - size.Width;
        int top = area.Top + margin;
        int bottom = area.Bottom - margin - size.Height;
        var (x, y) = place switch
        {
            NotePosition.TopRight => (right, top),
            NotePosition.TopLeft => (left, top),
            NotePosition.BottomLeft => (left, bottom),
            NotePosition.Center => (area.Left + (area.Width - size.Width) / 2, area.Top + (area.Height - size.Height) / 2),
            _ => (right, bottom),
        };
        x = Math.Max(area.Left, Math.Min(x, area.Right - size.Width));
        y = Math.Max(area.Top, Math.Min(y, area.Bottom - size.Height));
        return new Rectangle(x, y, size.Width, size.Height);
    }

    public static string Label(NotePosition place, Strings text) => place switch
    {
        NotePosition.TopRight => text.V1.TopRight,
        NotePosition.TopLeft => text.V1.TopLeft,
        NotePosition.BottomLeft => text.V1.BottomLeft,
        NotePosition.Center => text.V1.Center,
        _ => text.V1.BottomRight,
    };
}
