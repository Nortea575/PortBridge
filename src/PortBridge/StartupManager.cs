using Microsoft.Win32;

namespace PortBridge;

public interface IStartupManager
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

public sealed class WindowsStartupManager : IStartupManager
{
    public const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "PortBridge";

    private readonly string subKeyPath;
    private readonly Func<string?> executablePath;

    public WindowsStartupManager(string? subKeyPath = null, Func<string?>? executablePath = null)
    {
        this.subKeyPath = subKeyPath ?? RunSubKey;
        this.executablePath = executablePath ?? (() => Environment.ProcessPath);
    }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: false);
            var command = key?.GetValue(ValueName) as string;
            var processPath = executablePath();
            if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(processPath)) return false;

            var expectedPath = Path.GetFullPath(processPath);
            var expectedCommand = QuoteExecutable(expectedPath) + " --startup";
            return string.Equals(command.Trim(), expectedCommand, StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: true);
            existing?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var processPath = executablePath();
        if (string.IsNullOrWhiteSpace(processPath)) throw new InvalidOperationException("无法确定 PortBridge.exe 的安装路径。");

        using var key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true)
            ?? throw new UnauthorizedAccessException("无法写入当前用户的启动设置。");
        var command = QuoteExecutable(Path.GetFullPath(processPath)) + " --startup";
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    private static string QuoteExecutable(string path) => "\"" + path + "\"";
}
