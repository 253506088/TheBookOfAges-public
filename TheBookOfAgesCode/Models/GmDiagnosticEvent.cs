using MegaCrit.Sts2.Core.Logging;

namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// GM 诊断导出的触发类型。
/// </summary>
public enum GmDiagnosticEventKind
{
    /// <summary>
    /// 普通错误日志触发。
    /// </summary>
    ErrorLog,

    /// <summary>
    /// 日志关键词触发。
    /// </summary>
    KeywordLog,

    /// <summary>
    /// 达到玩家配置的日志等级触发。
    /// </summary>
    ConfiguredLogLevel,

    /// <summary>
    /// 联机不同步或联机校验失败触发。
    /// </summary>
    MultiplayerDesync,

    /// <summary>
    /// 上次游戏会话疑似异常退出触发。
    /// </summary>
    PreviousCrash,

    /// <summary>
    /// 未处理异常触发。
    /// </summary>
    UnhandledException,

    /// <summary>
    /// 未观察到的异步任务异常触发。
    /// </summary>
    UnobservedTaskException,
}

/// <summary>
/// GM 诊断导出的触发事件，用于在日志包里写清楚为什么生成本次诊断包。
/// </summary>
public sealed class GmDiagnosticEvent
{
    /// <summary>
    /// 创建一个诊断触发事件。
    /// </summary>
    public GmDiagnosticEvent(GmDiagnosticEventKind kind, string source, string message, LogLevel? logLevel = null)
    {
        Kind = kind;
        Source = source;
        Message = string.IsNullOrWhiteSpace(message) ? "无详细信息" : message;
        LogLevel = logLevel;
        CreatedAt = DateTimeOffset.Now;
    }

    /// <summary>
    /// 触发类型。
    /// </summary>
    public GmDiagnosticEventKind Kind { get; }

    /// <summary>
    /// 触发来源，例如全局日志、日志文件扫描或未处理异常。
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// 触发时捕获到的原始消息。
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 如果来自游戏日志，这里记录日志级别。
    /// </summary>
    public LogLevel? LogLevel { get; }

    /// <summary>
    /// 触发时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// 构建防抖签名，避免同一条异常短时间内刷出多个诊断包。
    /// </summary>
    public string BuildDeduplicationKey()
    {
        string normalizedMessage = NormalizeMessageForDeduplication(Message);
        if (normalizedMessage.Length > 180)
        {
            normalizedMessage = normalizedMessage[..180];
        }

        return $"{Kind}|{normalizedMessage}";
    }

    /// <summary>
    /// 归一化防抖用的日志文本；同一条日志从全局回调和 godot.log 轮询进入时，前缀可能不同。
    /// </summary>
    private static string NormalizeMessageForDeduplication(string message)
    {
        string normalized = message.Replace("\r", string.Empty).Replace("\n", " ").Trim();
        normalized = RemoveLogLevelPrefix(normalized);
        while (normalized.Contains("  ", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        }

        return normalized;
    }

    /// <summary>
    /// 去掉日志文件里常见的等级前缀，避免同一条 Error 因来源不同绕过防抖。
    /// </summary>
    private static string RemoveLogLevelPrefix(string message)
    {
        string normalized = message.TrimStart();
        foreach (string prefix in GetLogLevelPrefixes())
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return normalized[prefix.Length..].TrimStart();
            }
        }

        return normalized;
    }

    /// <summary>
    /// 获取可被防抖归一化忽略的日志等级前缀。
    /// </summary>
    private static IReadOnlyList<string> GetLogLevelPrefixes()
    {
        return ["[ERROR]", "[ERR]", "[WARN]", "[WARNING]", "[INFO]", "ERROR:", "ERR:", "WARN:", "WARNING:", "INFO:"];
    }
}
