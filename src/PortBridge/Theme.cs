using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace PortBridge;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(244, 247, 251);
    public static readonly Color Ink = Color.FromArgb(27, 38, 59);
    public static readonly Color Muted = Color.FromArgb(115, 128, 150);
    public static readonly Color Line = Color.FromArgb(226, 232, 241);
    public static readonly Color Accent = Color.FromArgb(63, 99, 236);
    public static readonly Color Sidebar = Color.FromArgb(22, 32, 54);
    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath(); var d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    public static ModernButton Button(string text, Action? action = null, bool primary = false)
    {
        var button = new ModernButton { Text = text, Primary = primary, Size = new Size(108, 38), Margin = new Padding(0, 0, 8, 0) };
        if (action != null) button.Click += (_, _) => action();
        return button;
    }

    public static Label Label(string text, float size = 9, Color? color = null, bool bold = false) => new()
    {
        Text = text, AutoSize = true, ForeColor = color ?? Ink,
        Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        Margin = Padding.Empty, BackColor = Color.Transparent
    };

    public static Control Input(Control control)
    {
        var wrapper = new RoundedPanel { Radius = 8, Fill = Color.FromArgb(249, 251, 254), Stroke = Line, Dock = DockStyle.Fill, Padding = new Padding(11, 8, 11, 5), Margin = Padding.Empty };
        control.Margin = Padding.Empty; control.Dock = DockStyle.Fill; control.BackColor = wrapper.Fill; control.ForeColor = Ink;
        if (control is TextBox t) t.BorderStyle = BorderStyle.None;
        if (control is NumericUpDown n) n.BorderStyle = BorderStyle.None;
        if (control is ComboBox combo) { combo.FlatStyle = FlatStyle.Flat; combo.IntegralHeight = false; combo.DropDownHeight = 240; }
        control.GotFocus += (_, _) => { wrapper.Stroke = Accent; wrapper.Invalidate(); };
        control.LostFocus += (_, _) => { wrapper.Stroke = Line; wrapper.Invalidate(); };
        wrapper.Controls.Add(control); return wrapper;
    }

    public static Control Field(string title, Control input)
    {
        var group = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = new Padding(0, 0, 14, 0), BackColor = Color.White };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        group.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); group.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        group.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        group.Controls.Add(Label(title, 8.5f, Muted), 0, 0); group.Controls.Add(Input(input), 0, 1);
        return group;
    }
}

internal class RoundedPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int Radius { get; set; } = 14;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Color Fill { get; set; } = Color.White;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Color Stroke { get; set; } = Theme.Line;
    public RoundedPanel() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var parent = Parent;
        while (parent != null && parent.BackColor.A != 255) parent = parent.Parent;
        e.Graphics.Clear(parent?.BackColor ?? Theme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (Width < Radius * 2 || Height < Radius * 2) return;
        using var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), Radius);
        using var brush = new SolidBrush(Fill); e.Graphics.FillPath(brush, path);
        using var pen = new Pen(Stroke); e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class ModernButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Primary { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Dark { get; set; }
    private bool hover, pressed;
    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand; DoubleBuffered = true; TabStop = true;
        Font = new Font("Microsoft YaHei UI", 9);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var fill = Primary ? Theme.Accent : Dark ? Color.FromArgb(35, 48, 74) : Color.White;
        if (!Enabled) fill = Dark ? Color.FromArgb(29, 41, 64) : Color.FromArgb(237, 241, 247);
        else if (pressed) fill = Primary ? Color.FromArgb(44, 72, 195) : Dark ? Color.FromArgb(51, 66, 96) : Color.FromArgb(230, 235, 247);
        else if (hover) fill = Primary ? Color.FromArgb(80, 115, 248) : Dark ? Color.FromArgb(43, 58, 86) : Color.FromArgb(244, 247, 255);
        using var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 8);
        using var brush = new SolidBrush(fill); e.Graphics.FillPath(brush, path);
        if (!Primary && !Dark) { using var border = new Pen(Theme.Line); e.Graphics.DrawPath(border, path); }
        var text = Enabled ? Primary || Dark ? Color.White : Theme.Ink : Color.FromArgb(151, 163, 182);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) { var r = ClientRectangle; r.Inflate(-5, -5); ControlPaint.DrawFocusRectangle(e.Graphics, r, text, fill); }
    }
}

internal sealed class BrandMark : Control
{
    public BrandMark() { DoubleBuffered = true; Size = new Size(42, 42); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), 11);
        using var brush = new SolidBrush(Theme.Accent); e.Graphics.FillPath(brush, path);
        using var pen = new Pen(Color.White, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var sx = Width / 42f; var sy = Height / 42f;
        e.Graphics.ScaleTransform(sx, sy);
        e.Graphics.DrawLines(pen, new[] { new Point(10, 17), new Point(28, 17), new Point(24, 13) });
        e.Graphics.DrawLines(pen, new[] { new Point(32, 25), new Point(14, 25), new Point(18, 29) });
    }
}
