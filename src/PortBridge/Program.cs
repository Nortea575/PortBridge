namespace PortBridge;

internal static class Program
{
    internal const int RestoreWindowMessage = 0x8001;
    internal const string WindowTitle = "PortBridge · 服务转发器";

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [STAThread]
    private static void Main(string[] args)
    {
        using var instance = new Mutex(true, "Local\\PortBridge.Desktop", out var first);
        var startupLaunch = args.Any(x => string.Equals(x, "--startup", StringComparison.OrdinalIgnoreCase));
        if (!first)
        {
            if (!startupLaunch)
            {
                var window = FindWindow(null, WindowTitle);
                if (window != IntPtr.Zero) PostMessage(window, RestoreWindowMessage, IntPtr.Zero, IntPtr.Zero);
                else MessageBox.Show("PortBridge 已在运行，请从任务栏右下角托盘打开。", "PortBridge");
            }
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(startInTray: startupLaunch));
    }
}
