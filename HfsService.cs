using System.ComponentModel;
using System.ServiceProcess;

namespace HfsIndicator;

internal sealed class HfsService(string serviceName)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public ServiceControllerStatus? ReadStatus()
    {
        try
        {
            using var service = new ServiceController(serviceName);
            return service.Status;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string? Stop() => Transition(
        ServiceControllerStatus.Stopped,
        ServiceControllerStatus.StopPending,
        service => service.Stop(),
        "服务 15 秒内没有停止，可能卡住了");

    public string? Start() => Transition(
        ServiceControllerStatus.Running,
        ServiceControllerStatus.StartPending,
        service => service.Start(),
        "服务 15 秒内没有启动，检查端口是否被占用");

    public string? Restart() => Stop() ?? Start();

    private string? Transition(
        ServiceControllerStatus target,
        ServiceControllerStatus pending,
        Action<ServiceController> change,
        string timeoutMessage)
    {
        try
        {
            using var service = new ServiceController(serviceName);
            var status = service.Status;

            if (status == target) return null;
            if (status != pending) change(service);

            service.WaitForStatus(target, Timeout);
            return null;
        }
        catch (System.TimeoutException)
        {
            return timeoutMessage;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
        catch (Win32Exception exception)
        {
            return exception.Message;
        }
    }
}
