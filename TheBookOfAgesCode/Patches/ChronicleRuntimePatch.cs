using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.Services;

namespace TheBookOfAges.TheBookOfAgesCode.Patches;

/// <summary>
/// 岁月史书运行时补丁，处理无法通过控制台命令完成的持续倍率类功能。
/// </summary>
[HarmonyPatch]
public static class ChronicleDamagePatch
{
    /// <summary>
    /// 只修改最终执行伤害的集合重载，避免单目标等转发重载把倍率重复计算。
    /// </summary>
    public static IEnumerable<MethodBase> TargetMethods()
    {
        return typeof(CreatureCmd)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == nameof(CreatureCmd.Damage))
            .Where(ChroniclePatchHelper.IsPatchableMethod)
            .Where(method => method.GetParameters().Any(parameter => parameter.Name == "amount" && parameter.ParameterType == typeof(decimal)))
            .Where(method => method.GetParameters().Any(parameter => parameter.Name == "targets" && parameter.ParameterType == typeof(IEnumerable<Creature>)))
            .Where(method => method.GetParameters().Any(parameter => parameter.Name == "cardSource" && parameter.ParameterType == typeof(CardModel)));
    }

    /// <summary>
    /// 在伤害命令执行前按玩家/敌人倍率改写 amount 参数。
    /// </summary>
    public static void Prefix(object[] __args, MethodBase __originalMethod)
    {
        if (GmRunContextService.IsMultiplayer())
        {
            return;
        }

        ParameterInfo[] parameters = __originalMethod.GetParameters();
        int amountIndex = Array.FindIndex(parameters, parameter => parameter.Name == "amount");
        if (amountIndex < 0 || __args[amountIndex] is not decimal amount)
        {
            return;
        }

        Creature? target = FindFirstTarget(__args, parameters);
        Creature? dealer = FindCreatureArg(__args, parameters, "dealer");
        __args[amountIndex] = ChronicleService.ScaleDamage(amount, target, dealer);
    }

    /// <summary>
    /// 从 Harmony 参数数组中按参数名读取 Creature。
    /// </summary>
    private static Creature? FindCreatureArg(object[] args, ParameterInfo[] parameters, string name)
    {
        int index = Array.FindIndex(parameters, parameter => parameter.Name == name);
        return index >= 0 ? args[index] as Creature : null;
    }

    /// <summary>
    /// 集合重载没有单独的 target 参数；保留无施法者伤害的目标阵营判断，并避免重复枚举一次性序列。
    /// </summary>
    private static Creature? FindFirstTarget(object[] args, ParameterInfo[] parameters)
    {
        int index = Array.FindIndex(parameters, parameter => parameter.Name == "targets");
        if (index < 0 || args[index] is not IEnumerable<Creature> targets)
        {
            return null;
        }

        IReadOnlyList<Creature> targetList = targets as IReadOnlyList<Creature> ?? targets.ToList();
        args[index] = targetList;
        return targetList.FirstOrDefault();
    }
}

/// <summary>
/// 岁月史书商店价格补丁，在游戏原本价格计算后再套用倍率。
/// </summary>
[HarmonyPatch]
public static class ChronicleShopPricePatch
{
    /// <summary>
    /// 查找所有商店条目的 CalcCost 方法，覆盖卡牌、遗物、药水等不同商品类型。
    /// </summary>
    public static IEnumerable<MethodBase> TargetMethods()
    {
        return typeof(MerchantEntry).Assembly.GetTypes()
            .Where(type => typeof(MerchantEntry).IsAssignableFrom(type))
            .Select(type => type.GetMethod(nameof(MerchantEntry.CalcCost), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method != null && ChroniclePatchHelper.IsPatchableMethod(method))!;
    }

    /// <summary>
    /// 商店条目计算价格后，按岁月史书商店价格倍率改写基类私有 _cost 字段。
    /// </summary>
    public static void Postfix(MerchantEntry __instance)
    {
        try
        {
            FieldInfo? field = typeof(MerchantEntry).GetField("_cost", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.GetValue(__instance) is not int cost)
            {
                return;
            }

            field.SetValue(__instance, ChronicleService.ScaleShopPrice(cost));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书商店价格倍率应用失败：{ex.Message}");
        }
    }

}

/// <summary>
/// 岁月史书敌人行动补丁，在总是玩家回合开启时跳过敌人实际行动。
/// </summary>
[HarmonyPatch(typeof(Creature), nameof(Creature.TakeTurn))]
public static class ChronicleEnemyTakeTurnPatch
{
    /// <summary>
    /// 如果当前行动者是敌人，则直接返回完成任务，让战斗流程继续但敌人不执行动作。
    /// </summary>
    public static bool Prefix(Creature __instance, ref Task __result)
    {
        if (!ChronicleService.ShouldSkipEnemyTurn() || !__instance.IsEnemy)
        {
            return true;
        }

        __result = Task.CompletedTask;
        MainFile.Logger.Info($"岁月史书总是玩家回合跳过敌人行动：敌人={__instance.Name}。");
        return false;
    }
}

/// <summary>
/// 岁月史书奖励倍率补丁，在战斗和宝箱奖励生成后按倍率补足奖励。
/// </summary>
[HarmonyPatch(typeof(RewardsSet), nameof(RewardsSet.GenerateWithoutOffering))]
public static class ChronicleRewardMultiplierPatch
{
    private static readonly PropertyInfo? CardRewardOptionsProperty = typeof(CardReward).GetProperty("Options", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly PropertyInfo? CardRewardOptionCountProperty = typeof(CardReward).GetProperty("OptionCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    /// <summary>
    /// 包装原始异步奖励任务，等游戏先生成并填充原始奖励后再追加额外随机奖励。
    /// </summary>
    public static void Postfix(RewardsSet __instance, ref Task __result)
    {
        __result = ApplyRewardMultiplierAsync(__instance, __result);
    }

    /// <summary>
    /// 根据岁月史书奖励倍率补足奖励；金币复制原金额，药水、遗物、卡牌重新随机生成。
    /// </summary>
    private static async Task ApplyRewardMultiplierAsync(RewardsSet rewardsSet, Task originalTask)
    {
        await originalTask;
        List<Reward> rewards = rewardsSet.Rewards;
        int multiplier = ChronicleService.GetRewardMultiplier();
        if (multiplier <= 1 || !ShouldApplyToRoom(rewardsSet.Room))
        {
            return;
        }

        try
        {
            List<Reward> originals = rewards.ToList();
            int addedCount = 0;
            for (int copyIndex = 1; copyIndex < multiplier; copyIndex++)
            {
                foreach (Reward reward in originals)
                {
                    Reward? extraReward = CreateExtraReward(reward, rewardsSet.Player);
                    if (extraReward == null)
                    {
                        continue;
                    }

                    rewards.Add(extraReward);
                    addedCount++;
                }
            }

            MainFile.Logger.Info($"岁月史书奖励倍率已应用：房间={GetRoomLogText(rewardsSet.Room)}，倍率={multiplier}，原始奖励={originals.Count}，新增奖励={addedCount}。");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书奖励倍率应用失败：房间={GetRoomLogText(rewardsSet.Room)}，倍率={multiplier}，异常类型={ex.GetType().FullName}，异常信息={ex.Message}。");
        }

        return;
    }

    /// <summary>
    /// 判断奖励倍率是否应该影响当前房间；只处理普通战斗和宝箱，排除事件、商人和休息处。
    /// </summary>
    private static bool ShouldApplyToRoom(AbstractRoom? room)
    {
        if (room is TreasureRoom)
        {
            return true;
        }

        if (room is not CombatRoom combatRoom)
        {
            return false;
        }

        return combatRoom.ParentEventId == null || combatRoom.ParentEventId.Equals(ModelId.none);
    }

    /// <summary>
    /// 按原始奖励类型创建额外奖励；非目标类型保持为空。
    /// </summary>
    private static Reward? CreateExtraReward(Reward reward, Player player)
    {
        Reward? extraReward = reward switch
        {
            GoldReward goldReward => new GoldReward(Math.Max(0, goldReward.Amount), player, false),
            PotionReward => new PotionReward(player),
            RelicReward => new RelicReward(player),
            CardReward cardReward => CreateRandomCardReward(cardReward, player),
            _ => null,
        };

        if (extraReward != null)
        {
            extraReward.Populate();
        }

        return extraReward;
    }

    /// <summary>
    /// 基于原卡牌奖励的生成参数重新创建一份随机卡牌奖励，避免复制同一组卡牌。
    /// </summary>
    private static CardReward? CreateRandomCardReward(CardReward originalReward, Player player)
    {
        if (CardRewardOptionsProperty?.GetValue(originalReward) is not CardCreationOptions options)
        {
            MainFile.Logger.Info("岁月史书奖励倍率跳过额外卡牌奖励：无法读取原卡牌奖励生成参数。");
            return null;
        }

        int optionCount = CardRewardOptionCountProperty?.GetValue(originalReward) is int count ? count : 3;
        return new CardReward(options, Math.Max(1, optionCount), player);
    }

    /// <summary>
    /// 拼接房间日志文本，方便排查倍率是否错误影响了事件、商人或休息处。
    /// </summary>
    private static string GetRoomLogText(AbstractRoom? room)
    {
        return room == null
            ? "空房间"
            : $"{room.GetType().Name}/{room.RoomType}/{room.ModelId}";
    }
}

/// <summary>
/// 岁月史书地图点可点击判断补丁，开关开启时让地图节点尽量保持可点击状态。
/// </summary>
[HarmonyPatch]
public static class ChronicleMapPointAvailabilityPatch
{
    private static readonly HashSet<string> AvailabilityLogKeys = [];

    /// <summary>
    /// 查找地图点里疑似“是否可点击/可选择”的布尔方法。
    /// </summary>
    public static IEnumerable<MethodBase> TargetMethods()
    {
        return GetMapPointTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.ReturnType == typeof(bool))
            .Where(method => method.GetParameters().Length == 0)
            .Where(method => IsAvailabilityMethodName(method.Name))
            .Where(ChroniclePatchHelper.IsPatchableMethod);
    }

    /// <summary>
    /// 开关开启时把可点击判断改成 true；关闭时完全使用游戏原逻辑。
    /// </summary>
    public static void Postfix(ref bool __result, MethodBase __originalMethod)
    {
        if (!ChronicleService.IsAnyMapNodeClickableEnabled())
        {
            return;
        }

        __result = true;
        LogAvailabilityOverrideOnce(__originalMethod);
    }

    /// <summary>
    /// 判断方法名是否像地图点可点击状态判断。
    /// </summary>
    private static bool IsAvailabilityMethodName(string methodName)
    {
        return methodName.Equals("get_IsTravelable", StringComparison.Ordinal) ||
               methodName.Equals("IsInputAllowed", StringComparison.Ordinal) ||
               methodName.Contains("Can", StringComparison.OrdinalIgnoreCase) ||
               methodName.Contains("Selectable", StringComparison.OrdinalIgnoreCase) ||
               methodName.Contains("Available", StringComparison.OrdinalIgnoreCase) ||
               methodName.Contains("Reachable", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 放行可点击判断时按方法只记一次日志，避免地图刷新时刷屏。
    /// </summary>
    private static void LogAvailabilityOverrideOnce(MethodBase method)
    {
        string key = $"{method.DeclaringType?.FullName}.{method.Name}";
        if (!AvailabilityLogKeys.Add(key))
        {
            return;
        }

        MainFile.Logger.Info($"岁月史书任意地图点可点击：放行地图点可点击判断，方法={method.DeclaringType?.Name}.{method.Name}。");
    }

    /// <summary>
    /// 获取游戏地图点相关节点类型。
    /// </summary>
    internal static IEnumerable<Type> GetMapPointTypes()
    {
        Type baseType = typeof(NMapPoint);
        return new[] { baseType }.Concat(baseType.Assembly.GetTypes()
            .Where(type => baseType.IsAssignableFrom(type) && !type.IsAbstract));
    }

    /// <summary>
    /// 获取具体地图点节点类型，用于只拦截真实节点点击方法。
    /// </summary>
    internal static IEnumerable<Type> GetConcreteMapPointTypes()
    {
        Type baseType = typeof(NMapPoint);
        return baseType.Assembly.GetTypes()
            .Where(type => baseType.IsAssignableFrom(type) && !type.IsAbstract);
    }
}

/// <summary>
/// 岁月史书地图点点击补丁，开关开启时拦截原地图节点点击并跳到对应坐标。
/// </summary>
[HarmonyPatch]
public static class ChronicleMapPointClickPatch
{
    private static readonly HashSet<string> FailedCoordReadLogKeys = [];

    /// <summary>
    /// 查找地图点的输入、点击和选择方法。
    /// </summary>
    public static IEnumerable<MethodBase> TargetMethods()
    {
        return ChronicleMapPointAvailabilityPatch.GetMapPointTypes()
            .Distinct()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.ReturnType == typeof(void))
            .Where(IsClickCandidate)
            .Where(ChroniclePatchHelper.IsPatchableMethod);
    }

    /// <summary>
    /// 开关开启且本次确实是点击输入时，直接进入被点击地图点对应房间。
    /// </summary>
    public static bool Prefix(object __instance, object[] __args, MethodBase __originalMethod)
    {
        if (!ChronicleService.IsAnyMapNodeClickableEnabled())
        {
            return true;
        }

        if (!IsClickInput(__args))
        {
            return true;
        }

        if (!TryReadMapCoord(__instance, out MapCoord coord))
        {
            LogFailedCoordReadOnce(__instance, __originalMethod);
            return true;
        }

        MainFile.Logger.Info($"岁月史书任意地图点可点击：拦截地图点点击，方法={__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}，目标坐标=({coord.col},{coord.row})。");
        JumpToMapCoordAsync(coord);
        return false;
    }

    /// <summary>
    /// 异步进入地图坐标；点击回调本身是同步方法，所以这里用 async void 承接游戏异步流程。
    /// </summary>
    private static async void JumpToMapCoordAsync(MapCoord coord)
    {
        bool success = await ChronicleService.JumpToMapRoomCoordAsync(coord);
        MainFile.Logger.Info($"岁月史书任意地图点可点击：地图点点击跳转结束，目标坐标=({coord.col},{coord.row})，success={success}。");
    }

    /// <summary>
    /// 判断目标方法是否像地图点点击或输入处理。
    /// </summary>
    private static bool IsClickCandidate(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Any(parameter => typeof(InputEvent).IsAssignableFrom(parameter.ParameterType)))
        {
            return true;
        }

        return parameters.Length == 0 &&
               (method.Name.Equals("OnClick", StringComparison.Ordinal) ||
                method.Name.Equals("OnClicked", StringComparison.Ordinal) ||
                method.Name.Equals("OnPress", StringComparison.Ordinal) ||
                method.Name.Equals("OnPressed", StringComparison.Ordinal) ||
                method.Name.Equals("OnRelease", StringComparison.Ordinal) ||
                method.Name.Equals("OnReleased", StringComparison.Ordinal) ||
                method.Name.Equals("OnSelected", StringComparison.Ordinal) ||
                method.Name.Equals("OnChosen", StringComparison.Ordinal));
    }

    /// <summary>
    /// 判断本次调用是否来自玩家点击；无参数点击方法默认视为点击。
    /// </summary>
    private static bool IsClickInput(object[] args)
    {
        InputEvent? inputEvent = args.OfType<InputEvent>().FirstOrDefault();
        if (inputEvent == null)
        {
            return args.Length == 0;
        }

        return inputEvent switch
        {
            InputEventMouseButton mouseButton => mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Left,
            InputEventScreenTouch screenTouch => screenTouch.Pressed,
            InputEventAction inputAction => inputAction.Pressed,
            _ => false,
        };
    }

    /// <summary>
    /// 从地图点 Godot 节点中读取 MapCoord；优先找 MapPoint，再找 MapCoord。
    /// </summary>
    private static bool TryReadMapCoord(object mapPointNode, out MapCoord coord)
    {
        return TryReadMapCoordFromObject(mapPointNode, out coord, depth: 0);
    }

    /// <summary>
    /// 递归读取对象里的地图坐标；限制深度避免误扫太多 Godot 内部对象。
    /// </summary>
    private static bool TryReadMapCoordFromObject(object? value, out MapCoord coord, int depth)
    {
        coord = new MapCoord(0, 0);
        if (value == null || depth > 2)
        {
            return false;
        }

        if (value is MapCoord directCoord)
        {
            coord = directCoord;
            return true;
        }

        if (value is MapPoint mapPoint)
        {
            coord = mapPoint.coord;
            return true;
        }

        Type type = value.GetType();
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (!CouldContainMapCoord(property.PropertyType))
            {
                continue;
            }

            try
            {
                if (TryReadMapCoordFromObject(property.GetValue(value), out coord, depth + 1))
                {
                    return true;
                }
            }
            catch
            {
                // 某些 Godot 属性在未初始化时会抛异常，跳过继续找其他字段。
            }
        }

        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (!CouldContainMapCoord(field.FieldType))
            {
                continue;
            }

            try
            {
                if (TryReadMapCoordFromObject(field.GetValue(value), out coord, depth + 1))
                {
                    return true;
                }
            }
            catch
            {
                // 某些 Godot 字段读取可能失败，跳过继续找其他字段。
            }
        }

        return false;
    }

    /// <summary>
    /// 判断类型是否可能包含地图坐标，减少反射扫描范围。
    /// </summary>
    private static bool CouldContainMapCoord(Type type)
    {
        return type == typeof(MapCoord) ||
               type == typeof(MapPoint) ||
               typeof(MapPoint).IsAssignableFrom(type) ||
               type.FullName?.Contains("MapPoint", StringComparison.OrdinalIgnoreCase) == true ||
               type.FullName?.Contains("MapCoord", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// 坐标读取失败时只按方法记录一次，避免鼠标经过地图时刷屏。
    /// </summary>
    private static void LogFailedCoordReadOnce(object instance, MethodBase method)
    {
        string key = $"{instance.GetType().FullName}.{method.Name}";
        if (!FailedCoordReadLogKeys.Add(key))
        {
            return;
        }

        MainFile.Logger.Info($"岁月史书任意地图点可点击：无法从地图节点读取坐标，节点类型={instance.GetType().FullName}，方法={method.Name}。");
    }
}

/// <summary>
/// 岁月史书 Patch 辅助方法，集中处理 Harmony 启动期安全检查。
/// </summary>
[HarmonyPatch]
public static class ChronicleOrbLayoutGuardPatch
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        Type? orbManagerType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Orbs.NOrbManager");
        if (orbManagerType == null)
        {
            return [];
        }

        return orbManagerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => (method.Name == "TweenLayout" || method.Name == "AddOrbAnim") &&
                             ChroniclePatchHelper.IsPatchableMethod(method));
    }

    public static Exception? Finalizer(Exception? __exception, MethodBase __originalMethod)
    {
        if (__exception is not ArgumentOutOfRangeException)
        {
            return __exception;
        }

        if (GmRunContextService.IsMultiplayer())
        {
            MainFile.Logger.Info($"岁月史书充能球防崩补丁在多人模式下不拦截异常：方法={__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}，异常={__exception.Message}。");
            return __exception;
        }

        MainFile.Logger.Info($"岁月史书充能球防崩补丁拦截越界：方法={__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}，异常={__exception.Message}。");
        ChronicleService.TryRecoverOrbLayoutAfterException(__originalMethod.Name);
        return null;
    }
}

internal static class ChroniclePatchHelper
{
    /// <summary>
    /// 判断方法是否适合 Harmony Patch，避免抽象方法在模组初始化阶段导致加载失败。
    /// </summary>
    public static bool IsPatchableMethod(MethodInfo method)
    {
        return !method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody() != null;
    }
}
