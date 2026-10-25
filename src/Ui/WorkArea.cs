namespace SteppedOut;

// The part of a screen that windows may cover, without the taskbar. The render checks put a
// smaller screen in its place, to see the windows on a laptop's.
static class WorkArea
{
    internal static Func<Screen, Rectangle> Of { get; set; } = screen => screen.WorkingArea;
}
