namespace PortBridge;

public sealed partial class MainForm
{
    private void BuildUi()
    {
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(shell);
        var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar, Margin = Padding.Empty, Padding = new Padding(22, 26, 22, 22) };
        shell.Controls.Add(sidebar, 0, 0);
        var side = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10, BackColor = Theme.Sidebar, Margin = Padding.Empty };
        side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 80, 30, 46, 52, 52, 52, 62 }) side.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        side.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        sidebar.Controls.Add(side);
        var brand = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar, Margin = Padding.Empty };
        brand.Controls.Add(new BrandMark { Location = new Point(0, 1) });
        var brandText = Theme.Label("PortBridge", 12, Color.White, true); brandText.Location = new Point(52, 3); brand.Controls.Add(brandText);
        var brandCaption = Theme.Label("让服务，近在本机", 8, Color.FromArgb(149, 164, 192)); brandCaption.Location = new Point(53, 30); brand.Controls.Add(brandCaption);
        side.Controls.Add(brand, 0, 0);
        side.Controls.Add(Theme.Label("服务器配置", 8, Color.FromArgb(135, 151, 179)), 0, 1);
        var profileBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty, BackColor = Theme.Sidebar };
        profileBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        side.Controls.Add(profileBar, 0, 2); side.SetRowSpan(profileBar, 5);
        foreach (var height in new[] { 48, 52, 52, 52, 62 }) profileBar.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        profiles.Dock = DockStyle.Fill; profiles.Width = 180; profiles.DropDownWidth = 230;
        profileBar.Controls.Add(Theme.Input(profiles), 0, 0);
        var newButton = SideButton("＋  新建服务器", () =>
        {
            if (!SaveCurrent()) return;
            var p = new ServerProfile(); config.Profiles.Add(p); RefreshProfiles(p.Id); Persist();
        });
        profileBar.Controls.Add(newButton, 0, 1);
        profileBar.Controls.Add(SideButton("保存当前配置", () =>
        {
            if (SaveCurrent()) { RefreshProfileNames(); Persist(); WriteLog("配置已保存。密码和私钥口令不写入配置。"); }
        }), 0, 2);
        var shareButtons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 8, 0, 0), BackColor = Theme.Sidebar };
        shareButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); shareButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var import = SideButton("导入", ImportProfile); import.Margin = new Padding(0, 0, 5, 0);
        var export = SideButton("导出", ExportProfile); export.Margin = new Padding(5, 0, 0, 0);
        shareButtons.Controls.Add(import, 0, 0); shareButtons.Controls.Add(export, 1, 0);
        profileBar.Controls.Add(shareButtons, 0, 3);
        profileBar.Controls.Add(SideButton("删除当前配置", () =>
        {
            if (Current == null || MessageBox.Show(this, "删除当前配置？", "PortBridge", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            config.Profiles.Remove(Current);
            if (config.Profiles.Count == 0) config.Profiles.Add(new ServerProfile());
            RefreshProfiles(config.Profiles[0].Id); Persist();
        }), 0, 4);
        editControls.Add(profileBar);
        var note = new Label
        {
            Text = "安心留在后台\n关闭窗口后，转发会继续\n在系统托盘中运行。", Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(149, 164, 192), Font = new Font(Font.FontFamily, 8.5f), Padding = new Padding(0, 6, 0, 0)
        };
        side.Controls.Add(note, 0, 8);
        var trayButton = SideButton("收起到托盘  ↘", Hide); trayButton.Margin = Padding.Empty;
        side.Controls.Add(trayButton, 0, 9);

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26, 23, 26, 14), RowCount = 7, ColumnCount = 1, Margin = Padding.Empty };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 83, 248, 16 }) main.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        shell.Controls.Add(main, 1, 0);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 370));
        var heading = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var title = Theme.Label("服务转发", 22, Theme.Ink, true); title.Location = new Point(0, 0); heading.Controls.Add(title);
        var caption = Theme.Label("连接远端服务，在熟悉的本机环境中使用。", 8.5f, Theme.Muted); caption.Location = new Point(2, 44); heading.Controls.Add(caption);
        header.Controls.Add(heading, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 8, 0, 0), Margin = Padding.Empty };
        connect.Margin = new Padding(8, 0, 0, 0); disconnect.Margin = new Padding(8, 0, 0, 0);
        connect.Click += async (_, _) => await Connect(); disconnect.Click += (_, _) => Disconnect();
        actions.Controls.Add(connect); actions.Controls.Add(disconnect);
        var pill = new RoundedPanel { Radius = 10, Fill = Color.FromArgb(231, 237, 247), Stroke = Color.FromArgb(231, 237, 247), Size = new Size(138, 40), Margin = Padding.Empty, Padding = new Padding(5) };
        status.AutoSize = false; status.AutoEllipsis = true; status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleCenter; status.Padding = Padding.Empty; status.BackColor = pill.Fill; status.Font = new Font(Font.FontFamily, 8.5f);
        pill.Controls.Add(status); actions.Controls.Add(pill); header.Controls.Add(actions, 1, 0); main.Controls.Add(header, 0, 0);

        var connectionCard = Card();
        var connection = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.White, Margin = Padding.Empty };
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 53)); connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 76)); connection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        connection.Controls.Add(SectionTitle("连接设置", "填写 SSH 服务器与认证信息"), 0, 0);
        var row1 = Columns(27, 34, 13, 26);
        row1.Controls.Add(Theme.Field("配置名称", name), 0, 0); row1.Controls.Add(Theme.Field("SSH 主机", host), 1, 0);
        row1.Controls.Add(Theme.Field("端口", port), 2, 0); row1.Controls.Add(Theme.Field("用户名", user), 3, 0);
        connection.Controls.Add(row1, 0, 1);
        var row2 = Columns(27, 27, 46);
        auth.Items.AddRange(new object[] { "密码", "私钥文件" }); auth.SelectedIndex = 0;
        row2.Controls.Add(Theme.Field("认证方式", auth), 0, 0);
        var secretGroup = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = new Padding(0, 0, 14, 0) };
        secretGroup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        secretGroup.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); secretGroup.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        secretGroup.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        secretLabel.Font = new Font(Font.FontFamily, 8.5f); secretLabel.ForeColor = Theme.Muted; secretLabel.Padding = Padding.Empty; secretLabel.Margin = Padding.Empty;
        secretGroup.Controls.Add(secretLabel, 0, 0); secretGroup.Controls.Add(Theme.Input(secret), 0, 1); row2.Controls.Add(secretGroup, 1, 0);
        var keyRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 67));
        var keyInput = Theme.Field("私钥文件", keyPath); keyInput.Margin = new Padding(0, 0, 8, 0); keyRow.Controls.Add(keyInput, 0, 0);
        var browse = Theme.Button("浏览…", () => { using var d = new OpenFileDialog { Title = "选择 SSH 私钥文件" }; if (d.ShowDialog(this) == DialogResult.OK) keyPath.Text = d.FileName; });
        browse.Dock = DockStyle.Top; browse.Height = 39; browse.Margin = new Padding(0, 25, 0, 0); keyRow.Controls.Add(browse, 1, 0);
        keyRow.Enabled = false; row2.Controls.Add(keyRow, 2, 0); connection.Controls.Add(row2, 0, 2);
        auth.SelectedIndexChanged += (_, _) => { secretLabel.Text = auth.SelectedIndex == 1 ? "私钥口令（可留空）" : "密码（仅本次使用）"; keyRow.Enabled = auth.SelectedIndex == 1; secret.Clear(); };
        secretLabel.Text = "密码（仅本次使用）";
        connectionCard.Controls.Add(connection); main.Controls.Add(connectionCard, 0, 1); editControls.Add(connectionCard);

        var rulesCard = Card(); rulesCard.Padding = new Padding(18, 16, 18, 14);
        var rules = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.White, Margin = Padding.Empty };
        rules.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rules.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); rules.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); rules.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        var rulesHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        rulesHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); rulesHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        rulesHeader.Controls.Add(SectionTitle("转发规则", "将远端端口映射到本机"), 0, 0);
        var ruleActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        var add = Theme.Button("＋ 添加规则", () => { grid.EndEdit(); Current?.Rules.Add(new PortRule()); });
        var remove = Theme.Button("删除", () => { if (grid.CurrentRow?.DataBoundItem is PortRule r) Current?.Rules.Remove(r); }); remove.Width = 66;
        var open = Theme.Button("打开服务 ↗", OpenService);
        ruleActions.Controls.Add(open); ruleActions.Controls.Add(add); ruleActions.Controls.Add(remove);
        editControls.Add(add); editControls.Add(remove);
        rulesHeader.Controls.Add(ruleActions, 1, 0); rules.Controls.Add(rulesHeader, 0, 0);
        ConfigureGrid(); rules.Controls.Add(grid, 0, 1);
        var localNote = Theme.Label("仅本机可访问  ·  目标地址从 SSH 服务器侧连接", 8, Theme.Muted); localNote.Padding = new Padding(0, 7, 0, 0);
        rules.Controls.Add(localNote, 0, 2); rulesCard.Controls.Add(rules); main.Controls.Add(rulesCard, 0, 3);

        var logCard = Card(); logCard.Padding = new Padding(18, 12, 18, 12);
        var logs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.White, Margin = Padding.Empty };
        logs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        logs.RowStyles.Add(new RowStyle(SizeType.Absolute, 29)); logs.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        logs.Controls.Add(Theme.Label("连接动态", 9, Theme.Ink, true), 0, 0);
        log.BorderStyle = BorderStyle.None; log.BackColor = Color.White; log.ForeColor = Theme.Muted; log.Font = new Font("Microsoft YaHei UI", 8.5f);
        logs.Controls.Add(log, 0, 1); logCard.Controls.Add(logs); main.Controls.Add(logCard, 0, 5);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        var footnote = Theme.Label("SSH 加密连接   /   密码与口令不写入配置", 8, Theme.Muted); footnote.Padding = new Padding(0, 10, 0, 0); footer.Controls.Add(footnote, 0, 0);
        var exit = Theme.Button("退出程序", ExitApp); exit.Height = 28; exit.Width = 90; exit.Margin = new Padding(0, 4, 0, 0); footer.Controls.Add(exit, 1, 0); main.Controls.Add(footer, 0, 6);
        profiles.SelectedIndexChanged += (_, _) => { if (!loading) { SaveCurrent(); LoadProfile(); } };
    }

    private static ModernButton SideButton(string text, Action action)
    {
        var b = Theme.Button(text, action); b.Dark = true; b.Dock = DockStyle.Top; b.Height = 40; b.Margin = new Padding(0, 10, 0, 0); return b;
    }
    private static RoundedPanel Card() => new() { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(20, 16, 6, 16), BackColor = Color.White };
    private static TableLayoutPanel Columns(params float[] widths)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = widths.Length, RowCount = 1, Margin = Padding.Empty, BackColor = Color.White };
        foreach (var width in widths) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        return panel;
    }
    private static Panel SectionTitle(string title, string subtitle)
    {
        var p = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.White };
        var t = Theme.Label(title, 11, Theme.Ink, true); t.Location = new Point(0, 0); p.Controls.Add(t);
        var s = Theme.Label(subtitle, 8, Theme.Muted); s.Location = new Point(0, 26); p.Controls.Add(s); return p;
    }
    private void ConfigureGrid()
    {
        grid.EnableHeadersVisualStyles = false; grid.GridColor = Theme.Line;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(246, 248, 252), ForeColor = Theme.Muted, Font = new Font(Font.FontFamily, 8.5f),
            SelectionBackColor = Color.FromArgb(246, 248, 252), SelectionForeColor = Theme.Muted, Padding = new Padding(8, 0, 0, 0)
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White, ForeColor = Theme.Ink, SelectionBackColor = Color.FromArgb(237, 242, 255),
            SelectionForeColor = Theme.Ink, Font = new Font(Font.FontFamily, 9), Padding = new Padding(8, 0, 0, 0)
        };
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(251, 252, 255);
        var enabled = new DataGridViewCheckBoxColumn { DataPropertyName = "Enabled", HeaderText = "启用", FillWeight = 44, MinimumWidth = 52 };
        enabled.HeaderCell.Style.Padding = Padding.Empty; enabled.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
        grid.Columns.Add(enabled);
        foreach (var spec in new[] { ("Name", "服务名称", 96), ("LocalPort", "本地端口", 76), ("TargetHost", "目标主机", 112), ("TargetPort", "目标端口", 76) })
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = spec.Item1, HeaderText = spec.Item2, FillWeight = spec.Item3, MinimumWidth = 75 });
        var protocol = new DataGridViewComboBoxColumn { DataPropertyName = "Protocol", HeaderText = "打开方式", FillWeight = 80, MinimumWidth = 80, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing };
        protocol.Items.AddRange("http", "https", "tcp"); grid.Columns.Add(protocol);
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Path", HeaderText = "网页路径", FillWeight = 170, MinimumWidth = 100 });
        foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        grid.RowTemplate.Height = 43; grid.ColumnHeadersHeight = 40; grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.AllowUserToResizeRows = false;
        grid.DataError += (_, e) => { e.ThrowException = false; WriteLog("输入无效：端口请使用整数，打开方式请选择 http / https / tcp。"); };
    }
}
