namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 岁月史书页签的持久化状态 DTO，跟随游戏 current_run 存档同生共死。
/// 用 JSON 落盘到 user://...profile{N}/saves/ 目录，SL 之后由 RunStarted 回调读回内存。
/// </summary>
public sealed class ChronicleSavedState
{
    /// <summary>
    /// 当前默认百分比倍率。和 ChronicleService 内部 DefaultPercent 一致。
    /// </summary>
    private const int DefaultPercent = 100;

    /// <summary>
    /// 当前默认奖励倍率。和 ChronicleService 内部 DefaultRewardMultiplier 一致。
    /// </summary>
    private const int DefaultRewardMultiplier = 1;

    /// <summary>
    /// 当前默认手牌上限。和 ChronicleService 内部 DefaultHandLimit 一致。
    /// </summary>
    private const int DefaultHandLimit = 10;

    /// <summary>
    /// 当前 DTO schema 版本号，后续字段变更时升级；老存档读到不同版本时按默认值兜底。
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// 数值锁定字典；key 使用 ChronicleValueKey 枚举名字符串，新增枚举不破坏老存档。
    /// </summary>
    public Dictionary<string, int> LockedValues { get; set; } = new();

    /// <summary>持续消灭所有敌人开关。</summary>
    public bool AlwaysKillAllEnemies { get; set; }

    /// <summary>上帝模式开关（toggle 控制台命令，加载后会强制对齐一次）。</summary>
    public bool GodMode { get; set; }

    /// <summary>无限能量开关。</summary>
    public bool InfiniteEnergy { get; set; }

    /// <summary>始终抽到上限开关。</summary>
    public bool AlwaysDrawToHandLimit { get; set; }

    /// <summary>免疫负面效果开关。</summary>
    public bool NegativePowerImmune { get; set; }

    /// <summary>总是玩家回合开关。</summary>
    public bool AlwaysPlayerTurn { get; set; }

    /// <summary>任意地图点可点击开关。</summary>
    public bool AnyMapNodeClickable { get; set; }

    /// <summary>玩家攻击倍率百分比，默认 100。</summary>
    public int PlayerDamagePercent { get; set; } = DefaultPercent;

    /// <summary>敌人攻击倍率百分比，默认 100。</summary>
    public int EnemyDamagePercent { get; set; } = DefaultPercent;

    /// <summary>奖励倍率，默认 1。</summary>
    public int RewardMultiplier { get; set; } = DefaultRewardMultiplier;

    /// <summary>商店价格倍率百分比，默认 100。</summary>
    public int ShopPricePercent { get; set; } = DefaultPercent;

    /// <summary>药水效果倍率百分比，默认 100。</summary>
    public int PotionEffectPercent { get; set; } = DefaultPercent;

    /// <summary>GM 手牌上限，默认 10。</summary>
    public int HandLimit { get; set; } = DefaultHandLimit;

    /// <summary>
    /// 返回一份全部为默认值的状态副本，用于首次进入存档或文件损坏时兜底。
    /// </summary>
    public static ChronicleSavedState Default()
    {
        return new ChronicleSavedState();
    }
}
