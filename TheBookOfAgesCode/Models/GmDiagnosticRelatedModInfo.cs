namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 诊断包相关 Mod 的自动识别置信度。
/// </summary>
public enum GmDiagnosticRelatedModConfidence
{
    /// <summary>
    /// 低置信度，只能作为 README 里的参考，不追加到文件名。
    /// </summary>
    Low,

    /// <summary>
    /// 中置信度，可以追加到文件名。
    /// </summary>
    Medium,

    /// <summary>
    /// 高置信度，可以追加到文件名。
    /// </summary>
    High,
}

/// <summary>
/// 诊断包相关 Mod 的自动识别结果。
/// </summary>
public sealed class GmDiagnosticRelatedModInfo
{
    /// <summary>
    /// 创建一个相关 Mod 识别结果。
    /// </summary>
    public GmDiagnosticRelatedModInfo(
        string modId,
        string displayName,
        string version,
        GmDiagnosticRelatedModConfidence confidence,
        string reason,
        string evidence,
        int score)
    {
        ModId = string.IsNullOrWhiteSpace(modId) ? "Unknown" : modId;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? ModId : displayName;
        Version = string.IsNullOrWhiteSpace(version) ? "Unknown" : version;
        Confidence = confidence;
        Reason = string.IsNullOrWhiteSpace(reason) ? "未记录判断依据" : reason;
        Evidence = string.IsNullOrWhiteSpace(evidence) ? "无证据片段" : evidence;
        Score = score;
    }

    /// <summary>
    /// Mod 清单里的 id。
    /// </summary>
    public string ModId { get; }

    /// <summary>
    /// Mod 清单里的展示名，优先使用 json 里的 name。
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Mod 清单里的版本号。
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// 自动识别置信度。
    /// </summary>
    public GmDiagnosticRelatedModConfidence Confidence { get; }

    /// <summary>
    /// 判断依据。
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// 命中的证据片段。
    /// </summary>
    public string Evidence { get; }

    /// <summary>
    /// 排序分数，值越大越可能相关。
    /// </summary>
    public int Score { get; }

    /// <summary>
    /// 是否足够可靠，可以写进压缩包文件名。
    /// </summary>
    public bool CanUseInFileName()
    {
        return Confidence is GmDiagnosticRelatedModConfidence.High or GmDiagnosticRelatedModConfidence.Medium;
    }
}
