using MegaCrit.Sts2.Core.Logging;
using TheBookOfAges.TheBookOfAgesCode.Config;
using TheBookOfAges.TheBookOfAgesCode.Models;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// GM 诊断触发匹配器：只负责判断日志文本是否值得导出诊断包。
/// </summary>
public static class GmDiagnosticTriggerMatcher
{
    private static readonly string[] MultiplayerKeywords =
    [
        "state divergence",
        "desync",
        "checksum",
        "StateDivergenceMessage",
        "ModelDb hash mismatch",
        "Model ID hash does not",
        "hash mismatch",
        "VersionMismatch",
        "ClientConnectionFailedException",
        "AttacksPlayed mismatch",
        "Tried to pop model",
        "Multiplayer run save validation failed",
        "Corrupt multiplayer run save detected",
        "Corrupt multiplayer run backup detected",
        "current_run_mp",
        ".corrupt",
        "CanonicalizeSave",
        "CanonicalizeExtendedRunData",
        "different state",
        "SaveManager.HasMultiplayerRunSave",
        "不同步",
        "数据不同步",
    ];

    private static readonly string[] CrashKeywords =
    [
        "Unhandled exception",
        "NullReferenceException",
        "InvalidOperationException",
        "AccessViolationException",
        "0xC0000005",
        "access violation",
        "native crash",
        "CrashDumps",
        "Fatal",
        "Crash",
        "log subsystem",
        "LogListener",
        "NLogWindow",
        "OpenLogWindowOnError",
        "ArgumentOutOfRangeException",
        "HarmonyException",
        "Patching exception",
        "Undefined target method",
        "Custom network message",
        "custom messages break",
    ];

    /// <summary>
    /// 根据游戏日志级别和消息内容尝试构建诊断事件。
    /// </summary>
    public static GmDiagnosticEvent? TryMatchLog(LogLevel level, string message, string source)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        if (!GmModConfig.ShouldExportDiagnosticForLogLevel(level))
        {
            return null;
        }

        if (IsMultiplayerIssue(message))
        {
            return new GmDiagnosticEvent(GmDiagnosticEventKind.MultiplayerDesync, source, message, level);
        }

        if (ContainsAny(message, CrashKeywords))
        {
            return new GmDiagnosticEvent(GmDiagnosticEventKind.KeywordLog, source, message, level);
        }

        if (level == LogLevel.Error)
        {
            return new GmDiagnosticEvent(GmDiagnosticEventKind.ErrorLog, source, message, level);
        }

        return new GmDiagnosticEvent(GmDiagnosticEventKind.ConfiguredLogLevel, source, message, level);
    }

    /// <summary>
    /// 根据普通文本尝试构建诊断事件，主要用于扫描 godot.log 尾部。
    /// </summary>
    public static GmDiagnosticEvent? TryMatchText(string message, string source)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        if (!TryGetTextLogLevel(message, out LogLevel level)
            || !GmModConfig.ShouldExportDiagnosticForLogLevel(level))
        {
            return null;
        }

        if (IsMultiplayerIssue(message))
        {
            return new GmDiagnosticEvent(GmDiagnosticEventKind.MultiplayerDesync, source, message, level);
        }

        if (ContainsAny(message, CrashKeywords))
        {
            return new GmDiagnosticEvent(GmDiagnosticEventKind.KeywordLog, source, message, level);
        }

        if (level == LogLevel.Error)
        {
            return new GmDiagnosticEvent(GmDiagnosticEventKind.ErrorLog, source, message, level);
        }

        return new GmDiagnosticEvent(GmDiagnosticEventKind.ConfiguredLogLevel, source, message, level);
    }

    /// <summary>
    /// 尝试从普通日志文本前缀解析日志等级；用于 godot.log 文件扫描，避免绕过玩家等级配置。
    /// </summary>
    private static bool TryGetTextLogLevel(string message, out LogLevel level)
    {
        string trimmedMessage = message.TrimStart();
        if (trimmedMessage.StartsWith("[ERROR]", StringComparison.OrdinalIgnoreCase)
            || trimmedMessage.StartsWith("[ERR]", StringComparison.OrdinalIgnoreCase)
            || trimmedMessage.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)
            || trimmedMessage.StartsWith("ERR:", StringComparison.OrdinalIgnoreCase))
        {
            level = LogLevel.Error;
            return true;
        }

        if (trimmedMessage.StartsWith("[WARN]", StringComparison.OrdinalIgnoreCase)
            || trimmedMessage.StartsWith("WARN:", StringComparison.OrdinalIgnoreCase)
            || trimmedMessage.StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase))
        {
            level = LogLevel.Warn;
            return true;
        }

        if (trimmedMessage.StartsWith("[INFO]", StringComparison.OrdinalIgnoreCase)
            || trimmedMessage.StartsWith("INFO:", StringComparison.OrdinalIgnoreCase))
        {
            level = LogLevel.Info;
            return true;
        }

        level = default;
        return false;
    }

    /// <summary>
    /// 判断文本是否命中联机不同步或联机校验失败。
    /// </summary>
    private static bool IsMultiplayerIssue(string message)
    {
        if (ContainsAny(message, MultiplayerKeywords))
        {
            return true;
        }

        return ContainsIgnoreCase(message, "mismatch")
               && ContainsIgnoreCase(message, "Host:")
               && ContainsIgnoreCase(message, "Client:");
    }

    /// <summary>
    /// 判断文本是否命中任意关键词。
    /// </summary>
    private static bool ContainsAny(string message, IEnumerable<string> keywords)
    {
        foreach (string keyword in keywords)
        {
            if (ContainsIgnoreCase(message, keyword))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 使用大小写不敏感的方式判断文本是否包含关键词。
    /// </summary>
    private static bool ContainsIgnoreCase(string message, string keyword)
    {
        return message.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}
