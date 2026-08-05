using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using System.Reflection;
using TheBookOfAges.TheBookOfAgesCode.GameActions;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.UI;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// 卡片附魔服务：读取游戏附魔库、筛选可附魔牌组卡片，并负责批量附魔。
/// </summary>
public sealed class EnchantmentService
{
    private readonly CardService _cardService = new();
    private static readonly HashSet<string> SafeCustomAmountBaseGameEnchantments = new(StringComparer.Ordinal)
    {
        "Adroit",
        "Nimble",
        "Sharp",
        "Momentum",
        "Swift",
        "Sown",
        "Vigorous",
        "Goopy",
    };
    private static readonly HashSet<string> UnsafeCustomAmountBaseGameEnchantments = new(StringComparer.Ordinal)
    {
        "Glam",
        "Spiral",
    };
    private const int MinCustomAmount = 1;
    private const int MaxCustomAmount = 99;

    /// <summary>
    /// 读取全部附魔，并按关键字和来源筛选。
    /// </summary>
    public IReadOnlyList<EnchantmentDisplayInfo> GetEnchantments(string keyword, string sourceFilter)
    {
        string normalizedKeyword = Normalize(keyword);
        return ModelDb.DebugEnchantments
            .Where(enchantment => enchantment != null)
            .Select(TryBuildDisplayInfo)
            .Where(info => info != null)
            .Cast<EnchantmentDisplayInfo>()
            .Where(info => MatchesKeyword(info, normalizedKeyword))
            .Where(info => MatchesSource(info, sourceFilter))
            .OrderBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 获取附魔来源筛选项。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetSourceOptions()
    {
        return
        [
            new GmFilterOption(CardService.AllOptionKey, GmUiText.Pick("全部来源", "All Sources")),
            new GmFilterOption(CardService.SourceBaseGameKey, GmUiText.Pick("游戏本体", "Base Game")),
            new GmFilterOption(CardService.SourceModsKey, "Mods"),
        ];
    }

    /// <summary>
    /// 获取当前牌组中没有附魔且能被指定附魔作用的卡片。
    /// </summary>
    public IReadOnlyList<CardDisplayInfo> GetEnchantableDeckCards(EnchantmentDisplayInfo enchantment, string keyword, string categoryFilter)
    {
        return _cardService.GetDeckCardsSorted(keyword, categoryFilter)
            .Where(info => CanEnchantNewEffect(enchantment.Model, info.Card))
            .ToList();
    }

    /// <summary>
    /// 批量为玩家牌组里的卡片附魔；每张卡执行前都会重新检查当前状态。
    /// </summary>
    public async Task<CardEnchantResult> EnchantDeckCardsAsync(EnchantmentDisplayInfo enchantment, IReadOnlyList<CardDisplayInfo> selectedCards, int amount)
    {
        CardEnchantResult result = new();
        int normalizedAmount = Math.Clamp(amount, MinCustomAmount, MaxCustomAmount);
        GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(GmFeature.EnchantCard);
        if (!availability.IsAllowed)
        {
            result.FailedCount += selectedCards.Count;
            result.Messages.Add(availability.Reason);
            MainFile.Logger.Info($"卡片附魔已被多人安全闸门阻止：选择数量={selectedCards.Count}，附魔={enchantment.Name}，数值={normalizedAmount}，原因={availability.Reason}。");
            return result;
        }

        Player? player = GetCurrentPlayer();
        if (player?.Deck == null)
        {
            result.FailedCount += selectedCards.Count;
            result.Messages.Add(GmUiText.CardEnchantNotInRun);
            return result;
        }

        if (GmRunContextService.IsMultiplayer())
        {
            return await EnchantDeckCardsByNetworkActionAsync(player, enchantment, selectedCards, normalizedAmount);
        }

        foreach (CardDisplayInfo info in selectedCards)
        {
            ApplyEnchantmentToCard(player, enchantment, info, normalizedAmount, result);
        }

        return result;
    }

    /// <summary>
    /// 判断当前是否处于可读取牌组的局内状态。
    /// </summary>
    public bool CanOperateDeck()
    {
        return GmRunContextService.GetFeatureAvailability(GmFeature.EnchantCard).IsAllowed
            && GetCurrentPlayer()?.Deck != null;
    }

    /// <summary>
    /// 构造单个附魔的展示数据；任何字段读取失败都跳过该附魔，避免卡死整个页面。
    /// </summary>
    private static EnchantmentDisplayInfo? TryBuildDisplayInfo(EnchantmentModel enchantment)
    {
        try
        {
            return BuildDisplayInfo(enchantment);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"附魔解析失败，已跳过：{enchantment.Id}，异常={ex.Message}。");
            return null;
        }
    }

    /// <summary>
    /// 把游戏附魔模型转换成 GM 页面需要的展示数据。
    /// </summary>
    private static EnchantmentDisplayInfo BuildDisplayInfo(EnchantmentModel enchantment)
    {
        string idText = enchantment.Id.ToString();
        Type enchantmentType = enchantment.GetType();
        string assemblyName = enchantmentType.Assembly.GetName().Name ?? string.Empty;
        string sourceKey = ToSourceKey(assemblyName);
        CustomAmountSupport support = AnalyzeCustomAmountSupport(enchantment, enchantmentType, sourceKey);
        return new EnchantmentDisplayInfo
        {
            Id = enchantment.Id,
            IdText = idText,
            Name = SafeText(GmLocalizationService.ResolveText(enchantment.Title, "解析附魔名称失败 / Failed to resolve enchantment title"), idText),
            Description = SafeText(ReadDescription(enchantment), GmUiText.NoDescription),
            SourceKey = sourceKey,
            Source = ToSourceText(assemblyName),
            Icon = TryReadIcon(enchantment),
            CanCustomizeAmountSafely = support.CanCustomizeSafely,
            CanCustomizeAmountWhenUnsafe = support.CanCustomizeWhenUnsafe,
            IsModEnchantment = support.IsModEnchantment,
            CustomAmountHint = support.Hint,
            Model = enchantment,
        };
    }

    /// <summary>
    /// 读取附魔描述；临时 mutable 副本只用于预览，不会绑定到任何卡片。
    /// </summary>
    private static string? ReadDescription(EnchantmentModel enchantment)
    {
        EnchantmentModel preview = enchantment.ToMutable();
        preview.Amount = 1;
        TryRecalculateValues(preview, preview.Amount, enchantment.Id.ToString());
        return GmLocalizationService.ResolveText(preview.DynamicDescription, "解析附魔描述失败 / Failed to resolve enchantment description")
            ?? GmLocalizationService.ResolveRawText(preview.DynamicDescription, "解析附魔原始描述失败 / Failed to resolve raw enchantment description");
    }

    /// <summary>
    /// 安全读取附魔图标，失败时返回空图标。
    /// </summary>
    private static Texture2D? TryReadIcon(EnchantmentModel enchantment)
    {
        try
        {
            return enchantment.Icon;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"读取附魔图标失败，已用 null 兜底：{enchantment.Id}，异常={ex.Message}。");
            return null;
        }
    }

    /// <summary>
    /// 附魔关键字匹配名称、描述和 ID。
    /// </summary>
    private static bool MatchesKeyword(EnchantmentDisplayInfo info, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return Normalize(info.Name).Contains(keyword) ||
               Normalize(info.Description).Contains(keyword) ||
               Normalize(info.IdText).Contains(keyword);
    }

    /// <summary>
    /// 来源筛选：BaseGame 只匹配本体；Mods 表示非本体。
    /// </summary>
    private static bool MatchesSource(EnchantmentDisplayInfo info, string filter)
    {
        if (string.IsNullOrEmpty(filter) || string.Equals(filter, CardService.AllOptionKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(filter, CardService.SourceBaseGameKey, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(info.SourceKey, CardService.SourceBaseGameKey, StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(filter, CardService.SourceModsKey, StringComparison.OrdinalIgnoreCase))
        {
            return !string.Equals(info.SourceKey, CardService.SourceBaseGameKey, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    /// <summary>
    /// 判断一张卡是否能获得一个新的附魔效果；这里只允许没有附魔的卡。
    /// </summary>
    private static bool CanEnchantNewEffect(EnchantmentModel enchantment, CardModel card)
    {
        try
        {
            return card.Enchantment == null && enchantment.CanEnchant(card);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"检查卡片是否可附魔失败，已跳过：卡片={card.Id}，附魔={enchantment.Id}，异常={ex.Message}。");
            return false;
        }
    }

    /// <summary>
    /// 多人模式下把牌组附魔转换成同步网络动作；只提交牌组下标、附魔 ID 和数值。
    /// </summary>
    /// <param name="player">当前本机玩家。</param>
    /// <param name="enchantment">要使用的附魔。</param>
    /// <param name="selectedCards">用户选中的牌组卡片。</param>
    /// <param name="amount">附魔数值。</param>
    private static async Task<CardEnchantResult> EnchantDeckCardsByNetworkActionAsync(
        Player player,
        EnchantmentDisplayInfo enchantment,
        IReadOnlyList<CardDisplayInfo> selectedCards,
        int amount)
    {
        CardEnchantResult result = new();
        CardPile playerDeck = PileType.Deck.GetPile(player);
        List<uint> deckIndexes = [];
        List<CardDisplayInfo> submittedCards = [];
        HashSet<int> selectedDeckIndexes = [];

        foreach (CardDisplayInfo info in selectedCards)
        {
            TryCollectDeckCardEnchantIndex(player, playerDeck, enchantment, info, selectedDeckIndexes, deckIndexes, submittedCards, result);
        }

        if (deckIndexes.Count == 0)
        {
            return result;
        }

        bool submitted = await GmNetworkCommandService.TryEnqueueGameActionAsync(new GmDeckCardEnchantAction(player, deckIndexes, enchantment.IdText, amount), "卡片附魔");
        if (!submitted)
        {
            result.FailedCount += deckIndexes.Count;
            foreach (CardDisplayInfo info in submittedCards)
            {
                string displayName = string.IsNullOrEmpty(info.Name) ? info.IdText : info.Name;
                result.Messages.Add(string.Format(GmUiText.CardEnchantFailedNamed, displayName));
            }

            return result;
        }

        result.SuccessCount += deckIndexes.Count;
        foreach (CardDisplayInfo info in submittedCards)
        {
            string displayName = string.IsNullOrEmpty(info.Name) ? info.IdText : info.Name;
            result.Messages.Add(string.Format(GmUiText.CardEnchantSubmittedNamed, displayName, enchantment.Name));
        }

        MainFile.Logger.Info($"多人模式卡片附魔已提交同步动作：附魔={enchantment.IdText}，数值={amount}，选择数量={selectedCards.Count}，提交数量={deckIndexes.Count}，下标=[{string.Join(",", deckIndexes)}]。");
        return result;
    }

    /// <summary>
    /// 把一张 UI 选中的卡转换成当前牌组下标，并完成附魔入队前的安全检查。
    /// </summary>
    private static void TryCollectDeckCardEnchantIndex(
        Player player,
        CardPile playerDeck,
        EnchantmentDisplayInfo enchantment,
        CardDisplayInfo info,
        HashSet<int> selectedDeckIndexes,
        List<uint> deckIndexes,
        List<CardDisplayInfo> submittedCards,
        CardEnchantResult result)
    {
        string displayName = string.IsNullOrEmpty(info.Name) ? info.IdText : info.Name;
        try
        {
            CardModel card = info.Card;
            CardPile? currentPile = card.Pile;
            if (currentPile == null || currentPile.Type != PileType.Deck)
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.CardEnchantFailedNamed, displayName));
                MainFile.Logger.Info($"多人卡片附魔提交失败：卡片不在 Deck 里 / Card not in Deck pile: {info.IdText} (currentPile={currentPile?.Type.ToString() ?? "null"})");
                return;
            }

            if (currentPile != playerDeck)
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.CardEnchantFailedNamed, displayName));
                MainFile.Logger.Info($"多人卡片附魔提交失败：所在 pile 不是当前玩家 Deck / Card pile is not the current player's Deck: {info.IdText}，玩家NetId={player.NetId}。");
                return;
            }

            int deckIndex = FindDeckCardIndex(playerDeck, card);
            if (deckIndex < 0)
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.CardEnchantFailedNamed, displayName));
                MainFile.Logger.Info($"多人卡片附魔提交失败：无法定位牌组下标 / Deck index not found: {info.IdText}，玩家NetId={player.NetId}。");
                return;
            }

            if (!selectedDeckIndexes.Add(deckIndex))
            {
                result.SkippedCount++;
                result.Messages.Add(string.Format(GmUiText.CardEnchantSkippedNamed, displayName));
                MainFile.Logger.Info($"多人卡片附魔提交跳过重复下标：card={info.IdText}，deckIndex={deckIndex}，玩家NetId={player.NetId}。");
                return;
            }

            if (!CanEnchantNewEffect(enchantment.Model, card))
            {
                result.SkippedCount++;
                result.Messages.Add(string.Format(GmUiText.CardEnchantSkippedNamed, displayName));
                MainFile.Logger.Info($"多人卡片附魔提交跳过：卡片已有附魔或不满足条件，card={info.IdText}，enchantment={enchantment.IdText}。");
                return;
            }

            deckIndexes.Add((uint)deckIndex);
            submittedCards.Add(info);
        }
        catch (Exception ex)
        {
            result.FailedCount++;
            result.Messages.Add(string.Format(GmUiText.CardEnchantFailedNamed, displayName));
            MainFile.Logger.Info($"多人卡片附魔提交异常 / Card enchant submit exception: {info.IdText} / {ex.Message}");
        }
    }

    /// <summary>
    /// 按引用查找卡片在当前牌组中的下标，避免同名重复卡按 ID 误定位。
    /// </summary>
    private static int FindDeckCardIndex(CardPile deck, CardModel card)
    {
        IReadOnlyList<CardModel> cards = deck.Cards;
        for (int index = 0; index < cards.Count; index++)
        {
            if (ReferenceEquals(cards[index], card))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// 对单张卡执行附魔，并把结果累加到汇总对象。
    /// </summary>
    private static void ApplyEnchantmentToCard(Player player, EnchantmentDisplayInfo enchantment, CardDisplayInfo info, int amount, CardEnchantResult result)
    {
        string displayName = string.IsNullOrEmpty(info.Name) ? info.IdText : info.Name;
        try
        {
            if (!IsCurrentDeckCard(player, info.Card))
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.CardEnchantFailedNamed, displayName));
                MainFile.Logger.Info($"卡片附魔失败：卡片已不在当前玩家牌组中，卡片={info.IdText}。");
                return;
            }

            if (info.Card.Enchantment != null)
            {
                result.SkippedCount++;
                result.Messages.Add(string.Format(GmUiText.CardEnchantSkippedNamed, displayName));
                MainFile.Logger.Info($"卡片附魔跳过：卡片已有附魔，卡片={info.IdText}。");
                return;
            }

            if (!enchantment.Model.CanEnchant(info.Card))
            {
                result.SkippedCount++;
                result.Messages.Add(string.Format(GmUiText.CardEnchantSkippedNamed, displayName));
                MainFile.Logger.Info($"卡片附魔跳过：游戏规则不允许该附魔作用于该卡，附魔={enchantment.IdText}，卡片={info.IdText}。");
                return;
            }

            CardCmd.Enchant(enchantment.Model.ToMutable(), info.Card, amount);
            result.SuccessCount++;
            result.Messages.Add(string.Format(GmUiText.CardEnchantSuccessNamed, displayName, enchantment.Name));
            MainFile.Logger.Info($"卡片附魔成功：卡片={info.IdText}，附魔={enchantment.IdText}，数值={amount}。");
        }
        catch (Exception ex)
        {
            result.FailedCount++;
            result.Messages.Add(string.Format(GmUiText.CardEnchantFailedNamed, displayName));
            MainFile.Logger.Info($"卡片附魔异常：卡片={info.IdText}，附魔={enchantment.IdText}，数值={amount}，异常={ex.Message}。");
        }
    }

    /// <summary>
    /// 确认卡片仍然属于当前玩家牌组，避免修改已经移动到别处的实例。
    /// </summary>
    private static bool IsCurrentDeckCard(Player player, CardModel card)
    {
        CardPile? currentPile = card.Pile;
        if (currentPile == null || currentPile.Type != PileType.Deck)
        {
            return false;
        }

        return currentPile == PileType.Deck.GetPile(player);
    }

    /// <summary>
    /// 获取当前玩家；没有局内状态时返回空。
    /// </summary>
    private static Player? GetCurrentPlayer()
    {
        return GmRunContextService.GetCurrentPlayer();
    }

    /// <summary>
    /// 分析当前附魔是否支持手填数值，并生成给 UI 使用的提示信息。
    /// </summary>
    private static CustomAmountSupport AnalyzeCustomAmountSupport(EnchantmentModel enchantment, Type enchantmentType, string sourceKey)
    {
        bool isModEnchantment = !string.Equals(sourceKey, CardService.SourceBaseGameKey, StringComparison.OrdinalIgnoreCase);
        if (!isModEnchantment)
        {
            if (SafeCustomAmountBaseGameEnchantments.Contains(enchantmentType.Name))
            {
                return new CustomAmountSupport(true, false, false, GmUiText.CardEnchantSafeCustomAmountHint);
            }

            if (UnsafeCustomAmountBaseGameEnchantments.Contains(enchantmentType.Name))
            {
                return new CustomAmountSupport(false, true, false, GmUiText.CardEnchantUnsafeOverrideHint);
            }

            return new CustomAmountSupport(false, false, false, GmUiText.CardEnchantFixedAmountHint);
        }

        bool amountReactive = DetectAmountReactivePreview(enchantment);
        bool hasCustomSavedProperties = HasCustomSavedProperties(enchantmentType);
        if (amountReactive && !hasCustomSavedProperties)
        {
            return new CustomAmountSupport(true, false, true, GmUiText.CardEnchantModSafeCustomAmountHint);
        }

        string hint = hasCustomSavedProperties
            ? GmUiText.CardEnchantModSavedPropertyHint
            : GmUiText.CardEnchantUnsafeOverrideHint;
        return new CustomAmountSupport(false, true, true, hint);
    }

    /// <summary>
    /// 运行时比较 Amount=1 和 Amount=2 的预览差异，粗略判断该附魔是否由 Amount 驱动。
    /// </summary>
    private static bool DetectAmountReactivePreview(EnchantmentModel enchantment)
    {
        try
        {
            EnchantmentPreviewSignature amountOne = ReadPreviewSignature(enchantment, 1);
            EnchantmentPreviewSignature amountTwo = ReadPreviewSignature(enchantment, 2);
            return !string.Equals(amountOne.Description, amountTwo.Description, StringComparison.Ordinal) ||
                   amountOne.DisplayAmount != amountTwo.DisplayAmount ||
                   amountOne.ShowAmount != amountTwo.ShowAmount;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"附魔数值驱动探测失败：附魔={enchantment.Id}，异常={ex.Message}。");
            return false;
        }
    }

    /// <summary>
    /// 读取某个 Amount 下的附魔预览签名，用于未知 Mod 附魔的安全探测。
    /// </summary>
    private static EnchantmentPreviewSignature ReadPreviewSignature(EnchantmentModel enchantment, int amount)
    {
        EnchantmentModel preview = enchantment.ToMutable();
        preview.Amount = amount;
        TryRecalculateValues(preview, amount, enchantment.Id.ToString());
        string description = SafeText(
            GmLocalizationService.ResolveText(preview.DynamicDescription, "附魔预览描述解析失败")
            ?? GmLocalizationService.ResolveRawText(preview.DynamicDescription, "附魔预览原始描述解析失败"),
            GmUiText.NoDescription);
        return new EnchantmentPreviewSignature(description, preview.DisplayAmount, preview.ShowAmount);
    }

    /// <summary>
    /// 检查附魔子类上是否声明了额外的 SavedProperty，用于规避读档丢值风险。
    /// </summary>
    private static bool HasCustomSavedProperties(Type enchantmentType)
    {
        return enchantmentType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(property => property.DeclaringType != typeof(EnchantmentModel) &&
                             property.GetCustomAttribute<SavedPropertyAttribute>() != null);
    }

    /// <summary>
    /// 尝试刷新附魔的动态描述和展示数值；失败时只记日志，不阻断整个页面。
    /// </summary>
    private static void TryRecalculateValues(EnchantmentModel enchantment, int amount, string enchantmentId)
    {
        try
        {
            enchantment.RecalculateValues();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"附魔预览重算失败：附魔={enchantmentId}，Amount={amount}，异常={ex.Message}。");
        }
    }

    /// <summary>
    /// 把来源程序集名转换为稳定筛选键。
    /// </summary>
    private static string ToSourceKey(string? assemblyName)
    {
        return string.Equals(assemblyName, "sts2", StringComparison.OrdinalIgnoreCase)
            ? CardService.SourceBaseGameKey
            : CardService.SourceModsKey;
    }

    /// <summary>
    /// 把来源程序集名转换为展示文本。
    /// </summary>
    private static string ToSourceText(string? assemblyName)
    {
        return string.Equals(assemblyName, "sts2", StringComparison.OrdinalIgnoreCase)
            ? GmUiText.Pick("游戏本体", "Base Game")
            : string.IsNullOrWhiteSpace(assemblyName) ? "Mods" : assemblyName;
    }

    /// <summary>
    /// 安全文本兜底。
    /// </summary>
    private static string SafeText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    /// <summary>
    /// 统一关键字大小写，便于中英文搜索。
    /// </summary>
    private static string Normalize(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// 附魔手填数值能力的内部分析结果。
    /// </summary>
    private readonly record struct CustomAmountSupport(bool CanCustomizeSafely, bool CanCustomizeWhenUnsafe, bool IsModEnchantment, string Hint);

    /// <summary>
    /// 用于比较不同 Amount 下附魔预览输出的轻量签名。
    /// </summary>
    private readonly record struct EnchantmentPreviewSignature(string Description, int DisplayAmount, bool ShowAmount);
}
