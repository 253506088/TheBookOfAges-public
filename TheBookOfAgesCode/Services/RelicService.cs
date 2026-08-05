using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.Config;
using System.Text.RegularExpressions;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.UI;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// 遗物服务，负责实时读取游戏遗物和调用游戏内部流程发放遗物。
/// </summary>
public sealed class RelicService
{
    private const string AllFilterKey = "all";
    private const string CategoryCharacterKey = "character";
    private const string CategoryCharacterPrefix = "character:";
    private const string CategorySharedKey = "shared";
    private const string CategoryAncientKey = "ancient";
    private const string SourceBaseGameKey = "base-game";
    private const string SourceModsKey = "mods";
    private static readonly Regex RelicIfUpgradedShowRegex = new(@"\{IfUpgraded:show:(.*?)\|(.*?)\}", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex RelicIfUpgradedHideRegex = new(@"\{IfUpgraded:hide:(.*?)\|(.*?)\}", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex RelicChoiceFormatRegex = new(@"\{[A-Za-z][A-Za-z0-9_]*:[A-Za-z][A-Za-z0-9_]*:(.*?)\|(.*?)\}", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex RelicSmartFormatRegex = new(@"\{[A-Za-z][A-Za-z0-9_]*(?::[^{}|]*)?\}", RegexOptions.Compiled);
    private readonly RelicConsoleCmd _relicConsoleCmd = new();

    /// <summary>
    /// 从游戏当前 ModelDb 实时读取所有遗物，并转换成页面展示数据。
    /// </summary>
    public IReadOnlyList<RelicDisplayInfo> GetRelics(string keyword, string categoryFilterKey, string rarityFilterKey, string sourceFilterKey)
    {
        Player? player = GetCurrentPlayer();
        HashSet<string> ownedIds = GetOwnedRelicIds(player);
        string normalizedKeyword = Normalize(keyword);

        return ModelDb.AllRelics
            .Select(relic => BuildDisplayInfo(relic, ownedIds))
            .Where(info => MatchesKeyword(info, normalizedKeyword))
            .Where(info => MatchesCategoryFilter(info.CategoryKey, categoryFilterKey))
            .Where(info => MatchesFilter(info.RarityKey, rarityFilterKey))
            .Where(info => MatchesSource(info.SourceKey, sourceFilterKey))
            .OrderBy(info => GetCategoryOrder(info.CategoryKey))
            .ThenBy(info => GetRarityOrder(info.RarityKey))
            .ThenBy(info => GetSourceOrder(info.SourceKey))
            .ThenBy(info => info.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 从当前玩家身上读取已经持有的遗物，并转换成页面展示数据。
    /// </summary>
    public IReadOnlyList<RelicDisplayInfo> GetOwnedRelics(string keyword, string categoryFilterKey, string rarityFilterKey, string sourceFilterKey)
    {
        Player? player = GetCurrentPlayer();
        if (player?.Relics == null)
        {
            return [];
        }

        HashSet<string> ownedIds = GetOwnedRelicIds(player);
        string normalizedKeyword = Normalize(keyword);
        return player.Relics
            .Where(relic => relic != null)
            .Select(relic => BuildDisplayInfo(relic, ownedIds))
            .Where(info => MatchesKeyword(info, normalizedKeyword))
            .Where(info => MatchesCategoryFilter(info.CategoryKey, categoryFilterKey))
            .Where(info => MatchesFilter(info.RarityKey, rarityFilterKey))
            .Where(info => MatchesSource(info.SourceKey, sourceFilterKey))
            .OrderBy(info => GetCategoryOrder(info.CategoryKey))
            .ThenBy(info => GetRarityOrder(info.RarityKey))
            .ThenBy(info => GetSourceOrder(info.SourceKey))
            .ThenBy(info => info.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 获取遗物分类筛选项。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetCategoryOptions()
    {
        List<GmFilterOption> options =
        [
            new GmFilterOption(AllFilterKey, GmUiText.Pick("全部分类", "All Categories")),
            new GmFilterOption(CategoryCharacterKey, GmUiText.Pick("职业遗物（全部角色）", "Character Relics (All Characters)")),
        ];
        options.AddRange(ModelDb.AllRelics
            .Select(relic =>
            {
                string categoryKey = ToCategoryKey(relic);
                return new GmFilterOption(categoryKey, ToCategoryText(categoryKey, relic));
            })
            .Where(option => IsCharacterCategoryKey(option.Key))
            .DistinctBy(option => option.Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(option => option.Text, StringComparer.CurrentCultureIgnoreCase));
        options.Add(new GmFilterOption(CategorySharedKey, GmUiText.Pick("公有遗物", "Shared Relics")));
        options.Add(new GmFilterOption(CategoryAncientKey, GmUiText.Pick("先古之民遗物", "Ancient Relics")));
        return options;
    }

    /// <summary>
    /// 获取遗物稀有度筛选项。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetRarityOptions()
    {
        return [
            new GmFilterOption(AllFilterKey, GmUiText.Pick("全部稀有度", "All Rarities")),
            new GmFilterOption("starter", GmUiText.Pick("初始", "Starter")),
            new GmFilterOption("common", GmUiText.Pick("普通", "Common")),
            new GmFilterOption("uncommon", GmUiText.Pick("罕见", "Uncommon")),
            new GmFilterOption("rare", GmUiText.Pick("稀有", "Rare")),
            new GmFilterOption("boss", GmUiText.Pick("首领", "Boss")),
            new GmFilterOption("shop", GmUiText.Pick("商店", "Shop")),
            new GmFilterOption("special", GmUiText.Pick("特殊", "Special")),
            new GmFilterOption("ancient", GmUiText.Pick("先古", "Ancient")),
        ];
    }

    /// <summary>
    /// 获取遗物来源筛选项。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetSourceOptions()
    {
        return [
            new GmFilterOption(AllFilterKey, GmUiText.Pick("全部来源", "All Sources")),
            new GmFilterOption(SourceBaseGameKey, GmUiText.Pick("游戏本体", "Base Game")),
            new GmFilterOption(SourceModsKey, "Mods"),
        ];
    }

    /// <summary>
    /// 批量发放已选择的遗物，优先复用控制台命令路径，失败后再走 RelicCmd 兜底。
    /// </summary>
    public async Task<RelicGrantResult> GrantRelicsAsync(IEnumerable<RelicDisplayInfo> selectedRelics)
    {
        RelicGrantResult result = new();
        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            result.FailedCount++;
            result.Messages.Add(GmUiText.NotInRun);
            return result;
        }

        bool isMultiplayer = GmRunContextService.IsMultiplayer();
        bool preventDuplicateGrant = GmModConfig.ShouldPreventDuplicateRelicGrant;
        foreach (RelicDisplayInfo info in selectedRelics)
        {
            bool alreadyOwned = HasRelic(player, info.IdText);
            if (preventDuplicateGrant && alreadyOwned)
            {
                result.SkippedCount++;
                result.Messages.Add(string.Format(GmUiText.AlreadyOwnedNamed, info.Name));
                continue;
            }

            string commandId = GetRelicCommandId(info);
            bool success = isMultiplayer
                ? await SubmitRelicNetworkCommandAsync(player, $"relic {commandId}", "遗物发放", info, commandId)
                : alreadyOwned
                    ? await TryGrantByRelicCmd(player, info)
                    : await TryGrantByConsoleCommand(player, info) || await TryGrantByRelicCmd(player, info);
            if (success)
            {
                result.SuccessCount++;
                string template = isMultiplayer ? GmUiText.GrantSubmittedNamed : GmUiText.GrantSuccessNamed;
                result.Messages.Add(string.Format(template, info.Name));
            }
            else
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.GrantFailedNamed, info.Name));
            }
        }

        return result;
    }

    /// <summary>
    /// 批量删除已选择的遗物，使用游戏内部 RelicCmd 删除流程。
    /// </summary>
    public async Task<RelicRemoveResult> RemoveRelicsAsync(IEnumerable<RelicDisplayInfo> selectedRelics)
    {
        RelicRemoveResult result = new();
        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            result.FailedCount++;
            result.Messages.Add(GmUiText.NotInRun);
            return result;
        }

        bool isMultiplayer = GmRunContextService.IsMultiplayer();
        foreach (RelicDisplayInfo info in selectedRelics)
        {
            RelicModel? currentRelic = FindOwnedRelic(player, info.IdText);
            if (currentRelic == null)
            {
                result.SkippedCount++;
                result.Messages.Add(string.Format(GmUiText.RemoveMissingNamed, info.Name));
                continue;
            }

            string commandId = GetRelicCommandId(info);
            bool success = isMultiplayer
                ? await SubmitRelicNetworkCommandAsync(player, $"relic remove {commandId}", "遗物删除", info, commandId)
                : await TryRemoveByRelicCmd(currentRelic, player, info.IdText);
            if (success)
            {
                result.SuccessCount++;
                string template = isMultiplayer ? GmUiText.RemoveSubmittedNamed : GmUiText.RemoveSuccessNamed;
                result.Messages.Add(string.Format(template, info.Name));
            }
            else
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.RemoveFailedNamed, info.Name));
            }
        }

        return result;
    }

    /// <summary>
    /// 获取当前玩家；没有局内状态时返回空。
    /// </summary>
    private static Player? GetCurrentPlayer()
    {
        return GmRunContextService.GetCurrentPlayer();
    }

    /// <summary>
    /// 提交多人遗物网络命令，并记录完整 ID 与控制台命令 ID，方便排查命令匹配问题。
    /// </summary>
    private static async Task<bool> SubmitRelicNetworkCommandAsync(Player player, string command, string actionName, RelicDisplayInfo info, string commandId)
    {
        MainFile.Logger.Info($"遗物多人命令准备提交：动作={actionName}，遗物={info.Name}，完整ID={info.IdText}，命令ID={commandId}，命令={command}。");
        bool submitted = await GmNetworkCommandService.TryEnqueueConsoleCommandAsync(player, command, actionName);
        MainFile.Logger.Info($"遗物多人命令提交结束：动作={actionName}，遗物={info.Name}，完整ID={info.IdText}，命令ID={commandId}，提交成功={submitted}。");
        return submitted;
    }

    /// <summary>
    /// 读取遗物控制台命令真正匹配的 ID；原生命令按 ModelId.Entry 匹配，不吃 Category.Entry 完整格式。
    /// </summary>
    private static string GetRelicCommandId(RelicDisplayInfo info)
    {
        return string.IsNullOrWhiteSpace(info.Id.Entry) ? info.IdText : info.Id.Entry;
    }

    /// <summary>
    /// 获取当前玩家已经拥有的遗物 ID，用于页面置灰和发放前二次检查。
    /// </summary>
    private static HashSet<string> GetOwnedRelicIds(Player? player)
    {
        if (player?.Relics == null)
        {
            return [];
        }

        return player.Relics
            .Where(relic => relic != null)
            .Select(relic => relic.Id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 把游戏遗物模型转换成页面需要的简单字段。
    /// </summary>
    private static RelicDisplayInfo BuildDisplayInfo(RelicModel relic, HashSet<string> ownedIds)
    {
        string idText = relic.Id.ToString();
        string rarityKey = ToRarityKey(relic.Rarity.ToString());
        string categoryKey = ToCategoryKey(relic);
        string sourceKey = ToSourceKey(relic.GetType().Assembly.GetName().Name);
        return new RelicDisplayInfo
        {
            Id = relic.Id,
            IdText = idText,
            Name = SafeText(GetLocalizedText(relic.Title), idText),
            Description = SafeText(GetLocalizedDescriptionText(relic.DynamicDescription), GmUiText.NoDescription),
            RarityKey = rarityKey,
            Rarity = ToRarityText(rarityKey),
            CategoryKey = categoryKey,
            Category = ToCategoryText(categoryKey, relic),
            SourceKey = sourceKey,
            Source = ToSourceText(sourceKey, relic.GetType().Assembly.GetName().Name),
            Icon = relic.Icon,
            IsOwned = ownedIds.Contains(idText),
            Relic = relic,
        };
    }

    /// <summary>
    /// 判断遗物名称或描述是否匹配搜索关键字。
    /// </summary>
    private static bool MatchesKeyword(RelicDisplayInfo info, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return Normalize(info.Name).Contains(keyword) ||
               Normalize(info.Description).Contains(keyword);
    }

    /// <summary>
    /// 判断普通筛选条件是否命中，“全部”表示不过滤。
    /// </summary>
    private static bool MatchesFilter(string valueKey, string filterKey)
    {
        return string.IsNullOrWhiteSpace(filterKey) ||
               filterKey.Equals(AllFilterKey, StringComparison.OrdinalIgnoreCase) ||
               valueKey.Equals(filterKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断分类筛选是否命中，职业遗物会带具体职业名，所以需要按分类前缀匹配。
    /// </summary>
    private static bool MatchesCategoryFilter(string valueKey, string filterKey)
    {
        if (string.IsNullOrWhiteSpace(filterKey) ||
            filterKey.Equals(AllFilterKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (filterKey.Equals(CategoryCharacterKey, StringComparison.OrdinalIgnoreCase))
        {
            return IsCharacterCategoryKey(valueKey);
        }

        return valueKey.Equals(filterKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断来源筛选是否命中，本体、当前 Mod 和其他 Mod 分开处理。
    /// </summary>
    private static bool MatchesSource(string valueKey, string filterKey)
    {
        if (string.IsNullOrWhiteSpace(filterKey) ||
            filterKey.Equals(AllFilterKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (filterKey.Equals(SourceModsKey, StringComparison.OrdinalIgnoreCase))
        {
            return !valueKey.Equals(SourceBaseGameKey, StringComparison.OrdinalIgnoreCase);
        }

        return valueKey.Equals(filterKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 使用控制台命令发放遗物，尽量复用游戏已有调试命令逻辑。
    /// </summary>
    private async Task<bool> TryGrantByConsoleCommand(Player player, RelicDisplayInfo info)
    {
        try
        {
            CmdResult cmdResult = _relicConsoleCmd.Process(player, [info.IdText]);
            if (cmdResult.task != null)
            {
                await cmdResult.task;
            }

            return cmdResult.success || HasRelic(player, info.IdText);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"控制台发放遗物失败，改用 RelicCmd 兜底 / Console relic grant failed, fallback to RelicCmd: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 使用游戏命令对象发放遗物，作为控制台命令失败后的兜底方案。
    /// </summary>
    private static async Task<bool> TryGrantByRelicCmd(Player player, RelicDisplayInfo info)
    {
        try
        {
            int oldCount = CountRelic(player, info.IdText);
            RelicModel relic = info.Relic.ToMutable();
            await RelicCmd.Obtain(relic, player, player.Relics.Count);
            return CountRelic(player, info.IdText) > oldCount;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"RelicCmd 发放遗物失败 / RelicCmd grant failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 判断玩家是否已经持有指定遗物。
    /// </summary>
    private static bool HasRelic(Player player, string idText)
    {
        return player.Relics.Any(relic => relic.Id.ToString().Equals(idText, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 统计玩家当前持有指定遗物的数量，用于重复发放时判断是否真的新增了一件。
    /// </summary>
    private static int CountRelic(Player player, string idText)
    {
        return player.Relics.Count(relic => relic.Id.ToString().Equals(idText, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 从玩家当前遗物列表里查找指定遗物，删除前重新获取一次避免旧引用。
    /// </summary>
    private static RelicModel? FindOwnedRelic(Player player, string idText)
    {
        return player.Relics.FirstOrDefault(relic => relic.Id.ToString().Equals(idText, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 使用游戏命令对象删除遗物，并在删除后确认遗物已经离开玩家列表。
    /// </summary>
    private static async Task<bool> TryRemoveByRelicCmd(RelicModel relic, Player player, string idText)
    {
        try
        {
            await RelicCmd.Remove(relic);
            return !HasRelic(player, idText);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"RelicCmd 删除遗物失败 / RelicCmd remove failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 转换遗物分类文本，方便玩家按职业、公有、先古之民查看。
    /// </summary>
    private static string ToCategoryKey(RelicModel relic)
    {
        string rarity = relic.Rarity.ToString();
        string poolName = relic.Pool?.GetType().Name ?? "";
        if (rarity.Contains("Ancient", StringComparison.OrdinalIgnoreCase) ||
            poolName.Contains("Event", StringComparison.OrdinalIgnoreCase))
        {
            return CategoryAncientKey;
        }

        if (poolName.Contains("Shared", StringComparison.OrdinalIgnoreCase) ||
            poolName.Length == 0)
        {
            return CategorySharedKey;
        }

        string characterName = poolName.Replace("RelicPool", "");
        return $"{CategoryCharacterPrefix}{characterName}";
    }

    /// <summary>
    /// 转换遗物分类文本，方便玩家按职业、公有、先古之民查看。
    /// </summary>
    private static string ToCategoryText(string categoryKey, RelicModel relic)
    {
        if (categoryKey.Equals(CategoryCharacterKey, StringComparison.OrdinalIgnoreCase))
        {
            return GmUiText.Pick("职业遗物（全部角色）", "Character Relics (All Characters)");
        }

        if (categoryKey.Equals(CategoryAncientKey, StringComparison.OrdinalIgnoreCase))
        {
            return GmUiText.Pick("先古之民遗物", "Ancient Relics");
        }

        if (categoryKey.Equals(CategorySharedKey, StringComparison.OrdinalIgnoreCase))
        {
            return GmUiText.Pick("公有遗物", "Shared Relics");
        }

        if (IsCharacterCategoryKey(categoryKey))
        {
            string fallbackCharacterName = GetCharacterNameByCategoryKey(categoryKey);
            string characterName = GmLocalizationService.ResolveCharacterNameByRelicPool(relic.Pool, fallbackCharacterName);
            return GmUiText.Pick($"职业遗物（{characterName}）", $"Character Relics ({characterName})");
        }

        string poolName = relic.Pool?.GetType().Name ?? "";
        string rawName = poolName.Replace("RelicPool", "");
        string displayName = GmLocalizationService.ResolveCharacterNameByRelicPool(relic.Pool, rawName);
        return GmUiText.Pick($"职业遗物（{displayName}）", $"Character Relics ({displayName})");
    }

    /// <summary>
    /// 转换遗物稀有度键值，供筛选和排序使用。
    /// </summary>
    private static string ToRarityKey(string rarity)
    {
        return rarity.ToLowerInvariant();
    }

    /// <summary>
    /// 转换遗物稀有度文本。
    /// </summary>
    private static string ToRarityText(string rarityKey)
    {
        return rarityKey switch
        {
            "starter" => GmUiText.Pick("初始", "Starter"),
            "common" => GmUiText.Pick("普通", "Common"),
            "uncommon" => GmUiText.Pick("罕见", "Uncommon"),
            "rare" => GmUiText.Pick("稀有", "Rare"),
            "boss" => GmUiText.Pick("首领", "Boss"),
            "shop" => GmUiText.Pick("商店", "Shop"),
            "special" => GmUiText.Pick("特殊", "Special"),
            "ancient" => GmUiText.Pick("先古", "Ancient"),
            _ => rarityKey,
        };
    }

    /// <summary>
    /// 转换来源键值，本体、当前 Mod、其他 Mod 使用统一格式。
    /// </summary>
    private static string ToSourceKey(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName) || assemblyName.Equals("sts2", StringComparison.OrdinalIgnoreCase))
        {
            return SourceBaseGameKey;
        }

        return $"mod:{assemblyName}";
    }

    /// <summary>
    /// 转换来源文本，本体、当前 Mod、其他 Mod 分开显示。
    /// </summary>
    private static string ToSourceText(string sourceKey, string? assemblyName)
    {
        if (sourceKey.Equals(SourceBaseGameKey, StringComparison.OrdinalIgnoreCase))
        {
            return GmUiText.Pick("游戏本体", "Base Game");
        }

        if (string.Equals(assemblyName, "TheBookOfAges", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(assemblyName, MainFile.ModId, StringComparison.OrdinalIgnoreCase))
        {
            return "TheBookOfAges / GM Console";
        }

        return GmUiText.Pick($"{assemblyName} 模组", $"{assemblyName} Mod");
    }

    /// <summary>
    /// 分类排序权重，让职业、公有、先古之民的顺序稳定。
    /// </summary>
    private static int GetCategoryOrder(string categoryKey)
    {
        if (categoryKey.Equals(CategoryCharacterKey, StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        if (IsCharacterCategoryKey(categoryKey))
        {
            return 0;
        }

        if (categoryKey.Equals(CategorySharedKey, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (categoryKey.Equals(CategoryAncientKey, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 9;
    }

    /// <summary>
    /// 判断是否为职业遗物分类键值。
    /// </summary>
    private static bool IsCharacterCategoryKey(string categoryKey)
    {
        return categoryKey.StartsWith(CategoryCharacterPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 从职业遗物分类键值中提取职业名。
    /// </summary>
    private static string GetCharacterNameByCategoryKey(string categoryKey)
    {
        if (!IsCharacterCategoryKey(categoryKey))
        {
            return categoryKey;
        }

        return categoryKey[CategoryCharacterPrefix.Length..];
    }

    /// <summary>
    /// 稀有度排序权重，让列表浏览顺序更接近游戏认知。
    /// </summary>
    private static int GetRarityOrder(string rarityKey)
    {
        string[] order = ["starter", "common", "uncommon", "rare", "boss", "shop", "special", "ancient"];
        for (int i = 0; i < order.Length; i++)
        {
            if (rarityKey.Equals(order[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 99;
    }

    /// <summary>
    /// 来源排序权重，让本体来源排在 Mod 来源前面。
    /// </summary>
    private static int GetSourceOrder(string sourceKey)
    {
        if (sourceKey.Equals(SourceBaseGameKey, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (sourceKey.Equals($"mod:{MainFile.ModId}", StringComparison.OrdinalIgnoreCase) ||
            sourceKey.Equals("mod:TheBookOfAges", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    /// <summary>
    /// 规范化搜索文本，避免大小写影响检索。
    /// </summary>
    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
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
    private static string? GetLocalizedText(MegaCrit.Sts2.Core.Localization.LocString? locString)
    {
        return GmLocalizationService.ResolveText(locString, "解析遗物本地化文本失败 / Failed to resolve relic localized text");
    }

    /// <summary>
    /// 解析遗物描述文本；描述可能依赖运行时变量，所以只读原文并清理 SmartFormat 占位符。
    /// </summary>
    private static string? GetLocalizedDescriptionText(MegaCrit.Sts2.Core.Localization.LocString? locString)
    {
        string? rawText = GmLocalizationService.ResolveRawText(locString, "解析遗物原始本地化文本失败 / Failed to resolve raw relic localized text");
        return CleanRelicDescriptionText(rawText);
    }

    /// <summary>
    /// 清理遗物描述里的运行时 SmartFormat 片段，避免 GM 列表展示模板遗物时触发格式化异常。
    /// </summary>
    private static string? CleanRelicDescriptionText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        string cleaned = RelicIfUpgradedShowRegex.Replace(text, "$2");
        cleaned = RelicIfUpgradedHideRegex.Replace(cleaned, "$1");
        cleaned = RelicChoiceFormatRegex.Replace(cleaned, "$2");
        cleaned = RelicSmartFormatRegex.Replace(cleaned, "?");
        return cleaned;
    }
}
