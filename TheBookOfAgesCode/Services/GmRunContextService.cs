using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.UI;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// GM 运行上下文服务，统一处理单人/多人模式、本机玩家和功能可用性判断。
/// </summary>
public static class GmRunContextService
{
    /// <summary>
    /// 获取当前局内状态；没有有效局内状态时返回空。
    /// </summary>
    public static RunState? GetCurrentRunState()
    {
        RunManager? manager = RunManager.Instance;
        if (manager == null || !manager.IsInProgress || manager.IsGameOver)
        {
            return null;
        }

        return manager.DebugOnlyGetState();
    }

    /// <summary>
    /// 获取 GM 当前允许操作的玩家。多人模式下永远只返回本机玩家。
    /// </summary>
    public static Player? GetCurrentPlayer()
    {
        RunState? state = GetCurrentRunState();
        if (state == null)
        {
            return null;
        }

        if (IsMultiplayer())
        {
            try
            {
                return LocalContext.GetMe(state);
            }
            catch (Exception ex)
            {
                MainFile.Logger.Info($"GM 多人上下文获取本机玩家失败：NetId={LocalContext.NetId?.ToString() ?? "空"}，异常={ex.Message}。");
                return null;
            }
        }

        return LocalContext.GetMe(state) ?? state.Players.FirstOrDefault();
    }

    /// <summary>
    /// 判断当前是否处于多人模式。
    /// </summary>
    public static bool IsMultiplayer()
    {
        NetGameType type = GetNetGameType();
        return type == NetGameType.Host || type == NetGameType.Client;
    }

    /// <summary>
    /// 判断当前是否处于战斗中。
    /// </summary>
    public static bool IsInCombat()
    {
        return CombatManager.Instance?.DebugOnlyGetState() != null;
    }

    /// <summary>
    /// 获取当前网络模式文本，用于日志和 UI 提示。
    /// </summary>
    public static string GetNetworkModeText()
    {
        return GetNetGameType() switch
        {
            NetGameType.Host => GmUiText.Pick("房主", "Host"),
            NetGameType.Client => GmUiText.Pick("客户端", "Client"),
            NetGameType.Replay => GmUiText.Pick("回放", "Replay"),
            NetGameType.Singleplayer => GmUiText.Pick("单人", "Singleplayer"),
            _ => GmUiText.Pick("未知", "Unknown"),
        };
    }

    /// <summary>
    /// 判断指定 GM 功能在当前上下文中是否允许执行。
    /// </summary>
    /// <param name="feature">要判断的 GM 功能。</param>
    public static GmFeatureAvailability GetFeatureAvailability(GmFeature feature)
    {
        if (!IsMultiplayer())
        {
            return GmFeatureAvailability.Allow(GmFeatureExecutionMode.LocalDirect);
        }

        if (GetCurrentPlayer() == null)
        {
            return GmFeatureAvailability.Deny(GmUiText.MultiplayerLocalPlayerMissing);
        }

        if (IsNetworkLoading())
        {
            return GmFeatureAvailability.Deny(GmUiText.MultiplayerNetworkLoadingDisabled);
        }

        return feature switch
        {
            GmFeature.GrantCard => GetDeckCardMutationAvailability(allowCombat: true, inCombatReason: string.Empty, executionMode: GmFeatureExecutionMode.NetworkGameAction),
            GmFeature.RemoveCard => GetDeckCardMutationAvailability(allowCombat: true, inCombatReason: string.Empty, executionMode: GmFeatureExecutionMode.NetworkGameAction),
            GmFeature.UpgradeCard => GetDeckCardMutationAvailability(allowCombat: false, inCombatReason: GmUiText.MultiplayerCardUpgradeInCombatDisabled, executionMode: GmFeatureExecutionMode.NetworkGameAction),
            GmFeature.GrantRelic => GmFeatureAvailability.Allow(GmFeatureExecutionMode.NetworkConsoleCommand),
            GmFeature.RemoveRelic => GmFeatureAvailability.Allow(GmFeatureExecutionMode.NetworkConsoleCommand),
            GmFeature.GrantPotion => GetPotionGrantAvailability(),
            GmFeature.TriggerEvent => GetEventTriggerAvailability(),
            GmFeature.RoomTravel => DenyDangerous(GmUiText.MultiplayerRoomTravelDisabledInUnstableRoom),
            GmFeature.ActTravel => DenyDangerous(GmUiText.MultiplayerRoomTravelDisabledInUnstableRoom),
            GmFeature.SetPotionSlots => DenyDangerous(GmUiText.MultiplayerPotionSlotsDisabled),
            GmFeature.GrantOrb => GetCombatActionAvailability(GmUiText.OrbNotInCombat),
            GmFeature.ToggleBlight => DenyDangerous(GmUiText.MultiplayerBlightDisabled),
            GmFeature.EnchantCard => GetDeckCardMutationAvailability(allowCombat: false, inCombatReason: GmUiText.MultiplayerCardEnchantInCombatDisabled, executionMode: GmFeatureExecutionMode.NetworkGameAction),
            GmFeature.ApplyPower => GetCombatActionAvailability(GmUiText.PowerNotInCombat),
            GmFeature.SpawnMonster => GetCombatActionAvailability(GmUiText.MonsterNotInCombat),
            GmFeature.Chronicle => DenyDangerous(GmUiText.MultiplayerChronicleDisabled),
            _ => DenyDangerous(GmUiText.MultiplayerDangerousFeatureDisabled),
        };
    }

    /// <summary>
    /// 判断多人模式下原生控制台网络命令是否处于可提交状态。
    /// </summary>
    /// <param name="requireCombat">该命令是否必须在稳定战斗内提交。</param>
    /// <param name="notInCombatReason">需要战斗但当前不在战斗时展示给玩家的原因。</param>
    public static GmFeatureAvailability GetNetworkConsoleCommandAvailability(bool requireCombat, string notInCombatReason)
    {
        if (!IsMultiplayer())
        {
            return GmFeatureAvailability.Allow(GmFeatureExecutionMode.LocalDirect);
        }

        if (GetCurrentPlayer() == null)
        {
            return GmFeatureAvailability.Deny(GmUiText.MultiplayerLocalPlayerMissing);
        }

        if (IsNetworkLoading())
        {
            return GmFeatureAvailability.Deny(GmUiText.MultiplayerNetworkLoadingDisabled);
        }

        if (requireCombat)
        {
            RunState? state = GetCurrentRunState();
            CombatManager? combatManager = CombatManager.Instance;
            if (state?.CurrentRoom is not CombatRoom ||
                combatManager == null ||
                !combatManager.IsInProgress ||
                combatManager.IsOverOrEnding ||
                combatManager.DebugOnlyGetState() == null)
            {
                return GmFeatureAvailability.Deny(notInCombatReason);
            }
        }

        if (!IsActionQueueIdle())
        {
            return GmFeatureAvailability.Deny(GmUiText.MultiplayerActionQueueBusyDisabled);
        }

        return GmFeatureAvailability.Allow(GmFeatureExecutionMode.NetworkConsoleCommand);
    }

    /// <summary>
    /// 读取当前网络模式；读取失败时返回 None。
    /// </summary>
    private static NetGameType GetNetGameType()
    {
        try
        {
            return RunManager.Instance?.NetService?.Type ?? NetGameType.None;
        }
        catch
        {
            return NetGameType.None;
        }
    }

    /// <summary>
    /// 判断事件任意门是否处于多人模式下可提交同步动作的稳定状态。
    /// </summary>
    private static GmFeatureAvailability GetEventTriggerAvailability()
    {
        if (IsNetworkLoading())
        {
            return GmFeatureAvailability.Deny(GmUiText.MultiplayerNetworkLoadingDisabled);
        }

        RunState? state = GetCurrentRunState();
        AbstractRoom? currentRoom = state?.CurrentRoom;
        if (currentRoom == null)
        {
            return GmFeatureAvailability.Deny(GmUiText.MultiplayerEventDisabledInUnstableRoom);
        }

        if (IsInCombat() || currentRoom is CombatRoom || IsCombatRoomType(currentRoom))
        {
            return GmFeatureAvailability.Deny(GmUiText.MultiplayerEventDisabledInUnstableRoom);
        }

        if (!IsActionQueueIdle())
        {
            return GmFeatureAvailability.Deny(GmUiText.MultiplayerActionQueueBusyDisabled);
        }

        if (HasBlockingOverlayScreen())
        {
            return GmFeatureAvailability.Deny(GmUiText.MultiplayerEventDisabledInUnstableRoom);
        }

        MainFile.Logger.Info($"GM 联机事件任意门闸门放行：当前房间={GetRoomText(currentRoom)}，地图打开={IsMapOpen()}，覆盖层数量={GetOverlayScreenCount()}。");
        return GmFeatureAvailability.Allow(GmFeatureExecutionMode.NetworkGameAction);
    }

    /// <summary>
    /// 判断多人模式下牌组卡片修改是否处于可同步执行的稳定状态。
    /// </summary>
    /// <param name="allowCombat">是否允许战斗中提交；删卡只改 run 级牌组，可以沿用战斗同步队列。</param>
    /// <param name="inCombatReason">不允许战斗中提交时展示的原因。</param>
    /// <param name="executionMode">检查通过后的执行方式。</param>
    private static GmFeatureAvailability GetDeckCardMutationAvailability(bool allowCombat, string inCombatReason, GmFeatureExecutionMode executionMode)
    {
        if (IsNetworkLoading())
        {
            return GmFeatureAvailability.Deny(GmUiText.MultiplayerNetworkLoadingDisabled);
        }

        RunState? state = GetCurrentRunState();
        if (!allowCombat && (IsInCombat() || state?.CurrentRoom is CombatRoom))
        {
            return GmFeatureAvailability.Deny(inCombatReason);
        }

        return GmFeatureAvailability.Allow(executionMode);
    }

    /// <summary>
    /// 判断多人模式下药水发放是否可提交同步动作。
    /// </summary>
    private static GmFeatureAvailability GetPotionGrantAvailability()
    {
        if (IsNetworkLoading())
        {
            return GmFeatureAvailability.Deny(GmUiText.MultiplayerNetworkLoadingDisabled);
        }

        return GmFeatureAvailability.Allow(GmFeatureExecutionMode.NetworkGameAction);
    }

    /// <summary>
    /// 判断多人模式下战斗内同步动作是否处于可提交的稳定状态。
    /// </summary>
    /// <param name="notInCombatReason">不在有效战斗中时展示给玩家的原因。</param>
    private static GmFeatureAvailability GetCombatActionAvailability(string notInCombatReason)
    {
        if (IsNetworkLoading())
        {
            return GmFeatureAvailability.Deny(GmUiText.MultiplayerNetworkLoadingDisabled);
        }

        RunState? state = GetCurrentRunState();
        CombatManager? combatManager = CombatManager.Instance;
        if (state?.CurrentRoom is not CombatRoom ||
            combatManager == null ||
            !combatManager.IsInProgress ||
            combatManager.IsOverOrEnding ||
            combatManager.DebugOnlyGetState() == null)
        {
            return GmFeatureAvailability.Deny(notInCombatReason);
        }

        if (!IsActionQueueIdle())
        {
            return GmFeatureAvailability.Deny(GmUiText.MultiplayerActionQueueBusyDisabled);
        }

        return GmFeatureAvailability.Allow(GmFeatureExecutionMode.NetworkGameAction);
    }

    /// <summary>
    /// 判断多人网络是否正在加载同步。
    /// </summary>
    private static bool IsNetworkLoading()
    {
        try
        {
            return RunManager.Instance?.NetService?.IsGameLoading ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 判断官方网络动作队列是否空闲。
    /// </summary>
    private static bool IsActionQueueIdle()
    {
        try
        {
            return RunManager.Instance?.ActionQueueSet?.IsEmpty ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 判断当前房间类型是否属于战斗房；读取失败时保守认为不是战斗类型。
    /// </summary>
    /// <param name="room">当前房间。</param>
    private static bool IsCombatRoomType(AbstractRoom room)
    {
        try
        {
            return room.RoomType.IsCombatRoom();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 判断是否有奖励、选牌等覆盖层挡在当前房间流程上。
    /// </summary>
    private static bool HasBlockingOverlayScreen()
    {
        return GetOverlayScreenCount() > 0;
    }

    /// <summary>
    /// 获取当前覆盖层数量；读取失败时按没有覆盖层处理。
    /// </summary>
    private static int GetOverlayScreenCount()
    {
        try
        {
            return NOverlayStack.Instance?.ScreenCount ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 判断地图界面当前是否打开；只用于日志排查，不直接拦截事件任意门。
    /// </summary>
    private static bool IsMapOpen()
    {
        try
        {
            return NMapScreen.Instance?.IsOpen ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 构造当前房间日志文本，避免联机排查时只看到空泛状态。
    /// </summary>
    /// <param name="room">当前房间。</param>
    private static string GetRoomText(AbstractRoom room)
    {
        try
        {
            return $"{room.GetType().Name}/{room.RoomType}/{room.ModelId}";
        }
        catch (Exception ex)
        {
            return $"读取失败:{ex.GetType().Name}";
        }
    }

    /// <summary>
    /// 创建多人模式下危险功能的禁用结果。
    /// </summary>
    /// <param name="reason">禁用原因。</param>
    private static GmFeatureAvailability DenyDangerous(string reason)
    {
        return GmFeatureAvailability.Deny(reason);
    }
}
