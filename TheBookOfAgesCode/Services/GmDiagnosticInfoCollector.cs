using System.Runtime.InteropServices;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Platform;
using TheBookOfAges.TheBookOfAgesCode.Models;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// GM 诊断信息收集器：独立收集游戏版本、系统版本和运行环境信息。
/// </summary>
public sealed class GmDiagnosticInfoCollector
{
    private readonly GmDiagnosticNameService _nameService;

    /// <summary>
    /// 创建诊断信息收集器。
    /// </summary>
    public GmDiagnosticInfoCollector(GmDiagnosticNameService nameService)
    {
        _nameService = nameService;
    }

    /// <summary>
    /// 构建游戏与系统信息文本。
    /// </summary>
    public string BuildInfoText(string modId, GmDiagnosticEvent diagnosticEvent)
    {
        StringBuilder builder = new();
        bool chinese = _nameService.IsChineseGameLanguage();

        AppendLine(builder, chinese ? "=== 游戏与系统信息 ===" : "=== Game And System Info ===");
        AppendLine(builder, $"{Label(chinese, "生成时间", "Generated At")}: {DateTimeOffset.Now:O}");
        AppendLine(builder, $"{Label(chinese, "触发类型", "Trigger Kind")}: {diagnosticEvent.Kind}");
        AppendLine(builder, $"{Label(chinese, "触发来源", "Trigger Source")}: {diagnosticEvent.Source}");
        AppendLine(builder, $"{Label(chinese, "当前 Mod ID", "Current Mod ID")}: {modId}");
        AppendLine(builder, string.Empty);

        AppendGameInfo(builder, chinese);
        AppendSystemInfo(builder, chinese);

        return GmDiagnosticTextSanitizer.Sanitize(builder.ToString());
    }

    /// <summary>
    /// 追加游戏版本相关信息。
    /// </summary>
    private void AppendGameInfo(StringBuilder builder, bool chinese)
    {
        AppendLine(builder, Label(chinese, "【游戏信息】", "[Game]"));
        ReleaseInfo? releaseInfo = TryGetReleaseInfo();
        AppendLine(builder, $"{Label(chinese, "游戏版本", "Game Version")}: {releaseInfo?.Version ?? "Unknown"}");
        AppendLine(builder, $"{Label(chinese, "游戏分支", "Game Branch")}: {releaseInfo?.Branch ?? "Unknown"}");
        AppendLine(builder, $"{Label(chinese, "提交版本", "Commit")}: {releaseInfo?.Commit ?? "Unknown"}");
        AppendLine(builder, $"{Label(chinese, "发布日期", "Release Date")}: {releaseInfo?.Date.ToString("O") ?? "Unknown"}");
        AppendLine(builder, $"{Label(chinese, "平台分支", "Platform Branch")}: {TryGetPlatformBranch()}");
        AppendLine(builder, $"{Label(chinese, "游戏语言", "Game Language")}: {_nameService.GetGameLanguage()}");
        AppendLine(builder, $"{Label(chinese, "语言候选", "Language Candidates")}: {_nameService.GetLanguageCandidatesText()}");
        AppendLine(builder, $"{Label(chinese, "Godot 版本", "Godot Version")}: {TryGetGodotVersion()}");
        AppendLine(builder, string.Empty);
    }

    /// <summary>
    /// 追加系统版本相关信息。
    /// </summary>
    private static void AppendSystemInfo(StringBuilder builder, bool chinese)
    {
        AppendLine(builder, Label(chinese, "【系统信息】", "[System]"));
        AppendLine(builder, $"{Label(chinese, "系统描述", "OS Description")}: {RuntimeInformation.OSDescription}");
        AppendLine(builder, $"{Label(chinese, "系统架构", "OS Architecture")}: {RuntimeInformation.OSArchitecture}");
        AppendLine(builder, $"{Label(chinese, ".NET 版本", ".NET Version")}: {RuntimeInformation.FrameworkDescription}");
        AppendLine(builder, $"{Label(chinese, "进程架构", "Process Architecture")}: {RuntimeInformation.ProcessArchitecture}");
    }

    /// <summary>
    /// 获取游戏 release_info 信息。
    /// </summary>
    private static ReleaseInfo? TryGetReleaseInfo()
    {
        try
        {
            return ReleaseInfoManager.Instance.ReleaseInfo;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取平台分支。
    /// </summary>
    private static string TryGetPlatformBranch()
    {
        try
        {
            object? branch = PlatformUtil.GetPlatformBranch();
            return branch?.ToString() ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// 获取 Godot 引擎版本。
    /// </summary>
    private static string TryGetGodotVersion()
    {
        try
        {
            return Engine.GetVersionInfo()["string"].AsString();
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// 根据语言选择标签。
    /// </summary>
    private static string Label(bool chinese, string zhs, string eng)
    {
        return chinese ? zhs : eng;
    }

    /// <summary>
    /// 追加一行文本。
    /// </summary>
    private static void AppendLine(StringBuilder builder, string line)
    {
        builder.AppendLine(line);
    }
}
