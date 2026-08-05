using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using TheBookOfAges.TheBookOfAgesCode.Models;

namespace TheBookOfAges.TheBookOfAgesCode.GameActions;

/// <summary>
/// GM 战斗目标引用：联机同步时不直接传 Creature 实例，只传玩家目标标记或战斗内稳定 CombatId。
/// </summary>
/// <param name="IsPlayerTarget">是否指向本动作所属玩家。</param>
/// <param name="CombatId">怪物目标的战斗内稳定 ID。</param>
public readonly record struct GmCreatureTargetRef(bool IsPlayerTarget, uint CombatId)
{
    private const uint InvalidCombatId = uint.MaxValue;

    /// <summary>
    /// 当前引用是否可用于网络动作。
    /// </summary>
    public bool IsValid => IsPlayerTarget || CombatId != InvalidCombatId;

    /// <summary>
    /// 从 UI 选择的目标生成网络动作可传输的稳定引用。
    /// </summary>
    /// <param name="target">UI 层当前选中的目标。</param>
    public static GmCreatureTargetRef FromTarget(CreatureTargetInfo target)
    {
        Creature creature = target.Creature;
        if (creature.IsPlayer)
        {
            return ForPlayer();
        }

        return creature.CombatId.HasValue
            ? new GmCreatureTargetRef(false, creature.CombatId.Value)
            : new GmCreatureTargetRef(false, InvalidCombatId);
    }

    /// <summary>
    /// 创建指向本动作所属玩家的目标引用。
    /// </summary>
    public static GmCreatureTargetRef ForPlayer()
    {
        return new GmCreatureTargetRef(true, 0);
    }

    /// <summary>
    /// 在动作执行端把目标引用解析回当前战斗里的 Creature。
    /// </summary>
    /// <param name="player">动作所属玩家。</param>
    /// <param name="combatState">当前战斗状态。</param>
    public Creature? Resolve(Player player, ICombatState combatState)
    {
        if (IsPlayerTarget)
        {
            Creature? playerCreature = combatState.GetPlayer(player.NetId)?.Creature ?? player.Creature;
            return playerCreature?.CombatState == combatState && combatState.ContainsCreature(playerCreature)
                ? playerCreature
                : null;
        }

        Creature? creature = combatState.GetCreature(CombatId);
        if (creature == null || !creature.IsMonster || !creature.IsAlive)
        {
            return null;
        }

        return creature;
    }
}
