using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace Jarvis.Native;

public enum CoreMode
{
    Idle,
    Listening,
    Capturing,
    Processing,
    Speaking,
    Paused,
    Error
}

public static class TechTheme
{
    public static readonly Color Background = Color.FromArgb(5, 9, 18);
    public static readonly Color Panel = Color.FromArgb(10, 18, 32);
    public static readonly Color PanelRaised = Color.FromArgb(14, 25, 43);
    public static readonly Color PanelLight = Color.FromArgb(19, 32, 52);
    public static readonly Color Cyan = Color.FromArgb(55, 226, 255);
    public static readonly Color CyanSoft = Color.FromArgb(95, 178, 198);
    public static readonly Color Violet = Color.FromArgb(142, 99, 255);
    public static readonly Color Amber = Color.FromArgb(255, 191, 82);
    public static readonly Color Red = Color.FromArgb(255, 102, 126);
    public static readonly Color Text = Color.FromArgb(233, 241, 252);
    public static readonly Color Muted = Color.FromArgb(115, 136, 164);
    public static readonly Color Border = Color.FromArgb(37, 69, 94);

    public static GraphicsPath Rounded(RectangleF bounds, float radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        if (diameter <= 0.5F)
        {
            path.AddRectangle(bounds);
            path.CloseFigure();
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

public class TechPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = TechTheme.Panel;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColorBottom { get; set; } = Color.FromArgb(7, 14, 27);
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = TechTheme.Border;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float CornerRadius { get; set; } = 18;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool AccentCorners { get; set; } = true;

    public TechPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Padding = new Padding(1);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        using var path = TechTheme.Rounded(bounds, CornerRadius);
        using var fill = new LinearGradientBrush(bounds, FillColor, FillColorBottom, 90F);
        graphics.FillPath(fill, path);
        using var border = new Pen(BorderColor, 1F);
        graphics.DrawPath(border, path);

        if (AccentCorners && Width > 80 && Height > 50)
        {
            using var accent = new Pen(Color.FromArgb(150, TechTheme.Cyan), 1.6F);
            graphics.DrawLine(accent, 20, 1.5F, 48, 1.5F);
            graphics.DrawLine(accent, 1.5F, 20, 1.5F, 42);
            using var violet = new Pen(Color.FromArgb(95, TechTheme.Violet), 1.4F);
            graphics.DrawLine(violet, Width - 48, Height - 2F, Width - 20, Height - 2F);
            graphics.DrawLine(violet, Width - 2F, Height - 42, Width - 2F, Height - 20);
        }

        base.OnPaint(eventArgs);
    }
}

public sealed class TechCoreControl : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 35 };
    private float _phase;
    private CoreMode _mode = CoreMode.Idle;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CoreMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            Invalidate();
        }
    }

    public TechCoreControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        _timer.Tick += (_, _) =>
        {
            _phase = (_phase + SpeedForMode()) % 360F;
            Invalidate();
        };
        _timer.Start();
    }

    private float SpeedForMode() => _mode switch
    {
        CoreMode.Processing => 4.5F,
        CoreMode.Capturing => 3.2F,
        CoreMode.Speaking => 2.6F,
        CoreMode.Paused => 0.3F,
        _ => 1.25F
    };

    private Color ActiveColor() => _mode switch
    {
        CoreMode.Error => TechTheme.Red,
        CoreMode.Processing => TechTheme.Amber,
        CoreMode.Capturing => TechTheme.Amber,
        CoreMode.Speaking => TechTheme.Violet,
        CoreMode.Paused => TechTheme.Muted,
        _ => TechTheme.Cyan
    };

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        var color = ActiveColor();
        var center = new PointF(Width / 2F, Height / 2F);
        var radius = Math.Max(25F, Math.Min(Width, Height) / 2F - 17F);

        using (var gridPen = new Pen(Color.FromArgb(28, color), 1F))
        {
            graphics.DrawLine(gridPen, center.X - radius - 8, center.Y, center.X + radius + 8, center.Y);
            graphics.DrawLine(gridPen, center.X, center.Y - radius - 8, center.X, center.Y + radius + 8);
        }

        for (var index = 0; index < 36; index++)
        {
            var angle = (index * 10 + _phase * 0.16F) * MathF.PI / 180F;
            var outer = radius + (index % 3 == 0 ? 4 : 1);
            var inner = radius - (index % 3 == 0 ? 7 : 4);
            var alpha = index % 3 == 0 ? 145 : 62;
            using var tick = new Pen(Color.FromArgb(alpha, color), index % 3 == 0 ? 1.5F : 1F);
            graphics.DrawLine(
                tick,
                center.X + MathF.Cos(angle) * inner,
                center.Y + MathF.Sin(angle) * inner,
                center.X + MathF.Cos(angle) * outer,
                center.Y + MathF.Sin(angle) * outer);
        }

        DrawGlowArc(graphics, center, radius - 12, color, _phase, 104, 3F);
        DrawGlowArc(graphics, center, radius - 25, TechTheme.Violet, -_phase * 0.72F, 76, 2F);
        DrawGlowArc(graphics, center, radius - 36, color, _phase * 1.35F + 180, 54, 2F);

        using (var ring = new Pen(Color.FromArgb(65, color), 1F))
        {
            graphics.DrawEllipse(ring, center.X - radius + 2, center.Y - radius + 2, (radius - 2) * 2, (radius - 2) * 2);
            graphics.DrawEllipse(ring, center.X - radius + 31, center.Y - radius + 31, (radius - 31) * 2, (radius - 31) * 2);
        }

        var pulse = 0.5F + MathF.Sin(_phase * MathF.PI / 90F) * 0.5F;
        var coreRadius = Math.Max(20F, radius * 0.34F + pulse * 3F);
        using (var corePath = new GraphicsPath())
        {
            corePath.AddEllipse(center.X - coreRadius, center.Y - coreRadius, coreRadius * 2, coreRadius * 2);
            using var glow = new PathGradientBrush(corePath)
            {
                CenterColor = Color.FromArgb(_mode == CoreMode.Paused ? 40 : 155, color),
                SurroundColors = [Color.FromArgb(3, color)]
            };
            graphics.FillPath(glow, corePath);
        }

        using (var coreBorder = new Pen(Color.FromArgb(215, color), 1.8F))
        {
            graphics.DrawEllipse(coreBorder, center.X - coreRadius, center.Y - coreRadius, coreRadius * 2, coreRadius * 2);
        }

        using var font = new Font("Segoe UI Light", Math.Max(19F, coreRadius * 0.7F), FontStyle.Regular, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(TechTheme.Text);
        var size = graphics.MeasureString("J", font);
        graphics.DrawString("J", font, textBrush, center.X - size.Width / 2F, center.Y - size.Height / 2F - 1);
    }

    private static void DrawGlowArc(
        Graphics graphics,
        PointF center,
        float radius,
        Color color,
        float start,
        float sweep,
        float width)
    {
        var bounds = new RectangleF(center.X - radius, center.Y - radius, radius * 2, radius * 2);
        using var glow = new Pen(Color.FromArgb(35, color), width + 7) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var line = new Pen(Color.FromArgb(225, color), width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawArc(glow, bounds, start, sweep);
        graphics.DrawArc(line, bounds, start, sweep);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}

public sealed class VoiceWaveControl : Control
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 45 };
    private float _phase;
    private bool _active;
    private Color _accent = TechTheme.Cyan;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Accent
    {
        get => _accent;
        set
        {
            _accent = value;
            Invalidate();
        }
    }

    public VoiceWaveControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        _timer.Tick += (_, _) =>
        {
            _phase += _active ? 0.32F : 0.08F;
            Invalidate();
        };
        _timer.Start();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        const int bars = 31;
        var gap = 3F;
        var barWidth = Math.Max(2F, (Width - (bars - 1) * gap) / bars);
        var center = Height / 2F;
        for (var index = 0; index < bars; index++)
        {
            var envelope = MathF.Sin(index / (bars - 1F) * MathF.PI);
            var wave = 0.35F + MathF.Abs(MathF.Sin(_phase + index * 0.46F)) * 0.65F;
            var amplitude = _active ? Math.Max(3F, (Height - 8) * envelope * wave) : 3F + envelope * 2F;
            var x = index * (barWidth + gap);
            var rectangle = new RectangleF(x, center - amplitude / 2F, barWidth, amplitude);
            using var brush = new LinearGradientBrush(
                rectangle,
                Color.FromArgb(_active ? 220 : 75, _accent),
                Color.FromArgb(_active ? 75 : 28, TechTheme.Violet),
                90F);
            graphics.FillRoundedRectangle(brush, rectangle, barWidth / 2F);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF bounds, float radius)
    {
        using var path = TechTheme.Rounded(bounds, radius);
        graphics.FillPath(brush, path);
    }
}

public sealed class TechToggle : CheckBox
{
    public TechToggle()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        AutoSize = false;
        Size = new Size(178, 32);
        Cursor = Cursors.Hand;
        ForeColor = TechTheme.Text;
        Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Parent?.BackColor ?? TechTheme.Background);
        TextRenderer.DrawText(
            graphics,
            Text.ToUpperInvariant(),
            Font,
            new Rectangle(0, 0, Width - 48, Height),
            Checked ? TechTheme.Cyan : TechTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var toggle = new RectangleF(Width - 44, 7, 42, 19);
        using var path = TechTheme.Rounded(toggle, 9.5F);
        using var background = new SolidBrush(Checked ? Color.FromArgb(90, TechTheme.Cyan) : Color.FromArgb(35, 52, 68));
        graphics.FillPath(background, path);
        using var border = new Pen(Checked ? TechTheme.Cyan : TechTheme.Border, 1F);
        graphics.DrawPath(border, path);
        var knobX = Checked ? toggle.Right - 15 : toggle.Left + 4;
        using var knob = new SolidBrush(Checked ? TechTheme.Cyan : TechTheme.Muted);
        graphics.FillEllipse(knob, knobX, toggle.Top + 4, 11, 11);
    }
}

public sealed class NeonButton : Button
{
    private bool _hover;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Accent { get; set; } = TechTheme.Cyan;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Filled { get; set; } = true;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SurfaceColor { get; set; } = TechTheme.Panel;

    public NeonButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = TechTheme.Panel;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(SurfaceColor);
        var bounds = new RectangleF(1, 1, Width - 3, Height - 3);
        using var path = TechTheme.Rounded(bounds, 10);
        var first = Filled ? Color.FromArgb(_hover ? 255 : 225, Accent) : Color.FromArgb(_hover ? 42 : 20, Accent);
        var second = Filled ? Color.FromArgb(205, TechTheme.Violet) : Color.FromArgb(8, TechTheme.Panel);
        using var fill = new LinearGradientBrush(bounds, first, second, 8F);
        graphics.FillPath(fill, path);
        using var border = new Pen(Color.FromArgb(_hover ? 245 : 150, Accent), _hover ? 1.6F : 1F);
        graphics.DrawPath(border, path);
        TextRenderer.DrawText(
            graphics,
            Text.ToUpperInvariant(),
            Font,
            Rectangle.Round(bounds),
            Filled ? Color.FromArgb(4, 12, 22) : (_hover ? TechTheme.Text : TechTheme.CyanSoft),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

public sealed class TechStatusPill : Control
{
    private string _statusText = "INICIALIZANDO";
    private Color _accent = TechTheme.Cyan;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Accent
    {
        get => _accent;
        set { _accent = value; Invalidate(); }
    }

    public TechStatusPill()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(290, 34);
        Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold);
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(TechTheme.Background);
        var bounds = new RectangleF(1, 1, Width - 3, Height - 3);
        using var path = TechTheme.Rounded(bounds, 12);
        using var fill = new SolidBrush(Color.FromArgb(28, _accent));
        graphics.FillPath(fill, path);
        using var border = new Pen(Color.FromArgb(115, _accent), 1F);
        graphics.DrawPath(border, path);
        using var glow = new SolidBrush(Color.FromArgb(45, _accent));
        graphics.FillEllipse(glow, 11, Height / 2F - 6, 12, 12);
        using var dot = new SolidBrush(_accent);
        graphics.FillEllipse(dot, 14, Height / 2F - 3, 6, 6);
        TextRenderer.DrawText(
            graphics,
            _statusText.ToUpperInvariant(),
            Font,
            new Rectangle(31, 0, Width - 40, Height),
            TechTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

public sealed class TechConversation : Panel
{
    private readonly FlowLayoutPanel _flow = new();

    public TechConversation()
    {
        BackColor = TechTheme.Panel;
        _flow.Dock = DockStyle.Fill;
        _flow.AutoScroll = true;
        _flow.FlowDirection = FlowDirection.TopDown;
        _flow.WrapContents = false;
        _flow.BackColor = TechTheme.Panel;
        _flow.Padding = new Padding(14, 14, 8, 14);
        Controls.Add(_flow);
        _flow.ClientSizeChanged += (_, _) => RelayoutRows();
    }

    public void AddMessage(string author, string text, Color accent)
    {
        var isUser = author.StartsWith("TÚ", StringComparison.OrdinalIgnoreCase);
        var row = new ChatMessageRow(author, text, accent, isUser)
        {
            Width = Math.Max(260, _flow.ClientSize.Width - 28),
            Margin = new Padding(0, 0, 0, 8)
        };
        row.Relayout();
        _flow.Controls.Add(row);
        RelayoutRows();
        _flow.ScrollControlIntoView(row);
    }

    private void RelayoutRows()
    {
        var width = Math.Max(260, _flow.ClientSize.Width - 28);
        foreach (Control control in _flow.Controls)
        {
            if (control is ChatMessageRow row)
            {
                row.Width = width;
                row.Relayout();
            }
        }
    }
}

internal sealed class ChatMessageRow : Panel
{
    private readonly ChatBubble _bubble;
    private readonly bool _isUser;

    public ChatMessageRow(string author, string text, Color accent, bool isUser)
    {
        _isUser = isUser;
        BackColor = TechTheme.Panel;
        _bubble = new ChatBubble(author, text, accent, isUser);
        Controls.Add(_bubble);
    }

    public void Relayout()
    {
        var bubbleWidth = Math.Min(780, Math.Max(250, (int)(Width * (_isUser ? 0.76 : 0.88))));
        _bubble.SetBubbleWidth(bubbleWidth);
        _bubble.Left = _isUser ? Math.Max(0, Width - bubbleWidth - 5) : 4;
        _bubble.Top = 2;
        Height = _bubble.Height + 5;
    }
}

internal sealed class ChatBubble : Control
{
    private readonly string _author;
    private readonly string _text;
    private readonly Color _accent;
    private readonly bool _isUser;
    private int _textHeight;

    public ChatBubble(string author, string text, Color accent, bool isUser)
    {
        _author = author;
        _text = text.Trim();
        _accent = accent;
        _isUser = isUser;
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Font = new Font("Segoe UI", 10.5F);
    }

    public void SetBubbleWidth(int width)
    {
        Width = width;
        using var graphics = CreateGraphics();
        _textHeight = TextRenderer.MeasureText(
            graphics,
            _text,
            Font,
            new Size(Math.Max(60, width - 38), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
        Height = Math.Max(80, _textHeight + 58);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        var graphics = eventArgs.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(1, 1, Width - 3, Height - 3);
        using var path = TechTheme.Rounded(bounds, 14);
        var top = _isUser ? Color.FromArgb(21, 43, 67) : Color.FromArgb(13, 27, 46);
        var bottom = _isUser ? Color.FromArgb(15, 29, 51) : Color.FromArgb(9, 19, 34);
        using var fill = new LinearGradientBrush(bounds, top, bottom, 90F);
        graphics.FillPath(fill, path);
        using var border = new Pen(Color.FromArgb(_isUser ? 95 : 70, _accent), 1F);
        graphics.DrawPath(border, path);
        using var edge = new Pen(Color.FromArgb(190, _accent), 2F);
        graphics.DrawLine(edge, _isUser ? Width - 2.5F : 2.5F, 17, _isUser ? Width - 2.5F : 2.5F, Height - 17);

        using var headerFont = new Font("Segoe UI Semibold", 8.2F, FontStyle.Bold);
        TextRenderer.DrawText(
            graphics,
            _author.ToUpperInvariant(),
            headerFont,
            new Rectangle(19, 12, Width - 38, 20),
            _accent,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(
            graphics,
            _text,
            Font,
            new Rectangle(19, 38, Width - 38, _textHeight + 4),
            TechTheme.Text,
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
    }
}
