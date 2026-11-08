using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PortBridge;

internal sealed class ServerListView : UserControl
{
    private readonly FlowLayoutPanel serverList = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        AutoScroll = true, BackColor = Theme.Sidebar, Margin = Padding.Empty, Padding = Padding.Empty
    };
    private readonly ModernButton addButton = Theme.Button("＋  添加服务器");
    private readonly ModernButton importButton = Theme.Button("导入配置");
    private readonly ModernButton settingsButton = Theme.Button("应用设置");
    private readonly ModernButton trayButton = Theme.Button("收起到托盘");
    private readonly ModernButton exitButton = Theme.Button("退出程序");
    private bool interactionEnabled = true;

    public event Action? AddRequested;
    public event Action? ImportRequested;
    public event Action? SettingsRequested;
    public event Action? TrayRequested;
    public event Action? ExitRequested;
    public event Action<string>? ServerSelected;

    public ServerListView()
    {
        Dock = DockStyle.Fill; BackColor = Theme.Sidebar; Padding = new Padding(16, 18, 16, 14);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Margin = Padding.Empty, BackColor = Theme.Sidebar };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        Controls.Add(layout);

        var brand = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar, Margin = Padding.Empty };
        brand.Controls.Add(new BrandMark { Location = new Point(0, 1), Size = new Size(38, 38) });
        var product = Theme.Label("PortBridge", 12, Theme.Ink, true); product.Location = new Point(48, 1); brand.Controls.Add(product);
        var version = Theme.Label("0.3.0", 8, Theme.Muted); version.Location = new Point(49, 27); brand.Controls.Add(version);
        layout.Controls.Add(brand, 0, 0);

        StyleButton(addButton, alignLeft: true);
        addButton.Click += (_, _) => AddRequested?.Invoke();
        layout.Controls.Add(addButton, 0, 1);
        var section = Theme.Label("服务器", 8.5f, Theme.Muted);
        section.Dock = DockStyle.Fill; section.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(section, 0, 2);
        layout.Controls.Add(serverList, 0, 3);

        StyleButton(importButton, alignLeft: true); importButton.Click += (_, _) => ImportRequested?.Invoke();
        layout.Controls.Add(importButton, 0, 4);
        StyleButton(settingsButton, alignLeft: true); settingsButton.Click += (_, _) => SettingsRequested?.Invoke();
        layout.Controls.Add(settingsButton, 0, 5);
        var bottomActions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Sidebar };
        bottomActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        bottomActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        StyleButton(trayButton, alignLeft: false); trayButton.Margin = new Padding(0, 2, 2, 2);
        StyleButton(exitButton, alignLeft: false); exitButton.Margin = new Padding(2, 2, 0, 2);
        exitButton.Text = "退出"; exitButton.AccessibleName = "退出程序";
        trayButton.Click += (_, _) => TrayRequested?.Invoke();
        exitButton.Click += (_, _) => ExitRequested?.Invoke();
        bottomActions.Controls.Add(trayButton, 0, 0); bottomActions.Controls.Add(exitButton, 1, 0);
        layout.Controls.Add(bottomActions, 0, 6);
        var versionFooter = Theme.Label("", 8, Theme.Muted); versionFooter.Dock = DockStyle.Fill;
        layout.Controls.Add(versionFooter, 0, 7);
        serverList.ClientSizeChanged += (_, _) => ResizeServerItems();
    }

    public void SetServers(IReadOnlyList<ServerProfile> profiles, string selectedId)
    {
        serverList.SuspendLayout();
        foreach (Control old in serverList.Controls.Cast<Control>().ToArray())
        {
            serverList.Controls.Remove(old);
            old.Dispose();
        }

        if (profiles.Count == 0)
        {
            var empty = Theme.Label("添加服务器后\n会显示在这里", 8.5f, Theme.Muted);
            empty.Margin = new Padding(10, 14, 0, 0); serverList.Controls.Add(empty);
        }
        else
        {
            foreach (var profile in profiles)
            {
                var details = string.IsNullOrWhiteSpace(profile.Host)
                    ? "尚未配置 SSH 地址"
                    : $"{profile.User}@{profile.Host}:{profile.Port}";
                var button = new ServerRowButton(profile.Name, details)
                {
                    Selected = profile.Id == selectedId,
                    Enabled = interactionEnabled,
                    Width = Math.Max(100, serverList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 1),
                    Height = 62,
                    Margin = new Padding(0, 0, 0, 5),
                    Tag = profile.Id,
                    AccessibleName = profile.Name,
                    AccessibleDescription = details
                };
                button.Click += (_, _) => ServerSelected?.Invoke(profile.Id);
                serverList.Controls.Add(button);
            }
        }
        serverList.ResumeLayout(true);
    }

    public void SetInteractionEnabled(bool enabled)
    {
        interactionEnabled = enabled;
        addButton.Enabled = importButton.Enabled = enabled;
        foreach (Control child in serverList.Controls)
            if (child is ServerRowButton item) item.Enabled = enabled;
    }

    public void SetUpdateNotice(bool available)
    {
        settingsButton.Text = available ? "应用设置  ·  有新版" : "应用设置";
        settingsButton.Invalidate();
    }

    public void SelectServer(string selectedId)
    {
        foreach (Control child in serverList.Controls)
        {
            if (child is not ServerRowButton item) continue;
            var selected = string.Equals(item.Tag as string, selectedId, StringComparison.Ordinal);
            if (item.Selected == selected) continue;
            item.Selected = selected;
            item.Invalidate();
        }
    }

    private void ResizeServerItems()
    {
        var width = Math.Max(100, serverList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 1);
        foreach (Control child in serverList.Controls) child.Width = width;
    }

    private static void StyleButton(ModernButton button, bool alignLeft)
    {
        button.Dark = true; button.Dock = DockStyle.Fill; button.Margin = new Padding(0, 2, 0, 2);
        button.TextAlign = alignLeft ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleCenter;
        if (alignLeft) button.Padding = new Padding(10, 0, 0, 0);
    }

    private sealed class ServerRowButton : Button
    {
        private readonly string detail;
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Selected { get; set; }

        public ServerRowButton(string name, string detail)
        {
            Text = name; this.detail = detail;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);
            ForeColor = Theme.Ink; BackColor = Theme.Sidebar;
            Cursor = Cursors.Hand; DoubleBuffered = true; TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e) { Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Sidebar);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var fill = Selected ? Theme.Selected : ClientRectangle.Contains(PointToClient(Cursor.Position)) ? Theme.Surface : Theme.Sidebar;
            using var path = Theme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 9);
            using var brush = new SolidBrush(Enabled ? fill : Theme.Sidebar);
            e.Graphics.FillPath(brush, path);

            var textColor = Enabled ? Theme.Ink : Theme.Muted;
            var nameBounds = new Rectangle(12, 8, Width - 24, 22);
            TextRenderer.DrawText(e.Graphics, Text, Font, nameBounds, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            var detailBounds = new Rectangle(12, 33, Width - 24, 18);
            TextRenderer.DrawText(e.Graphics, detail, Font, detailBounds, Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
            {
                var focus = ClientRectangle; focus.Inflate(-4, -4);
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, Theme.Ink, fill);
            }
        }
    }
}
