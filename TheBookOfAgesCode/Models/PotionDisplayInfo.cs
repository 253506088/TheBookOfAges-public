using Godot;
using MegaCrit.Sts2.Core.Models;

namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 药水列表展示数据，避免 UI 直接依赖复杂的游戏对象。
/// </summary>
public sealed class PotionDisplayInfo
{
    public required ModelId Id { get; init; }

    public required string IdText { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string RarityKey { get; init; }

    public required string Rarity { get; init; }

    public required string CategoryKey { get; init; }

    public required string Category { get; init; }

    public required string SourceKey { get; init; }

    public required string Source { get; init; }

    public required Texture2D? Icon { get; init; }

    public required PotionModel Potion { get; init; }
}
