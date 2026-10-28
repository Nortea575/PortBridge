using System.ComponentModel;
using System.Text.Json;

namespace PortBridge;

public sealed class PortRule
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "WebUI";
    public int LocalPort { get; set; } = 18080;
    public string TargetHost { get; set; } = "127.0.0.1";
    public int TargetPort { get; set; } = 8080;
    public string Protocol { get; set; } = "http";
    public string Path { get; set; } = "/";

    public string BrowserUrl()
    {
        if (Protocol is not ("http" or "https"))
            throw new InvalidOperationException("这是 TCP 服务，请在对应客户端中连接 127.0.0.1:" + LocalPort);
        // Relative paths only: the browser must open the local forwarded endpoint.
        var path = string.IsNullOrWhiteSpace(Path) ? "/" : Path;
        if (!path.StartsWith('/') || path.StartsWith("//") || path.Contains('\\') || path.Any(char.IsControl))
            throw new InvalidOperationException("网页路径必须以单个 / 开头，不能包含反斜杠或控制字符。");
        return $"{Protocol}://127.0.0.1:{LocalPort}{path}";
    }
}

public sealed class ServerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新服务器";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string User { get; set; } = "";
    public string Auth { get; set; } = "password";
    public string KeyPath { get; set; } = "";
    public BindingList<PortRule> Rules { get; set; } = new();
    public override string ToString() => Name;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(User))
            throw new InvalidOperationException("请填写配置名称、SSH 主机和用户名。");
        CheckPort(Port);
        if (Auth is not ("password" or "key")) throw new InvalidOperationException("未知的认证方式。");
        if (Auth == "key" && !File.Exists(KeyPath)) throw new InvalidOperationException("请选择存在的私钥文件。");
        var enabled = Rules.Where(r => r.Enabled).ToList();
        if (enabled.Count == 0) throw new InvalidOperationException("请至少启用一条转发规则。");
        if (enabled.Select(r => r.LocalPort).Distinct().Count() != enabled.Count)
            throw new InvalidOperationException("已启用规则的本地端口不能重复。");
        foreach (var r in enabled)
        {
            CheckPort(r.LocalPort); CheckPort(r.TargetPort);
            if (string.IsNullOrWhiteSpace(r.TargetHost)) throw new InvalidOperationException("请填写目标主机。");
            if (r.Protocol is not ("http" or "https" or "tcp")) throw new InvalidOperationException("协议请选择 http、https 或 tcp。");
            if (r.Protocol != "tcp") _ = r.BrowserUrl();
        }
    }

    private static void CheckPort(int port)
    {
        if (port is < 1 or > 65535) throw new InvalidOperationException("端口范围为 1–65535。");
    }
}

public sealed class AppConfig
{
    public List<ServerProfile> Profiles { get; set; } = new();
    public string SelectedId { get; set; } = "";
    public Dictionary<string, string> TrustedHosts { get; set; } = new();
}

public sealed class ConfigStore
{
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string DirectoryPath { get; }
    public string FilePath => System.IO.Path.Combine(DirectoryPath, "settings.json");
    public ConfigStore(string? directory = null) => DirectoryPath = directory ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortBridge");

    public AppConfig Load() => File.Exists(FilePath)
        ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Json) ?? throw new InvalidDataException("配置文件为空。")
        : new AppConfig();

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, Json));
        File.Move(temp, FilePath, true);
    }
}
