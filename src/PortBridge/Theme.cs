using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PortBridge;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(15, 16, 17);
    public static readonly Color Sidebar = Color.FromArgb(18, 20, 21);
    public static readonly Color Surface = Color.FromArgb(22, 24, 25);
    public static readonly Color InputSurface = Color.FromArgb(28, 30, 32);
    public static readonly Color Line = Color.FromArgb(48, 51, 54);
    public static readonly Color Ink = Color.White;
    public static readonly Color Muted = Color.FromArgb(156, 163, 175);
    public static readonly Color Accent = Color.FromArgb(217, 251, 6);
    public static readonly Color Selected = Color.FromArgb(43, 46, 48);
    public static readonly Color Error = Color.FromArgb(255, 112, 107);
    public static readonly ToolStripProfessionalRenderer MenuRenderer = new(new DarkColorTable());

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string applicationName, string? subIdList);

    public static void ApplyDarkNativeStyle(Control control)
    {
        void Apply()
        {
            try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        control.HandleCreated += (_, _) => Apply();
        if (control.IsHandleCreated) Apply();
    }

    public static GraphicsPath Round(RectangleF rectangle, float radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static ModernButton Button(string text, Action? action = null, bool primary = false)
    {
        var button = new ModernButton
        {
            Text = text, Primary = primary, Size = new Size(108, 38),
            Margin = new Padding(0, 0, 8, 0)
        };
        if (action != null) button.Click += (_, _) => action();
        return button;
    }

    public static Label Label(string text, float size = 9, Color? color = null, bool bold = false) => new()
    {
        Text = text, AutoSize = true, ForeColor = color ?? Ink,
        Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        Margin = Padding.Empty, BackColor = Color.Transparent, UseMnemonic = false
    };

    public static Control Input(Control control)
    {
        var wrapper = new RoundedPanel
        {
            Radius = 8, Fill = InputSurface, Stroke = Line, Dock = DockStyle.Fill,
            Padding = new Padding(11, 7, 11, 4), Margin = Padding.Empty
        };
        control.Margin = Padding.Empty; control.Dock = DockStyle.Fill;
        control.BackColor = InputSurface; control.ForeColor = Ink;
        if (control is TextBox text) text.BorderStyle = BorderStyle.None;
        if (control is NumericUpDown number) number.BorderStyle = BorderStyle.None;
        if (control is ComboBox combo)
        {
            combo.FlatStyle = FlatStyle.Flat; combo.IntegralHeight = false;
            combo.DropDownHeight = 240; combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = 24;
            ApplyDarkNativeStyle(combo);
            combo.DrawItem += (_, e) =>
            {
                var selected = e.State.HasFlag(DrawItemState.Selected);
                using (var background = new SolidBrush(selected ? Selected : InputSurface))
                    e.Graphics.FillRectangle(background, e.Bounds);
                var text = e.Index >= 0 ? combo.GetItemText(combo.Items[e.Index]) : combo.Text;
                var foreColor = selected ? Ink : Muted;
                TextRenderer.DrawText(e.Graphics, text, e.Font ?? combo.Font, e.Bounds, foreColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
        }
        control.GotFocus += (_, _) => { wrapper.Stroke = Accent; wrapper.Invalidate(); };
        control.LostFocus += (_, _) => { wrapper.Stroke = Line; wrapper.Invalidate(); };
        wrapper.Controls.Add(control);
        return wrapper;
    }

    public static Control Field(string title, Control input)
    {
        var group = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Margin = new Padding(0, 0, 14, 0), BackColor = Surface
        };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        group.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        group.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        group.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        group.Controls.Add(Label(title, 8.5f, Muted), 0, 0);
        group.Controls.Add(Input(input), 0, 1);
        return group;
    }
}

internal sealed class DarkColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Theme.Surface;
    public override Color MenuItemSelected => Theme.Selected;
    public override Color MenuItemSelectedGradientBegin => Theme.Selected;
    public override Color MenuItemSelectedGradientEnd => Theme.Selected;
    public override Color MenuItemPressedGradientBegin => Theme.InputSurface;
    public override Color MenuItemPressedGradientEnd => Theme.InputSurface;
    public override Color ImageMarginGradientBegin => Theme.Surface;
    public override Color ImageMarginGradientMiddle => Theme.Surface;
    public override Color ImageMarginGradientEnd => Theme.Surface;
    public override Color SeparatorDark => Theme.Line;
    public override Color SeparatorLight => Theme.Surface;
    public override Color CheckBackground => Theme.Selected;
    public override Color CheckSelectedBackground => Theme.Selected;
    public override Color CheckPressedBackground => Theme.InputSurface;
}

internal class RoundedPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int Radius { get; set; } = 14;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Color Fill { get; set; } = Theme.Surface;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Color Stroke { get; set; } = Theme.Line;

    public RoundedPanel() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
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
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Selected { get; set; }
    private bool hover, pressed;

    public ModernButton()
    {
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand; DoubleBuffered = true; TabStop = true;
        Font = new Font("Microsoft YaHei UI", 9);
        ForeColor = Theme.Ink; BackColor = Theme.Background;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var fill = Primary ? Theme.Accent : Dark ? (Selected ? Theme.Selected : Theme.Sidebar) : Theme.InputSurface;
        if (!Enabled) fill = Primary ? Color.FromArgb(75, 82, 51) : Theme.Surface;
        else if (pressed) fill = Primary ? Color.FromArgb(196, 228, 0) : Theme.Selected;
        else if (hover) fill = Primary ? Color.FromArgb(231, 255, 68) : Theme.Selected;
        var radius = Primary ? Math.Min(Height / 2f, 22) : 8;
        using var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), radius);
        using var brush = new SolidBrush(fill); e.Graphics.FillPath(brush, path);
        if (!Primary && !Dark) { using var border = new Pen(Theme.Line); e.Graphics.DrawPath(border, path); }
        var textColor = !Enabled ? Color.FromArgb(104, 109, 112)
            : Primary ? Theme.Background : Dark && !Selected ? Theme.Muted : Theme.Ink;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        if (TextAlign == ContentAlignment.MiddleLeft) flags |= TextFormatFlags.Left;
        else flags |= TextFormatFlags.HorizontalCenter;
        var textBounds = ClientRectangle;
        textBounds.Inflate(-Math.Max(5, Padding.Left), 0);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, textColor, flags);
        if (Focused && ShowFocusCues) { var focus = ClientRectangle; focus.Inflate(-5, -5); ControlPaint.DrawFocusRectangle(e.Graphics, focus, textColor, fill); }
    }
}

internal sealed class ToggleSwitch : CheckBox
{
    public ToggleSwitch()
    {
        AutoSize = false; Size = new Size(50, 28); Text = ""; Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AccessibleRole = AccessibleRole.CheckButton;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? Theme.Background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(1, 3, Width - 3, Height - 7);
        using var path = Theme.Round(bounds, bounds.Height / 2);
        using var track = new SolidBrush(Checked ? Theme.Accent : Theme.Line);
        e.Graphics.FillPath(track, path);
        var diameter = Math.Max(12, Height - 15);
        var x = Checked ? Width - diameter - 7 : 4;
        var y = Math.Max(4, (Height - diameter) / 2);
        using var thumb = new SolidBrush(Checked ? Theme.Background : Theme.Muted);
        e.Graphics.FillEllipse(thumb, x, y, diameter, diameter);
        if (Focused && ShowFocusCues)
        {
            var focus = Rectangle.Inflate(ClientRectangle, -1, -1);
            ControlPaint.DrawFocusRectangle(e.Graphics, focus, Theme.Ink, Parent?.BackColor ?? Theme.Background);
        }
    }

    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
}

internal sealed class BrandMark : Control
{
    public BrandMark() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var basePath = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), Math.Max(7, Width * .25f));
        using var baseBrush = new SolidBrush(Theme.Surface); e.Graphics.FillPath(baseBrush, basePath);
        using var accent = new Pen(Theme.Accent, Math.Max(1.5f, Width * .065f)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var sx = Width / 38f; var sy = Height / 38f;
        e.Graphics.ScaleTransform(sx, sy);
        e.Graphics.DrawLines(accent, new[] { new Point(7, 12), new Point(29, 12), new Point(25, 8) });
        e.Graphics.DrawLines(accent, new[] { new Point(31, 26), new Point(9, 26), new Point(13, 30) });
    }
}
