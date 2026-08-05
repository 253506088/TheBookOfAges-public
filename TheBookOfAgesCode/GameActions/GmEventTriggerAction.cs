using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace TheBookOfAges.TheBookOfAgesCode.GameActions;

/// <summary>
/// GM 事件任意门网络动作：联机时同步事件控制台命令，并在各端执行前清理残留地图和覆盖层。
/// </summary>
public sealed class GmEventTriggerAction : GameAction
{
    private readonly Player _player;
    private readonly string _commandName;
    private readonly string _eventCommandId;
    private readonly string _eventName;
    private readonly bool _isAncient;

    /// <summary>
    /// 当前网络动作所属玩家。
    /// </summary>
    public override ulong OwnerId => _player.NetId;

    /// <summary>
    /// 事件任意门只允许非战斗稳定状态提交。
    /// </summary>
    public override GameActionType ActionType => GameActionType.Any;

    /// <summary>
    /// 创建 GM 事件任意门网络动作。
    /// </summary>
    /// <param name="player">触发事件的玩家。</param>
    /// <param name="commandName">原生控制台命令名，event 或 ancient。</param>
    /// <param name="eventCommandId">控制台使用的事件 ID。</param>
    /// <param name="eventName">用于日志展示的事件名称。</param>
    /// <param name="isAncient">是否先古之民事件。</param>
    public GmEventTriggerAction(Player player, string commandName, string eventCommandId, string eventName, bool isAncient)
    {
        _player = player;
        _commandName = commandName;
        _eventCommandId = eventCommandId;
        _eventName = eventName;
        _isAncient = isAncient;
    }

    /// <summary>
    /// 在本机执行网络动作；先清残留 UI，再调用原生事件控制台命令。
    /// </summary>
    protected override async Task ExecuteAction()
    {
        string commandText = BuildConsoleCommandText(_commandName, _eventCommandId);
        MainFile.Logger.Info($"GM 联机事件任意门动作开始执行：玩家NetId={_player.NetId}，事件={_eventName}，事件ID={_eventCommandId}，命令={commandText}，是否先古之民={_isAncient}。");

        CloseMapAndOverlayScreens();
        bool success = await GmEventTriggerExecutor.RunEventCommandAsync(_player, _commandName, _eventCommandId);
        MainFile.Logger.Info($"GM 联机事件任意门动作执行完成：玩家NetId={_player.NetId}，事件={_eventName}，事件ID={_eventCommandId}，命令={commandText}，success={success}。");
    }

    /// <summary>
    /// 把本地动作转换成可同步的网络动作数据。
    /// </summary>
    public override INetAction ToNetAction()
    {
        return new NetGmEventTriggerAction
        {
            CommandName = _commandName,
            EventCommandId = _eventCommandId,
            EventName = _eventName,
            IsAncient = _isAncient
        };
    }

    /// <summary>
    /// 返回便于日志定位的动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"GmEventTriggerAction player {_player.NetId} command {_commandName} event {_eventCommandId} ancient {_isAncient}";
    }

    /// <summary>
    /// 拼接原生控制台命令文本。
    /// </summary>
    /// <param name="commandName">命令名。</param>
    /// <param name="eventCommandId">事件 ID。</param>
    private static string BuildConsoleCommandText(string commandName, string eventCommandId)
    {
        return $"{commandName} {eventCommandId}";
    }

    /// <summary>
    /// 关闭事件进入前可能残留的地图和覆盖层，避免进入事件后被旧界面遮住。
    /// </summary>
    private static void CloseMapAndOverlayScreens()
    {
        try
        {
            NOverlayStack? overlayStack = NOverlayStack.Instance;
            if (overlayStack != null && overlayStack.ScreenCount > 0)
            {
                MainFile.Logger.Info($"GM 联机事件任意门执行前清理覆盖层：数量={overlayStack.ScreenCount}。");
                overlayStack.Clear();
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 联机事件任意门清理覆盖层失败：异常类型={ex.GetType().FullName}，异常信息={ex.Message}。");
        }

        try
        {
            NMapScreen? mapScreen = NMapScreen.Instance;
            if (mapScreen != null && mapScreen.IsOpen)
            {
                MainFile.Logger.Info("GM 联机事件任意门执行前关闭地图页面。");
                mapScreen.Close(animateOut: false);
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 联机事件任意门关闭地图页面失败：异常类型={ex.GetType().FullName}，异常信息={ex.Message}。");
        }
    }
}

/// <summary>
/// GM 事件任意门网络数据：同步原生控制台命令名和事件 ID。
/// </summary>
public struct NetGmEventTriggerAction : INetAction
{
    /// <summary>
    /// 原生控制台命令名，event 或 ancient。
    /// </summary>
    public string CommandName { get; set; }

    /// <summary>
    /// 控制台使用的事件 ID。
    /// </summary>
    public string EventCommandId { get; set; }

    /// <summary>
    /// 用于日志展示的事件名称。
    /// </summary>
    public string EventName { get; set; }

    /// <summary>
    /// 是否先古之民事件。
    /// </summary>
    public bool IsAncient { get; set; }

    /// <summary>
    /// 根据网络数据还原成本地可执行动作。
    /// </summary>
    /// <param name="player">动作所属玩家。</param>
    public GameAction ToGameAction(Player player)
    {
        return new GmEventTriggerAction(player, CommandName ?? string.Empty, EventCommandId ?? string.Empty, EventName ?? string.Empty, IsAncient);
    }

    /// <summary>
    /// 序列化事件任意门请求。
    /// </summary>
    /// <param name="writer">游戏网络包写入器。</param>
    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(CommandName ?? string.Empty);
        writer.WriteString(EventCommandId ?? string.Empty);
        writer.WriteString(EventName ?? string.Empty);
        writer.WriteBool(IsAncient);
    }

    /// <summary>
    /// 反序列化事件任意门请求。
    /// </summary>
    /// <param name="reader">游戏网络包读取器。</param>
    public void Deserialize(PacketReader reader)
    {
        CommandName = reader.ReadString();
        EventCommandId = reader.ReadString();
        EventName = reader.ReadString();
        IsAncient = reader.ReadBool();
    }

    /// <summary>
    /// 返回便于日志定位的网络动作描述。
    /// </summary>
    public override string ToString()
    {
        return $"NetGmEventTriggerAction command {CommandName} event {EventCommandId} ancient {IsAncient}";
    }
}

/// <summary>
/// GM 事件任意门执行器：集中调用原生 event/ancient 控制台命令。
/// </summary>
internal static class GmEventTriggerExecutor
{
    /// <summary>
    /// 执行事件控制台命令，并等待命令返回的异步任务。
    /// </summary>
    /// <param name="player">触发事件的玩家。</param>
    /// <param name="commandName">原生控制台命令名。</param>
    /// <param name="eventCommandId">控制台事件 ID。</param>
    public static async Task<bool> RunEventCommandAsync(Player player, string commandName, string eventCommandId)
    {
        if (player == null)
        {
            MainFile.Logger.Info("GM 联机事件任意门执行跳过：玩家为空。");
            return false;
        }

        if (string.IsNullOrWhiteSpace(eventCommandId))
        {
            MainFile.Logger.Info("GM 联机事件任意门执行跳过：事件 ID 为空。");
            return false;
        }

        AbstractConsoleCmd consoleCmd = commandName.Equals("ancient", StringComparison.OrdinalIgnoreCase)
            ? new AncientConsoleCmd()
            : new EventConsoleCmd();

        try
        {
            CmdResult cmdResult = consoleCmd.Process(player, [eventCommandId]);
            MainFile.Logger.Info($"GM 联机事件任意门控制台命令返回：命令={consoleCmd.CmdName}，事件ID={eventCommandId}，success={cmdResult.success}，msg={SafeLogText(cmdResult.msg)}，是否包含异步任务={cmdResult.task != null}。");

            if (cmdResult.task != null)
            {
                await cmdResult.task;
                MainFile.Logger.Info($"GM 联机事件任意门控制台命令异步任务完成：命令={consoleCmd.CmdName}，事件ID={eventCommandId}。");
            }

            return cmdResult.success;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 联机事件任意门执行异常：命令={consoleCmd.CmdName}，事件ID={eventCommandId}，异常类型={ex.GetType().FullName}，异常信息={ex.Message}，堆栈={SafeLogText(ex.StackTrace)}。");
            return false;
        }
    }

    /// <summary>
    /// 清理日志文本，避免空文本或换行把排查日志切得太碎。
    /// </summary>
    /// <param name="value">原始日志文本。</param>
    private static string SafeLogText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "空"
            : value.Replace("\r", "\\r").Replace("\n", "\\n");
    }
}
