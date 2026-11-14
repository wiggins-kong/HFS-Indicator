namespace HfsIndicator;

internal static class Autostart
{
    private const string LinkName = "HFS 状态托盘.lnk";

    private static string LinkPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), LinkName);

    public static bool IsEnabled(string exePath)
    {
        try
        {
            return File.Exists(LinkPath)
                   && string.Equals(ReadTarget(LinkPath), exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Enable(string exePath)
    {
        dynamic shell = CreateShell();
        dynamic link = shell.CreateShortcut(LinkPath);
        link.TargetPath = exePath;
        link.WorkingDirectory = Path.GetDirectoryName(exePath) ?? "";
        link.Description = "HFS 状态托盘";
        link.Save();
    }

    public static void Disable()
    {
        if (File.Exists(LinkPath)) File.Delete(LinkPath);
    }

    private static string? ReadTarget(string linkPath)
    {
        dynamic shell = CreateShell();
        dynamic link = shell.CreateShortcut(linkPath);
        return (string?)link.TargetPath;
    }

    private static dynamic CreateShell() =>
        Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")
                                 ?? throw new InvalidOperationException("系统缺少 WScript.Shell，无法创建快捷方式"))
        ?? throw new InvalidOperationException("无法创建 WScript.Shell");
}
