using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;
using PortBridge;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception(message);
    }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (InvalidOperationException) { assertions++; return; }
        throw new Exception(message);
    }

    private static T Field<T>(object instance, string name) =>
        (T)(instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(instance)
            ?? throw new Exception($"Missing field {name} on {instance.GetType().Name}"));

    private static IEnumerable<Control> Descendants(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
            foreach (var descendant in Descendants(child)) yield return descendant;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            ApplicationConfiguration.Initialize();
            ModelTests();
            StartupManagerTests();
            UpdateServiceTests().GetAwaiter().GetResult();
            if (args.Contains("--integration")) Integration().GetAwaiter().GetResult();
            ProfileSwitchTests();
            EditorFlowTests();
            WorkspaceActionTests();
            CorruptConfigProtectionTests();
            FormDisposalTests();
            if (args.Contains("--render")) Render();
            if (args.Contains("--preview")) Preview();
            Console.WriteLine($"PASS: {assertions} assertions"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static void ModelTests()
    {
        var p = new ServerProfile { Name = "Test", Host = "example.invalid", User = "tester", Rules = new() { new PortRule() } };
        p.Validate();
        var connectionOnly = new ServerProfile { Name = "No rules yet", Host = "example.invalid", User = "tester", Rules = new() };
        connectionOnly.ValidateConnection();
        Reject(connectionOnly.Validate, "Connect validation accepted a profile without an enabled rule");
        p.Rules.Add(new PortRule()); Reject(p.Validate, "Duplicate local port accepted");
        p.Rules[1].Enabled = false; p.Validate();
        p.Rules[0].TargetPort = 0; Reject(p.Validate, "Invalid port accepted"); p.Rules[0].TargetPort = 8080;
        var rule = p.Rules[0]; rule.Path = "/vnc.html?path=websockify&autoconnect=1";
        Check(rule.BrowserUrl() == "http://127.0.0.1:18080/vnc.html?path=websockify&autoconnect=1", "URL changed");
        Check(rule.LocalEndpoint() == "http://127.0.0.1:18080/vnc.html?path=websockify&autoconnect=1", "Copyable web endpoint lost its query string");
        rule.Path = "//example.com"; Reject(() => rule.BrowserUrl(), "External URL accepted");
        rule.Path = "/\\example.com"; Reject(() => rule.BrowserUrl(), "Backslash URL accepted");
        rule.Protocol = "tcp"; Reject(() => rule.BrowserUrl(), "TCP opened as HTTP");
        rule.Path = "/"; rule.Protocol = "http";
        rule.Path = "//invalid"; Reject(rule.Validate, "Invalid HTTP path passed rule validation");
        rule.Path = "/";
        var dir = Path.Combine(Path.GetTempPath(), "PortBridge-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConfigStore(dir);
            var c = new AppConfig { Profiles = new() { p } };
            store.Save(c);
            Check(store.Load().Profiles[0].Rules.Count == 2, "Rules not persisted");
            store.Save(c);
            using var json = JsonDocument.Parse(File.ReadAllText(store.FilePath));
            var storedProfile = json.RootElement.GetProperty("Profiles")[0];
            Check(!storedProfile.EnumerateObject().Any(x => x.Name is "Password" or "Secret" or "Passphrase"), "Secret field persisted");
            File.WriteAllText(store.FilePath, "{\"Profiles\":[],\"SelectedId\":\"\",\"TrustedHosts\":{}}");
            var legacy = store.Load();
            Check(!legacy.StartWithWindows && legacy.CheckUpdatesOnStartup, "Legacy settings defaults changed");
            File.WriteAllText(store.FilePath, "invalid-json");
            try { store.Load(); throw new Exception("Invalid configuration accepted"); } catch (JsonException) { assertions++; }
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    private static void StartupManagerTests()
    {
        var subKey = $@"Software\PortBridge.Tests\{Guid.NewGuid():N}\Run";
        var exe = @"C:\Program Files\PortBridge\PortBridge.exe";
        try
        {
            var manager = new WindowsStartupManager(subKey, () => exe);
            Check(!manager.IsEnabled, "Startup entry should start disabled");
            manager.SetEnabled(true);
            using (var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false))
            {
                var command = key?.GetValue(WindowsStartupManager.ValueName) as string;
                Check(command == "\"C:\\Program Files\\PortBridge\\PortBridge.exe\" --startup", "Startup command was not quoted or missing startup switch");
            }
            Check(manager.IsEnabled, "Startup manager did not recognize its command");
            manager.SetEnabled(false);
            Check(!manager.IsEnabled, "Startup entry was not removed");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(subKey[..subKey.LastIndexOf('\\')], throwOnMissingSubKey: false); }
    }

    private static void FormDisposalTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PortBridge-dispose-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var form = new MainForm(new ConfigStore(dir), new MemoryStartupManager(), allowStartupUpdateCheck: false);
            using var closeTimer = new System.Windows.Forms.Timer { Interval = 80 };
            closeTimer.Tick += (_, _) =>
            {
                closeTimer.Stop();
                typeof(MainForm).GetMethod("ExitApp", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            };
            closeTimer.Start();
            Application.Run(form);
            form.Dispose();
            form.Dispose();
            Check(form.IsDisposed, "Exit did not leave the form disposed after repeated Dispose calls");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    private static void ProfileSwitchTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PortBridge-profile-switch-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = new ServerProfile { Name = "First", Host = "first.example", User = "tester", Port = 2022, Rules = new() { new PortRule() } };
            var second = new ServerProfile { Name = "Second", Host = "second.example", User = "tester", Port = 2200, Rules = new() { new PortRule { LocalPort = 18081, TargetPort = 8081 } } };
            var store = new ConfigStore(dir);
            store.Save(new AppConfig { Profiles = new() { first, second }, SelectedId = first.Id });
            using var form = new MainForm(store, new MemoryStartupManager(), allowStartupUpdateCheck: false);
            var config = Field<AppConfig>(form, "config");
            var select = typeof(MainForm).GetMethod("SelectServer", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var workspace = Field<object>(form, "workspaceView");
            var title = Field<Label>(workspace, "title");
            var homePage = Field<Control>(form, "homeView");
            var settingsPage = Field<Control>(form, "settingsView");
            var activePage = () => Field<Control>(form, "activePage");
            var credential = Field<TextBox>(workspace, "credential");
            var serverList = Field<object>(form, "serverListView");
            var rowList = Field<FlowLayoutPanel>(serverList, "serverList");
            int SelectedRows() => rowList.Controls.Cast<Control>().Count(row =>
                row.GetType().GetProperty("Selected", BindingFlags.Instance | BindingFlags.Public)?.GetValue(row) as bool? == true);
            Check(title.Text == "First", "Initial saved server was not selected in the workspace");
            Check(SelectedRows() == 1, "Server list did not show one selected server");
            credential.Text = "first-server-secret";
            select.Invoke(form, new object[] { second.Id });
            Check(config.SelectedId == second.Id && title.Text == "Second" && SelectedRows() == 1 && ReferenceEquals(activePage(), homePage), "Switching servers did not route/highlight the second workspace");
            Check(credential.Text.Length == 0, "Switching servers retained the previous server's unsaved credential");
            typeof(MainForm).GetMethod("ShowSettingsPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { true });
            Check(ReferenceEquals(activePage(), settingsPage), "Settings did not become the active page");
            credential.Text = "second-server-secret";
            select.Invoke(form, new object[] { first.Id });
            Check(config.SelectedId == first.Id && title.Text == "First" && ReferenceEquals(activePage(), homePage)
                && credential.Text.Length == 0, "Selecting a server from Settings did not return to its workspace or clear credentials");

            credential.Text = "credential-before-endpoint-change";
            typeof(MainForm).GetMethod("EditServer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            var editor = Field<object>(form, "serverEditorView");
            Field<TextBox>(editor, "host").Text = "first-updated.example";
            Field<ComboBox>(editor, "auth").SelectedIndex = 1;
            var testKeyPath = Path.Combine(dir, "test-key"); File.WriteAllText(testKeyPath, "temporary test key placeholder");
            Field<TextBox>(editor, "keyPath").Text = testKeyPath;
            typeof(MainForm).GetMethod("SaveServerDraft", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            Check(config.Profiles[0].Host == "first-updated.example" && config.Profiles[0].Auth == "key"
                && credential.Text.Length == 0, "Saving server host/auth changes retained the previous connection credential");

            typeof(MainForm).GetMethod("EditServer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            Field<TextBox>(Field<object>(form, "serverEditorView"), "name").Text = "Discarded editor draft";
            typeof(MainForm).GetMethod("CancelEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            select.Invoke(form, new object[] { second.Id });
            Check(title.Text == "Second" && ReferenceEquals(activePage(), homePage), "Selecting a server after canceling an editor did not enter its workspace");
            select.Invoke(form, new object[] { first.Id });
            Check(config.SelectedId == first.Id && title.Text == "First" && ReferenceEquals(activePage(), homePage), "Switching back did not restore the first workspace");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    private static void EditorFlowTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PortBridge-editor-flow-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConfigStore(dir);
            using var form = new MainForm(store, new MemoryStartupManager(), allowStartupUpdateCheck: false);
            var config = Field<AppConfig>(form, "config");
            var workspace = Field<object>(form, "workspaceView");
            Check(config.Profiles.Count == 0 && (bool)workspace.GetType().GetProperty("IsShowingEmptyWorkspace", BindingFlags.Instance | BindingFlags.Public)!.GetValue(workspace)!,
                "First-run state created a placeholder server instead of showing the empty state");
            var addServer = typeof(MainForm).GetMethod("AddServer", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var addMapping = typeof(MainForm).GetMethod("AddMapping", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var cancel = typeof(MainForm).GetMethod("CancelEditor", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var saveServer = typeof(MainForm).GetMethod("SaveServerDraft", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var saveMapping = typeof(MainForm).GetMethod("SaveRuleDraft", BindingFlags.Instance | BindingFlags.NonPublic)!;

            addServer.Invoke(form, null);
            Check(config.Profiles.Count == 0, "Opening a new server editor changed configuration before Save");
            var firstEditor = Field<object>(form, "serverEditorView");
            Field<TextBox>(firstEditor, "name").Text = "Discarded draft";
            cancel.Invoke(form, null);
            Check(config.Profiles.Count == 0 && !File.Exists(store.FilePath), "Explicit server Cancel wrote its draft");

            addServer.Invoke(form, null);
            var serverEditor = Field<object>(form, "serverEditorView");
            Field<TextBox>(serverEditor, "name").Text = "开发节点";
            Field<TextBox>(serverEditor, "host").Text = "dev.example.com";
            Field<TextBox>(serverEditor, "user").Text = "developer";
            Field<NumericUpDown>(serverEditor, "port").Value = 2222;
            saveServer.Invoke(form, null);
            Check(config.Profiles.Count == 1 && config.Profiles[0].Host == "dev.example.com" && config.Profiles[0].Port == 2222,
                "Saving the server editor did not commit its draft");
            Check(store.Load().Profiles.Count == 1, "Saved server draft was not persisted");

            addMapping.Invoke(form, null);
            Check(config.Profiles[0].Rules.Count == 0, "Opening a new rule editor changed the profile");
            Check(!Field<bool>(Field<object>(form, "ruleEditorView"), "canDelete"), "New mapping editor exposed deletion");
            cancel.Invoke(form, null);
            Check(config.Profiles[0].Rules.Count == 0, "Explicit rule Cancel changed the profile");

            addMapping.Invoke(form, null);
            var ruleEditor = Field<object>(form, "ruleEditorView");
            Field<TextBox>(ruleEditor, "name").Text = "noVNC";
            Field<NumericUpDown>(ruleEditor, "localPort").Value = 15900;
            Field<TextBox>(ruleEditor, "targetHost").Text = "10.0.0.18";
            Field<NumericUpDown>(ruleEditor, "targetPort").Value = 5900;
            Field<ComboBox>(ruleEditor, "protocol").SelectedIndex = 0;
            Field<TextBox>(ruleEditor, "path").Text = "/vnc.html?path=websockify&autoconnect=1";
            saveMapping.Invoke(form, null);
            Check(config.Profiles[0].Rules.Count == 1 && config.Profiles[0].Rules[0].BrowserUrl().EndsWith("/vnc.html?path=websockify&autoconnect=1", StringComparison.Ordinal),
                "Saving the rule draft lost its complete local entry");
            Check(store.Load().Profiles[0].Rules.Count == 1, "Saved rule draft was not persisted");

            var savedProfile = config.Profiles[0];
            var savedRule = savedProfile.Rules[0];
            typeof(MainForm).GetMethod("EditServer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            serverEditor = Field<object>(form, "serverEditorView");
            Field<TextBox>(serverEditor, "host").Text = "changed.example.com";
            cancel.Invoke(form, null);
            Check(savedProfile.Host == "dev.example.com", "Canceling server edits mutated the saved profile");

            typeof(MainForm).GetMethod("EditMapping", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { savedRule });
            ruleEditor = Field<object>(form, "ruleEditorView");
            Check((bool)ruleEditor.GetType().GetProperty("CanDelete", BindingFlags.Instance | BindingFlags.Public)!.GetValue(ruleEditor)!,
                "Existing mapping editor did not expose deletion");
            Field<TextBox>(ruleEditor, "name").Text = "unsaved name";
            cancel.Invoke(form, null);
            Check(savedRule.Name == "noVNC", "Canceling rule edits mutated the saved mapping");

            typeof(MainForm).GetMethod("EditMapping", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { savedRule });
            typeof(MainForm).GetMethod("DeleteRuleDraftConfirmed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            Check(config.Profiles.Count == 1 && config.Profiles[0].Rules.Count == 0, "Deleting the last mapping removed its server or kept the rule");
            Check(store.Load().Profiles[0].Rules.Count == 0, "Deleting the mapping was not persisted");
            var workspaceAfterDelete = Field<object>(form, "workspaceView");
            Check((bool)workspaceAfterDelete.GetType().GetProperty("IsShowingNoMappings", BindingFlags.Instance | BindingFlags.Public)!.GetValue(workspaceAfterDelete)!,
                "Deleting the last mapping did not return to the empty mapping state");

            addMapping.Invoke(form, null);
            var page = Field<Control>(form, "activePage");
            var hiddenRuleEditor = Field<object>(form, "ruleEditorView");
            Field<TextBox>(hiddenRuleEditor, "name").Text = "unsaved hidden draft";
            form.Hide();
            Check(!form.Visible && ReferenceEquals(page, Field<Control>(form, "activePage")) && page == Field<Control>(form, "ruleEditorHost")
                && (bool)hiddenRuleEditor.GetType().GetProperty("IsDirty", BindingFlags.Instance | BindingFlags.Public)!.GetValue(hiddenRuleEditor)!,
                "Hiding the window lost the active unsaved editor route");
            cancel.Invoke(form, null);
            Check(config.Profiles[0].Rules.Count == 0, "Discarding a hidden new mapping changed configuration");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    private static void WorkspaceActionTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PortBridge-workspace-actions-" + Guid.NewGuid().ToString("N"));
        try
        {
            var profile = new ServerProfile
            {
                Name = "Actions", Host = "actions.example", User = "tester", Rules = new()
                {
                    new PortRule { Name = "Web", LocalPort = 18080, TargetPort = 8080 },
                    new PortRule { Name = "Disabled", Enabled = false, LocalPort = 18081, TargetPort = 8081 },
                    new PortRule { Name = "TCP", Enabled = false, LocalPort = 15900, TargetPort = 5900, Protocol = "tcp" },
                    new PortRule { Name = "Broken URL", LocalPort = 18082, TargetPort = 8082, Path = "//old-invalid" }
                }
            };
            var store = new ConfigStore(dir);
            store.Save(new AppConfig { Profiles = new() { profile }, SelectedId = profile.Id });
            using var form = new MainForm(store, new MemoryStartupManager(), allowStartupUpdateCheck: false);
            form.Show(); Application.DoEvents();
            var workspace = Field<object>(form, "workspaceView");
            var bind = workspace.GetType().GetMethod("Bind", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            void BindRules(bool connected, bool editable, IReadOnlyList<PortRule> rules) => bind.Invoke(workspace, new object[]
            {
                profile, connected ? "已连接" : "未连接", connected ? Color.LimeGreen : Color.Gray,
                false, connected, rules, new List<string>(), editable
            });
            void Bind(bool connected, bool editable) => BindRules(connected, editable, profile.Rules.ToList());
            FlowLayoutPanel Rows() => Field<FlowLayoutPanel>(workspace, "rowsHost");
            Control RowFor(PortRule rule) => Rows().Controls.Cast<Control>().First(row =>
                ReferenceEquals(Field<PortRule>(row, "rule"), rule));
            Button ButtonFor(Control row, string text) => Descendants(row).OfType<Button>().First(button => button.Text == text);

            var emptyMapping = Field<Control>(workspace, "emptyMapping");
            var noMappings = Field<Label>(workspace, "noMappings");
            var noMappingsAdd = Field<Button>(workspace, "noMappingsAdd");
            BindRules(connected: false, editable: true, Array.Empty<PortRule>()); Application.DoEvents();
            Check(emptyMapping.Visible && noMappings.Visible && noMappingsAdd.Visible && noMappings.Text.Contains("启用")
                && noMappingsAdd.Text == "添加服务映射", "Empty mapping state did not show its guidance and add action");
            BindRules(connected: false, editable: true, profile.Rules.Where(rule => !rule.Enabled).ToList());
            var connectButton = Field<Button>(workspace, "connectButton");
            Check(!connectButton.Enabled && (connectButton.AccessibleDescription ?? "").Contains("启用"),
                "Connect remained available without an enabled mapping or guidance");

            Bind(connected: false, editable: true);
            Check(!ButtonFor(RowFor(profile.Rules[0]), "打开").Enabled, "Web Open was enabled before connection");
            Check(ButtonFor(RowFor(profile.Rules[0]), "复制").Enabled, "HTTP entry copy was not available while disconnected");
            Check(ButtonFor(RowFor(profile.Rules[1]), "复制").Enabled, "Disabled HTTP mapping lost copy access");
            Check(ButtonFor(RowFor(profile.Rules[2]), "复制地址").Enabled, "Disabled TCP mapping lost copy access");

            Bind(connected: true, editable: false);
            Check(ButtonFor(RowFor(profile.Rules[0]), "打开").Enabled, "Connected enabled Web rule did not expose Open");
            Check(!ButtonFor(RowFor(profile.Rules[0]), "编辑").Enabled, "Connected mapping remained editable");
            Check(!ButtonFor(RowFor(profile.Rules[1]), "打开").Enabled, "Disabled Web rule exposed Open");

            Bind(connected: false, editable: true);
            var brokenRow = RowFor(profile.Rules[3]);
            Check(Descendants(brokenRow).OfType<Label>().Any(label => label.Text == "网页路径需要检查"), "Legacy invalid path did not show local guidance");
            Check(!ButtonFor(brokenRow, "打开").Enabled && ButtonFor(brokenRow, "编辑").Enabled,
                "Invalid legacy path blocked editing or left Open enabled");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    private static void CorruptConfigProtectionTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PortBridge-config-protection-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConfigStore(dir);
            store.Save(new AppConfig());
            using var form = new MainForm(store, new MemoryStartupManager(), allowStartupUpdateCheck: false);
            const string unreadable = "invalid-original-settings";
            File.WriteAllText(store.FilePath, unreadable);
            typeof(MainForm).GetField("configLoadFailed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
            typeof(MainForm).GetMethod("ExitApp", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            Check(File.ReadAllText(store.FilePath) == unreadable, "Exit path replaced unreadable settings with empty configuration");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    private static async Task UpdateServiceTests()
    {
        var newer = """
        [{"tag_name":"v0.4.1","draft":false,"prerelease":false,"html_url":"https://github.com/Nortea575/PortBridge/releases/tag/v0.4.1","assets":[
          {"name":"PortBridge-0.4.1-Setup-arm64.exe","browser_download_url":"https://github.com/Nortea575/PortBridge/releases/download/v0.4.1/PortBridge-0.4.1-Setup-arm64.exe"},
          {"name":"PortBridge-0.4.1-Setup-x64.exe","browser_download_url":"https://github.com/Nortea575/PortBridge/releases/download/v0.4.1/PortBridge-0.4.1-Setup-x64.exe"},
          {"name":"PortBridge-0.4.1-Setup-x86.exe","browser_download_url":"https://github.com/Nortea575/PortBridge/releases/download/v0.4.1/PortBridge-0.4.1-Setup-x86.exe"}
        ]}]
        """;
        var available = await CheckRelease(newer);
        Check(available.Status == UpdateCheckStatus.UpdateAvailable, "New stable release was not found");
        Check(available.Message.Contains("0.4.1", StringComparison.Ordinal), "Update version display was not normalized");
        Check(available.UpdateUri?.AbsoluteUri.EndsWith("PortBridge-0.4.1-Setup-x64.exe", StringComparison.Ordinal) == true, "Wrong setup architecture was selected");

        var olderAndPrerelease = """
        [
          {"tag_name":"v99.0.0-beta","draft":false,"prerelease":true,"html_url":"https://github.com/Nortea575/PortBridge/releases/tag/v99.0.0-beta","assets":[]},
          {"tag_name":"v0.2.0","draft":false,"prerelease":false,"html_url":"https://github.com/Nortea575/PortBridge/releases/tag/v0.2.0","assets":[]}
        ]
        """;
        var current = await CheckRelease(olderAndPrerelease);
        Check(current.Status == UpdateCheckStatus.UpToDate, "Older or prerelease release produced a false update alert");
        Check(current.UpdateUri == null, "Up-to-date response unexpectedly exposed a download link");

        var noX64 = """
        [{"tag_name":"v0.4.0","draft":false,"prerelease":false,"html_url":"https://github.com/Nortea575/PortBridge/releases/tag/v0.4.0","assets":[
          {"name":"PortBridge-0.4.0-Setup-arm64.exe","browser_download_url":"https://github.com/Nortea575/PortBridge/releases/download/v0.4.0/PortBridge-0.4.0-Setup-arm64.exe"}
        ]}]
        """;
        var fallback = await CheckRelease(noX64);
        Check(fallback.Status == UpdateCheckStatus.UpdateAvailable && fallback.UpdateUri?.AbsoluteUri == "https://github.com/Nortea575/PortBridge/releases/tag/v0.4.0", "Missing x64 Setup did not fall back to the release page");

        var malformed = await CheckRelease("[{\"tag_name\":\"v1.2.3-beta\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://github.com/Nortea575/PortBridge/releases/tag/v1.2.3-beta\"}]");
        Check(malformed.Status == UpdateCheckStatus.Failed, "Malformed stable release tag was accepted");
        var badJson = await CheckRelease("not json");
        Check(badJson.Status == UpdateCheckStatus.Failed, "Malformed JSON did not produce a retryable failure");

        foreach (var status in new[] { HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests })
        {
            var limited = await CheckResponse(new HttpResponseMessage(status));
            Check(limited.Status == UpdateCheckStatus.Failed, $"HTTP {(int)status} was not reported as retryable failure");
        }
        using (var offlineClient = new HttpClient(new ResponseHandler((_, _) => throw new HttpRequestException("offline"))))
        using (var service = TestUpdateService(offlineClient))
        {
            var offline = await service.CheckAsync(new Version(0, 3, 0));
            Check(offline.Status == UpdateCheckStatus.Failed && offline.Message.Contains("重试", StringComparison.Ordinal), "Offline failure did not offer retry guidance");
        }

        using (var slowClient = new HttpClient(new ResponseHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BlockingReadStream()) }))))
        using (var service = TestUpdateService(slowClient, TimeSpan.FromMilliseconds(120)))
        {
            var timedOut = await service.CheckAsync(new Version(0, 3, 0));
            Check(timedOut.Status == UpdateCheckStatus.Failed && timedOut.Message.Contains("超时", StringComparison.Ordinal), "Slow response body escaped the request timeout");
        }
    }

    private static async Task<UpdateCheckResult> CheckRelease(string body) => await CheckResponse(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });

    private static async Task<UpdateCheckResult> CheckResponse(HttpResponseMessage response)
    {
        using (response)
        using (var client = new HttpClient(new ResponseHandler((_, _) => Task.FromResult(response))))
        using (var service = TestUpdateService(client))
            return await service.CheckAsync(new Version(0, 3, 0));
    }

    private static UpdateCheckService TestUpdateService(HttpClient client, TimeSpan? timeout = null) =>
        new(client, new Uri("https://api.github.com/test/releases"), timeout ?? TimeSpan.FromSeconds(1));

    private sealed class ResponseHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(Span<byte> buffer) => throw new NotSupportedException();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Wait(cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => new(Wait(cancellationToken));
        private static async Task<int> Wait(CancellationToken cancellationToken) { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); return 0; }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class MemoryStartupManager : IStartupManager
    {
        public bool IsEnabled { get; private set; }
        public void SetEnabled(bool enabled) => IsEnabled = enabled;
    }

    private static async Task Integration()
    {
        string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new Exception("Missing " + name);
        var expected = Required("PORTBRIDGE_TEST_FINGERPRINT");
        var p = new ServerProfile { Name = "Integration", Host = Required("PORTBRIDGE_TEST_HOST"), User = Required("PORTBRIDGE_TEST_USER"), Auth = "key", KeyPath = Required("PORTBRIDGE_TEST_KEY") };
        var remotePorts = Required("PORTBRIDGE_TEST_PORTS").Split(',').Select(int.Parse).ToArray();
        var locals = new List<int>();
        foreach (var remote in remotePorts)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var local = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); locals.Add(local);
            p.Rules.Add(new PortRule { LocalPort = local, TargetPort = remote });
        }
        using (var rejected = new TunnelSession())
        {
            var denied = false;
            try { await rejected.ConnectAsync(p, "", (_, _) => false); } catch { denied = true; }
            Check(denied && !rejected.IsConnected, "Untrusted host accepted");
        }
        using (var tunnel = new TunnelSession())
        {
            await tunnel.ConnectAsync(p, "", (_, fp) => fp == expected);
            Check(tunnel.IsConnected, "Tunnel not connected");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            foreach (var local in locals)
            {
                var response = await http.GetAsync($"http://127.0.0.1:{local}/");
                Check(response.IsSuccessStatusCode, "Forwarded HTTP request failed");
            }
        }
        foreach (var local in locals)
        {
            var listener = new TcpListener(IPAddress.Loopback, local); listener.Start(); listener.Stop();
            Check(true, "Port released");
        }
        // A later bind failure must roll back earlier successful forwards.
        var blocker = new TcpListener(IPAddress.Loopback, locals[1]); blocker.Start();
        try
        {
            using var tunnel = new TunnelSession(); var failed = false;
            try { await tunnel.ConnectAsync(p, "", (_, fp) => fp == expected); } catch { failed = true; }
            Check(failed && !tunnel.IsConnected, "Port conflict did not fail");
            var probe = new TcpListener(IPAddress.Loopback, locals[0]); probe.Start(); probe.Stop();
            Check(true, "Partial tunnel was not rolled back");
        }
        finally { blocker.Stop(); }
    }

    private static void Render()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PortBridge-render-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConfigStore(dir);
            var development = new ServerProfile
            {
                Name = "开发工作站", Host = "dev.example.com", User = "developer", Rules = new()
                {
                    new PortRule { Name = "管理后台", LocalPort = 18080, TargetPort = 8080, Protocol = "https" },
                    new PortRule { Name = "VNC 桌面", LocalPort = 15900, TargetPort = 5900, Protocol = "tcp" },
                    new PortRule { Name = "实验室控制台", LocalPort = 13000, TargetHost = "10.0.0.18", TargetPort = 3000, Path = "/vnc.html?path=websockify&autoconnect=1" }
                }
            };
            var staging = new ServerProfile
            {
                Name = "测试环境", Host = "stage.example.com", User = "operator", Port = 2222,
                Rules = new()
            };
            store.Save(new AppConfig { Profiles = new() { development, staging }, SelectedId = development.Id });
            using var form = new MainForm(store, new MemoryStartupManager(), allowStartupUpdateCheck: false);
            form.Show(); Application.DoEvents();
            Console.WriteLine("UI DPI: " + form.DeviceDpi);
            SaveScreenshot(form, "test-results/ui-workspace.png");
            Check(form.Width >= 950, "Unexpected form size");

            typeof(MainForm).GetMethod("EditServer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            Application.DoEvents(); SaveScreenshot(form, "test-results/ui-server-editor.png");
            typeof(MainForm).GetMethod("CancelEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);

            var loadedDevelopment = Field<AppConfig>(form, "config").Profiles.First(p => p.Id == development.Id);
            typeof(MainForm).GetMethod("EditMapping", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { loadedDevelopment.Rules[0] });
            Application.DoEvents(); SaveScreenshot(form, "test-results/ui-rule-http.png");
            typeof(MainForm).GetMethod("CancelEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            typeof(MainForm).GetMethod("EditMapping", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { loadedDevelopment.Rules[1] });
            Application.DoEvents(); SaveScreenshot(form, "test-results/ui-rule-tcp.png");
            typeof(MainForm).GetMethod("CancelEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);

            typeof(MainForm).GetMethod("SelectServer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { staging.Id });
            Application.DoEvents(); SaveScreenshot(form, "test-results/ui-no-mappings.png");
            typeof(MainForm).GetMethod("SelectServer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { development.Id });

            typeof(MainForm).GetMethod("ShowSettingsPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { true });
            Application.DoEvents(); SaveScreenshot(form, "test-results/ui-settings.png");
            typeof(MainForm).GetMethod("ShowSettingsPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, new object[] { false });
            form.Size = form.MinimumSize; Application.DoEvents();
            var rowsAtMinimum = Field<FlowLayoutPanel>(Field<object>(form, "workspaceView"), "rowsHost");
            Check(!rowsAtMinimum.HorizontalScroll.Visible, "Minimum-width workspace exposed a horizontal scrollbar");
            Check(rowsAtMinimum.Controls.Cast<Control>().All(child => child.Right <= rowsAtMinimum.ClientSize.Width), "A mapping row exceeds the minimum-width workspace viewport");
            SaveScreenshot(form, "test-results/ui-small.png");
            typeof(MainForm).GetMethod("EditServer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            Application.DoEvents(); SaveScreenshot(form, "test-results/ui-server-editor-small.png");
            typeof(MainForm).GetMethod("CancelEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            form.Close(); Application.DoEvents();
            Check(!form.IsDisposed && !form.Visible, "Closing window did not retain the tray application");

            var emptyDir = Path.Combine(Path.GetTempPath(), "PortBridge-empty-render-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var emptyForm = new MainForm(new ConfigStore(emptyDir), new MemoryStartupManager(), allowStartupUpdateCheck: false);
                emptyForm.Show(); Application.DoEvents();
                SaveScreenshot(emptyForm, "test-results/ui-empty.png");
                emptyForm.Close(); Application.DoEvents();
            }
            finally { if (Directory.Exists(emptyDir)) Directory.Delete(emptyDir, true); }
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    private static void SaveScreenshot(Form form, string relativePath)
    {
        using var image = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
        var output = Path.GetFullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        image.Save(output);
        Console.WriteLine("Rendered: " + output);
    }

    private static void Preview()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PortBridge-preview-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConfigStore(dir);
            store.Save(new AppConfig { Profiles = new() { new ServerProfile
            {
                Name = "我的开发服务器", Host = "server.example.com", User = "developer", Rules = new()
                {
                    new PortRule { Name = "管理后台", LocalPort = 18080, TargetPort = 8080 },
                    new PortRule { Name = "开发服务", LocalPort = 13000, TargetPort = 3000, Path = "/admin" },
                    new PortRule { Name = "VNC 桌面", LocalPort = 15900, TargetPort = 5900, Protocol = "tcp" }
                }
            } } });
            using var form = new MainForm(store, new MemoryStartupManager(), startInTray: false, allowStartupUpdateCheck: false);
            Application.Run(form);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
