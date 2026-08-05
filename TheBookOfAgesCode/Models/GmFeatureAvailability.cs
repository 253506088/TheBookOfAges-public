namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// GM 功能执行方式，用于多人模式下区分“允许”背后到底怎么执行。
/// </summary>
public enum GmFeatureExecutionMode
{
    /// <summary>
    /// 单人或本地直接执行，不进入网络动作队列。
    /// </summary>
    LocalDirect,

    /// <summary>
    /// 多人模式下通过游戏原生控制台网络命令执行。
    /// </summary>
    NetworkConsoleCommand,

    /// <summary>
    /// 多人模式下通过 GM 自定义网络动作执行。
    /// </summary>
    NetworkGameAction,

    /// <summary>
    /// 多人模式下根据具体选项选择原生网络命令或 GM 自定义网络动作。
    /// </summary>
    NetworkCommandOrGameAction,

    /// <summary>
    /// 只读取游戏状态，不写运行时状态。
    /// </summary>
    ReadOnly,

    /// <summary>
    /// 当前上下文禁止执行。
    /// </summary>
    Disabled,
}

/// <summary>
/// GM 功能可用性结果，包含是否允许执行、执行方式以及给玩家看的原因。
/// </summary>
/// <param name="IsAllowed">当前功能是否允许执行。</param>
/// <param name="Reason">不允许执行时展示给玩家的原因。</param>
/// <param name="ExecutionMode">当前功能的执行方式。</param>
public sealed record GmFeatureAvailability(bool IsAllowed, string Reason, GmFeatureExecutionMode ExecutionMode)
{
    /// <summary>
    /// 创建允许执行的结果。
    /// </summary>
    /// <param name="executionMode">允许执行时采用的执行方式。</param>
    public static GmFeatureAvailability Allow(GmFeatureExecutionMode executionMode = GmFeatureExecutionMode.LocalDirect)
    {
        return new GmFeatureAvailability(true, string.Empty, executionMode);
    }

    /// <summary>
    /// 创建禁止执行的结果。
    /// </summary>
    /// <param name="reason">禁止执行的中文/英文提示。</param>
    public static GmFeatureAvailability Deny(string reason)
    {
        return new GmFeatureAvailability(false, reason, GmFeatureExecutionMode.Disabled);
    }
}
