namespace TheBookOfAges.TheBookOfAgesCode.Models;

using TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// 充能球发放结果，用来给页面显示成功、跳过和失败数量。
/// </summary>
public sealed class OrbGrantResult
{
    public int SuccessCount { get; set; }

    public int SkippedCount { get; set; }

    public int FailedCount { get; set; }

    public List<string> Messages { get; } = [];

    /// <summary>
    /// 汇总本次充能球发放结果，给 GM 页面底部提示使用。
    /// </summary>
    public string BuildSummary()
    {
        return GmUiText.BuildOrbGrantSummary(SuccessCount, SkippedCount, FailedCount);
    }
}
