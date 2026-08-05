using Godot;
using MegaCrit.Sts2.Core.Models;

namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 能力列表展示数据，避免 UI 直接依赖复杂的游戏能力对象。
/// </summary>
public sealed class PowerDisplayInfo
{
    public required ModelId Id { get; init; }

    public required string IdText { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string TypeKey { get; init; }

    public required string TypeText { get; init; }

    public required string SourceKey { get; init; }

    public required string Source { get; init; }

    public required bool AllowNegative { get; init; }

    public required Texture2D? Icon { get; init; }

    public required PowerModel Power { get; init; }
}
