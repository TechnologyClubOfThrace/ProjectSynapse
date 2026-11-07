using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Synapse.App;

/// <summary>
/// Dark card container that redraws its complete border after every resize.
/// A regular Panel can leave the previous right border visible when a card
/// grows, especially at high DPI or in a maximized window.
/// </summary>
internal sealed class AtmosphericCardPanel : Panel
{
    public AtmosphericCardPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        UpdateStyles();
    }

    public Color BorderColor { get; set; } = Color.FromArgb(48, 72, 59);

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        using var pen = new Pen(BorderColor);
        eventArgs.Graphics.DrawRectangle(
            pen,
            0,
            0,
            ClientSize.Width - 1,
            ClientSize.Height - 1);
    }
}

/// <summary>
/// Keeps the top command bar inside the same dark palette, including hover,
/// pressed and disabled states.
/// </summary>
internal sealed class AtmosphericToolStripRenderer : ToolStripProfessionalRenderer
{
    public AtmosphericToolStripRenderer()
        : base(new AtmosphericColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs eventArgs)
    {
        using var brush = new SolidBrush(Color.FromArgb(15, 29, 23));
        eventArgs.Graphics.FillRectangle(brush, eventArgs.AffectedBounds);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs eventArgs)
    {
        if (eventArgs.Item is not ToolStripButton button ||
            (!button.Selected && !button.Pressed && !button.Checked))
        {
            return;
        }

        var bounds = new Rectangle(Point.Empty, button.Size);
        var background = button.Pressed || button.Checked
            ? Color.FromArgb(55, 82, 61)
            : Color.FromArgb(36, 62, 47);
        using var brush = new SolidBrush(background);
        using var pen = new Pen(Color.FromArgb(83, 116, 92));
        eventArgs.Graphics.FillRectangle(brush, bounds);
        eventArgs.Graphics.DrawRectangle(
            pen,
            0,
            0,
            Math.Max(0, bounds.Width - 1),
            Math.Max(0, bounds.Height - 1));
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs eventArgs)
    {
        if (eventArgs.Item is ToolStripButton)
        {
            eventArgs.TextColor = eventArgs.Item.Enabled
                ? Color.FromArgb(242, 239, 229)
                : Color.FromArgb(126, 148, 132);
        }
        base.OnRenderItemText(eventArgs);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs eventArgs)
    {
        var x = eventArgs.Item.Width / 2;
        using var pen = new Pen(Color.FromArgb(48, 72, 59));
        eventArgs.Graphics.DrawLine(pen, x, 3, x, Math.Max(3, eventArgs.Item.Height - 4));
    }

    private sealed class AtmosphericColorTable : ProfessionalColorTable
    {
        public override Color ToolStripBorder => Color.FromArgb(48, 72, 59);
        public override Color ToolStripGradientBegin => Color.FromArgb(15, 29, 23);
        public override Color ToolStripGradientMiddle => Color.FromArgb(15, 29, 23);
        public override Color ToolStripGradientEnd => Color.FromArgb(15, 29, 23);
        public override Color ButtonSelectedBorder => Color.FromArgb(83, 116, 92);
        public override Color ButtonSelectedGradientBegin => Color.FromArgb(36, 62, 47);
        public override Color ButtonSelectedGradientMiddle => Color.FromArgb(36, 62, 47);
        public override Color ButtonSelectedGradientEnd => Color.FromArgb(36, 62, 47);
        public override Color ButtonPressedBorder => Color.FromArgb(224, 161, 58);
        public override Color ButtonPressedGradientBegin => Color.FromArgb(55, 82, 61);
        public override Color ButtonPressedGradientMiddle => Color.FromArgb(55, 82, 61);
        public override Color ButtonPressedGradientEnd => Color.FromArgb(55, 82, 61);
    }
}

/// <summary>
/// Button with explicit colours for every state. WinForms normally fades a
/// disabled button too aggressively on a dark background, so we draw it here.
/// </summary>
internal sealed class AtmosphericButton : Button
{
    private bool pointerOver;
    private bool pointerDown;

    public AtmosphericButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint,
            true);
    }

    protected override void OnMouseEnter(EventArgs eventArgs)
    {
        pointerOver = true;
        Invalidate();
        base.OnMouseEnter(eventArgs);
    }

    protected override void OnMouseLeave(EventArgs eventArgs)
    {
        pointerOver = false;
        pointerDown = false;
        Invalidate();
        base.OnMouseLeave(eventArgs);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        pointerDown = eventArgs.Button == MouseButtons.Left;
        Invalidate();
        base.OnMouseDown(eventArgs);
    }

    protected override void OnMouseUp(MouseEventArgs eventArgs)
    {
        pointerDown = false;
        Invalidate();
        base.OnMouseUp(eventArgs);
    }

    protected override void OnEnabledChanged(EventArgs eventArgs)
    {
        pointerDown = false;
        Invalidate();
        base.OnEnabledChanged(eventArgs);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        eventArgs.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));

        var background = !Enabled
            ? Color.FromArgb(20, 38, 29)
            : pointerDown
                ? Color.FromArgb(52, 81, 61)
                : pointerOver
                    ? Color.FromArgb(43, 70, 53)
                    : Color.FromArgb(29, 52, 40);
        var border = !Enabled
            ? Color.FromArgb(57, 79, 66)
            : Focused
                ? Color.FromArgb(224, 161, 58)
                : Color.FromArgb(83, 116, 92);
        var text = Enabled
            ? Color.FromArgb(242, 239, 229)
            : Color.FromArgb(126, 148, 132);

        using var backgroundBrush = new SolidBrush(background);
        using var borderPen = new Pen(border);
        eventArgs.Graphics.FillRectangle(backgroundBrush, bounds);
        eventArgs.Graphics.DrawRectangle(borderPen, bounds);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            Text,
            Font,
            bounds,
            text,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);
    }
}

/// <summary>
/// Owner-drawn combo box that keeps the selected value, open list and arrow
/// readable without the white Windows control surface breaking the night view.
/// </summary>
internal sealed class AtmosphericComboBox : ComboBox
{
    private const int PaintMessage = 0x000F;
    private const int PrintClientMessage = 0x0318;

    private static readonly Color Surface = Color.FromArgb(20, 39, 30);
    private static readonly Color SurfaceSelected = Color.FromArgb(55, 82, 61);
    private static readonly Color Border = Color.FromArgb(74, 102, 83);
    private static readonly Color TextEnabled = Color.FromArgb(242, 239, 229);
    private static readonly Color TextDisabled = Color.FromArgb(126, 148, 132);

    public AtmosphericComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        BackColor = Surface;
        ForeColor = TextEnabled;
        ItemHeight = 20;
    }

    protected override void OnDrawItem(DrawItemEventArgs eventArgs)
    {
        if (eventArgs.Index < 0)
        {
            return;
        }

        var selected = (eventArgs.State & DrawItemState.Selected) != 0;
        var background = selected ? SurfaceSelected : Surface;
        var foreground = Enabled ? TextEnabled : TextDisabled;
        using var brush = new SolidBrush(background);
        eventArgs.Graphics.FillRectangle(brush, eventArgs.Bounds);
        TextRenderer.DrawText(
            eventArgs.Graphics,
            GetItemText(Items[eventArgs.Index]),
            Font,
            Rectangle.Inflate(eventArgs.Bounds, -5, 0),
            foreground,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);
        if ((eventArgs.State & DrawItemState.Focus) != 0)
        {
            ControlPaint.DrawFocusRectangle(eventArgs.Graphics, eventArgs.Bounds, foreground, background);
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == PaintMessage && IsHandleCreated)
        {
            var deviceContext = BeginPaint(Handle, out var paintState);
            try
            {
                using var graphics = Graphics.FromHdc(deviceContext);
                DrawClosedControl(graphics);
            }
            finally
            {
                EndPaint(Handle, ref paintState);
            }
            message.Result = IntPtr.Zero;
            return;
        }

        if (message.Msg == PrintClientMessage && message.WParam != IntPtr.Zero)
        {
            using var graphics = Graphics.FromHdc(message.WParam);
            DrawClosedControl(graphics);
            message.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref message);
    }

    private void DrawClosedControl(Graphics graphics)
    {
        var surface = Enabled ? Surface : Color.FromArgb(18, 34, 27);
        using var surfaceBrush = new SolidBrush(surface);
        graphics.FillRectangle(surfaceBrush, ClientRectangle);

        var arrowWidth = Math.Max(22, SystemInformation.VerticalScrollBarWidth + 4);
        var arrowBounds = new Rectangle(
            Math.Max(0, ClientSize.Width - arrowWidth),
            1,
            Math.Max(1, arrowWidth - 1),
            Math.Max(1, ClientSize.Height - 2));
        using var arrowBackground = new SolidBrush(surface);
        graphics.FillRectangle(arrowBackground, arrowBounds);

        var textBounds = new Rectangle(
            6,
            1,
            Math.Max(1, arrowBounds.Left - 10),
            Math.Max(1, ClientSize.Height - 2));
        TextRenderer.DrawText(
            graphics,
            Text,
            Font,
            textBounds,
            Enabled ? TextEnabled : TextDisabled,
            TextFormatFlags.Left |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis);

        var centerX = arrowBounds.Left + arrowBounds.Width / 2;
        var centerY = arrowBounds.Top + arrowBounds.Height / 2;
        var points = new[]
        {
            new Point(centerX - 4, centerY - 2),
            new Point(centerX + 4, centerY - 2),
            new Point(centerX, centerY + 3)
        };
        using var arrowBrush = new SolidBrush(Enabled ? TextEnabled : TextDisabled);
        graphics.FillPolygon(arrowBrush, points);
        using var borderPen = new Pen(Border);
        graphics.DrawRectangle(borderPen, 0, 0, Math.Max(0, ClientSize.Width - 1), Math.Max(0, ClientSize.Height - 1));
    }

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr windowHandle, out PaintState paintState);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(IntPtr windowHandle, ref PaintState paintState);

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintState
    {
        public IntPtr DeviceContext;
        public int EraseBackground;
        public Rectangle PaintRectangle;
        public int Restore;
        public int IncrementalUpdate;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Reserved;
    }
}
