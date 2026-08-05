using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.GameActions;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.UI;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// 事件服务，负责读取游戏中所有事件（包括?房间事件和先古之民事件），
/// 并提供通过控制台命令触发事件的能力。
/// </summary>
public sealed class EventService
{
    private const string AllFilterKey = "all";
    private const string CategoryAncientKey = "ancient";
    private const string CategoryMysteryKey = "mystery";
    private const string CategoryModKey = "mod";
    private const string SourceBaseGameKey = "base-game";
    private const string SourceModsKey = "mods";
    /// <summary>
    /// 先古之民事件控制台命令，用于触发先古之民事件。
    /// </summary>
    private readonly AncientConsoleCmd _ancientConsoleCmd = new();

    /// <summary>
    /// 普通事件控制台命令，用于触发?房间事件。
    /// </summary>
    private readonly EventConsoleCmd _eventConsoleCmd = new();

    /// <summary>
    /// 阶段控制台命令，用于跳转到指定阶段。
    /// </summary>
    private readonly ActConsoleCmd _actConsoleCmd = new();

    /// <summary>
    /// 房间控制台命令，用于跳转到宝箱、商店、营火等房间。
    /// </summary>
    private readonly RoomConsoleCmd _roomConsoleCmd = new();

    /// <summary>
    /// 获取所有事件（?房间事件 + 先古之民事件），按分类和名称排序。
    /// 支持关键词搜索和分类/来源筛选。
    /// </summary>
    public IReadOnlyList<EventDisplayInfo> GetAllEvents(string keyword, string categoryFilterKey, string sourceFilterKey)
    {
        string normalizedKeyword = Normalize(keyword);
        List<EventDisplayInfo> result = [];

        // 读取所有?房间事件（普通事件）
        result.AddRange(
            ModelDb.AllEvents
                .Select(evt => BuildEventDisplayInfo(evt, isAncient: false))
                .Where(info => info != null)!);

        // 读取所有先古之民事件
        result.AddRange(
            ModelDb.AllAncients
                .Select(evt => BuildAncientDisplayInfo(evt))
                .Where(info => info != null)!);

        // 筛选 + 排序：先古之民 → ?房间 → Mod，然后按名称排序
        return result
            .Where(info => MatchesKeyword(info, normalizedKeyword))
            .Where(info => MatchesCategoryFilter(info.CategoryKey, categoryFilterKey))
            .Where(info => MatchesSourceFilter(info.SourceKey, sourceFilterKey))
            .OrderBy(info => GetCategoryOrder(info.CategoryKey))
            .ThenBy(info => GetSourceOrder(info.SourceKey))
            .ThenBy(info => info.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 获取事件分类筛选选项列表，动态从当前事件数据中收集。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetCategoryOptions()
    {
        List<GmFilterOption> options = [new GmFilterOption(AllFilterKey, GmUiText.Pick("全部事件", "All Events"))];

        HashSet<string> categoryKeys = [];

        foreach (EventModel _ in ModelDb.AllAncients)
        {
            categoryKeys.Add(CategoryAncientKey);
        }

        foreach (EventModel evt in ModelDb.AllEvents)
        {
            string sourceKey = ToSourceKey(evt.GetType().Assembly.GetName().Name);
            categoryKeys.Add(sourceKey.Equals(SourceBaseGameKey, StringComparison.OrdinalIgnoreCase) ? CategoryMysteryKey : CategoryModKey);
        }

        options.AddRange(categoryKeys
            .OrderBy(GetCategoryOrder)
            .Select(key => new GmFilterOption(key, ToCategoryText(key))));
        return options;
    }

    /// <summary>
    /// 获取事件来源筛选选项列表，动态从当前事件数据中收集。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetSourceOptions()
    {
        List<GmFilterOption> options = [new GmFilterOption(AllFilterKey, GmUiText.Pick("全部来源", "All Sources"))];

        // 收集所有不重复的来源
        HashSet<string> sourceKeys = [];
        foreach (EventModel evt in ModelDb.AllEvents)
        {
            sourceKeys.Add(ToSourceKey(evt.GetType().Assembly.GetName().Name));
        }

        foreach (EventModel evt in ModelDb.AllAncients)
        {
            sourceKeys.Add(ToSourceKey(evt.GetType().Assembly.GetName().Name));
        }

        options.AddRange(sourceKeys
            .Select(key => new GmFilterOption(key, ToSourceText(key, GetAssemblyNameFromSourceKey(key))))
            .OrderBy(option => GetSourceOrder(option.Key))
            .ThenBy(option => option.Text, StringComparer.CurrentCultureIgnoreCase));

        // 最后追加一个汇总 Mod 选项
        if (!options.Any(option => option.Key.Equals(SourceModsKey, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new GmFilterOption(SourceModsKey, "Mods"));
        }

        return options;
    }

    /// <summary>
    /// 触发指定事件，根据事件类型调用不同的控制台命令。
    /// 先古之民事件走 AncientConsoleCmd，?房间事件走 EventConsoleCmd。
    /// </summary>
    public async Task<bool> TriggerEventAsync(EventDisplayInfo eventInfo)
    {
        MainFile.Logger.Info($"事件触发开始：事件名称={eventInfo.Name}，完整事件ID={eventInfo.IdText}，控制台事件ID={eventInfo.CommandIdText}，分类={eventInfo.Category}，来源={eventInfo.Source}，是否先古之民事件={eventInfo.IsAncient}。");
        LogRunState("事件触发前");

        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            MainFile.Logger.Info($"事件触发中止：没有拿到当前玩家。请检查是否已经进入一局游戏、游戏是否已经结束、RunState.Players 是否为空。完整事件ID={eventInfo.IdText}，控制台事件ID={eventInfo.CommandIdText}。");
            return false;
        }

        if (GmRunContextService.IsMultiplayer())
        {
            GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(GmFeature.TriggerEvent);
            if (!availability.IsAllowed)
            {
                MainFile.Logger.Info($"事件触发已被多人安全闸门阻止：事件={eventInfo.Name}，原因={availability.Reason}。");
                return false;
            }

            string commandName = eventInfo.IsAncient ? _ancientConsoleCmd.CmdName : _eventConsoleCmd.CmdName;
            MainFile.Logger.Info($"事件触发准备提交 GM 同步动作：事件={eventInfo.Name}，命令={commandName}，控制台事件ID={eventInfo.CommandIdText}，执行方式={availability.ExecutionMode}。");
            return await GmNetworkCommandService.TryEnqueueGameActionAsync(
                new GmEventTriggerAction(player, commandName, eventInfo.CommandIdText, eventInfo.Name, eventInfo.IsAncient),
                "事件任意门");
        }

        try
        {
            await PrepareRunForEventAsync();

            CmdResult cmdResult;
            string commandName;

            if (eventInfo.IsAncient)
            {
                // 先古之民事件使用 ancient 命令
                commandName = _ancientConsoleCmd.CmdName;
                MainFile.Logger.Info($"准备调用事件控制台命令：命令={commandName}，参数=[{eventInfo.CommandIdText}]，完整事件ID={eventInfo.IdText}，说明=先古之民事件必须继续走 AncientConsoleCmd。");
                cmdResult = _ancientConsoleCmd.Process(player, [eventInfo.CommandIdText]);
            }
            else
            {
                // 普通?房间事件使用 event 命令
                commandName = _eventConsoleCmd.CmdName;
                MainFile.Logger.Info($"准备调用事件控制台命令：命令={commandName}，参数=[{eventInfo.CommandIdText}]，完整事件ID={eventInfo.IdText}，说明=普通事件必须继续走 EventConsoleCmd。");
                cmdResult = _eventConsoleCmd.Process(player, [eventInfo.CommandIdText]);
            }

            MainFile.Logger.Info($"事件控制台命令已返回：命令={commandName}，完整事件ID={eventInfo.IdText}，控制台事件ID={eventInfo.CommandIdText}，success={cmdResult.success}，msg={SafeLogText(cmdResult.msg)}，是否包含异步任务={cmdResult.task != null}。");

            // 等待异步任务完成（如果有的话）
            if (cmdResult.task != null)
            {
                MainFile.Logger.Info($"事件控制台命令开始等待异步任务：命令={commandName}，完整事件ID={eventInfo.IdText}，控制台事件ID={eventInfo.CommandIdText}。");
                await cmdResult.task;
                MainFile.Logger.Info($"事件控制台命令异步任务完成：命令={commandName}，完整事件ID={eventInfo.IdText}，控制台事件ID={eventInfo.CommandIdText}。");
            }

            if (cmdResult.success)
            {
                MainFile.Logger.Info($"事件触发成功：事件名称={eventInfo.Name}，完整事件ID={eventInfo.IdText}，控制台事件ID={eventInfo.CommandIdText}，命令={commandName}。");
                LogRunState("事件触发成功后");
            }
            else
            {
                MainFile.Logger.Info($"事件触发失败：控制台命令返回失败。事件名称={eventInfo.Name}，完整事件ID={eventInfo.IdText}，控制台事件ID={eventInfo.CommandIdText}，命令={commandName}，失败信息={SafeLogText(cmdResult.msg)}。");
                LogRunState("事件触发失败后");
            }

            return cmdResult.success;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"事件触发异常：事件名称={eventInfo.Name}，完整事件ID={eventInfo.IdText}，控制台事件ID={eventInfo.CommandIdText}，异常类型={ex.GetType().FullName}，异常信息={ex.Message}，堆栈={SafeLogText(ex.StackTrace)}。");
            LogRunState("事件触发异常后");
            return false;
        }
    }

    /// <summary>
    /// 跳转到指定阶段，优先使用游戏自带 act 控制台命令。
    /// </summary>
    public async Task<bool> JumpToActAsync(string actCommandArg, string displayName)
    {
        return await RunConsoleCommandAsync("阶段跳转", _actConsoleCmd, [actCommandArg], displayName, GmFeature.ActTravel);
    }

    /// <summary>
    /// 跳转到指定房间，优先使用游戏自带 room 控制台命令。
    /// </summary>
    public async Task<bool> JumpToRoomAsync(string roomCommandArg, string displayName)
    {
        return await RunConsoleCommandAsync("房间跳转", _roomConsoleCmd, [roomCommandArg], displayName, GmFeature.RoomTravel);
    }

    /// <summary>
    /// 统一执行控制台命令，并等待命令可能返回的异步任务。
    /// </summary>
    private async Task<bool> RunConsoleCommandAsync(string actionName, AbstractConsoleCmd consoleCmd, string[] args, string displayName, GmFeature feature)
    {
        MainFile.Logger.Info($"{actionName}开始：目标={displayName}，命令={consoleCmd.CmdName}，参数=[{string.Join(", ", args)}]。");
        LogRunState($"{actionName}前");

        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            MainFile.Logger.Info($"{actionName}中止：没有拿到当前玩家。请检查是否已经进入一局游戏、游戏是否已经结束、RunState.Players 是否为空。目标={displayName}。");
            return false;
        }

        if (GmRunContextService.IsMultiplayer())
        {
            GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(feature);
            if (!availability.IsAllowed)
            {
                MainFile.Logger.Info($"{actionName}已被多人安全闸门阻止：目标={displayName}，原因={availability.Reason}。");
                return false;
            }

            return await GmNetworkCommandService.TryEnqueueConsoleCommandAsync(player, BuildConsoleCommandText(consoleCmd.CmdName, args), actionName);
        }

        try
        {
            await PrepareRunForEventAsync();

            CmdResult cmdResult = consoleCmd.Process(player, args);
            MainFile.Logger.Info($"{actionName}控制台命令已返回：目标={displayName}，命令={consoleCmd.CmdName}，success={cmdResult.success}，msg={SafeLogText(cmdResult.msg)}，是否包含异步任务={cmdResult.task != null}。");

            // 控制台命令有些会把真正跳转放到 task 里，这里统一等它跑完。
            if (cmdResult.task != null)
            {
                await cmdResult.task;
                MainFile.Logger.Info($"{actionName}控制台命令异步任务完成：目标={displayName}，命令={consoleCmd.CmdName}。");
            }

            if (cmdResult.success)
            {
                LogRunState($"{actionName}成功后");
            }
            else
            {
                LogRunState($"{actionName}失败后");
            }

            return cmdResult.success;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"{actionName}异常：目标={displayName}，命令={consoleCmd.CmdName}，异常类型={ex.GetType().FullName}，异常信息={ex.Message}，堆栈={SafeLogText(ex.StackTrace)}。");
            LogRunState($"{actionName}异常后");
            return false;
        }
    }

    /// <summary>
    /// 拼接可交给原生控制台执行的命令文本。
    /// </summary>
    private static string BuildConsoleCommandText(string commandName, string[] args)
    {
        return args.Length == 0
            ? commandName
            : $"{commandName} {string.Join(" ", args)}";
    }

    /// <summary>
    /// 触发事件前先整理当前运行状态：如果战斗奖励房间还没真正离开，就先正常离开房间。
    /// 这里不会替玩家选择卡牌，只等价于点击奖励页的继续/离开。
    /// </summary>
    private static async Task PrepareRunForEventAsync()
    {
        RunManager? manager = RunManager.Instance;
        RunState? state = manager?.DebugOnlyGetState();
        if (manager == null || state == null)
        {
            MainFile.Logger.Info("事件触发预处理：RunManager 或 RunState 为空，跳过离开当前房间处理。");
            return;
        }

        if (state.CurrentRoom is CombatRoom)
        {
            MainFile.Logger.Info($"事件触发预处理：当前仍在战斗房间，准备调用 ProceedFromTerminalRewardsScreen 正常离开。当前房间={GetCurrentRoomText(state, state.CurrentRoomCount)}。");
            await manager.ProceedFromTerminalRewardsScreen();
            MainFile.Logger.Info("事件触发预处理：已调用 ProceedFromTerminalRewardsScreen，未替玩家选择卡牌，未自动领取卡牌奖励。");
            LogRunState("离开战斗房间后");
        }

        CloseMapAndOverlayScreens();
    }

    /// <summary>
    /// 关闭可能还盖在事件房间上面的地图和覆盖层，避免事件已经进入但画面还停在地图/奖励界面。
    /// </summary>
    private static void CloseMapAndOverlayScreens()
    {
        try
        {
            NMapScreen? mapScreen = NMapScreen.Instance;
            if (mapScreen != null && mapScreen.IsOpen)
            {
                MainFile.Logger.Info("事件触发预处理：检测到地图页面仍打开，准备关闭地图页面，避免遮住事件房间。");
                mapScreen.Close();
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"事件触发预处理：关闭地图页面失败，继续尝试触发事件。异常类型={ex.GetType().FullName}，异常信息={ex.Message}。");
        }

        try
        {
            NOverlayStack? overlayStack = NOverlayStack.Instance;
            if (overlayStack != null && overlayStack.ScreenCount > 0)
            {
                MainFile.Logger.Info($"事件触发预处理：检测到覆盖层数量={overlayStack.ScreenCount}，准备移除残留覆盖层，避免奖励/选牌界面挡住事件。");
                RemoveResidualOverlays(overlayStack);
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"事件触发预处理：移除覆盖层失败，继续尝试触发事件。异常类型={ex.GetType().FullName}，异常信息={ex.Message}。");
        }
    }

    /// <summary>
    /// 逐个移除覆盖层栈中的残留界面，避免只隐藏界面却留下透明输入层挡住事件按钮。
    /// </summary>
    private static void RemoveResidualOverlays(NOverlayStack overlayStack)
    {
        System.Reflection.FieldInfo? overlaysField = typeof(NOverlayStack).GetField("_overlays", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (overlaysField?.GetValue(overlayStack) is not IEnumerable<IOverlayScreen> overlays)
        {
            MainFile.Logger.Info("事件触发预处理：无法读取覆盖层列表，只隐藏覆盖层兜底。");
            overlayStack.HideOverlays();
            overlayStack.HideBackstop();
            return;
        }

        List<IOverlayScreen> residualOverlays = overlays.ToList();
        foreach (IOverlayScreen overlay in residualOverlays)
        {
            MainFile.Logger.Info($"事件触发预处理：移除残留覆盖层，类型={overlay.GetType().FullName}。");
            overlayStack.Remove(overlay);
        }

        overlayStack.HideBackstop();
        MainFile.Logger.Info($"事件触发预处理：残留覆盖层移除完成，移除数量={residualOverlays.Count}，剩余覆盖层数量={overlayStack.ScreenCount}。");
    }

    /// <summary>
    /// 记录当前局内状态，方便判断事件触发失败是不是卡在局内、玩家或房间栈状态。
    /// </summary>
    private static void LogRunState(string stage)
    {
        RunManager? manager = RunManager.Instance;
        if (manager == null)
        {
            MainFile.Logger.Info($"{stage}：RunManager.Instance 为空，说明当前没有可用的局内管理器。");
            return;
        }

        RunState? state = manager.DebugOnlyGetState();
        int playerCount = state?.Players.Count ?? 0;
        int currentRoomCount = state?.CurrentRoomCount ?? -1;
        string currentRoomText = GetCurrentRoomText(state, currentRoomCount);
        MainFile.Logger.Info($"{stage}：RunManager 存在，IsInProgress={manager.IsInProgress}，IsGameOver={manager.IsGameOver}，RunState是否为空={state == null}，玩家数量={playerCount}，当前房间栈数量={currentRoomCount}，当前房间={currentRoomText}。");
    }

    /// <summary>
    /// 获取当前房间的简要文本，避免日志里只看到房间栈数量却不知道实际房间类型。
    /// </summary>
    private static string GetCurrentRoomText(RunState? state, int currentRoomCount)
    {
        if (state == null)
        {
            return "RunState为空";
        }

        if (currentRoomCount <= 0)
        {
            return "没有当前房间";
        }

        try
        {
            MegaCrit.Sts2.Core.Rooms.AbstractRoom? currentRoom = state.CurrentRoom;
            if (currentRoom == null)
            {
                return "当前房间为空";
            }

            return $"{currentRoom.GetType().Name}/{currentRoom.RoomType}/{currentRoom.ModelId}";
        }
        catch (Exception ex)
        {
            return $"读取当前房间失败：{ex.GetType().Name}，{ex.Message}";
        }
    }

    /// <summary>
    /// 清理日志文本，避免空文本或换行把排查日志切得太碎。
    /// </summary>
    private static string SafeLogText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "空";
        }

        return value.Replace("\r", "\\r").Replace("\n", "\\n");
    }

    /// <summary>
    /// 获取当前玩家；没有局内状态时返回空。
    /// </summary>
    private static Player? GetCurrentPlayer()
    {
        return GmRunContextService.GetCurrentPlayer();
    }

    /// <summary>
    /// 将普通事件模型转换成页面展示数据。
    /// 自动判断是游戏本体事件还是 Mod 事件来决定分类。
    /// </summary>
    private static EventDisplayInfo? BuildEventDisplayInfo(EventModel eventModel, bool isAncient)
    {
        string idText = eventModel.Id.ToString();
        string commandIdText = eventModel.Id.Entry;
        string? name = GetLocalizedText(eventModel.Title);
        string sourceKey = ToSourceKey(eventModel.GetType().Assembly.GetName().Name);
        string source = ToSourceText(sourceKey, eventModel.GetType().Assembly.GetName().Name);

        // 根据来源决定是?房间事件还是 Mod 事件
        string categoryKey = isAncient
            ? CategoryAncientKey
            : sourceKey.Equals(SourceBaseGameKey, StringComparison.OrdinalIgnoreCase)
                ? CategoryMysteryKey
                : CategoryModKey;

        return new EventDisplayInfo
        {
            IdText = idText,
            CommandIdText = commandIdText,
            Name = SafeText(name, idText),
            CategoryKey = categoryKey,
            Category = ToCategoryText(categoryKey),
            SourceKey = sourceKey,
            Source = source,
            IsAncient = isAncient,
        };
    }

    /// <summary>
    /// 将先古之民事件模型转换成页面展示数据。
    /// 先古之民事件统一归类为"先古之民事件"分类。
    /// </summary>
    private static EventDisplayInfo? BuildAncientDisplayInfo(EventModel ancientModel)
    {
        return BuildEventDisplayInfo(ancientModel, isAncient: true);
    }

    /// <summary>
    /// 判断事件名称是否匹配搜索关键字，支持中英文模糊检索。
    /// </summary>
    private static bool MatchesKeyword(EventDisplayInfo info, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        // 同时匹配名称和 ID，方便用户用 ID 精确搜索
        return Normalize(info.Name).Contains(keyword) ||
               Normalize(info.IdText).Contains(keyword) ||
               Normalize(info.CommandIdText).Contains(keyword);
    }

    /// <summary>
    /// 判断分类筛选是否命中，"全部"表示不过滤。
    /// </summary>
    private static bool MatchesCategoryFilter(string valueKey, string filterKey)
    {
        return string.IsNullOrWhiteSpace(filterKey) ||
               filterKey.Equals(AllFilterKey, StringComparison.OrdinalIgnoreCase) ||
               valueKey.Equals(filterKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断来源筛选是否命中，本体和 Mod 分开处理。
    /// </summary>
    private static bool MatchesSourceFilter(string valueKey, string filterKey)
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
    /// 转换来源文本，本体、当前 Mod、其他 Mod 分开显示。
    /// </summary>
    private static string ToCategoryText(string categoryKey)
    {
        return categoryKey switch
        {
            CategoryAncientKey => GmUiText.Pick("先古之民事件", "Ancient Events"),
            CategoryMysteryKey => GmUiText.Pick("?房间事件", "Mystery Events"),
            CategoryModKey => GmUiText.Pick("Mod 事件", "Mod Events"),
            _ => categoryKey,
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
    /// 分类排序权重，先古之民事件排最前，然后?房间事件，最后 Mod 事件。
    /// </summary>
    private static int GetCategoryOrder(string categoryKey)
    {
        if (categoryKey.Equals(CategoryAncientKey, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (categoryKey.Equals(CategoryMysteryKey, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (categoryKey.Equals(CategoryModKey, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 9;
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
    /// 规范化搜索文本，转小写并去除首尾空格，避免大小写影响检索。
    /// </summary>
    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// 给空文本提供兜底文案，避免页面出现空白按钮。
    /// </summary>
    private static string SafeText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    /// <summary>
    /// 解析游戏本地化文本，按玩家当前语言返回可读内容。
    /// 优先使用格式化文本，失败后退到原始文本。
    /// </summary>
    private static string? GetLocalizedText(MegaCrit.Sts2.Core.Localization.LocString? locString)
    {
        return GmLocalizationService.ResolveText(locString, "解析事件本地化文本失败 / Failed to resolve event localized text");
    }

    /// <summary>
    /// 从来源键值里还原程序集名，便于生成展示文本。
    /// </summary>
    private static string? GetAssemblyNameFromSourceKey(string sourceKey)
    {
        return sourceKey.StartsWith("mod:", StringComparison.OrdinalIgnoreCase)
            ? sourceKey["mod:".Length..]
            : null;
    }
}
