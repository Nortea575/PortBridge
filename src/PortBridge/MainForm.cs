using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PortBridge;

public sealed partial class MainForm : Form
{
    private readonly ConfigStore store;
    private AppConfig config = new();
    private readonly Panel contentHost = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Panel homeView = new() { Dock = DockStyle.Fill, Visible = true, Margin = Padding.Empty };
    private readonly Panel settingsView = new() { Dock = DockStyle.Fill, Visible = false, Margin = Padding.Empty };
    private readonly Panel serverEditorHost = new() { Dock = DockStyle.Fill, Visible = false, Margin = Padding.Empty };
    private readonly Panel ruleEditorHost = new() { Dock = DockStyle.Fill, Visible = false, Margin = Padding.Empty };
    private ServerListView? serverListView;
    private WorkspaceView? workspaceView;
    private ServerEditorView? serverEditorView;
    private RuleEditorView? ruleEditorView;
    private readonly ToggleSwitch autoStartToggle = new();
    private readonly ToggleSwitch autoUpdateToggle = new();
    private readonly Label startupMessage = new() { AutoSize = true };
    private readonly Label updateStatus = new() { AutoSize = true };
    private readonly ModernButton checkUpdatesButton = new() { Text = "检查更新", Size = new Size(112, 38) };
    private readonly ModernButton openUpdateButton = new() { Text = "查看新版", Size = new Size(112, 38), Visible = false };
    private readonly IStartupManager startupManager;
    private readonly UpdateCheckService updateService;
    private readonly bool ownsUpdateService;
    private readonly bool startInTray, allowStartupUpdateCheck;
    private readonly Icon appIcon;
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
    private TunnelSession? session;
    private CancellationTokenSource? connecting;
    private bool busy, exiting, toldTray, settingsReady, updateCheckRunning, suppressSettingsEvents, resourcesDisposed, configLoadFailed;
    private readonly CancellationTokenSource appCancellation = new();
    private string uiState = "未连接";
    private Color uiStateColor = Theme.Muted;
    private readonly List<string> logHistory = new();
    private ServerProfile? serverEditSource, serverEditDraft, ruleEditOwner;
    private PortRule? ruleEditSource, ruleEditDraft;
    private bool serverEditIsNew, ruleEditIsNew;
    private Control? activePage;
    private (string Id, string Host, int Port, string User, string Auth, string KeyPath)? boundCredentialProfile;
    private StableRelease? availableRelease;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    public MainForm(ConfigStore? configStore = null, IStartupManager? startupManager = null,
        UpdateCheckService? updateService = null, bool startInTray = false, bool allowStartupUpdateCheck = true)
    {
        store = configStore ?? new ConfigStore();
        this.startupManager = startupManager ?? new WindowsStartupManager();
        this.updateService = updateService ?? new UpdateCheckService();
        ownsUpdateService = updateService == null;
        this.startInTray = startInTray;
        this.allowStartupUpdateCheck = allowStartupUpdateCheck;
        Text = Program.WindowTitle;
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;
        var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        var minWidth = Math.Min(1000, Math.Max(900, workArea.Width - 24));
        var minHeight = Math.Min(680, Math.Max(560, workArea.Height - 24));
        MinimumSize = new Size(minWidth, minHeight);
        Size = new Size(Math.Max(minWidth, Math.Min(1240, workArea.Width - 24)), Math.Max(minHeight, Math.Min(820, workArea.Height - 24)));
        BackColor = Theme.Background;
        StartPosition = FormStartPosition.CenterScreen;
        appIcon = LoadApplicationIcon();
        Icon = appIcon;
        BuildUi();
        tray = new NotifyIcon { Icon = Icon, Text = "PortBridge · 未连接", Visible = true };
        var menu = new ContextMenuStrip();
        menu.Renderer = Theme.MenuRenderer;
        menu.BackColor = Theme.Surface; menu.ForeColor = Theme.Ink;
        menu.Items.Add("打开主窗口", null, (_, _) => ShowWindow());
        menu.Items.Add("断开连接", null, (_, _) => Disconnect());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp());
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowWindow();
        try { config = store.Load(); }
        catch (Exception e)
        {
            configLoadFailed = true;
            MessageBox.Show("无法读取配置，原文件会保留。请检查或移走以下文件后重新启动：\n" + store.FilePath + "\n\n" + e.Message, "配置读取失败");
            // Never replace an unreadable existing configuration with an empty one.
            Shown += (_, _) => { exiting = true; Close(); };
            return;
        }
        InitializeSettingsControls();
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
        Shown += (_, _) =>
        {
            if (startInTray) Hide();
            if (allowStartupUpdateCheck && config.CheckUpdatesOnStartup) _ = CheckForUpdatesAsync();
        };
    }

    private ServerProfile? Current => config.Profiles.FirstOrDefault(p => p.Id == config.SelectedId);
    private void RefreshProfiles(string selected)
    {
        var selectedProfile = config.Profiles.FirstOrDefault(p => p.Id == selected) ?? config.Profiles.FirstOrDefault();
        if (selectedProfile != null) config.SelectedId = selectedProfile.Id;
        else config.SelectedId = "";
        serverListView?.SetServers(config.Profiles, config.SelectedId);
        BindWorkspace();
    }

    private void BindWorkspace()
    {
        if (workspaceView == null) return;
        var profile = Current;
        (string Id, string Host, int Port, string User, string Auth, string KeyPath)? credentialIdentity = profile == null
            ? null
            : (profile.Id, profile.Host, profile.Port, profile.User, profile.Auth, profile.KeyPath);
        if (credentialIdentity != boundCredentialProfile)
        {
            workspaceView.ClearCredential();
            boundCredentialProfile = credentialIdentity;
        }
        var connected = session?.IsConnected == true;
        var canEdit = !busy && !connected && !configLoadFailed;
        workspaceView.Bind(profile, uiState, uiStateColor, busy, connected,
            profile?.Rules.ToList() ?? new List<PortRule>(), logHistory, canEdit);
        workspaceView.SetInteractionEnabled(canEdit);
        serverListView?.SetInteractionEnabled(canEdit);
    }

    private void SelectServer(string id)
    {
        if (!ResolveDraftNavigation()) return;
        if (busy || session?.IsConnected == true)
        {
            MessageBox.Show(this, "当前连接中。断开后可以切换服务器或编辑映射。", "PortBridge");
            serverListView?.SelectServer(config.SelectedId);
            return;
        }
        var profile = config.Profiles.FirstOrDefault(p => p.Id == id);
        if (profile == null) return;
        var oldSelectedId = config.SelectedId;
        config.SelectedId = profile.Id;
        if (!Persist())
        {
            config.SelectedId = oldSelectedId;
            RefreshProfiles(oldSelectedId);
            return;
        }
        RefreshProfiles(profile.Id);
        ShowWorkspace();
    }

    private void AddServer()
    {
        if (!ResolveDraftNavigation()) return;
        if (busy || session?.IsConnected == true) { ShowDisconnectToEditMessage(); return; }
        serverEditIsNew = true;
        serverEditSource = null;
        serverEditDraft = new ServerProfile { Name = "", Host = "", User = "", Rules = new() };
        serverEditorView!.LoadProfile(serverEditDraft, creating: true);
        ShowPage(serverEditorHost);
    }

    private void EditServer()
    {
        if (!ResolveDraftNavigation()) return;
        if (busy || session?.IsConnected == true) { ShowDisconnectToEditMessage(); return; }
        if (Current is not { } profile) return;
        serverEditIsNew = false;
        serverEditSource = profile;
        serverEditDraft = CloneProfile(profile);
        serverEditorView!.LoadProfile(serverEditDraft, creating: false);
        ShowPage(serverEditorHost);
    }

    private void AddMapping()
    {
        if (!ResolveDraftNavigation()) return;
        if (busy || session?.IsConnected == true) { ShowDisconnectToEditMessage(); return; }
        if (Current is not { } owner) return;
        ruleEditOwner = owner;
        ruleEditSource = null;
        ruleEditIsNew = true;
        ruleEditDraft = new PortRule();
        ruleEditorView!.LoadRule(ruleEditDraft, creating: true);
        ShowPage(ruleEditorHost);
    }

    private void EditMapping(PortRule rule)
    {
        if (!ResolveDraftNavigation()) return;
        if (busy || session?.IsConnected == true) { ShowDisconnectToEditMessage(); return; }
        if (Current is not { } owner) return;
        var source = owner.Rules.FirstOrDefault(x => ReferenceEquals(x, rule));
        if (source == null) return;
        ruleEditOwner = owner;
        ruleEditSource = source;
        ruleEditIsNew = false;
        ruleEditDraft = CloneRule(source);
        ruleEditorView!.LoadRule(ruleEditDraft, creating: false);
        ShowPage(ruleEditorHost);
    }

    private void DeleteServer()
    {
        if (!ResolveDraftNavigation()) return;
        if (busy || session?.IsConnected == true) { ShowDisconnectToEditMessage(); return; }
        if (Current is not { } profile || MessageBox.Show(this, $"删除服务器“{profile.Name}”？", "PortBridge", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var index = config.Profiles.IndexOf(profile);
        var oldSelectedId = config.SelectedId;
        config.Profiles.RemoveAt(index);
        var next = config.Profiles.FirstOrDefault();
        config.SelectedId = next?.Id ?? "";
        if (!Persist())
        {
            config.Profiles.Insert(index, profile);
            config.SelectedId = oldSelectedId;
            RefreshProfiles(oldSelectedId);
            return;
        }
        RefreshProfiles(next?.Id ?? "");
        ShowWorkspace();
    }

    private void CopyMapping(PortRule rule)
    {
        try
        {
            var endpoint = rule.LocalEndpoint();
            Clipboard.SetText(endpoint);
            WriteLog("已复制本机入口：" + endpoint);
        }
        catch (Exception e) { MessageBox.Show(this, "无法复制地址：" + e.Message, "PortBridge"); }
    }

    private void ShowDisconnectToEditMessage() => MessageBox.Show(this, "当前连接中。断开后可以切换服务器或编辑映射。", "PortBridge");
    private void ShowPage(Control page)
    {
        foreach (Control child in contentHost.Controls) child.Visible = false;
        page.Visible = true;
        page.BringToFront();
        activePage = page;
    }

    private void ShowWorkspace() => ShowPage(homeView);

    private bool ResolveDraftNavigation()
    {
        if (activePage == serverEditorHost)
        {
            if (serverEditorView?.IsDirty == true)
            {
                if (!Visible) ShowWindow();
                var choice = MessageBox.Show(this, "服务器编辑内容尚未保存。\n\n是：保存并继续\n否：放弃修改并继续\n取消：继续编辑", "未保存修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (choice == DialogResult.Cancel) return false;
                if (choice == DialogResult.Yes) return CommitServerDraft();
            }
            ClearServerDraft();
            return true;
        }
        if (activePage == ruleEditorHost)
        {
            if (ruleEditorView?.IsDirty == true)
            {
                if (!Visible) ShowWindow();
                var choice = MessageBox.Show(this, "服务映射编辑内容尚未保存。\n\n是：保存并继续\n否：放弃修改并继续\n取消：继续编辑", "未保存修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (choice == DialogResult.Cancel) return false;
                if (choice == DialogResult.Yes) return CommitRuleDraft();
            }
            ClearRuleDraft();
            return true;
        }
        return true;
    }

    private void CancelEditor()
    {
        if (activePage == serverEditorHost) ClearServerDraft();
        if (activePage == ruleEditorHost) ClearRuleDraft();
        ShowWorkspace();
    }

    private void BackFromEditor()
    {
        if (!ResolveDraftNavigation()) return;
        ShowWorkspace();
    }

    private void SaveServerDraft()
    {
        if (CommitServerDraft()) ShowWorkspace();
    }

    private bool CommitServerDraft()
    {
        if (serverEditDraft == null || serverEditorView == null) return false;
        var draft = CloneProfile(serverEditDraft);
        serverEditorView.ApplyTo(draft);
        try { draft.ValidateConnection(); }
        catch (Exception e) { MessageBox.Show(this, e.Message, "请检查服务器"); return false; }

        var oldSelected = config.SelectedId;
        var index = serverEditIsNew ? -1 : config.Profiles.FindIndex(p => p.Id == draft.Id);
        if (!serverEditIsNew && index < 0) { MessageBox.Show(this, "服务器已不存在，请重新打开编辑。", "PortBridge"); return false; }
        if (serverEditIsNew) config.Profiles.Add(draft);
        else config.Profiles[index] = draft;
        config.SelectedId = draft.Id;
        if (!Persist())
        {
            if (serverEditIsNew) config.Profiles.Remove(draft);
            else config.Profiles[index] = serverEditSource!;
            config.SelectedId = oldSelected;
            return false;
        }
        ClearServerDraft();
        RefreshProfiles(draft.Id);
        ShowWorkspace();
        return true;
    }

    private void SaveRuleDraft()
    {
        if (CommitRuleDraft()) ShowWorkspace();
    }

    private void DeleteRuleDraft()
    {
        if (ruleEditOwner == null || ruleEditSource == null || ruleEditIsNew) return;
        if (MessageBox.Show(this, $"删除服务映射“{ruleEditSource.Name}”？", "PortBridge", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        DeleteRuleDraftConfirmed();
    }

    private void DeleteRuleDraftConfirmed()
    {
        if (ruleEditOwner == null || ruleEditSource == null || ruleEditIsNew) return;
        var index = ruleEditOwner.Rules.IndexOf(ruleEditSource);
        if (index < 0) return;
        ruleEditOwner.Rules.RemoveAt(index);
        var oldSelected = config.SelectedId;
        config.SelectedId = ruleEditOwner.Id;
        if (!Persist())
        {
            ruleEditOwner.Rules.Insert(index, ruleEditSource);
            config.SelectedId = oldSelected;
            return;
        }
        var ownerId = ruleEditOwner.Id;
        ClearRuleDraft();
        RefreshProfiles(ownerId);
        ShowWorkspace();
    }

    private bool CommitRuleDraft()
    {
        if (ruleEditOwner == null || ruleEditDraft == null || ruleEditorView == null) return false;
        var draft = CloneRule(ruleEditDraft);
        ruleEditorView.ApplyTo(draft);
        try
        {
            draft.Validate();
            if (draft.Enabled && ruleEditOwner.Rules.Any(r => !ReferenceEquals(r, ruleEditSource) && r.Enabled && r.LocalPort == draft.LocalPort))
                throw new InvalidOperationException("已启用映射的本地端口不能重复。");
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "请检查服务映射"); return false; }

        var oldSelected = config.SelectedId;
        var index = ruleEditIsNew ? -1 : ruleEditOwner.Rules.IndexOf(ruleEditSource!);
        if (!ruleEditIsNew && index < 0) { MessageBox.Show(this, "服务映射已不存在，请重新打开编辑。", "PortBridge"); return false; }
        if (ruleEditIsNew) ruleEditOwner.Rules.Add(draft);
        else ruleEditOwner.Rules[index] = draft;
        config.SelectedId = ruleEditOwner.Id;
        if (!Persist())
        {
            if (ruleEditIsNew) ruleEditOwner.Rules.Remove(draft);
            else ruleEditOwner.Rules[index] = ruleEditSource!;
            config.SelectedId = oldSelected;
            return false;
        }
        var ownerId = ruleEditOwner.Id;
        ClearRuleDraft();
        RefreshProfiles(ownerId);
        ShowWorkspace();
        return true;
    }

    private void ClearServerDraft()
    {
        serverEditSource = null; serverEditDraft = null; serverEditIsNew = false;
    }

    private void ClearRuleDraft()
    {
        ruleEditOwner = null; ruleEditSource = null; ruleEditDraft = null; ruleEditIsNew = false;
    }

    private static ServerProfile CloneProfile(ServerProfile source) => new()
    {
        Id = source.Id, Name = source.Name, Host = source.Host, Port = source.Port, User = source.User,
        Auth = source.Auth, KeyPath = source.KeyPath,
        Rules = new System.ComponentModel.BindingList<PortRule>((source.Rules ?? new()).Select(CloneRule).ToList())
    };

    private static PortRule CloneRule(PortRule source) => new()
    {
        Enabled = source.Enabled, Name = source.Name, LocalPort = source.LocalPort,
        TargetHost = source.TargetHost, TargetPort = source.TargetPort, Protocol = source.Protocol, Path = source.Path
    };
    private bool Persist()
    {
        if (configLoadFailed) return false;
        try { store.Save(config); return true; }
        catch (Exception e) { MessageBox.Show(this, "无法保存配置：" + e.Message, "PortBridge"); return false; }
    }

    private void InitializeSettingsControls()
    {
        try
        {
            config.StartWithWindows = startupManager.IsEnabled;
            autoStartToggle.Checked = config.StartWithWindows;
        }
        catch (Exception e)
        {
            autoStartToggle.Checked = false;
            autoStartToggle.Enabled = false;
            startupMessage.Text = "无法读取登录启动状态：" + e.Message;
            startupMessage.ForeColor = Theme.Error;
        }
        autoUpdateToggle.Checked = config.CheckUpdatesOnStartup;
        if (string.IsNullOrEmpty(startupMessage.Text)) startupMessage.Text = config.StartWithWindows ? "已启用，登录后将缩至托盘。" : "已关闭。";

        settingsReady = true;
        autoStartToggle.CheckedChanged += (_, _) =>
        {
            if (!settingsReady || suppressSettingsEvents) return;
            var previous = config.StartWithWindows;
            try
            {
                startupManager.SetEnabled(autoStartToggle.Checked);
                config.StartWithWindows = autoStartToggle.Checked;
                if (!Persist())
                {
                    startupManager.SetEnabled(previous);
                    config.StartWithWindows = previous;
                    suppressSettingsEvents = true;
                    autoStartToggle.Checked = previous;
                    suppressSettingsEvents = false;
                    return;
                }
                startupMessage.ForeColor = Theme.Muted;
                startupMessage.Text = previous == config.StartWithWindows
                    ? "设置已保存。"
                    : config.StartWithWindows ? "已启用，登录后将缩至托盘。" : "已关闭。";
            }
            catch (Exception e)
            {
                config.StartWithWindows = previous;
                suppressSettingsEvents = true;
                autoStartToggle.Checked = previous;
                suppressSettingsEvents = false;
                startupMessage.ForeColor = Theme.Error;
                startupMessage.Text = "无法更新登录启动设置：" + e.Message;
            }
        };
        autoUpdateToggle.CheckedChanged += (_, _) =>
        {
            if (!settingsReady || suppressSettingsEvents) return;
            var previous = config.CheckUpdatesOnStartup;
            config.CheckUpdatesOnStartup = autoUpdateToggle.Checked;
            if (!Persist())
            {
                config.CheckUpdatesOnStartup = previous;
                suppressSettingsEvents = true;
                autoUpdateToggle.Checked = previous;
                suppressSettingsEvents = false;
            }
        };
        checkUpdatesButton.Click += async (_, _) => await CheckForUpdatesAsync();
        openUpdateButton.Click += (_, _) => OpenUpdate();
    }

    private static Version CurrentVersion
    {
        get
        {
            var info = typeof(MainForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (info != null && Version.TryParse(info.Split('+')[0], out var parsed)) return parsed;
            return new Version(0, 3, 0);
        }
    }

    private static string DisplayCurrentVersion() => UpdateCheckService.DisplayVersion(CurrentVersion);

    private async Task CheckForUpdatesAsync()
    {
        if (updateCheckRunning || IsDisposed) return;
        updateCheckRunning = true;
        checkUpdatesButton.Enabled = false;
        updateStatus.ForeColor = Theme.Muted;
        updateStatus.Text = "正在检查 GitHub 最新稳定版…";
        openUpdateButton.Visible = false;
        try
        {
            var result = await updateService.CheckAsync(CurrentVersion, appCancellation.Token);
            if (IsDisposed || exiting || appCancellation.IsCancellationRequested) return;
            updateStatus.Text = result.Message;
            updateStatus.ForeColor = result.Status == UpdateCheckStatus.Failed ? Theme.Error
                : result.Status == UpdateCheckStatus.UpdateAvailable ? Theme.Accent : Theme.Muted;
            availableRelease = result.Status == UpdateCheckStatus.UpdateAvailable ? result.Release : null;
            openUpdateButton.Visible = availableRelease != null;
            serverListView?.SetUpdateNotice(availableRelease != null);
        }
        catch (OperationCanceledException) when (appCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception)
        {
            if (IsDisposed || exiting) return;
            updateStatus.Text = "更新检查失败，请稍后重试。";
            updateStatus.ForeColor = Theme.Error;
            availableRelease = null;
            openUpdateButton.Visible = false;
            serverListView?.SetUpdateNotice(false);
        }
        finally
        {
            updateCheckRunning = false;
            if (!IsDisposed)
            {
                updateCheckRunning = false;
                checkUpdatesButton.Enabled = true;
            }
        }
    }

    private void OpenUpdate()
    {
        var target = availableRelease == null ? null : availableRelease.SetupAsset ?? availableRelease.ReleasePage;
        if (target == null || target.Scheme != Uri.UriSchemeHttps || !target.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return;
        try { Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception e) { updateStatus.Text = "无法打开下载页面：" + e.Message; updateStatus.ForeColor = Theme.Error; }
    }

    private void ShowSettingsPage(bool showSettings)
    {
        if (showSettings)
        {
            if (!ResolveDraftNavigation()) return;
            ShowPage(settingsView);
        }
        else ShowWorkspace();
    }

    private async Task Connect(string credential)
    {
        if (busy || session != null || Current is not { } p || configLoadFailed) return;
        try { p.Validate(); } catch (Exception e) { MessageBox.Show(this, e.Message, "请检查配置"); return; }
        if (!Persist()) return;
        busy = true; SetEditing(false); SetStatus("连接中…", Color.FromArgb(245, 185, 72));
        connecting = new CancellationTokenSource();
        var candidate = new TunnelSession();
        candidate.Error += message => Ui(() => WriteLog("连接提示：" + message));
        try
        {
            await candidate.ConnectAsync(p, credential, TrustHost, connecting.Token);
            if (exiting) { candidate.Dispose(); return; }
            session = candidate;
            SetStatus("已连接", Theme.Accent);
            WriteLog("隧道已建立：" + string.Join("；", p.Rules.Where(r => r.Enabled).Select(r => r.Name + " → 127.0.0.1:" + r.LocalPort)));
            WriteLog("目标服务是否可用，请打开对应网页或客户端验证。");
        }
        catch (Exception e)
        {
            candidate.Dispose(); SetStatus("连接失败", Theme.Error);
            WriteLog("连接失败：" + e.Message);
            if (!exiting) MessageBox.Show(this, e.Message + "\n\n请检查 SSH 地址、认证、转发权限及本地端口占用。", "连接失败");
        }
        finally
        {
            credential = ""; workspaceView?.ClearCredential(); busy = false; connecting.Dispose(); connecting = null;
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
        session?.Dispose(); session = null; SetEditing(true); SetStatus("未连接", Theme.Muted); WriteLog("隧道已断开。");
    }
    private void SetEditing(bool enabled)
    {
        BindWorkspace();
    }
    private void SetStatus(string message, Color color)
    {
        uiState = message; uiStateColor = color;
        BindWorkspace();
        if (tray != null) tray.Text = ("PortBridge · " + message)[..Math.Min(63, ("PortBridge · " + message).Length)];
    }
    private void OpenService(PortRule rule)
    {
        if (session?.IsConnected != true) { MessageBox.Show(this, "请先建立连接。", "PortBridge"); return; }
        if (!rule.Enabled || rule.Protocol == "tcp") return;
        try { Process.Start(new ProcessStartInfo(rule.BrowserUrl()) { UseShellExecute = true }); }
        catch (Exception e) { MessageBox.Show(this, e.Message, "打开服务"); }
    }
    private void WriteLog(string message)
    {
        var line = DateTime.Now.ToString("HH:mm:ss") + "  " + message.Replace("\r", " ").Replace("\n", " ");
        logHistory.Add(line);
        if (logHistory.Count > 250) logHistory.RemoveRange(0, logHistory.Count - 200);
        BindWorkspace();
    }
    private void Ui(Action action) { if (!IsDisposed && IsHandleCreated) BeginInvoke(action); }
    private void ShowWindow() { if (!Visible) Show(); WindowState = FormWindowState.Normal; Activate(); }
    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.WindowsShutDown) { exiting = true; appCancellation.Cancel(); }
        if (!exiting)
        {
            if (!configLoadFailed) Persist();
            e.Cancel = true; Hide();
            if (!toldTray) { tray.ShowBalloonTip(3000, "PortBridge 仍在运行", "从右下角托盘打开；选择“退出”可停止转发。", ToolTipIcon.Info); toldTray = true; }
        }
    }
    private void ExitApp()
    {
        if (!ResolveDraftNavigation()) return;
        if (!configLoadFailed && !busy) Persist();
        exiting = true; connecting?.Cancel(); appCancellation.Cancel();
        if (!busy) Close();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true;
            timer.Dispose(); session?.Dispose(); tray?.Dispose(); connecting?.Dispose();
            appCancellation.Cancel(); appCancellation.Dispose();
            if (ownsUpdateService) updateService.Dispose();
            appIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            var enabled = 1;
            if (DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));
            var borderColor = ToColorRef(Theme.Line);
            var captionColor = ToColorRef(Theme.Background);
            var textColor = ToColorRef(Theme.Ink);
            DwmSetWindowAttribute(Handle, DwmwaBorderColor, ref borderColor, sizeof(int));
            DwmSetWindowAttribute(Handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
            DwmSetWindowAttribute(Handle, DwmwaTextColor, ref textColor, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Program.RestoreWindowMessage)
        {
            ShowWindow();
            return;
        }
        base.WndProc(ref m);
    }

    private static Icon LoadApplicationIcon()
    {
        var resource = typeof(MainForm).Assembly.GetManifestResourceNames()
            .FirstOrDefault(x => x.EndsWith("assets.PortBridge.ico", StringComparison.OrdinalIgnoreCase));
        if (resource != null)
        {
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream(resource);
            if (stream != null)
            {
                using var loaded = new Icon(stream);
                return (Icon)loaded.Clone();
            }
        }
        return (Icon)SystemIcons.Application.Clone();
    }

    private void ExportProfile()
    {
        if (configLoadFailed || Current == null) return;
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
        if (!ResolveDraftNavigation() || configLoadFailed) return;
        using var d = new OpenFileDialog { Filter = "PortBridge 配置 (*.json)|*.json" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var p = JsonSerializer.Deserialize<ServerProfile>(File.ReadAllText(d.FileName)) ?? throw new InvalidDataException("配置为空。");
            if (p.Rules == null || p.Rules.Any(r => r == null)) throw new InvalidDataException("转发规则格式错误。");
            var oldSelectedId = config.SelectedId;
            var index = config.Profiles.Count;
            p.Id = Guid.NewGuid().ToString("N");
            config.Profiles.Insert(index, p);
            config.SelectedId = p.Id;
            if (!Persist())
            {
                config.Profiles.RemoveAt(index);
                config.SelectedId = oldSelectedId;
                RefreshProfiles(oldSelectedId);
                return;
            }
            RefreshProfiles(p.Id);
            ShowWorkspace();
            WriteLog("已导入。请检查主机、认证和规则，然后手动连接。");
        }
        catch (Exception e) { MessageBox.Show(this, e.Message, "导入失败"); }
    }
}
