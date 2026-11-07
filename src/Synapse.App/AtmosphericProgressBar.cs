using System.Drawing.Drawing2D;

namespace Synapse.App;

/// <summary>
/// A quiet, rounded progress bar for the public installation view.
/// It is drawn locally, so its colours remain consistent on Windows 10 and 11.
/// </summary>
internal sealed class AtmosphericProgressBar : Control
{
    private int minimum;
    private int maximum = 100;
    private int currentValue;

    public int Minimum
    {
        get => minimum;
        set
        {
            minimum = Math.Min(value, maximum - 1);
            Value = currentValue;
            Invalidate();
        }
    }

    public int Maximum
    {
        get => maximum;
        set
        {
            maximum = Math.Max(value, minimum + 1);
            Value = currentValue;
            Invalidate();
        }
    }

    public int Value
    {
        get => currentValue;
        set
        {
            var nextValue = Math.Clamp(value, minimum, maximum);
            if (nextValue == currentValue)
            {
                return;
            }

            currentValue = nextValue;
            Invalidate();
        }
    }

    public Color TrackColor { get; set; } = Color.FromArgb(35, 54, 45);

    public Color BorderColor { get; set; } = Color.FromArgb(55, 78, 65);

    public Color FillStartColor { get; set; } = Color.FromArgb(225, 157, 54);

    public Color FillEndColor { get; set; } = Color.FromArgb(104, 190, 105);

    public AtmosphericProgressBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        var radius = Math.Min(Height / 2, 8);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var trackPath = CreateRoundedRectangle(bounds, radius);
        using var trackBrush = new SolidBrush(TrackColor);
        e.Graphics.FillPath(trackBrush, trackPath);

        var range = maximum - minimum;
        var ratio = range <= 0 ? 0D : (currentValue - minimum) / (double)range;
        var fillWidth = (int)Math.Round(bounds.Width * ratio);
        if (fillWidth > 0)
        {
            var fillBounds = new Rectangle(bounds.X, bounds.Y, Math.Max(1, fillWidth), bounds.Height);
            using var fillPath = CreateRoundedRectangle(fillBounds, Math.Min(radius, fillWidth / 2));
            using var fillBrush = new LinearGradientBrush(
                fillBounds,
                FillStartColor,
                FillEndColor,
                LinearGradientMode.Horizontal);
            e.Graphics.FillPath(fillBrush, fillPath);
        }

        using var borderPen = new Pen(BorderColor);
        e.Graphics.DrawPath(borderPen, trackPath);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
