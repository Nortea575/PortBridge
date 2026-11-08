namespace PortBridge;

internal sealed class RuleEditorView : UserControl
{
    private readonly TextBox name = new();
    private readonly ToggleSwitch enabled = new();
    private readonly ComboBox protocol = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown localPort = new() { Minimum = 1, Maximum = 65535, Value = 18080, ThousandsSeparator = false };
    private readonly TextBox targetHost = new();
    private readonly NumericUpDown targetPort = new() { Minimum = 1, Maximum = 65535, Value = 8080, ThousandsSeparator = false };
    private readonly TextBox path = new();
    private readonly Panel pathRow = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly Label preview = Theme.Label("", 9, Theme.Ink);
    private readonly Label headingTitle = Theme.Label("添加服务映射", 18, Theme.Ink, true);
    private TableLayoutPanel? bodyContent;
    private readonly ModernButton save = Theme.Button("保存映射", primary: true);
    private readonly ModernButton cancel = Theme.Button("取消");
    private readonly ModernButton delete = Theme.Button("删除此映射");
    private readonly Panel previewPanel = new() { Dock = DockStyle.Fill, BackColor = Theme.InputSurface, Padding = new Padding(14, 6, 14, 6), Margin = Padding.Empty };
    private string originalName = "", originalTargetHost = "127.0.0.1", originalPath = "/", originalProtocol = "http";
    private int originalLocalPort = 18080, originalTargetPort = 8080;
    private bool originalEnabled = true;
    private bool loading, isNew;
    private bool canDelete;

    public event Action? SaveRequested;
    public event Action? BackRequested;
    public event Action? CancelRequested;
    public event Action? DeleteRequested;

    public bool IsDirty => name.Text != originalName || targetHost.Text != originalTargetHost || path.Text != originalPath
        || SelectedProtocol != originalProtocol || (int)localPort.Value != originalLocalPort
        || (int)targetPort.Value != originalTargetPort || enabled.Checked != originalEnabled;
    public bool IsNew => isNew;
    public bool CanDelete => canDelete;

    private string SelectedProtocol => protocol.SelectedItem?.ToString()?.ToLowerInvariant() ?? "http";

    public RuleEditorView()
    {
        Dock = DockStyle.Fill; BackColor = Theme.Background; Padding = new Padding(30, 24, 30, 20);
        protocol.Items.AddRange(new object[] { "HTTP", "HTTPS", "TCP" });
        enabled.AccessibleName = "启用此映射";
        enabled.AccessibleDescription = "连接后建立该本机转发入口。";
        path.PlaceholderText = "/";

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, BackColor = Theme.Background };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Background };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        headingTitle.Dock = DockStyle.Fill; headingTitle.AutoSize = false; headingTitle.TextAlign = ContentAlignment.MiddleLeft;
        header.Controls.Add(headingTitle, 0, 0);
        var back = Theme.Button("返回"); back.Size = new Size(100, 36); back.Margin = new Padding(0, 6, 0, 0);
        back.Click += (_, _) => BackRequested?.Invoke(); header.Controls.Add(back, 1, 0);
        root.Controls.Add(header, 0, 0);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background };
        root.Controls.Add(scroll, 0, 1);
        var content = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 6, Height = 402, Margin = Padding.Empty, Padding = new Padding(0, 12, 0, 0), BackColor = Theme.Background };
        bodyContent = content;
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        scroll.Controls.Add(content);

        var nameEnabled = Row(2, 62, 38);
        nameEnabled.Controls.Add(Theme.Field("服务名称", name), 0, 0);
        nameEnabled.Controls.Add(BuildEnabledField(), 1, 0);
        content.Controls.Add(nameEnabled, 0, 0);

        var typeLocal = Row(2, 36, 64);
        typeLocal.Controls.Add(Theme.Field("协议", protocol), 0, 0);
        typeLocal.Controls.Add(Theme.Field("本地端口", localPort), 1, 0);
        content.Controls.Add(typeLocal, 0, 1);

        var destination = Row(2, 68, 32);
        destination.Controls.Add(Theme.Field("远端主机", targetHost), 0, 0);
        destination.Controls.Add(Theme.Field("远端端口", targetPort), 1, 0);
        content.Controls.Add(destination, 0, 2);

        pathRow.Controls.Add(Theme.Field("网页路径（HTTP / HTTPS）", path));
        content.Controls.Add(pathRow, 0, 3);

        previewPanel.Controls.Add(preview);
        preview.Dock = DockStyle.Fill; preview.AutoSize = false; preview.AutoEllipsis = true; preview.UseMnemonic = false; preview.TextAlign = ContentAlignment.MiddleLeft;
        content.Controls.Add(previewPanel, 0, 4);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Background, Padding = new Padding(0, 4, 0, 0) };
        save.Size = new Size(124, 38); save.Margin = Padding.Empty;
        cancel.Size = new Size(88, 38); cancel.Margin = new Padding(0, 0, 8, 0);
        delete.Size = new Size(116, 38); delete.Margin = new Padding(0, 0, 8, 0); delete.Visible = false;
        save.Click += (_, _) => SaveRequested?.Invoke();
        cancel.Click += (_, _) => CancelRequested?.Invoke();
        delete.Click += (_, _) => DeleteRequested?.Invoke();
        footer.Controls.Add(save); footer.Controls.Add(cancel); footer.Controls.Add(delete); content.Controls.Add(footer, 0, 5);

        protocol.SelectedIndexChanged += (_, _) => UpdateProtocolUi();
        localPort.ValueChanged += (_, _) => UpdatePreview();
        targetPort.ValueChanged += (_, _) => UpdatePreview();
        targetHost.TextChanged += (_, _) => UpdatePreview();
        path.TextChanged += (_, _) => UpdatePreview();
        name.TextChanged += (_, _) => UpdateTitle();
    }

    public void LoadRule(PortRule rule, bool creating)
    {
        loading = true;
        isNew = creating;
        canDelete = !creating;
        delete.Visible = canDelete;
        originalName = rule.Name; originalTargetHost = rule.TargetHost; originalPath = rule.Path;
        originalLocalPort = rule.LocalPort; originalTargetPort = rule.TargetPort;
        originalProtocol = rule.Protocol; originalEnabled = rule.Enabled;
        name.Text = rule.Name; targetHost.Text = rule.TargetHost;
        localPort.Value = Math.Clamp(rule.LocalPort, 1, 65535);
        targetPort.Value = Math.Clamp(rule.TargetPort, 1, 65535);
        enabled.Checked = rule.Enabled;
        protocol.SelectedIndex = rule.Protocol switch { "https" => 1, "tcp" => 2, _ => 0 };
        path.Text = rule.Path;
        loading = false;
        UpdateTitle(); UpdateProtocolUi();
    }

    public void ApplyTo(PortRule rule)
    {
        rule.Name = name.Text.Trim();
        rule.Enabled = enabled.Checked;
        rule.LocalPort = (int)localPort.Value;
        rule.TargetHost = targetHost.Text.Trim();
        rule.TargetPort = (int)targetPort.Value;
        rule.Protocol = SelectedProtocol;
        rule.Path = path.Text.Trim();
    }

    private Control BuildEnabledField()
    {
        var group = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 14, 0), BackColor = Theme.Background };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
        group.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var label = Theme.Label("启用此映射", 8.5f, Theme.Muted); label.Dock = DockStyle.Fill; label.TextAlign = ContentAlignment.MiddleLeft;
        group.Controls.Add(label, 0, 0);
        enabled.Anchor = AnchorStyles.Right; enabled.Margin = Padding.Empty;
        group.Controls.Add(enabled, 1, 0);
        return group;
    }

    private void UpdateProtocolUi()
    {
        var isWeb = SelectedProtocol is "http" or "https";
        pathRow.Visible = isWeb;
        if (bodyContent != null && bodyContent.RowStyles.Count > 3)
        {
            bodyContent.RowStyles[3].Height = isWeb ? 72 : 0;
            bodyContent.Height = isWeb ? 402 : 330;
            bodyContent.PerformLayout();
        }
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (preview == null || loading) return;
        var rule = new PortRule
        {
            LocalPort = (int)localPort.Value,
            Protocol = SelectedProtocol,
            Path = path.Text,
            TargetHost = targetHost.Text,
            TargetPort = (int)targetPort.Value
        };
        try
        {
            preview.Text = "本机入口  " + (rule.Protocol == "tcp" ? $"127.0.0.1:{rule.LocalPort}" : rule.BrowserUrl());
            preview.ForeColor = Theme.Ink;
        }
        catch (InvalidOperationException)
        {
            preview.Text = "本机入口  网页路径需要检查";
            preview.ForeColor = Theme.Error;
        }
    }

    private void UpdateTitle() => headingTitle.Text = isNew ? "添加服务映射" : "编辑服务映射";

    private static TableLayoutPanel Row(int columns, params float[] widths)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Background };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (var width in widths) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        return row;
    }
}
