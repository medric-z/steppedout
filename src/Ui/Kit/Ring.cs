using System.Drawing.Drawing2D;

namespace SteppedOut;

// The ring around the logo: 84 px in the status box, 36 px in the tray menu. Its track is the status
// box's edge color, and an arc in the accent, with round ends, fills it clockwise from 12 o'clock as
// idle time passes, full at the lock. In Arabic the arc runs the other way; the logo inside is never
// mirrored. In high contrast the track is a thin line under a thick arc.
sealed class Ring : KitControl
{
    public const int Large = 84;
    public const int Small = 36;

    readonly int diameter;
    float fraction;

    public Ring(int diameter = Large)
    {
        this.diameter = diameter;
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        AccessibleRole = AccessibleRole.Graphic;
        Size = new Size(diameter, diameter);
    }

    public int Diameter => diameter;

    // How much of the lock time has passed, from 0 (empty) to 1 (full).
    public float Fraction
    {
        get => fraction;
        set
        {
            value = float.IsNaN(value) ? 0 : Math.Clamp(value, 0f, 1f);
            if (value == fraction) return;
            fraction = value;
            Invalidate();
        }
    }

    // The ring for a status: idle time over the lock time while it counts, empty while it never locks,
    // is paused or is starting (D19), and full once you are away or the PC is locked.
    public static float FractionOf(LockStatus status) => status.Kind switch
    {
        LockState.On or LockState.Countdown or LockState.LockingOff =>
            status.Time.TotalSeconds is decimal total && total > 0 ? (float)Math.Clamp(status.IdleSeconds / (double)total, 0, 1) : 0f,
        LockState.Away or LockState.SessionLocked => 1f,
        _ => 0f,
    };

    public override Size GetPreferredSize(Size proposedSize) => new(Px(diameter), Px(diameter));

    protected override void OnPaint(PaintEventArgs e)
    {
        int side = Math.Min(Width, Height);
        Draw(e.Graphics, new Rectangle((Width - side) / 2, (Height - side) / 2, side, side), fraction,
            RightToLeft == RightToLeft.Yes, IsMirrored);
        base.OnPaint(e);
    }

    // The ring in a square box, also for what draws it by hand, like the tray menu's status box.
    // rightToLeft: the arc runs counter-clockwise. mirrored: the surface mirrors what is drawn on it,
    // as a window laid out right to left does, so the arc and the logo are drawn the other way round
    // and come out right.
    public static void Draw(Graphics g, Rectangle box, float fraction, bool rightToLeft, bool mirrored, Theme? theme = null)
    {
        var t = theme ?? Theme.Current;
        float side = Math.Min(box.Width, box.Height);
        float stroke = side * 0.07f;
        float radius = side * 0.45f;
        var center = new PointF(box.X + box.Width / 2f, box.Y + box.Height / 2f);
        var round = new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        var saved = g.Save();

        // The logo fills the middle 62%, copied pixel for pixel, as the welcome window does.
        int logoSide = (int)Math.Round(side * 0.62f);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(LogoAt(logoSide, flipped: mirrored),
            new Rectangle((int)Math.Round(center.X - logoSide / 2f), (int)Math.Round(center.Y - logoSide / 2f), logoSide, logoSide));

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        // In high contrast the track is the text color and the arc the highlight, which can be as
        // close as 1.4:1 to each other, so the track is a thin line there and the arc shows by its
        // thickness as well as its color.
        float trackStroke = t.IsHighContrast ? Math.Max(1f, stroke / 3f) : stroke;
        using (var track = new Pen(t.HeroEdge, trackStroke))
            g.DrawEllipse(track, round);
        float f = float.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0f, 1f);
        if (f > 0)
        {
            using var arc = new Pen(t.Accent, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (f >= 1)
            {
                g.DrawEllipse(arc, round);
            }
            else
            {
                // At least a dot, so a ring that has just started counting shows it.
                float sweep = Math.Max(f * 360f, 1f);
                g.DrawArc(arc, round, -90, rightToLeft != mirrored ? -sweep : sweep);
            }
        }
        g.Restore(saved);
    }

    // A handful of sizes for the life of the app, each also turned round for mirrored windows.
    static readonly Dictionary<(int, bool), Bitmap> Logos = new();

    static Bitmap LogoAt(int side, bool flipped)
    {
        if (Logos.TryGetValue((side, flipped), out var logo)) return logo;
        logo = Logo.Draw(side);
        if (flipped) logo.RotateFlip(RotateFlipType.RotateNoneFlipX);
        return Logos[(side, flipped)] = logo;
    }
}
