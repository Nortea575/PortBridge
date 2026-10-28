using System.Diagnostics;
using System.Text.Json;

namespace PortBridge;

public sealed partial class MainForm : Form
{
    private readonly ConfigStore store;
    private AppConfig config = new();
    private readonly ComboBox profiles = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly TextBox name = new(), host = new(), user = new(), keyPath = new();
    private readonly NumericUpDown port = new() { Minimum = 1, Maximum = 65535, Value = 22, Width = 90 };
    private readonly ComboBox auth = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox secret = new() { UseSystemPasswordChar = true };
    private readonly Label secretLabel = new() { Text = "密码（不保存）", AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly DataGridView grid = new()
    {
        AutoGenerateColumns = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false, Dock = DockStyle.Fill, BackgroundColor = Color.White,
        BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
    };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.FromArgb(245, 247, 250) };
    private readonly Label status = new() { Text = "● 未连接", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(8) };
    private readonly ModernButton connect = new() { Text = "连接服务器", Primary = true, Size = new Size(120, 40) }, disconnect = new() { Text = "断开", Size = new Size(76, 40), Enabled = false };
    private readonly List<Control> editControls = new();
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private TunnelSession? session;
    private CancellationTokenSource? connecting;
    private bool loading, busy, exiting, toldTray;
    private string activeName = "";
    private ServerProfile? editedProfile;

    public MainForm(ConfigStore? configStore = null)
    {
        store = configStore ?? new ConfigStore();
        Text = "PortBridge · 服务转发器";
        Font = new Font("Microsoft YaHei UI", 9F);
        Size = new Size(1280, 920); MinimumSize = new Size(1120, 850);
        BackColor = Theme.Background;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = SystemIcons.Application;
        BuildUi();
        tray = new NotifyIcon { Icon = Icon, Text = "PortBridge · 未连接", Visible = true };
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开主窗口", null, (_, _) => ShowWindow());
        menu.Items.Add("断开连接", null, (_, _) => Disconnect());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowWindow();
        try { config = store.Load(); }
        catch (Exception e)
        {
            MessageBox.Show("无法读取配置，原文件会保留。请检查或移走以下文件后重新启动：\n" + store.FilePath + "\n\n" + e.Message, "配置读取失败");
            // Never replace an unreadable existing configuration with an empty one.
            Shown += (_, _) => { exiting = true; Close(); };
            return;
        }
        if (config.Profiles.Count == 0) config.Profiles.Add(new ServerProfile { Rules = new() { new PortRule() } });
        RefreshProfiles(config.SelectedId);
        timer.Tick += (_, _) =>
        {
            if (!busy && session != null && !session.IsConnected)
            {
                WriteLog("连接已中断，请检查网络后重新连接。");
                Disconnect();
            }
        };
        timer.Start();
        FormClosing += OnClosing;
    }

    private ServerProfile? Current => profiles.SelectedItem as ServerProfile;
    private void RefreshProfiles(string selected)
    {
        loading = true;
        profiles.DataSource = null; profiles.DataSource = config.Profiles.ToList();
        profiles.SelectedItem = config.Profiles.FirstOrDefault(p => p.Id == selected) ?? config.Profiles[0];
        loading = false; LoadProfile();
    }
    private void RefreshProfileNames()
    {
        var selected = Current?.Id ?? ""; RefreshProfiles(selected);
    }
    private void LoadProfile()
    {
        if (Current is not { } p) return;
        editedProfile = p;
        name.Text = p.Name; host.Text = p.Host; port.Value = Math.Clamp(p.Port, 1, 65535); user.Text = p.User;
        auth.SelectedIndex = p.Auth == "key" ? 1 : 0; keyPath.Text = p.KeyPath; secret.Clear();
        grid.DataSource = p.Rules; config.SelectedId = p.Id;
    }
    private bool SaveCurrent()
    {
        if (!grid.EndEdit()) { MessageBox.Show(this, "请先修正表格中的无效值。", "PortBridge"); return false; }
        if (editedProfile is { } p)
        {
            p.Name = name.Text.Trim(); p.Host = host.Text.Trim(); p.Port = (int)port.Value; p.User = user.Text.Trim();
            p.Auth = auth.SelectedIndex == 1 ? "key" : "password"; p.KeyPath = keyPath.Text.Trim(); config.SelectedId = p.Id;
        }
        return true;
    }
    private bool Persist()
    {
        try { store.Save(config); return true; }
        catch (Exception e) { MessageBox.Show(this, "无法保存配置：" + e.Message, "PortBridge"); return false; }
    }

    private async Task Connect()
    {
        if (busy || session != null || !SaveCurrent() || Current is not { } p) return;
        try { p.Validate(); } catch (Exception e) { MessageBox.Show(this, e.Message, "请检查配置"); return; }
        if (!Persist()) return;
        busy = true; SetEditing(false); SetStatus("连接中…", Color.DarkOrange);
        connecting = new CancellationTokenSource();
        var candidate = new TunnelSession();
        candidate.Error += message => Ui(() => WriteLog("连接提示：" + message));
        var credential = secret.Text;
        try
        {
            await candidate.ConnectAsync(p, credential, TrustHost, connecting.Token);
            if (exiting) { candidate.Dispose(); return; }
            session = candidate; activeName = p.Name;
            SetStatus("已连接 · " + activeName, Color.ForestGreen);
            WriteLog("隧道已建立：" + string.Join("；", p.Rules.Where(r => r.Enabled).Select(r => r.Name + " → 127.0.0.1:" + r.LocalPort)));
            WriteLog("目标服务是否可用，请打开对应网页或客户端验证。");
        }
        catch (Exception e)
        {
            candidate.Dispose(); SetStatus("连接失败", Color.Firebrick);
            WriteLog("连接失败：" + e.Message);
            if (!exiting) MessageBox.Show(this, e.Message + "\n\n请检查 SSH 地址、认证、转发权限及本地端口占用。", "连接失败");
        }
        finally
        {
            credential = ""; secret.Clear(); busy = false; connecting.Dispose(); connecting = null;
            SetEditing(session == null);
            if (exiting) Close();
        }
    }

    private bool TrustHost(string endpoint, string fingerprint)
    {
        if (exiting) return false;
        return (bool)Invoke(() =>
        {
            if (config.TrustedHosts.TryGetValue(endpoint, out var saved))
            {
                if (saved == fingerprint) return true;
                WriteLog("服务器指纹发生变化，已拒绝连接。");
                MessageBox.Show(this, "服务器指纹与已保存记录不一致，已拒绝连接。\n\n" + endpoint + "\n原指纹：" + saved + "\n当前指纹：" + fingerprint + "\n\n确认服务器重装或密钥轮换后，可在本地 settings.json 中移除对应 TrustedHosts 记录，再连接并核对新指纹。", "服务器身份变化", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            var accept = MessageBox.Show(this, "首次连接，请与服务器管理员提供的指纹核对：\n\n" + endpoint + "\n" + fingerprint + "\n\n是否信任并保存此服务器指纹？", "确认 SSH 服务器身份", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            if (!accept) return false;
            config.TrustedHosts[endpoint] = fingerprint;
            if (Persist()) return true;
            config.TrustedHosts.Remove(endpoint); return false;
        });
    }

    private void Disconnect()
    {
        if (busy) return;
        session?.Dispose(); session = null; SetEditing(true); SetStatus("未连接", Color.DimGray); WriteLog("隧道已断开。");
    }
    private void SetEditing(bool enabled)
    {
        foreach (var c in editControls) c.Enabled = enabled;
        grid.ReadOnly = !enabled; connect.Enabled = enabled; disconnect.Enabled = !busy && session != null;
    }
    private void SetStatus(string message, Color color)
    {
        status.Text = "● " + message; status.ForeColor = color;
        if (status.Parent is RoundedPanel pill)
        {
            var fill = color == Color.ForestGreen ? Color.FromArgb(229, 247, 239)
                : color == Color.Firebrick ? Color.FromArgb(253, 235, 236)
                : color == Color.DarkOrange ? Color.FromArgb(255, 245, 224)
                : Color.FromArgb(231, 237, 247);
            pill.Fill = pill.Stroke = status.BackColor = fill; pill.Invalidate();
        }
        if (tray != null) tray.Text = ("PortBridge · " + message)[..Math.Min(63, ("PortBridge · " + message).Length)];
    }
    private void OpenService()
    {
        if (session?.IsConnected != true) { MessageBox.Show(this, "请先建立连接。", "PortBridge"); return; }
        if (grid.CurrentRow?.DataBoundItem is not PortRule r || !r.Enabled) return;
        try { Process.Start(new ProcessStartInfo(r.BrowserUrl()) { UseShellExecute = true }); }
        catch (Exception e) { MessageBox.Show(this, e.Message, "打开服务"); }
    }
    private void WriteLog(string message)
    {
        if (log.TextLength > 24000) log.Text = log.Text[^12000..];
        log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
    }
    private void Ui(Action action) { if (!IsDisposed && IsHandleCreated) BeginInvoke(action); }
    private void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.WindowsShutDown) exiting = true;
        if (!exiting)
        {
            if (SaveCurrent()) Persist();
            e.Cancel = true; Hide();
            if (!toldTray) { tray.ShowBalloonTip(3000, "PortBridge 仍在运行", "从右下角托盘打开；选择“退出”可停止转发。", ToolTipIcon.Info); toldTray = true; }
        }
    }
    private void ExitApp()
    {
        if (!busy && SaveCurrent()) Persist();
        exiting = true; connecting?.Cancel();
        if (!busy) Close();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); session?.Dispose(); tray?.Dispose(); connecting?.Dispose(); }
        base.Dispose(disposing);
    }

    private void ExportProfile()
    {
        if (!SaveCurrent() || Current == null) return;
        using var d = new SaveFileDialog { Filter = "PortBridge 配置 (*.json)|*.json", FileName = "profile.json" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            // Do not export private-key paths or trusted-host decisions.
            var copy = JsonSerializer.Deserialize<ServerProfile>(JsonSerializer.Serialize(Current))!;
            copy.KeyPath = "";
            File.WriteAllText(d.FileName, JsonSerializer.Serialize(copy, ConfigStore.Json));
            WriteLog("配置已导出，不包含密码、私钥文件内容、私钥路径或服务器信任记录。分享前请检查主机地址和用户名。");
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "导出失败"); }
    }
    private void ImportProfile()
    {
        if (!SaveCurrent()) return;
        using var d = new OpenFileDialog { Filter = "PortBridge 配置 (*.json)|*.json" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var p = JsonSerializer.Deserialize<ServerProfile>(File.ReadAllText(d.FileName)) ?? throw new InvalidDataException("配置为空。");
            if (p.Rules == null || p.Rules.Any(r => r == null)) throw new InvalidDataException("转发规则格式错误。");
            p.Id = Guid.NewGuid().ToString("N");
            config.Profiles.Add(p); RefreshProfiles(p.Id); Persist();
            WriteLog("已导入。请检查主机、认证和规则，然后手动连接。");
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "导入失败"); }
    }
}
