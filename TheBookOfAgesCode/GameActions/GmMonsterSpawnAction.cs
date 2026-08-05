using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Rooms;
using TheBookOfAges.TheBookOfAgesCode.Services;

namespace TheBookOfAges.TheBookOfAgesCode.GameActions;

/// <summary>
/// GM 怪物召唤网络动作：在联机战斗队列里按怪物 ID 追加敌人。
/// </summary>
public sealed class GmMonsterSpawnAction : GameAction
{
    private readonly Player _player;
    private readonly IReadOnlyList<string> _monsterIds;

    /// <summary>
    /// 当前网络动作所属玩家。
    /// </summary>
    public override ulong OwnerId => _player.NetId;

    /// <summary>
    /// 怪物召唤必须在战斗出牌阶段的同步队列中执行。
    /// </summary>
    public override GameActionType ActionType => GameActionType.CombatPlayPhaseOnly;

    /// <summary>
    /// 创建 GM 怪物召唤网络动作。
    /// </summary>
    /// <param name="player">提交动作的玩家。</param>
    /// <param name="monsterIds">要召唤的怪物 ModelId 字符串列表。</param>
    public GmMonsterSpawnAction(Player player, IReadOnlyList<string> monsterIds)
    {
        _player = player;
        _monsterIds = monsterIds.ToList();
    }

    /// <summary>
    /// 在本机执行网络动作；执行端重新读取当前战斗和怪物模型，避免直接同步运行时对象。
    /// </summary>
    protected override async Task ExecuteAction()
    {
        if (!MonsterService.TryGetCurrentCombatContext(out _, out CombatRoom? currentRoom, out CombatState? combatState))
        {
            MainFile.Logger.Info($"GM 多人怪物召唤执行跳过：玩家NetId={_player.NetId}，原因=当前不在有效战斗。");
            return;
        }

        int spawnedCount = 0;
        foreach (string monsterId in _monsterIds)
        {
            if (await MonsterService.TrySpawnMonsterByIdAsync(currentRoom!, combatState!, monsterId))
            {
                spawnedCount++;
            }
        }

        MainFile.Logger.Info($"GM 多人怪物召唤网络动作执行完成：玩家NetId={_player.NetId}，请求数量={_monsterIds.Count}，实际召唤={spawnedCount}。");
    }

    /// <summary>
    /// 把本地动作转换成可同步的网络动作数据。
    /// </summary>
    public override INetAction ToNetAction()
    {
        return new NetGmMonsterSpawnAction
        {
            MonsterIds = _monsterIds.ToList()
        };
    }

    /// <summary>
    /// 返回便于日志定位的动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"GmMonsterSpawnAction player {_player.NetId} monsters [{string.Join(",", _monsterIds)}]";
    }
}

/// <summary>
/// GM 怪物召唤网络数据：只同步怪物 ModelId 字符串。
/// </summary>
public struct NetGmMonsterSpawnAction : INetAction
{
    /// <summary>
    /// 要召唤的怪物 ModelId 字符串列表。
    /// </summary>
    public List<string> MonsterIds { get; set; }

    /// <summary>
    /// 根据网络数据还原成本地可执行动作。
    /// </summary>
    /// <param name="player">动作所属玩家。</param>
    public GameAction ToGameAction(Player player)
    {
        return new GmMonsterSpawnAction(player, MonsterIds ?? []);
    }

    /// <summary>
    /// 序列化怪物 ID 列表。
    /// </summary>
    /// <param name="writer">游戏网络包写入器。</param>
    public void Serialize(PacketWriter writer)
    {
        IReadOnlyList<string> monsterIds = MonsterIds ?? [];
        writer.WriteInt(monsterIds.Count, 16);
        foreach (string monsterId in monsterIds)
        {
            writer.WriteString(monsterId ?? string.Empty);
        }
    }

    /// <summary>
    /// 反序列化怪物 ID 列表。
    /// </summary>
    /// <param name="reader">游戏网络包读取器。</param>
    public void Deserialize(PacketReader reader)
    {
        int count = reader.ReadInt(16);
        MonsterIds = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            MonsterIds.Add(reader.ReadString());
        }
    }

    /// <summary>
    /// 返回便于日志定位的网络动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"NetGmMonsterSpawnAction monsters [{string.Join(",", MonsterIds ?? [])}]";
    }
}
