using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace TheBookOfAges.TheBookOfAgesCode.GameActions;

/// <summary>
/// GM 充能球发放网络动作：联机战斗中通过原生 OrbCmd.Channel 同步发球。
/// </summary>
public sealed class GmOrbGrantAction : GameAction
{
    private readonly Player _player;
    private readonly IReadOnlyList<string> _orbIds;

    /// <summary>
    /// 当前网络动作所属玩家。
    /// </summary>
    public override ulong OwnerId => _player.NetId;

    /// <summary>
    /// 充能球只能在战斗出牌阶段同步执行。
    /// </summary>
    public override GameActionType ActionType => GameActionType.CombatPlayPhaseOnly;

    /// <summary>
    /// 创建 GM 充能球发放网络动作。
    /// </summary>
    /// <param name="player">要发放充能球的玩家。</param>
    /// <param name="orbIds">要发放的充能球 ModelId 字符串列表。</param>
    public GmOrbGrantAction(Player player, IReadOnlyList<string> orbIds)
    {
        _player = player;
        _orbIds = orbIds.ToList();
    }

    /// <summary>
    /// 在本机执行网络动作；每个球重新按 ID 取 canonical model 并转 mutable。
    /// </summary>
    protected override async Task ExecuteAction()
    {
        if (_player?.PlayerCombatState?.OrbQueue == null || _player.Creature?.CombatState == null)
        {
            MainFile.Logger.Info("GM 多人充能球发放执行跳过：玩家不在有效战斗或 OrbQueue 为空。");
            return;
        }

        int grantedCount = 0;
        foreach (string orbId in _orbIds)
        {
            OrbModel? canonicalOrb = FindOrb(orbId);
            if (canonicalOrb == null)
            {
                MainFile.Logger.Info($"GM 多人充能球发放跳过未知充能球：orbId={orbId}。");
                continue;
            }

            try
            {
                await OrbCmd.Channel(new ThrowingPlayerChoiceContext(), canonicalOrb.ToMutable(), _player);
                grantedCount++;
                MainFile.Logger.Info($"GM 多人充能球发放成功：玩家NetId={_player.NetId}，orb={canonicalOrb.Id}。");
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"GM 多人充能球发放异常：玩家NetId={_player.NetId}，orb={canonicalOrb.Id}，异常={ex.Message}。");
            }
        }

        MainFile.Logger.Info($"GM 多人充能球发放网络动作执行完成：玩家NetId={_player.NetId}，请求数量={_orbIds.Count}，实际发放={grantedCount}。");
    }

    /// <summary>
    /// 把本地动作转换成可同步的网络动作数据。
    /// </summary>
    public override INetAction ToNetAction()
    {
        return new NetGmOrbGrantAction
        {
            OrbIds = _orbIds.ToList()
        };
    }

    /// <summary>
    /// 返回便于日志定位的动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"GmOrbGrantAction player {_player.NetId} orbs [{string.Join(",", _orbIds)}]";
    }

    /// <summary>
    /// 按字符串 ID 查找充能球模型，包含本体和 Mod 注入的 OrbModel。
    /// </summary>
    /// <param name="orbId">充能球 ModelId 字符串。</param>
    private static OrbModel? FindOrb(string orbId)
    {
        return ModelDb.AllAbstractModelSubtypes
            .Where(type => type.IsSubclassOf(typeof(OrbModel)))
            .Select(ModelDb.DebugOrb)
            .Where(orb => orb != null)
            .FirstOrDefault(orb => string.Equals(orb!.Id.ToString(), orbId, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// GM 充能球发放网络数据：同步充能球 ModelId 字符串列表。
/// </summary>
public struct NetGmOrbGrantAction : INetAction
{
    /// <summary>
    /// 要发放的充能球 ModelId 字符串列表。
    /// </summary>
    public List<string> OrbIds { get; set; }

    /// <summary>
    /// 根据网络数据还原成本地可执行动作。
    /// </summary>
    /// <param name="player">动作所属玩家。</param>
    public GameAction ToGameAction(Player player)
    {
        return new GmOrbGrantAction(player, OrbIds ?? []);
    }

    /// <summary>
    /// 序列化充能球 ID 列表。
    /// </summary>
    /// <param name="writer">游戏网络包写入器。</param>
    public void Serialize(PacketWriter writer)
    {
        IReadOnlyList<string> orbIds = OrbIds ?? [];
        writer.WriteInt(orbIds.Count, 16);
        foreach (string orbId in orbIds)
        {
            writer.WriteString(orbId ?? string.Empty);
        }
    }

    /// <summary>
    /// 反序列化充能球 ID 列表。
    /// </summary>
    /// <param name="reader">游戏网络包读取器。</param>
    public void Deserialize(PacketReader reader)
    {
        int count = reader.ReadInt(16);
        OrbIds = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            OrbIds.Add(reader.ReadString());
        }
    }

    /// <summary>
    /// 返回便于日志定位的网络动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"NetGmOrbGrantAction orbs [{string.Join(",", OrbIds ?? [])}]";
    }
}
