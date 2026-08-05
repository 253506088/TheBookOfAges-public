using MegaCrit.Sts2.Core.Logging;
using TheBookOfAges.TheBookOfAgesCode.Config;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// 统一控制 GM Mod 的日志输出。
/// </summary>
public sealed class GmLogger
{
    private readonly Logger _logger;

    /// <summary>
    /// 创建受配置控制的日志输出器。
    /// </summary>
    public GmLogger(string modId)
    {
        _logger = new Logger(modId, LogType.Generic);
    }

    /// <summary>
    /// 重新读取日志配置。
    /// </summary>
    public void ReloadConfig()
    {
        GmModConfig.Reload();
    }

    /// <summary>
    /// 输出普通信息日志；配置关闭时直接跳过。
    /// </summary>
    public void Info(string message)
    {
        if (!GmModConfig.IsLogEnabled)
        {
            return;
        }

        _logger.Info(message);
    }

    /// <summary>
    /// 输出错误日志；配置关闭时仍允许输出，保证诊断导出测试和异常排查可触发。
    /// </summary>
    public void Error(string message)
    {
        _logger.Error(message);
    }
}
