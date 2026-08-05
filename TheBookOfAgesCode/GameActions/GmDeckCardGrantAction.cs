using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;

namespace TheBookOfAges.TheBookOfAgesCode.GameActions;

/// <summary>
/// GM 牌组卡片发放网络动作：用于原生命令无法表达的升级版发卡。
/// </summary>
public sealed class GmDeckCardGrantAction : GameAction
{
    private readonly Player _player;
    private readonly IReadOnlyList<GmDeckCardGrantEntry> _entries;
    private readonly bool _inCombat;

    /// <summary>
    /// 当前网络动作所属玩家。
    /// </summary>
    public override ulong OwnerId => _player.NetId;

    /// <summary>
    /// 发卡动作类型跟随提交时的战斗状态，保持和原生 ConsoleCmdGameAction 一致。
    /// </summary>
    public override GameActionType ActionType => _inCombat ? GameActionType.CombatPlayPhaseOnly : GameActionType.Any;

    /// <summary>
    /// 创建 GM 牌组卡片发放网络动作。
    /// </summary>
    /// <param name="player">要发放卡片的玩家。</param>
    /// <param name="entries">要发放的卡片 ID、数量和升级次数。</param>
    /// <param name="inCombat">提交动作时是否处于战斗中。</param>
    public GmDeckCardGrantAction(Player player, IReadOnlyList<GmDeckCardGrantEntry> entries, bool inCombat)
    {
        _player = player;
        _entries = entries.ToList();
        _inCombat = inCombat;
    }

    /// <summary>
    /// 在本机执行网络动作；每张卡重新从 ModelDb 找 canonical model，再通过 run scope 创建玩家卡。
    /// </summary>
    protected override async Task ExecuteAction()
    {
        int grantedCount = await GmDeckCardGrantExecutor.GrantCards(_player, _entries);
        MainFile.Logger.Info($"GM 多人卡片发放网络动作执行完成：玩家NetId={_player.NetId}，战斗中={_inCombat}，请求种类={_entries.Count}，实际发放={grantedCount}。");
    }

    /// <summary>
    /// 把本地动作转换成可同步的网络动作数据。
    /// </summary>
    public override INetAction ToNetAction()
    {
        return new NetGmDeckCardGrantAction
        {
            Entries = _entries.ToList(),
            InCombat = _inCombat
        };
    }

    /// <summary>
    /// 返回便于日志定位的动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"GmDeckCardGrantAction player {_player.NetId} inCombat {_inCombat} entries [{string.Join(",", _entries)}]";
    }
}

/// <summary>
/// GM 发卡网络动作的单条请求：卡牌 ID、数量和升级次数。
/// </summary>
/// <param name="CardId">卡牌 ModelId 字符串。</param>
/// <param name="Count">要发放的数量。</param>
/// <param name="UpgradeCount">发放后要执行的升级次数。</param>
public readonly record struct GmDeckCardGrantEntry(string CardId, int Count, int UpgradeCount);

/// <summary>
/// GM 牌组卡片发放网络数据：同步卡牌 ID、数量和升级次数。
/// </summary>
public struct NetGmDeckCardGrantAction : INetAction
{
    /// <summary>
    /// 发卡请求列表。
    /// </summary>
    public List<GmDeckCardGrantEntry> Entries { get; set; }

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
        return new GmDeckCardGrantAction(player, Entries ?? [], InCombat);
    }

    /// <summary>
    /// 序列化发卡请求列表。
    /// </summary>
    /// <param name="writer">游戏网络包写入器。</param>
    public void Serialize(PacketWriter writer)
    {
        IReadOnlyList<GmDeckCardGrantEntry> entries = Entries ?? [];
        writer.WriteBool(InCombat);
        writer.WriteInt(entries.Count, 16);
        foreach (GmDeckCardGrantEntry entry in entries)
        {
            writer.WriteString(entry.CardId ?? string.Empty);
            writer.WriteInt(Math.Clamp(entry.Count, 0, ushort.MaxValue), 16);
            writer.WriteInt(Math.Clamp(entry.UpgradeCount, 0, byte.MaxValue), 8);
        }
    }

    /// <summary>
    /// 反序列化发卡请求列表。
    /// </summary>
    /// <param name="reader">游戏网络包读取器。</param>
    public void Deserialize(PacketReader reader)
    {
        InCombat = reader.ReadBool();
        int count = reader.ReadInt(16);
        Entries = new List<GmDeckCardGrantEntry>(count);
        for (int i = 0; i < count; i++)
        {
            string cardId = reader.ReadString();
            int cardCount = reader.ReadInt(16);
            int upgradeCount = reader.ReadInt(8);
            Entries.Add(new GmDeckCardGrantEntry(cardId, cardCount, upgradeCount));
        }
    }

    /// <summary>
    /// 返回便于日志定位的网络动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"NetGmDeckCardGrantAction inCombat {InCombat} entries [{string.Join(",", Entries ?? [])}]";
    }
}

/// <summary>
/// GM 牌组卡片发放执行器：集中处理卡牌查找、创建、升级和加入牌组。
/// </summary>
internal static class GmDeckCardGrantExecutor
{
    /// <summary>
    /// 批量发放卡片到玩家牌组。
    /// </summary>
    /// <param name="player">要操作的玩家。</param>
    /// <param name="entries">发卡请求列表。</param>
    public static async Task<int> GrantCards(Player player, IReadOnlyList<GmDeckCardGrantEntry> entries)
    {
        if (player?.Deck == null)
        {
            MainFile.Logger.Info("GM 多人卡片发放执行跳过：玩家或牌组为空。");
            return 0;
        }

        ICardScope? runScope = RunManager.Instance?.DebugOnlyGetState();
        if (runScope == null)
        {
            MainFile.Logger.Info("GM 多人卡片发放执行跳过：RunScope 为空。");
            return 0;
        }

        int grantedCount = 0;
        foreach (GmDeckCardGrantEntry entry in entries)
        {
            CardModel? canonicalCard = FindCard(entry.CardId);
            if (canonicalCard == null)
            {
                MainFile.Logger.Info($"GM 多人卡片发放跳过未知卡牌：cardId={entry.CardId}。");
                continue;
            }

            int count = Math.Clamp(entry.Count, 0, ushort.MaxValue);
            for (int i = 0; i < count; i++)
            {
                if (await TryGrantOne(player, runScope, canonicalCard, entry.UpgradeCount))
                {
                    grantedCount++;
                }
            }
        }

        return grantedCount;
    }

    /// <summary>
    /// 从游戏卡牌库按字符串 ID 找 canonical 卡牌模型。
    /// </summary>
    /// <param name="cardId">卡牌 ModelId 字符串。</param>
    private static CardModel? FindCard(string cardId)
    {
        return ModelDb.AllCards.FirstOrDefault(card =>
            card != null &&
            string.Equals(card.Id.ToString(), cardId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 创建并发放单张卡片。
    /// </summary>
    /// <param name="player">要操作的玩家。</param>
    /// <param name="runScope">当前 run 卡牌作用域。</param>
    /// <param name="canonicalCard">卡牌库里的 canonical 卡牌模型。</param>
    /// <param name="upgradeCount">发放后升级次数。</param>
    private static async Task<bool> TryGrantOne(Player player, ICardScope runScope, CardModel canonicalCard, int upgradeCount)
    {
        try
        {
            CardModel card = runScope.CreateCard(canonicalCard, player);
            TryUpgrade(card, upgradeCount);
            CardPileAddResult addResult = await CardPileCmd.Add(
                card,
                PileType.Deck,
                CardPilePosition.Top,
                null,
                false);

            NotifyPileAddFinished(addResult, card, PileType.Deck, player);
            MainFile.Logger.Info($"GM 多人卡片发放{(addResult.success ? "成功" : "失败")}：玩家NetId={player.NetId}，card={canonicalCard.Id}，升级次数={upgradeCount}。");
            return addResult.success;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 多人卡片发放异常：玩家NetId={player.NetId}，card={canonicalCard.Id}，升级次数={upgradeCount}，异常={ex.Message}。");
            return false;
        }
    }

    /// <summary>
    /// 对刚创建出来的卡片执行指定次数的升级；不可升级或已满级时自然停止。
    /// </summary>
    /// <param name="card">要升级的卡片实例。</param>
    /// <param name="upgradeCount">最多执行的升级次数。</param>
    private static void TryUpgrade(CardModel card, int upgradeCount)
    {
        int safeUpgradeCount = Math.Clamp(upgradeCount, 0, byte.MaxValue);
        for (int i = 0; i < safeUpgradeCount; i++)
        {
            if (!card.IsUpgradable || card.CurrentUpgradeLevel >= card.MaxUpgradeLevel)
            {
                return;
            }

            try
            {
                CardCmd.Upgrade(card, CardPreviewStyle.None);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"GM 多人卡片发放升级失败（保留当前等级继续发放）：card={card.Id}，异常={ex.Message}。");
                return;
            }
        }
    }

    /// <summary>
    /// 在 CardPileCmd.Add 成功后补一次刷新事件，让顶栏牌组数字及时更新。
    /// </summary>
    /// <param name="addResult">加牌结果。</param>
    /// <param name="card">刚加入的卡片。</param>
    /// <param name="pileType">目标牌堆类型。</param>
    /// <param name="player">卡片所属玩家。</param>
    private static void NotifyPileAddFinished(CardPileAddResult addResult, CardModel card, PileType pileType, Player player)
    {
        if (!addResult.success)
        {
            return;
        }

        CardPile? pile = card?.Pile ?? pileType.GetPile(player);
        TryInvokeCardAddFinished(pile);
    }

    /// <summary>
    /// 安全触发牌组刷新事件；事件失败不影响实际发卡结果。
    /// </summary>
    /// <param name="pile">需要刷新显示的牌堆。</param>
    private static void TryInvokeCardAddFinished(CardPile? pile)
    {
        if (pile == null)
        {
            return;
        }

        try
        {
            pile.InvokeCardAddFinished();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 多人卡片发放触发牌组刷新事件失败（不影响实际发卡）：异常={ex.Message}。");
        }
    }
}
