using MegaCrit.Sts2.Core.Entities.Creatures;

namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 能力操作目标数据，用于在 UI 层展示玩家或怪物目标。
/// </summary>
public sealed class CreatureTargetInfo
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required Creature Creature { get; init; }
}
