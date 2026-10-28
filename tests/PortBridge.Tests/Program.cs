using System.Net;
using System.Net.Sockets;
using System.Text.Json;
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

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            ModelTests();
            if (args.Contains("--integration")) Integration().GetAwaiter().GetResult();
            if (args.Contains("--render")) Render();
            Console.WriteLine($"PASS: {assertions} assertions"); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static void ModelTests()
    {
        var p = new ServerProfile { Name = "Test", Host = "example.invalid", User = "tester", Rules = new() { new PortRule() } };
        p.Validate();
        p.Rules.Add(new PortRule()); Reject(p.Validate, "Duplicate local port accepted");
        p.Rules[1].Enabled = false; p.Validate();
        p.Rules[0].TargetPort = 0; Reject(p.Validate, "Invalid port accepted"); p.Rules[0].TargetPort = 8080;
        var rule = p.Rules[0]; rule.Path = "/vnc.html?path=websockify&autoconnect=1";
        Check(rule.BrowserUrl() == "http://127.0.0.1:18080/vnc.html?path=websockify&autoconnect=1", "URL changed");
        rule.Path = "//example.com"; Reject(() => rule.BrowserUrl(), "External URL accepted");
        rule.Path = "/\\example.com"; Reject(() => rule.BrowserUrl(), "Backslash URL accepted");
        rule.Protocol = "tcp"; Reject(() => rule.BrowserUrl(), "TCP opened as HTTP");
        rule.Path = "/"; rule.Protocol = "http";
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
            File.WriteAllText(store.FilePath, "invalid-json");
            try { store.Load(); throw new Exception("Invalid configuration accepted"); } catch (JsonException) { assertions++; }
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
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
        ApplicationConfiguration.Initialize();
        var dir = Path.Combine(Path.GetTempPath(), "PortBridge-render-" + Guid.NewGuid().ToString("N"));
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
            using var form = new MainForm(store);
            form.Show(); Application.DoEvents();
            using var image = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
            var output = Path.GetFullPath("test-results/ui.png");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!); image.Save(output);
            Check(image.Width >= 950, "Unexpected form size");
            Console.WriteLine("Rendered: " + output);
            form.Size = form.MinimumSize; Application.DoEvents();
            using var small = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(small, new Rectangle(Point.Empty, form.Size));
            small.Save(Path.GetFullPath("test-results/ui-small.png"));
            form.Close(); Application.DoEvents();
            Check(!form.IsDisposed && !form.Visible, "Closing window did not retain the tray application");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
