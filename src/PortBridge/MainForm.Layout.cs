namespace PortBridge;

public sealed partial class MainForm
{
    private void BuildUi()
    {
        var frame = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Margin = Padding.Empty };
        Controls.Add(frame);
        var shell = new TableLayoutPanel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Theme.Background
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 224));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void FitShell()
        {
            if (frame.ClientSize.Width <= 0 || frame.ClientSize.Height <= 0) return;
            shell.Width = Math.Min(frame.ClientSize.Width, 1440);
            shell.Height = frame.ClientSize.Height;
            shell.Left = (frame.ClientSize.Width - shell.Width) / 2;
            shell.Top = 0;
        }
        frame.Controls.Add(shell);
        frame.ClientSizeChanged += (_, _) => FitShell();
        Shown += (_, _) => FitShell();
        FitShell();

        serverListView = new ServerListView();
        shell.Controls.Add(serverListView, 0, 0);
        serverListView.ServerSelected += SelectServer;
        serverListView.AddRequested += AddServer;
        serverListView.ImportRequested += ImportProfile;
        serverListView.SettingsRequested += () => ShowSettingsPage(true);
        serverListView.TrayRequested += Hide;
        serverListView.ExitRequested += ExitApp;

        contentHost.BackColor = Theme.Background;
        shell.Controls.Add(contentHost, 1, 0);
        homeView.BackColor = settingsView.BackColor = serverEditorHost.BackColor = ruleEditorHost.BackColor = Theme.Background;
        foreach (var page in new Control[] { homeView, settingsView, serverEditorHost, ruleEditorHost })
        {
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            contentHost.Controls.Add(page);
        }

        workspaceView = new WorkspaceView { Dock = DockStyle.Fill };
        homeView.Controls.Add(workspaceView);
        workspaceView.AddFirstServerRequested += AddServer;
        workspaceView.ImportRequested += ImportProfile;
        workspaceView.EditServerRequested += EditServer;
        workspaceView.ExportServerRequested += ExportProfile;
        workspaceView.DeleteServerRequested += DeleteServer;
        workspaceView.AddMappingRequested += AddMapping;
        workspaceView.EditMappingRequested += EditMapping;
        workspaceView.OpenMappingRequested += OpenService;
        workspaceView.CopyMappingRequested += CopyMapping;
        workspaceView.ConnectRequested += async credential => await Connect(credential);
        workspaceView.DisconnectRequested += Disconnect;

        var settingsContent = new SettingsView(autoStartToggle, autoUpdateToggle, startupMessage,
            updateStatus, checkUpdatesButton, openUpdateButton, DisplayCurrentVersion());
        settingsContent.BackRequested += () => ShowSettingsPage(false);
        settingsView.Controls.Add(settingsContent);

        serverEditorView = new ServerEditorView { Dock = DockStyle.Fill };
        serverEditorHost.Controls.Add(serverEditorView);
        serverEditorView.SaveRequested += SaveServerDraft;
        serverEditorView.BackRequested += BackFromEditor;
        serverEditorView.CancelRequested += CancelEditor;

        ruleEditorView = new RuleEditorView { Dock = DockStyle.Fill };
        ruleEditorHost.Controls.Add(ruleEditorView);
        ruleEditorView.SaveRequested += SaveRuleDraft;
        ruleEditorView.BackRequested += BackFromEditor;
        ruleEditorView.CancelRequested += CancelEditor;
        ruleEditorView.DeleteRequested += DeleteRuleDraft;

        ShowPage(homeView);
    }
}
