namespace TheBookOfAges.TheBookOfAgesCode.Models;

using TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// 能力批量操作结果，用来给页面显示成功、跳过和失败数量。
/// </summary>
public sealed class PowerApplyResult
{
    public int SuccessCount { get; set; }

    public int SkippedCount { get; set; }

    public int FailedCount { get; set; }

    public List<string> Messages { get; } = [];

    /// <summary>
    /// 汇总本次能力发放结果，给 GM 页面底部提示使用。
    /// </summary>
    public string BuildGrantSummary()
    {
        return GmUiText.BuildPowerGrantSummary(SuccessCount, SkippedCount, FailedCount);
    }

    /// <summary>
    /// 汇总本次能力删除结果（按选中能力删除），给 GM 页面底部提示使用。
    /// </summary>
    public string BuildClearSummary()
    {
        return GmUiText.BuildPowerClearSummary(SuccessCount, SkippedCount, FailedCount);
    }

    /// <summary>
    /// 汇总本次能力清除（一键清空目标全部能力）结果，给 GM 页面底部提示使用。
    /// </summary>
    public string BuildWipeSummary()
    {
        return GmUiText.BuildPowerWipeSummary(SuccessCount, SkippedCount, FailedCount);
    }
}
