namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 岁月史书地图房间选项，用于把当前地图节点展示成可点击按钮。
/// </summary>
public sealed record ChronicleMapRoomOption(
    string Id,
    string Name,
    string Detail,
    bool IsVisited,
    bool IsCurrent);
