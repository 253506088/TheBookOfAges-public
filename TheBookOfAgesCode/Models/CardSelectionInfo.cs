namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 卡片选择数据，用来支持同一张卡片重复选择多次。
/// 左键点击 +1，Ctrl+左键 +5，右键清零。
/// </summary>
public sealed class CardSelectionInfo
{
    public required CardDisplayInfo Info { get; init; }

    public int Count { get; set; }
}
