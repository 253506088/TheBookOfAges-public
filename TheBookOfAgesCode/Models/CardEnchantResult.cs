using TheBookOfAges.TheBookOfAgesCode.UI;

namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 卡片附魔结果，用来给页面显示成功、跳过和失败数量。
/// </summary>
public sealed class CardEnchantResult
{
    /// <summary>
    /// 成功附魔的卡片数量。
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// 因已有附魔或游戏规则不允许而跳过的卡片数量。
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// 执行异常或卡片状态失效导致失败的数量。
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// 详细结果消息，后续需要时可用于调试面板展示。
    /// </summary>
    public List<string> Messages { get; } = [];

    /// <summary>
    /// 汇总本次卡片附魔结果，给 GM 页面底部提示使用。
    /// </summary>
    public string BuildSummary()
    {
        return GmUiText.BuildCardEnchantSummary(SuccessCount, SkippedCount, FailedCount);
    }
}
