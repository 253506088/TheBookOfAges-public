using Godot;
using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.GameActions;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.UI;
using System.Reflection;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// 怪物服务，负责读取可召唤怪物目录，并在当前战斗中追加怪物。
/// </summary>
public sealed class MonsterService
{
    private const string AllFilterKey = "all";
    private const string TierMonsterKey = "monster";
    private const string TierEliteKey = "elite";
    private const string TierBossKey = "boss";
    private const string SourceBaseGameKey = "base-game";
    private const string SourceModsKey = "mods";
    private const string GrandMageDadIdKeyword = "GRAND_MAGE_DAD";
    private const string GrandMageDadTypeKeyword = "GrandMageDad";
    private const float ManualLayoutDefaultWidth = 120f;
    private const float ManualLayoutMinWidth = 40f;
    private const float ManualLayoutGap = 28f;
    private static readonly MethodInfo? NCombatRoomEncounterSlotsGetter = typeof(NCombatRoom).GetMethod("get_EncounterSlots", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// 从游戏当前 ModelDb 实时读取所有可用于普通战斗、精英战和 Boss 战的怪物。
    /// </summary>
    public IReadOnlyList<MonsterDisplayInfo> GetMonsters(string keyword, string tierFilterKey, string sourceFilterKey)
    {
        Dictionary<string, HashSet<string>> tierMap = BuildMonsterTierMap();
        string normalizedKeyword = Normalize(keyword);

        List<MonsterModel> catalogMonsters = GetCatalogMonsters()
            .DistinctBy(monster => monster.Id.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();
        List<MonsterDisplayInfo?> displayInfos = catalogMonsters
            .Select(monster => BuildDisplayInfo(monster, tierMap))
            .ToList();
        List<MonsterDisplayInfo> visibleMonsters = displayInfos
            .Where(info => info != null)
            .Select(info => info!)
            .Where(info => MatchesKeyword(info, normalizedKeyword))
            .Where(info => MatchesTierFilter(info.TierKeys, tierFilterKey))
            .Where(info => MatchesSource(info.SourceKey, sourceFilterKey))
            .OrderBy(info => GetTierOrder(info.TierKeys))
            .ThenBy(info => GetSourceOrder(info.SourceKey))
            .ThenBy(info => info.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        LogCatalogDiagnostics("GetMonsters", keyword, tierFilterKey, sourceFilterKey, tierMap, catalogMonsters, displayInfos, visibleMonsters);
        return visibleMonsters;
    }

    /// <summary>
    /// 获取怪物品阶筛选项。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetTierOptions()
    {
        return [
            new GmFilterOption(AllFilterKey, GmUiText.MonsterTierAll),
            new GmFilterOption(TierMonsterKey, GmUiText.MonsterTierNormal),
            new GmFilterOption(TierEliteKey, GmUiText.MonsterTierElite),
            new GmFilterOption(TierBossKey, GmUiText.MonsterTierBoss),
        ];
    }

    /// <summary>
    /// 获取怪物来源筛选项。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetSourceOptions()
    {
        Dictionary<string, HashSet<string>> tierMap = BuildMonsterTierMap();
        List<GmFilterOption> options = [new GmFilterOption(AllFilterKey, GmUiText.Pick("全部来源", "All Sources"))];

        List<MonsterModel> catalogMonsters = GetCatalogMonsters()
            .DistinctBy(monster => monster.Id.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        options.AddRange(catalogMonsters
            .Where(monster => monster != null && tierMap.ContainsKey(monster.Id.ToString()))
            .Select(monster =>
            {
                string sourceKey = ToSourceKey(monster.GetType().Assembly.GetName().Name);
                return new GmFilterOption(sourceKey, ToSourceText(sourceKey, monster.GetType().Assembly.GetName().Name));
            })
            .DistinctBy(option => option.Key, StringComparer.OrdinalIgnoreCase)
            .OrderBy(option => GetSourceOrder(option.Key))
            .ThenBy(option => option.Text, StringComparer.CurrentCultureIgnoreCase));

        if (!options.Any(option => option.Key.Equals(SourceModsKey, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new GmFilterOption(SourceModsKey, "Mods"));
        }

        LogSourceOptionsDiagnostics(tierMap, catalogMonsters, options);
        return options;
    }

    /// <summary>
    /// 判断当前是否处于可召唤怪物的战斗房间。
    /// </summary>
    public bool CanSpawnInCurrentRoom()
    {
        return GmRunContextService.GetFeatureAvailability(GmFeature.SpawnMonster).IsAllowed
            && TryGetCurrentCombatContext(out _, out _, out _);
    }

    /// <summary>
    /// 判断怪物是否推荐在当前房间召唤；非战斗状态不额外标记，由确认按钮负责禁用。
    /// </summary>
    public bool IsMonsterRecommendedInCurrentRoom(MonsterDisplayInfo info)
    {
        if (!TryGetCurrentCombatContext(out _, out CombatRoom? currentRoom, out _))
        {
            return true;
        }

        return IsMonsterAllowedInRoom(GetCurrentRoomType(currentRoom!), info.TierKeys);
    }

    /// <summary>
    /// 获取怪物与当前房间不匹配的提示；匹配或非战斗状态时返回空文本。
    /// </summary>
    public string GetMonsterRoomMismatchHint(MonsterDisplayInfo info)
    {
        if (!TryGetCurrentCombatContext(out _, out CombatRoom? currentRoom, out _))
        {
            return string.Empty;
        }

        RoomType? roomType = GetCurrentRoomType(currentRoom!);
        if (IsMonsterAllowedInRoom(roomType, info.TierKeys))
        {
            return string.Empty;
        }

        return GmUiText.MonsterRoomMismatchHint(ToRoomTypeText(roomType), info.TierText);
    }

    /// <summary>
    /// 在当前战斗中召唤选中的怪物。
    /// </summary>
    public async Task<MonsterSpawnResult> SpawnMonstersAsync(IEnumerable<MonsterDisplayInfo> selectedMonsters, bool allowRiskyCrossRoomSummon)
    {
        MonsterSpawnResult result = new();
        List<MonsterDisplayInfo> monsterList = selectedMonsters.ToList();
        GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(GmFeature.SpawnMonster);
        if (!availability.IsAllowed)
        {
            result.FailedCount += monsterList.Count;
            result.Messages.Add(availability.Reason);
            MainFile.Logger.Info($"刷怪笼召唤已被多人安全闸门阻止：选择数量={monsterList.Count}，原因={availability.Reason}。");
            return result;
        }

        if (!TryGetCurrentCombatContext(out _, out CombatRoom? currentRoom, out CombatState? combatState))
        {
            result.FailedCount++;
            result.Messages.Add(GmUiText.MonsterNotInCombat);
            return result;
        }

        if (GmRunContextService.IsMultiplayer())
        {
            return await SubmitMonsterSpawnActionAsync(monsterList, currentRoom!, allowRiskyCrossRoomSummon);
        }

        foreach (MonsterDisplayInfo info in monsterList)
        {
            string mismatchHint = GetMonsterRoomMismatchHint(currentRoom!, info);
            if (!allowRiskyCrossRoomSummon && !string.IsNullOrWhiteSpace(mismatchHint))
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.MonsterSpawnFailedNamed, info.Name));
                MainFile.Logger.Info($"刷怪笼安全模式跳过跨房间召唤：名称={info.Name}，ID={info.IdText}，原因={mismatchHint}");
                continue;
            }

            if (allowRiskyCrossRoomSummon && !string.IsNullOrWhiteSpace(mismatchHint))
            {
                MainFile.Logger.Info($"刷怪笼高风险模式允许跨房间召唤：名称={info.Name}，ID={info.IdText}，风险={mismatchHint}");
            }

            bool success = await TrySpawnMonsterAsync(currentRoom!, combatState!, info);
            if (success)
            {
                result.SuccessCount++;
                result.Messages.Add(string.Format(GmUiText.MonsterSpawnSuccessNamed, info.Name));
            }
            else
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.MonsterSpawnFailedNamed, info.Name));
            }
        }

        return result;
    }

    /// <summary>
    /// 多人模式下提交怪物召唤同步动作，不在服务层直接改本机战斗状态。
    /// </summary>
    /// <param name="monsterList">玩家选择的怪物列表。</param>
    /// <param name="currentRoom">当前战斗房间。</param>
    /// <param name="allowRiskyCrossRoomSummon">是否允许跨房间类型召唤。</param>
    private static async Task<MonsterSpawnResult> SubmitMonsterSpawnActionAsync(IReadOnlyList<MonsterDisplayInfo> monsterList, CombatRoom currentRoom, bool allowRiskyCrossRoomSummon)
    {
        MonsterSpawnResult result = new();
        Player? player = GmRunContextService.GetCurrentPlayer();
        if (player == null)
        {
            result.FailedCount += monsterList.Count;
            result.Messages.Add(GmUiText.MultiplayerLocalPlayerMissing);
            MainFile.Logger.Info($"刷怪笼多人同步提交失败：选择数量={monsterList.Count}，原因=当前玩家为空。");
            return result;
        }

        List<MonsterDisplayInfo> acceptedMonsters = [];
        foreach (MonsterDisplayInfo info in monsterList)
        {
            string mismatchHint = GetMonsterRoomMismatchHint(currentRoom, info);
            if (!allowRiskyCrossRoomSummon && !string.IsNullOrWhiteSpace(mismatchHint))
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.MonsterSpawnFailedNamed, info.Name));
                MainFile.Logger.Info($"刷怪笼多人安全模式跳过跨房间召唤：名称={info.Name}，ID={info.IdText}，原因={mismatchHint}。");
                continue;
            }

            if (allowRiskyCrossRoomSummon && !string.IsNullOrWhiteSpace(mismatchHint))
            {
                MainFile.Logger.Info($"刷怪笼多人高风险模式允许跨房间召唤：名称={info.Name}，ID={info.IdText}，风险={mismatchHint}。");
            }

            acceptedMonsters.Add(info);
        }

        if (acceptedMonsters.Count == 0)
        {
            return result;
        }

        List<string> monsterIds = acceptedMonsters.Select(info => info.IdText).ToList();
        bool submitted = await GmNetworkCommandService.TryEnqueueGameActionAsync(new GmMonsterSpawnAction(player, monsterIds), "怪物召唤");
        foreach (MonsterDisplayInfo info in acceptedMonsters)
        {
            if (submitted)
            {
                result.SuccessCount++;
                result.Messages.Add(string.Format(GmUiText.MonsterSpawnSubmittedNamed, info.Name));
            }
            else
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.MonsterSpawnFailedNamed, info.Name));
            }
        }

        MainFile.Logger.Info($"刷怪笼多人同步提交结束：提交成功={submitted}，提交数量={acceptedMonsters.Count}。");
        return result;
    }

    /// <summary>
    /// 构建怪物品阶映射，按遭遇战类型反推怪物属于普通、精英还是 Boss。
    /// </summary>
    private static Dictionary<string, HashSet<string>> BuildMonsterTierMap()
    {
        Dictionary<string, HashSet<string>> tierMap = new(StringComparer.OrdinalIgnoreCase);

        foreach (EncounterModel encounter in ModelDb.AllEncounters.Where(IsSupportedEncounter))
        {
            AddEncounterMonstersToTierMap(tierMap, encounter);
        }

        foreach (EncounterModel encounter in CustomContentDictionary.CustomEncounters.Where(IsSupportedEncounter))
        {
            AddEncounterMonstersToTierMap(tierMap, encounter);
        }

        LogTierMapDiagnostics(tierMap);
        return tierMap;
    }

    /// <summary>
    /// 获取刷怪笼可展示的怪物候选；部分 BaseLib 自定义怪物不一定进入 ModelDb.Monsters，但会出现在自定义遭遇里。
    /// </summary>
    /// <returns>怪物候选列表。</returns>
    private static IEnumerable<MonsterModel> GetCatalogMonsters()
    {
        return ModelDb.Monsters
            .OfType<MonsterModel>()
            .Concat(GetSupportedEncounterMonsters());
    }

    /// <summary>
    /// 从所有支持的遭遇里读取怪物候选，兼容不进入正式楼层池的独立自定义 Boss。
    /// </summary>
    /// <returns>遭遇里声明的怪物列表。</returns>
    private static IEnumerable<MonsterModel> GetSupportedEncounterMonsters()
    {
        foreach (EncounterModel encounter in ModelDb.AllEncounters.Where(IsSupportedEncounter))
        {
            foreach (MonsterModel monster in SafeGetEncounterMonsters(encounter, "ModelDb.AllEncounters"))
            {
                yield return monster;
            }
        }

        foreach (EncounterModel encounter in CustomContentDictionary.CustomEncounters.Where(IsSupportedEncounter))
        {
            foreach (MonsterModel monster in SafeGetEncounterMonsters(encounter, "BaseLib.CustomEncounters"))
            {
                yield return monster;
            }
        }
    }

    /// <summary>
    /// 把指定遭遇里的全部怪物写入品阶映射；自定义独立 Boss 可能不在 ModelDb.AllEncounters 中，也需要能被 GM 面板搜到。
    /// </summary>
    /// <param name="tierMap">怪物 ID 到品阶列表的映射。</param>
    /// <param name="encounter">用于反推怪物品阶的遭遇。</param>
    private static void AddEncounterMonstersToTierMap(Dictionary<string, HashSet<string>> tierMap, EncounterModel encounter)
    {
        string tierKey = ToTierKey(encounter.RoomType);
        foreach (MonsterModel monster in SafeGetEncounterMonsters(encounter, "BuildMonsterTierMap"))
        {
            string idText = monster.Id.ToString();
            if (!tierMap.TryGetValue(idText, out HashSet<string>? tierKeys))
            {
                tierKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                tierMap[idText] = tierKeys;
            }

            tierKeys.Add(tierKey);
        }
    }

    /// <summary>
    /// 安全读取遭遇内声明的怪物列表；读取失败时写清楚是哪一个遭遇炸了，避免刷怪笼静默漏怪。
    /// </summary>
    /// <param name="encounter">待读取的遭遇。</param>
    /// <param name="source">调用来源。</param>
    /// <returns>成功读取到的怪物列表。</returns>
    private static IReadOnlyList<MonsterModel> SafeGetEncounterMonsters(EncounterModel encounter, string source)
    {
        try
        {
            List<MonsterModel> monsters = encounter.AllPossibleMonsters
                .Where(monster => monster != null)
                .ToList();

            if (IsGrandMageDadEncounter(encounter) || monsters.Any(IsGrandMageDadMonster))
            {
                MainFile.Logger.Info($"[刷怪笼诊断] 读取遭遇怪物成功：来源={source}，遭遇={DescribeEncounter(encounter)}，怪物数量={monsters.Count}，怪物={string.Join(", ", monsters.Select(DescribeMonster))}。");
            }

            return monsters;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"[刷怪笼诊断] 读取遭遇怪物失败：来源={source}，遭遇={DescribeEncounter(encounter)}，异常类型={ex.GetType().Name}，异常={ex.Message}。");
            return [];
        }
    }

    /// <summary>
    /// 记录品阶映射诊断，重点确认大法师老爹有没有被映射成 Boss。
    /// </summary>
    /// <param name="tierMap">怪物品阶映射。</param>
    private static void LogTierMapDiagnostics(Dictionary<string, HashSet<string>> tierMap)
    {
        int customEncounterCount = CustomContentDictionary.CustomEncounters.Count;
        List<EncounterModel> dadEncounters = CustomContentDictionary.CustomEncounters
            .Where(IsGrandMageDadEncounter)
            .Cast<EncounterModel>()
            .ToList();
        List<string> dadTierEntries = tierMap
            .Where(pair => IsGrandMageDadText(pair.Key))
            .Select(pair => $"{pair.Key}=>{string.Join("/", pair.Value)}")
            .ToList();

        MainFile.Logger.Info($"[刷怪笼诊断] 品阶映射完成：ModelDb遭遇数={ModelDb.AllEncounters.Count()}，BaseLib自定义遭遇数={customEncounterCount}，老爹自定义遭遇数={dadEncounters.Count}，老爹品阶映射={FormatList(dadTierEntries)}。");
    }

    /// <summary>
    /// 记录怪物列表诊断，确认候选、展示和最终过滤结果中是否存在大法师老爹。
    /// </summary>
    /// <param name="caller">调用入口。</param>
    /// <param name="keyword">搜索关键字。</param>
    /// <param name="tierFilterKey">品阶筛选。</param>
    /// <param name="sourceFilterKey">来源筛选。</param>
    /// <param name="tierMap">品阶映射。</param>
    /// <param name="catalogMonsters">候选怪物。</param>
    /// <param name="displayInfos">展示转换结果。</param>
    /// <param name="visibleMonsters">最终可见怪物。</param>
    private static void LogCatalogDiagnostics(
        string caller,
        string keyword,
        string tierFilterKey,
        string sourceFilterKey,
        Dictionary<string, HashSet<string>> tierMap,
        IReadOnlyList<MonsterModel> catalogMonsters,
        IReadOnlyList<MonsterDisplayInfo?> displayInfos,
        IReadOnlyList<MonsterDisplayInfo> visibleMonsters)
    {
        List<MonsterModel> dadCatalog = catalogMonsters.Where(IsGrandMageDadMonster).ToList();
        List<MonsterDisplayInfo> dadDisplay = displayInfos.Where(info => info != null && IsGrandMageDadText(info.IdText)).Select(info => info!).ToList();
        List<MonsterDisplayInfo> dadVisible = visibleMonsters.Where(info => IsGrandMageDadText(info.IdText) || IsGrandMageDadText(info.Name)).ToList();
        List<string> dadTierEntries = tierMap
            .Where(pair => IsGrandMageDadText(pair.Key))
            .Select(pair => $"{pair.Key}=>{string.Join("/", pair.Value)}")
            .ToList();

        MainFile.Logger.Info($"[刷怪笼诊断] {caller}：keyword={keyword ?? ""}，tier={tierFilterKey ?? ""}，source={sourceFilterKey ?? ""}，ModelDb怪物数={ModelDb.Monsters.Count()}，候选去重后={catalogMonsters.Count}，可构建展示={displayInfos.Count(info => info != null)}，最终可见={visibleMonsters.Count}，老爹候选={FormatMonsters(dadCatalog)}，老爹展示={FormatDisplayInfos(dadDisplay)}，老爹可见={FormatDisplayInfos(dadVisible)}，老爹品阶={FormatList(dadTierEntries)}。");
    }

    /// <summary>
    /// 记录来源筛选项诊断，确认 Mods 来源是否真的包含大法师老爹来源。
    /// </summary>
    /// <param name="tierMap">品阶映射。</param>
    /// <param name="catalogMonsters">候选怪物。</param>
    /// <param name="options">来源筛选项。</param>
    private static void LogSourceOptionsDiagnostics(Dictionary<string, HashSet<string>> tierMap, IReadOnlyList<MonsterModel> catalogMonsters, IReadOnlyList<GmFilterOption> options)
    {
        List<MonsterModel> dadCatalog = catalogMonsters.Where(IsGrandMageDadMonster).ToList();
        MainFile.Logger.Info($"[刷怪笼诊断] 来源筛选项：候选去重后={catalogMonsters.Count}，来源项={string.Join(", ", options.Select(option => $"{option.Key}:{option.Text}"))}，老爹候选={FormatMonsters(dadCatalog)}，老爹有品阶={dadCatalog.Any(monster => tierMap.ContainsKey(monster.Id.ToString()))}。");
    }

    /// <summary>
    /// 判断遭遇是否疑似大法师老爹遭遇。
    /// </summary>
    /// <param name="encounter">遭遇模型。</param>
    /// <returns>命中返回 true。</returns>
    private static bool IsGrandMageDadEncounter(EncounterModel encounter)
    {
        return IsGrandMageDadText(encounter.GetType().FullName) ||
               IsGrandMageDadText(encounter.Id.ToString());
    }

    /// <summary>
    /// 判断怪物是否疑似大法师老爹。
    /// </summary>
    /// <param name="monster">怪物模型。</param>
    /// <returns>命中返回 true。</returns>
    private static bool IsGrandMageDadMonster(MonsterModel monster)
    {
        return IsGrandMageDadText(monster.GetType().FullName) ||
               IsGrandMageDadText(monster.Id.ToString());
    }

    /// <summary>
    /// 判断文本是否命中大法师老爹常见标识。
    /// </summary>
    /// <param name="text">待检查文本。</param>
    /// <returns>命中返回 true。</returns>
    private static bool IsGrandMageDadText(string? text)
    {
        return !string.IsNullOrWhiteSpace(text) &&
               (text.Contains(GrandMageDadIdKeyword, StringComparison.OrdinalIgnoreCase) ||
                text.Contains(GrandMageDadTypeKeyword, StringComparison.OrdinalIgnoreCase) ||
                text.Contains("大法师老爹", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 格式化遭遇信息，方便从日志里直接定位来源。
    /// </summary>
    /// <param name="encounter">遭遇模型。</param>
    /// <returns>遭遇诊断文本。</returns>
    private static string DescribeEncounter(EncounterModel encounter)
    {
        return $"{encounter.GetType().FullName}/Id={encounter.Id}/RoomType={encounter.RoomType}";
    }

    /// <summary>
    /// 格式化怪物信息，方便从日志里直接定位来源。
    /// </summary>
    /// <param name="monster">怪物模型。</param>
    /// <returns>怪物诊断文本。</returns>
    private static string DescribeMonster(MonsterModel monster)
    {
        return $"{monster.GetType().FullName}/Id={monster.Id}/Source={monster.GetType().Assembly.GetName().Name}";
    }

    /// <summary>
    /// 格式化怪物列表。
    /// </summary>
    /// <param name="monsters">怪物列表。</param>
    /// <returns>诊断文本。</returns>
    private static string FormatMonsters(IReadOnlyList<MonsterModel> monsters)
    {
        return monsters.Count == 0 ? "无" : string.Join(" | ", monsters.Select(DescribeMonster));
    }

    /// <summary>
    /// 格式化展示列表。
    /// </summary>
    /// <param name="infos">展示列表。</param>
    /// <returns>诊断文本。</returns>
    private static string FormatDisplayInfos(IReadOnlyList<MonsterDisplayInfo> infos)
    {
        return infos.Count == 0 ? "无" : string.Join(" | ", infos.Select(info => $"{info.Name}/Id={info.IdText}/Tier={info.TierText}/Source={info.SourceKey}"));
    }

    /// <summary>
    /// 格式化普通字符串列表。
    /// </summary>
    /// <param name="values">字符串列表。</param>
    /// <returns>诊断文本。</returns>
    private static string FormatList(IReadOnlyList<string> values)
    {
        return values.Count == 0 ? "无" : string.Join(" | ", values);
    }

    /// <summary>
    /// 把游戏怪物模型转换成页面需要的简单字段；没有品阶信息的怪物先不展示。
    /// </summary>
    private static MonsterDisplayInfo? BuildDisplayInfo(MonsterModel monster, Dictionary<string, HashSet<string>> tierMap)
    {
        string idText = monster.Id.ToString();
        if (!tierMap.TryGetValue(idText, out HashSet<string>? tierKeys) || tierKeys.Count == 0)
        {
            return null;
        }

        List<string> orderedTierKeys = tierKeys
            .OrderBy(GetTierOrder)
            .ToList();
        string sourceKey = ToSourceKey(monster.GetType().Assembly.GetName().Name);
        return new MonsterDisplayInfo
        {
            Id = monster.Id,
            IdText = idText,
            Name = SafeText(GetLocalizedText(monster.Title), idText),
            TierKeys = orderedTierKeys,
            TierText = string.Join(" / ", orderedTierKeys.Select(ToTierText)),
            SourceKey = sourceKey,
            Source = ToSourceText(sourceKey, monster.GetType().Assembly.GetName().Name),
            MinHp = monster.MinInitialHp,
            MaxHp = monster.MaxInitialHp,
            // 怪物预览图改由 UI 层尝试创建动态可视节点；这里不再读取 AssetPaths，
            // 避免某些规范实例在取资源路径时触发状态机初始化导致页签打开即崩。
            PreviewTexture = null,
            Monster = monster,
        };
    }

    /// <summary>
    /// 判断怪物名称或 ID 是否匹配搜索关键字。
    /// </summary>
    private static bool MatchesKeyword(MonsterDisplayInfo info, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return Normalize(info.Name).Contains(keyword) ||
               Normalize(info.IdText).Contains(keyword);
    }

    /// <summary>
    /// 判断怪物品阶筛选是否命中，“全部”表示不过滤。
    /// </summary>
    private static bool MatchesTierFilter(IReadOnlyList<string> tierKeys, string filterKey)
    {
        return string.IsNullOrWhiteSpace(filterKey) ||
               filterKey.Equals(AllFilterKey, StringComparison.OrdinalIgnoreCase) ||
               tierKeys.Any(key => key.Equals(filterKey, StringComparison.OrdinalIgnoreCase));
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
    /// 判断当前遭遇战是否属于刷怪笼可用的普通、精英或 Boss 战。
    /// </summary>
    private static bool IsSupportedEncounter(EncounterModel encounter)
    {
        return encounter.RoomType == RoomType.Monster ||
               encounter.RoomType == RoomType.Elite ||
               encounter.RoomType == RoomType.Boss;
    }

    /// <summary>
    /// 尝试读取当前战斗上下文；当前不在有效战斗房间时返回 false。
    /// </summary>
    internal static bool TryGetCurrentCombatContext(out RunState? runState, out CombatRoom? currentRoom, out CombatState? combatState)
    {
        runState = null;
        currentRoom = null;
        combatState = null;

        RunManager? manager = RunManager.Instance;
        CombatManager? combatManager = CombatManager.Instance;
        if (manager == null ||
            combatManager == null ||
            !manager.IsInProgress ||
            manager.IsGameOver ||
            !combatManager.IsInProgress ||
            combatManager.IsOverOrEnding)
        {
            return false;
        }

        runState = manager.DebugOnlyGetState();
        currentRoom = runState?.CurrentRoom as CombatRoom;
        combatState = currentRoom?.CombatState ?? combatManager.DebugOnlyGetState();
        return currentRoom != null && combatState != null;
    }

    /// <summary>
    /// 获取当前战斗房间的原生房间类型，读取失败时返回空并写日志。
    /// </summary>
    private static RoomType? GetCurrentRoomType(CombatRoom currentRoom)
    {
        try
        {
            return currentRoom.Encounter?.RoomType;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"刷怪笼读取当前房间类型失败：异常={ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 获取怪物与指定房间不匹配的提示；匹配时返回空文本。
    /// </summary>
    private static string GetMonsterRoomMismatchHint(CombatRoom currentRoom, MonsterDisplayInfo info)
    {
        RoomType? roomType = GetCurrentRoomType(currentRoom);
        if (IsMonsterAllowedInRoom(roomType, info.TierKeys))
        {
            return string.Empty;
        }

        return GmUiText.MonsterRoomMismatchHint(ToRoomTypeText(roomType), info.TierText);
    }

    /// <summary>
    /// 判断怪物品阶是否适合指定房间；普通怪允许进入所有战斗房，精英和 Boss 只允许进入对应房间。
    /// </summary>
    private static bool IsMonsterAllowedInRoom(RoomType? roomType, IReadOnlyList<string> tierKeys)
    {
        if (tierKeys.Any(key => key.Equals(TierMonsterKey, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (roomType == RoomType.Elite && tierKeys.Any(key => key.Equals(TierEliteKey, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (roomType == RoomType.Boss && tierKeys.Any(key => key.Equals(TierBossKey, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 把房间类型转换成可读文本，供安全召唤提示使用。
    /// </summary>
    private static string ToRoomTypeText(RoomType? roomType)
    {
        return roomType switch
        {
            RoomType.Monster => GmUiText.MonsterTierNormal,
            RoomType.Elite => GmUiText.MonsterTierElite,
            RoomType.Boss => GmUiText.MonsterTierBoss,
            null => GmUiText.Pick("未知房间", "Unknown Room"),
            _ => roomType.ToString()!,
        };
    }

    /// <summary>
    /// 按怪物 ID 在当前战斗里召唤怪物，供多人同步动作执行端复用单人召唤逻辑。
    /// </summary>
    /// <param name="currentRoom">当前战斗房间。</param>
    /// <param name="combatState">当前战斗状态。</param>
    /// <param name="monsterId">怪物 ModelId 字符串。</param>
    internal static async Task<bool> TrySpawnMonsterByIdAsync(CombatRoom currentRoom, CombatState combatState, string monsterId)
    {
        MonsterModel? monster = FindMonsterById(monsterId);
        if (monster == null)
        {
            MainFile.Logger.Info($"刷怪笼多人同步召唤跳过未知怪物：ID={monsterId}。");
            return false;
        }

        string idText = monster.Id.ToString();
        string? assemblyName = monster.GetType().Assembly.GetName().Name;
        string sourceKey = ToSourceKey(assemblyName);
        MonsterDisplayInfo info = new()
        {
            Id = monster.Id,
            IdText = idText,
            Name = SafeText(GetLocalizedText(monster.Title), idText),
            TierKeys = [],
            TierText = string.Empty,
            SourceKey = sourceKey,
            Source = ToSourceText(sourceKey, assemblyName),
            MinHp = monster.MinInitialHp,
            MaxHp = monster.MaxInitialHp,
            PreviewTexture = null,
            Monster = monster,
        };

        return await TrySpawnMonsterAsync(currentRoom, combatState, info);
    }

    /// <summary>
    /// 按字符串 ID 查找怪物模型。
    /// </summary>
    /// <param name="monsterId">怪物 ModelId 字符串。</param>
    private static MonsterModel? FindMonsterById(string monsterId)
    {
        return ModelDb.Monsters.FirstOrDefault(monster =>
            monster != null &&
            string.Equals(monster.Id.ToString(), monsterId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 使用游戏命令对象把怪物追加到当前战斗中，并尽量复用遭遇战自己的槽位安排。
    /// </summary>
    private static async Task<bool> TrySpawnMonsterAsync(CombatRoom currentRoom, CombatState combatState, MonsterDisplayInfo info)
    {
        try
        {
            int beforeCount = combatState.Enemies.Count;
            bool hasEncounterSlots = HasEncounterSlots();
            string? slotName = hasEncounterSlots ? GetSpawnSlotName(currentRoom, combatState) : null;
            if (!hasEncounterSlots)
            {
                MainFile.Logger.Info($"刷怪笼当前战斗未提供 EncounterSlots，改走无槽位召唤并在召唤后手动摆位：名称={info.Name}，ID={info.IdText}。");
            }

            MainFile.Logger.Info($"刷怪笼开始召唤怪物：名称={info.Name}，ID={info.IdText}，槽位={slotName ?? "无槽位(手动摆位)"}，召唤前敌人数={beforeCount}。");

            Creature? spawned = await CreatureCmd.Add(info.Monster.ToMutable(), combatState, CombatSide.Enemy, slotName!);
            if (spawned != null)
            {
                TryRepairUnsetMoves(combatState);

                if (!hasEncounterSlots || string.IsNullOrWhiteSpace(slotName))
                {
                    TryLayoutEnemiesWithoutSlots(combatState);
                }
            }

            bool success = spawned != null && combatState.Enemies.Count > beforeCount;
            MainFile.Logger.Info($"刷怪笼召唤怪物结束：名称={info.Name}，ID={info.IdText}，槽位={slotName ?? "无槽位(手动摆位)"}，success={success}，召唤后敌人数={combatState.Enemies.Count}。");
            return success;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"刷怪笼召唤怪物失败：名称={info.Name}，ID={info.IdText}，异常={ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 判断当前战斗节点是否存在 EncounterSlots；存在时才允许使用槽位名定位。
    /// </summary>
    private static bool HasEncounterSlots()
    {
        try
        {
            if (NCombatRoomEncounterSlotsGetter == null)
            {
                return false;
            }

            NCombatRoom? combatRoomNode = NCombatRoom.Instance;
            if (combatRoomNode == null)
            {
                return false;
            }

            return NCombatRoomEncounterSlotsGetter.Invoke(combatRoomNode, null) is Control;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"刷怪笼检测 EncounterSlots 失败，按无槽位处理：异常={ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 获取怪物的目标槽位名；优先使用当前遭遇战自己的槽位规则，拿不到时返回空并走手动摆位。
    /// </summary>
    private static string? GetSpawnSlotName(CombatRoom currentRoom, CombatState combatState)
    {
        try
        {
            string? slotName = currentRoom.Encounter?.GetNextSlot(combatState);
            if (!string.IsNullOrWhiteSpace(slotName))
            {
                return slotName;
            }

            MainFile.Logger.Info("刷怪笼未拿到可用槽位名，改走无槽位召唤并手动摆位。");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"刷怪笼读取怪物槽位失败，改走无槽位召唤并手动摆位：异常={ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 修复当前战斗里所有敌人的 UNSET_MOVE，避免结束回合时报 No move has been set。
    /// </summary>
    private static void TryRepairUnsetMoves(CombatState combatState)
    {
        try
        {
            IEnumerable<Creature> playerTargets = combatState.Players.Select(player => player.Creature);
            int repairedCount = 0;

            foreach (Creature enemy in combatState.Enemies.Where(enemy => enemy != null && enemy.IsAlive))
            {
                if (enemy.Monster == null || !IsUnsetMove(enemy.Monster.NextMove))
                {
                    continue;
                }

                enemy.PrepareForNextTurn(playerTargets, true);
                if (!IsUnsetMove(enemy.Monster.NextMove))
                {
                    repairedCount++;
                    continue;
                }

                enemy.Monster.RollMove(playerTargets);
                if (!IsUnsetMove(enemy.Monster.NextMove))
                {
                    repairedCount++;
                }
            }

            if (repairedCount > 0)
            {
                MainFile.Logger.Info($"刷怪笼已修复 UNSET_MOVE 敌人：数量={repairedCount}。");
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"刷怪笼修复 UNSET_MOVE 失败：异常={ex.Message}");
        }
    }

    /// <summary>
    /// 判断怪物动作是否处于 UNSET_MOVE 状态。
    /// </summary>
    private static bool IsUnsetMove(MoveState? move)
    {
        if (move == null)
        {
            return true;
        }

        return string.Equals(move.StateId, "UNSET_MOVE", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(move.Id, "UNSET_MOVE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 在 EncounterSlots 为空时，按当前敌人节点宽度手动横向摆位，尽量避免重叠。
    /// </summary>
    private static void TryLayoutEnemiesWithoutSlots(CombatState combatState)
    {
        try
        {
            NCombatRoom? combatRoomNode = NCombatRoom.Instance;
            if (combatRoomNode == null)
            {
                return;
            }

            List<(NCreature Node, float Width)> enemyNodes = [];
            foreach (Creature enemy in combatState.Enemies.Where(enemy => enemy != null && enemy.IsAlive))
            {
                NCreature? node = combatRoomNode.GetCreatureNode(enemy);
                if (node == null || !GodotObject.IsInstanceValid(node))
                {
                    continue;
                }

                float width = node.Visuals?.Bounds?.Size.X ?? ManualLayoutDefaultWidth;
                if (!float.IsFinite(width) || width < ManualLayoutMinWidth)
                {
                    width = ManualLayoutDefaultWidth;
                }

                enemyNodes.Add((node, width));
            }

            if (enemyNodes.Count <= 1)
            {
                return;
            }

            List<(NCreature Node, float Width)> orderedNodes = enemyNodes
                .OrderBy(item => item.Node.GlobalPosition.X)
                .ToList();

            float centerX = orderedNodes.Average(item => item.Node.GlobalPosition.X);
            float centerY = orderedNodes.Average(item => item.Node.GlobalPosition.Y);
            float totalWidth = orderedNodes.Sum(item => item.Width) + ManualLayoutGap * (orderedNodes.Count - 1);
            float cursorX = centerX - totalWidth * 0.5f;

            foreach ((NCreature node, float width) in orderedNodes)
            {
                float halfWidth = width * 0.5f;
                cursorX += halfWidth;
                node.GlobalPosition = new Vector2(cursorX, centerY);
                cursorX += halfWidth + ManualLayoutGap;
            }

            MainFile.Logger.Info($"刷怪笼无槽位手动摆位完成：敌人数={orderedNodes.Count}，总宽={totalWidth:0.##}。");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"刷怪笼无槽位手动摆位失败：异常={ex.Message}");
        }
    }

    /// <summary>
    /// 转换怪物品阶键值，供筛选和排序使用。
    /// </summary>
    private static string ToTierKey(RoomType roomType)
    {
        return roomType switch
        {
            RoomType.Monster => TierMonsterKey,
            RoomType.Elite => TierEliteKey,
            RoomType.Boss => TierBossKey,
            _ => roomType.ToString().ToLowerInvariant(),
        };
    }

    /// <summary>
    /// 转换怪物品阶展示文本。
    /// </summary>
    private static string ToTierText(string tierKey)
    {
        return tierKey switch
        {
            TierMonsterKey => GmUiText.MonsterTierNormal,
            TierEliteKey => GmUiText.MonsterTierElite,
            TierBossKey => GmUiText.MonsterTierBoss,
            _ => tierKey,
        };
    }

    /// <summary>
    /// 返回怪物品阶排序权重；多品阶怪物按最靠前的品阶排序。
    /// </summary>
    private static int GetTierOrder(IReadOnlyList<string> tierKeys)
    {
        return tierKeys.Count == 0 ? 99 : tierKeys.Min(GetTierOrder);
    }

    /// <summary>
    /// 返回单个怪物品阶的排序权重。
    /// </summary>
    private static int GetTierOrder(string tierKey)
    {
        return tierKey switch
        {
            TierMonsterKey => 0,
            TierEliteKey => 1,
            TierBossKey => 2,
            _ => 99,
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
        return GmLocalizationService.ResolveText(locString, "解析怪物本地化文本失败 / Failed to resolve monster localized text");
    }
}
