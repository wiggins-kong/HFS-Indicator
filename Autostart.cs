namespace HfsIndicator;

internal static class Autostart
{
    private const string TaskName = "HfsIndicator";
    private const string LegacyLinkName = "HFS 状态托盘.lnk";

    // Task Scheduler COM 常量
    private const int CreateOrUpdate = 6;
    private const int IgnoreNew = 2;
    private const int ExecAction = 0;
    private const int RunLevelHighest = 1;
    private const int LogonTrigger = 9;
    private const int InteractiveToken = 3;
    private const uint FileNotFound = 0x80070002;

    private static string LegacyLinkPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), LegacyLinkName);

    private static string CurrentUser => $@"{Environment.UserDomainName}\{Environment.UserName}";

    public static bool IsEnabled(string exePath)
    {
        try
        {
            dynamic actions = CreateService().GetFolder("\\").GetTask(TaskName).Definition.Actions;
            return actions.Count > 0
                   && string.Equals((string?)actions.Item(1).Path, exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void Enable(string exePath)
    {
        dynamic service = CreateService();
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = "HFS 状态托盘";

        // 必须用当前用户 + 最高权限：SYSTEM 进不了用户会话，普通权限又管不了服务。
        dynamic principal = definition.Principal;
        principal.UserId = CurrentUser;
        principal.LogonType = InteractiveToken;
        principal.RunLevel = RunLevelHighest;

        dynamic trigger = definition.Triggers.Create(LogonTrigger);
        trigger.UserId = CurrentUser;
        trigger.Delay = "PT10S"; // 通知区没就绪就起，托盘图标会丢

        dynamic action = definition.Actions.Create(ExecAction);
        action.Path = exePath;
        action.WorkingDirectory = Path.GetDirectoryName(exePath) ?? "";

        dynamic settings = definition.Settings;
        settings.Enabled = true;
        settings.DisallowStartIfOnBatteries = false;
        settings.StopIfGoingOnBatteries = false;
        settings.ExecutionTimeLimit = "PT0S";
        settings.MultipleInstances = IgnoreNew;
        settings.StartWhenAvailable = true;

        service.GetFolder("\\").RegisterTaskDefinition(
            TaskName, definition, CreateOrUpdate, CurrentUser, null, InteractiveToken, null);

        DeleteLegacyLink();
    }

    public static void Disable()
    {
        try
        {
            CreateService().GetFolder("\\").DeleteTask(TaskName, 0);
        }
        catch (Exception exception) when ((uint)exception.HResult == FileNotFound)
        {
            // 没建过，忽略。
        }

        DeleteLegacyLink();
    }

    private static void DeleteLegacyLink()
    {
        // 旧版本用启动文件夹快捷方式，升级后顺手清掉。
        if (File.Exists(LegacyLinkPath)) File.Delete(LegacyLinkPath);
    }

    private static dynamic CreateService()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")
                                                  ?? throw new InvalidOperationException("系统缺少 Schedule.Service，无法创建计划任务"))
                          ?? throw new InvalidOperationException("无法创建 Schedule.Service");
        service.Connect();
        return service;
    }
}
