namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 球盒页选中的充能球和重复发放数量。
/// </summary>
public sealed class OrbSelectionInfo
{
    public required OrbDisplayInfo Info { get; init; }

    public int Count { get; set; }
}
