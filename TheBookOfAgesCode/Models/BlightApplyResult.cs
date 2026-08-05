using TheBookOfAges.TheBookOfAgesCode.UI;

namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 荒疫添加或移除的批量执行结果。
/// </summary>
public sealed class BlightApplyResult
{
    public int SuccessCount { get; set; }

    public int SkippedCount { get; set; }

    public int FailedCount { get; set; }

    public List<string> Messages { get; } = [];

    /// <summary>
    /// 汇总荒疫添加结果，优先展示具体失败原因。
    /// </summary>
    public string BuildAddSummary()
    {
        if (Messages.Count > 0 && FailedCount > 0)
        {
            return string.Join("\n", Messages.Take(3));
        }

        return GmUiText.BuildBlightAddSummary(SuccessCount, SkippedCount, FailedCount);
    }

    /// <summary>
    /// 汇总荒疫移除结果，优先展示具体失败原因。
    /// </summary>
    public string BuildRemoveSummary()
    {
        if (Messages.Count > 0 && FailedCount > 0)
        {
            return string.Join("\n", Messages.Take(3));
        }

        return GmUiText.BuildBlightRemoveSummary(SuccessCount, SkippedCount, FailedCount);
    }
}
