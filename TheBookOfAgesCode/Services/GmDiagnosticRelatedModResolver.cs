using System.Text;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Modding;
using TheBookOfAges.TheBookOfAgesCode.Models;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// GM 诊断相关 Mod 识别器：根据异常文本和最近日志上下文保守推断疑似相关 Mod。
/// </summary>
public sealed class GmDiagnosticRelatedModResolver
{
    private const int MaxResultCount = 3;
    private const int MaxEvidenceLength = 220;
    private const int ContextScorePenalty = 45;
    private static readonly string[] ContextNoiseMarkers =
    [
        "Found mod manifest file",
        "Looking for mods to load",
        "Loading workshop mod",
        "Scanning mod",
    ];

    /// <summary>
    /// 根据诊断事件和最近日志上下文解析疑似相关 Mod。
    /// </summary>
    public IReadOnlyList<GmDiagnosticRelatedModInfo> Resolve(GmDiagnosticEvent diagnosticEvent, string recentLogContext)
    {
        string eventText = BuildEventText(diagnosticEvent);
        string contextText = FilterRecentLogContext(recentLogContext);
        if (string.IsNullOrWhiteSpace(eventText) && string.IsNullOrWhiteSpace(contextText))
        {
            return Array.Empty<GmDiagnosticRelatedModInfo>();
        }

        List<GmDiagnosticRelatedModInfo> results = [];
        foreach (Mod mod in TryGetMods())
        {
            GmDiagnosticRelatedModInfo? info = TryMatchMod(mod, eventText, contextText);
            if (info != null)
            {
                results.Add(info);
            }
        }

        return results
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResultCount)
            .ToArray();
    }

    /// <summary>
    /// 拼接触发事件自身文本。主证据权重最高，不能被普通日志上下文反超。
    /// </summary>
    private static string BuildEventText(GmDiagnosticEvent diagnosticEvent)
    {
        StringBuilder builder = new();
        builder.AppendLine(diagnosticEvent.Source);
        builder.AppendLine(diagnosticEvent.Message);
        return builder.ToString();
    }

    /// <summary>
    /// 尝试匹配单个 Mod。
    /// </summary>
    private static GmDiagnosticRelatedModInfo? TryMatchMod(Mod mod, string eventText, string contextText)
    {
        ModManifest? manifest = mod.manifest;
        string modId = NormalizeToken(manifest?.id);
        string displayName = NormalizeToken(manifest?.name);
        string version = NormalizeToken(manifest?.version);
        string path = NormalizeToken(mod.path);

        MatchResult best = MatchResult.Empty;
        best = ChooseBetter(best, MatchLogContextPrefix(eventText, modId));
        best = ChooseBetter(best, MatchPath(eventText, path, modId, "触发消息中出现 Mod 路径。"));
        best = ChooseBetter(best, MatchDllName(eventText, modId, "触发消息中出现 Mod DLL 文件名。"));
        best = ChooseBetter(best, MatchWholeToken(eventText, modId, 95, "触发消息中出现 Mod ID。"));
        best = ChooseBetter(best, MatchDisplayName(eventText, displayName, "触发消息中出现 Mod 展示名。"));
        best = ChooseBetter(best, MatchNamespace(eventText, modId, "触发消息堆栈中出现 Mod 命名空间。"));
        best = ChooseBetter(best, PenalizeContextMatch(MatchPath(contextText, path, modId, "最近日志上下文中出现 Mod 路径。")));
        best = ChooseBetter(best, PenalizeContextMatch(MatchDllName(contextText, modId, "最近日志上下文中出现 Mod DLL 文件名。")));
        best = ChooseBetter(best, PenalizeContextMatch(MatchWholeToken(contextText, modId, 90, "最近日志上下文中出现 Mod ID。")));
        best = ChooseBetter(best, PenalizeContextMatch(MatchDisplayName(contextText, displayName, "最近日志上下文中出现 Mod 展示名。")));
        best = ChooseBetter(best, PenalizeContextMatch(MatchNamespace(contextText, modId, "最近日志上下文堆栈中出现 Mod 命名空间。")));

        if (best.Score <= 0)
        {
            return null;
        }

        return new GmDiagnosticRelatedModInfo(
            modId,
            string.IsNullOrWhiteSpace(displayName) ? modId : displayName,
            version,
            GetConfidence(best.Score),
            best.Reason,
            GmDiagnosticTextSanitizer.Sanitize(best.Evidence),
            best.Score);
    }

    /// <summary>
    /// 根据分数换算置信度。
    /// </summary>
    private static GmDiagnosticRelatedModConfidence GetConfidence(int score)
    {
        if (score >= 80)
        {
            return GmDiagnosticRelatedModConfidence.High;
        }

        if (score >= 55)
        {
            return GmDiagnosticRelatedModConfidence.Medium;
        }

        return GmDiagnosticRelatedModConfidence.Low;
    }

    /// <summary>
    /// 匹配 Mod 路径或路径尾部目录名。
    /// </summary>
    private static MatchResult MatchPath(string text, string path, string modId, string reason)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(path))
        {
            return MatchResult.Empty;
        }

        string normalizedText = NormalizePathText(text);
        string normalizedPath = NormalizePathText(path);
        if (IsUsefulToken(normalizedPath) && normalizedText.Contains(normalizedPath, StringComparison.OrdinalIgnoreCase))
        {
            return new MatchResult(100, reason, BuildEvidence(text, path));
        }

        string directoryName = NormalizeToken(Path.GetFileName(path.TrimEnd('\\', '/')));
        if (!string.Equals(directoryName, modId, StringComparison.OrdinalIgnoreCase)
            && IsUsefulToken(directoryName)
            && ContainsWholeToken(text, directoryName))
        {
            return new MatchResult(80, reason.Replace("路径", "目录名", StringComparison.Ordinal), BuildEvidence(text, directoryName));
        }

        return MatchResult.Empty;
    }

    /// <summary>
    /// 匹配 Mod DLL 文件名。
    /// </summary>
    private static MatchResult MatchDllName(string text, string modId, string reason)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsUsefulToken(modId))
        {
            return MatchResult.Empty;
        }

        string dllName = $"{modId}.dll";
        if (ContainsIgnoreCase(text, dllName))
        {
            return new MatchResult(98, reason, BuildEvidence(text, dllName));
        }

        return MatchResult.Empty;
    }

    /// <summary>
    /// 匹配 Mod 展示名。
    /// </summary>
    private static MatchResult MatchDisplayName(string text, string displayName, string reason)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsUsefulToken(displayName) || !ContainsIgnoreCase(text, displayName))
        {
            return MatchResult.Empty;
        }

        return new MatchResult(92, reason, BuildEvidence(text, displayName));
    }

    /// <summary>
    /// 匹配完整词 token。
    /// </summary>
    private static MatchResult MatchWholeToken(string text, string token, int score, string reason)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsUsefulToken(token) || !ContainsWholeToken(text, token))
        {
            return MatchResult.Empty;
        }

        return new MatchResult(score, reason, BuildEvidence(text, token));
    }

    /// <summary>
    /// 匹配命名空间形式，例如 TheBookOfAges.xxx。
    /// </summary>
    private static MatchResult MatchNamespace(string text, string modId, string reason)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsUsefulToken(modId))
        {
            return MatchResult.Empty;
        }

        string namespacePrefix = $"{modId}.";
        if (ContainsIgnoreCase(text, namespacePrefix))
        {
            return new MatchResult(94, reason, BuildEvidence(text, namespacePrefix));
        }

        return MatchResult.Empty;
    }

    /// <summary>
    /// 匹配日志前缀，例如 [TheBookOfAges]。
    /// </summary>
    private static MatchResult MatchLogContextPrefix(string text, string modId)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsUsefulToken(modId))
        {
            return MatchResult.Empty;
        }

        string prefix = $"[{modId}]";
        if (ContainsIgnoreCase(text, prefix))
        {
            return new MatchResult(120, "触发消息中出现 Mod 日志前缀。", BuildEvidence(text, prefix));
        }

        return MatchResult.Empty;
    }

    /// <summary>
    /// 最近日志上下文只能作为旁证，不能反超触发消息自身的证据。
    /// </summary>
    private static MatchResult PenalizeContextMatch(MatchResult result)
    {
        if (result.Score <= 0)
        {
            return result;
        }

        int score = Math.Max(1, result.Score - ContextScorePenalty);
        return result with { Score = score };
    }

    /// <summary>
    /// 选择更可信的匹配结果。
    /// </summary>
    private static MatchResult ChooseBetter(MatchResult current, MatchResult candidate)
    {
        return candidate.Score > current.Score ? candidate : current;
    }

    /// <summary>
    /// 构建证据片段。
    /// </summary>
    private static string BuildEvidence(string text, string token)
    {
        int index = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return token;
        }

        int start = Math.Max(0, index - 80);
        int length = Math.Min(text.Length - start, MaxEvidenceLength);
        string evidence = text.Substring(start, length).Replace("\r", " ").Replace("\n", " ").Trim();
        return evidence.Length > MaxEvidenceLength ? evidence[..MaxEvidenceLength] : evidence;
    }

    /// <summary>
    /// 判断是否包含大小写不敏感的文本。
    /// </summary>
    private static bool ContainsIgnoreCase(string text, string token)
    {
        return text.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 过滤最近日志里的普通 Mod 扫描行，避免把“发现某 Mod 清单”误当成异常来源。
    /// </summary>
    private static string FilterRecentLogContext(string recentLogContext)
    {
        if (string.IsNullOrWhiteSpace(recentLogContext))
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        foreach (string line in recentLogContext.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            if (ContextNoiseMarkers.Any(marker => line.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 判断是否包含完整词，避免很短片段误伤。
    /// </summary>
    private static bool ContainsWholeToken(string text, string token)
    {
        if (!IsUsefulToken(token))
        {
            return false;
        }

        string pattern = $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(token)}(?![\p{{L}}\p{{N}}_])";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// 判断 token 是否足够有辨识度。
    /// </summary>
    private static bool IsUsefulToken(string token)
    {
        return token.Trim().Length >= 4;
    }

    /// <summary>
    /// 规范化普通 token。
    /// </summary>
    private static string NormalizeToken(string? token)
    {
        return string.IsNullOrWhiteSpace(token) ? string.Empty : token.Trim();
    }

    /// <summary>
    /// 规范化路径文本，统一路径分隔符。
    /// </summary>
    private static string NormalizePathText(string text)
    {
        return text.Replace('\\', '/');
    }

    /// <summary>
    /// 安全获取当前 Mod 列表。
    /// </summary>
    private static IReadOnlyList<Mod> TryGetMods()
    {
        try
        {
            return ModManager.Mods;
        }
        catch
        {
            return Array.Empty<Mod>();
        }
    }

    /// <summary>
    /// 单次匹配结果。
    /// </summary>
    private readonly record struct MatchResult(int Score, string Reason, string Evidence)
    {
        /// <summary>
        /// 空匹配结果。
        /// </summary>
        public static MatchResult Empty => new(0, string.Empty, string.Empty);
    }
}
