using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace TheBookOfAges.TheBookOfAgesCode.GameActions;

/// <summary>
/// GM 牌组卡片附魔网络动作：双方按同一批牌组下标和同一个附魔 ID 执行附魔。
/// </summary>
public sealed class GmDeckCardEnchantAction : GameAction
{
    private readonly Player _player;
    private readonly IReadOnlyList<uint> _deckIndexes;
    private readonly string _enchantmentId;
    private readonly int _amount;

    /// <summary>
    /// 当前网络动作所属玩家。
    /// </summary>
    public override ulong OwnerId => _player.NetId;

    /// <summary>
    /// 牌组附魔只允许在非战斗稳定状态执行。
    /// </summary>
    public override GameActionType ActionType => GameActionType.NonCombat;

    /// <summary>
    /// 创建 GM 牌组卡片附魔网络动作。
    /// </summary>
    /// <param name="player">要附魔牌组卡片的玩家。</param>
    /// <param name="deckIndexes">要附魔的牌组下标列表。</param>
    /// <param name="enchantmentId">附魔 ModelId 字符串。</param>
    /// <param name="amount">附魔数值。</param>
    public GmDeckCardEnchantAction(Player player, IReadOnlyList<uint> deckIndexes, string enchantmentId, int amount)
    {
        _player = player;
        _deckIndexes = deckIndexes.ToList();
        _enchantmentId = enchantmentId;
        _amount = Math.Clamp(amount, 1, 99);
    }

    /// <summary>
    /// 在本机执行网络动作；异常只记录日志并跳过，避免一张异常卡打断网络动作队列。
    /// </summary>
    protected override Task ExecuteAction()
    {
        int enchantedCount = GmDeckCardEnchantExecutor.EnchantByDeckIndexes(_player, _deckIndexes, _enchantmentId, _amount);
        MainFile.Logger.Info($"GM 多人卡片附魔网络动作执行完成：玩家NetId={_player.NetId}，附魔={_enchantmentId}，数值={_amount}，请求数量={_deckIndexes.Count}，实际附魔={enchantedCount}。");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 把本地动作转换成可同步的网络动作数据。
    /// </summary>
    public override INetAction ToNetAction()
    {
        return new NetGmDeckCardEnchantAction
        {
            DeckIndexes = _deckIndexes.ToList(),
            EnchantmentId = _enchantmentId,
            Amount = _amount
        };
    }

    /// <summary>
    /// 返回便于日志定位的动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"GmDeckCardEnchantAction player {_player.NetId} enchantment {_enchantmentId} amount {_amount} deckIndexes [{string.Join(",", _deckIndexes)}]";
    }
}

/// <summary>
/// GM 牌组卡片附魔网络数据：同步牌组下标、附魔 ID 和数值。
/// </summary>
public struct NetGmDeckCardEnchantAction : INetAction
{
    /// <summary>
    /// 要附魔的牌组下标列表。
    /// </summary>
    public List<uint> DeckIndexes { get; set; }

    /// <summary>
    /// 附魔 ModelId 字符串。
    /// </summary>
    public string EnchantmentId { get; set; }

    /// <summary>
    /// 附魔数值。
    /// </summary>
    public int Amount { get; set; }

    /// <summary>
    /// 根据网络数据还原成本地可执行动作。
    /// </summary>
    /// <param name="player">动作所属玩家。</param>
    public GameAction ToGameAction(Player player)
    {
        return new GmDeckCardEnchantAction(player, DeckIndexes ?? [], EnchantmentId ?? string.Empty, Amount);
    }

    /// <summary>
    /// 序列化附魔请求。
    /// </summary>
    /// <param name="writer">游戏网络包写入器。</param>
    public void Serialize(PacketWriter writer)
    {
        IReadOnlyList<uint> indexes = DeckIndexes ?? [];
        writer.WriteString(EnchantmentId ?? string.Empty);
        writer.WriteInt(Math.Clamp(Amount, 1, 99), 16);
        writer.WriteInt(indexes.Count, 16);
        foreach (uint index in indexes)
        {
            writer.WriteUInt(index, 16);
        }
    }

    /// <summary>
    /// 反序列化附魔请求。
    /// </summary>
    /// <param name="reader">游戏网络包读取器。</param>
    public void Deserialize(PacketReader reader)
    {
        EnchantmentId = reader.ReadString();
        Amount = reader.ReadInt(16);
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
        return $"NetGmDeckCardEnchantAction enchantment {EnchantmentId} amount {Amount} deckIndexes [{string.Join(",", DeckIndexes ?? [])}]";
    }
}

/// <summary>
/// GM 牌组卡片附魔执行器：按牌组下标重新取卡，集中处理附魔前安全检查。
/// </summary>
internal static class GmDeckCardEnchantExecutor
{
    /// <summary>
    /// 按牌组下标批量附魔卡片。
    /// </summary>
    /// <param name="player">要操作的玩家。</param>
    /// <param name="deckIndexes">要附魔的牌组下标列表。</param>
    /// <param name="enchantmentId">附魔 ModelId 字符串。</param>
    /// <param name="amount">附魔数值。</param>
    public static int EnchantByDeckIndexes(Player player, IReadOnlyList<uint> deckIndexes, string enchantmentId, int amount)
    {
        if (player?.Deck == null)
        {
            MainFile.Logger.Info("GM 多人卡片附魔执行跳过：玩家或牌组为空。");
            return 0;
        }

        EnchantmentModel? enchantment = FindEnchantment(enchantmentId);
        if (enchantment == null)
        {
            MainFile.Logger.Info($"GM 多人卡片附魔执行跳过：找不到附魔，enchantment={enchantmentId}。");
            return 0;
        }

        CardPile deck = PileType.Deck.GetPile(player);
        HashSet<uint> handledIndexes = [];
        int enchantedCount = 0;
        int safeAmount = Math.Clamp(amount, 1, 99);

        foreach (uint deckIndex in deckIndexes)
        {
            if (!handledIndexes.Add(deckIndex))
            {
                MainFile.Logger.Info($"GM 多人卡片附魔跳过重复下标：玩家NetId={player.NetId}，deckIndex={deckIndex}。");
                continue;
            }

            if (deckIndex >= deck.Cards.Count)
            {
                MainFile.Logger.Info($"GM 多人卡片附魔跳过越界下标：玩家NetId={player.NetId}，deckIndex={deckIndex}，牌组数量={deck.Cards.Count}。");
                continue;
            }

            CardModel card = deck.Cards[(int)deckIndex];
            if (card.Pile == null || card.Pile.Type != PileType.Deck || card.Pile != deck)
            {
                MainFile.Logger.Info($"GM 多人卡片附魔跳过非当前牌组卡：玩家NetId={player.NetId}，deckIndex={deckIndex}，card={card.Id}，pile={card.Pile?.Type.ToString() ?? "空"}。");
                continue;
            }

            if (!CanEnchant(enchantment, card))
            {
                MainFile.Logger.Info($"GM 多人卡片附魔跳过不满足条件卡片：玩家NetId={player.NetId}，deckIndex={deckIndex}，card={card.Id}，enchantment={enchantmentId}。");
                continue;
            }

            try
            {
                CardCmd.Enchant(enchantment.ToMutable(), card, safeAmount);
                enchantedCount++;
                MainFile.Logger.Info($"GM 多人卡片附魔成功：玩家NetId={player.NetId}，deckIndex={deckIndex}，card={card.Id}，enchantment={enchantmentId}，数值={safeAmount}。");
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"GM 多人卡片附魔异常：玩家NetId={player.NetId}，deckIndex={deckIndex}，card={card.Id}，enchantment={enchantmentId}，数值={safeAmount}，异常={ex.Message}。");
            }
        }

        return enchantedCount;
    }

    /// <summary>
    /// 按字符串 ID 查找附魔模型。
    /// </summary>
    /// <param name="enchantmentId">附魔 ModelId 字符串。</param>
    private static EnchantmentModel? FindEnchantment(string enchantmentId)
    {
        return ModelDb.DebugEnchantments.FirstOrDefault(enchantment =>
            enchantment != null &&
            string.Equals(enchantment.Id.ToString(), enchantmentId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 判断指定附魔是否能作用在这张卡上。
    /// </summary>
    /// <param name="enchantment">附魔模型。</param>
    /// <param name="card">牌组卡片实例。</param>
    private static bool CanEnchant(EnchantmentModel enchantment, CardModel card)
    {
        try
        {
            return card.Enchantment == null && enchantment.CanEnchant(card);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 多人卡片附魔条件检查异常：card={card.Id}，enchantment={enchantment.Id}，异常={ex.Message}。");
            return false;
        }
    }
}
