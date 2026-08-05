using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using System.Collections;
using System.Reflection;
using TheBookOfAges.TheBookOfAgesCode.GameActions;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.UI;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// 药水服务，负责实时读取游戏药水和调用游戏内部流程发放药水。
/// </summary>
public sealed class PotionService
{
    private const string AllFilterKey = "all";
    private const string CategoryCharacterKey = "character";
    private const string CategoryCharacterPrefix = "character:";
    private const string CategorySharedKey = "shared";
    private const string SourceBaseGameKey = "base-game";
    private const string SourceModsKey = "mods";
    private readonly PotionConsoleCmd _potionConsoleCmd = new();

    /// <summary>
    /// 从游戏当前 ModelDb 实时读取所有药水，并转换成页面展示数据。
    /// </summary>
    public IReadOnlyList<PotionDisplayInfo> GetPotions(string keyword, string categoryFilterKey, string rarityFilterKey, string sourceFilterKey)
    {
        string normalizedKeyword = Normalize(keyword);
        return ModelDb.AllPotions
            .Select(BuildDisplayInfo)
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
    /// 获取药水分类筛选项，职业药水会带上职业池名称。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetCategoryOptions()
    {
        List<GmFilterOption> options = [new GmFilterOption(AllFilterKey, GmUiText.Pick("全部分类", "All Categories"))];
        List<GmFilterOption> dynamicOptions = ModelDb.AllPotions
            .Select(potion =>
            {
                string categoryKey = ToCategoryKey(potion);
                return new GmFilterOption(categoryKey, ToCategoryText(categoryKey, potion));
            })
            .DistinctBy(option => option.Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(option => GetCategoryOrder(option.Key))
            .ThenBy(option => option.Text, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        options.Add(new GmFilterOption(CategoryCharacterKey, GmUiText.Pick("职业药水（全部角色）", "Character Potions (All Characters)")));
        options.AddRange(dynamicOptions);
        return options;
    }

    /// <summary>
    /// 获取药水稀有度筛选项。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetRarityOptions()
    {
        List<GmFilterOption> options = [new GmFilterOption(AllFilterKey, GmUiText.Pick("全部稀有度", "All Rarities"))];
        options.AddRange(ModelDb.AllPotions
            .Select(potion =>
            {
                string rarityKey = ToRarityKey(potion.Rarity.ToString());
                return new GmFilterOption(rarityKey, ToRarityText(rarityKey));
            })
            .DistinctBy(option => option.Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(option => GetRarityOrder(option.Key))
            .ThenBy(option => option.Text, StringComparer.CurrentCultureIgnoreCase));
        return options;
    }

    /// <summary>
    /// 获取药水来源筛选项。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetSourceOptions()
    {
        List<GmFilterOption> options = [new GmFilterOption(AllFilterKey, GmUiText.Pick("全部来源", "All Sources"))];
        options.AddRange(ModelDb.AllPotions
            .Select(potion =>
            {
                string sourceKey = ToSourceKey(potion.GetType().Assembly.GetName().Name);
                return new GmFilterOption(sourceKey, ToSourceText(sourceKey, potion.GetType().Assembly.GetName().Name));
            })
            .DistinctBy(option => option.Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(option => GetSourceOrder(option.Key))
            .ThenBy(option => option.Text, StringComparer.CurrentCultureIgnoreCase));
        options.Add(new GmFilterOption(SourceModsKey, "Mods"));
        return options;
    }

    /// <summary>
    /// 批量发放已选择的药水，支持同一种药水重复发放。
    /// </summary>
    public async Task<PotionGrantResult> GrantPotionsAsync(IEnumerable<PotionDisplayInfo> selectedPotions)
    {
        PotionGrantResult result = new();
        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            result.FailedCount++;
            result.Messages.Add(GmUiText.NotInRun);
            return result;
        }

        List<PotionDisplayInfo> selectedPotionList = selectedPotions.ToList();
        if (GmRunContextService.IsMultiplayer())
        {
            return await GrantPotionsByNetworkActionAsync(player, selectedPotionList);
        }

        foreach (PotionDisplayInfo info in selectedPotionList)
        {
            if (!player.HasOpenPotionSlots)
            {
                result.SkippedCount++;
                result.Messages.Add(string.Format(GmUiText.PotionSlotsFullNamed, info.Name));
                continue;
            }

            bool success = await TryGrantByConsoleCommand(player, info) || await TryGrantByPotionCmd(player, info);
            if (success)
            {
                result.SuccessCount++;
                result.Messages.Add(string.Format(GmUiText.PotionGrantSuccessNamed, info.Name));
            }
            else
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.PotionGrantFailedNamed, info.Name));
            }
        }

        return result;
    }

    /// <summary>
    /// 多人模式下把药水发放转换成 GM 自定义同步动作，避免控制台文本命令入队后不落地。
    /// </summary>
    /// <param name="player">当前本机玩家。</param>
    /// <param name="selectedPotions">用户选择的药水列表，已经按数量展开。</param>
    private static async Task<PotionGrantResult> GrantPotionsByNetworkActionAsync(Player player, IReadOnlyList<PotionDisplayInfo> selectedPotions)
    {
        PotionGrantResult result = new();
        int remainingSlots = GetAvailablePotionSlotCount(player);
        List<PotionDisplayInfo> submittedPotions = [];
        List<string> potionIds = [];

        foreach (PotionDisplayInfo info in selectedPotions)
        {
            if (remainingSlots <= 0)
            {
                result.SkippedCount++;
                result.Messages.Add(string.Format(GmUiText.PotionSlotsFullNamed, info.Name));
                continue;
            }

            remainingSlots--;
            submittedPotions.Add(info);
            potionIds.Add(info.IdText);
        }

        if (potionIds.Count == 0)
        {
            return result;
        }

        bool submitted = await GmNetworkCommandService.TryEnqueueGameActionAsync(new GmPotionGrantAction(player, potionIds, CardService.IsInCombat()), "药水发放");
        if (!submitted)
        {
            result.FailedCount += submittedPotions.Count;
            foreach (PotionDisplayInfo info in submittedPotions)
            {
                result.Messages.Add(string.Format(GmUiText.PotionGrantFailedNamed, info.Name));
            }

            return result;
        }

        result.SuccessCount += submittedPotions.Count;
        foreach (PotionDisplayInfo info in submittedPotions)
        {
            result.Messages.Add(string.Format(GmUiText.PotionGrantSubmittedNamed, info.Name));
        }

        MainFile.Logger.Info($"多人药水发放已提交同步动作：提交数量={submittedPotions.Count}，跳过数量={result.SkippedCount}，药水ID=[{string.Join(",", potionIds)}]，战斗中={CardService.IsInCombat()}。");
        return result;
    }

    /// <summary>
    /// 计算多人模式本批次还能提交几瓶药水，避免连续入队超过当前空栏数量。
    /// </summary>
    private static int GetAvailablePotionSlotCount(Player player)
    {
        int filled = player.PotionSlots.Count(slot => slot != null);
        int available = Math.Max(0, player.MaxPotionCount - filled);
        MainFile.Logger.Info($"读取多人药水可提交栏位：最大栏位={player.MaxPotionCount}，已占用={filled}，可提交={available}。");
        return available;
    }

    /// <summary>
    /// 获取当前玩家；没有局内状态时返回空。
    /// </summary>
    private static Player? GetCurrentPlayer()
    {
        return GmRunContextService.GetCurrentPlayer();
    }

    /// <summary>
    /// 把游戏药水模型转换成页面需要的简单字段。
    /// </summary>
    private static PotionDisplayInfo BuildDisplayInfo(PotionModel potion)
    {
        string idText = potion.Id.ToString();
        string rarityKey = ToRarityKey(potion.Rarity.ToString());
        string categoryKey = ToCategoryKey(potion);
        string sourceKey = ToSourceKey(potion.GetType().Assembly.GetName().Name);
        return new PotionDisplayInfo
        {
            Id = potion.Id,
            IdText = idText,
            Name = SafeText(GetLocalizedText(potion.Title), idText),
            Description = SafeText(GetLocalizedText(potion.DynamicDescription), GmUiText.NoDescription),
            RarityKey = rarityKey,
            Rarity = ToRarityText(rarityKey),
            CategoryKey = categoryKey,
            Category = ToCategoryText(categoryKey, potion),
            SourceKey = sourceKey,
            Source = ToSourceText(sourceKey, potion.GetType().Assembly.GetName().Name),
            Icon = potion.Image,
            Potion = potion,
        };
    }

    /// <summary>
    /// 判断药水名称或描述是否匹配搜索关键字。
    /// </summary>
    private static bool MatchesKeyword(PotionDisplayInfo info, string keyword)
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
    /// 判断分类筛选是否命中，职业药水分类包含具体职业名。
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
    /// 使用控制台命令发放药水，尽量复用游戏已有调试命令逻辑。
    /// </summary>
    private async Task<bool> TryGrantByConsoleCommand(Player player, PotionDisplayInfo info)
    {
        try
        {
            int beforeCount = CountPotions(player);
            CmdResult cmdResult = _potionConsoleCmd.Process(player, [info.IdText]);
            if (cmdResult.task != null)
            {
                await cmdResult.task;
            }

            return cmdResult.success && CountPotions(player) > beforeCount;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"控制台发放药水失败，改用 PotionCmd 兜底 / Console potion grant failed, fallback to PotionCmd: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 使用游戏命令对象发放药水，作为控制台命令失败后的兜底方案。
    /// </summary>
    private static async Task<bool> TryGrantByPotionCmd(Player player, PotionDisplayInfo info)
    {
        try
        {
            int slotIndex = GetFirstOpenPotionSlotIndex(player);
            if (slotIndex < 0)
            {
                return false;
            }

            int beforeCount = CountPotions(player);
            PotionProcureResult result = await PotionCmd.TryToProcure(info.Potion.ToMutable(), player, slotIndex);
            return result.success && CountPotions(player) > beforeCount;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"PotionCmd 发放药水失败 / PotionCmd grant failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 获取当前玩家的最大药水栏数量；没有玩家时返回 0。
    /// </summary>
    public int GetMaxPotionSlots()
    {
        Player? player = GetCurrentPlayer();
        int maxSlots = player?.MaxPotionCount ?? 0;
        MainFile.Logger.Info($"读取药水最大栏位：玩家={(player == null ? "空" : player.GetType().FullName)}，最大栏位={maxSlots}。");
        return maxSlots;
    }

    /// <summary>
    /// 获取当前玩家已放入药水的栏位数；没有玩家时返回 0。
    /// </summary>
    public int GetFilledPotionSlots()
    {
        Player? player = GetCurrentPlayer();
        int filled = player?.PotionSlots.Count(slot => slot != null) ?? 0;
        MainFile.Logger.Info($"读取药水已占用栏位：玩家={(player == null ? "空" : player.GetType().FullName)}，已占用={filled}，列表长度={player?.PotionSlots.Count ?? 0}。");
        return filled;
    }

    /// <summary>
    /// 设置最大药水栏数量；已放入药水的栏位不会被移除。
    /// 返回 false 表示数量不合法，并通过 errorMessage 告知原因。
    /// </summary>
    public bool TrySetMaxPotionSlots(int count, out string errorMessage)
    {
        errorMessage = "";
        GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(GmFeature.SetPotionSlots);
        if (!availability.IsAllowed)
        {
            errorMessage = availability.Reason;
            MainFile.Logger.Info($"修改药水栏位已被多人安全闸门阻止：目标栏位={count}，原因={availability.Reason}。");
            return false;
        }

        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            MainFile.Logger.Info($"修改药水栏位失败：当前不在有效局内，目标栏位={count}。");
            errorMessage = GmUiText.NotInRun;
            return false;
        }

        int beforeMax = player.MaxPotionCount;
        int beforeListCount = player.PotionSlots.Count;
        int filled = player.PotionSlots.Count(slot => slot != null);
        MainFile.Logger.Info($"开始修改药水栏位：目标栏位={count}，修改前最大栏位={beforeMax}，修改前列表长度={beforeListCount}，已占用={filled}，玩家类型={player.GetType().FullName}。");
        if (count < filled)
        {
            MainFile.Logger.Info($"修改药水栏位失败：目标栏位小于已占用数量，目标栏位={count}，已占用={filled}。");
            errorMessage = GmUiText.PotionSlotsCannotReduce(filled, count);
            return false;
        }

        var potionSlotsField = typeof(Player).GetField("_potionSlots",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (potionSlotsField == null)
        {
            MainFile.Logger.Info("修改药水栏位失败：找不到 Player._potionSlots 字段。");
            LogPotionSlotMembers();
            errorMessage = GmUiText.Pick("无法同步药水栏位列表。", "Cannot sync potion slot list.");
            return false;
        }

        if (potionSlotsField.GetValue(player) is not IList list)
        {
            MainFile.Logger.Info($"修改药水栏位失败：Player._potionSlots 不是可变列表，字段值类型={potionSlotsField.GetValue(player)?.GetType().FullName ?? "空"}。");
            errorMessage = GmUiText.Pick("无法同步药水栏位列表。", "Cannot sync potion slot list.");
            return false;
        }

        MainFile.Logger.Info($"已取得药水栏位内部列表：字段类型={potionSlotsField.FieldType.FullName}，列表类型={list.GetType().FullName}，当前长度={list.Count}。");

        while (list.Count < count)
        {
            list.Add(null);
            MainFile.Logger.Info($"补充空药水栏位：目标栏位={count}，当前列表长度={list.Count}。");
        }

        while (list.Count > count && list[list.Count - 1] == null)
        {
            list.RemoveAt(list.Count - 1);
            MainFile.Logger.Info($"移除尾部空药水栏位：目标栏位={count}，当前列表长度={list.Count}。");
        }

        TryNotifyMaxPotionCountChanged(player, count);

        int afterMax = player.MaxPotionCount;
        int afterListCount = player.PotionSlots.Count;
        int afterFilled = player.PotionSlots.Count(slot => slot != null);
        MainFile.Logger.Info($"完成修改药水栏位：目标栏位={count}，修改后最大栏位={afterMax}，修改后列表长度={afterListCount}，修改后已占用={afterFilled}。");

        if (afterMax != count || afterListCount != count)
        {
            MainFile.Logger.Info($"修改药水栏位结果异常：目标栏位={count}，实际最大栏位={afterMax}，实际列表长度={afterListCount}。");
            errorMessage = GmUiText.Pick("药水栏位数量修改后校验失败。", "Potion slot count validation failed.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 记录玩家药水栏位相关成员，方便排查游戏版本差异导致的反射失败。
    /// </summary>
    private static void LogPotionSlotMembers()
    {
        foreach (FieldInfo field in typeof(Player).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.Name.Contains("Potion", StringComparison.OrdinalIgnoreCase))
            {
                MainFile.Logger.Info($"Player 药水相关字段：名称={field.Name}，类型={field.FieldType.FullName}。");
            }
        }

        foreach (PropertyInfo property in typeof(Player).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (property.Name.Contains("Potion", StringComparison.OrdinalIgnoreCase))
            {
                MainFile.Logger.Info($"Player 药水相关属性：名称={property.Name}，类型={property.PropertyType.FullName}，可读={property.CanRead}，可写={property.CanWrite}。");
            }
        }
    }

    /// <summary>
    /// 尝试触发药水最大栏位变化事件，让游戏 UI 或监听者刷新栏位显示。
    /// </summary>
    private static void TryNotifyMaxPotionCountChanged(Player player, int count)
    {
        try
        {
            FieldInfo? eventField = typeof(Player).GetField("MaxPotionCountChanged",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (eventField?.GetValue(player) is Action<int> handler)
            {
                handler.Invoke(count);
                MainFile.Logger.Info($"已触发药水最大栏位变化事件：目标栏位={count}。");
                return;
            }

            MainFile.Logger.Info("跳过触发药水最大栏位变化事件：未找到事件处理器。");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"触发药水最大栏位变化事件失败：目标栏位={count}，异常={ex}。");
        }
    }

    /// <summary>
    /// 统计玩家当前药水数量。
    /// </summary>
    private static int CountPotions(Player player)
    {
        return player.Potions.Count();
    }

    /// <summary>
    /// 获取第一个空药水槽位置；没有空位时返回 -1。
    /// </summary>
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

    /// <summary>
    /// 转换药水分类文本，方便玩家按职业和公有查看。
    /// </summary>
    private static string ToCategoryKey(PotionModel potion)
    {
        string poolName = potion.Pool?.GetType().Name ?? "";
        Type? poolType = potion.Pool?.GetType();
        if (poolType != null && ModelDb.AllCharacterPotionPools.Any(pool => pool.GetType() == poolType))
        {
            string characterName = poolName.Replace("PotionPool", "");
            return $"{CategoryCharacterPrefix}{characterName}";
        }

        if (poolName.Contains("Shared", StringComparison.OrdinalIgnoreCase) ||
            poolType == null ||
            !ModelDb.AllCharacterPotionPools.Any(pool => pool.GetType() == poolType))
        {
            return CategorySharedKey;
        }

        return $"{CategorySharedKey}:{poolName}";
    }

    /// <summary>
    /// 转换药水分类文本，方便玩家按职业和公有查看。
    /// </summary>
    private static string ToCategoryText(string categoryKey, PotionModel potion)
    {
        if (categoryKey.Equals(CategoryCharacterKey, StringComparison.OrdinalIgnoreCase))
        {
            return GmUiText.Pick("职业药水（全部角色）", "Character Potions (All Characters)");
        }

        if (IsCharacterCategoryKey(categoryKey))
        {
            string fallbackCharacterName = GetCharacterNameByCategoryKey(categoryKey);
            string characterName = GmLocalizationService.ResolveCharacterNameByPotionPool(potion.Pool, fallbackCharacterName);
            return GmUiText.Pick($"职业药水（{characterName}）", $"Character Potions ({characterName})");
        }

        if (categoryKey.Equals(CategorySharedKey, StringComparison.OrdinalIgnoreCase))
        {
            return GmUiText.Pick("公有药水", "Shared Potions");
        }

        string poolName = potion.Pool?.GetType().Name ?? "";
        string readablePoolName = string.IsNullOrWhiteSpace(poolName) ? GmUiText.Pick("未知", "Unknown") : poolName.Replace("PotionPool", "");
        return GmUiText.Pick($"公有药水（{readablePoolName}）", $"Shared Potions ({readablePoolName})");
    }

    /// <summary>
    /// 转换药水稀有度键值，供筛选和排序使用。
    /// </summary>
    private static string ToRarityKey(string rarity)
    {
        return rarity.ToLowerInvariant();
    }

    /// <summary>
    /// 转换药水稀有度文本。
    /// </summary>
    private static string ToRarityText(string rarityKey)
    {
        return rarityKey switch
        {
            "common" => GmUiText.Pick("普通", "Common"),
            "uncommon" => GmUiText.Pick("罕见", "Uncommon"),
            "rare" => GmUiText.Pick("稀有", "Rare"),
            "special" => GmUiText.Pick("特殊", "Special"),
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
    /// 分类排序权重，让职业药水排在公有药水前面。
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

        if (categoryKey.StartsWith(CategorySharedKey, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 9;
    }

    /// <summary>
    /// 稀有度排序权重，让列表浏览顺序更接近游戏认知。
    /// </summary>
    private static int GetRarityOrder(string rarityKey)
    {
        string[] order = ["common", "uncommon", "rare", "special"];
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
    /// 判断是否为职业药水分类键值。
    /// </summary>
    private static bool IsCharacterCategoryKey(string categoryKey)
    {
        return categoryKey.StartsWith(CategoryCharacterPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 从职业药水分类键值中提取职业名。
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
        return GmLocalizationService.ResolveText(locString, "解析药水本地化文本失败 / Failed to resolve potion localized text");
    }
}
