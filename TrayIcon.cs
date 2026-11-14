using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace HfsIndicator;

internal sealed class TrayIcon : ApplicationContext
{
    private enum State
    {
        Checking,
        Running,
        Paused,
        Busy,
        Unresponsive,
        Error,
        NoService
    }

    private static readonly string ExePath = Environment.ProcessPath ?? Application.ExecutablePath;

    private readonly Settings _settings;
    private readonly HfsApi _api;
    private readonly HfsService _service;
    private readonly NotifyIcon _notify;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly EventWaitHandle _wake;
    private readonly Icon _green;
    private readonly Icon _yellow;
    private readonly Icon _gray;
    private readonly Icon _red;

    private State _state = State.Checking;
    private string? _note;
    private string _busyText = "";
    private HfsStatus? _status;
    private int _connections;
    private int _httpFailures;
    private bool _busy;
    private bool _refreshing;
    private bool _started;

    public TrayIcon(Settings settings)
    {
        _settings = settings;
        _api = new HfsApi(settings.Port);
        _service = new HfsService(settings.ServiceName);

        _green = CreateStatusIcon(Color.FromArgb(0x2E, 0xCC, 0x71));
        _yellow = CreateStatusIcon(Color.FromArgb(0xF1, 0xC4, 0x0F));
        _gray = CreateStatusIcon(Color.FromArgb(0x9A, 0xA0, 0xA6));
        _red = CreateStatusIcon(Color.FromArgb(0xE7, 0x4C, 0x3C));

        _wake = new EventWaitHandle(false, EventResetMode.AutoReset, Program.WakeEventName, out _);

        _menu = new ContextMenuStrip();
        _menu.Opening += (_, _) => BuildMenu();

        _notify = new NotifyIcon
        {
            Icon = _yellow,
            Text = "HFS 状态托盘",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notify.MouseClick += OnIconClick;

        _timer = new System.Windows.Forms.Timer { Interval = 3000 };
        _timer.Tick += OnTick;
        _timer.Start();

        _ = RefreshAsync();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        try
        {
            if (_wake.WaitOne(0)) Notify("托盘已经在运行了");
            await RefreshAsync();
        }
        catch (Exception)
        {
            // 定时器里绝不冒泡，否则整个托盘会退出。
        }
    }

    private void OnIconClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) OpenUrl(FrontUrl());
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || _busy) return;
        _refreshing = true;

        try
        {
            var serviceStatus = _service.ReadStatus();
            if (serviceStatus is null)
            {
                Apply(State.NoService);
                return;
            }

            switch (serviceStatus.Value)
            {
                case ServiceControllerStatus.Running:
                {
                    var status = await _api.GetStatusAsync();
                    if (status is null)
                    {
                        _httpFailures++;
                        if (_httpFailures >= 2) Apply(State.Unresponsive);
                        return;
                    }

                    _httpFailures = 0;
                    _status = status;
                    _connections = await _api.GetConnectionCountAsync() ?? _connections;
                    Apply(State.Running);
                    break;
                }
                case ServiceControllerStatus.Stopped:
                    _httpFailures = 0;
                    Apply(State.Paused);
                    break;
                default:
                    Apply(State.Busy);
                    break;
            }
        }
        catch (Exception exception)
        {
            Apply(State.Error, exception.Message);
        }
        finally
        {
            _refreshing = false;
            _started = true;
        }
    }

    private async Task ToggleServiceAsync()
    {
        var running = _service.ReadStatus() == ServiceControllerStatus.Running;
        await RunActionAsync(running ? "正在停止…" : "正在启动…", running ? _service.Stop : _service.Start);
    }

    private async Task RestartServiceAsync() => await RunActionAsync("正在重启…", _service.Restart);

    private async Task RunActionAsync(string busyText, Func<string?> action)
    {
        if (_busy) return;

        _busy = true;
        _busyText = busyText;
        Apply(State.Busy);

        string? error;
        try
        {
            error = await Task.Run(action);
        }
        catch (Exception exception)
        {
            error = exception.Message;
        }

        _busy = false;
        _busyText = "";

        if (error is not null)
        {
            Apply(State.Error, error);
            Notify("操作失败：" + error);
            return;
        }

        await Task.Delay(300);
        await RefreshAsync();
    }

    private void ToggleAutostart()
    {
        try
        {
            if (Autostart.IsEnabled(ExePath))
            {
                Autostart.Disable();
                Notify("已取消开机自启");
            }
            else
            {
                Autostart.Enable(ExePath);
                Notify("已设为开机自启");
            }
        }
        catch (Exception exception)
        {
            Notify("自启设置失败：" + exception.Message);
        }
    }

    private void CopyLink()
    {
        try
        {
            Clipboard.SetText(FrontUrl());
            Notify("链接已复制：" + FrontUrl());
        }
        catch (Exception exception)
        {
            Notify("复制失败：" + exception.Message);
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Notify("打开链接失败：" + exception.Message);
        }
    }

    private string FrontUrl() =>
        _status?.LanUrl is { Length: > 0 } url ? url : $"http://localhost:{_settings.Port}/";

    private string AdminUrl() => $"http://localhost:{_settings.Port}/~/admin/";

    private string LanHost()
    {
        if (_status?.LanUrl is { Length: > 0 } url
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Authority.Length > 0)
        {
            return uri.Authority;
        }

        return $"localhost:{_settings.Port}";
    }

    private string Prefix() => _status?.Version is { Length: > 0 } version ? $"HFS {version}" : "HFS";

    private string HeaderText()
    {
        var text = _state switch
        {
            State.Running => $"HFS 运行中 · {LanHost()} · {_connections} 连接",
            State.Paused => $"HFS 已暂停 · {LanHost()}",
            State.Busy => string.IsNullOrEmpty(_busyText) ? "HFS 正在切换状态…" : $"HFS {_busyText}",
            State.Unresponsive => "HFS 无响应 · 服务在运行",
            State.Error => $"操作失败 · {_note}",
            State.NoService => $"未找到 HFS 服务「{_settings.ServiceName}」",
            _ => "正在检测 HFS…"
        };

        return text.Length > 80 ? text[..79] + "…" : text;
    }

    private string TooltipText() => _state switch
    {
        State.Running => $"{Prefix()} · {LanHost()} · {_connections} 连接",
        State.Paused => $"HFS 已暂停 · {LanHost()}",
        State.Busy => string.IsNullOrEmpty(_busyText) ? "HFS 正在切换状态" : $"HFS {_busyText}",
        State.Unresponsive => "HFS 无响应 · 服务在运行",
        State.Error => $"HFS 操作失败 · {_note}",
        State.NoService => $"未找到 HFS 服务「{_settings.ServiceName}」",
        _ => "HFS 状态托盘 · 正在检测"
    };

    private void Apply(State state, string? note = null)
    {
        var previous = _state;
        _state = state;
        _note = note;

        _notify.Icon = IconFor(state);

        var tooltip = TooltipText();
        _notify.Text = tooltip.Length > 127 ? tooltip[..126] + "…" : tooltip;

        if (_started && previous != state) Announce(state, previous);
    }

    private void Announce(State state, State previous)
    {
        var message = state switch
        {
            State.Running when previous == State.Paused => "HFS 已恢复",
            State.Paused when previous == State.Running => "HFS 已暂停",
            State.Unresponsive => "HFS 无响应：服务在运行，但页面打不开",
            _ => null
        };

        if (message is not null) Notify(message);
    }

    private void Notify(string message) =>
        _notify.ShowBalloonTip(3000, "HFS 状态托盘", message, ToolTipIcon.None);

    private Icon IconFor(State state) => state switch
    {
        State.Running => _green,
        State.Paused or State.NoService => _gray,
        State.Unresponsive or State.Error => _red,
        _ => _yellow
    };

    private void BuildMenu()
    {
        _menu.Items.Clear();

        _menu.Items.Add(new ToolStripMenuItem(HeaderText()) { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item("打开前台页面", () => OpenUrl(FrontUrl())));
        _menu.Items.Add(Item("打开后台管理", () => OpenUrl(AdminUrl())));
        _menu.Items.Add(Item("复制局域网链接", CopyLink));
        _menu.Items.Add(new ToolStripSeparator());

        var isRunning = _state is State.Running or State.Unresponsive;
        var canControl = _state != State.NoService && !_busy;

        var toggle = Item(isRunning ? "暂停服务" : "启动服务", () => _ = ToggleServiceAsync());
        toggle.Enabled = canControl;
        _menu.Items.Add(toggle);

        var restart = Item("重启服务", () => _ = RestartServiceAsync());
        restart.Enabled = canControl && isRunning;
        _menu.Items.Add(restart);

        _menu.Items.Add(new ToolStripSeparator());

        var autostart = new ToolStripMenuItem("开机自启") { Checked = Autostart.IsEnabled(ExePath) };
        autostart.Click += (_, _) => ToggleAutostart();
        _menu.Items.Add(autostart);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Item("退出", ExitApp));
    }

    private static ToolStripMenuItem Item(string text, Action action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        return item;
    }

    private void ExitApp()
    {
        _timer.Stop();
        _timer.Dispose();
        _notify.Visible = false;
        _notify.Dispose();
        _menu.Dispose();
        _wake.Dispose();
        _green.Dispose();
        _yellow.Dispose();
        _gray.Dispose();
        _red.Dispose();
        ExitThread();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);

    private const int IconSize = 64;

    // 状态灯的位置和半径按 64px 画布取值，与设计稿比例一致。
    private const float DotCenter = 55.0f;
    private const float DotRadius = 6.8f;
    private const float HaloRadius = 7.8f;

    private static readonly Bitmap BaseIcon = LoadBaseIcon();

    private static Bitmap LoadBaseIcon()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("HfsIndicator.Assets.seagull.png")
            ?? throw new InvalidOperationException("缺少图标资源 Assets/seagull.png");

        using var source = new Bitmap(stream);
        var bitmap = new Bitmap(IconSize, IconSize);

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, 0, 0, IconSize, IconSize);
        }

        return bitmap;
    }

    private static Icon CreateStatusIcon(Color color)
    {
        using var bitmap = new Bitmap(BaseIcon);

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var halo = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
            using var fill = new SolidBrush(color);
            using var outline = new Pen(Color.FromArgb(70, 0, 0, 0), 1f);

            graphics.FillEllipse(halo, DotCenter - HaloRadius, DotCenter - HaloRadius, HaloRadius * 2, HaloRadius * 2);
            graphics.FillEllipse(fill, DotCenter - DotRadius, DotCenter - DotRadius, DotRadius * 2, DotRadius * 2);
            graphics.DrawEllipse(outline, DotCenter - DotRadius, DotCenter - DotRadius, DotRadius * 2, DotRadius * 2);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}
