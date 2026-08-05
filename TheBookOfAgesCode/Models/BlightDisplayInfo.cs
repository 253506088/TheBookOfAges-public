using Godot;
using MegaCrit.Sts2.Core.Models;

namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 荒疫列表展示数据；底层对应游戏里的 ModifierModel，UI 统一用“荒疫”这个 GM 名称展示。
/// </summary>
public sealed class BlightDisplayInfo
{
    public required ModelId Id { get; init; }

    public required string IdText { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string OptionTitle { get; init; }

    public required string OptionDescription { get; init; }

    public required string CategoryKey { get; init; }

    public required string Category { get; init; }

    public required string SourceKey { get; init; }

    public required string Source { get; init; }

    public required bool IsEnabled { get; init; }

    public required Texture2D? Icon { get; init; }

    public required ModifierModel Modifier { get; init; }
}
