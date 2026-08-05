using Godot;
using MegaCrit.Sts2.Core.Models;

namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 怪物列表展示数据，避免 UI 直接依赖复杂的游戏怪物对象。
/// </summary>
public sealed class MonsterDisplayInfo
{
    public required ModelId Id { get; init; }

    public required string IdText { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyList<string> TierKeys { get; init; }

    public required string TierText { get; init; }

    public required string SourceKey { get; init; }

    public required string Source { get; init; }

    public required int MinHp { get; init; }

    public required int MaxHp { get; init; }

    public required Texture2D? PreviewTexture { get; init; }

    public required MonsterModel Monster { get; init; }
}
