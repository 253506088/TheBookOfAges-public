namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 药水选择数据，用来支持同一种药水重复选择多次。
/// </summary>
public sealed class PotionSelectionInfo
{
    public required PotionDisplayInfo Info { get; init; }

    public int Count { get; set; }
}
