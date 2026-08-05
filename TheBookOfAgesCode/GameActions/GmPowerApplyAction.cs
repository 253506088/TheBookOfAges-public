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
/// GM 能力赋予网络动作：联机战斗中按目标引用和能力 ID 同步修改能力层数。
/// </summary>
public sealed class GmPowerApplyAction : GameAction
{
    private readonly Player _player;
    private readonly IReadOnlyList<GmCreatureTargetRef> _targets;
    private readonly IReadOnlyList<GmPowerApplyEntry> _entries;

    /// <summary>
    /// 当前网络动作所属玩家。
    /// </summary>
    public override ulong OwnerId => _player.NetId;

    /// <summary>
    /// 能力修改必须在战斗出牌阶段同步执行。
    /// </summary>
    public override GameActionType ActionType => GameActionType.CombatPlayPhaseOnly;

    /// <summary>
    /// 创建 GM 能力赋予网络动作。
    /// </summary>
    /// <param name="player">提交动作的玩家。</param>
    /// <param name="targets">要操作的战斗目标引用。</param>
    /// <param name="entries">要应用的能力 ID 和层数变化。</param>
    public GmPowerApplyAction(Player player, IReadOnlyList<GmCreatureTargetRef> targets, IReadOnlyList<GmPowerApplyEntry> entries)
    {
        _player = player;
        _targets = targets.ToList();
        _entries = entries.ToList();
    }

    /// <summary>
    /// 在本机执行网络动作；执行端重新解析目标和能力模型。
    /// </summary>
    protected override async Task ExecuteAction()
    {
        ICombatState? combatState = _player.Creature?.CombatState ?? CombatManager.Instance?.DebugOnlyGetState();
        if (combatState == null)
        {
            MainFile.Logger.Info($"GM 多人能力赋予执行跳过：玩家NetId={_player.NetId}，原因=CombatState 为空。");
            return;
        }

        int appliedCount = 0;
        foreach (GmCreatureTargetRef targetRef in _targets)
        {
            Creature? target = targetRef.Resolve(_player, combatState);
            if (target == null)
            {
                MainFile.Logger.Info($"GM 多人能力赋予跳过无效目标：玩家NetId={_player.NetId}，target={targetRef}。");
                continue;
            }

            foreach (GmPowerApplyEntry entry in _entries)
            {
                if (entry.AmountDelta == 0)
                {
                    continue;
                }

                PowerModel? canonicalPower = FindPower(entry.PowerId);
                if (canonicalPower == null)
                {
                    MainFile.Logger.Info($"GM 多人能力赋予跳过未知能力：powerId={entry.PowerId}。");
                    continue;
                }

                if (await TryApplyPowerDeltaAsync(target, canonicalPower, entry.AmountDelta))
                {
                    appliedCount++;
                }
            }
        }

        MainFile.Logger.Info($"GM 多人能力赋予网络动作执行完成：玩家NetId={_player.NetId}，目标数量={_targets.Count}，能力数量={_entries.Count}，实际成功={appliedCount}。");
    }

    /// <summary>
    /// 把本地动作转换成可同步的网络动作数据。
    /// </summary>
    public override INetAction ToNetAction()
    {
        return new NetGmPowerApplyAction
        {
            Targets = _targets.ToList(),
            Entries = _entries.ToList()
        };
    }

    /// <summary>
    /// 返回便于日志定位的动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"GmPowerApplyAction player {_player.NetId} targets [{string.Join(",", _targets)}] entries [{string.Join(",", _entries)}]";
    }

    /// <summary>
    /// 尝试对单个目标应用能力层数变化；原生命令失败时在同步动作执行端兜底。
    /// </summary>
    /// <param name="target">要操作的目标。</param>
    /// <param name="canonicalPower">能力 canonical model。</param>
    /// <param name="delta">层数变化。</param>
    private static async Task<bool> TryApplyPowerDeltaAsync(Creature target, PowerModel canonicalPower, int delta)
    {
        try
        {
            PowerModel? existing = target.GetPowerById(canonicalPower.Id);
            if (existing != null)
            {
                try
                {
                    await PowerCmd.ModifyAmount(new ThrowingPlayerChoiceContext(), existing, delta, null, null);
                    MainFile.Logger.Info($"GM 多人能力层数修改成功：目标={SafeCreatureName(target)}，能力={canonicalPower.Id}，层数变化={delta:+#;-#;0}。");
                    return true;
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Info($"GM 多人能力层数命令修改失败，准备兜底：目标={SafeCreatureName(target)}，能力={canonicalPower.Id}，层数变化={delta:+#;-#;0}，异常={ex.Message}。");
                    return TrySetPowerAmountDirect(existing, existing.Amount + delta);
                }
            }

            if (delta < 0 && !canonicalPower.AllowNegative)
            {
                MainFile.Logger.Info($"GM 多人能力赋予跳过：目标={SafeCreatureName(target)}，能力={canonicalPower.Id} 不允许负数层数，层数变化={delta:+#;-#;0}。");
                return false;
            }

            try
            {
                await PowerCmd.Apply(new ThrowingPlayerChoiceContext(), canonicalPower.ToMutable(0), target, delta, null, null);
                bool applied = target.GetPowerById(canonicalPower.Id) != null;
                MainFile.Logger.Info($"GM 多人能力赋予命令执行{(applied ? "成功" : "未生效")}：目标={SafeCreatureName(target)}，能力={canonicalPower.Id}，层数变化={delta:+#;-#;0}。");
                if (applied)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"GM 多人能力赋予命令失败，准备兜底：目标={SafeCreatureName(target)}，能力={canonicalPower.Id}，层数变化={delta:+#;-#;0}，异常={ex.Message}。");
            }

            return TryApplyNewPowerDirect(target, canonicalPower, delta);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 多人能力变更异常：目标={SafeCreatureName(target)}，能力={canonicalPower.Id}，层数变化={delta:+#;-#;0}，{FormatException(ex)}");
            return false;
        }
    }

    /// <summary>
    /// 直接设置已有能力层数，作为同步动作执行端兜底。
    /// </summary>
    /// <param name="power">已有能力实例。</param>
    /// <param name="targetAmount">目标层数。</param>
    private static bool TrySetPowerAmountDirect(PowerModel power, int targetAmount)
    {
        try
        {
            int before = power.Amount;
            power.SetAmount(targetAmount, false);
            bool changed = power.Amount != before;
            MainFile.Logger.Info($"GM 多人能力直接设值兜底{(changed ? "成功" : "未变化")}：能力={power.Id}，目标层数={targetAmount}。");
            return changed;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 多人能力直接设值兜底失败：能力={power.Id}，目标层数={targetAmount}，{FormatException(ex)}");
            return false;
        }
    }

    /// <summary>
    /// 直接挂载新能力，作为同步动作执行端兜底。
    /// </summary>
    /// <param name="target">要操作的目标。</param>
    /// <param name="canonicalPower">能力 canonical model。</param>
    /// <param name="delta">初始层数。</param>
    private static bool TryApplyNewPowerDirect(Creature target, PowerModel canonicalPower, int delta)
    {
        try
        {
            PowerModel? alreadyApplied = target.GetPowerById(canonicalPower.Id);
            if (alreadyApplied != null)
            {
                int targetAmount = alreadyApplied.Amount == 0 ? delta : alreadyApplied.Amount;
                return TrySetPowerAmountDirect(alreadyApplied, targetAmount);
            }

            PowerModel freshPower = canonicalPower.ToMutable(0);
            freshPower.ApplyInternal(target, delta, false);
            PowerModel? after = target.GetPowerById(canonicalPower.Id);
            bool applied = after != null && ValidatePowerDeltaApplied(after, delta);
            MainFile.Logger.Info($"GM 多人能力直接挂载兜底{(applied ? "成功" : "未生效")}：目标={SafeCreatureName(target)}，能力={canonicalPower.Id}，层数变化={delta:+#;-#;0}。");
            return applied;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 多人能力直接挂载兜底失败：目标={SafeCreatureName(target)}，能力={canonicalPower.Id}，层数变化={delta:+#;-#;0}，{FormatException(ex)}");
            return false;
        }
    }

    /// <summary>
    /// 检查能力层数变化是否生效。
    /// </summary>
    /// <param name="power">执行后的能力实例。</param>
    /// <param name="delta">本次层数变化。</param>
    private static bool ValidatePowerDeltaApplied(PowerModel power, int delta)
    {
        if (delta > 0)
        {
            return power.Amount > 0;
        }

        if (delta < 0)
        {
            return power.Amount < 0 || power.Amount == 0;
        }

        return true;
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

    /// <summary>
    /// 统一格式化异常，方便排查联机动作执行端问题。
    /// </summary>
    /// <param name="ex">捕获到的异常。</param>
    private static string FormatException(Exception ex)
    {
        string inner = ex.InnerException == null
            ? string.Empty
            : $"，内部异常={ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
        string stack = ex.StackTrace ?? "<no stack>";
        return $"异常={ex.GetType().Name}: {ex.Message}{inner}，堆栈={stack}";
    }
}

/// <summary>
/// GM 能力赋予网络动作的单条请求：能力 ID 和层数变化。
/// </summary>
/// <param name="PowerId">能力 ModelId 字符串。</param>
/// <param name="AmountDelta">要增减的层数。</param>
public readonly record struct GmPowerApplyEntry(string PowerId, int AmountDelta);

/// <summary>
/// GM 能力赋予网络数据：同步目标引用、能力 ID 和层数变化。
/// </summary>
public struct NetGmPowerApplyAction : INetAction
{
    /// <summary>
    /// 要操作的目标引用列表。
    /// </summary>
    public List<GmCreatureTargetRef> Targets { get; set; }

    /// <summary>
    /// 要应用的能力请求列表。
    /// </summary>
    public List<GmPowerApplyEntry> Entries { get; set; }

    /// <summary>
    /// 根据网络数据还原成本地可执行动作。
    /// </summary>
    /// <param name="player">动作所属玩家。</param>
    public GameAction ToGameAction(Player player)
    {
        return new GmPowerApplyAction(player, Targets ?? [], Entries ?? []);
    }

    /// <summary>
    /// 序列化能力赋予请求。
    /// </summary>
    /// <param name="writer">游戏网络包写入器。</param>
    public void Serialize(PacketWriter writer)
    {
        IReadOnlyList<GmCreatureTargetRef> targets = Targets ?? [];
        IReadOnlyList<GmPowerApplyEntry> entries = Entries ?? [];
        writer.WriteInt(targets.Count, 16);
        foreach (GmCreatureTargetRef target in targets)
        {
            writer.WriteBool(target.IsPlayerTarget);
            writer.WriteUInt(target.CombatId, 32);
        }

        writer.WriteInt(entries.Count, 16);
        foreach (GmPowerApplyEntry entry in entries)
        {
            int safeDelta = Math.Clamp(entry.AmountDelta, short.MinValue, short.MaxValue);
            writer.WriteString(entry.PowerId ?? string.Empty);
            writer.WriteBool(safeDelta < 0);
            writer.WriteInt(Math.Abs(safeDelta), 16);
        }
    }

    /// <summary>
    /// 反序列化能力赋予请求。
    /// </summary>
    /// <param name="reader">游戏网络包读取器。</param>
    public void Deserialize(PacketReader reader)
    {
        int targetCount = reader.ReadInt(16);
        Targets = new List<GmCreatureTargetRef>(targetCount);
        for (int i = 0; i < targetCount; i++)
        {
            bool isPlayerTarget = reader.ReadBool();
            uint combatId = reader.ReadUInt(32);
            Targets.Add(new GmCreatureTargetRef(isPlayerTarget, combatId));
        }

        int entryCount = reader.ReadInt(16);
        Entries = new List<GmPowerApplyEntry>(entryCount);
        for (int i = 0; i < entryCount; i++)
        {
            string powerId = reader.ReadString();
            bool negative = reader.ReadBool();
            int amount = reader.ReadInt(16);
            Entries.Add(new GmPowerApplyEntry(powerId, negative ? -amount : amount));
        }
    }

    /// <summary>
    /// 返回便于日志定位的网络动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"NetGmPowerApplyAction targets [{string.Join(",", Targets ?? [])}] entries [{string.Join(",", Entries ?? [])}]";
    }
}
