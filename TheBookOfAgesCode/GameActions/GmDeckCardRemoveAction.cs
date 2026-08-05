using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace TheBookOfAges.TheBookOfAgesCode.GameActions;

/// <summary>
/// GM 牌组卡片删除网络动作：双方按同一批牌组下标删卡，避免多人模式下同名重复卡按 ID 删错。
/// </summary>
public sealed class GmDeckCardRemoveAction : GameAction
{
    private readonly Player _player;
    private readonly IReadOnlyList<uint> _deckIndexes;
    private readonly bool _inCombat;

    /// <summary>
    /// 当前网络动作所属玩家。
    /// </summary>
    public override ulong OwnerId => _player.NetId;

    /// <summary>
    /// 删卡在战斗中也只改 run 级牌组；动作类型跟随当前队列状态，避免放错同步队列。
    /// </summary>
    public override GameActionType ActionType => _inCombat ? GameActionType.CombatPlayPhaseOnly : GameActionType.Any;

    /// <summary>
    /// 创建 GM 牌组卡片删除网络动作。
    /// </summary>
    /// <param name="player">要删除牌组卡片的玩家。</param>
    /// <param name="deckIndexes">要删除的牌组下标列表。</param>
    /// <param name="inCombat">提交动作时是否处于战斗中。</param>
    public GmDeckCardRemoveAction(Player player, IReadOnlyList<uint> deckIndexes, bool inCombat)
    {
        _player = player;
        _deckIndexes = deckIndexes.ToList();
        _inCombat = inCombat;
    }

    /// <summary>
    /// 在本机执行网络动作；按下标倒序删除，避免前面的删除让后续下标前移。
    /// </summary>
    protected override Task ExecuteAction()
    {
        int removedCount = GmDeckCardRemoveExecutor.RemoveByDeckIndexes(_player, _deckIndexes);
        MainFile.Logger.Info($"GM 多人卡片删除网络动作执行完成：玩家NetId={_player.NetId}，战斗中={_inCombat}，请求数量={_deckIndexes.Count}，实际删除={removedCount}。");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 把本地动作转换成可同步的网络动作数据。
    /// </summary>
    public override INetAction ToNetAction()
    {
        return new NetGmDeckCardRemoveAction
        {
            DeckIndexes = _deckIndexes.ToList(),
            InCombat = _inCombat
        };
    }

    /// <summary>
    /// 返回便于日志定位的动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"GmDeckCardRemoveAction player {_player.NetId} inCombat {_inCombat} deckIndexes [{string.Join(",", _deckIndexes)}]";
    }
}

/// <summary>
/// GM 牌组卡片删除网络数据：只传牌组下标，不传卡 ID，解决重复同名卡定位不准的问题。
/// </summary>
public struct NetGmDeckCardRemoveAction : INetAction
{
    /// <summary>
    /// 要删除的牌组下标列表。
    /// </summary>
    public List<uint> DeckIndexes { get; set; }

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
        return new GmDeckCardRemoveAction(player, DeckIndexes ?? [], InCombat);
    }

    /// <summary>
    /// 序列化牌组下标列表。
    /// </summary>
    /// <param name="writer">游戏网络包写入器。</param>
    public void Serialize(PacketWriter writer)
    {
        IReadOnlyList<uint> indexes = DeckIndexes ?? [];
        writer.WriteBool(InCombat);
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
        InCombat = reader.ReadBool();
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
        return $"NetGmDeckCardRemoveAction inCombat {InCombat} deckIndexes [{string.Join(",", DeckIndexes ?? [])}]";
    }
}

/// <summary>
/// GM 牌组卡片删除执行器：集中处理按下标删卡、二次校验和牌组数字刷新事件。
/// </summary>
internal static class GmDeckCardRemoveExecutor
{
    /// <summary>
    /// 按牌组下标批量删除卡片。
    /// </summary>
    /// <param name="player">要操作的玩家。</param>
    /// <param name="deckIndexes">要删除的牌组下标列表。</param>
    public static int RemoveByDeckIndexes(Player player, IReadOnlyList<uint> deckIndexes)
    {
        if (player?.Deck == null)
        {
            MainFile.Logger.Info("GM 多人卡片删除执行跳过：玩家或牌组为空。");
            return 0;
        }

        CardPile deck = PileType.Deck.GetPile(player);
        List<uint> orderedIndexes = deckIndexes
            .Distinct()
            .OrderByDescending(index => index)
            .ToList();
        int removedCount = 0;

        foreach (uint deckIndex in orderedIndexes)
        {
            if (deckIndex >= deck.Cards.Count)
            {
                MainFile.Logger.Info($"GM 多人卡片删除跳过越界下标：玩家NetId={player.NetId}，deckIndex={deckIndex}，牌组数量={deck.Cards.Count}。");
                continue;
            }

            CardModel card = deck.Cards[(int)deckIndex];
            if (card.Pile == null || card.Pile.Type != PileType.Deck || card.Pile != deck)
            {
                MainFile.Logger.Info($"GM 多人卡片删除跳过非当前牌组卡：玩家NetId={player.NetId}，deckIndex={deckIndex}，card={card.Id}，pile={card.Pile?.Type.ToString() ?? "空"}。");
                continue;
            }

            try
            {
                card.RemoveFromCurrentPile();
                removedCount++;
                MainFile.Logger.Info($"GM 多人卡片删除成功：玩家NetId={player.NetId}，deckIndex={deckIndex}，card={card.Id}。");
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"GM 多人卡片删除异常：玩家NetId={player.NetId}，deckIndex={deckIndex}，card={card.Id}，异常={ex.Message}。");
            }
        }

        if (removedCount > 0)
        {
            TryInvokeCardAddFinished(deck);
        }

        return removedCount;
    }

    /// <summary>
    /// 安全触发牌组刷新事件；删除卡不会自动刷新顶栏牌组数字，这里补一次。
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
            MainFile.Logger.Info($"GM 多人卡片删除触发牌组刷新事件失败（不影响实际删除）：异常={ex.Message}。");
        }
    }
}
