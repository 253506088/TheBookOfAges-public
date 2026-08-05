namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 事件列表展示数据，避免 UI 直接依赖复杂的游戏事件对象。
/// 只保留页面渲染和触发所需的最小字段。
/// </summary>
public sealed class EventDisplayInfo
{
    /// <summary>
    /// 事件完整 ID 文本，用于页面展示、搜索和日志排查。
    /// </summary>
    public required string IdText { get; init; }

    /// <summary>
    /// 事件控制台命令 ID 文本，传给 event/ancient 控制台命令时使用。
    /// </summary>
    public required string CommandIdText { get; init; }

    /// <summary>
    /// 事件本地化名称，中文用户看中文、英文用户看英文。
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 事件分类稳定键值，用于筛选和排序，不直接展示给玩家。
    /// </summary>
    public required string CategoryKey { get; init; }

    /// <summary>
    /// 事件分类文本，例如"先古之民事件"、"?房间事件"、"Mod 事件"。
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// 事件来源稳定键值，用于筛选和排序，不直接展示给玩家。
    /// </summary>
    public required string SourceKey { get; init; }

    /// <summary>
    /// 事件来源文本，例如"游戏本体"或具体 Mod 名称。
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// 是否是先古之民事件，触发时使用不同的控制台命令。
    /// true = AncientConsoleCmd，false = EventConsoleCmd。
    /// </summary>
    public required bool IsAncient { get; init; }
}
