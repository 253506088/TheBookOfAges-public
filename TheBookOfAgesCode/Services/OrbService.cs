using System.Collections;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.GameActions;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.UI;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// 充能球服务，负责读取游戏内充能球目录并把选中的充能球发放给当前玩家。
/// </summary>
public sealed class OrbService
{
    private const string AllFilterKey = "all";
    private const string SourceBaseGameKey = "base-game";
    private const string SourceModsKey = "mods";
    private static readonly string[] OrbCollectionPropertyNames = ["AllOrbs", "Orbs", "OrbModels", "AllOrbModels"];
    private static readonly string[] OrbCollectionFieldNames = ["AllOrbs", "Orbs", "OrbModels", "AllOrbModels"];
    private static readonly string[] IdMemberNames = ["Id", "ID", "ModelId", "Key"];
    private static readonly string[] TitleMemberNames = ["Title", "Name", "DisplayName"];
    private static readonly string[] DescriptionMemberNames = ["DynamicDescription", "Description", "BodyText"];
    private static readonly string[] HoverTipMemberNames = ["DumbHoverTip", "HoverTip"];
    private static readonly string[] HoverTipsMemberNames = ["HoverTips", "ExtraHoverTips"];
    private static readonly string[] HoverTipTitleMemberNames = ["Title"];
    private static readonly string[] HoverTipDescriptionMemberNames = ["Description"];
    private static readonly string[] IconMemberNames = ["Image", "Icon", "Texture"];
    private static readonly string[] OrbQueueCountMemberNames = ["Count", "Length"];
    private static readonly string[] OrbQueueCapacityMemberNames = ["Capacity"];
    private static readonly string[] ChannelMethodNames = ["Channel", "ChannelOrb", "TryChannel", "TryChannelOrb", "AddOrb", "Add", "Enqueue", "Push", "PushOrb", "Procure", "SpawnOrb", "AddToQueue"];
    private static readonly string[] EvokeMethodNames = ["Evoke", "EvokeFirst", "EvokeOldest", "PopAndEvoke", "TryEvoke", "TryEvokeFirst", "RemoveAndEvoke"];
    private static readonly string[] ToMutableMethodNames = ["ToMutable", "Create", "Make", "Instantiate", "ToEntity", "CreateInstance", "Build"];
    private static readonly string[] StaticOrbCommandTypeNames = [
        "MegaCrit.Sts2.Core.Commands.OrbCmd",
        "MegaCrit.Sts2.Core.Commands.ChannelOrbCmd",
        "MegaCrit.Sts2.Core.DevConsole.ConsoleCommands.OrbConsoleCmd",
    ];
    private static readonly string[] PlayerChoiceContextTypeNames = [
        "MegaCrit.Sts2.Core.Choices.ThrowingPlayerChoiceContext",
        "MegaCrit.Sts2.Core.Commands.ThrowingPlayerChoiceContext",
        "MegaCrit.Sts2.Core.Combat.ThrowingPlayerChoiceContext",
        "MegaCrit.Sts2.Core.Entities.PlayerChoices.ThrowingPlayerChoiceContext",
        "MegaCrit.Sts2.Core.PlayerChoices.ThrowingPlayerChoiceContext",
    ];

    /// <summary>
    /// 从游戏当前 ModelDb 实时读取所有充能球，并转换成页面展示数据。
    /// </summary>
    public IReadOnlyList<OrbDisplayInfo> GetOrbs(string keyword, string sourceFilterKey)
    {
        string normalizedKeyword = Normalize(keyword);
        return ReadAllOrbModels()
            .Select(BuildDisplayInfo)
            .Where(info => MatchesKeyword(info, normalizedKeyword))
            .Where(info => MatchesSource(info.SourceKey, sourceFilterKey))
            .OrderBy(info => GetSourceOrder(info.SourceKey))
            .ThenBy(info => info.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 获取球盒来源筛选项。
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
    /// 判断当前是否可以给玩家发放充能球。
    /// </summary>
    public bool CanGrantOrbs()
    {
        return GmRunContextService.GetFeatureAvailability(GmFeature.GrantOrb).IsAllowed
            && GetCurrentPlayer()?.PlayerCombatState?.OrbQueue != null;
    }

    /// <summary>
    /// 批量发放已选择的充能球，支持同一种充能球重复发放。
    /// </summary>
    public async Task<OrbGrantResult> GrantOrbsAsync(IEnumerable<OrbDisplayInfo> selectedOrbs)
    {
        OrbGrantResult result = new();
        List<OrbDisplayInfo> orbList = selectedOrbs.ToList();
        MainFile.Logger.Info($"球盒开始发放：选择数量={orbList.Count}");
        GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(GmFeature.GrantOrb);
        if (!availability.IsAllowed)
        {
            result.FailedCount += orbList.Count;
            result.Messages.Add(availability.Reason);
            MainFile.Logger.Info($"球盒发放已被多人安全闸门阻止：选择数量={orbList.Count}，原因={availability.Reason}。");
            return result;
        }

        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            MainFile.Logger.Info("球盒发放失败：当前没有有效玩家或不在进行中的游戏局内。");
            result.FailedCount++;
            result.Messages.Add(GmUiText.OrbNotInRun);
            return result;
        }

        object? orbQueue = player.PlayerCombatState?.OrbQueue;
        if (orbQueue == null)
        {
            MainFile.Logger.Info($"球盒发放失败：玩家存在但 PlayerCombatState 或 OrbQueue 为空。PlayerType={player.GetType().FullName}");
            result.FailedCount++;
            result.Messages.Add(GmUiText.OrbNotInCombat);
            return result;
        }

        if (GmRunContextService.IsMultiplayer())
        {
            return await SubmitOrbGrantActionAsync(player, orbList);
        }

        MainFile.Logger.Info($"球盒发放环境：PlayerType={player.GetType().FullName}，CombatStateType={player.PlayerCombatState?.GetType().FullName}，OrbQueueType={orbQueue.GetType().FullName}，Capacity={ReadIntMember(orbQueue, OrbQueueCapacityMemberNames, -1)}，Count={ReadOrbCount(orbQueue)}");
        foreach (OrbDisplayInfo info in orbList)
        {
            (bool success, string message) = await TryGrantOneOrbAsync(player, orbQueue, info);
            if (!success)
            {
                result.FailedCount++;
                result.Messages.Add(message);
                continue;
            }

            result.SuccessCount++;
            result.Messages.Add(message);
        }

        MainFile.Logger.Info($"球盒发放结束：成功={result.SuccessCount}，跳过={result.SkippedCount}，失败={result.FailedCount}");
        return result;
    }

    /// <summary>
    /// 多人模式下提交充能球发放同步动作，不在服务层直接改本机 OrbQueue。
    /// </summary>
    /// <param name="player">当前本机玩家。</param>
    /// <param name="orbList">要发放的充能球列表。</param>
    private static async Task<OrbGrantResult> SubmitOrbGrantActionAsync(Player player, IReadOnlyList<OrbDisplayInfo> orbList)
    {
        OrbGrantResult result = new();
        List<OrbDisplayInfo> acceptedOrbs = [];
        foreach (OrbDisplayInfo info in orbList)
        {
            if (IsUnsafeInternalOrbId(info.IdText))
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.OrbGrantFailedNamed, info.Name));
                MainFile.Logger.Info($"球盒多人同步拒绝发放内部测试充能球：Id={info.IdText}，Name={info.Name}。");
                continue;
            }

            acceptedOrbs.Add(info);
        }

        if (acceptedOrbs.Count == 0)
        {
            return result;
        }

        List<string> orbIds = acceptedOrbs.Select(info => info.IdText).ToList();
        bool submitted = await GmNetworkCommandService.TryEnqueueGameActionAsync(new GmOrbGrantAction(player, orbIds), "充能球发放");
        foreach (OrbDisplayInfo info in acceptedOrbs)
        {
            if (submitted)
            {
                result.SuccessCount++;
                result.Messages.Add(string.Format(GmUiText.OrbGrantSubmittedNamed, info.Name));
            }
            else
            {
                result.FailedCount++;
                result.Messages.Add(string.Format(GmUiText.OrbGrantFailedNamed, info.Name));
            }
        }

        MainFile.Logger.Info($"球盒多人同步提交结束：提交成功={submitted}，提交数量={acceptedOrbs.Count}。");
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
    /// 读取 ModelDb 里的全部充能球集合，优先走公开属性，找不到时返回空集合。
    /// </summary>
    private static IReadOnlyList<object> ReadAllOrbModels()
    {
        Type modelDbType = typeof(ModelDb);
        Dictionary<string, object> allOrbs = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in OrbCollectionPropertyNames)
        {
            PropertyInfo? property = modelDbType.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (TryReadOrbEnumerable(property?.GetValue(null), out IReadOnlyList<object> values))
            {
                MainFile.Logger.Info($"球盒读取充能球列表：使用 ModelDb.{name} 属性，共 {values.Count} 个。");
                AddOrbModels(allOrbs, values);
            }
        }

        foreach (string name in OrbCollectionFieldNames)
        {
            FieldInfo? field = modelDbType.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (TryReadOrbEnumerable(field?.GetValue(null), out IReadOnlyList<object> values))
            {
                MainFile.Logger.Info($"球盒读取充能球列表：使用 ModelDb.{name} 字段，共 {values.Count} 个。");
                AddOrbModels(allOrbs, values);
            }
        }

        foreach (PropertyInfo property in modelDbType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (OrbCollectionPropertyNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            object? value = TryReadStaticProperty(property);
            if (TryReadOrbEnumerable(value, out IReadOnlyList<object> values))
            {
                MainFile.Logger.Info($"球盒扫描 ModelDb.{property.Name} 属性发现充能球，共 {values.Count} 个。");
                AddOrbModels(allOrbs, values);
            }
        }

        foreach (FieldInfo field in modelDbType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (OrbCollectionFieldNames.Contains(field.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            object? value = TryReadStaticField(field);
            if (TryReadOrbEnumerable(value, out IReadOnlyList<object> values))
            {
                MainFile.Logger.Info($"球盒扫描 ModelDb.{field.Name} 字段发现充能球，共 {values.Count} 个。");
                AddOrbModels(allOrbs, values);
            }
        }

        AddOrbModels(allOrbs, ScanOrbModelTypes());

        if (allOrbs.Count == 0)
        {
            MainFile.Logger.Info("球盒读取充能球列表失败：未在 ModelDb 找到可识别的充能球集合。");
            return [];
        }

        MainFile.Logger.Info($"球盒读取充能球列表完成：合并去重后共 {allOrbs.Count} 个。ID={string.Join(", ", allOrbs.Keys)}");
        return allOrbs.Values.ToList();
    }

    /// <summary>
    /// 把反射读到的对象转换成充能球模型列表。
    /// </summary>
    private static bool TryReadOrbEnumerable(object? value, out IReadOnlyList<object> values)
    {
        values = [];
        if (value is not IEnumerable enumerable || value is string)
        {
            return false;
        }

        List<object> list = [];
        foreach (object? item in enumerable)
        {
            object? candidate = UnwrapModelEntry(item);
            if (candidate != null && IsOrbModelLike(candidate))
            {
                list.Add(candidate);
            }
        }

        values = list
            .DistinctBy(item => ReadMemberText(item, IdMemberNames, item.GetType().FullName ?? item.GetHashCode().ToString()), StringComparer.OrdinalIgnoreCase)
            .ToList();
        return values.Count > 0;
    }

    /// <summary>
    /// 合并充能球模型，并按 ID 去重。
    /// </summary>
    private static void AddOrbModels(Dictionary<string, object> allOrbs, IEnumerable<object> values)
    {
        foreach (object orb in values)
        {
            string id = ReadMemberText(orb, IdMemberNames, orb.GetType().FullName ?? orb.GetHashCode().ToString());
            allOrbs.TryAdd(id, orb);
        }
    }

    /// <summary>
    /// 判断对象是否像充能球模型，避免扫描 ModelDb 全集合时混入卡牌、遗物等对象。
    /// </summary>
    private static bool IsOrbModelLike(object item)
    {
        Type type = item.GetType();
        string idText = ReadMemberText(item, IdMemberNames, "");
        if (IsUnsafeInternalOrbId(idText))
        {
            //球盒跳过内部测试充能球
//            MainFile.Logger.Info($"球盒跳过内部测试充能球：Id={idText}，Type={type.FullName}");
            return false;
        }

        if (idText.StartsWith("ORB.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(idText) && type.Name.Contains("Orb", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 判断是否为游戏内部测试充能球；这类球可能缺少贴图资源，展示或发放都会触发游戏异常。
    /// </summary>
    private static bool IsUnsafeInternalOrbId(string idText)
    {
        return idText.Contains("MOCK_", StringComparison.OrdinalIgnoreCase) ||
               idText.Contains("_MOCK", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 解包 ModelDb 字典项，_contentById 枚举出来的是 KeyValuePair<ModelId, AbstractModel>，真正模型在 Value 里。
    /// </summary>
    private static object? UnwrapModelEntry(object? item)
    {
        if (item == null)
        {
            return null;
        }

        Type type = item.GetType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        {
            object? value = ReadMemberValue(item, ["Value"]);
            if (value != null)
            {
                return value;
            }
        }

        return item;
    }

    /// <summary>
    /// 判断类型是否存在指定方法名。
    /// </summary>
    private static bool HasMethod(Type type, string methodName)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Any(method => method.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 安全读取静态属性。
    /// </summary>
    private static object? TryReadStaticProperty(PropertyInfo property)
    {
        try
        {
            return property.GetValue(null);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 安全读取静态字段。
    /// </summary>
    private static object? TryReadStaticField(FieldInfo field)
    {
        try
        {
            return field.GetValue(null);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 扫描游戏程序集里所有可实例化的充能球模型类型，补齐未暴露在 ModelDb.Orbs 集合里的球。
    /// </summary>
    private static IReadOnlyList<object> ScanOrbModelTypes()
    {
        try
        {
            Type baseType = typeof(OrbModel);

            List<object> orbs = [];
            foreach (Type type in typeof(ModelDb).Assembly.GetTypes()
                         .Where(type => !type.IsAbstract && baseType.IsAssignableFrom(type)))
            {
                try
                {
                    object? orb = Activator.CreateInstance(type);
                    if (orb != null && IsOrbModelLike(orb))
                    {
                        orbs.Add(orb);
                    }
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Info($"球盒扫描充能球类型实例化失败：{type.FullName}，原因：{ex.Message}");
                }
            }

            MainFile.Logger.Info($"球盒扫描充能球类型完成：共实例化 {orbs.Count} 个。ID={string.Join(", ", orbs.Select(orb => ReadMemberText(orb, IdMemberNames, orb.GetType().Name)))}");
            return orbs;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"球盒扫描充能球类型失败：{ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// 把游戏充能球模型转换成页面需要的简单字段。
    /// </summary>
    private static OrbDisplayInfo BuildDisplayInfo(object orbModel)
    {
        string idText = ReadMemberText(orbModel, IdMemberNames, orbModel.GetType().Name);
        string sourceKey = ToSourceKey(orbModel.GetType().Assembly.GetName().Name);
        string name = SafeText(ReadLocalizedMemberText(orbModel, TitleMemberNames), "");
        string description = SafeText(ReadLocalizedMemberText(orbModel, DescriptionMemberNames), "");
        FillOrbTextFromHoverTips(orbModel, ref name, ref description);
        LogOrbDisplayDiagnostics(orbModel, idText, name, description);
        return new OrbDisplayInfo
        {
            IdText = idText,
            Name = SafeText(name, BuildReadableOrbName(idText)),
            Description = SafeText(description, GmUiText.NoDescription),
            SourceKey = sourceKey,
            Source = ToSourceText(sourceKey, orbModel.GetType().Assembly.GetName().Name),
            Icon = ReadTexture(orbModel),
            OrbModel = orbModel,
        };
    }

    /// <summary>
    /// 从游戏悬停说明里补充充能球名称和描述，解决部分扫描出来的球没有 Title 字段文本的问题。
    /// </summary>
    private static void FillOrbTextFromHoverTips(object orbModel, ref string name, ref string description)
    {
        object? dumbHoverTip = ReadMemberValue(orbModel, HoverTipMemberNames);
        TryFillTextFromHoverTip(dumbHoverTip, ref name, ref description);
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(description))
        {
            return;
        }

        object? hoverTips = ReadMemberValue(orbModel, HoverTipsMemberNames);
        if (hoverTips is not IEnumerable enumerable || hoverTips is string)
        {
            return;
        }

        foreach (object? hoverTip in enumerable)
        {
            TryFillTextFromHoverTip(hoverTip, ref name, ref description);
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(description))
            {
                return;
            }
        }
    }

    /// <summary>
    /// 从单个 HoverTip 对象里读取标题和描述。
    /// </summary>
    private static void TryFillTextFromHoverTip(object? hoverTip, ref string name, ref string description)
    {
        if (hoverTip == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = SafeText(ReadMemberValue(hoverTip, HoverTipTitleMemberNames)?.ToString(), name);
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            description = SafeText(ReadMemberValue(hoverTip, HoverTipDescriptionMemberNames)?.ToString(), description);
        }
    }

    /// <summary>
    /// 把充能球 ID 转成人能看的兜底名称。
    /// </summary>
    private static string BuildReadableOrbName(string idText)
    {
        string text = idText;
        int dotIndex = text.LastIndexOf('.');
        if (dotIndex >= 0 && dotIndex < text.Length - 1)
        {
            text = text[(dotIndex + 1)..];
        }

        if (text.EndsWith("_ORB", StringComparison.OrdinalIgnoreCase))
        {
            text = text[..^4];
        }

        return string.Join(" ", text.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }

    /// <summary>
    /// 打印充能球展示数据诊断日志，先观察游戏原始数据，再决定通用解析策略。
    /// </summary>
    private static void LogOrbDisplayDiagnostics(object orbModel, string idText, string resolvedName, string resolvedDescription)
    {
        try
        {
            object? title = ReadMemberValue(orbModel, TitleMemberNames);
            object? description = ReadMemberValue(orbModel, DescriptionMemberNames);
            object? icon = ReadMemberValue(orbModel, IconMemberNames);
            object? dumbHoverTip = ReadMemberValue(orbModel, HoverTipMemberNames);
            object? hoverTips = ReadMemberValue(orbModel, HoverTipsMemberNames);
            MainFile.Logger.Info($"球盒展示诊断：Id={idText}，Type={orbModel.GetType().FullName}，ResolvedName={resolvedName}，ResolvedDescription={resolvedDescription}，TitleRaw={DescribeValue(title)}，DescriptionRaw={DescribeValue(description)}，IconRaw={DescribeValue(icon)}，DumbHoverTip={DescribeHoverTip(dumbHoverTip)}，HoverTips={DescribeHoverTips(hoverTips)}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"球盒展示诊断失败：Id={idText}，Type={orbModel.GetType().FullName}，原因：{ex.Message}");
        }
    }

    /// <summary>
    /// 描述一个反射读到的值，避免日志里只看到类型名。
    /// </summary>
    private static string DescribeValue(object? value)
    {
        if (value == null)
        {
            return "null";
        }

        string text = value is LocString locString
            ? SafeText(GmLocalizationService.ResolveRawText(locString, "诊断充能球原始本地化文本失败"), value.ToString() ?? "")
            : value.ToString() ?? "";
        return $"{value.GetType().FullName}={text}";
    }

    /// <summary>
    /// 描述单个 HoverTip 的标题和描述。
    /// </summary>
    private static string DescribeHoverTip(object? hoverTip)
    {
        if (hoverTip == null)
        {
            return "null";
        }

        object? title = ReadMemberValue(hoverTip, HoverTipTitleMemberNames);
        object? description = ReadMemberValue(hoverTip, HoverTipDescriptionMemberNames);
        return $"{hoverTip.GetType().FullName}{{Title={DescribeValue(title)}, Description={DescribeValue(description)}}}";
    }

    /// <summary>
    /// 描述 HoverTips 集合。
    /// </summary>
    private static string DescribeHoverTips(object? hoverTips)
    {
        if (hoverTips == null)
        {
            return "null";
        }

        if (hoverTips is not IEnumerable enumerable || hoverTips is string)
        {
            return DescribeValue(hoverTips);
        }

        return string.Join(" | ", enumerable.Cast<object?>().Select(DescribeHoverTip).Take(8));
    }

    /// <summary>
    /// 尝试发放一个充能球；如果队列满，先调用游戏逻辑激发最前面的球。
    /// </summary>
    private static async Task<(bool Success, string Message)> TryGrantOneOrbAsync(Player player, object orbQueue, OrbDisplayInfo info)
    {
        string message = string.Format(GmUiText.OrbGrantFailedNamed, info.Name);
        if (IsUnsafeInternalOrbId(info.IdText))
        {
            MainFile.Logger.Info($"球盒拒绝发放内部测试充能球：Id={info.IdText}，Name={info.Name}");
            return (false, message);
        }

        int capacity = ReadIntMember(orbQueue, OrbQueueCapacityMemberNames, -1);
        int beforeCount = ReadOrbCount(orbQueue);
        MainFile.Logger.Info($"球盒尝试发放：Name={info.Name}，Id={info.IdText}，ModelType={info.OrbModel.GetType().FullName}，QueueType={orbQueue.GetType().FullName}，Capacity={capacity}，BeforeCount={beforeCount}");
        if (capacity <= 0)
        {
            MainFile.Logger.Info($"球盒发放失败：当前充能球栏位为 0，不自动增加栏位。充能球={info.IdText}");
            message = GmUiText.OrbNoSlots;
            return (false, message);
        }

        if (beforeCount >= capacity)
        {
            MainFile.Logger.Info($"球盒栏位已满：不手动激发，交给游戏原生 Channel 流程处理。充能球={info.IdText}");
        }

        if (!await TryChannelOrbAsync(player, orbQueue, info))
        {
            MainFile.Logger.Info($"球盒发放充能球失败：无法调用游戏 API 加入充能球。充能球={info.IdText}");
            LogOrbDiagnostics(player, orbQueue, info, CreateOrbInstance(info.OrbModel));
            return (false, message);
        }

        MainFile.Logger.Info($"球盒发放成功：Name={info.Name}，Id={info.IdText}，AfterCount={ReadOrbCount(orbQueue)}");
        message = string.Format(GmUiText.OrbGrantSuccessNamed, info.Name);
        return (true, message);
    }

    /// <summary>
    /// 读取当前队列里已有的充能球数量。
    /// </summary>
    private static int ReadOrbCount(object orbQueue)
    {
        int directCount = ReadIntMember(orbQueue, OrbQueueCountMemberNames, -1);
        if (directCount >= 0)
        {
            return directCount;
        }

        object? orbs = ReadMemberValue(orbQueue, ["Orbs", "orbs", "_orbs", "Items", "items"]);
        if (orbs is ICollection collection)
        {
            return collection.Count;
        }

        if (orbs is IEnumerable enumerable)
        {
            return enumerable.Cast<object?>().Count(item => item != null);
        }

        return 0;
    }

    /// <summary>
    /// 尝试调用游戏自身的激发方法处理最前面的充能球。
    /// </summary>
    private static bool TryEvokeFrontOrb(Player player, object orbQueue)
    {
        foreach (string methodName in EvokeMethodNames)
        {
            if (TryInvokeCompatibleMethod(orbQueue, methodName, [player, player.Creature, 0], out _, logPrefix: "球盒激发队列"))
            {
                return true;
            }
        }

        object? firstOrb = ReadFirstOrb(orbQueue);
        if (firstOrb == null)
        {
            return true;
        }

        foreach (string methodName in EvokeMethodNames)
        {
            if (TryInvokeCompatibleMethod(firstOrb, methodName, [player, player.Creature, orbQueue], out _, logPrefix: "球盒激发首球"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 尝试调用游戏自身的加入/Channel 方法，把充能球放入当前玩家队列。
    /// </summary>
    private static async Task<bool> TryChannelOrbAsync(Player player, object orbQueue, OrbDisplayInfo info)
    {
        object orbInstance = CreateOrbInstance(info.OrbModel);
        object? orbId = ReadMemberValue(info.OrbModel, IdMemberNames);
        object? creature = player.Creature;
        object? combatState = player.PlayerCombatState;
        object? combatManager = TryGetCombatManagerInstance();
        object? choiceContext = CreatePlayerChoiceContext();
        object?[] args = [choiceContext, orbInstance, info.OrbModel, orbId, player, creature, combatState, orbQueue, 0];
        MainFile.Logger.Info($"球盒准备调用发放 API：OrbId={info.IdText}，ModelType={info.OrbModel.GetType().FullName}，InstanceType={orbInstance.GetType().FullName}，IdType={orbId?.GetType().FullName ?? "null"}，ChoiceContextType={choiceContext?.GetType().FullName ?? "null"}");
        foreach (string methodName in ChannelMethodNames)
        {
            if (await TryInvokeCompatibleMethodAsync(orbQueue, methodName, args, logPrefix: "球盒发放队列"))
            {
                return true;
            }
        }

        if (combatState != null)
        {
            foreach (string methodName in ChannelMethodNames)
            {
                if (await TryInvokeCompatibleMethodAsync(combatState, methodName, args, logPrefix: "球盒发放战斗状态"))
                {
                    return true;
                }
            }
        }

        foreach (string methodName in ChannelMethodNames)
        {
            if (await TryInvokeCompatibleMethodAsync(player, methodName, args, logPrefix: "球盒发放玩家"))
            {
                return true;
            }
        }

        if (combatManager != null)
        {
            foreach (string methodName in ChannelMethodNames)
            {
                if (await TryInvokeCompatibleMethodAsync(combatManager, methodName, args, logPrefix: "球盒发放战斗管理器"))
                {
                    return true;
                }
            }
        }

        if (await TryChannelOrbByStaticCommandAsync(args))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 尽量把充能球模型转换成运行时实例；如果没有转换方法，则直接传模型给游戏 API。
    /// </summary>
    private static object CreateOrbInstance(object orbModel)
    {
        foreach (string methodName in ToMutableMethodNames)
        {
            if (TryInvokeCompatibleMethod(orbModel, methodName, [], out object? result, logPrefix: "球盒创建充能球实例") && result != null)
            {
                MainFile.Logger.Info($"球盒充能球实例创建成功：ModelType={orbModel.GetType().FullName}，Method={methodName}，InstanceType={result.GetType().FullName}");
                return result;
            }
        }

        MainFile.Logger.Info($"球盒未找到充能球实例创建方法，直接使用模型对象：ModelType={orbModel.GetType().FullName}");
        return orbModel;
    }

    /// <summary>
    /// 读取队列中最前面的充能球对象。
    /// </summary>
    private static object? ReadFirstOrb(object orbQueue)
    {
        object? orbs = ReadMemberValue(orbQueue, ["Orbs", "orbs", "_orbs", "Items", "items"]);
        if (orbs is IEnumerable enumerable)
        {
            return enumerable.Cast<object?>().FirstOrDefault(item => item != null);
        }

        return null;
    }

    /// <summary>
    /// 调用参数兼容的实例方法，按可赋值参数自动挑选重载。
    /// </summary>
    private static bool TryInvokeCompatibleMethod(object target, string methodName, object?[] availableArgs, out object? result, string? logPrefix = null)
    {
        result = null;
        MethodInfo[] methods = target.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(method => method.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
            .Where(method => !method.ContainsGenericParameters)
            .OrderBy(method => method.GetParameters().Length)
            .ToArray();
        if (methods.Length == 0)
        {
            return false;
        }

        foreach (MethodInfo method in methods)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (!TryBuildArguments(parameters, availableArgs, out object?[] args))
            {
                if (logPrefix != null)
                {
                    MainFile.Logger.Info($"{logPrefix} 跳过方法：{target.GetType().FullName}.{method.Name}({BuildParameterText(parameters)})，参数不匹配。");
                }
                continue;
            }

            try
            {
                result = method.Invoke(target, args);
                MainFile.Logger.Info($"{logPrefix ?? "球盒反射调用"} 成功：{target.GetType().FullName}.{method.Name}({BuildParameterText(parameters)})");
                return true;
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"{logPrefix ?? "球盒反射调用"} 失败：{target.GetType().FullName}.{method.Name}({BuildParameterText(parameters)})，原因：{ex.Message}");
            }
        }

        return false;
    }

    /// <summary>
    /// 调用参数兼容的实例方法；如果返回结果带有异步任务，则等待任务完成。
    /// </summary>
    private static async Task<bool> TryInvokeCompatibleMethodAsync(object target, string methodName, object?[] availableArgs, string? logPrefix = null)
    {
        if (!TryInvokeCompatibleMethod(target, methodName, availableArgs, out object? result, logPrefix))
        {
            return false;
        }

        return await AwaitReturnedTaskAsync(result, logPrefix ?? "球盒反射调用");
    }

    /// <summary>
    /// 尝试调用静态充能球命令类，兼容游戏把 Channel 逻辑放在命令对象上的情况。
    /// </summary>
    private static async Task<bool> TryChannelOrbByStaticCommandAsync(object?[] args)
    {
        foreach (string typeName in StaticOrbCommandTypeNames)
        {
            Type? type = typeof(ModelDb).Assembly.GetType(typeName);
            if (type == null)
            {
                MainFile.Logger.Info($"球盒静态命令类型不存在：{typeName}");
                continue;
            }

            foreach (string methodName in ChannelMethodNames)
            {
                if (await TryInvokeCompatibleStaticMethodAsync(type, methodName, args))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 尝试调用参数兼容的静态方法。
    /// </summary>
    private static bool TryInvokeCompatibleStaticMethod(Type type, string methodName, object?[] availableArgs, out object? result)
    {
        result = null;
        MethodInfo[] methods = type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(method => method.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
            .Where(method => !method.ContainsGenericParameters)
            .OrderBy(method => method.GetParameters().Length)
            .ToArray();

        foreach (MethodInfo method in methods)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (!TryBuildArguments(parameters, availableArgs, out object?[] args))
            {
                MainFile.Logger.Info($"球盒静态命令跳过方法：{type.FullName}.{method.Name}({BuildParameterText(parameters)})，参数不匹配。");
                continue;
            }

            try
            {
                result = method.Invoke(null, args);
                MainFile.Logger.Info($"球盒静态命令调用成功：{type.FullName}.{method.Name}({BuildParameterText(parameters)})");
                return true;
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"球盒静态命令调用失败：{type.FullName}.{method.Name}({BuildParameterText(parameters)})，原因：{ex.Message}");
            }
        }

        return false;
    }

    /// <summary>
    /// 尝试调用参数兼容的静态方法；如果返回结果带有异步任务，则等待任务完成。
    /// </summary>
    private static async Task<bool> TryInvokeCompatibleStaticMethodAsync(Type type, string methodName, object?[] availableArgs)
    {
        if (!TryInvokeCompatibleStaticMethod(type, methodName, availableArgs, out object? result))
        {
            return false;
        }

        return await AwaitReturnedTaskAsync(result, $"球盒静态命令 {type.FullName}.{methodName}");
    }

    /// <summary>
    /// 等待游戏命令返回对象里的 task/Task，避免连续发放时动画和效果被挤到一起。
    /// </summary>
    private static async Task<bool> AwaitReturnedTaskAsync(object? result, string logContext)
    {
        if (result == null)
        {
            return true;
        }

        try
        {
            if (result is Task directTask)
            {
                MainFile.Logger.Info($"{logContext}：等待直接返回的 Task 完成。");
                await directTask;
                return true;
            }

            object? taskValue = ReadMemberValue(result, ["task", "Task"]);
            if (taskValue is Task task)
            {
                MainFile.Logger.Info($"{logContext}：等待返回结果中的 task 完成。");
                await task;
            }

            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"{logContext}：等待任务失败，原因：{ex}");
            return false;
        }
    }

    /// <summary>
    /// 尝试读取 CombatManager.Instance，用于兼容充能球发放逻辑挂在战斗管理器上的情况。
    /// </summary>
    private static object? TryGetCombatManagerInstance()
    {
        Type? type = typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Combat.CombatManager");
        if (type == null)
        {
            type = typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Nodes.Combat.CombatManager");
        }

        if (type == null)
        {
            return null;
        }

        object? instance = ReadStaticMemberValue(type, ["Instance", "_instance", "Current"]);
        MainFile.Logger.Info($"球盒读取 CombatManager：Type={type.FullName}，Instance={(instance == null ? "null" : instance.GetType().FullName)}");
        return instance;
    }

    /// <summary>
    /// 失败时打印关键类型的方法和字段，方便下一轮按日志补适配。
    /// </summary>
    private static void LogOrbDiagnostics(Player player, object orbQueue, OrbDisplayInfo info, object? orbInstance)
    {
        MainFile.Logger.Info($"球盒诊断：PlayerType={player.GetType().FullName}");
        MainFile.Logger.Info($"球盒诊断：CombatStateType={player.PlayerCombatState?.GetType().FullName ?? "null"}");
        MainFile.Logger.Info($"球盒诊断：OrbQueueType={orbQueue.GetType().FullName}");
        MainFile.Logger.Info($"球盒诊断：OrbModelType={info.OrbModel.GetType().FullName}");
        MainFile.Logger.Info($"球盒诊断：OrbInstanceType={orbInstance?.GetType().FullName ?? "null"}");
        LogTypeMembers("球盒诊断 OrbQueue", orbQueue.GetType());
        if (player.PlayerCombatState != null)
        {
            LogTypeMembers("球盒诊断 PlayerCombatState", player.PlayerCombatState.GetType());
        }

        LogTypeMembers("球盒诊断 OrbModel", info.OrbModel.GetType());
        if (orbInstance != null)
        {
            LogTypeMembers("球盒诊断 OrbInstance", orbInstance.GetType());
        }
    }

    /// <summary>
    /// 打印类型的主要方法和字段名，控制长度避免日志刷屏。
    /// </summary>
    private static void LogTypeMembers(string title, Type type)
    {
        string methods = string.Join(", ", type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(method => $"{method.Name}/{method.GetParameters().Length}")
            .Distinct()
            .Take(80));
        string fields = string.Join(", ", type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(field => field.Name)
            .Distinct()
            .Take(80));
        string properties = string.Join(", ", type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(property => property.Name)
            .Distinct()
            .Take(80));
        MainFile.Logger.Info($"{title}：方法=[{methods}]，字段=[{fields}]，属性=[{properties}]");
    }

    /// <summary>
    /// 拼接方法参数类型，方便阅读反射诊断日志。
    /// </summary>
    private static string BuildParameterText(IEnumerable<ParameterInfo> parameters)
    {
        return string.Join(", ", parameters.Select(parameter => $"{parameter.ParameterType.Name} {parameter.Name}"));
    }

    /// <summary>
    /// 根据可用参数为目标方法拼装调用参数。
    /// </summary>
    private static bool TryBuildArguments(ParameterInfo[] parameters, object?[] availableArgs, out object?[] args)
    {
        args = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            Type parameterType = parameters[i].ParameterType;
            object? matched = availableArgs.FirstOrDefault(arg => arg != null && parameterType.IsInstanceOfType(arg));
            if (matched != null)
            {
                args[i] = matched;
                continue;
            }

            if (parameterType == typeof(int) && availableArgs.OfType<int>().Any())
            {
                args[i] = availableArgs.OfType<int>().First();
                continue;
            }

            object? nullableMatched = availableArgs.FirstOrDefault(arg => arg != null && Nullable.GetUnderlyingType(parameterType)?.IsInstanceOfType(arg) == true);
            if (nullableMatched != null)
            {
                args[i] = nullableMatched;
                continue;
            }

            if (parameters[i].HasDefaultValue)
            {
                args[i] = parameters[i].DefaultValue;
                continue;
            }

            if (parameterType.IsValueType)
            {
                return false;
            }

            if (parameterType.Name.Contains("PlayerChoiceContext", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            args[i] = null;
        }

        return true;
    }

    /// <summary>
    /// 创建一个游戏命令可用的玩家选择上下文，避免 OrbCmd.Channel 在 Hook 阶段拿到空上下文。
    /// </summary>
    private static object? CreatePlayerChoiceContext()
    {
        foreach (string typeName in PlayerChoiceContextTypeNames)
        {
            Type? type = typeof(ModelDb).Assembly.GetType(typeName);
            if (type == null)
            {
                continue;
            }

            try
            {
                object? context = Activator.CreateInstance(type);
                MainFile.Logger.Info($"球盒创建 PlayerChoiceContext 成功：{type.FullName}");
                return context;
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"球盒创建 PlayerChoiceContext 失败：{type.FullName}，原因：{ex.Message}");
            }
        }

        foreach (Type type in FindPlayerChoiceContextTypes())
        {
            try
            {
                object? context = Activator.CreateInstance(type);
                MainFile.Logger.Info($"球盒扫描并创建 PlayerChoiceContext 成功：{type.FullName}");
                return context;
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"球盒扫描到 PlayerChoiceContext 但创建失败：{type.FullName}，原因：{ex.Message}");
            }
        }

        MainFile.Logger.Info("球盒创建 PlayerChoiceContext 失败：未找到可实例化的 ThrowingPlayerChoiceContext 类型。");
        return null;
    }

    /// <summary>
    /// 在游戏程序集里扫描可用于命令调用的 ThrowingPlayerChoiceContext 类型，避免命名空间变动导致发放失败。
    /// </summary>
    private static IReadOnlyList<Type> FindPlayerChoiceContextTypes()
    {
        try
        {
            List<Type> types = typeof(ModelDb).Assembly.GetTypes()
                .Where(type => type.Name.Equals("ThrowingPlayerChoiceContext", StringComparison.OrdinalIgnoreCase))
                .Where(type => !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
                .ToList();
            if (types.Count > 0)
            {
                MainFile.Logger.Info($"球盒扫描 PlayerChoiceContext 类型：{string.Join(", ", types.Select(type => type.FullName))}");
            }
            else
            {
                string candidates = string.Join(", ", typeof(ModelDb).Assembly.GetTypes()
                    .Where(type => type.Name.Contains("PlayerChoiceContext", StringComparison.OrdinalIgnoreCase))
                    .Select(type => type.FullName)
                    .Take(40));
                MainFile.Logger.Info($"球盒未扫描到 ThrowingPlayerChoiceContext，可见 PlayerChoiceContext 候选：{candidates}");
            }

            return types;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"球盒扫描 PlayerChoiceContext 类型失败：{ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// 判断名称、描述或 ID 是否匹配搜索关键字。
    /// </summary>
    private static bool MatchesKeyword(OrbDisplayInfo info, string keyword)
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
    /// 判断来源筛选是否命中，“全部”表示不过滤，“Mods”表示所有非本体来源。
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
    /// 读取对象上的本地化成员文本。
    /// </summary>
    private static string? ReadLocalizedMemberText(object target, IEnumerable<string> memberNames)
    {
        object? value = ReadMemberValue(target, memberNames);
        if (value is LocString locString)
        {
            return GmLocalizationService.ResolveText(locString, "解析充能球本地化文本失败 / Failed to resolve orb localization");
        }

        return value?.ToString();
    }

    /// <summary>
    /// 读取对象上的字符串成员。
    /// </summary>
    private static string ReadMemberText(object target, IEnumerable<string> memberNames, string fallback)
    {
        return SafeText(ReadMemberValue(target, memberNames)?.ToString(), fallback);
    }

    /// <summary>
    /// 读取对象上的贴图成员。
    /// </summary>
    private static Texture2D? ReadTexture(object target)
    {
        return ReadMemberValue(target, IconMemberNames) as Texture2D;
    }

    /// <summary>
    /// 读取对象上的整数成员。
    /// </summary>
    private static int ReadIntMember(object target, IEnumerable<string> memberNames, int fallback)
    {
        object? value = ReadMemberValue(target, memberNames);
        if (value is int intValue)
        {
            return intValue;
        }

        return int.TryParse(value?.ToString(), out int parsed) ? parsed : fallback;
    }

    /// <summary>
    /// 按名称读取对象的属性或字段。
    /// </summary>
    private static object? ReadMemberValue(object target, IEnumerable<string> memberNames)
    {
        Type type = target.GetType();
        foreach (string name in memberNames)
        {
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (property != null)
            {
                try
                {
                    return property.GetValue(property.GetGetMethod(true)?.IsStatic == true ? null : target);
                }
                catch
                {
                    continue;
                }
            }

            FieldInfo? field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (field != null)
            {
                try
                {
                    return field.GetValue(field.IsStatic ? null : target);
                }
                catch
                {
                    continue;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 按名称读取类型上的静态属性或字段。
    /// </summary>
    private static object? ReadStaticMemberValue(Type type, IEnumerable<string> memberNames)
    {
        foreach (string name in memberNames)
        {
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (property != null)
            {
                try
                {
                    return property.GetValue(null);
                }
                catch
                {
                    continue;
                }
            }

            FieldInfo? field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field != null)
            {
                try
                {
                    return field.GetValue(null);
                }
                catch
                {
                    continue;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 标准化搜索文本，避免大小写和首尾空格影响匹配。
    /// </summary>
    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    /// <summary>
    /// 空文本兜底。
    /// </summary>
    private static string SafeText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    /// <summary>
    /// 把程序集名转换成来源筛选键。
    /// </summary>
    private static string ToSourceKey(string? assemblyName)
    {
        return string.IsNullOrWhiteSpace(assemblyName) || assemblyName.Equals("sts2", StringComparison.OrdinalIgnoreCase)
            ? SourceBaseGameKey
            : assemblyName;
    }

    /// <summary>
    /// 把来源筛选键转换成页面展示文本。
    /// </summary>
    private static string ToSourceText(string sourceKey, string? assemblyName)
    {
        return sourceKey.Equals(SourceBaseGameKey, StringComparison.OrdinalIgnoreCase)
            ? GmUiText.Pick("游戏本体", "Base Game")
            : SafeText(assemblyName, "Mods");
    }

    /// <summary>
    /// 来源排序：本体优先，Mod 其次。
    /// </summary>
    private static int GetSourceOrder(string sourceKey)
    {
        return sourceKey.Equals(SourceBaseGameKey, StringComparison.OrdinalIgnoreCase) ? 0 : 1;
    }
}
