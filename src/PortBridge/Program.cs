namespace PortBridge;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instance = new Mutex(true, "Local\\PortBridge.Desktop", out var first);
        if (!first) { MessageBox.Show("PortBridge 已在运行，请从任务栏右下角托盘打开。", "PortBridge"); return; }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
