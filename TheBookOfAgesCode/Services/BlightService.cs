using System.Collections;
using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.UI;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// 荒疫服务，负责读取游戏 ModifierModel 并对当前局内修饰器执行添加和移除。
/// </summary>
public sealed class BlightService
{
    private const string AllFilterKey = "all";
    private const string CategoryGoodKey = "good";
    private const string CategoryBadKey = "bad";
    private const string CategoryOtherKey = "other";
    private const string SourceBaseGameKey = "base-game";
    private const string SourceModsKey = "mods";

    /// <summary>
    /// 从游戏当前 ModelDb 实时读取所有荒疫候选项，并转换成页面展示数据。
    /// </summary>
    public IReadOnlyList<BlightDisplayInfo> GetBlights(string keyword, string categoryFilterKey, string sourceFilterKey)
    {
        RunState? runState = GetCurrentRunState();
        if (!GmRunContextService.IsMultiplayer())
        {
            TryRepairCurrentRunModifiers(runState);
        }

        IReadOnlyList<ModifierModel> enabledModifiers = runState?.Modifiers ?? [];
        string normalizedKeyword = Normalize(keyword);
        return GetAllModifiers()
            .Select(modifier => BuildDisplayInfo(modifier, enabledModifiers))
            .Where(info => MatchesKeyword(info, normalizedKeyword))
            .Where(info => MatchesFilter(info.CategoryKey, categoryFilterKey))
            .Where(info => MatchesFilter(info.SourceKey, sourceFilterKey))
            .OrderBy(info => GetCategoryOrder(info.CategoryKey))
            .ThenBy(info => GetSourceOrder(info.SourceKey))
            .ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 获取荒疫分类筛选项。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetCategoryOptions()
    {
        return [
            new GmFilterOption(AllFilterKey, GmUiText.Pick("全部荒疫", "All Blights")),
            new GmFilterOption(CategoryGoodKey, GmUiText.Pick("增益选项", "Good Options")),
            new GmFilterOption(CategoryBadKey, GmUiText.Pick("负面选项", "Bad Options")),
            new GmFilterOption(CategoryOtherKey, GmUiText.Pick("其他选项", "Other Options")),
        ];
    }

    /// <summary>
    /// 获取荒疫来源筛选项。
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
    /// 判断当前是否在可操作的游戏局内。
    /// </summary>
    public bool CanOperateBlights()
    {
        return GmRunContextService.GetFeatureAvailability(GmFeature.ToggleBlight).IsAllowed
            && GetCurrentRunState() != null;
    }

    /// <summary>
    /// 读取当前局内已启用的荒疫数量。
    /// </summary>
    public int GetEnabledBlightCount()
    {
        if (!GmRunContextService.IsMultiplayer())
        {
            TryRepairCurrentRunModifiers(GetCurrentRunState());
        }

        return GetCurrentModifiers().Count;
    }

    /// <summary>
    /// 批量添加荒疫到当前局内。
    /// </summary>
    public Task<BlightApplyResult> AddBlightsAsync(IEnumerable<BlightDisplayInfo> selectedBlights)
    {
        BlightApplyResult result = new();
        List<BlightDisplayInfo> blights = selectedBlights.ToList();
        GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(GmFeature.ToggleBlight);
        if (!availability.IsAllowed)
        {
            result.FailedCount += blights.Count;
            result.Messages.Add(availability.Reason);
            MainFile.Logger.Info($"[荒疫] 添加已被多人安全闸门阻止：选择数量={blights.Count}，原因={availability.Reason}。");
            return Task.FromResult(result);
        }

        RunState? runState = GetCurrentRunState();
        if (runState == null)
        {
            result.FailedCount++;
            result.Messages.Add(GmUiText.BlightNotInRun);
            MainFile.Logger.Info($"[荒疫] 添加失败：当前不在有效局内，选择数量={blights.Count}。");
            return Task.FromResult(result);
        }

        TryRepairCurrentRunModifiers(runState);

        foreach (BlightDisplayInfo blight in blights)
        {
            if (IsModifierEnabled(runState, blight.Modifier))
            {
                result.SkippedCount++;
                result.Messages.Add(string.Format(GmUiText.BlightAlreadyEnabledNamed, blight.Name));
                continue;
            }

            try
            {
                ModifierModel mutableModifier = CreateRunnableModifier(blight.Modifier, runState);
                runState.AddModifierDebug(mutableModifier);
                result.SuccessCount++;
                result.Messages.Add(string.Format(GmUiText.BlightAddSuccessNamed, blight.Name));
                MainFile.Logger.Info($"[荒疫] 添加成功：名称={blight.Name}，ID={blight.IdText}，已使用已初始化的 mutable 副本。");
            }
            catch (Exception ex)
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.BlightAddFailedNamed, blight.Name));
                MainFile.Logger.Info($"[荒疫] 添加异常：名称={blight.Name}，ID={blight.IdText}，异常={ex.Message}。");
            }
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// 批量从当前局内移除荒疫。
    /// </summary>
    public Task<BlightApplyResult> RemoveBlightsAsync(IEnumerable<BlightDisplayInfo> selectedBlights)
    {
        BlightApplyResult result = new();
        List<BlightDisplayInfo> blights = selectedBlights.ToList();
        GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(GmFeature.ToggleBlight);
        if (!availability.IsAllowed)
        {
            result.FailedCount += blights.Count;
            result.Messages.Add(availability.Reason);
            MainFile.Logger.Info($"[荒疫] 移除已被多人安全闸门阻止：选择数量={blights.Count}，原因={availability.Reason}。");
            return Task.FromResult(result);
        }

        RunState? runState = GetCurrentRunState();
        if (runState == null)
        {
            result.FailedCount++;
            result.Messages.Add(GmUiText.BlightNotInRun);
            MainFile.Logger.Info($"[荒疫] 移除失败：当前不在有效局内，选择数量={blights.Count}。");
            return Task.FromResult(result);
        }

        TryRepairCurrentRunModifiers(runState);

        foreach (BlightDisplayInfo blight in blights)
        {
            if (!IsModifierEnabled(runState, blight.Modifier))
            {
                result.SkippedCount++;
                result.Messages.Add(string.Format(GmUiText.BlightNotEnabledNamed, blight.Name));
                continue;
            }

            if (TryRemoveModifier(runState, blight.Modifier, blight.IdText))
            {
                result.SuccessCount++;
                result.Messages.Add(string.Format(GmUiText.BlightRemoveSuccessNamed, blight.Name));
                MainFile.Logger.Info($"[荒疫] 移除成功：名称={blight.Name}，ID={blight.IdText}。");
            }
            else
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.BlightRemoveFailedNamed, blight.Name));
                MainFile.Logger.Info($"[荒疫] 移除失败：没有从当前 Modifiers 集合移除目标，名称={blight.Name}，ID={blight.IdText}。");
            }
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// 获取所有游戏内修饰器，包含好选项、坏选项和互斥组选项。
    /// </summary>
    private static IReadOnlyList<ModifierModel> GetAllModifiers()
    {
        Dictionary<string, ModifierModel> result = new(StringComparer.OrdinalIgnoreCase);
        AddModifiers(result, ModelDb.GoodModifiers);
        AddModifiers(result, ModelDb.BadModifiers);
        foreach (IReadOnlySet<ModifierModel> group in ModelDb.MutuallyExclusiveModifiers)
        {
            AddModifiers(result, group);
        }

        return result.Values.ToList();
    }

    /// <summary>
    /// 把一批修饰器合并到字典中，按 ID 去重。
    /// </summary>
    private static void AddModifiers(Dictionary<string, ModifierModel> target, IEnumerable<ModifierModel> modifiers)
    {
        foreach (ModifierModel modifier in modifiers.Where(modifier => modifier != null))
        {
            if (modifier is CharacterCards characterCards)
            {
                foreach (CharacterModel character in ModelDb.AllCharacters)
                {
                    CharacterCards characterCardsModifier = (CharacterCards)characterCards.ToMutable();
                    characterCardsModifier.CharacterModel = character.Id;
                    target.TryAdd(GetModifierIdentityKey(characterCardsModifier), characterCardsModifier);
                }

                continue;
            }

            target.TryAdd(GetModifierIdentityKey(modifier), modifier);
        }
    }

    /// <summary>
    /// 把游戏修饰器模型转换成荒疫页展示数据。
    /// </summary>
    private static BlightDisplayInfo BuildDisplayInfo(ModifierModel modifier, IReadOnlyList<ModifierModel> enabledModifiers)
    {
        string idText = modifier.Id.ToString();
        string categoryKey = ToCategoryKey(modifier);
        string sourceKey = ToSourceKey(modifier.GetType().Assembly.GetName().Name);
        string optionTitle = SafeText(TryGetLocalizedText(() => modifier.NeowOptionTitle, idText, "NeowOptionTitle"), "");
        string title = SafeText(TryGetLocalizedText(() => modifier.Title, idText, "Title"), idText);
        string optionDescription = SafeText(TryGetLocalizedText(() => modifier.NeowOptionDescription, idText, "NeowOptionDescription"), "");
        string description = SafeText(TryGetLocalizedText(() => modifier.Description, idText, "Description"), GmUiText.NoDescription);
        return new BlightDisplayInfo
        {
            Id = modifier.Id,
            IdText = idText,
            Name = string.IsNullOrWhiteSpace(optionTitle) ? title : optionTitle,
            Description = string.IsNullOrWhiteSpace(optionDescription) ? description : optionDescription,
            OptionTitle = optionTitle,
            OptionDescription = optionDescription,
            CategoryKey = categoryKey,
            Category = ToCategoryText(categoryKey),
            SourceKey = sourceKey,
            Source = ToSourceText(sourceKey),
            IsEnabled = enabledModifiers.Any(enabledModifier => IsSameModifier(enabledModifier, modifier)),
            Icon = TryGetIcon(modifier, idText),
            Modifier = modifier,
        };
    }

    /// <summary>
    /// 获取当前局内状态；没有局内状态时返回空。
    /// </summary>
    private static RunState? GetCurrentRunState()
    {
        return GmRunContextService.GetCurrentRunState();
    }

    /// <summary>
    /// 获取当前局内已经启用的修饰器列表。
    /// </summary>
    private static IReadOnlyList<ModifierModel> GetCurrentModifiers()
    {
        return GetCurrentRunState()?.Modifiers ?? [];
    }

    /// <summary>
    /// 判断指定修饰器是否已经在当前局内启用。
    /// </summary>
    private static bool IsModifierEnabled(RunState runState, ModifierModel modifier)
    {
        return runState.Modifiers.Any(currentModifier => IsSameModifier(currentModifier, modifier));
    }

    /// <summary>
    /// 从当前局内修饰器集合移除指定 ID；优先复制列表后通过属性 setter 回写，避免原集合只读。
    /// </summary>
    private static bool TryRemoveModifier(RunState runState, ModifierModel targetModifier, string idText)
    {
        List<ModifierModel> modifiers = runState.Modifiers.ToList();
        int beforeCount = modifiers.Count;
        modifiers.RemoveAll(modifier => IsSameModifier(modifier, targetModifier));
        if (modifiers.Count == beforeCount)
        {
            return false;
        }

        return TrySetModifiersByReflection(runState, modifiers, $"移除ID={idText}", beforeCount);
    }

    /// <summary>
    /// 当属性 setter 不可用时，反射写入 Modifiers backing field。
    /// </summary>
    private static bool TrySetModifiersByReflection(RunState runState, List<ModifierModel> modifiers, string context, int beforeCount)
    {
        FieldInfo? field = typeof(RunState).GetField("<Modifiers>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
        {
            MainFile.Logger.Info($"[荒疫] 反射回写失败：找不到 Modifiers backing field，场景={context}。");
            return false;
        }

        try
        {
            field.SetValue(runState, modifiers);
            MainFile.Logger.Info($"[荒疫] 已通过反射字段回写集合：场景={context}，回写前={beforeCount}，回写后={modifiers.Count}。");
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"[荒疫] 反射回写异常：场景={context}，异常={ex.Message}。");
            return false;
        }
    }

    /// <summary>
    /// 修复当前局内误放入的 canonical 修饰器，避免保存时 ToSerializable 抛异常。
    /// </summary>
    private static void TryRepairCurrentRunModifiers(RunState? runState)
    {
        if (runState == null)
        {
            return;
        }

        IReadOnlyList<ModifierModel> currentModifiers = runState.Modifiers;
        if (currentModifiers.Count == 0)
        {
            return;
        }

        bool changed = false;
        List<ModifierModel> repairedModifiers = new(currentModifiers.Count);
        foreach (ModifierModel modifier in currentModifiers)
        {
            string idText = SafeGetModifierIdText(modifier);
            if (ShouldRepairModifierInstance(modifier, idText))
            {
                if (TryCreateMutableModifier(modifier, idText, out ModifierModel? mutableModifier) && mutableModifier != null)
                {
                    TryInitializeModifierForCurrentRun(mutableModifier, runState, idText);
                    repairedModifiers.Add(mutableModifier);
                    changed = true;
                    MainFile.Logger.Info($"[荒疫] 已修复当前局 canonical 修饰器：ID={idText}，类型={modifier.GetType().FullName}。");
                    continue;
                }
            }

            TryInitializeModifierForCurrentRun(modifier, runState, idText);
            repairedModifiers.Add(modifier);
        }

        if (changed)
        {
            TrySetModifiersByReflection(runState, repairedModifiers, "修复当前局 canonical 修饰器", currentModifiers.Count);
        }
    }

    /// <summary>
    /// 判断当前局内修饰器实例是否需要替换为 mutable 副本。
    /// </summary>
    private static bool ShouldRepairModifierInstance(ModifierModel modifier, string idText)
    {
        try
        {
            return modifier.IsCanonical || !modifier.IsMutable;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"[荒疫] 判断修饰器可变状态失败：ID={idText}，异常={ex.Message}。将尝试修复。");
            return true;
        }
    }

    /// <summary>
    /// 为修饰器创建 mutable 副本。
    /// </summary>
    private static bool TryCreateMutableModifier(ModifierModel modifier, string idText, out ModifierModel? mutableModifier)
    {
        try
        {
            mutableModifier = modifier.ToMutable();
            return true;
        }
        catch (Exception ex)
        {
            mutableModifier = null;
            MainFile.Logger.Info($"[荒疫] 创建 mutable 修饰器失败：ID={idText}，类型={modifier.GetType().FullName}，异常={ex.Message}。");
            return false;
        }
    }

    /// <summary>
    /// 创建可加入当前局的修饰器实例，并绑定当前 RunState，避免运行时 Hook 读取 RunState 时崩溃。
    /// </summary>
    private static ModifierModel CreateRunnableModifier(ModifierModel modifier, RunState runState)
    {
        ModifierModel mutableModifier = modifier.IsCanonical
            ? modifier.ToMutable()
            : (ModifierModel)modifier.ClonePreservingMutability();
        string idText = SafeGetModifierIdText(mutableModifier);
        if (!TryInitializeModifierForCurrentRun(mutableModifier, runState, idText))
        {
            throw new InvalidOperationException($"荒疫初始化失败，已取消添加：ID={idText}。");
        }

        return mutableModifier;
    }

    /// <summary>
    /// 为 mutable 修饰器绑定当前局；使用 OnRunLoaded，避免运行中触发 OnRunCreated 的开局清牌等副作用。
    /// </summary>
    private static bool TryInitializeModifierForCurrentRun(ModifierModel modifier, RunState runState, string idText)
    {
        if (IsModifierInitialized(modifier))
        {
            return true;
        }

        try
        {
            modifier.OnRunLoaded(runState);
            MainFile.Logger.Info($"[荒疫] 已初始化修饰器运行状态：ID={idText}，类型={modifier.GetType().FullName}。");
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"[荒疫] 初始化修饰器运行状态失败：ID={idText}，类型={modifier.GetType().FullName}，异常={ex.Message}。");
            return false;
        }
    }

    /// <summary>
    /// 判断修饰器是否已经绑定 RunState，避免重复调用 OnRunLoaded 造成副作用。
    /// </summary>
    private static bool IsModifierInitialized(ModifierModel modifier)
    {
        FieldInfo? field = typeof(ModifierModel).GetField("_runState", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
        {
            return false;
        }

        return field.GetValue(modifier) != null;
    }

    /// <summary>
    /// 判断两个修饰器是否指向同一个荒疫；角色卡包荒疫需要额外比较角色 ID。
    /// </summary>
    private static bool IsSameModifier(ModifierModel left, ModifierModel right)
    {
        if (left.GetType() != right.GetType())
        {
            return false;
        }

        if (left is CharacterCards || right is CharacterCards)
        {
            return TryGetCharacterCardsModelId(left, out ModelId? leftCharacterId) &&
                   TryGetCharacterCardsModelId(right, out ModelId? rightCharacterId) &&
                   leftCharacterId != null &&
                   leftCharacterId.Equals(rightCharacterId);
        }

        return string.Equals(SafeGetModifierIdText(left), SafeGetModifierIdText(right), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 读取荒疫唯一键；角色卡包荒疫同 ID 多变体，所以把角色 ID 拼进键里。
    /// </summary>
    private static string GetModifierIdentityKey(ModifierModel modifier)
    {
        if (TryGetCharacterCardsModelId(modifier, out ModelId? characterId))
        {
            return $"{SafeGetModifierIdText(modifier)}|{characterId}";
        }

        return SafeGetModifierIdText(modifier);
    }

    /// <summary>
    /// 安全读取 CharacterCards 荒疫绑定的角色 ID。
    /// </summary>
    private static bool TryGetCharacterCardsModelId(ModifierModel modifier, out ModelId? characterId)
    {
        if (modifier is CharacterCards characterCards)
        {
            try
            {
                characterId = characterCards.CharacterModel;
                return true;
            }
            catch
            {
            }
        }

        characterId = default;
        return false;
    }

    /// <summary>
    /// 安全读取修饰器 ID，避免修复日志因为 ID 读取失败再次抛异常。
    /// </summary>
    private static string SafeGetModifierIdText(ModifierModel modifier)
    {
        try
        {
            return modifier.Id.ToString();
        }
        catch
        {
            return modifier.GetType().FullName ?? modifier.GetType().Name;
        }
    }

    /// <summary>
    /// 判断荒疫名称、描述、来源或 ID 是否匹配搜索关键字。
    /// </summary>
    private static bool MatchesKeyword(BlightDisplayInfo info, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return Normalize(info.Name).Contains(keyword) ||
               Normalize(info.Description).Contains(keyword) ||
               Normalize(info.Source).Contains(keyword) ||
               Normalize(info.IdText).Contains(keyword);
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
    /// 转换修饰器分类键。
    /// </summary>
    private static string ToCategoryKey(ModifierModel modifier)
    {
        string idText = modifier.Id.ToString();
        if (ModelDb.GoodModifiers.Any(item => string.Equals(item.Id.ToString(), idText, StringComparison.OrdinalIgnoreCase)))
        {
            return CategoryGoodKey;
        }

        if (ModelDb.BadModifiers.Any(item => string.Equals(item.Id.ToString(), idText, StringComparison.OrdinalIgnoreCase)))
        {
            return CategoryBadKey;
        }

        return CategoryOtherKey;
    }

    /// <summary>
    /// 转换修饰器分类文本。
    /// </summary>
    private static string ToCategoryText(string categoryKey)
    {
        return categoryKey switch
        {
            CategoryGoodKey => GmUiText.Pick("增益选项", "Good Option"),
            CategoryBadKey => GmUiText.Pick("负面选项", "Bad Option"),
            _ => GmUiText.Pick("其他选项", "Other Option"),
        };
    }

    /// <summary>
    /// 转换来源键。
    /// </summary>
    private static string ToSourceKey(string? assemblyName)
    {
        return string.Equals(assemblyName, "sts2", StringComparison.OrdinalIgnoreCase)
            ? SourceBaseGameKey
            : SourceModsKey;
    }

    /// <summary>
    /// 转换来源文本。
    /// </summary>
    private static string ToSourceText(string sourceKey)
    {
        return sourceKey.Equals(SourceBaseGameKey, StringComparison.OrdinalIgnoreCase)
            ? GmUiText.Pick("游戏本体", "Base Game")
            : "Mods";
    }

    /// <summary>
    /// 分类排序。
    /// </summary>
    private static int GetCategoryOrder(string categoryKey)
    {
        return categoryKey switch
        {
            CategoryGoodKey => 0,
            CategoryBadKey => 1,
            _ => 2,
        };
    }

    /// <summary>
    /// 来源排序。
    /// </summary>
    private static int GetSourceOrder(string sourceKey)
    {
        return sourceKey.Equals(SourceBaseGameKey, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }

    /// <summary>
    /// 读取本地化文本。
    /// </summary>
    private static string? GetLocalizedText(MegaCrit.Sts2.Core.Localization.LocString? locString)
    {
        return GmLocalizationService.ResolveText(locString, "解析荒疫文本失败 / Failed to resolve blight text");
    }

    /// <summary>
    /// 安全读取修饰器本地化字段；部分修饰器未绑定运行时上下文时，直接读取属性会抛异常。
    /// </summary>
    private static string? TryGetLocalizedText(Func<MegaCrit.Sts2.Core.Localization.LocString?> locStringProvider, string idText, string fieldName)
    {
        try
        {
            return GetLocalizedText(locStringProvider());
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"[荒疫] 读取修饰器文本失败：ID={idText}，字段={fieldName}，异常={ex.Message}。已使用兜底文本。");
            return null;
        }
    }

    /// <summary>
    /// 安全读取修饰器图标；避免个别修饰器资源异常影响整个荒疫页。
    /// </summary>
    private static Godot.Texture2D? TryGetIcon(ModifierModel modifier, string idText)
    {
        try
        {
            return modifier.Icon;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"[荒疫] 读取修饰器图标失败：ID={idText}，异常={ex.Message}。");
            return null;
        }
    }

    /// <summary>
    /// 安全返回文本，空白时使用兜底文本。
    /// </summary>
    private static string SafeText(string? text, string fallback)
    {
        return string.IsNullOrWhiteSpace(text) ? fallback : text;
    }

    /// <summary>
    /// 归一化搜索文本。
    /// </summary>
    private static string Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant() ?? "";
    }
}
