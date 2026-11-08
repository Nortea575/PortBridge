namespace PortBridge;

internal sealed class SettingsView : UserControl
{
    public event Action? BackRequested;

    public SettingsView(ToggleSwitch autoStart, ToggleSwitch autoUpdate, Label startupMessage,
        Label updateStatus, ModernButton checkUpdates, ModernButton openUpdate, string version)
    {
        Dock = DockStyle.Fill; AutoScroll = true; BackColor = Theme.Background; Padding = new Padding(30, 24, 30, 18);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 1, RowCount = 7, Height = 516,
            Margin = Padding.Empty, BackColor = Theme.Background
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 148));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Background };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = Theme.Label("应用设置", 18, Theme.Ink, true);
        title.AutoSize = false; title.Dock = DockStyle.Fill; title.TextAlign = ContentAlignment.MiddleLeft;
        heading.Controls.Add(title, 0, 0);
        var back = Theme.Button("返回服务列表");
        back.Dock = DockStyle.Fill; back.Margin = new Padding(0, 5, 0, 5);
        back.Click += (_, _) => BackRequested?.Invoke();
        heading.Controls.Add(back, 1, 0); layout.Controls.Add(heading, 0, 0);

        layout.Controls.Add(SettingRow("登录 Windows 时启动", "登录后启动到系统托盘，不会自动连接 SSH。", autoStart, startupMessage), 0, 1);
        layout.Controls.Add(Separator(), 0, 2);
        layout.Controls.Add(SettingRow("启动时检查更新", "只检查 GitHub 最新稳定版，不会自动下载或安装。", autoUpdate), 0, 3);
        layout.Controls.Add(Separator(), 0, 4);

        var updates = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty, BackColor = Theme.Background };
        updates.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        updates.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 236));
        updates.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        updates.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        updates.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var versionLabel = Theme.Label("当前版本  " + version, 9.5f, Theme.Ink, true);
        versionLabel.Dock = DockStyle.Fill; versionLabel.TextAlign = ContentAlignment.MiddleLeft;
        updates.Controls.Add(versionLabel, 0, 0); updates.SetColumnSpan(versionLabel, 2);
        updateStatus.Dock = DockStyle.Fill; updateStatus.TextAlign = ContentAlignment.MiddleLeft;
        updates.Controls.Add(updateStatus, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Background };
        checkUpdates.Size = new Size(108, 36); checkUpdates.Margin = new Padding(0, 2, 0, 0);
        openUpdate.Size = new Size(108, 36); openUpdate.Margin = new Padding(8, 2, 0, 0);
        actions.Controls.Add(checkUpdates); actions.Controls.Add(openUpdate);
        updates.Controls.Add(actions, 1, 1);
        layout.Controls.Add(updates, 0, 5);
    }

    private static TableLayoutPanel SettingRow(string title, string description, ToggleSwitch toggle, Label? statusLabel = null)
    {
        toggle.AccessibleName = title; toggle.AccessibleDescription = description;
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Background };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var copy = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = statusLabel == null ? 2 : 3, Margin = Padding.Empty, BackColor = Theme.Background };
        copy.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        if (statusLabel != null) copy.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = Theme.Label(title, 10, Theme.Ink, true); heading.Dock = DockStyle.Fill; heading.TextAlign = ContentAlignment.BottomLeft;
        var body = Theme.Label(description, 8.5f, Theme.Muted); body.Dock = DockStyle.Fill; body.TextAlign = ContentAlignment.MiddleLeft;
        copy.Controls.Add(heading, 0, 0); copy.Controls.Add(body, 0, 1);
        if (statusLabel != null)
        {
            statusLabel.Dock = DockStyle.Fill; statusLabel.ForeColor = Theme.Muted;
            statusLabel.Font = new Font("Microsoft YaHei UI", 8); statusLabel.Margin = Padding.Empty;
            copy.Controls.Add(statusLabel, 0, 2);
        }
        row.Controls.Add(copy, 0, 0);
        toggle.Anchor = AnchorStyles.Right; toggle.Margin = new Padding(0, 0, 8, 0);
        row.Controls.Add(toggle, 1, 0);
        return row;
    }

    private static Panel Separator() => new() { Dock = DockStyle.Fill, Height = 1, BackColor = Theme.Line, Margin = Padding.Empty };
}
