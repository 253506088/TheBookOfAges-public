using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace TheBookOfAges.TheBookOfAgesCode.GameActions;

/// <summary>
/// GM 药水发放网络动作：联机时同步药水 ID，执行端重新查空槽并调用原生 PotionCmd 发放。
/// </summary>
public sealed class GmPotionGrantAction : GameAction
{
    private readonly Player _player;
    private readonly IReadOnlyList<string> _potionIds;
    private readonly bool _inCombat;

    /// <summary>
    /// 当前网络动作所属玩家。
    /// </summary>
    public override ulong OwnerId => _player.NetId;

    /// <summary>
    /// 药水发放动作类型跟随提交时的战斗状态，避免提交到错误队列。
    /// </summary>
    public override GameActionType ActionType => _inCombat ? GameActionType.CombatPlayPhaseOnly : GameActionType.Any;

    /// <summary>
    /// 创建 GM 药水发放网络动作。
    /// </summary>
    /// <param name="player">要获得药水的玩家。</param>
    /// <param name="potionIds">要发放的药水 ModelId 字符串列表。</param>
    /// <param name="inCombat">提交动作时是否处于战斗中。</param>
    public GmPotionGrantAction(Player player, IReadOnlyList<string> potionIds, bool inCombat)
    {
        _player = player;
        _potionIds = potionIds.ToList();
        _inCombat = inCombat;
    }

    /// <summary>
    /// 在本机执行网络动作；每瓶药水都会重新查找当前空槽。
    /// </summary>
    protected override async Task ExecuteAction()
    {
        int grantedCount = await GmPotionGrantExecutor.GrantPotions(_player, _potionIds);
        MainFile.Logger.Info($"GM 多人药水发放网络动作执行完成：玩家NetId={_player.NetId}，战斗中={_inCombat}，请求数量={_potionIds.Count}，实际发放={grantedCount}。");
    }

    /// <summary>
    /// 把本地动作转换成可同步的网络动作数据。
    /// </summary>
    public override INetAction ToNetAction()
    {
        return new NetGmPotionGrantAction
        {
            PotionIds = _potionIds.ToList(),
            InCombat = _inCombat
        };
    }

    /// <summary>
    /// 返回便于日志定位的动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"GmPotionGrantAction player {_player.NetId} inCombat {_inCombat} potions [{string.Join(",", _potionIds)}]";
    }
}

/// <summary>
/// GM 药水发放网络数据：同步药水 ID 列表和提交时的战斗状态。
/// </summary>
public struct NetGmPotionGrantAction : INetAction
{
    /// <summary>
    /// 要发放的药水 ModelId 字符串列表。
    /// </summary>
    public List<string> PotionIds { get; set; }

    /// <summary>
    /// 提交动作时是否处于战斗中。
    /// </summary>
    public bool InCombat { get; set; }

    /// <summary>
    /// 根据网络数据还原成本地可执行动作。
    /// </summary>
    /// <param name="player">动作所属玩家。</param>
    public GameAction ToGameAction(Player player)
    {
        return new GmPotionGrantAction(player, PotionIds ?? [], InCombat);
    }

    /// <summary>
    /// 序列化药水发放请求。
    /// </summary>
    /// <param name="writer">游戏网络包写入器。</param>
    public void Serialize(PacketWriter writer)
    {
        IReadOnlyList<string> potionIds = PotionIds ?? [];
        writer.WriteBool(InCombat);
        writer.WriteInt(potionIds.Count, 16);
        foreach (string potionId in potionIds)
        {
            writer.WriteString(potionId ?? string.Empty);
        }
    }

    /// <summary>
    /// 反序列化药水发放请求。
    /// </summary>
    /// <param name="reader">游戏网络包读取器。</param>
    public void Deserialize(PacketReader reader)
    {
        InCombat = reader.ReadBool();
        int count = reader.ReadInt(16);
        PotionIds = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            PotionIds.Add(reader.ReadString());
        }
    }

    /// <summary>
    /// 返回便于日志定位的网络动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"NetGmPotionGrantAction inCombat {InCombat} potions [{string.Join(",", PotionIds ?? [])}]";
    }
}

/// <summary>
/// GM 药水发放执行器：集中处理药水查找、空槽定位和原生命令调用。
/// </summary>
internal static class GmPotionGrantExecutor
{
    /// <summary>
    /// 批量发放药水到玩家药水栏。
    /// </summary>
    /// <param name="player">要操作的玩家。</param>
    /// <param name="potionIds">药水 ModelId 字符串列表。</param>
    public static async Task<int> GrantPotions(Player player, IReadOnlyList<string> potionIds)
    {
        if (player == null)
        {
            MainFile.Logger.Info("GM 多人药水发放执行跳过：玩家为空。");
            return 0;
        }

        int grantedCount = 0;
        foreach (string potionId in potionIds)
        {
            PotionModel? canonicalPotion = FindPotion(potionId);
            if (canonicalPotion == null)
            {
                MainFile.Logger.Info($"GM 多人药水发放跳过未知药水：potionId={potionId}。");
                continue;
            }

            int slotIndex = GetFirstOpenPotionSlotIndex(player);
            if (slotIndex < 0)
            {
                MainFile.Logger.Info($"GM 多人药水发放跳过：药水栏已满，玩家NetId={player.NetId}，potion={canonicalPotion.Id}。");
                continue;
            }

            try
            {
                PotionProcureResult result = await PotionCmd.TryToProcure(canonicalPotion.ToMutable(), player, slotIndex);
                if (result.success)
                {
                    grantedCount++;
                    MainFile.Logger.Info($"GM 多人药水发放成功：玩家NetId={player.NetId}，potion={canonicalPotion.Id}，slotIndex={slotIndex}。");
                }
                else
                {
                    MainFile.Logger.Info($"GM 多人药水发放失败：玩家NetId={player.NetId}，potion={canonicalPotion.Id}，slotIndex={slotIndex}，结果=PotionCmd 返回失败。");
                }
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"GM 多人药水发放异常：玩家NetId={player.NetId}，potion={canonicalPotion.Id}，slotIndex={slotIndex}，异常={ex.Message}。");
            }
        }

        return grantedCount;
    }

    /// <summary>
    /// 从游戏药水库按字符串 ID 查找 canonical 药水模型。
    /// </summary>
    /// <param name="potionId">药水 ModelId 字符串。</param>
    private static PotionModel? FindPotion(string potionId)
    {
        return ModelDb.AllPotions.FirstOrDefault(potion =>
            potion != null &&
            string.Equals(potion.Id.ToString(), potionId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 获取第一个空药水槽；没有空槽时返回 -1。
    /// </summary>
    /// <param name="player">要检查的玩家。</param>
    private static int GetFirstOpenPotionSlotIndex(Player player)
    {
        for (int i = 0; i < player.PotionSlots.Count; i++)
        {
            if (player.PotionSlots[i] == null)
            {
                return i;
            }
        }

        return player.PotionSlots.Count < player.MaxPotionCount ? player.PotionSlots.Count : -1;
    }
}
