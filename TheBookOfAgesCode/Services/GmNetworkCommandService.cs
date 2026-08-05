using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// GM 多人网络命令服务，负责把安全的原生控制台命令放入游戏官方网络动作队列。
/// </summary>
public static class GmNetworkCommandService
{
    /// <summary>
    /// 在多人模式下把原生控制台命令放入官方网络动作队列。
    /// </summary>
    /// <param name="player">命令所属玩家，多人模式下必须是本机玩家。</param>
    /// <param name="command">完整控制台命令文本。</param>
    /// <param name="actionName">用于日志展示的动作名称。</param>
    public static Task<bool> TryEnqueueConsoleCommandAsync(Player player, string command, string actionName)
    {
        if (!GmRunContextService.IsMultiplayer())
        {
            return Task.FromResult(false);
        }

        try
        {
            RunManager? manager = RunManager.Instance;
            if (manager?.ActionQueueSynchronizer == null)
            {
                MainFile.Logger.Info($"GM 多人命令提交失败：动作={actionName}，网络模式={GmRunContextService.GetNetworkModeText()}，玩家NetId={player.NetId}，命令={command}，原因=ActionQueueSynchronizer 为空。");
                return Task.FromResult(false);
            }

            bool inCombat = GmRunContextService.IsInCombat();
            ConsoleCmdGameAction action = new(player, command, inCombat);
            string queueState = BuildQueueStateText(manager);
            manager.ActionQueueSynchronizer.RequestEnqueue(action);
            MainFile.Logger.Info($"GM 多人命令已提交同步：动作={actionName}，网络模式={GmRunContextService.GetNetworkModeText()}，玩家NetId={player.NetId}，战斗中={inCombat}，房间={GetCurrentRoomName()}，队列={queueState}，ActionType={action.ActionType}，命令={command}，结果=已提交到官方队列，注意=提交成功不代表已执行完成。");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 多人命令提交异常：动作={actionName}，网络模式={GmRunContextService.GetNetworkModeText()}，玩家NetId={player.NetId}，命令={command}，异常={ex.Message}。");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 在多人模式下把自定义游戏动作放入官方网络动作队列。
    /// </summary>
    /// <param name="action">要入队的游戏动作。</param>
    /// <param name="actionName">用于日志展示的动作名称。</param>
    public static Task<bool> TryEnqueueGameActionAsync(GameAction action, string actionName)
    {
        if (!GmRunContextService.IsMultiplayer())
        {
            return Task.FromResult(false);
        }

        try
        {
            RunManager? manager = RunManager.Instance;
            if (manager?.ActionQueueSynchronizer == null)
            {
                MainFile.Logger.Info($"GM 多人动作提交失败：动作={actionName}，网络模式={GmRunContextService.GetNetworkModeText()}，玩家NetId={action.OwnerId}，GameActionType={action.GetType().Name}，ActionType={action.ActionType}，原因=ActionQueueSynchronizer 为空。");
                return Task.FromResult(false);
            }

            INetAction netAction = action.ToNetAction();
            string queueState = BuildQueueStateText(manager);
            bool inCombat = GmRunContextService.IsInCombat();
            manager.ActionQueueSynchronizer.RequestEnqueue(action);
            MainFile.Logger.Info($"GM 多人动作已提交同步：动作={actionName}，网络模式={GmRunContextService.GetNetworkModeText()}，玩家NetId={action.OwnerId}，战斗中={inCombat}，房间={GetCurrentRoomName()}，队列={queueState}，GameActionType={action.GetType().Name}，NetActionType={netAction.GetType().Name}，ActionType={action.ActionType}，结果=已提交到官方队列，注意=提交成功不代表已执行完成。");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 多人动作提交异常：动作={actionName}，网络模式={GmRunContextService.GetNetworkModeText()}，玩家NetId={action.OwnerId}，GameActionType={action.GetType().Name}，ActionType={action.ActionType}，异常={ex.Message}。");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 构造当前动作队列状态文本，只用于中文联机日志排查。
    /// </summary>
    /// <param name="manager">当前 RunManager。</param>
    private static string BuildQueueStateText(RunManager manager)
    {
        try
        {
            return manager.ActionQueueSet == null
                ? "空对象"
                : manager.ActionQueueSet.IsEmpty ? "空闲" : "忙碌";
        }
        catch (Exception ex)
        {
            return $"读取失败:{ex.GetType().Name}";
        }
    }

    /// <summary>
    /// 获取当前房间类型文本，只用于中文联机日志排查。
    /// </summary>
    private static string GetCurrentRoomName()
    {
        try
        {
            object? room = RunManager.Instance?.DebugOnlyGetState()?.CurrentRoom;
            return room == null ? "空" : room.GetType().Name;
        }
        catch (Exception ex)
        {
            return $"读取失败:{ex.GetType().Name}";
        }
    }
}
