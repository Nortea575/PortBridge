namespace PortBridge;

internal sealed class ServerEditorView : UserControl
{
    private readonly TextBox name = new();
    private readonly TextBox host = new();
    private readonly NumericUpDown port = new() { Minimum = 1, Maximum = 65535, Value = 22, ThousandsSeparator = false };
    private readonly TextBox user = new();
    private readonly ComboBox auth = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox keyPath = new();
    private readonly Panel keyPathRow = new() { Dock = DockStyle.Fill, BackColor = Theme.Surface, Visible = false };
    private readonly Label headingTitle = Theme.Label("添加服务器", 18, Theme.Ink, true);
    private TableLayoutPanel? bodyContent;
    private readonly Label note = Theme.Label("密码和私钥口令会在连接时输入，不保存在配置中。", 8.5f, Theme.Muted);
    private readonly ModernButton save = Theme.Button("保存服务器", primary: true);
    private readonly ModernButton cancel = Theme.Button("取消");
    private string originalName = "", originalHost = "", originalUser = "", originalAuth = "password", originalKeyPath = "";
    private int originalPort = 22;
    private bool loading;
    private bool isNew;

    public event Action? SaveRequested;
    public event Action? BackRequested;
    public event Action? CancelRequested;

    public bool IsDirty
    {
        get
        {
            var currentAuth = auth.SelectedIndex == 1 ? "key" : "password";
            return !string.Equals(name.Text, originalName, StringComparison.Ordinal)
                || !string.Equals(host.Text, originalHost, StringComparison.Ordinal)
                || !string.Equals(user.Text, originalUser, StringComparison.Ordinal)
                || (int)port.Value != originalPort
                || currentAuth != originalAuth
                || (currentAuth == "key" && !string.Equals(keyPath.Text, originalKeyPath, StringComparison.Ordinal));
        }
    }

    public ServerEditorView()
    {
        Dock = DockStyle.Fill; BackColor = Theme.Background; Padding = new Padding(30, 24, 30, 20);
        auth.Items.AddRange(new object[] { "密码", "私钥文件" });
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
        var content = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 5, Height = 256, Margin = Padding.Empty, Padding = new Padding(0, 12, 0, 0), BackColor = Theme.Background };
        bodyContent = content;
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        scroll.Controls.Add(content);

        var names = Row(2, 58, 42);
        names.Controls.Add(Theme.Field("服务器名称", name), 0, 0);
        names.Controls.Add(Theme.Field("SSH 主机", host), 1, 0);
        content.Controls.Add(names, 0, 0);

        var details = Row(3, 20, 45, 35);
        details.Controls.Add(Theme.Field("SSH 端口", port), 0, 0);
        details.Controls.Add(Theme.Field("用户名", user), 1, 0);
        details.Controls.Add(Theme.Field("认证方式", auth), 2, 0);
        content.Controls.Add(details, 0, 1);

        var keyRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Background };
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        keyRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        keyPathRow.Controls.Add(keyRow);
        keyRow.Controls.Add(Theme.Field("私钥文件", keyPath), 0, 0);
        var browse = Theme.Button("浏览文件…"); browse.Size = new Size(96, 36); browse.Margin = new Padding(4, 23, 0, 0); browse.Dock = DockStyle.Top;
        browse.Click += (_, _) => BrowseKey(); keyRow.Controls.Add(browse, 1, 0);
        content.Controls.Add(keyPathRow, 0, 2);

        note.Dock = DockStyle.Fill; note.TextAlign = ContentAlignment.MiddleLeft;
        content.Controls.Add(note, 0, 3);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty, BackColor = Theme.Background, Padding = new Padding(0, 4, 0, 0) };
        save.Size = new Size(124, 38); save.Margin = Padding.Empty;
        cancel.Size = new Size(88, 38); cancel.Margin = new Padding(0, 0, 8, 0);
        save.Click += (_, _) => SaveRequested?.Invoke();
        cancel.Click += (_, _) => CancelRequested?.Invoke();
        footer.Controls.Add(save); footer.Controls.Add(cancel); content.Controls.Add(footer, 0, 4);

        auth.SelectedIndexChanged += (_, _) => UpdateAuthUi();
        name.TextChanged += (_, _) => UpdateTitle();
    }

    public void LoadProfile(ServerProfile profile, bool creating)
    {
        loading = true;
        isNew = creating;
        originalName = profile.Name; originalHost = profile.Host; originalUser = profile.User;
        originalPort = profile.Port; originalAuth = profile.Auth; originalKeyPath = profile.KeyPath;
        name.Text = profile.Name; host.Text = profile.Host; user.Text = profile.User;
        port.Value = Math.Clamp(profile.Port, 1, 65535);
        auth.SelectedIndex = profile.Auth == "key" ? 1 : 0;
        keyPath.Text = profile.KeyPath;
        loading = false;
        UpdateTitle(); UpdateAuthUi();
    }

    public void ApplyTo(ServerProfile profile)
    {
        profile.Name = name.Text.Trim();
        profile.Host = host.Text.Trim();
        profile.Port = (int)port.Value;
        profile.User = user.Text.Trim();
        profile.Auth = auth.SelectedIndex == 1 ? "key" : "password";
        profile.KeyPath = profile.Auth == "key" ? keyPath.Text.Trim() : "";
    }

    public bool IsNew => isNew;

    private void UpdateTitle()
    {
        headingTitle.Text = isNew ? "添加服务器" : "编辑服务器";
    }

    private void UpdateAuthUi()
    {
        var usingKey = auth.SelectedIndex == 1;
        keyPathRow.Visible = usingKey;
        if (bodyContent != null)
        {
            bodyContent.RowStyles[2].Height = usingKey ? 74 : 0;
            bodyContent.Height = usingKey ? 330 : 256;
        }
        if (!loading) UpdateTitle();
    }

    private void BrowseKey()
    {
        using var dialog = new OpenFileDialog { Title = "选择 SSH 私钥文件" };
        if (dialog.ShowDialog(this) == DialogResult.OK) keyPath.Text = dialog.FileName;
    }

    private static TableLayoutPanel Row(int columns, params float[] widths)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = 1, Margin = Padding.Empty, BackColor = Theme.Background };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (var width in widths) row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        return row;
    }
}
