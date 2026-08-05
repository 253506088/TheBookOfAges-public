using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace TheBookOfAges.TheBookOfAgesCode.GameActions;

/// <summary>
/// GM 能力删除网络动作：联机战斗中按目标引用删除指定能力或清空全部能力。
/// </summary>
public sealed class GmPowerRemoveAction : GameAction
{
    private readonly Player _player;
    private readonly IReadOnlyList<GmCreatureTargetRef> _targets;
    private readonly IReadOnlyList<string> _powerIds;
    private readonly GmPowerRemoveMode _mode;

    /// <summary>
    /// 当前网络动作所属玩家。
    /// </summary>
    public override ulong OwnerId => _player.NetId;

    /// <summary>
    /// 能力删除必须在战斗出牌阶段同步执行。
    /// </summary>
    public override GameActionType ActionType => GameActionType.CombatPlayPhaseOnly;

    /// <summary>
    /// 创建 GM 能力删除网络动作。
    /// </summary>
    /// <param name="player">提交动作的玩家。</param>
    /// <param name="targets">要操作的战斗目标引用。</param>
    /// <param name="powerIds">按选中能力删除时的能力 ID 列表。</param>
    /// <param name="mode">删除模式。</param>
    public GmPowerRemoveAction(Player player, IReadOnlyList<GmCreatureTargetRef> targets, IReadOnlyList<string> powerIds, GmPowerRemoveMode mode)
    {
        _player = player;
        _targets = targets.ToList();
        _powerIds = powerIds.ToList();
        _mode = mode;
    }

    /// <summary>
    /// 在本机执行网络动作；执行端重新解析目标和当前能力实例。
    /// </summary>
    protected override async Task ExecuteAction()
    {
        ICombatState? combatState = _player.Creature?.CombatState ?? CombatManager.Instance?.DebugOnlyGetState();
        if (combatState == null)
        {
            MainFile.Logger.Info($"GM 多人能力删除执行跳过：玩家NetId={_player.NetId}，原因=CombatState 为空。");
            return;
        }

        int removedCount = 0;
        foreach (GmCreatureTargetRef targetRef in _targets)
        {
            Creature? target = targetRef.Resolve(_player, combatState);
            if (target == null)
            {
                MainFile.Logger.Info($"GM 多人能力删除跳过无效目标：玩家NetId={_player.NetId}，target={targetRef}。");
                continue;
            }

            removedCount += _mode == GmPowerRemoveMode.AllCurrentPowers
                ? await RemoveAllPowersAsync(target)
                : await RemoveSelectedPowersAsync(target, _powerIds);
        }

        MainFile.Logger.Info($"GM 多人能力删除网络动作执行完成：玩家NetId={_player.NetId}，模式={_mode}，目标数量={_targets.Count}，能力数量={_powerIds.Count}，实际删除={removedCount}。");
    }

    /// <summary>
    /// 把本地动作转换成可同步的网络动作数据。
    /// </summary>
    public override INetAction ToNetAction()
    {
        return new NetGmPowerRemoveAction
        {
            Targets = _targets.ToList(),
            PowerIds = _powerIds.ToList(),
            Mode = _mode
        };
    }

    /// <summary>
    /// 返回便于日志定位的动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"GmPowerRemoveAction player {_player.NetId} mode {_mode} targets [{string.Join(",", _targets)}] powers [{string.Join(",", _powerIds)}]";
    }

    /// <summary>
    /// 删除目标身上的指定能力。
    /// </summary>
    /// <param name="target">要操作的目标。</param>
    /// <param name="powerIds">要删除的能力 ID 列表。</param>
    private static async Task<int> RemoveSelectedPowersAsync(Creature target, IReadOnlyList<string> powerIds)
    {
        int removedCount = 0;
        foreach (string powerId in powerIds)
        {
            PowerModel? canonicalPower = FindPower(powerId);
            if (canonicalPower == null)
            {
                MainFile.Logger.Info($"GM 多人能力删除跳过未知能力：powerId={powerId}。");
                continue;
            }

            PowerModel? existing = target.GetPowerById(canonicalPower.Id);
            if (existing == null)
            {
                MainFile.Logger.Info($"GM 多人能力删除跳过不存在能力：目标={SafeCreatureName(target)}，能力={canonicalPower.Id}。");
                continue;
            }

            if (await TryRemovePowerAsync(existing))
            {
                removedCount++;
            }
        }

        return removedCount;
    }

    /// <summary>
    /// 清空目标当前所有能力。
    /// </summary>
    /// <param name="target">要操作的目标。</param>
    private static async Task<int> RemoveAllPowersAsync(Creature target)
    {
        List<PowerModel> powersSnapshot = target.Powers.ToList();
        int removedCount = 0;
        foreach (PowerModel power in powersSnapshot)
        {
            if (await TryRemovePowerAsync(power))
            {
                removedCount++;
            }
        }

        return removedCount;
    }

    /// <summary>
    /// 尝试移除单个能力；原生命令失败时在同步动作执行端兜底。
    /// </summary>
    /// <param name="power">要移除的能力实例。</param>
    private static async Task<bool> TryRemovePowerAsync(PowerModel power)
    {
        try
        {
            await PowerCmd.Remove(power);
            MainFile.Logger.Info($"GM 多人能力删除成功：能力={power.Id}。");
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 多人能力删除命令失败，准备兜底：能力={power.Id}，异常={ex.Message}。");
        }

        try
        {
            Creature oldOwner = power.Owner;
            power.RemoveInternal();
            await power.AfterRemoved(oldOwner);
            MainFile.Logger.Info($"GM 多人能力删除直接兜底成功：能力={power.Id}。");
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 多人能力删除直接兜底失败：能力={power.Id}，异常={ex.Message}。");
            return false;
        }
    }

    /// <summary>
    /// 按字符串 ID 查找能力模型。
    /// </summary>
    /// <param name="powerId">能力 ModelId 字符串。</param>
    private static PowerModel? FindPower(string powerId)
    {
        return ModelDb.AllPowers.FirstOrDefault(power =>
            power != null &&
            string.Equals(power.Id.ToString(), powerId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 安全获取目标名称，避免日志因本地化异常中断。
    /// </summary>
    /// <param name="creature">目标生物。</param>
    private static string SafeCreatureName(Creature creature)
    {
        try
        {
            return string.IsNullOrWhiteSpace(creature.Name) ? creature.ModelId.ToString() : creature.Name;
        }
        catch
        {
            return creature.ModelId.ToString();
        }
    }
}

/// <summary>
/// GM 能力删除模式。
/// </summary>
public enum GmPowerRemoveMode
{
    /// <summary>
    /// 只删除指定能力 ID。
    /// </summary>
    SelectedPowers = 1,

    /// <summary>
    /// 清空目标当前所有能力。
    /// </summary>
    AllCurrentPowers = 2,
}

/// <summary>
/// GM 能力删除网络数据：同步目标引用、能力 ID 列表和删除模式。
/// </summary>
public struct NetGmPowerRemoveAction : INetAction
{
    /// <summary>
    /// 要操作的目标引用列表。
    /// </summary>
    public List<GmCreatureTargetRef> Targets { get; set; }

    /// <summary>
    /// 要删除的能力 ID 列表。
    /// </summary>
    public List<string> PowerIds { get; set; }

    /// <summary>
    /// 删除模式。
    /// </summary>
    public GmPowerRemoveMode Mode { get; set; }

    /// <summary>
    /// 根据网络数据还原成本地可执行动作。
    /// </summary>
    /// <param name="player">动作所属玩家。</param>
    public GameAction ToGameAction(Player player)
    {
        return new GmPowerRemoveAction(player, Targets ?? [], PowerIds ?? [], Mode);
    }

    /// <summary>
    /// 序列化能力删除请求。
    /// </summary>
    /// <param name="writer">游戏网络包写入器。</param>
    public void Serialize(PacketWriter writer)
    {
        IReadOnlyList<GmCreatureTargetRef> targets = Targets ?? [];
        IReadOnlyList<string> powerIds = PowerIds ?? [];
        writer.WriteInt((int)Mode, 8);
        writer.WriteInt(targets.Count, 16);
        foreach (GmCreatureTargetRef target in targets)
        {
            writer.WriteBool(target.IsPlayerTarget);
            writer.WriteUInt(target.CombatId, 32);
        }

        writer.WriteInt(powerIds.Count, 16);
        foreach (string powerId in powerIds)
        {
            writer.WriteString(powerId ?? string.Empty);
        }
    }

    /// <summary>
    /// 反序列化能力删除请求。
    /// </summary>
    /// <param name="reader">游戏网络包读取器。</param>
    public void Deserialize(PacketReader reader)
    {
        Mode = (GmPowerRemoveMode)reader.ReadInt(8);
        int targetCount = reader.ReadInt(16);
        Targets = new List<GmCreatureTargetRef>(targetCount);
        for (int i = 0; i < targetCount; i++)
        {
            bool isPlayerTarget = reader.ReadBool();
            uint combatId = reader.ReadUInt(32);
            Targets.Add(new GmCreatureTargetRef(isPlayerTarget, combatId));
        }

        int powerCount = reader.ReadInt(16);
        PowerIds = new List<string>(powerCount);
        for (int i = 0; i < powerCount; i++)
        {
            PowerIds.Add(reader.ReadString());
        }
    }

    /// <summary>
    /// 返回便于日志定位的网络动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"NetGmPowerRemoveAction mode {Mode} targets [{string.Join(",", Targets ?? [])}] powers [{string.Join(",", PowerIds ?? [])}]";
    }
}
