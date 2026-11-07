using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Synapse.App;

/// <summary>
/// Public-facing view of the five musical layers. The logical layer order stays
/// unchanged (roots are layer 1), while the drawing follows the natural vertical
/// structure of a tree: roots at the bottom and full life at the top.
/// </summary>
internal sealed class TreeLayerVisualization : Control
{
    private static readonly string[] StageNames =
        ["Ρίζες", "Κορμός", "Κλαδιά", "Φύλλωμα", "Πλήρης ζωή"];

    private static readonly Color[] StageColors =
    [
        Color.FromArgb(224, 151, 53),
        Color.FromArgb(213, 182, 73),
        Color.FromArgb(183, 202, 73),
        Color.FromArgb(128, 194, 101),
        Color.FromArgb(79, 184, 115)
    ];

    private static readonly Color PrimaryText = Color.FromArgb(242, 239, 229);
    private static readonly Color SecondaryText = Color.FromArgb(157, 178, 162);
    private static readonly Color Track = Color.FromArgb(20, 39, 30);
    private static readonly Color Border = Color.FromArgb(48, 72, 59);
    private static readonly Lazy<Bitmap?> TreeArtwork = new(LoadTreeArtwork);

    private int activeLayerCount;
    private int growingLayerIndex = -1;

    public int ActiveLayerCount
    {
        get => activeLayerCount;
        set
        {
            var nextValue = Math.Clamp(value, 0, StageNames.Length);
            if (nextValue == activeLayerCount)
            {
                return;
            }
            activeLayerCount = nextValue;
            Invalidate();
        }
    }

    public int GrowingLayerIndex
    {
        get => growingLayerIndex;
        set
        {
            var nextValue = Math.Clamp(value, -1, StageNames.Length - 1);
            if (nextValue == growingLayerIndex)
            {
                return;
            }
            growingLayerIndex = nextValue;
            Invalidate();
        }
    }

    public TreeLayerVisualization()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.UserPaint,
            true);
        BackColor = Color.Transparent;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        if (ClientSize.Width < 200 || ClientSize.Height < 150)
        {
            return;
        }

        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

        const int outerPadding = 4;
        var labelWidth = Math.Clamp(ClientSize.Width / 8, 90, 118);
        var stateWidth = Math.Clamp(ClientSize.Width / 7, 108, 128);
        var rowGap = 8;
        var rowHeight = Math.Max(24, (ClientSize.Height - outerPadding * 2 - rowGap * 4) / 5);
        var visualLeft = labelWidth + 10;
        var visualRight = ClientSize.Width - stateWidth - 10;
        var visualWidth = Math.Max(100, visualRight - visualLeft);
        var visualArea = new Rectangle(
            visualLeft,
            outerPadding,
            visualWidth,
            rowHeight * 5 + rowGap * 4);

        var rows = new Rectangle[5];
        for (var displayRow = 0; displayRow < 5; displayRow++)
        {
            var stageIndex = 4 - displayRow;
            var top = outerPadding + displayRow * (rowHeight + rowGap);
            var row = new Rectangle(visualLeft, top, visualWidth, rowHeight);
            rows[stageIndex] = row;
            DrawStageRow(graphics, row, stageIndex);

            var labelBounds = new Rectangle(0, top, labelWidth, rowHeight);
            TextRenderer.DrawText(
                graphics,
                StageNames[stageIndex],
                Font,
                labelBounds,
                stageIndex < activeLayerCount ? PrimaryText : SecondaryText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            var state = stageIndex < activeLayerCount
                ? stageIndex == growingLayerIndex ? "Αναπτύσσεται" : "Ζωντανό"
                : "Σε αναμονή";
            var stateBounds = new Rectangle(visualRight + 8, top, stateWidth, rowHeight);
            TextRenderer.DrawText(
                graphics,
                state,
                Font,
                stateBounds,
                stageIndex < activeLayerCount ? StageColors[stageIndex] : SecondaryText,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        DrawTree(graphics, visualArea, rows);
    }

    private void DrawStageRow(Graphics graphics, Rectangle row, int stageIndex)
    {
        using var path = CreateRoundedRectangle(row, 9);
        using var trackBrush = new SolidBrush(Track);
        graphics.FillPath(trackBrush, path);

        if (stageIndex < activeLayerCount)
        {
            var color = StageColors[stageIndex];
            using var activeBrush = new LinearGradientBrush(
                row,
                Color.FromArgb(38, color),
                Color.FromArgb(72, color),
                LinearGradientMode.Horizontal);
            graphics.FillPath(activeBrush, path);
        }

        using var borderPen = new Pen(
            stageIndex < activeLayerCount ? Color.FromArgb(135, StageColors[stageIndex]) : Border);
        graphics.DrawPath(borderPen, path);
    }

    private void DrawTree(Graphics graphics, Rectangle visualArea, IReadOnlyList<Rectangle> rows)
    {
        var artwork = TreeArtwork.Value;
        if (artwork is null)
        {
            return;
        }

        var availableArea = RectangleF.Inflate(visualArea, -18F, -6F);
        var scale = Math.Min(
            availableArea.Width / artwork.Width,
            availableArea.Height / artwork.Height);
        var targetSize = new SizeF(artwork.Width * scale, artwork.Height * scale);
        var target = new RectangleF(
            availableArea.Left + (availableArea.Width - targetSize.Width) / 2F,
            availableArea.Top + (availableArea.Height - targetSize.Height) / 2F,
            targetSize.Width,
            targetSize.Height);

        var previousInterpolation = graphics.InterpolationMode;
        var previousCompositing = graphics.CompositingQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.CompositingQuality = CompositingQuality.HighQuality;

        for (var stageIndex = 0; stageIndex < 5; stageIndex++)
        {
            var graphicsState = graphics.Save();
            graphics.SetClip(rows[stageIndex]);
            var active = stageIndex < activeLayerCount;
            var color = active
                ? StageColors[stageIndex]
                : Color.FromArgb(76, 103, 84);
            using var attributes = CreateTintAttributes(color, active ? .94F : .19F);
            graphics.DrawImage(
                artwork,
                Rectangle.Round(target),
                0,
                0,
                artwork.Width,
                artwork.Height,
                GraphicsUnit.Pixel,
                attributes);
            graphics.Restore(graphicsState);
        }

        graphics.InterpolationMode = previousInterpolation;
        graphics.CompositingQuality = previousCompositing;
    }

    private static ImageAttributes CreateTintAttributes(Color color, float opacity)
    {
        var matrix = new ColorMatrix(
        [
            [color.R / 255F, 0F, 0F, 0F, 0F],
            [0F, color.G / 255F, 0F, 0F, 0F],
            [0F, 0F, color.B / 255F, 0F, 0F],
            [0F, 0F, 0F, opacity, 0F],
            [0F, 0F, 0F, 0F, 1F]
        ]);
        var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        return attributes;
    }

    private static Bitmap? LoadTreeArtwork()
    {
        using var stream = typeof(TreeLayerVisualization).Assembly.GetManifestResourceStream(
            "Synapse.App.Assets.TreeBotanical.png");
        if (stream is null)
        {
            return null;
        }
        using var source = new Bitmap(stream);
        return new Bitmap(source);
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 1)
        {
            path.AddRectangle(bounds);
            return path;
        }
        var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
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
