using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace TheBookOfAges.TheBookOfAgesCode.GameActions;

/// <summary>
/// GM 牌组卡片强化网络动作：双方按同一批牌组下标取卡并执行强化，避免多人模式只改本机牌组。
/// </summary>
public sealed class GmDeckCardUpgradeAction : GameAction
{
    private readonly Player _player;
    private readonly IReadOnlyList<uint> _deckIndexes;

    /// <summary>
    /// 当前网络动作所属玩家。
    /// </summary>
    public override ulong OwnerId => _player.NetId;

    /// <summary>
    /// 牌组强化只允许在非战斗稳定状态执行。
    /// </summary>
    public override GameActionType ActionType => GameActionType.NonCombat;

    /// <summary>
    /// 创建 GM 牌组卡片强化网络动作。
    /// </summary>
    /// <param name="player">要强化牌组的玩家。</param>
    /// <param name="deckIndexes">要强化的牌组下标列表。</param>
    public GmDeckCardUpgradeAction(Player player, IReadOnlyList<uint> deckIndexes)
    {
        _player = player;
        _deckIndexes = deckIndexes.ToList();
    }

    /// <summary>
    /// 在本机执行网络动作；异常只记录日志并跳过，避免一张异常卡打断网络动作队列。
    /// </summary>
    protected override Task ExecuteAction()
    {
        int upgradedCount = GmDeckCardUpgradeExecutor.UpgradeByDeckIndexes(_player, _deckIndexes);
        MainFile.Logger.Info($"GM 多人卡片强化网络动作执行完成：玩家NetId={_player.NetId}，请求数量={_deckIndexes.Count}，实际强化={upgradedCount}。");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 把本地动作转换成可同步的网络动作数据。
    /// </summary>
    public override INetAction ToNetAction()
    {
        return new NetGmDeckCardUpgradeAction
        {
            DeckIndexes = _deckIndexes.ToList()
        };
    }

    /// <summary>
    /// 返回便于日志定位的动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"GmDeckCardUpgradeAction player {_player.NetId} deckIndexes [{string.Join(",", _deckIndexes)}]";
    }
}

/// <summary>
/// GM 牌组卡片强化网络数据：只传牌组下标，不传卡 ID，避免同名重复卡定位错误。
/// </summary>
public struct NetGmDeckCardUpgradeAction : INetAction
{
    /// <summary>
    /// 要强化的牌组下标列表。
    /// </summary>
    public List<uint> DeckIndexes { get; set; }

    /// <summary>
    /// 根据网络数据还原成本地可执行动作。
    /// </summary>
    /// <param name="player">动作所属玩家。</param>
    public GameAction ToGameAction(Player player)
    {
        return new GmDeckCardUpgradeAction(player, DeckIndexes ?? []);
    }

    /// <summary>
    /// 序列化牌组下标列表。
    /// </summary>
    /// <param name="writer">游戏网络包写入器。</param>
    public void Serialize(PacketWriter writer)
    {
        IReadOnlyList<uint> indexes = DeckIndexes ?? [];
        writer.WriteInt(indexes.Count, 16);
        foreach (uint index in indexes)
        {
            writer.WriteUInt(index, 16);
        }
    }

    /// <summary>
    /// 反序列化牌组下标列表。
    /// </summary>
    /// <param name="reader">游戏网络包读取器。</param>
    public void Deserialize(PacketReader reader)
    {
        int count = reader.ReadInt(16);
        DeckIndexes = new List<uint>(count);
        for (int i = 0; i < count; i++)
        {
            DeckIndexes.Add(reader.ReadUInt(16));
        }
    }

    /// <summary>
    /// 返回便于日志定位的网络动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"NetGmDeckCardUpgradeAction deckIndexes [{string.Join(",", DeckIndexes ?? [])}]";
    }
}

/// <summary>
/// GM 牌组卡片强化执行器：按牌组下标重新取卡，集中处理升级前安全检查。
/// </summary>
internal static class GmDeckCardUpgradeExecutor
{
    /// <summary>
    /// 按牌组下标批量强化卡片。
    /// </summary>
    /// <param name="player">要操作的玩家。</param>
    /// <param name="deckIndexes">要强化的牌组下标列表。</param>
    public static int UpgradeByDeckIndexes(Player player, IReadOnlyList<uint> deckIndexes)
    {
        if (player?.Deck == null)
        {
            MainFile.Logger.Info("GM 多人卡片强化执行跳过：玩家或牌组为空。");
            return 0;
        }

        CardPile deck = PileType.Deck.GetPile(player);
        HashSet<uint> handledIndexes = [];
        int upgradedCount = 0;

        foreach (uint deckIndex in deckIndexes)
        {
            if (!handledIndexes.Add(deckIndex))
            {
                MainFile.Logger.Info($"GM 多人卡片强化跳过重复下标：玩家NetId={player.NetId}，deckIndex={deckIndex}。");
                continue;
            }

            if (deckIndex >= deck.Cards.Count)
            {
                MainFile.Logger.Info($"GM 多人卡片强化跳过越界下标：玩家NetId={player.NetId}，deckIndex={deckIndex}，牌组数量={deck.Cards.Count}。");
                continue;
            }

            CardModel card = deck.Cards[(int)deckIndex];
            if (!card.IsUpgradable || card.CurrentUpgradeLevel >= card.MaxUpgradeLevel)
            {
                MainFile.Logger.Info($"GM 多人卡片强化跳过不可升级卡片：玩家NetId={player.NetId}，deckIndex={deckIndex}，card={card.Id}，等级={card.CurrentUpgradeLevel}/{card.MaxUpgradeLevel}。");
                continue;
            }

            try
            {
                CardCmd.Upgrade(card, CardPreviewStyle.None);
                upgradedCount++;
                MainFile.Logger.Info($"GM 多人卡片强化成功：玩家NetId={player.NetId}，deckIndex={deckIndex}，card={card.Id}，等级={card.CurrentUpgradeLevel}/{card.MaxUpgradeLevel}。");
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"GM 多人卡片强化异常：玩家NetId={player.NetId}，deckIndex={deckIndex}，card={card.Id}，异常={ex.Message}。");
            }
        }

        return upgradedCount;
    }
}
