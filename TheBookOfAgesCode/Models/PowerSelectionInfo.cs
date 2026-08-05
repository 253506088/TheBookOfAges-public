namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 能力选择数据，用来记录每个能力要增减的层数。
/// </summary>
public sealed class PowerSelectionInfo
{
    public required PowerDisplayInfo Info { get; init; }

    public int AmountDelta { get; set; }
}
