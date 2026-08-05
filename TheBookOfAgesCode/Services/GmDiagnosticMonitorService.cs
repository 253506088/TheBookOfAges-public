using MegaCrit.Sts2.Core.Logging;
using TheBookOfAges.TheBookOfAgesCode.Config;
using TheBookOfAges.TheBookOfAgesCode.Models;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// GM 诊断监视服务：独立监视全局日志、异常事件和 godot.log 新增内容。
/// </summary>
public static class GmDiagnosticMonitorService
{
    private const int MaxReportsPerSession = 3;
    private const int MaxConsecutiveReportFailures = 2;
    private const int DuplicateSuppressSeconds = 60;
    private const int LogWatcherIntervalMs = 2000;
    private const long MaxLogBytesPerFile = 8 * 1024 * 1024;
    private static readonly object StateLock = new();
    private static readonly SemaphoreSlim ReportLock = new(1, 1);
    private static readonly Dictionary<string, DateTimeOffset> LastReportTimes = new();
    private static Logger? _logger;
    private static GmDiagnosticSessionService? _sessionService;
    private static GmDiagnosticLogCollector? _logCollector;
    private static GmDiagnosticReportWriter? _reportWriter;
    private static CancellationTokenSource? _logWatcherCancellation;
    private static bool _started;
    private static bool _logCallbackSubscribed;
    private static bool _sessionStarted;
    private static bool _reportingDisabledByFailure;
    private static int _reportCount;
    private static int _consecutiveReportFailures;

    /// <summary>
    /// 启动诊断监视服务；多次调用只会生效一次。
    /// </summary>
    public static void Start(string modId)
    {
        try
        {
            StartCore(modId);
        }
        catch (Exception ex)
        {
            try
            {
                _logger ??= new Logger($"{modId}.Diagnostics", LogType.Generic);
                _logger.Info($"GM 诊断监视服务启动失败，已禁用诊断导出以避免影响游戏：异常类型={ex.GetType().Name}，异常={ex.Message}。");
            }
            catch
            {
                // 诊断模块启动失败不能反过来影响游戏初始化。
            }
        }
    }

    /// <summary>
    /// 启动诊断监视服务核心逻辑；外层 Start 会兜住所有异常。
    /// </summary>
    private static void StartCore(string modId)
    {
        lock (StateLock)
        {
            if (_started)
            {
                return;
            }

            string userDataDirectory = GetUserDataDirectory();
            GmDiagnosticNameService nameService = new();
            _logCollector = new GmDiagnosticLogCollector(userDataDirectory, MaxLogBytesPerFile);
            _sessionService = new GmDiagnosticSessionService(userDataDirectory);
            _reportWriter = new GmDiagnosticReportWriter(
                modId,
                userDataDirectory,
                nameService,
                new GmDiagnosticInfoCollector(nameService),
                new GmDiagnosticModCollector(nameService),
                _logCollector);
            _logger = new Logger($"{modId}.Diagnostics", LogType.Generic);

            AppDomain.CurrentDomain.ProcessExit += HandleProcessExit;
            AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
            TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
            GmModConfig.RuntimeConfigChanged += HandleRuntimeConfigChanged;
            _started = true;
        }

        ApplyRuntimeConfig();
    }

    /// <summary>
    /// 根据当前配置开启或关闭运行时监视。
    /// </summary>
    private static void ApplyRuntimeConfig()
    {
        if (GmModConfig.IsDiagnosticExportEnabled)
        {
            EnableMonitoring();
        }
        else
        {
            DisableLogWatcher();
            UnsubscribeLogCallback();
        }
    }

    /// <summary>
    /// 启用日志监视和会话标记。
    /// </summary>
    private static void EnableMonitoring()
    {
        bool previousSessionWasRunning = false;
        lock (StateLock)
        {
            SubscribeLogCallback();
            if (!_sessionStarted)
            {
                previousSessionWasRunning = _sessionService?.MarkSessionStarted() == true;
                _sessionStarted = true;
            }

            StartLogWatcher();
        }

        if (previousSessionWasRunning)
        {
            QueuePreviousCrashReport();
        }
    }

    /// <summary>
    /// 订阅全局日志回调。
    /// </summary>
    private static void SubscribeLogCallback()
    {
        if (_logCallbackSubscribed)
        {
            return;
        }

        Log.LogCallback += HandleGlobalLog;
        _logCallbackSubscribed = true;
    }

    /// <summary>
    /// 取消订阅全局日志回调。
    /// </summary>
    private static void UnsubscribeLogCallback()
    {
        if (!_logCallbackSubscribed)
        {
            return;
        }

        Log.LogCallback -= HandleGlobalLog;
        _logCallbackSubscribed = false;
    }

    /// <summary>
    /// 启动 godot.log 新增内容轮询。
    /// </summary>
    private static void StartLogWatcher()
    {
        if (_logWatcherCancellation != null)
        {
            return;
        }

        _logWatcherCancellation = new CancellationTokenSource();
        CancellationToken token = _logWatcherCancellation.Token;
        _ = Task.Run(() => WatchPrimaryLogAsync(token), token);
    }

    /// <summary>
    /// 停止 godot.log 新增内容轮询。
    /// </summary>
    private static void DisableLogWatcher()
    {
        CancellationTokenSource? cancellation = _logWatcherCancellation;
        if (cancellation == null)
        {
            return;
        }

        _logWatcherCancellation = null;
        cancellation.Cancel();
        cancellation.Dispose();
    }

    /// <summary>
    /// 轮询 godot.log 的新增行；只读文件，不修改任何游戏状态。
    /// </summary>
    private static async Task WatchPrimaryLogAsync(CancellationToken cancellationToken)
    {
        long position = -1;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (GmModConfig.IsDiagnosticExportEnabled && _logCollector != null)
                {
                    foreach (string line in _logCollector.ReadNewPrimaryLogLines(ref position))
                    {
                        GmDiagnosticEvent? diagnosticEvent = GmDiagnosticTriggerMatcher.TryMatchText(line, "godot.log 新增日志");
                        if (diagnosticEvent != null)
                        {
                            QueueReport(diagnosticEvent);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Info($"GM 诊断日志轮询异常：异常类型={ex.GetType().Name}，异常={ex.Message}。");
            }

            try
            {
                await Task.Delay(LogWatcherIntervalMs, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// 处理全局日志回调。
    /// </summary>
    private static void HandleGlobalLog(LogLevel level, string message, int skipFrames)
    {
        if (!GmModConfig.IsDiagnosticExportEnabled)
        {
            return;
        }

        GmDiagnosticEvent? diagnosticEvent = GmDiagnosticTriggerMatcher.TryMatchLog(level, message, "全局日志回调");
        if (diagnosticEvent != null)
        {
            QueueReport(diagnosticEvent);
        }
    }

    /// <summary>
    /// 处理未捕获异常。
    /// </summary>
    private static void HandleUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (!GmModConfig.IsDiagnosticExportEnabled)
        {
            return;
        }

        string message = e.ExceptionObject?.ToString() ?? "未处理异常，但没有异常对象。";
        QueueReport(new GmDiagnosticEvent(GmDiagnosticEventKind.UnhandledException, "AppDomain.UnhandledException", message, LogLevel.Error));
    }

    /// <summary>
    /// 处理未观察到的异步任务异常。
    /// </summary>
    private static void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (!GmModConfig.IsDiagnosticExportEnabled)
        {
            return;
        }

        QueueReport(new GmDiagnosticEvent(GmDiagnosticEventKind.UnobservedTaskException, "TaskScheduler.UnobservedTaskException", e.Exception.ToString(), LogLevel.Error));
    }

    /// <summary>
    /// 处理配置变更。
    /// </summary>
    private static void HandleRuntimeConfigChanged()
    {
        ApplyRuntimeConfig();
    }

    /// <summary>
    /// 处理进程正常退出，写入正常结束标记。
    /// </summary>
    private static void HandleProcessExit(object? sender, EventArgs e)
    {
        if (_sessionStarted)
        {
            _sessionService?.MarkSessionClosed();
        }
    }

    /// <summary>
    /// 发现上次会话疑似异常退出时，生成一次补充诊断包。
    /// </summary>
    private static void QueuePreviousCrashReport()
    {
        GmDiagnosticEvent? recentTrigger = _logCollector?.TryFindRecentTrigger();
        if (recentTrigger != null)
        {
            QueueReport(recentTrigger);
            return;
        }

        QueueReport(new GmDiagnosticEvent(
            GmDiagnosticEventKind.PreviousCrash,
            "会话标记检查",
            "上次游戏会话没有写入正常结束标记，疑似闪退或被强制关闭。"));
    }

    /// <summary>
    /// 把诊断事件加入导出队列。
    /// </summary>
    private static void QueueReport(GmDiagnosticEvent diagnosticEvent)
    {
        if (!TryReserveReportSlot(diagnosticEvent))
        {
            return;
        }

        _ = Task.Run(() => WriteReportSafelyAsync(diagnosticEvent));
    }

    /// <summary>
    /// 尝试预留一次导出机会，并执行防抖判断。
    /// </summary>
    private static bool TryReserveReportSlot(GmDiagnosticEvent diagnosticEvent)
    {
        lock (StateLock)
        {
            if (!GmModConfig.IsDiagnosticExportEnabled || _reportingDisabledByFailure || _reportCount >= MaxReportsPerSession)
            {
                return false;
            }

            string key = diagnosticEvent.BuildDeduplicationKey();
            DateTimeOffset now = DateTimeOffset.Now;
            if (LastReportTimes.TryGetValue(key, out DateTimeOffset lastTime)
                && now - lastTime < TimeSpan.FromSeconds(DuplicateSuppressSeconds))
            {
                return false;
            }

            LastReportTimes[key] = now;
            _reportCount++;
            return true;
        }
    }

    /// <summary>
    /// 安全写出诊断包；写失败只记录日志，不影响游戏。
    /// </summary>
    private static async Task WriteReportSafelyAsync(GmDiagnosticEvent diagnosticEvent)
    {
        await ReportLock.WaitAsync();
        try
        {
            if (!GmModConfig.IsDiagnosticExportEnabled || _reportWriter == null)
            {
                return;
            }

            string reportPath = _reportWriter.WriteReport(diagnosticEvent);
            ResetReportFailureCount();
            _logger?.Info($"GM 诊断包已生成：{reportPath}，{BuildCurrentPlatformLogText()}。");
        }
        catch (Exception ex)
        {
            RegisterReportFailure();
            _logger?.Info($"GM 诊断包生成失败：{BuildCurrentPlatformLogText()}，异常类型={ex.GetType().Name}，异常={ex.Message}。");
        }
        finally
        {
            ReportLock.Release();
        }
    }

    /// <summary>
    /// 重置连续导出失败计数。
    /// </summary>
    private static void ResetReportFailureCount()
    {
        lock (StateLock)
        {
            _consecutiveReportFailures = 0;
        }
    }

    /// <summary>
    /// 记录一次导出失败；连续失败过多时本局熔断自动导出，避免反复 IO 压力。
    /// </summary>
    private static void RegisterReportFailure()
    {
        lock (StateLock)
        {
            _consecutiveReportFailures++;
            if (_consecutiveReportFailures >= MaxConsecutiveReportFailures)
            {
                _reportingDisabledByFailure = true;
            }
        }
    }

    /// <summary>
    /// 获取游戏用户数据目录。
    /// </summary>
    private static string GetUserDataDirectory()
    {
        try
        {
            return Godot.OS.GetUserDataDir();
        }
        catch
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SlayTheSpire2");
        }
    }

    /// <summary>
    /// 构建诊断导出日志里的平台判断信息，方便移动端玩家反馈时直接看出导出环境。
    /// </summary>
    private static string BuildCurrentPlatformLogText()
    {
        string systemName = GetCurrentSystemName();
        bool isMobile = IsMobilePlatform(systemName);
        bool canUseDesktopDirectory = CanUseDesktopDirectory(isMobile);
        bool canExportOnCurrentSystem = CanExportOnCurrentSystem();
        return $"当前系统={systemName}，是否移动端={FormatBool(isMobile)}，是否允许当前系统导出={FormatBool(canExportOnCurrentSystem)}，是否允许桌面导出={FormatBool(canUseDesktopDirectory)}";
    }

    /// <summary>
    /// 获取当前运行系统名称，优先使用 Godot 的平台名。
    /// </summary>
    private static string GetCurrentSystemName()
    {
        try
        {
            string systemName = Godot.OS.GetName();
            if (!string.IsNullOrWhiteSpace(systemName))
            {
                return systemName;
            }
        }
        catch
        {
            // Godot 平台接口不可用时退回 .NET 系统描述。
        }

        return string.IsNullOrWhiteSpace(System.Runtime.InteropServices.RuntimeInformation.OSDescription)
            ? "Unknown"
            : System.Runtime.InteropServices.RuntimeInformation.OSDescription;
    }

    /// <summary>
    /// 判断当前系统是否属于移动端。
    /// </summary>
    private static bool IsMobilePlatform(string systemName)
    {
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS())
        {
            return true;
        }

        return systemName.Contains("Android", StringComparison.OrdinalIgnoreCase)
               || systemName.Contains("iOS", StringComparison.OrdinalIgnoreCase)
               || systemName.Contains("iPhoneOS", StringComparison.OrdinalIgnoreCase)
               || systemName.Contains("iPadOS", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断当前平台是否允许优先尝试桌面目录。
    /// </summary>
    private static bool CanUseDesktopDirectory(bool isMobile)
    {
        if (isMobile || OperatingSystem.IsBrowser())
        {
            return false;
        }

        return OperatingSystem.IsWindows()
               || OperatingSystem.IsMacOS()
               || OperatingSystem.IsLinux()
               || OperatingSystem.IsFreeBSD();
    }

    /// <summary>
    /// 判断当前系统是否允许诊断导出；移动端允许导出到用户数据目录，浏览器环境不支持本地文件写入。
    /// </summary>
    private static bool CanExportOnCurrentSystem()
    {
        return !OperatingSystem.IsBrowser();
    }

    /// <summary>
    /// 把布尔值格式化成日志中更容易读懂的中文。
    /// </summary>
    private static string FormatBool(bool value)
    {
        return value ? "是" : "否";
    }
}
