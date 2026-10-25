using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.GameActions;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.UI;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// 卡片服务：实时读取游戏卡片库、分类筛选、批量发放到牌组与手牌。
/// 战斗中走 CardPileCmd.Add 双发到 Deck + Hand；
/// 非战斗中优先走控制台 CardConsoleCmd.Process，失败后用 CardPileCmd.Add 兜底。
/// </summary>
public sealed class CardService
{
    /// <summary>
    /// 用于 X 费卡的特殊费用值，避免与真实费用冲突。
    /// </summary>
    public const int CostXSentinel = -1;

    /// <summary>
    /// 「无色」职业 key，保留 ASCII 以便筛选匹配稳定。
    /// </summary>
    public const string ColorlessKey = "Colorless";

    /// <summary>
    /// 全部筛选项的 key。
    /// </summary>
    public const string AllOptionKey = "All";

    /// <summary>
    /// 「3+ 费」的稳定 key。
    /// </summary>
    public const string Cost3PlusKey = "3+";

    /// <summary>
    /// 「X 费」的稳定 key。
    /// </summary>
    public const string CostXKey = "X";

    /// <summary>
    /// 「游戏本体」来源 key。
    /// </summary>
    public const string SourceBaseGameKey = "BaseGame";

    /// <summary>
    /// 「Mods」来源 key（除本体之外的所有 mod）。
    /// </summary>
    public const string SourceModsKey = "Mods";

    /// <summary>
    /// 卡片导出固定分类顺序，严格按老大爷要求的六类输出文件。
    /// </summary>
    private static readonly string[] CardExportCategoryOrder = ["攻击", "技能", "能力", "诅咒", "状态", "任务"];

    /// <summary>
    /// 反射读取游戏私有语言表加载方法，用于在不切换当前游戏语言的前提下读取中英文卡名。
    /// </summary>
    private static readonly MethodInfo? LoadTablesFromPathMethod = typeof(LocManager).GetMethod("LoadTablesFromPath", BindingFlags.Static | BindingFlags.NonPublic);

    /// <summary>
    /// 已加载的导出语言表缓存：languageCode → tableName → LocTable。
    /// 避免同一次游戏里重复导出时反复读取资源。
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, LocTable>> ExportLanguageTablesCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 保护导出语言表缓存的锁，避免极端情况下重复加载同一语言资源。
    /// </summary>
    private static readonly object ExportLanguageTablesLock = new();

    private readonly CardConsoleCmd _cardConsoleCmd = new();

    /// <summary>
    /// 读取并构造卡片展示数据。
    /// </summary>
    /// <param name="keyword">关键字，匹配卡名 + 描述。</param>
    /// <param name="characterFilter">角色筛选（卡池 Title 或 ColorlessKey）。</param>
    /// <param name="typeFilter">卡片类型，传枚举名或本地化文本均可。</param>
    /// <param name="rarityFilter">稀有度。</param>
    /// <param name="costFilter">费用筛选，"0"/"1"/"2"/"3+"/"X" 或 "全部费用"。</param>
    /// <param name="sourceFilter">来源筛选。</param>
    /// <param name="includeMultiplayer">是否展示多人模式专属卡。</param>
    /// <param name="showUpgraded">是否展示升级版（方案 A：只在卡名加 +1 后缀）。</param>
    public IReadOnlyList<CardDisplayInfo> GetCards(
        string keyword,
        string characterFilter,
        string typeFilter,
        string rarityFilter,
        string costFilter,
        string sourceFilter,
        bool includeMultiplayer,
        bool showUpgraded)
    {
        string normalizedKeyword = Normalize(keyword);

        return ModelDb.AllCards
            .Where(card => card != null)
            .Select(card => TryBuildDisplayInfo(card, showUpgraded))
            .Where(info => info != null)
            .Cast<CardDisplayInfo>()
            .Where(info => MatchesMultiplayer(info, includeMultiplayer))
            .Where(info => MatchesKeyword(info, normalizedKeyword))
            .Where(info => MatchesCharacter(info, characterFilter))
            .Where(info => MatchesType(info, typeFilter))
            .Where(info => MatchesRarity(info, rarityFilter))
            .Where(info => MatchesCost(info, costFilter))
            .Where(info => MatchesSource(info, sourceFilter))
            .OrderBy(info => GetCharacterOrder(info.CharacterKey))
            .ThenBy(info => GetTypeOrder(info.TypeKey))
            .ThenBy(info => GetRarityOrder(info.RarityKey))
            .ThenBy(info => GetCostOrder(info.Cost, info.CostsX))
            .ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 仅按「桶维度」(showUpgraded × includeMultiplayer) 取卡片显示数据，
    /// 不应用 keyword / character / type / rarity / cost / source 这些可视后期切换的筛选。
    /// 用于 GmUiManager 卡片缓存桶首屏一次性构建：之后切筛选只显隐，不再走 BuildDisplayInfo。
    /// </summary>
    public IReadOnlyList<CardDisplayInfo> GetAllCardsForBucket(bool includeMultiplayer, bool showUpgraded)
    {
        return ModelDb.AllCards
            .Where(card => card != null)
            .Select(card => TryBuildDisplayInfo(card, showUpgraded))
            .Where(info => info != null)
            .Cast<CardDisplayInfo>()
            .Where(info => MatchesMultiplayer(info, includeMultiplayer))
            .OrderBy(info => GetCharacterOrder(info.CharacterKey))
            .ThenBy(info => GetTypeOrder(info.TypeKey))
            .ThenBy(info => GetRarityOrder(info.RarityKey))
            .ThenBy(info => GetCostOrder(info.Cost, info.CostsX))
            .ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 按【攻击、技能、能力、诅咒、状态、任务】把当前游戏已注册的全部卡片导出成 6 个文本文件。
    /// 每行仅输出「中文名 | 英文名」，文件写到 Mod DLL 同目录下的 exports/cards/时间戳 目录。
    /// </summary>
    public string ExportAllCardsByType()
    {
        string exportDirectory = CreateCardExportDirectory();
        Dictionary<string, List<string>> exportBuckets = CreateCardExportBuckets();
        int exportedCount = 0;
        int skippedCount = 0;

        foreach (CardModel? card in ModelDb.AllCards)
        {
            if (card == null)
            {
                continue;
            }

            if (!TryGetCardExportCategory(card.Type, out string categoryName))
            {
                skippedCount++;
                MainFile.Logger.Info($"[卡片导出] 跳过未支持的卡片类型：card={card.Id}，type={card.Type}");
                continue;
            }

            (string chineseName, string englishName) = GetBilingualCardName(card);
            exportBuckets[categoryName].Add($"{chineseName} | {englishName}");
            exportedCount++;
        }

        foreach (string categoryName in CardExportCategoryOrder)
        {
            List<string> lines = exportBuckets[categoryName]
                .OrderBy(line => line, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            string filePath = Path.Combine(exportDirectory, $"{categoryName}.txt");
            File.WriteAllLines(filePath, lines, Encoding.UTF8);
            MainFile.Logger.Info($"[卡片导出] 已生成文件：{filePath}");
        }

        MainFile.Logger.Info($"[卡片导出] 导出完成：目录={exportDirectory}，导出卡片={exportedCount}，跳过={skippedCount}");
        return exportDirectory;
    }

    /// <summary>
    /// 在已构造的 CardDisplayInfo 上跑「用户态筛选」(keyword / character / type / rarity / cost / source)。
    /// 用 GmUiManager 切筛选时遍历缓存桶判定 button 是否 Visible，避免重新走 BuildDisplayInfo。
    /// 注：MatchesMultiplayer 已经在桶维度处理过，这里不再重复判断。
    /// </summary>
    public bool MatchesUserFilters(
        CardDisplayInfo info,
        string keyword,
        string characterFilter,
        string typeFilter,
        string rarityFilter,
        string costFilter,
        string sourceFilter)
    {
        string normalizedKeyword = Normalize(keyword);
        return MatchesKeyword(info, normalizedKeyword)
            && MatchesCharacter(info, characterFilter)
            && MatchesType(info, typeFilter)
            && MatchesRarity(info, rarityFilter)
            && MatchesCost(info, costFilter)
            && MatchesSource(info, sourceFilter);
    }

    /// <summary>
    /// 获取角色筛选项（含「全部角色」+ 各职业卡池 + 「无色」）。
    /// 顺序按当前角色优先，然后是其他职业，最后是无色。
    /// Key 为卡池 Title 或 ColorlessKey；Text 为本地化展示名。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetCharacterOptions()
    {
        List<GmFilterOption> options = [new GmFilterOption(AllOptionKey, GmUiText.CardCharacterAll)];
        string? currentKey = GetCurrentCharacterKey();
        HashSet<string> added = new(StringComparer.OrdinalIgnoreCase);

        // 当前角色排第一
        if (!string.IsNullOrEmpty(currentKey))
        {
            CardPoolModel? currentPool = ModelDb.AllCharacterCardPools
                .FirstOrDefault(pool => string.Equals(pool.Title, currentKey, StringComparison.Ordinal));
            if (currentPool != null)
            {
                // key 仍用英文 Title 保证筛选键值稳定；text 走 ResolveCharacterNameByCardPool 取游戏内置本地化标题
                string displayName = GmLocalizationService.ResolveCharacterNameByCardPool(currentPool, currentPool.Title);
                options.Add(new GmFilterOption(currentPool.Title, displayName));
                added.Add(currentPool.Title);
            }
        }

        // 其他职业按 Title 排序
        foreach (CardPoolModel pool in ModelDb.AllCharacterCardPools.OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            if (added.Add(pool.Title))
            {
                string displayName = GmLocalizationService.ResolveCharacterNameByCardPool(pool, pool.Title);
                options.Add(new GmFilterOption(pool.Title, displayName));
            }
        }

        // 无色固定排在最后
        options.Add(new GmFilterOption(ColorlessKey, GmUiText.CardCharacterColorless));
        return options;
    }

    /// <summary>
    /// 获取卡片类型筛选项。Key 用 CardType 的 enum 名稳定匹配。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetTypeOptions()
    {
        return [
            new GmFilterOption(AllOptionKey, GmUiText.CardTypeAll),
            new GmFilterOption(nameof(CardType.Attack), GmUiText.CardTypeAttack),
            new GmFilterOption(nameof(CardType.Skill), GmUiText.CardTypeSkill),
            new GmFilterOption(nameof(CardType.Power), GmUiText.CardTypePower),
            new GmFilterOption(nameof(CardType.Status), GmUiText.CardTypeStatus),
            new GmFilterOption(nameof(CardType.Curse), GmUiText.CardTypeCurse),
            new GmFilterOption(nameof(CardType.Quest), GmUiText.CardTypeQuest),
        ];
    }

    /// <summary>
    /// 获取稀有度筛选项。Key 用 CardRarity 的 enum 名稳定匹配。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetRarityOptions()
    {
        return [
            new GmFilterOption(AllOptionKey, GmUiText.Pick("全部稀有度", "All Rarities")),
            new GmFilterOption(nameof(CardRarity.Common), GmUiText.Pick("普通", "Common")),
            new GmFilterOption(nameof(CardRarity.Uncommon), GmUiText.Pick("罕见", "Uncommon")),
            new GmFilterOption(nameof(CardRarity.Rare), GmUiText.Pick("稀有", "Rare")),
            new GmFilterOption(nameof(CardRarity.Basic), GmUiText.Pick("基础", "Basic")),
            new GmFilterOption(nameof(CardRarity.Ancient), GmUiText.Pick("先古", "Ancient")),
            new GmFilterOption(nameof(CardRarity.Event), GmUiText.Pick("事件", "Event")),
            new GmFilterOption(nameof(CardRarity.Token), GmUiText.Pick("代币", "Token")),
        ];
    }

    /// <summary>
    /// 获取费用筛选项。Key 用 "0"/"1"/"2"/"3+"/"X" 字符串。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetCostOptions()
    {
        return [
            new GmFilterOption(AllOptionKey, GmUiText.CardCostAll),
            new GmFilterOption("0", "0"),
            new GmFilterOption("1", "1"),
            new GmFilterOption("2", "2"),
            new GmFilterOption(Cost3PlusKey, GmUiText.CardCost3Plus),
            new GmFilterOption(CostXKey, GmUiText.CardCostX),
        ];
    }

    /// <summary>
    /// 获取来源筛选项。Key 用 "BaseGame"/"Mods" 字符串。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetSourceOptions()
    {
        return [
            new GmFilterOption(AllOptionKey, GmUiText.Pick("全部来源", "All Sources")),
            new GmFilterOption(SourceBaseGameKey, GmUiText.Pick("游戏本体", "Base Game")),
            new GmFilterOption(SourceModsKey, "Mods"),
        ];
    }

    /// <summary>
    /// 获取当前局内玩家所属的卡池标题，作为「角色筛选」默认项。
    /// </summary>
    public string? GetCurrentCharacterKey()
    {
        Player? player = GetCurrentPlayer();
        return player?.Character?.CardPool?.Title;
    }

    /// <summary>
    /// 批量发放选中的卡片，按选择数量逐张发放。
    /// 战斗中：直接走 CardPileCmd.Add 双发到 Deck + Hand。
    /// 非战斗：upgraded=true 时直接走 CardPileCmd 路径（控制台命令不支持升级），否则优先控制台。
    /// </summary>
    /// <param name="selectedCards">用户选中的卡片列表（已经按数量展开）。</param>
    /// <param name="upgraded">是否发放升级版；true 时对每张可升级卡先 CreateCard 再 CardCmd.Upgrade。</param>
    public async Task<CardGrantResult> GrantCardsAsync(IReadOnlyList<CardDisplayInfo> selectedCards, bool upgraded)
    {
        CardGrantResult result = new();
        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            result.FailedCount++;
            result.Messages.Add(GmUiText.CardNotInRun);
            return result;
        }

        if (GmRunContextService.IsMultiplayer())
        {
            return await GrantCardsByNetworkActionAsync(player, selectedCards, upgraded);
        }

        bool inCombat = IsInCombat();

        foreach (CardDisplayInfo info in selectedCards)
        {
            bool success = inCombat
                ? await TryGrantInCombat(player, info, upgraded)
                : await TryGrantOutOfCombat(player, info, upgraded);

            if (success)
            {
                result.SuccessCount++;
                string template = inCombat ? GmUiText.CardGrantSuccessInCombatNamed : GmUiText.CardGrantSuccessNamed;
                result.Messages.Add(string.Format(template, info.Name));
            }
            else
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.CardGrantFailedNamed, info.Name));
            }
        }

        return result;
    }

    /// <summary>
    /// 多人模式下发卡只作用于本机玩家：统一走 GM 自定义同步动作，避免控制台文本命令变更导致伪成功。
    /// </summary>
    private static async Task<CardGrantResult> GrantCardsByNetworkActionAsync(Player player, IReadOnlyList<CardDisplayInfo> selectedCards, bool upgraded)
    {
        CardGrantResult result = new();
        int upgradeCount = upgraded ? 1 : 0;
        List<GmDeckCardGrantEntry> entries = selectedCards
            .GroupBy(info => info.IdText, StringComparer.OrdinalIgnoreCase)
            .Select(group => new GmDeckCardGrantEntry(group.Key, group.Count(), upgradeCount))
            .ToList();

        if (entries.Count == 0)
        {
            return result;
        }

        bool submitted = await GmNetworkCommandService.TryEnqueueGameActionAsync(new GmDeckCardGrantAction(player, entries, IsInCombat()), upgraded ? "升级版卡片发放" : "卡片发放");
        if (!submitted)
        {
            result.FailedCount += selectedCards.Count;
            foreach (CardDisplayInfo info in selectedCards)
            {
                result.Messages.Add(string.Format(GmUiText.CardGrantFailedNamed, info.Name));
            }

            return result;
        }

        result.SuccessCount += selectedCards.Count;
        foreach (CardDisplayInfo info in selectedCards)
        {
            result.Messages.Add(string.Format(GmUiText.CardGrantSubmittedNamed, info.Name));
        }

        MainFile.Logger.Info($"多人模式卡片发放已提交同步动作：升级={upgraded}，升级次数={upgradeCount}，选择数量={selectedCards.Count}，请求种类={entries.Count}，战斗中={IsInCombat()}。");
        return result;
    }

    /// <summary>
    /// 当前是否处于战斗中。
    /// </summary>
    public static bool IsInCombat()
    {
        return CombatManager.Instance?.DebugOnlyGetState() != null;
    }

    // ============================================================
    // 卡片删除页签：读取当前牌组、按筛选条件过滤、批量从牌组移除
    // ============================================================

    /// <summary>
    /// 「卡片删除」页签的合并筛选项 key：与游戏类型 enum 名保持一致 + 一个 "Colorless" 特殊 key。
    /// 这样一个下拉框即可覆盖「攻击/技能/能力/无色/状态/诅咒/任务」七类。
    /// </summary>
    public const string DeckCategoryColorlessKey = ColorlessKey;

    /// <summary>
    /// 获取「卡片删除」页签合并筛选项：全部 / 攻击 / 技能 / 能力 / 无色 / 状态 / 诅咒 / 任务。
    /// 顺序与「卡片自助」页签保持一致，便于老玩家肌肉记忆。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetDeckCategoryOptions()
    {
        return [
            new GmFilterOption(AllOptionKey, GmUiText.CardRemoveCategoryAll),
            new GmFilterOption(nameof(CardType.Attack), GmUiText.CardTypeAttack),
            new GmFilterOption(nameof(CardType.Skill), GmUiText.CardTypeSkill),
            new GmFilterOption(nameof(CardType.Power), GmUiText.CardTypePower),
            new GmFilterOption(DeckCategoryColorlessKey, GmUiText.CardCharacterColorless),
            new GmFilterOption(nameof(CardType.Status), GmUiText.CardTypeStatus),
            new GmFilterOption(nameof(CardType.Curse), GmUiText.CardTypeCurse),
            new GmFilterOption(nameof(CardType.Quest), GmUiText.CardTypeQuest),
        ];
    }

    /// <summary>
    /// 读取当前玩家牌组（run 级 player.Deck）的全部卡片，按关键字 + 合并类型筛选。
    /// 每张卡都构造独立的 CardDisplayInfo（5 张「打击」就有 5 个独立条目），
    /// 这样 UI 才能按实例选中/移除，不会出现合并去重。
    /// </summary>
    /// <param name="keyword">关键字，匹配卡名 + 描述。</param>
    /// <param name="categoryFilter">合并筛选：All / Attack / Skill / Power / Colorless / Status / Curse / Quest。</param>
    public IReadOnlyList<CardDisplayInfo> GetDeckCards(string keyword, string categoryFilter)
    {
        Player? player = GetCurrentPlayer();
        if (player?.Deck == null)
        {
            return [];
        }

        string normalizedKeyword = Normalize(keyword);
        List<CardDisplayInfo> result = [];
        foreach (CardModel card in player.Deck.Cards)
        {
            if (card == null)
            {
                continue;
            }

            CardDisplayInfo? info = TryBuildDeckDisplayInfo(card);
            if (info == null)
            {
                continue;
            }

            if (!MatchesKeyword(info, normalizedKeyword))
            {
                continue;
            }

            if (!MatchesDeckCategory(info, categoryFilter))
            {
                continue;
            }

            result.Add(info);
        }

        return result;
    }

    /// <summary>
    /// 删卡页排序后的牌组列表：先按类型，再按稀有度，再按费用，最后按卡名稳定排序。
    /// 直接复用 GetDeckCards 拿筛选后结果，再走与图鉴一致的排序键。
    /// </summary>
    public IReadOnlyList<CardDisplayInfo> GetDeckCardsSorted(string keyword, string categoryFilter)
    {
        return GetDeckCards(keyword, categoryFilter)
            .OrderBy(info => GetTypeOrder(info.TypeKey))
            .ThenBy(info => GetRarityOrder(info.RarityKey))
            .ThenBy(info => GetCostOrder(info.Cost, info.CostsX))
            .ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 批量从玩家牌组移除选中的卡片。
    /// 战斗中：仅修改 run 级 player.Deck，不动当前战斗的 Hand/Draw/Discard/Exhaust pile，
    ///         避免误碰已被使用 / 消耗的卡导致状态崩坏。
    /// 非战斗中：直接调 RemoveFromCurrentPile（其内部走 Pile.RemoveInternal）。
    /// 删除成功后手动触发一次 CardAddFinished 事件，用于刷新顶栏牌组数字（与发卡逻辑同因）。
    /// </summary>
    public async Task<CardGrantResult> RemoveDeckCardsAsync(IReadOnlyList<CardDisplayInfo> selectedCards)
    {
        CardGrantResult result = new();
        Player? player = GetCurrentPlayer();
        if (player?.Deck == null)
        {
            result.FailedCount += selectedCards.Count;
            result.Messages.Add(GmUiText.CardRemoveNotInRun);
            return result;
        }

        if (GmRunContextService.IsMultiplayer())
        {
            return await RemoveDeckCardsByNetworkActionAsync(player, selectedCards);
        }

        bool anyRemoved = false;
        foreach (CardDisplayInfo info in selectedCards)
        {
            string displayName = string.IsNullOrEmpty(info.Name) ? info.IdText : info.Name;
            bool ok = await TryRemoveFromDeck(player, info);
            if (ok)
            {
                anyRemoved = true;
                result.SuccessCount++;
                result.Messages.Add(string.Format(GmUiText.CardRemoveSuccessNamed, displayName));
            }
            else
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.CardRemoveFailedNamed, displayName));
            }
        }

        if (anyRemoved)
        {
            // 顶栏 NTopBarDeckButton 仅订阅 CardPile.CardAddFinished 事件来刷新数字；
            // RemoveFromCurrentPile 不会发这个事件，这里手动补一次让数字立刻刷新。
            TryInvokeCardAddFinished(PileType.Deck.GetPile(player));
        }

        return result;
    }

    /// <summary>
    /// 多人模式下把牌组删卡转换成同步网络动作；只提交牌组下标，不再因重复同 ID 卡而拒绝。
    /// </summary>
    /// <param name="player">当前本机玩家。</param>
    /// <param name="selectedCards">用户选中的牌组卡片。</param>
    private static async Task<CardGrantResult> RemoveDeckCardsByNetworkActionAsync(Player player, IReadOnlyList<CardDisplayInfo> selectedCards)
    {
        CardGrantResult result = new();
        CardPile playerDeck = PileType.Deck.GetPile(player);
        List<uint> deckIndexes = [];
        List<CardDisplayInfo> submittedCards = [];
        HashSet<int> selectedDeckIndexes = [];

        foreach (CardDisplayInfo info in selectedCards)
        {
            TryCollectDeckCardRemoveIndex(player, playerDeck, info, selectedDeckIndexes, deckIndexes, submittedCards, result);
        }

        if (deckIndexes.Count == 0)
        {
            return result;
        }

        bool submitted = await GmNetworkCommandService.TryEnqueueGameActionAsync(new GmDeckCardRemoveAction(player, deckIndexes, IsInCombat()), "卡片删除");
        if (!submitted)
        {
            result.FailedCount += deckIndexes.Count;
            foreach (CardDisplayInfo info in submittedCards)
            {
                string displayName = string.IsNullOrEmpty(info.Name) ? info.IdText : info.Name;
                result.Messages.Add(string.Format(GmUiText.CardRemoveFailedNamed, displayName));
            }

            return result;
        }

        result.SuccessCount += deckIndexes.Count;
        foreach (CardDisplayInfo info in submittedCards)
        {
            string displayName = string.IsNullOrEmpty(info.Name) ? info.IdText : info.Name;
            result.Messages.Add(string.Format(GmUiText.CardRemoveSubmittedNamed, displayName));
        }

        MainFile.Logger.Info($"多人模式卡片删除已提交同步动作：选择数量={selectedCards.Count}，提交数量={deckIndexes.Count}，战斗中={IsInCombat()}，下标=[{string.Join(",", deckIndexes)}]。");
        return result;
    }

    /// <summary>
    /// 把一张 UI 选中的卡转换成当前牌组下标，并完成删卡入队前的安全检查。
    /// </summary>
    private static void TryCollectDeckCardRemoveIndex(
        Player player,
        CardPile playerDeck,
        CardDisplayInfo info,
        HashSet<int> selectedDeckIndexes,
        List<uint> deckIndexes,
        List<CardDisplayInfo> submittedCards,
        CardGrantResult result)
    {
        string displayName = string.IsNullOrEmpty(info.Name) ? info.IdText : info.Name;
        try
        {
            CardModel card = info.Card;
            CardPile? currentPile = card.Pile;
            if (currentPile == null || currentPile.Type != PileType.Deck)
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.CardRemoveFailedNamed, displayName));
                MainFile.Logger.Info($"多人卡片删除提交失败：卡片不在 Deck 里 / Card not in Deck pile: {info.IdText} (currentPile={currentPile?.Type.ToString() ?? "null"})");
                return;
            }

            if (currentPile != playerDeck)
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.CardRemoveFailedNamed, displayName));
                MainFile.Logger.Info($"多人卡片删除提交失败：所在 pile 不是当前玩家 Deck / Card pile is not the current player's Deck: {info.IdText}，玩家NetId={player.NetId}。");
                return;
            }

            int deckIndex = FindDeckCardIndex(playerDeck, card);
            if (deckIndex < 0)
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.CardRemoveFailedNamed, displayName));
                MainFile.Logger.Info($"多人卡片删除提交失败：无法定位牌组下标 / Deck index not found: {info.IdText}，玩家NetId={player.NetId}。");
                return;
            }

            if (!selectedDeckIndexes.Add(deckIndex))
            {
                result.SkippedCount++;
                result.Messages.Add(string.Format(GmUiText.CardRemoveSkippedNamed, displayName));
                MainFile.Logger.Info($"多人卡片删除提交跳过重复下标：card={info.IdText}，deckIndex={deckIndex}，玩家NetId={player.NetId}。");
                return;
            }

            deckIndexes.Add((uint)deckIndex);
            submittedCards.Add(info);
        }
        catch (Exception ex)
        {
            result.FailedCount++;
            result.Messages.Add(string.Format(GmUiText.CardRemoveFailedNamed, displayName));
            MainFile.Logger.Info($"多人卡片删除提交异常 / Card remove submit exception: {info.IdText} / {ex.Message}");
        }
    }

    /// <summary>
    /// 找出多人模式下无法安全按 ID 删除的重复卡 ID。
    /// </summary>
    private static HashSet<string> GetAmbiguousDeckCardIds(Player player)
    {
        return player.Deck.Cards
            .Where(card => card != null)
            .GroupBy(card => card.Id.ToString(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 真正执行单张卡的牌组移除：
    /// - 必须 AssertMutable，所以传进来的必须是 player.Deck 里的实例（这本来就是）。
    /// - 战斗中只动 run-scope 的 Deck pile：CardModel.Pile 实例的 Type 为 PileType.Deck 时才动；
    ///   不是的（异常情况，比如 caller 错传组合卡）直接拒绝并打日志。
    /// </summary>
    private static async Task<bool> TryRemoveFromDeck(Player player, CardDisplayInfo info)
    {
        try
        {
            CardModel card = info.Card;
            CardPile? currentPile = card.Pile;
            // 牌不在牌组里：可能已被其他逻辑移走或者传入了 canonical 实例
            if (currentPile == null || currentPile.Type != PileType.Deck)
            {
                MainFile.Logger.Info($"卡片删除拒绝：卡片不在 Deck 里 / Card not in Deck pile: {info.IdText} (currentPile={currentPile?.Type.ToString() ?? "null"})");
                return false;
            }

            // 二次校验确保 pile 是当前玩家的 Deck，避免误删别处共享的 pile
            CardPile playerDeck = PileType.Deck.GetPile(player);
            if (currentPile != playerDeck)
            {
                MainFile.Logger.Info($"卡片删除拒绝：所在 pile 不是当前玩家 Deck / Card pile is not the current player's Deck: {info.IdText}");
                return false;
            }

            // 原生命令同时处理删牌触发器、历史记录和已移除状态标记。
            await CardPileCmd.RemoveFromDeck(card, showPreview: false);
            MainFile.Logger.Info($"卡片已从牌组移除 / Card removed from deck: {info.IdText}");
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"卡片移除失败 / Card remove failed: {info.IdText} / {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 给 deck 卡构造展示数据：使用真实的当前升级等级生成名字后缀（"+1" / "+2"）。
    /// </summary>
    private static CardDisplayInfo? TryBuildDeckDisplayInfo(CardModel card)
    {
        string suffix = card.CurrentUpgradeLevel > 0 ? $"+{card.CurrentUpgradeLevel}" : "";
        return TryBuildDisplayInfo(card, showUpgraded: false, nameSuffixOverride: suffix);
    }

    // ============================================================
    // 卡片强化页签：列出可升级卡片、批量升级
    // ============================================================

    /// <summary>
    /// 读取当前牌组中可升级且尚未到升级上限的卡片，按关键字 + 合并类型筛选。
    /// 已升级到顶（CurrentUpgradeLevel == MaxUpgradeLevel）和本身不可升级（IsUpgradable=false）的卡都过滤掉。
    /// </summary>
    /// <param name="keyword">关键字，匹配卡名 + 描述。</param>
    /// <param name="categoryFilter">合并筛选：All / Attack / Skill / Power / Colorless / Status / Curse / Quest。</param>
    public IReadOnlyList<CardDisplayInfo> GetUpgradableDeckCards(string keyword, string categoryFilter)
    {
        return GetDeckCards(keyword, categoryFilter)
            .Where(info => info.Card.IsUpgradable && info.Card.CurrentUpgradeLevel < info.Card.MaxUpgradeLevel)
            .ToList();
    }

    /// <summary>
    /// GetUpgradableDeckCards 的排序版本，与删卡页保持一致的排序口径：
    /// 类型 → 稀有度 → 费用 → 卡名稳定排序。
    /// </summary>
    public IReadOnlyList<CardDisplayInfo> GetUpgradableDeckCardsSorted(string keyword, string categoryFilter)
    {
        return GetUpgradableDeckCards(keyword, categoryFilter)
            .OrderBy(info => GetTypeOrder(info.TypeKey))
            .ThenBy(info => GetRarityOrder(info.RarityKey))
            .ThenBy(info => GetCostOrder(info.Cost, info.CostsX))
            .ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 批量升级玩家牌组里的选中卡片。
    /// 战斗中：仅升级 run 级 player.Deck 中的实例，不动 hand/draw/discard/exhaust pile，
    ///         避免修改当前战斗正在使用的卡导致状态崩坏（与 RemoveDeckCardsAsync 同一安全模型）。
    /// 非战斗中：行为相同（CardCmd.Upgrade 本身只改 CardModel 字段，不会接触战斗 pile）。
    /// </summary>
    public async Task<CardGrantResult> UpgradeDeckCardsAsync(IReadOnlyList<CardDisplayInfo> selectedCards)
    {
        CardGrantResult result = new();
        GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(GmFeature.UpgradeCard);
        if (!availability.IsAllowed)
        {
            result.FailedCount += selectedCards.Count;
            result.Messages.Add(availability.Reason);
            MainFile.Logger.Info($"多人模式卡片强化已被安全闸门阻止：选择数量={selectedCards.Count}，原因={availability.Reason}。");
            return result;
        }

        Player? player = GetCurrentPlayer();
        if (player?.Deck == null)
        {
            result.FailedCount += selectedCards.Count;
            result.Messages.Add(GmUiText.CardUpgradeNotInRun);
            return result;
        }

        if (GmRunContextService.IsMultiplayer())
        {
            return await UpgradeDeckCardsByNetworkActionAsync(player, selectedCards);
        }

        foreach (CardDisplayInfo info in selectedCards)
        {
            TryUpgradeOneDeckCard(player, info, result);
        }

        return result;
    }

    /// <summary>
    /// 多人模式下把牌组强化转换成同步网络动作；只提交牌组下标，不直接改本机牌组。
    /// </summary>
    /// <param name="player">当前本机玩家。</param>
    /// <param name="selectedCards">用户选中的牌组卡片。</param>
    private static async Task<CardGrantResult> UpgradeDeckCardsByNetworkActionAsync(Player player, IReadOnlyList<CardDisplayInfo> selectedCards)
    {
        CardGrantResult result = new();
        CardPile playerDeck = PileType.Deck.GetPile(player);
        List<uint> deckIndexes = [];
        List<CardDisplayInfo> submittedCards = [];
        HashSet<int> selectedDeckIndexes = [];

        foreach (CardDisplayInfo info in selectedCards)
        {
            TryCollectDeckCardUpgradeIndex(player, playerDeck, info, selectedDeckIndexes, deckIndexes, submittedCards, result);
        }

        if (deckIndexes.Count == 0)
        {
            return result;
        }

        bool submitted = await GmNetworkCommandService.TryEnqueueGameActionAsync(new GmDeckCardUpgradeAction(player, deckIndexes), "卡片强化");
        if (!submitted)
        {
            result.FailedCount += deckIndexes.Count;
            foreach (CardDisplayInfo info in submittedCards)
            {
                string displayName = string.IsNullOrEmpty(info.Name) ? info.IdText : info.Name;
                result.Messages.Add(string.Format(GmUiText.CardUpgradeFailedNamed, displayName));
            }

            return result;
        }

        result.SuccessCount += deckIndexes.Count;
        foreach (CardDisplayInfo info in submittedCards)
        {
            string displayName = string.IsNullOrEmpty(info.Name) ? info.IdText : info.Name;
            result.Messages.Add(string.Format(GmUiText.CardUpgradeSubmittedNamed, displayName));
        }

        MainFile.Logger.Info($"多人模式卡片强化已提交同步动作：选择数量={selectedCards.Count}，提交数量={deckIndexes.Count}，下标=[{string.Join(",", deckIndexes)}]。");
        return result;
    }

    /// <summary>
    /// 把一张 UI 选中的卡转换成当前牌组下标，并完成入队前的安全检查。
    /// </summary>
    private static void TryCollectDeckCardUpgradeIndex(
        Player player,
        CardPile playerDeck,
        CardDisplayInfo info,
        HashSet<int> selectedDeckIndexes,
        List<uint> deckIndexes,
        List<CardDisplayInfo> submittedCards,
        CardGrantResult result)
    {
        string displayName = string.IsNullOrEmpty(info.Name) ? info.IdText : info.Name;
        try
        {
            CardModel card = info.Card;
            CardPile? currentPile = card.Pile;
            if (currentPile == null || currentPile.Type != PileType.Deck)
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.CardUpgradeFailedNamed, displayName));
                MainFile.Logger.Info($"多人卡片强化提交失败：卡片不在 Deck 里 / Card not in Deck pile: {info.IdText} (currentPile={currentPile?.Type.ToString() ?? "null"})");
                return;
            }

            if (currentPile != playerDeck)
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.CardUpgradeFailedNamed, displayName));
                MainFile.Logger.Info($"多人卡片强化提交失败：所在 pile 不是当前玩家 Deck / Card pile is not the current player's Deck: {info.IdText}，玩家NetId={player.NetId}。");
                return;
            }

            int deckIndex = FindDeckCardIndex(playerDeck, card);
            if (deckIndex < 0)
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.CardUpgradeFailedNamed, displayName));
                MainFile.Logger.Info($"多人卡片强化提交失败：无法定位牌组下标 / Deck index not found: {info.IdText}，玩家NetId={player.NetId}。");
                return;
            }

            if (!selectedDeckIndexes.Add(deckIndex))
            {
                result.SkippedCount++;
                result.Messages.Add(string.Format(GmUiText.CardUpgradeSkippedNamed, displayName));
                MainFile.Logger.Info($"多人卡片强化提交跳过重复下标：card={info.IdText}，deckIndex={deckIndex}，玩家NetId={player.NetId}。");
                return;
            }

            if (!card.IsUpgradable || card.CurrentUpgradeLevel >= card.MaxUpgradeLevel)
            {
                result.SkippedCount++;
                result.Messages.Add(string.Format(GmUiText.CardUpgradeSkippedNamed, displayName));
                MainFile.Logger.Info($"多人卡片强化提交跳过：卡片不可升级或已满级 / Card not upgradable or max-level: {info.IdText} (Upgradable={card.IsUpgradable}, Level={card.CurrentUpgradeLevel}/{card.MaxUpgradeLevel})");
                return;
            }

            deckIndexes.Add((uint)deckIndex);
            submittedCards.Add(info);
        }
        catch (Exception ex)
        {
            result.FailedCount++;
            result.Messages.Add(string.Format(GmUiText.CardUpgradeFailedNamed, displayName));
            MainFile.Logger.Info($"多人卡片强化提交异常 / Card upgrade submit exception: {info.IdText} / {ex.Message}");
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
    /// 真正执行单张牌组卡片的升级：
    /// 1) 二次校验卡仍在当前玩家 Deck 里；
    /// 2) 二次校验可升级且未满级；
    /// 3) 调用游戏自带 CardCmd.Upgrade。
    /// 任何环节失败都计入 result，不抛出，避免一张卡的异常影响整批。
    /// </summary>
    private static void TryUpgradeOneDeckCard(Player player, CardDisplayInfo info, CardGrantResult result)
    {
        string displayName = string.IsNullOrEmpty(info.Name) ? info.IdText : info.Name;
        try
        {
            CardModel card = info.Card;
            CardPile? currentPile = card.Pile;
            if (currentPile == null || currentPile.Type != PileType.Deck)
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.CardUpgradeFailedNamed, displayName));
                MainFile.Logger.Info($"卡片强化失败：卡片不在 Deck 里 / Card not in Deck pile: {info.IdText} (currentPile={currentPile?.Type.ToString() ?? "null"})");
                return;
            }

            CardPile playerDeck = PileType.Deck.GetPile(player);
            if (currentPile != playerDeck)
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.CardUpgradeFailedNamed, displayName));
                MainFile.Logger.Info($"卡片强化失败：所在 pile 不是当前玩家 Deck / Card pile is not the current player's Deck: {info.IdText}");
                return;
            }

            if (!card.IsUpgradable || card.CurrentUpgradeLevel >= card.MaxUpgradeLevel)
            {
                // 列表已过滤过；此处仍兜底校验，避免在战斗中手牌实例与牌组实例并存导致误判
                result.SkippedCount++;
                result.Messages.Add(string.Format(GmUiText.CardUpgradeSkippedNamed, displayName));
                MainFile.Logger.Info($"卡片强化跳过：卡片不可升级或已满级 / Card not upgradable or max-level: {info.IdText} (Upgradable={card.IsUpgradable}, Level={card.CurrentUpgradeLevel}/{card.MaxUpgradeLevel})");
                return;
            }

            CardCmd.Upgrade(card, CardPreviewStyle.None);
            result.SuccessCount++;
            result.Messages.Add(string.Format(GmUiText.CardUpgradeSuccessNamed, displayName));
            MainFile.Logger.Info($"卡片强化成功 / Card upgraded: {info.IdText} (Level={card.CurrentUpgradeLevel}/{card.MaxUpgradeLevel})");
        }
        catch (Exception ex)
        {
            result.FailedCount++;
            result.Messages.Add(string.Format(GmUiText.CardUpgradeFailedNamed, displayName));
            MainFile.Logger.Info($"卡片强化异常 / Card upgrade exception: {info.IdText} / {ex.Message}");
        }
    }

    /// <summary>
    /// 删卡页合并类型筛选匹配：
    /// - All / 空：全部通过
    /// - "Colorless"：按 IsColorless 匹配（不限类型）
    /// - 类型枚举名（Attack/Skill/Power/Status/Curse/Quest）：按 TypeKey 匹配
    /// </summary>
    private static bool MatchesDeckCategory(CardDisplayInfo info, string filter)
    {
        if (string.IsNullOrEmpty(filter) || string.Equals(filter, AllOptionKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(filter, DeckCategoryColorlessKey, StringComparison.Ordinal))
        {
            return info.IsColorless;
        }

        return string.Equals(info.TypeKey, filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 战斗中：把这张卡的两份独立实例分别加到牌组和手牌。
    /// 关键：必须用 ICardScope.CreateCard(canonical, player) 创建带 owner 的卡，
    ///       直接 ToMutable() 出来的卡 owner=null，CardPileCmd.Add 会拒收并报
    ///       "Attempted to add card ... has no owner"。
    ///       Deck 走 Run 域（非战斗 pile），Hand 走 Combat 域（战斗 pile）。
    ///       upgraded=true 时对每张可升级卡先调 CardCmd.Upgrade 再塞 pile。
    /// </summary>
    private static async Task<bool> TryGrantInCombat(Player player, CardDisplayInfo info, bool upgraded)
    {
        try
        {
            ICardScope? runScope = RunManager.Instance?.DebugOnlyGetState();
            ICardScope? combatScope = CombatManager.Instance?.DebugOnlyGetState();
            if (runScope == null || combatScope == null)
            {
                MainFile.Logger.Info($"战斗中发卡失败：无法获取卡片作用域 / Card scope unavailable: {info.IdText}");
                return false;
            }

            CardModel deckCard = runScope.CreateCard(info.Card, player);
            TryUpgrade(deckCard, upgraded, info.IdText);
            CardPileAddResult deckResult = await CardPileCmd.Add(
                deckCard,
                PileType.Deck,
                CardPilePosition.Top,
                null,
                false);
            // CardPileCmd.Add 对 brand-new 卡（oldPile=null）走的是无 cardNode 分支，
            // 不会创建 NCardFlyVfx/NCardFlyShuffleVfx，因此 CardPile.CardAddFinished 事件不会被触发，
            // 顶栏 NTopBarDeckButton 的牌组计数也就不会刷新。这里手动补一次事件，模拟 VFX 收尾。
            NotifyPileAddFinished(deckResult, deckCard, PileType.Deck, player);

            CardModel handCard = combatScope.CreateCard(info.Card, player);
            TryUpgrade(handCard, upgraded, info.IdText);
            CardPileAddResult handResult = await CardPileCmd.Add(
                handCard,
                PileType.Hand,
                CardPilePosition.Top,
                null,
                false);
            NotifyPileAddFinished(handResult, handCard, PileType.Hand, player);

            return deckResult.success && handResult.success;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"战斗中发卡失败 / Grant card in combat failed: {info.IdText} / {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 在 CardPileCmd.Add 成功后显式触发 CardAddFinished 事件。
    /// 必要性：CardPileCmd.Add 对「brand-new 卡 + 非战斗 Deck」分支不会创建 NCardFlyVfx，
    /// 因此原本由 VFX 收尾兜底调用的 CardPile.InvokeCardAddFinished 不会被触发。
    /// 而顶栏 NTopBarDeckButton 仅订阅 CardPile.CardAddFinished 来刷新数字，导致发卡后
    /// 数字不更新，必须 SL 才正确。这里在 add 成功后手动补一次事件，模拟 VFX 完成。
    /// </summary>
    private static void NotifyPileAddFinished(CardPileAddResult addResult, CardModel card, PileType pileType, Player player)
    {
        if (!addResult.success)
        {
            return;
        }

        // 优先用 card.Pile（add 成功后应已绑定到目标 pile），失败时回退按类型查询。
        CardPile? pile = card?.Pile ?? pileType.GetPile(player);
        TryInvokeCardAddFinished(pile);
    }

    /// <summary>
    /// 安全调用 CardPile.InvokeCardAddFinished：pile 为空或事件触发抛异常都不影响主发卡流程。
    /// </summary>
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
            MainFile.Logger.Info($"触发 CardAddFinished 事件失败（不影响实际发卡）/ Failed to invoke CardAddFinished: {ex.Message}");
        }
    }

    /// <summary>
    /// 当用户勾选「查看升级」时，对刚 CreateCard 出来的卡走一次 CardCmd.Upgrade。
    /// 不可升级 / 已满级的卡静默跳过；任何异常吞掉只记日志，不影响发卡主流程。
    /// </summary>
    private static void TryUpgrade(CardModel card, bool upgraded, string idText)
    {
        if (!upgraded || card == null || !card.IsUpgradable)
        {
            return;
        }

        try
        {
            CardCmd.Upgrade(card, CardPreviewStyle.None);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"卡片升级失败（仍按基础版发放）/ Card upgrade failed: {idText} / {ex.Message}");
        }
    }

    /// <summary>
    /// 非战斗中：upgraded=true 时直接走 CardPileCmd（控制台 card 命令不支持升级），
    /// 否则保持原优先级：先 CardConsoleCmd，失败兜底 CardPileCmd.Add。
    /// </summary>
    private async Task<bool> TryGrantOutOfCombat(Player player, CardDisplayInfo info, bool upgraded)
    {
        if (upgraded)
        {
            return await TryGrantByCardPileCmd(player, info, upgraded: true);
        }

        if (await TryGrantByConsoleCommand(player, info))
        {
            return true;
        }

        return await TryGrantByCardPileCmd(player, info, upgraded: false);
    }

    /// <summary>
    /// 用控制台 card 命令发卡，复用游戏自带逻辑。
    /// 显式传 "Deck" 第二参数：游戏默认是 Hand，非战斗下 Hand pile 不存在会抛 NRE，
    /// 这里直接走 Deck 让 RunScope 创建有 owner 的卡，落到牌组。
    /// </summary>
    private async Task<bool> TryGrantByConsoleCommand(Player player, CardDisplayInfo info)
    {
        try
        {
            int beforeCount = player.Deck?.Cards.Count ?? 0;
            CmdResult cmdResult = _cardConsoleCmd.Process(player, [info.IdText, "Deck"]);
            if (cmdResult.task != null)
            {
                await cmdResult.task;
            }

            int afterCount = player.Deck?.Cards.Count ?? 0;
            bool ok = cmdResult.success && afterCount > beforeCount;
            // 控制台 card 命令内部同样调 CardPileCmd.Add，brand-new 卡走的是无 VFX 分支，
            // 不触发 CardAddFinished。这里在确实加成功后补一次事件，让顶栏牌组数字立刻刷新。
            if (ok)
            {
                TryInvokeCardAddFinished(PileType.Deck.GetPile(player));
            }
            return ok;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"控制台发卡失败，改用 CardPileCmd 兜底 / Console card grant failed: {info.IdText} / {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 用 CardPileCmd.Add 把卡加到玩家牌组顶部（兜底方案）。
    /// 同样必须先经 ICardScope.CreateCard 绑 owner，否则会被拒收。
    /// upgraded=true 时在 Add 之前调一次 CardCmd.Upgrade。
    /// </summary>
    private static async Task<bool> TryGrantByCardPileCmd(Player player, CardDisplayInfo info, bool upgraded)
    {
        try
        {
            ICardScope? runScope = RunManager.Instance?.DebugOnlyGetState();
            if (runScope == null)
            {
                MainFile.Logger.Info($"CardPileCmd 发卡失败：无法获取 RunScope / Run scope unavailable: {info.IdText}");
                return false;
            }

            CardModel card = runScope.CreateCard(info.Card, player);
            TryUpgrade(card, upgraded, info.IdText);
            CardPileAddResult addResult = await CardPileCmd.Add(
                card,
                PileType.Deck,
                CardPilePosition.Top,
                null,
                false);
            // 同 TryGrantInCombat 中的注释：补一次 CardAddFinished 事件让顶栏牌组数字刷新
            NotifyPileAddFinished(addResult, card, PileType.Deck, player);
            return addResult.success;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"CardPileCmd 发卡失败 / CardPileCmd grant failed: {info.IdText} / {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 获取当前玩家；没有局内状态时返回空。
    /// </summary>
    private static Player? GetCurrentPlayer()
    {
        return GmRunContextService.GetCurrentPlayer();
    }

    /// <summary>
    /// 把游戏卡片模型转换成展示数据；任何字段抛异常都视为这张卡不可用，返回 null 跳过。
    /// 比如 Mad Science 这种 Type==None 的特殊卡，访问 Portrait 会 InvalidOperationException。
    /// </summary>
    private static CardDisplayInfo? TryBuildDisplayInfo(CardModel card, bool showUpgraded, string? nameSuffixOverride = null)
    {
        try
        {
            return BuildDisplayInfo(card, showUpgraded, nameSuffixOverride);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"卡片解析失败，已跳过 / Card build failed and skipped: {card.Id} / {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 把游戏卡片模型转换成展示数据。
    /// 名字后缀优先级：
    /// 1) nameSuffixOverride 非空：直接拼到 baseName 后；用于「卡片删除」页根据 CurrentUpgradeLevel 显示 "+1"。
    /// 2) showUpgraded == true 且卡可升级：方案 A 在卡名加 "+1"；用于「卡片自助」页的预览升级模式。
    /// 3) 否则：使用原始 baseName。
    /// </summary>
    private static CardDisplayInfo BuildDisplayInfo(CardModel card, bool showUpgraded, string? nameSuffixOverride = null)
    {
        // 跳过 Type==None 的废卡（如 CARD.MAD_SCIENCE）：
        // 这种卡的描述模板用 {CardType:choose(Attack|Skill|Power):...} 占位，
        // CardType=None 不在选择列表内会让 SmartFormat 抛 "is not a valid choice" 异常；
        // 同时 NCard.UpdateTypePlaque 也会因 CardTypeExtensions.ToLocString(None) 抛 ArgumentOutOfRangeException。
        // 这种卡渲染到 NCard 必崩，没有任何让用户在 GM 里发出去的意义，直接当成无效卡跳过。
        if (card.Type == CardType.None)
        {
            throw new InvalidOperationException($"Card {card.Id} has invalid Type=None, skipped to avoid NCard render exception.");
        }

        string idText = card.Id.ToString();
        string baseName = SafeText(GetLocalizedText(card.TitleLocString) ?? card.Title, idText);
        bool canUpgrade = card.IsUpgradable && card.CurrentUpgradeLevel < card.MaxUpgradeLevel;
        bool isMultiplayerOnly = IsMultiplayerOnlyConstraint(card.MultiplayerConstraint);

        string displayName;
        if (!string.IsNullOrEmpty(nameSuffixOverride))
        {
            // 删卡页走这里：直接按 nameSuffixOverride 拼接，体现卡片真实升级状态。
            displayName = $"{baseName}{nameSuffixOverride}";
        }
        else
        {
            displayName = (showUpgraded && canUpgrade) ? $"{baseName}+1" : baseName;
        }

        bool isColorless = card.Pool?.IsColorless ?? true;
        string poolTitle = card.Pool?.Title ?? "";
        string characterKey = isColorless ? ColorlessKey : poolTitle;
        // 展示用本地化职业名（中文环境显示「故障机器人」，英文环境显示「Defect」）
        string characterDisplay = isColorless
            ? GmUiText.CardCharacterColorless
            : GmLocalizationService.ResolveCharacterNameByCardPool(card.Pool, poolTitle);

        int cost;
        bool costsX = card.EnergyCost?.CostsX ?? false;
        if (costsX)
        {
            cost = CostXSentinel;
        }
        else
        {
            cost = card.EnergyCost?.Canonical ?? 0;
        }

        return new CardDisplayInfo
        {
            Id = card.Id,
            IdText = idText,
            Name = displayName,
            Description = SafeText(GetCardDescription(card.Description), GmUiText.NoDescription),
            TypeKey = card.Type.ToString(),
            Type = ToTypeText(card.Type),
            RarityKey = card.Rarity.ToString(),
            Rarity = ToRarityText(card.Rarity),
            Cost = cost,
            CostsX = costsX,
            CostText = costsX ? "X" : cost.ToString(),
            CharacterKey = characterKey,
            Character = characterDisplay,
            IsColorless = isColorless,
            IsMultiplayerOnly = isMultiplayerOnly,
            CanUpgrade = canUpgrade,
            SourceKey = ToSourceKey(card.GetType().Assembly.GetName().Name),
            Source = ToSourceText(card.GetType().Assembly.GetName().Name),
            Icon = TryReadPortrait(card),
            Card = card,
        };
    }

    /// <summary>
    /// 安全读取卡片立绘：某些特殊卡（如 Mad Science）类型为 None 时调 Portrait 会抛 InvalidOperationException，
    /// 失败时返回 null，避免一张坏卡卡死整个网格构建流程。
    /// </summary>
    private static Texture2D? TryReadPortrait(CardModel card)
    {
        try
        {
            return card.Portrait;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"读取卡片立绘失败，已用 null 兜底 / Failed to read card portrait: {card.Id} / {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 关键字匹配卡名 + 描述（区分大小写无关）。
    /// </summary>
    private static bool MatchesKeyword(CardDisplayInfo info, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return Normalize(info.Name).Contains(keyword) ||
               Normalize(info.Description).Contains(keyword);
    }

    /// <summary>
    /// 角色筛选：「All」直接通过；「Colorless」匹配 IsColorless；其他职业匹配卡池 Title。
    /// </summary>
    private static bool MatchesCharacter(CardDisplayInfo info, string filter)
    {
        if (string.IsNullOrEmpty(filter) || string.Equals(filter, AllOptionKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(filter, ColorlessKey, StringComparison.Ordinal))
        {
            return info.IsColorless;
        }

        // 选具体职业时排除无色卡
        if (info.IsColorless)
        {
            return false;
        }

        return string.Equals(info.CharacterKey, filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 类型筛选：按 enum 名匹配（CardType.Attack 的 key 就是 "Attack"）。
    /// </summary>
    private static bool MatchesType(CardDisplayInfo info, string filter)
    {
        if (string.IsNullOrEmpty(filter) || string.Equals(filter, AllOptionKey, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(info.TypeKey, filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 稀有度筛选：按 enum 名匹配。
    /// </summary>
    private static bool MatchesRarity(CardDisplayInfo info, string filter)
    {
        if (string.IsNullOrEmpty(filter) || string.Equals(filter, AllOptionKey, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(info.RarityKey, filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 费用筛选：0/1/2 直接比；3+ 表示 >=3；X 单独匹配。
    /// </summary>
    private static bool MatchesCost(CardDisplayInfo info, string filter)
    {
        if (string.IsNullOrEmpty(filter) || string.Equals(filter, AllOptionKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(filter, CostXKey, StringComparison.OrdinalIgnoreCase))
        {
            return info.CostsX;
        }

        if (string.Equals(filter, Cost3PlusKey, StringComparison.OrdinalIgnoreCase))
        {
            return !info.CostsX && info.Cost >= 3;
        }

        if (int.TryParse(filter, out int cost))
        {
            return !info.CostsX && info.Cost == cost;
        }

        return true;
    }

    /// <summary>
    /// 来源筛选：BaseGame 与本体匹配；Mods 表示非本体。
    /// </summary>
    private static bool MatchesSource(CardDisplayInfo info, string filter)
    {
        if (string.IsNullOrEmpty(filter) || string.Equals(filter, AllOptionKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(filter, SourceBaseGameKey, StringComparison.Ordinal))
        {
            return string.Equals(info.SourceKey, SourceBaseGameKey, StringComparison.Ordinal);
        }

        if (string.Equals(filter, SourceModsKey, StringComparison.Ordinal))
        {
            return !string.Equals(info.SourceKey, SourceBaseGameKey, StringComparison.Ordinal);
        }

        return string.Equals(info.SourceKey, filter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 多人模式开关：未勾选则隐藏 MultiplayerOnly 的卡。
    /// </summary>
    private static bool MatchesMultiplayer(CardDisplayInfo info, bool includeMultiplayer)
    {
        return includeMultiplayer || !info.IsMultiplayerOnly;
    }

    /// <summary>
    /// 判断卡牌约束是否属于多人模式专属。
    /// </summary>
    private static bool IsMultiplayerOnlyConstraint(CardMultiplayerConstraint constraint)
    {
        if (constraint == CardMultiplayerConstraint.None)
        {
            return false;
        }

        string constraintName = constraint.ToString();
        return constraintName.Contains("Multiplayer", StringComparison.OrdinalIgnoreCase) &&
               !constraintName.Contains("Single", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 获取卡牌伤害排序值；优先解析游戏格式化后的描述文本，读不到就按 0 排。
    /// </summary>
    public int GetCardDamageSortValue(CardDisplayInfo info)
    {
        string formattedDescription = GetFormattedCardDescriptionForDamage(info);
        if (TryParseDamageFromText(formattedDescription, out int formattedDamage))
        {
            return formattedDamage;
        }

        if (TryParseDamageFromText(info.Description, out int displayDamage))
        {
            return displayDamage;
        }

        string[] propertyNames = ["Damage", "BaseDamage", "AttackDamage", "CardDamage"];
        foreach (string propertyName in propertyNames)
        {
            if (TryReadIntProperty(info.Card, propertyName, out int value))
            {
                return value;
            }
        }

        return 0;
    }

    /// <summary>
    /// 获取用于伤害排序的游戏格式化描述，避免只看到原始模板里的动态变量占位符。
    /// </summary>
    private static string GetFormattedCardDescriptionForDamage(CardDisplayInfo info)
    {
        try
        {
            string description = info.Card.GetDescriptionForPile(PileType.None);
            return StripCardMarkup(description);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"读取卡牌格式化描述失败，伤害排序将回退到展示描述：card={info.IdText}，error={ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 从中英文卡牌描述中解析总伤害；例如「造成3点伤害3次」按 9 计算。
    /// </summary>
    private static bool TryParseDamageFromText(string? text, out int damage)
    {
        damage = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string cleaned = StripCardMarkup(text);
        damage += SumDamageMatches(ChineseDamageRegex.Matches(cleaned), amountGroup: 1, timesGroup: 2);
        damage += SumDamageMatches(EnglishDamageRegex.Matches(cleaned), amountGroup: 1, timesGroup: 2);
        return damage > 0;
    }

    /// <summary>
    /// 汇总正则匹配到的伤害值，支持「伤害值 × 次数」。
    /// </summary>
    private static int SumDamageMatches(MatchCollection matches, int amountGroup, int timesGroup)
    {
        int total = 0;
        foreach (Match match in matches)
        {
            if (!int.TryParse(match.Groups[amountGroup].Value, out int amount))
            {
                continue;
            }

            int times = 1;
            if (match.Groups.Count > timesGroup && match.Groups[timesGroup].Success &&
                int.TryParse(match.Groups[timesGroup].Value, out int parsedTimes))
            {
                times = Math.Max(1, parsedTimes);
            }

            total += amount * times;
        }

        return total;
    }

    /// <summary>
    /// 清理卡牌描述里的颜色、图片等 BBCode 标记，只保留可解析文本。
    /// </summary>
    private static string StripCardMarkup(string text)
    {
        return CardBbCodeRegex.Replace(text, string.Empty);
    }

    /// <summary>
    /// 安全读取卡牌模型上的整数属性，读取失败时只记录日志并返回 false。
    /// </summary>
    private static bool TryReadIntProperty(CardModel card, string propertyName, out int value)
    {
        value = 0;
        try
        {
            PropertyInfo? property = card.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property == null)
            {
                return false;
            }

            object? rawValue = property.GetValue(card);
            if (rawValue is int intValue)
            {
                value = intValue;
                return true;
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"读取卡牌伤害排序字段失败，已按 0 处理：card={card.Id}，property={propertyName}，error={ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// 创建导出目录。
    /// 每次导出都单独建一个时间戳子目录，避免覆盖上次结果。
    /// </summary>
    private static string CreateCardExportDirectory()
    {
        string? assemblyPath = typeof(CardService).Assembly.Location;
        string baseDirectory = string.IsNullOrWhiteSpace(assemblyPath)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(assemblyPath) ?? AppContext.BaseDirectory;
        string exportDirectory = Path.Combine(baseDirectory, "exports", "cards", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(exportDirectory);
        return exportDirectory;
    }

    /// <summary>
    /// 初始化导出桶，保证六个分类文件即便当前没有卡片也会生成空文件。
    /// </summary>
    private static Dictionary<string, List<string>> CreateCardExportBuckets()
    {
        Dictionary<string, List<string>> buckets = new(StringComparer.Ordinal);
        foreach (string categoryName in CardExportCategoryOrder)
        {
            buckets[categoryName] = [];
        }

        return buckets;
    }

    /// <summary>
    /// 把卡片类型映射到导出分类名。
    /// 仅导出老大爷指定的六类，其他类型统一跳过。
    /// </summary>
    private static bool TryGetCardExportCategory(CardType type, out string categoryName)
    {
        switch (type)
        {
            case CardType.Attack:
                categoryName = "攻击";
                return true;
            case CardType.Skill:
                categoryName = "技能";
                return true;
            case CardType.Power:
                categoryName = "能力";
                return true;
            case CardType.Curse:
                categoryName = "诅咒";
                return true;
            case CardType.Status:
                categoryName = "状态";
                return true;
            case CardType.Quest:
                categoryName = "任务";
                return true;
            default:
                categoryName = string.Empty;
                return false;
        }
    }

    /// <summary>
    /// 读取一张卡的中文名和英文名。
    /// 优先直接查指定语言表；查不到时再退回当前语言卡名和原始标题，尽量保证导出不断档。
    /// </summary>
    private static (string ChineseName, string EnglishName) GetBilingualCardName(CardModel card)
    {
        string idText = card.Id.ToString();
        string currentLanguageName = SafeText(GetLocalizedText(card.TitleLocString) ?? card.Title, idText);
        string englishFallback = SafeText(card.Title, currentLanguageName);

        string chineseName = ResolveCardNameByLanguage(card.TitleLocString, ResolveExportLanguageCode(chinese: true), GmLocalizationService.IsChinese ? currentLanguageName : englishFallback);
        string englishName = ResolveCardNameByLanguage(card.TitleLocString, ResolveExportLanguageCode(chinese: false), !GmLocalizationService.IsChinese ? currentLanguageName : englishFallback);
        return (SafeText(chineseName, currentLanguageName), SafeText(englishName, englishFallback));
    }

    /// <summary>
    /// 按指定语言码解析卡片名称。
    /// 不切换游戏当前语言，只从目标语言表里直接按表名和词条键读取原始文本。
    /// </summary>
    private static string ResolveCardNameByLanguage(LocString? titleLocString, string languageCode, string fallback)
    {
        if (titleLocString == null || titleLocString.IsEmpty)
        {
            return fallback;
        }

        Dictionary<string, LocTable>? languageTables = GetExportLanguageTables(languageCode);
        if (languageTables == null)
        {
            return fallback;
        }

        string tableName = titleLocString.LocTable;
        string entryKey = titleLocString.LocEntryKey;
        if (string.IsNullOrWhiteSpace(tableName) || string.IsNullOrWhiteSpace(entryKey))
        {
            return fallback;
        }

        if (!languageTables.TryGetValue(tableName, out LocTable? table) || !table.HasEntry(entryKey))
        {
            return fallback;
        }

        return SafeText(table.GetRawText(entryKey), fallback);
    }

    /// <summary>
    /// 获取导出所需的目标语言码。
    /// 英文固定优先 eng，中文固定优先 zhs，找不到时再从当前语言列表里模糊匹配。
    /// </summary>
    private static string ResolveExportLanguageCode(bool chinese)
    {
        List<string> languages = LocManager.Languages ?? [];
        string[] preferredCodes = chinese ? ["zhs", "zht", "zh"] : ["eng", "en"];

        foreach (string preferredCode in preferredCodes)
        {
            string? matchedCode = languages.FirstOrDefault(language => string.Equals(language, preferredCode, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(matchedCode))
            {
                return matchedCode;
            }
        }

        string? fuzzyMatchedCode = languages.FirstOrDefault(language => chinese
            ? language.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            : language.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(fuzzyMatchedCode))
        {
            return fuzzyMatchedCode;
        }

        return chinese ? "zhs" : "eng";
    }

    /// <summary>
    /// 读取指定语言的词条表。
    /// 通过反射调用游戏私有加载器，避免为了导出而切换当前游戏语言。
    /// </summary>
    private static Dictionary<string, LocTable>? GetExportLanguageTables(string languageCode)
    {
        lock (ExportLanguageTablesLock)
        {
            if (ExportLanguageTablesCache.TryGetValue(languageCode, out Dictionary<string, LocTable>? cachedTables))
            {
                return cachedTables;
            }

            if (LoadTablesFromPathMethod == null)
            {
                MainFile.Logger.Info("[卡片导出] 无法定位 LocManager.LoadTablesFromPath，双语导出将回退到兜底卡名。");
                return null;
            }

            try
            {
                object? rawResult = LoadTablesFromPathMethod.Invoke(null, [languageCode, false]);
                if (rawResult is not ValueTuple<Dictionary<string, LocTable>, bool, List<LocValidationError>> loadResult)
                {
                    MainFile.Logger.Info($"[卡片导出] 语言表返回值异常：language={languageCode}");
                    return null;
                }

                Dictionary<string, LocTable> loadedTables = loadResult.Item1 ?? [];
                if (loadedTables.Count == 0)
                {
                    MainFile.Logger.Info($"[卡片导出] 语言表为空：language={languageCode}");
                    return null;
                }

                ExportLanguageTablesCache[languageCode] = loadedTables;
                return loadedTables;
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"[卡片导出] 加载语言表失败：language={languageCode}，error={ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// 卡片类型本地化文本。
    /// </summary>
    private static string ToTypeText(CardType type)
    {
        return type switch
        {
            CardType.Attack => GmUiText.CardTypeAttack,
            CardType.Skill => GmUiText.CardTypeSkill,
            CardType.Power => GmUiText.CardTypePower,
            CardType.Status => GmUiText.CardTypeStatus,
            CardType.Curse => GmUiText.CardTypeCurse,
            CardType.Quest => GmUiText.CardTypeQuest,
            _ => GmUiText.Pick("未知", "Unknown"),
        };
    }

    /// <summary>
    /// 稀有度本地化文本。
    /// </summary>
    private static string ToRarityText(CardRarity rarity)
    {
        return rarity switch
        {
            CardRarity.Basic => GmUiText.Pick("基础", "Basic"),
            CardRarity.Common => GmUiText.Pick("普通", "Common"),
            CardRarity.Uncommon => GmUiText.Pick("罕见", "Uncommon"),
            CardRarity.Rare => GmUiText.Pick("稀有", "Rare"),
            CardRarity.Ancient => GmUiText.Pick("先古", "Ancient"),
            CardRarity.Event => GmUiText.Pick("事件", "Event"),
            CardRarity.Token => GmUiText.Pick("代币", "Token"),
            CardRarity.Status => GmUiText.Pick("状态", "Status"),
            CardRarity.Curse => GmUiText.Pick("诅咒", "Curse"),
            CardRarity.Quest => GmUiText.Pick("任务", "Quest"),
            _ => GmUiText.Pick("未知", "Unknown"),
        };
    }

    /// <summary>
    /// 来源标识，用于内部匹配（来源筛选的稳定 key）。
    /// </summary>
    private static string ToSourceKey(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName) || assemblyName.Equals("sts2", StringComparison.OrdinalIgnoreCase))
        {
            return "BaseGame";
        }

        if (assemblyName.Equals("TheBookOfAges", StringComparison.OrdinalIgnoreCase) ||
            assemblyName.Equals(MainFile.ModId, StringComparison.OrdinalIgnoreCase))
        {
            return "ThisMod";
        }

        return assemblyName;
    }

    /// <summary>
    /// 来源显示文本，本体、当前 mod、其他 mod 分开展示。
    /// </summary>
    private static string ToSourceText(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName) || assemblyName.Equals("sts2", StringComparison.OrdinalIgnoreCase))
        {
            return GmUiText.Pick("游戏本体", "Base Game");
        }

        if (assemblyName.Equals("TheBookOfAges", StringComparison.OrdinalIgnoreCase) ||
            assemblyName.Equals(MainFile.ModId, StringComparison.OrdinalIgnoreCase))
        {
            return "TheBookOfAges / GM Console";
        }

        return GmUiText.Pick($"{assemblyName} 模组", $"{assemblyName} Mod");
    }

    /// <summary>
    /// 角色排序：当前角色 0，其他职业按字母 1，无色 2。
    /// </summary>
    private static int GetCharacterOrder(string characterKey)
    {
        if (string.Equals(characterKey, ColorlessKey, StringComparison.Ordinal))
        {
            return 2;
        }

        return 1;
    }

    /// <summary>
    /// 类型排序，攻击/技能/能力优先，状态/诅咒靠后。
    /// </summary>
    private static int GetTypeOrder(string typeKey)
    {
        return typeKey switch
        {
            "Attack" => 0,
            "Skill" => 1,
            "Power" => 2,
            "Status" => 3,
            "Curse" => 4,
            _ => 9,
        };
    }

    /// <summary>
    /// 稀有度排序：基础/普通/罕见/稀有/先古/事件/特殊/状态/诅咒。
    /// </summary>
    private static int GetRarityOrder(string rarityKey)
    {
        return rarityKey switch
        {
            "Basic" => 0,
            "Common" => 1,
            "Uncommon" => 2,
            "Rare" => 3,
            "Ancient" => 4,
            "Event" => 5,
            "Token" => 6,
            "Status" => 7,
            "Curse" => 8,
            "Quest" => 9,
            _ => 99,
        };
    }

    /// <summary>
    /// 费用排序：先 X 费排最后，其余按数值升序。
    /// </summary>
    private static int GetCostOrder(int cost, bool costsX)
    {
        return costsX ? 1000 : cost;
    }

    /// <summary>
    /// 规范化搜索文本，避免大小写影响检索。
    /// </summary>
    private static string Normalize(string value)
    {
        return string.IsNullOrEmpty(value) ? "" : value.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// 给空文本提供兜底文案，避免页面出现空白。
    /// </summary>
    private static string SafeText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    /// <summary>
    /// 解析游戏本地化文本，按玩家当前语言返回可读内容。
    /// </summary>
    private static string? GetLocalizedText(LocString? locString)
    {
        if (locString == null || locString.IsEmpty)
        {
            return null;
        }

        try
        {
            return locString.GetFormattedText();
        }
        catch
        {
            try
            {
                return locString.GetRawText();
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"解析卡片本地化文本失败 / Failed to resolve card localized text: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// 卡片描述里 SmartFormat 占位符正则，匹配形如 {Block:diff()} {StrengthPower:diff()} {Stars:abs()} {Gold} 等。
    /// 这些占位符依赖卡片所在的玩家/战斗上下文才能格式化，模板态卡片直接 GetFormattedText 会触发本地化报错。
    /// </summary>
    private static readonly Regex CardSmartFormatRegex = new(@"\{[A-Za-z][A-Za-z0-9_]*(:[A-Za-z0-9_()]+)?\}", RegexOptions.Compiled);

    /// <summary>
    /// 卡片描述里的 BBCode 标签正则（如 [gold]xx[/gold]），仅去掉外层标签保留内容。
    /// </summary>
    private static readonly Regex CardBbCodeRegex = new(@"\[/?[A-Za-z][A-Za-z0-9_]*\]", RegexOptions.Compiled);

    /// <summary>
    /// 中文伤害文本正则：匹配「造成32点伤害」和「造成3点伤害3次」。
    /// </summary>
    private static readonly Regex ChineseDamageRegex = new(@"造成\s*(\d+)\s*点伤害(?:\s*(\d+)\s*次)?", RegexOptions.Compiled);

    /// <summary>
    /// 英文伤害文本正则：匹配「Deal 32 damage」和「Deal 3 damage 3 times」。
    /// </summary>
    private static readonly Regex EnglishDamageRegex = new(@"\bDeal\s+(\d+)\s+damage(?:\s+(\d+)\s+times)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// 解析卡片描述：直接走 LocString.GetRawText() 拿模板字符串，不调 GetFormattedText 避免 LocManager 写 ERROR 日志；
    /// 然后把 {XXX:diff()} 之类的动态占位符替换成 ?，再去掉 [gold][/gold] 等 BBCode 标签，得到适合 tooltip 与关键字搜索的纯文本。
    /// </summary>
    private static string? GetCardDescription(LocString? locString)
    {
        if (locString == null || locString.IsEmpty)
        {
            return null;
        }

        string raw;
        try
        {
            raw = locString.GetRawText();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"读取卡片描述原文失败 / Failed to read card description raw text: {ex.Message}");
            return null;
        }

        if (string.IsNullOrEmpty(raw))
        {
            return raw;
        }

        // {XXX:diff()} / {XXX:abs()} / {XXX} → ?
        string cleaned = CardSmartFormatRegex.Replace(raw, "?");
        // [gold]...[/gold] / [blue]...[/blue] 等 BBCode 标签去掉
        cleaned = CardBbCodeRegex.Replace(cleaned, string.Empty);
        return cleaned;
    }
}
