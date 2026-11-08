using System.Drawing.Drawing2D;

namespace PortBridge;

internal sealed class WorkspaceView : UserControl
{
    private readonly Label title = Theme.Label("", 17, Theme.Ink, true);
    private readonly Label endpoint = Theme.Label("", 9, Theme.Muted);
    private readonly Label authentication = Theme.Label("", 8.5f, Theme.Muted);
    private readonly Label status = Theme.Label("● 未连接", 8.5f, Theme.Muted, true);
    private readonly TextBox credential = new() { UseSystemPasswordChar = true, PlaceholderText = "连接密码 / 私钥口令（仅本次使用）" };
    private readonly Label credentialHint = Theme.Label("密码", 8, Theme.Muted);
    private readonly ModernButton connectButton = Theme.Button("连接", primary: true);
    private readonly ModernButton disconnectButton = Theme.Button("断开");
    private readonly FlowLayoutPanel mappings = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        AutoScroll = true, BackColor = Theme.Background, Margin = Padding.Empty, Padding = Padding.Empty
    };
    private readonly Label latestLog = Theme.Label("尚无连接记录", 8.5f, Theme.Muted);
    private readonly TextBox fullLog = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Muted,
        Font = new Font("Microsoft YaHei UI", 8.5f), Dock = DockStyle.Fill, Visible = false
    };
    private readonly ModernButton logToggle = Theme.Button("展开记录");
    private readonly ToolTip tips = new() { ShowAlways = true, AutomaticDelay = 250 };
    private readonly Panel profileArea = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly Panel emptyArea = new() { Dock = DockStyle.Fill, BackColor = Theme.Background, Visible = false };
    private readonly Label emptyTitle = Theme.Label("还没有服务器", 18, Theme.Ink, true);
    private readonly Label emptyDescription = Theme.Label("添加服务器后，即可在这里连接并打开转发的服务。", 9.5f, Theme.Muted);
    private readonly ModernButton emptyAdd = Theme.Button("添加第一台服务器", primary: true);
    private readonly ModernButton emptyImport = Theme.Button("导入配置");
    private readonly Label noMappings = Theme.Label("先添加或启用服务映射，再连接服务器。", 10, Theme.Muted);
    private readonly ModernButton noMappingsAdd = Theme.Button("添加服务映射");
    private readonly RoundedPanel emptyMapping = new() { Dock = DockStyle.Fill, Radius = 14, Fill = Theme.Surface, Stroke = Theme.Line, BackColor = Theme.Background, Visible = false };
    private readonly ModernButton editServerButton = Theme.Button("编辑服务器");
    private readonly ModernButton moreButton = Theme.Button("更多");
    private readonly ModernButton addMappingButton = Theme.Button("添加映射");
    private readonly FlowLayoutPanel rowsHost = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        AutoScroll = true, BackColor = Theme.Background, Margin = Padding.Empty, Padding = Padding.Empty
    };
    private readonly ToolStripDropDown moreMenu = new();
    private readonly ToolTip connectionTips = new() { ShowAlways = true, AutomaticDelay = 250 };
    private readonly TableLayoutPanel rootLayout;
    private bool expandedLog;
    private bool interactionEnabled = true;
    private bool resizingRows;
    private bool showingEmptyWorkspace, showingNoMappings;

    public event Action<string>? ConnectRequested;
    public event Action? DisconnectRequested;
    public event Action? EditServerRequested;
    public event Action? ExportServerRequested;
    public event Action? DeleteServerRequested;
    public event Action? AddMappingRequested;
    public event Action<PortRule>? EditMappingRequested;
    public event Action<PortRule>? OpenMappingRequested;
    public event Action<PortRule>? CopyMappingRequested;
    public event Action? AddFirstServerRequested;
    public event Action? ImportRequested;

    public WorkspaceView()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        Padding = new Padding(30, 24, 30, 16);

        rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = Theme.Background, Margin = Padding.Empty
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        Controls.Add(rootLayout);

        BuildProfileArea();
        BuildEmptyArea();
        rootLayout.Controls.Add(profileArea, 0, 0);
        rootLayout.Controls.Add(emptyArea, 0, 0);
        rootLayout.Controls.Add(BuildLogStrip(), 0, 1);

        editServerButton.Click += (_, _) => EditServerRequested?.Invoke();
        addMappingButton.Click += (_, _) => AddMappingRequested?.Invoke();
        noMappingsAdd.Click += (_, _) => AddMappingRequested?.Invoke();
        emptyAdd.Click += (_, _) => AddFirstServerRequested?.Invoke();
        emptyImport.Click += (_, _) => ImportRequested?.Invoke();
        connectButton.Click += (_, _) => ConnectRequested?.Invoke(credential.Text);
        disconnectButton.Click += (_, _) => DisconnectRequested?.Invoke();
        logToggle.Click += (_, _) => ToggleLog();
        moreButton.Click += (_, _) => moreMenu.Show(moreButton, new Point(0, moreButton.Height));

        moreMenu.Renderer = Theme.MenuRenderer;
        moreMenu.BackColor = Theme.Surface;
        moreMenu.ForeColor = Theme.Ink;
        moreMenu.Items.Add("导出当前服务器", null, (_, _) => ExportServerRequested?.Invoke());
        moreMenu.Items.Add("删除当前服务器", null, (_, _) => DeleteServerRequested?.Invoke());
    }

    public string CredentialText => credential.Text;
    public bool IsShowingEmptyWorkspace => showingEmptyWorkspace;
    public bool IsShowingNoMappings => showingNoMappings;

    public void ClearCredential() => credential.Clear();

    public void Bind(ServerProfile? profile, string state, Color stateColor, bool busy, bool connected,
        IReadOnlyList<PortRule> rules, IReadOnlyList<string> logLines, bool allowEditing)
    {
        profileArea.Visible = profile != null;
        emptyArea.Visible = profile == null;
        showingEmptyWorkspace = profile == null;
        if (profile == null) return;

        title.Text = profile.Name;
        endpoint.Text = $"{profile.User}@{profile.Host}:{profile.Port}";
        authentication.Text = profile.Auth == "key" ? "私钥认证" : "密码认证";
        credentialHint.Text = profile.Auth == "key" ? "私钥口令（可留空）" : "密码（仅本次连接）";
        status.Text = "● " + state;
        status.ForeColor = stateColor;
        status.BackColor = Theme.Surface;
        credential.Visible = !connected;
        credentialHint.Visible = !connected;
        credential.Enabled = !busy && allowEditing;
        connectButton.Visible = !connected;
        var hasEnabledRules = rules.Any(rule => rule.Enabled);
        connectButton.Enabled = !busy && allowEditing && hasEnabledRules;
        connectButton.AccessibleDescription = hasEnabledRules ? "连接当前服务器" : "请先添加或启用服务映射，再连接服务器。";
        connectionTips.SetToolTip(connectButton, hasEnabledRules ? "" : "请先添加或启用服务映射，再连接服务器。");
        connectButton.Text = busy ? "连接中…" : "连接服务器";
        disconnectButton.Visible = connected || busy;
        disconnectButton.Enabled = connected && !busy;
        editServerButton.Enabled = allowEditing;
        moreButton.Enabled = allowEditing;
        addMappingButton.Enabled = allowEditing;
        noMappingsAdd.Enabled = allowEditing;
        moreMenu.Enabled = allowEditing;
        rowsHost.SuspendLayout();
        foreach (Control old in rowsHost.Controls.Cast<Control>().ToArray())
        {
            rowsHost.Controls.Remove(old);
            old.Dispose();
        }
        foreach (var rule in rules)
        {
            var row = new MappingRow(rule, connected && rule.Enabled, allowEditing)
            {
                Width = Math.Max(360, rowsHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 3),
                Margin = new Padding(0, 0, 0, 9)
            };
            row.OpenRequested += value => OpenMappingRequested?.Invoke(value);
            row.CopyRequested += value => CopyMappingRequested?.Invoke(value);
            row.EditRequested += value => EditMappingRequested?.Invoke(value);
            rowsHost.Controls.Add(row);
        }
        rowsHost.ResumeLayout(true);
        showingNoMappings = rules.Count == 0;
        emptyMapping.Visible = showingNoMappings;
        rowsHost.Visible = !showingNoMappings;
        if (showingNoMappings) emptyMapping.BringToFront();
        else rowsHost.BringToFront();

        var latest = logLines.Count == 0 ? "尚无连接记录" : logLines[^1];
        latestLog.Text = latest;
        fullLog.Text = string.Join(Environment.NewLine, logLines);
        fullLog.SelectionStart = fullLog.TextLength;
        fullLog.ScrollToCaret();
        fullLog.Visible = expandedLog;
        logToggle.Text = expandedLog ? "收起记录" : "展开记录";
    }

    public void SetInteractionEnabled(bool enabled)
    {
        interactionEnabled = enabled;
        emptyAdd.Enabled = enabled;
        emptyImport.Enabled = enabled;
        editServerButton.Enabled = enabled;
        addMappingButton.Enabled = enabled;
        noMappingsAdd.Enabled = enabled;
        moreButton.Enabled = enabled;
        foreach (Control child in rowsHost.Controls)
            if (child is MappingRow row) row.SetInteractionEnabled(enabled);
    }

    private void BuildProfileArea()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            BackColor = Theme.Background, Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        profileArea.Controls.Add(layout);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty, BackColor = Theme.Background };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 204));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        title.Dock = DockStyle.Fill; title.Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold); title.TextAlign = ContentAlignment.MiddleLeft;
        title.AutoSize = false;
        header.Controls.Add(title, 0, 0);
        endpoint.Dock = DockStyle.Fill; endpoint.TextAlign = ContentAlignment.MiddleLeft;
        var meta = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Theme.Background };
        meta.Controls.Add(endpoint);
        var divider = Theme.Label(" · ", 8.5f, Theme.Line); meta.Controls.Add(divider);
        meta.Controls.Add(authentication);
        header.Controls.Add(meta, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 1, 0, 0), BackColor = Theme.Background };
        editServerButton.Size = new Size(112, 36); editServerButton.Margin = new Padding(0, 0, 0, 0);
        moreButton.Size = new Size(72, 36); moreButton.Margin = new Padding(0, 0, 8, 0);
        actions.Controls.Add(editServerButton); actions.Controls.Add(moreButton);
        header.Controls.Add(actions, 1, 0); header.SetRowSpan(actions, 2);
        layout.Controls.Add(header, 0, 0);

        var connection = new RoundedPanel
        {
            Dock = DockStyle.Fill, Radius = 14, Fill = Theme.Surface, Stroke = Theme.Line,
            Padding = new Padding(14, 11, 14, 11), Margin = new Padding(0, 0, 0, 12), BackColor = Theme.Background
        };
        var connectionRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Surface };
        connectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
        connectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        connectionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144));
        connectionRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        status.Dock = DockStyle.Fill; status.AutoSize = false; status.TextAlign = ContentAlignment.MiddleLeft;
        connectionRow.Controls.Add(status, 0, 0);
        var credentialStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(6, 0, 10, 0), BackColor = Theme.Surface };
        credentialStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        credentialStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        credentialStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        credentialHint.Dock = DockStyle.Fill; credentialHint.AutoSize = false; credentialHint.TextAlign = ContentAlignment.MiddleLeft;
        credentialStack.Controls.Add(credentialHint, 0, 0);
        credential.Margin = Padding.Empty; credential.BorderStyle = BorderStyle.None;
        credential.BackColor = Theme.InputSurface; credential.ForeColor = Theme.Ink; credential.Dock = DockStyle.Fill;
        credential.AccessibleName = "连接凭据，仅本次使用";
        credentialStack.Controls.Add(Theme.Input(credential), 0, 1);
        connectionRow.Controls.Add(credentialStack, 1, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Surface };
        connectButton.Size = new Size(130, 38); connectButton.Margin = new Padding(0, 0, 0, 0);
        disconnectButton.Size = new Size(84, 38); disconnectButton.Margin = new Padding(0, 0, 0, 0);
        buttons.Controls.Add(connectButton); buttons.Controls.Add(disconnectButton);
        connectionRow.Controls.Add(buttons, 2, 0);
        connection.Controls.Add(connectionRow); layout.Controls.Add(connection, 0, 1);

        var listHeading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Background };
        listHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        listHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        var heading = Theme.Label("服务映射", 11, Theme.Ink, true);
        heading.Dock = DockStyle.Fill; heading.TextAlign = ContentAlignment.MiddleLeft;
        listHeading.Controls.Add(heading, 0, 0);
        addMappingButton.Size = new Size(116, 34); addMappingButton.Margin = new Padding(0, 0, 0, 0);
        listHeading.Controls.Add(addMappingButton, 1, 0);
        layout.Controls.Add(listHeading, 0, 2);

        var listPanel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Margin = Padding.Empty };
        listPanel.Controls.Add(rowsHost);
        noMappings.Dock = DockStyle.Top; noMappings.TextAlign = ContentAlignment.MiddleCenter; noMappings.Height = 44;
        noMappingsAdd.Dock = DockStyle.Top; noMappingsAdd.Size = new Size(160, 36); noMappingsAdd.Margin = new Padding(0, 0, 0, 0);
        var emptyMappingContent = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20), BackColor = Theme.Surface };
        var emptyStack = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.None, BackColor = Theme.Surface };
        emptyStack.Controls.Add(noMappings); emptyStack.Controls.Add(noMappingsAdd);
        emptyMappingContent.Controls.Add(emptyStack);
        emptyMappingContent.Resize += (_, _) => emptyStack.Location = new Point(Math.Max(0, (emptyMappingContent.ClientSize.Width - emptyStack.Width) / 2), Math.Max(0, (emptyMappingContent.ClientSize.Height - emptyStack.Height) / 2));
        emptyMapping.Controls.Add(emptyMappingContent); listPanel.Controls.Add(emptyMapping);
        rowsHost.ClientSizeChanged += (_, _) => ResizeRows();
        layout.Controls.Add(listPanel, 0, 3);
    }

    private void BuildEmptyArea()
    {
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.None, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Background,
            Padding = new Padding(0), Margin = Padding.Empty
        };
        emptyTitle.Margin = new Padding(0, 0, 0, 10);
        emptyDescription.Margin = new Padding(0, 0, 0, 22);
        emptyAdd.Size = new Size(210, 40); emptyAdd.Margin = new Padding(0, 0, 0, 10);
        emptyImport.Size = new Size(210, 38); emptyImport.Margin = Padding.Empty;
        stack.Controls.Add(emptyTitle); stack.Controls.Add(emptyDescription); stack.Controls.Add(emptyAdd); stack.Controls.Add(emptyImport);
        emptyArea.Resize += (_, _) => stack.Location = new Point(Math.Max(24, (emptyArea.ClientSize.Width - stack.Width) / 2), Math.Max(32, (emptyArea.ClientSize.Height - stack.Height) / 2 - 20));
        emptyArea.Controls.Add(stack);
    }

    private Control BuildLogStrip()
    {
        var outer = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Margin = Padding.Empty };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Theme.Background, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var label = Theme.Label("最新记录", 8.5f, Theme.Muted, true); label.Dock = DockStyle.Fill; label.TextAlign = ContentAlignment.MiddleLeft;
        latestLog.Dock = DockStyle.Fill; latestLog.TextAlign = ContentAlignment.MiddleLeft;
        var copy = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, BackColor = Theme.Background };
        copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 18)); copy.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        copy.Controls.Add(label, 0, 0); copy.Controls.Add(latestLog, 0, 1);
        layout.Controls.Add(copy, 0, 0);
        logToggle.Size = new Size(104, 32); logToggle.Margin = new Padding(0, 5, 0, 0); layout.Controls.Add(logToggle, 1, 0);
        fullLog.Margin = new Padding(0, 0, 0, 4); layout.Controls.Add(fullLog, 0, 1); layout.SetColumnSpan(fullLog, 2);
        outer.Controls.Add(layout);
        return outer;
    }

    private void ToggleLog()
    {
        expandedLog = !expandedLog;
        fullLog.Visible = expandedLog;
        logToggle.Text = expandedLog ? "收起记录" : "展开记录";
        if (rootLayout.RowStyles.Count > 1)
            rootLayout.RowStyles[1].Height = expandedLog ? 160 : 44;
    }

    private void ResizeRows()
    {
        if (resizingRows) return;
        resizingRows = true;
        try
        {
            var width = Math.Max(320, rowsHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
            rowsHost.SuspendLayout();
            foreach (Control item in rowsHost.Controls) item.Width = width;
            rowsHost.ResumeLayout(true);
            rowsHost.PerformLayout();
        }
        finally { resizingRows = false; }
    }

    private sealed class MappingRow : UserControl
    {
        private readonly PortRule rule;
        private readonly ToolTip tips = new() { ShowAlways = true, AutomaticDelay = 250 };
        private readonly ModernButton openOrCopy;
        private readonly ModernButton edit = Theme.Button("编辑");
        public event Action<PortRule>? OpenRequested;
        public event Action<PortRule>? CopyRequested;
        public event Action<PortRule>? EditRequested;

        public MappingRow(PortRule rule, bool canOpen, bool canEdit)
        {
            this.rule = rule;
            Height = 76; MinimumSize = new Size(320, 76); BackColor = Theme.Background;
            var panel = new RoundedPanel { Dock = DockStyle.Fill, Radius = 13, Fill = Theme.Surface, Stroke = Theme.Line, BackColor = Theme.Background, Padding = new Padding(14, 8, 12, 8) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Theme.Surface, Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 178));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var title = Theme.Label(rule.Name, 9.5f, Theme.Ink, true); title.AutoSize = false; title.AutoEllipsis = true; title.Dock = DockStyle.Fill; title.TextAlign = ContentAlignment.BottomLeft; title.UseMnemonic = false;
            var protocol = Theme.Label((rule.Protocol ?? "tcp").ToUpperInvariant() + (rule.Enabled ? "  ·  已启用" : "  ·  已停用"), 8, Theme.Muted);
            protocol.AutoSize = false; protocol.AutoEllipsis = true; protocol.Dock = DockStyle.Fill; protocol.TextAlign = ContentAlignment.TopLeft; protocol.UseMnemonic = false;
            var info = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, BackColor = Theme.Surface };
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            info.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); info.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            info.Controls.Add(title, 0, 0); info.Controls.Add(protocol, 0, 1); layout.Controls.Add(info, 0, 0);

            var isWeb = rule.Protocol is "http" or "https";
            var validLocal = true;
            string local;
            if (isWeb)
            {
                try { local = rule.LocalEndpoint(); }
                catch (InvalidOperationException) { local = "网页路径需要检查"; validLocal = false; }
            }
            else local = rule.LocalEndpoint();
            var remote = $"远端  {rule.TargetHost}:{rule.TargetPort}";
            var localLabel = Theme.Label(local, 9, Theme.Ink); localLabel.AutoSize = false; localLabel.Dock = DockStyle.Fill; localLabel.TextAlign = ContentAlignment.BottomLeft; localLabel.AutoEllipsis = true; localLabel.UseMnemonic = false;
            var remoteLabel = Theme.Label(remote, 8, Theme.Muted); remoteLabel.AutoSize = false; remoteLabel.Dock = DockStyle.Fill; remoteLabel.TextAlign = ContentAlignment.TopLeft; remoteLabel.AutoEllipsis = true; remoteLabel.UseMnemonic = false;
            tips.SetToolTip(localLabel, validLocal ? local : "该规则的网页路径格式无效；请编辑后再打开或复制入口。");
            tips.SetToolTip(remoteLabel, remote);
            var target = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(6, 0, 0, 0), BackColor = Theme.Surface };
            target.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            target.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); target.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            target.Controls.Add(localLabel, 0, 0); target.Controls.Add(remoteLabel, 0, 1); layout.Controls.Add(target, 1, 0);

            openOrCopy = Theme.Button(isWeb ? "打开" : "复制地址");
            openOrCopy.Size = new Size(isWeb ? 56 : 82, 32); openOrCopy.Margin = new Padding(0, 0, 4, 0);
            openOrCopy.Enabled = isWeb ? rule.Enabled && canOpen && validLocal : true;
            openOrCopy.Click += (_, _) => { if (isWeb) OpenRequested?.Invoke(rule); else CopyRequested?.Invoke(rule); };
            edit.Size = new Size(58, 32); edit.Margin = Padding.Empty; edit.Enabled = canEdit;
            edit.Click += (_, _) => EditRequested?.Invoke(rule);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 10, 0, 0), BackColor = Theme.Surface };
            if (isWeb)
            {
                var copy = Theme.Button("复制");
                copy.Size = new Size(52, 32); copy.Margin = new Padding(0, 0, 4, 0);
                copy.Enabled = validLocal;
                copy.AccessibleName = "复制完整本机入口";
                copy.Click += (_, _) => CopyRequested?.Invoke(rule);
                tips.SetToolTip(copy, "复制完整入口");
                actions.Controls.Add(copy);
            }
            actions.Controls.Add(edit); actions.Controls.Add(openOrCopy);
            layout.Controls.Add(actions, 2, 0);
            panel.Controls.Add(layout); Controls.Add(panel);
        }

        public void SetInteractionEnabled(bool enabled)
        {
            edit.Enabled = enabled;
            if (!rule.Enabled && rule.Protocol != "tcp") openOrCopy.Enabled = false;
        }
    }
}
