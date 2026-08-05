using Godot;

namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 充能球列表展示数据，避免 UI 直接依赖可能变化的游戏内部充能球类型。
/// </summary>
public sealed class OrbDisplayInfo
{
    public required string IdText { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string SourceKey { get; init; }

    public required string Source { get; init; }

    public required Texture2D? Icon { get; init; }

    public required object OrbModel { get; init; }
}
