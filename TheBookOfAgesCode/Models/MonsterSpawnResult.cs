namespace TheBookOfAges.TheBookOfAgesCode.Models;

using TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// 怪物召唤结果，用来给页面显示成功和失败数量。
/// </summary>
public sealed class MonsterSpawnResult
{
    public int SuccessCount { get; set; }

    public int FailedCount { get; set; }

    public List<string> Messages { get; } = [];

    /// <summary>
    /// 汇总本次怪物召唤结果，给 GM 页面底部提示使用。
    /// </summary>
    public string BuildSummary()
    {
        return GmUiText.BuildMonsterSpawnSummary(SuccessCount, FailedCount);
    }
}
