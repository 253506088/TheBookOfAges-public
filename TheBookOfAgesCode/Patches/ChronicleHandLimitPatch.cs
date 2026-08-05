using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.Services;

namespace TheBookOfAges.TheBookOfAgesCode.Patches;

/// <summary>
/// 岁月史书手牌上限抽牌补丁：把游戏抽牌核心逻辑的硬编码 10 替换为 GM 手牌上限。
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Draw), typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool))]
public static class ChronicleHandLimitDrawPatch
{
    /// <summary>
    /// 当手牌上限仍是原生 10 时完全走游戏原逻辑；被修改后接管抽牌流程。
    /// </summary>
    public static bool Prefix(PlayerChoiceContext choiceContext, decimal count, Player player, bool fromHandDraw, ref Task<IEnumerable<CardModel>> __result)
    {
        if (GmRunContextService.IsMultiplayer() || ChronicleService.GetEffectiveHandLimit() == CardPile.MaxCardsInHand)
        {
            return true;
        }

        __result = ChronicleService.DrawCardsWithHandLimitAsync(choiceContext, count, player, fromHandDraw);
        return false;
    }
}

/// <summary>
/// 岁月史书手牌上限发牌补丁：让进入手牌的卡按 GM 上限判断，而不是 10 张后直接进弃牌堆。
/// </summary>
[HarmonyPatch]
public static class ChronicleHandLimitAddPatch
{
    /// <summary>
    /// 只 Patch CardPileCmd 的核心 Add 重载，所有短重载最终都会走到这里。
    /// </summary>
    public static MethodBase TargetMethod()
    {
        return AccessTools.Method(
            typeof(CardPileCmd),
            nameof(CardPileCmd.Add),
            [
                typeof(IEnumerable<CardModel>),
                typeof(CardPile),
                typeof(CardPilePosition),
                typeof(AbstractModel),
                typeof(bool),
                typeof(bool),
            ]);
    }

    /// <summary>
    /// 只有目标卡堆是手牌且 GM 上限不是 10 时才接管，其它卡堆保持原逻辑。
    /// </summary>
    public static bool Prefix(IEnumerable<CardModel> cards, CardPile newPile, CardPilePosition position, AbstractModel? clonedBy, bool skipVisuals, bool isChangingOwners, ref Task<IReadOnlyList<CardPileAddResult>> __result)
    {
        if (GmRunContextService.IsMultiplayer() || newPile.Type != PileType.Hand || ChronicleService.GetEffectiveHandLimit() == CardPile.MaxCardsInHand)
        {
            return true;
        }

        __result = ChronicleService.AddCardsToHandWithHandLimitAsync(cards, newPile, position, clonedBy, skipVisuals, isChangingOwners);
        return false;
    }
}

/// <summary>
/// 岁月史书手牌上限控制台补丁：让 card xxx hand 命令也跟随 GM 手牌上限。
/// </summary>
[HarmonyPatch(typeof(CardConsoleCmd), nameof(CardConsoleCmd.Process))]
public static class ChronicleHandLimitCardConsolePatch
{
    /// <summary>
    /// 只在手牌上限被修改时接管控制台 card 命令。
    /// </summary>
    public static bool Prefix(Player? issuingPlayer, string[] args, ref CmdResult __result)
    {
        if (GmRunContextService.IsMultiplayer() || ChronicleService.GetEffectiveHandLimit() == CardPile.MaxCardsInHand)
        {
            return true;
        }

        __result = ProcessWithHandLimit(issuingPlayer, args);
        return false;
    }

    /// <summary>
    /// 复刻游戏控制台发牌逻辑，仅把手牌已满判断改为 GM 上限。
    /// </summary>
    private static CmdResult ProcessWithHandLimit(Player? issuingPlayer, string[] args)
    {
        if (args.Length == 0)
        {
            return new CmdResult(success: false, "No card name specified.");
        }

        if (!RunManager.Instance.IsInProgress)
        {
            return new CmdResult(success: false, "A run is currently not in progress!");
        }

        if (issuingPlayer == null)
        {
            return new CmdResult(success: false, "A run is currently not in progress!");
        }

        PileType pileType = PileType.Hand;
        if (args.Length >= 2 && !Enum.TryParse(args[1], ignoreCase: true, out pileType))
        {
            return new CmdResult(success: false, "Unknown pile '" + args[1] + "'. Valid piles: " + string.Join(", ", Enum.GetNames<PileType>()));
        }

        if (pileType == PileType.Hand)
        {
            CardPile pile = PileType.Hand.GetPile(issuingPlayer);
            int count = pile.Cards.Count;
            int handLimit = ChronicleService.GetEffectiveHandLimit();
            if (count >= handLimit)
            {
                return new CmdResult(success: false, $"The hand is full ({count}/{handLimit}).");
            }
        }

        string cardName = args[0].ToUpperInvariant();
        CardModel? cardModel = ModelDb.AllCards.FirstOrDefault(card => card.Id.Entry == cardName);
        if (cardModel == null)
        {
            return new CmdResult(success: false, "Card '" + cardName + "' not found");
        }

        ICardScope? cardScope = pileType.IsCombatPile()
            ? CombatManager.Instance.DebugOnlyGetState()
            : RunManager.Instance.DebugOnlyGetState();
        if (cardScope == null)
        {
            return new CmdResult(success: false, "A run is currently not in progress!");
        }

        CardModel card = cardScope.CreateCard(cardModel, issuingPlayer);
        Task task = CardPileCmd.Add(card, pileType);
        return new CmdResult(task, success: true, $"Added card '{cardModel.Id.Entry}' to '{pileType}'");
    }
}

/// <summary>
/// 岁月史书手牌布局计算工具：超过 10 张时使用公式布局，避免 HandPosHelper 原生数组越界。
/// </summary>
internal static class ChronicleHandLayoutCalculator
{
    /// <summary>
    /// 动态计算 11 张以上手牌的位置；数量越多，横向间距越紧。
    /// </summary>
    public static Vector2 GetDynamicPosition(int handSize, int cardIndex)
    {
        float center = (handSize - 1) * 0.5f;
        float offset = cardIndex - center;
        float spacing = Mathf.Clamp(1180f / Math.Max(1, handSize - 1), 12f, 110f);
        float x = offset * spacing;
        float normalized = Math.Abs(offset) / Math.Max(1f, center);
        float y = -50f + normalized * normalized * 92f;
        return new Vector2(x, y);
    }

    /// <summary>
    /// 动态计算 11 张以上手牌的角度；角度有上限，避免极端数量时翻转过头。
    /// </summary>
    public static float GetDynamicAngle(int handSize, int cardIndex)
    {
        float center = (handSize - 1) * 0.5f;
        float offset = cardIndex - center;
        float angleStep = Mathf.Clamp(30f / Math.Max(1, handSize - 1), 0.2f, 3f);
        return Mathf.Clamp(offset * angleStep, -18f, 18f);
    }

    /// <summary>
    /// 动态计算 11 张以上手牌的缩放；不设硬上限，但给缩放保底避免完全看不见。
    /// </summary>
    public static Vector2 GetDynamicScale(int handSize)
    {
        float scale = Mathf.Clamp(0.68f - (handSize - 10) * 0.018f, 0.22f, 0.68f);
        return Vector2.One * scale;
    }
}

/// <summary>
/// 岁月史书手牌位置补丁。
/// </summary>
[HarmonyPatch(typeof(HandPosHelper), nameof(HandPosHelper.GetPosition))]
public static class ChronicleHandPositionPatch
{
    /// <summary>
    /// 超过 10 张时使用动态位置。
    /// </summary>
    public static bool Prefix(int handSize, int cardIndex, ref Vector2 __result)
    {
        if (GmRunContextService.IsMultiplayer() || handSize <= CardPile.MaxCardsInHand)
        {
            return true;
        }

        __result = ChronicleHandLayoutCalculator.GetDynamicPosition(handSize, cardIndex);
        return false;
    }
}

/// <summary>
/// 岁月史书手牌角度补丁。
/// </summary>
[HarmonyPatch(typeof(HandPosHelper), nameof(HandPosHelper.GetAngle))]
public static class ChronicleHandAnglePatch
{
    /// <summary>
    /// 超过 10 张时使用动态角度。
    /// </summary>
    public static bool Prefix(int handSize, int cardIndex, ref float __result)
    {
        if (GmRunContextService.IsMultiplayer() || handSize <= CardPile.MaxCardsInHand)
        {
            return true;
        }

        __result = ChronicleHandLayoutCalculator.GetDynamicAngle(handSize, cardIndex);
        return false;
    }
}

/// <summary>
/// 岁月史书手牌缩放补丁。
/// </summary>
[HarmonyPatch(typeof(HandPosHelper), nameof(HandPosHelper.GetScale))]
public static class ChronicleHandScalePatch
{
    /// <summary>
    /// 超过 10 张时使用动态缩放。
    /// </summary>
    public static bool Prefix(int handSize, ref Vector2 __result)
    {
        if (GmRunContextService.IsMultiplayer() || handSize <= CardPile.MaxCardsInHand)
        {
            return true;
        }

        __result = ChronicleHandLayoutCalculator.GetDynamicScale(handSize);
        return false;
    }
}

/// <summary>
/// Scrawl 写死 10 的补丁：让“抽满手牌”按 GM 手牌上限计算。
/// </summary>
[HarmonyPatch]
public static class ChronicleHandLimitScrawlPatch
{
    public static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(Scrawl), "OnPlay", [typeof(PlayerChoiceContext), typeof(CardPlay)]);
    }

    public static bool Prefix(Scrawl __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay, ref Task __result)
    {
        if (GmRunContextService.IsMultiplayer() || ChronicleService.GetEffectiveHandLimit() == CardPile.MaxCardsInHand)
        {
            return true;
        }

        __result = PlayWithHandLimitAsync(__instance, choiceContext);
        return false;
    }

    /// <summary>
    /// 使用 GM 手牌上限计算 Scrawl 的补牌数量。
    /// </summary>
    private static async Task PlayWithHandLimitAsync(Scrawl card, PlayerChoiceContext choiceContext)
    {
        if (card.Owner.PlayerCombatState == null)
        {
            return;
        }

        int count = ChronicleService.GetEffectiveHandLimit() - card.Owner.PlayerCombatState.Hand.Cards.Count;
        await CardPileCmd.Draw(choiceContext, count, card.Owner);
    }
}

/// <summary>
/// Pillage 写死 10 的补丁：连续抽攻击牌时按 GM 手牌上限判断是否继续。
/// </summary>
[HarmonyPatch]
public static class ChronicleHandLimitPillagePatch
{
    public static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(Pillage), "OnPlay", [typeof(PlayerChoiceContext), typeof(CardPlay)]);
    }

    public static bool Prefix(Pillage __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay, ref Task __result)
    {
        if (GmRunContextService.IsMultiplayer() || ChronicleService.GetEffectiveHandLimit() == CardPile.MaxCardsInHand)
        {
            return true;
        }

        __result = PlayWithHandLimitAsync(__instance, choiceContext, cardPlay);
        return false;
    }

    /// <summary>
    /// 使用 GM 手牌上限判断 Pillage 是否继续抽攻击牌。
    /// </summary>
    private static async Task PlayWithHandLimitAsync(Pillage card, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        ArgumentNullException.ThrowIfNull(cardPlay.Target, nameof(cardPlay.Target));
        await DamageCmd.Attack(card.DynamicVars.Damage.BaseValue).FromCard(card, cardPlay).Targeting(cardPlay.Target)
            .WithHitFx("vfx/vfx_attack_slash")
            .Execute(choiceContext);

        CardModel? drawnCard;
        do
        {
            drawnCard = await CardPileCmd.Draw(choiceContext, card.Owner);
        }
        while (drawnCard != null &&
               drawnCard.Type == CardType.Attack &&
               CardPile.GetCards(card.Owner, PileType.Hand).Count() < ChronicleService.GetEffectiveHandLimit());
    }
}
