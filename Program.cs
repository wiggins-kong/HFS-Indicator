using System.Text.Json;
using System.Threading;

namespace HfsIndicator;

internal static class Program
{
    private const string MutexName = @"Local\HfsIndicator.SingleInstance";

    internal const string WakeEventName = @"Local\HfsIndicator.Wake";

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            WakeRunningInstance();
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.Run(new TrayIcon(Settings.Load()));
    }

    private static void WakeRunningInstance()
    {
        try
        {
            using var wake = EventWaitHandle.OpenExisting(WakeEventName);
            wake.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // 第一个实例还没建好事件，直接退出即可。
        }
    }
}

internal sealed record Settings(string ServiceName, int Port)
{
    private const string FileName = "HfsIndicator.json";

    public static Settings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName);
        if (!File.Exists(path)) return new Settings("HFS", 1478);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            var name = root.TryGetProperty("serviceName", out var nameElement)
                       && nameElement.ValueKind == JsonValueKind.String
                       && nameElement.GetString() is { Length: > 0 } text
                ? text
                : "HFS";

            var port = root.TryGetProperty("port", out var portElement)
                       && portElement.TryGetInt32(out var value)
                       && value is > 0 and < 65536
                ? value
                : 1478;

            return new Settings(name, port);
        }
        catch (Exception)
        {
            return new Settings("HFS", 1478);
        }
    }
}
