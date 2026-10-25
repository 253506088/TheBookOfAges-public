using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.UI;
using System.Reflection;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// 岁月史书数值类型，用于 UI 和服务之间传递需要读取、应用、锁定的运行时字段。
/// </summary>
public enum ChronicleValueKey
{
    CurrentHp,
    MaxHp,
    Gold,
    Energy,
    MaxEnergy,
    Stars,
    OrbSlots,
    HandLimit,
    CardsPerTurn,
    AscensionLevel,
    PlayerDamagePercent,
    EnemyDamagePercent,
    RewardMultiplier,
    ShopPricePercent,
    PotionEffectPercent,
}

public sealed record ChronicleEnemyOption(string Id, string Name);

public sealed record ChronicleIntentOption(string Id, string Name);

/// <summary>
/// 岁月史书服务，优先调用游戏自带控制台命令，命令做不到时再走游戏内部命令对象或运行时字段兜底。
/// </summary>
public sealed class ChronicleService
{
    /// <summary>
    /// 全局单例；让 MainFile / Patch / Repository 都能拿到同一个服务对象，
    /// 避免 GmUiManager 重新 new 一份后状态对不上。构造函数私有，外部统一走 Instance。
    /// </summary>
    public static ChronicleService Instance { get; } = new ChronicleService();

    /// <summary>
    /// 持久化"反向写入"抑制开关：从磁盘加载状态、Run 结束清零、多人模式自动关闭等场景下置位，
    /// 阻止 Setter 把"加载/重置"中间过程当成玩家操作再写回磁盘，避免循环或覆盖玩家配置。
    /// </summary>
    private static bool _isSuppressingSave;

    /// <summary>
    /// 私有构造，配合 Instance 模式确保单例。
    /// </summary>
    private ChronicleService()
    {
    }

    private const int DefaultPercent = 100;
    private const int DefaultRewardMultiplier = 1;
    private const int DefaultHandLimit = 10;
    private const int MaxRewardMultiplier = 99;
    private const int MaxOrbSlots = 10;
    private const int InfiniteEnergyValue = 999;
    private const int InfiniteEnergyRefillThreshold = 100;
    private static readonly TimeSpan HandDrawDebounceTime = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan AutoDrawLogInterval = TimeSpan.FromSeconds(2);
    private readonly GoldConsoleCmd _goldConsoleCmd = new();
    private readonly HealConsoleCmd _healConsoleCmd = new();
    private readonly EnergyConsoleCmd _energyConsoleCmd = new();
    private readonly StarsConsoleCmd _starsConsoleCmd = new();
    private readonly DrawConsoleCmd _drawConsoleCmd = new();
    private readonly KillConsoleCmd _killConsoleCmd = new();
    private readonly GodModeConsoleCmd _godModeConsoleCmd = new();
    private readonly ActConsoleCmd _actConsoleCmd = new();
    private readonly RoomConsoleCmd _roomConsoleCmd = new();

    private readonly Dictionary<ChronicleValueKey, int> _lockedValues = new();
    private bool _isDrawingToHandLimit;
    private bool _isAutoKillingAllEnemies;
    private bool _isRefreshingGodModeForCombat;
    private string? _lastAutoKillRoomKey;
    private string? _lastGodModeRefreshRoomKey;
    private bool _drawPileEmptyLogged;
    private bool _handDrawRequested;
    private DateTime _lastHandShrinkAt = DateTime.MinValue;
    private DateTime _lastAutoDrawLogAt = DateTime.MinValue;
    /// <summary>
    /// 手牌哨兵：上一次观测到的玩家手牌数量，用于和当前帧比对；
    /// -1 表示尚未观测过（功能刚开启 / 离开战斗 / 玩家死亡）。
    /// 由 TryDrawIfHandShrunkAsync 维护：每次观测都会立刻刷新成当前值，
    /// 只有"减少"才触发补牌，避免普通定时轮询带来的抽牌动画连环触发。
    /// </summary>
    private int _lastObservedHandCount = -1;
    private static bool _alwaysPlayerTurnActive;
    private static bool _anyMapNodeClickableActive;
    private static bool _orbManagerMembersLogged;
    // 充能球异常补救重入锁：Finalizer 的递归调用会形成 TweenLayout 抛异常 → 补救 → 反射 TweenLayout → 又抛异常 的死循环，靠这把锁斩断
    private static bool _isRecoveringOrbLayout;
    // 充能球栏位调整重入锁：用户连点应用按钮或锁定定时器并发触发时，避免两个调整流程互相踩状态
    private static bool _isApplyingOrbSlots;
    private static IReadOnlyList<ChronicleIntentOption>? _intentOptionsCache;
    private static readonly FieldInfo? MoveStateIntentsField = typeof(MoveState).GetField("<Intents>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private int _lastStunIntentAppliedCount = -1;

    public static int PlayerDamagePercent { get; private set; } = DefaultPercent;
    public static int EnemyDamagePercent { get; private set; } = DefaultPercent;
    public static int RewardMultiplier { get; private set; } = DefaultRewardMultiplier;
    public static int ShopPricePercent { get; private set; } = DefaultPercent;
    public static int PotionEffectPercent { get; private set; } = DefaultPercent;
    public static int HandLimitOverride { get; private set; } = DefaultHandLimit;
    public bool AlwaysKillAllEnemiesEnabled { get; private set; }
    public bool GodModeEnabled { get; private set; }
    public bool InfiniteEnergyEnabled { get; private set; }
    public bool AlwaysPlayerTurnEnabled => _alwaysPlayerTurnActive;
    public bool AnyMapNodeClickableEnabled => _anyMapNodeClickableActive;
    public bool AlwaysDrawToHandLimitEnabled { get; private set; }
    public bool NegativePowerImmuneEnabled { get; private set; }

    /// <summary>
    /// 判断指定岁月史书数值项在多人模式下是否能通过游戏内置网络控制台命令执行。
    /// </summary>
    public static bool IsMultiplayerCommandValue(ChronicleValueKey key)
    {
        return key is ChronicleValueKey.CurrentHp or
            ChronicleValueKey.Gold or
            ChronicleValueKey.Energy or
            ChronicleValueKey.Stars;
    }

    /// <summary>
    /// 读取岁月史书当前生效的手牌上限；只兜底最小值，不人为限制最大值。
    /// </summary>
    public static int GetEffectiveHandLimit()
    {
        if (GmRunContextService.IsMultiplayer())
        {
            return CardPile.MaxCardsInHand;
        }

        return Math.Max(1, HandLimitOverride);
    }

    /// <summary>
    /// 判断岁月史书"主定时器"当前是否有事可做：有任意锁定值，或任何需要"定时回填"的持续开关被开启。
    /// 上帝模式在多人模式下若有旧状态残留，也让主定时器跑一次清理，避免联机不同步。
    /// 始终抽到上限仍优先靠独立手牌哨兵高频检查，这里只作为读档和面板生命周期异常时的低频兜底。
    /// 用于主定时器的快速门：所有条件都为否时，直接跳过本轮回填，避免空转。
    /// </summary>
    public bool HasOngoingChronicleWork =>
        _lockedValues.Count > 0 ||
        HandLimitOverride != DefaultHandLimit ||
        AlwaysKillAllEnemiesEnabled ||
        (GmRunContextService.IsMultiplayer() && GodModeEnabled) ||
        InfiniteEnergyEnabled ||
        AlwaysPlayerTurnEnabled ||
        AlwaysDrawToHandLimitEnabled ||
        NegativePowerImmuneEnabled;

    /// <summary>
    /// 判断岁月史书是否正在让敌人跳过行动，供运行时补丁读取。
    /// </summary>
    public static bool ShouldSkipEnemyTurn()
    {
        return !GmRunContextService.IsMultiplayer() && _alwaysPlayerTurnActive;
    }

    /// <summary>
    /// 判断任意地图点点击开关是否开启，供地图节点点击补丁读取。
    /// </summary>
    public static bool IsAnyMapNodeClickableEnabled()
    {
        return !GmRunContextService.IsMultiplayer() && _anyMapNodeClickableActive;
    }

    /// <summary>
    /// 清理手牌哨兵观测状态；关闭"始终抽到上限"或停止定时器时调用，避免下次开启沿用旧基线。
    /// </summary>
    public void ResetHandWatcherState()
    {
        _lastObservedHandCount = -1;
        _handDrawRequested = false;
        _lastHandShrinkAt = DateTime.MinValue;
    }

    public IReadOnlyList<ChronicleEnemyOption> GetEnemyOptions()
    {
        return GetAliveEnemies()
            .Select((enemy, index) => new ChronicleEnemyOption(GetEnemyId(enemy), $"{index + 1}. {SafeText(enemy.Name, enemy.ModelId.ToString())}"))
            .ToList();
    }

    public IReadOnlyList<ChronicleIntentOption> GetIntentOptions()
    {
        if (_intentOptionsCache != null)
        {
            return _intentOptionsCache;
        }

        Type abstractIntentType = typeof(AbstractIntent);
        _intentOptionsCache = abstractIntentType.Assembly.GetTypes()
            .Where(type => !type.IsAbstract && abstractIntentType.IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) != null)
            .Where(type => !IsHiddenIntentType(type))
            .Select(CreateIntentOption)
            .Where(option => option != null)
            .Select(option => option!)
            .OrderBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        MainFile.Logger.Info($"岁月史书敌人意图列表已缓存：数量={_intentOptionsCache.Count}。");
        return _intentOptionsCache;
    }

    public async Task<string> ApplyEnemyIntentAsync(bool allEnemies, string? enemyId, string? intentId)
    {
        if (TryBlockChronicleInMultiplayer(out string disabledMessage))
        {
            return disabledMessage;
        }

        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            return GmUiText.ChronicleNotInRun;
        }

        if (string.IsNullOrWhiteSpace(intentId))
        {
            return GmUiText.ChronicleNoIntentSelected;
        }

        List<Creature> enemies = GetAliveEnemies();
        if (enemies.Count == 0)
        {
            return GmUiText.ChronicleNeedCombat;
        }

        List<Creature> targets = allEnemies
            ? enemies
            : enemies.Where(enemy => string.Equals(GetEnemyId(enemy), enemyId, StringComparison.Ordinal)).ToList();
        if (targets.Count == 0)
        {
            return GmUiText.ChronicleNoEnemySelected;
        }

        try
        {
            Type? intentType = ResolveIntentType(intentId);
            if (intentType == null)
            {
                return GmUiText.ChronicleNoIntentSelected;
            }

            foreach (Creature enemy in targets)
            {
                if (enemy.Monster?.NextMove == null)
                {
                    continue;
                }

                AbstractIntent intent = (AbstractIntent)Activator.CreateInstance(intentType)!;
                MoveState replacement = new(enemy.Monster.NextMove.StateId, _ => Task.CompletedTask, intent);
                enemy.Monster.SetMoveImmediate(replacement, true);
            }

            await Task.CompletedTask;
            return GmUiText.ChronicleApplySuccess;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书修改敌人意图失败：目标数={targets.Count}，意图={intentId}，异常={ex.Message}");
            return GmUiText.ChronicleApplyFailed;
        }
    }

    /// <summary>
    /// 判断指定数值是否已经被锁定。
    /// </summary>
    public bool IsLocked(ChronicleValueKey key)
    {
        return _lockedValues.ContainsKey(key);
    }

    /// <summary>
    /// 获取指定数值的锁定目标值；未锁定时返回空。
    /// </summary>
    public int? GetLockedValue(ChronicleValueKey key)
    {
        return _lockedValues.TryGetValue(key, out int value) ? value : null;
    }

    /// <summary>
    /// 设置或取消指定数值的锁定状态，锁定关闭 GM 页面后也会继续生效。
    /// </summary>
    public void SetLocked(ChronicleValueKey key, bool locked, int value)
    {
        GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(GmFeature.Chronicle);
        if (!availability.IsAllowed)
        {
            MainFile.Logger.Info($"岁月史书锁定状态修改已被多人安全闸门阻止：key={key}，locked={locked}，value={value}，原因={availability.Reason}。");
            return;
        }

        if (locked)
        {
            _lockedValues[key] = value;
        }
        else
        {
            _lockedValues.Remove(key);
        }

        SaveCurrentState();
    }

    /// <summary>
    /// 读取页面展示需要的当前数值；拿不到运行时对象时返回空。
    /// </summary>
    public int? GetCurrentValue(ChronicleValueKey key)
    {
        Player? player = GetCurrentPlayer();
        return key switch
        {
            ChronicleValueKey.CurrentHp => player?.Creature.CurrentHp,
            ChronicleValueKey.MaxHp => player?.Creature.MaxHp,
            ChronicleValueKey.Gold => player?.Gold,
            ChronicleValueKey.Energy => player?.PlayerCombatState?.Energy,
            ChronicleValueKey.MaxEnergy => player?.MaxEnergy,
            ChronicleValueKey.Stars => player?.PlayerCombatState?.Stars,
            ChronicleValueKey.OrbSlots => player?.PlayerCombatState?.OrbQueue.Capacity,
            ChronicleValueKey.HandLimit => GetEffectiveHandLimit(),
            ChronicleValueKey.CardsPerTurn => ReadStaticIntField(typeof(CombatManager), nameof(CombatManager.baseHandDrawCount)),
            ChronicleValueKey.AscensionLevel => GetCurrentRunState()?.AscensionLevel,
            ChronicleValueKey.PlayerDamagePercent => PlayerDamagePercent,
            ChronicleValueKey.EnemyDamagePercent => EnemyDamagePercent,
            ChronicleValueKey.RewardMultiplier => RewardMultiplier,
            ChronicleValueKey.ShopPricePercent => ShopPricePercent,
            ChronicleValueKey.PotionEffectPercent => PotionEffectPercent,
            _ => null,
        };
    }

    /// <summary>
    /// 应用数值修改；命令可覆盖的功能先走控制台命令，再用代码兜底校准到目标值。
    /// </summary>
    public async Task<string> ApplyValueAsync(ChronicleValueKey key, int value)
    {
        if (GmRunContextService.IsMultiplayer())
        {
            return await ApplyValueByNetworkCommandAsync(key, value);
        }

        if (TryBlockChronicleInMultiplayer(out string disabledMessage))
        {
            return disabledMessage;
        }

        if (key == ChronicleValueKey.RewardMultiplier)
        {
            ApplyRewardMultiplier(value);
            return GmUiText.ChronicleApplySuccess;
        }

        if (key == ChronicleValueKey.PotionEffectPercent)
        {
            ApplyPercent(key, value);
            return GmUiText.ChroniclePatchPending;
        }

        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            return GmUiText.ChronicleNotInRun;
        }

        try
        {
            bool success = key switch
            {
                ChronicleValueKey.CurrentHp => await ApplyCurrentHpAsync(player, value, preferConsole: true),
                ChronicleValueKey.MaxHp => await ApplyMaxHpAsync(player, value),
                ChronicleValueKey.Gold => await ApplyGoldAsync(player, value, preferConsole: true),
                ChronicleValueKey.Energy => await ApplyEnergyAsync(player, value, preferConsole: true),
                ChronicleValueKey.MaxEnergy => ApplyMaxEnergy(player, value),
                ChronicleValueKey.Stars => await ApplyStarsAsync(player, value, preferConsole: true),
                ChronicleValueKey.OrbSlots => ApplyOrbSlots(player, value),
                ChronicleValueKey.HandLimit => ApplyHandLimit(value),
                ChronicleValueKey.CardsPerTurn => ApplyCardsPerTurn(value),
                ChronicleValueKey.AscensionLevel => ApplyAscensionLevel(value),
                ChronicleValueKey.PlayerDamagePercent => ApplyPercent(key, value),
                ChronicleValueKey.EnemyDamagePercent => ApplyPercent(key, value),
                ChronicleValueKey.RewardMultiplier => ApplyRewardMultiplier(value),
                ChronicleValueKey.ShopPricePercent => ApplyPercent(key, value),
                ChronicleValueKey.PotionEffectPercent => ApplyPercent(key, value),
                _ => false,
            };

            return success ? GmUiText.ChronicleApplySuccess : GmUiText.ChronicleApplyFailed;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书应用数值失败：key={key}，value={value}，异常={ex.Message}");
            return GmUiText.ChronicleApplyFailed;
        }
    }

    /// <summary>
    /// 消灭所有敌人；优先执行 kill all，失败后直接调用 CreatureCmd.Kill 兜底。
    /// </summary>
    public async Task<string> KillAllEnemiesAsync()
    {
        if (GmRunContextService.IsMultiplayer())
        {
            return await TrySubmitChronicleNetworkCommandAsync("消灭所有敌人", "kill all", requireCombat: true);
        }

        if (TryBlockChronicleInMultiplayer(out string disabledMessage))
        {
            return disabledMessage;
        }

        return await KillAllEnemiesInternalAsync("消灭所有敌人", allowNoCombatMessage: true);
    }

    /// <summary>
    /// 切换持续消灭所有敌人；开启后每进入一个战斗房间自动执行一次 kill all。
    /// </summary>
    public async Task<string> SetAlwaysKillAllEnemiesAsync(bool enabled)
    {
        if (GmRunContextService.IsMultiplayer())
        {
            if (GetCurrentPlayer() == null)
            {
                return GmUiText.ChronicleNotInRun;
            }

            AlwaysKillAllEnemiesEnabled = enabled;
            if (!enabled)
            {
                _lastAutoKillRoomKey = null;
            }

            MainFile.Logger.Info($"岁月史书多人持续消灭所有敌人已{(enabled ? "开启" : "关闭")}。");
            if (enabled)
            {
                await TryAutoKillAllEnemiesAsync();
            }

            return enabled ? GmUiText.ChronicleToggleOn : GmUiText.ChronicleToggleOff;
        }

        if (TryBlockChronicleInMultiplayer(out string disabledMessage))
        {
            return disabledMessage;
        }

        AlwaysKillAllEnemiesEnabled = enabled;
        if (!enabled)
        {
            _lastAutoKillRoomKey = null;
        }

        SaveCurrentState();
        MainFile.Logger.Info($"岁月史书持续消灭所有敌人已{(enabled ? "开启" : "关闭")}。");
        if (enabled)
        {
            await TryAutoKillAllEnemiesAsync();
        }

        return enabled ? GmUiText.ChronicleToggleOn : GmUiText.ChronicleToggleOff;
    }

    /// <summary>
    /// 切换游戏自带上帝模式；该控制台命令是切换式命令，所以只在状态变化时执行一次。
    /// </summary>
    public async Task<string> SetGodModeAsync(bool enabled)
    {
        if (GmRunContextService.IsMultiplayer())
        {
            GodModeEnabled = false;
            _lastGodModeRefreshRoomKey = null;
            _isRefreshingGodModeForCombat = false;
            MainFile.Logger.Info("岁月史书多人模式已阻止上帝模式：godmode 会直接修改本机战斗状态，无法安全同步。");
            return GmUiText.ChronicleMultiplayerCommandOnlyDisabled;
        }

        if (TryBlockChronicleInMultiplayer(out string disabledMessage))
        {
            return disabledMessage;
        }

        if (GodModeEnabled == enabled)
        {
            return enabled ? GmUiText.ChronicleToggleOn : GmUiText.ChronicleToggleOff;
        }

        bool success = await RunConsoleCommandAsync(enabled ? "开启上帝模式" : "关闭上帝模式", _godModeConsoleCmd, []);
        if (!success)
        {
            MainFile.Logger.Info($"岁月史书上帝模式切换失败：目标状态={(enabled ? "开启" : "关闭")}。");
            return GmUiText.ChronicleApplyFailed;
        }

        GodModeEnabled = enabled;
        _lastGodModeRefreshRoomKey = null;
        SaveCurrentState();
        MainFile.Logger.Info($"岁月史书上帝模式已{(enabled ? "开启" : "关闭")}。");
        return enabled ? GmUiText.ChronicleToggleOn : GmUiText.ChronicleToggleOff;
    }

    /// <summary>
    /// 抽指定数量的牌；底层 CardPileCmd.Draw 已由岁月史书手牌上限补丁接管。
    /// </summary>
    public async Task<string> DrawCardsAsync(int count)
    {
        if (GmRunContextService.IsMultiplayer())
        {
            if (count <= 0)
            {
                return GmUiText.ChronicleInvalidNumber;
            }

            return await TrySubmitChronicleNetworkCommandAsync("抽牌", $"draw {count}", requireCombat: true);
        }

        if (TryBlockChronicleInMultiplayer(out string disabledMessage))
        {
            return disabledMessage;
        }

        if (count <= 0)
        {
            return GmUiText.ChronicleInvalidNumber;
        }

        return await RunConsoleOnlyActionAsync("抽牌", _drawConsoleCmd, [count.ToString()]);
    }

    /// <summary>
    /// 抽牌到手牌上限；先计算缺口，再复用 draw 控制台命令。
    /// </summary>
    public async Task<string> DrawToHandLimitAsync()
    {
        if (GmRunContextService.IsMultiplayer())
        {
            return await DrawToHandLimitInternalAsync("抽牌到上限");
        }

        if (TryBlockChronicleInMultiplayer(out string disabledMessage))
        {
            return disabledMessage;
        }

        return await DrawToHandLimitInternalAsync("抽牌到上限");
    }

    /// <summary>
    /// 切换无限能量；开启时立即把当前能量给到 999，真正回填由 ApplyLockedValuesAsync 负责持续执行。
    /// </summary>
    public async Task<string> SetInfiniteEnergyAsync(bool enabled)
    {
        if (GmRunContextService.IsMultiplayer())
        {
            if (GetCurrentPlayer() == null)
            {
                return GmUiText.ChronicleNotInRun;
            }

            InfiniteEnergyEnabled = enabled;
            MainFile.Logger.Info($"岁月史书多人无限能量已{(enabled ? "开启" : "关闭")}。");
            if (enabled)
            {
                await ApplyInfiniteEnergyByNetworkCommandAsync(force: true);
            }

            return enabled ? GmUiText.ChronicleToggleOn : GmUiText.ChronicleToggleOff;
        }

        if (TryBlockChronicleInMultiplayer(out string disabledMessage))
        {
            return disabledMessage;
        }

        InfiniteEnergyEnabled = enabled;
        SaveCurrentState();
        if (enabled)
        {
            await ApplyInfiniteEnergyAsync(force: true);
        }

        return enabled ? GmUiText.ChronicleToggleOn : GmUiText.ChronicleToggleOff;
    }

    /// <summary>
    /// 切换总是玩家回合；当前用全场敌人显示击晕且敌人伤害归零的方式凑合实现。
    /// </summary>
    public string SetAlwaysPlayerTurn(bool enabled)
    {
        if (TryBlockChronicleInMultiplayer(out string disabledMessage))
        {
            return disabledMessage;
        }

        _alwaysPlayerTurnActive = enabled;
        _lastStunIntentAppliedCount = -1;
        SaveCurrentState();
        MainFile.Logger.Info($"岁月史书总是玩家回合已{(enabled ? "开启" : "关闭")}。");
        return enabled ? GmUiText.ChronicleToggleOn : GmUiText.ChronicleToggleOff;
    }

    /// <summary>
    /// 切换任意地图点可点击；开启后原游戏地图上的任意节点都允许直接点击进入。
    /// </summary>
    public string SetAnyMapNodeClickable(bool enabled)
    {
        if (TryBlockChronicleInMultiplayer(out string disabledMessage))
        {
            return disabledMessage;
        }

        _anyMapNodeClickableActive = enabled;
        SaveCurrentState();
        MainFile.Logger.Info($"岁月史书任意地图点可点击已{(enabled ? "开启" : "关闭")}。");
        if (enabled)
        {
            RefreshCurrentMapPointInputState();
        }

        return enabled ? GmUiText.ChronicleToggleOn : GmUiText.ChronicleToggleOff;
    }

    /// <summary>
    /// 切换始终抽到上限；开启时立即尝试补牌，后续由定时器持续检查。
    /// </summary>
    public async Task<string> SetAlwaysDrawToHandLimitAsync(bool enabled)
    {
        if (GmRunContextService.IsMultiplayer())
        {
            if (GetCurrentPlayer() == null)
            {
                return GmUiText.ChronicleNotInRun;
            }

            AlwaysDrawToHandLimitEnabled = enabled;
            _drawPileEmptyLogged = false;
            ResetHandWatcherState();
            MainFile.Logger.Info($"岁月史书多人始终抽到上限已{(enabled ? "开启" : "关闭")}。");
            if (enabled)
            {
                await DrawToHandLimitInternalAsync("始终抽到上限开启补牌");
            }

            return enabled ? GmUiText.ChronicleToggleOn : GmUiText.ChronicleToggleOff;
        }

        if (TryBlockChronicleInMultiplayer(out string disabledMessage))
        {
            return disabledMessage;
        }

        AlwaysDrawToHandLimitEnabled = enabled;
        _drawPileEmptyLogged = false;
        ResetHandWatcherState();
        SaveCurrentState();
        MainFile.Logger.Info($"岁月史书始终抽到上限已{(enabled ? "开启" : "关闭")}。");
        if (enabled)
        {
            await DrawToHandLimitInternalAsync("始终抽到上限开启补牌");
        }

        return enabled ? GmUiText.ChronicleToggleOn : GmUiText.ChronicleToggleOff;
    }

    /// <summary>
    /// 切换免疫负面效果；第一版使用定时清理明显负面 Power 的方式实现。
    /// </summary>
    public string SetNegativePowerImmune(bool enabled)
    {
        if (TryBlockChronicleInMultiplayer(out string disabledMessage))
        {
            return disabledMessage;
        }

        NegativePowerImmuneEnabled = enabled;
        SaveCurrentState();
        return enabled ? GmUiText.ChronicleToggleOn : GmUiText.ChronicleToggleOff;
    }

    /// <summary>
    /// 把当前所有运行时字段打包成持久化 DTO；锁定字典用 ChronicleValueKey 枚举名做 key，方便扩展。
    /// </summary>
    public ChronicleSavedState BuildSavedState()
    {
        ChronicleSavedState state = new()
        {
            AlwaysKillAllEnemies = AlwaysKillAllEnemiesEnabled,
            GodMode = GodModeEnabled,
            InfiniteEnergy = InfiniteEnergyEnabled,
            AlwaysDrawToHandLimit = AlwaysDrawToHandLimitEnabled,
            NegativePowerImmune = NegativePowerImmuneEnabled,
            AlwaysPlayerTurn = _alwaysPlayerTurnActive,
            AnyMapNodeClickable = _anyMapNodeClickableActive,
            PlayerDamagePercent = PlayerDamagePercent,
            EnemyDamagePercent = EnemyDamagePercent,
            RewardMultiplier = RewardMultiplier,
            ShopPricePercent = ShopPricePercent,
            PotionEffectPercent = PotionEffectPercent,
            HandLimit = GetEffectiveHandLimit(),
        };

        foreach ((ChronicleValueKey key, int value) in _lockedValues)
        {
            state.LockedValues[key.ToString()] = value;
        }

        return state;
    }

    /// <summary>
    /// 把 DTO 写回内存字段；过程中临时关掉 SaveCurrentState 防止回放写入磁盘形成循环。
    /// 未知的锁定 key（枚举改名）会被静默忽略，避免老存档损坏挡住新字段。
    /// </summary>
    public void LoadFrom(ChronicleSavedState state)
    {
        if (state == null || state.Version != ChronicleSavedState.CurrentVersion || state.LockedValues == null)
        {
            ResetToDefaults();
            MainFile.Logger.Info("岁月史书拒绝无效或不支持的配置，已恢复默认状态。");
            return;
        }

        bool previousSuppress = _isSuppressingSave;
        _isSuppressingSave = true;
        try
        {
            _lockedValues.Clear();
            foreach ((string keyText, int value) in state.LockedValues)
            {
                if (Enum.TryParse(keyText, ignoreCase: false, out ChronicleValueKey key))
                {
                    _lockedValues[key] = value;
                }
                else
                {
                    MainFile.Logger.Info($"岁月史书加载状态忽略未知锁定 key：keyText={keyText}，value={value}。");
                }
            }

            AlwaysKillAllEnemiesEnabled = state.AlwaysKillAllEnemies;
            GodModeEnabled = state.GodMode;
            InfiniteEnergyEnabled = state.InfiniteEnergy;
            AlwaysDrawToHandLimitEnabled = state.AlwaysDrawToHandLimit;
            NegativePowerImmuneEnabled = state.NegativePowerImmune;
            _alwaysPlayerTurnActive = state.AlwaysPlayerTurn;
            _anyMapNodeClickableActive = state.AnyMapNodeClickable;
            PlayerDamagePercent = state.PlayerDamagePercent;
            EnemyDamagePercent = state.EnemyDamagePercent;
            RewardMultiplier = state.RewardMultiplier;
            ShopPricePercent = state.ShopPricePercent;
            PotionEffectPercent = state.PotionEffectPercent;
            HandLimitOverride = Math.Max(1, state.HandLimit);
            _lastGodModeRefreshRoomKey = null;
            ResetHandWatcherState();
        }
        finally
        {
            _isSuppressingSave = previousSuppress;
        }

        MainFile.Logger.Info($"岁月史书加载状态完成：锁定数量={_lockedValues.Count}，GodMode={GodModeEnabled}，无限能量={InfiniteEnergyEnabled}，AlwaysPlayerTurn={_alwaysPlayerTurnActive}。");
    }

    /// <summary>
    /// 加载完毕后做一次"对齐到游戏运行时"动作。
    /// - GodMode 是 toggle 控制台命令，重启游戏后游戏内必然是 false，要主动调一次把游戏内对齐到 true。
    /// - 任意地图点可点击：补丁状态已自动生效，但已经存在的地图节点要刷新一次才能立即可点。
    /// - 其它持续开关（无限能量、持续消灭、始终抽到上限、免疫负面、总是玩家回合）都靠现成定时器轮询，恢复内存值即生效。
    /// </summary>
    public async Task ApplyAfterLoadAsync()
    {
        try
        {
            if (GmRunContextService.IsMultiplayer())
            {
                MainFile.Logger.Info("岁月史书加载后应用跳过：当前为多人模式，不主动恢复运行时状态。");
                return;
            }

            Player? player = GetCurrentPlayer();
            if (player == null)
            {
                MainFile.Logger.Info("岁月史书加载后应用跳过：当前没有玩家对象。");
                return;
            }

            if (GodModeEnabled)
            {
                bool previousSuppress = _isSuppressingSave;
                _isSuppressingSave = true;
                try
                {
                    bool success = await RunConsoleCommandAsync("加载后恢复上帝模式", _godModeConsoleCmd, []);
                    MainFile.Logger.Info($"岁月史书加载后恢复上帝模式：success={success}。");
                }
                finally
                {
                    _isSuppressingSave = previousSuppress;
                }
            }

            if (AlwaysDrawToHandLimitEnabled)
            {
                ResetHandWatcherState();
                string result = await DrawToHandLimitInternalAsync("加载后恢复始终抽到上限");
                MainFile.Logger.Info($"岁月史书加载后恢复始终抽到上限：结果={result}。");
            }

            if (_anyMapNodeClickableActive)
            {
                RefreshCurrentMapPointInputState();
                MainFile.Logger.Info("岁月史书加载后已主动刷新地图节点可点击状态。");
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书加载后应用异常：异常类型={ex.GetType().Name}，异常={ex.Message}。");
        }
    }

    /// <summary>
    /// 把所有运行时状态归零；Run 结束（通关/死亡/放弃）时由 Patch 调用，避免下一局沿用上一局配置。
    /// 整段包 _isSuppressingSave 防止字段赋值触发"反向写盘"覆盖刚被 Patch 删掉的目标文件。
    /// </summary>
    public void ResetToDefaults()
    {
        bool previousSuppress = _isSuppressingSave;
        _isSuppressingSave = true;
        try
        {
            _lockedValues.Clear();
            AlwaysKillAllEnemiesEnabled = false;
            GodModeEnabled = false;
            InfiniteEnergyEnabled = false;
            AlwaysDrawToHandLimitEnabled = false;
            NegativePowerImmuneEnabled = false;
            _alwaysPlayerTurnActive = false;
            _anyMapNodeClickableActive = false;
            PlayerDamagePercent = DefaultPercent;
            EnemyDamagePercent = DefaultPercent;
            RewardMultiplier = DefaultRewardMultiplier;
            ShopPricePercent = DefaultPercent;
            PotionEffectPercent = DefaultPercent;
            HandLimitOverride = DefaultHandLimit;
            _lastAutoKillRoomKey = null;
            _lastGodModeRefreshRoomKey = null;
            _drawPileEmptyLogged = false;
            _lastStunIntentAppliedCount = -1;
            ResetHandWatcherState();
            MainFile.Logger.Info("岁月史书运行时状态已全部重置为默认值。");
        }
        finally
        {
            _isSuppressingSave = previousSuppress;
        }
    }

    /// <summary>
    /// 把当前状态序列化到磁盘；_isSuppressingSave 期间直接短路，所有异常吞掉避免影响玩家操作。
    /// </summary>
    private static void SaveCurrentState()
    {
        if (_isSuppressingSave)
        {
            return;
        }

        try
        {
            ChronicleStateRepository.TrySave(Instance.BuildSavedState());
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书状态保存调度异常：异常类型={ex.GetType().Name}，异常={ex.Message}。");
        }
    }

    /// <summary>
    /// 跳转到指定阶段，复用游戏自带 act 控制台命令。
    /// </summary>
    public async Task<string> JumpToActAsync(string actArg)
    {
        if (GmRunContextService.IsMultiplayer())
        {
            return await TrySubmitChronicleNetworkCommandAsync("阶段跳转", $"act {actArg}", requireCombat: false);
        }

        if (TryBlockChronicleInMultiplayer(out string disabledMessage))
        {
            return disabledMessage;
        }

        return await RunConsoleOnlyActionAsync("阶段跳转", _actConsoleCmd, [actArg]);
    }

    /// <summary>
    /// 跳转到指定房间，复用游戏自带 room 控制台命令。
    /// </summary>
    public async Task<string> JumpToRoomAsync(string roomArg)
    {
        if (GmRunContextService.IsMultiplayer())
        {
            return await TrySubmitChronicleNetworkCommandAsync("房间跳转", $"room {roomArg}", requireCombat: false);
        }

        if (TryBlockChronicleInMultiplayer(out string disabledMessage))
        {
            return disabledMessage;
        }

        return await RunConsoleOnlyActionAsync("房间跳转", _roomConsoleCmd, [roomArg]);
    }

    /// <summary>
    /// 获取当前地图上的所有房间节点，供 UI 生成“点击前往任何房间”的按钮。
    /// </summary>
    public IReadOnlyList<ChronicleMapRoomOption> GetMapRoomOptions()
    {
        if (GmRunContextService.GetFeatureAvailability(GmFeature.Chronicle).IsAllowed == false)
        {
            return [];
        }

        RunState? state = GetCurrentRunState();
        if (state?.Map == null)
        {
            return [];
        }

        return state.Map.GetAllMapPoints()
            .Where(point => point.PointType != MapPointType.Unassigned)
            .OrderBy(point => point.coord.row)
            .ThenBy(point => point.coord.col)
            .Select(point => CreateMapRoomOption(state, point))
            .ToList();
    }

    /// <summary>
    /// 跳转到当前地图上的指定节点；节点 ID 来自 GetMapRoomOptions。
    /// </summary>
    public async Task<bool> JumpToMapRoomAsync(string optionId)
    {
        if (TryBlockChronicleInMultiplayer(out _))
        {
            return false;
        }

        if (!TryParseMapRoomOptionId(optionId, out MapCoord coord))
        {
            MainFile.Logger.Info($"岁月史书任意房间跳转失败：无法解析目标ID={optionId}。");
            return false;
        }

        return await JumpToMapRoomCoordAsync(coord);
    }

    /// <summary>
    /// 跳转到当前地图上的指定坐标；供 UI 旧入口和地图节点点击补丁共用。
    /// </summary>
    public static async Task<bool> JumpToMapRoomCoordAsync(MapCoord coord)
    {
        if (TryBlockChronicleInMultiplayer(out _))
        {
            return false;
        }

        RunManager? manager = RunManager.Instance;
        RunState? state = GetCurrentRunState();
        if (manager == null || state?.Map == null)
        {
            MainFile.Logger.Info($"岁月史书任意房间跳转失败：当前不在局内或地图为空，目标坐标=({coord.col},{coord.row})。");
            return false;
        }

        if (!state.Map.HasPoint(coord))
        {
            MainFile.Logger.Info($"岁月史书任意房间跳转失败：目标坐标不属于当前地图，目标坐标=({coord.col},{coord.row})。");
            return false;
        }

        MapPoint? point = state.Map.GetPoint(coord);
        if (point == null)
        {
            MainFile.Logger.Info($"岁月史书任意房间跳转失败：当前地图返回空节点，目标坐标=({coord.col},{coord.row})。");
            return false;
        }

        try
        {
            await PrepareRunForMapJumpAsync(manager, state);
            MainFile.Logger.Info($"岁月史书任意房间跳转开始：目标={BuildMapRoomName(point)}，地图点类型={point.PointType}，入口=游戏原生 EnterMapCoord。");
            await manager.EnterMapCoord(coord);
            MainFile.Logger.Info($"岁月史书任意房间跳转成功：目标={BuildMapRoomName(point)}。");
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书任意房间跳转异常：目标={BuildMapRoomName(point)}，异常类型={ex.GetType().FullName}，异常信息={ex.Message}。");
            return false;
        }
    }

    /// <summary>
    /// 定时回填所有锁定值和持续开关，GM 页面关闭后也继续执行。
    /// </summary>
    public async Task ApplyLockedValuesAsync()
    {
        if (GmRunContextService.IsMultiplayer())
        {
            DisableChronicleRuntimeStateForMultiplayer();
            if (AlwaysKillAllEnemiesEnabled)
            {
                await TryAutoKillAllEnemiesAsync();
            }

            if (GodModeEnabled)
            {
                await TryRefreshGodModeForCurrentCombatAsync();
            }

            if (InfiniteEnergyEnabled)
            {
                await ApplyInfiniteEnergyByNetworkCommandAsync(force: false);
            }

            if (AlwaysDrawToHandLimitEnabled)
            {
                await TryDrawIfHandShrunkAsync();
            }

            return;
        }

        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            return;
        }

        foreach ((ChronicleValueKey key, int value) in _lockedValues.ToList())
        {
            await ApplyLockedValueAsync(player, key, value);
        }

        if (AlwaysKillAllEnemiesEnabled)
        {
            await TryAutoKillAllEnemiesAsync();
        }

        if (InfiniteEnergyEnabled)
        {
            await ApplyInfiniteEnergyAsync(force: false);
        }

        if (AlwaysPlayerTurnEnabled)
        {
            TryKeepEnemiesStunned();
        }

        if (AlwaysDrawToHandLimitEnabled)
        {
            await TryDrawIfHandShrunkAsync();
        }

        if (NegativePowerImmuneEnabled)
        {
            RemoveNegativePowers(player);
        }
    }

    /// <summary>
    /// 多人模式下每场战斗刷新一次上帝模式。
    /// 原生 godmode 是切换命令，联机刷新时只提交一次；重复提交会把刚开启的状态再次关闭。
    /// </summary>
    private async Task TryRefreshGodModeForCurrentCombatAsync()
    {
        if (!GmRunContextService.IsMultiplayer())
        {
            return;
        }

        if (_isRefreshingGodModeForCombat)
        {
            return;
        }

        if (!TryGetCurrentCombatRoomKey(out string roomKey, out string roomText))
        {
            _lastGodModeRefreshRoomKey = null;
            return;
        }

        if (string.Equals(_lastGodModeRefreshRoomKey, roomKey, StringComparison.Ordinal))
        {
            return;
        }

        _isRefreshingGodModeForCombat = true;
        try
        {
            MainFile.Logger.Info($"岁月史书多人上帝模式战斗刷新开始：房间={roomText}。");

            (bool submitted, string message) = await TrySubmitChronicleNetworkCommandsResultAsync(
                "刷新上帝模式",
                ["godmode"],
                requireCombat: true);
            if (!submitted)
            {
                MainFile.Logger.Info($"岁月史书多人上帝模式战斗刷新中止：房间={roomText}，结果={message}。");
                return;
            }

            _lastGodModeRefreshRoomKey = roomKey;
            MainFile.Logger.Info($"岁月史书多人上帝模式战斗刷新完成：房间={roomText}。");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书多人上帝模式战斗刷新异常：异常类型={ex.GetType().FullName}，异常信息={ex.Message}。");
        }
        finally
        {
            _isRefreshingGodModeForCombat = false;
        }
    }

    /// <summary>
    /// 自动消灭所有敌人；同一个战斗房间只执行一次，避免定时器反复刷 kill all 命令。
    /// </summary>
    private async Task TryAutoKillAllEnemiesAsync()
    {
        if (_isAutoKillingAllEnemies)
        {
            return;
        }

        RunState? runState = GetCurrentRunState();
        CombatState? combatState = CombatManager.Instance?.DebugOnlyGetState();
        IReadOnlyCollection<Creature> aliveEnemies = combatState?.Enemies.Where(enemy => enemy.IsAlive).ToList() ?? [];
        if (runState?.CurrentRoom is not CombatRoom currentRoom || aliveEnemies.Count == 0)
        {
            _lastAutoKillRoomKey = null;
            return;
        }

        string roomKey = BuildAutoCombatRoomKey(currentRoom, runState.CurrentRoomCount);
        if (string.Equals(_lastAutoKillRoomKey, roomKey, StringComparison.Ordinal))
        {
            return;
        }

        _isAutoKillingAllEnemies = true;
        try
        {
            string roomText = GetCurrentRoomText(currentRoom, runState.CurrentRoomCount);
            MainFile.Logger.Info($"岁月史书持续消灭所有敌人开始：房间={roomText}，存活敌人数量={aliveEnemies.Count}。");
            string result = GmRunContextService.IsMultiplayer()
                ? await TrySubmitChronicleNetworkCommandAsync("持续消灭所有敌人", "kill all", requireCombat: true)
                : await KillAllEnemiesInternalAsync("持续消灭所有敌人", allowNoCombatMessage: false);
            if (string.Equals(result, GmUiText.ChronicleSubmitSuccess, StringComparison.Ordinal) ||
                string.Equals(result, GmUiText.ChronicleApplySuccess, StringComparison.Ordinal))
            {
                _lastAutoKillRoomKey = roomKey;
            }

            MainFile.Logger.Info($"岁月史书持续消灭所有敌人结束：房间={roomText}，结果={result}。");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书持续消灭所有敌人异常：异常类型={ex.GetType().FullName}，异常信息={ex.Message}。");
        }
        finally
        {
            _isAutoKillingAllEnemies = false;
        }
    }

    /// <summary>
    /// 开启任意地图点可点击时，立刻刷新当前已经生成的地图节点鼠标状态。
    /// </summary>
    private static void RefreshCurrentMapPointInputState()
    {
        try
        {
            NMapScreen? mapScreen = NMapScreen.Instance;
            if (mapScreen == null)
            {
                MainFile.Logger.Info("岁月史书任意地图点可点击刷新：当前没有地图界面实例，等待下次地图打开后由补丁生效。");
                return;
            }

            int refreshedCount = RefreshMapPointInputStateRecursive(mapScreen);
            MainFile.Logger.Info($"岁月史书任意地图点可点击刷新完成：已刷新地图节点数量={refreshedCount}。");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书任意地图点可点击刷新异常：异常类型={ex.GetType().FullName}，异常信息={ex.Message}。");
        }
    }

    /// <summary>
    /// 递归刷新地图节点控件，让已经存在的不可达节点也能接收鼠标点击。
    /// </summary>
    private static int RefreshMapPointInputStateRecursive(Node node)
    {
        int refreshedCount = 0;
        if (node is NMapPoint mapPoint)
        {
            EnableMapPointInput(mapPoint);
            refreshedCount++;
        }

        foreach (Node child in node.GetChildren())
        {
            refreshedCount += RefreshMapPointInputStateRecursive(child);
        }

        return refreshedCount;
    }

    /// <summary>
    /// 放开单个地图节点的鼠标输入状态；只改 UI 输入，不直接改游戏地图路径。
    /// </summary>
    private static void EnableMapPointInput(NMapPoint mapPoint)
    {
        if (mapPoint is Control control)
        {
            control.MouseFilter = Control.MouseFilterEnum.Stop;
        }

        TryInvokeMapPointMethod(mapPoint, "SetEnabled", true);
        TryInvokeMapPointMethod(mapPoint, "Enable");
        TrySetBoolProperty(mapPoint, "Disabled", false);
        TrySetBoolProperty(mapPoint, "Selectable", true);
        TrySetBoolProperty(mapPoint, "IsSelectable", true);
        TrySetBoolProperty(mapPoint, "IsTravelable", true);
        TryInvokeMapPointMethod(mapPoint, "RefreshColorInstantly");
        TryInvokeMapPointMethod(mapPoint, "RefreshVisualsInstantly");
        TryInvokeMapPointMethod(mapPoint, "SetEnabled", true);
        TryInvokeMapPointMethod(mapPoint, "Enable");
    }

    /// <summary>
    /// 通过反射调用地图节点自身的启用和刷新方法；初始地图节点通常需要 Enable 才能真正收到鼠标输入。
    /// </summary>
    private static void TryInvokeMapPointMethod(object target, string methodName, params object[] args)
    {
        MethodInfo? method = FindInstanceMethod(target.GetType(), methodName, args.Length);
        if (method == null)
        {
            return;
        }

        try
        {
            method.Invoke(target, args);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书任意地图点可点击刷新：调用节点方法失败，节点类型={target.GetType().FullName}，方法={methodName}，异常={ex.Message}。");
        }
    }

    /// <summary>
    /// 沿继承链查找实例方法，兼容地图点方法定义在父类 NClickableControl 的情况。
    /// </summary>
    private static MethodInfo? FindInstanceMethod(Type type, string methodName, int parameterCount)
    {
        Type? currentType = type;
        while (currentType != null)
        {
            MethodInfo? method = currentType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .FirstOrDefault(candidate => candidate.Name == methodName && candidate.GetParameters().Length == parameterCount);
            if (method != null)
            {
                return method;
            }

            currentType = currentType.BaseType;
        }

        return null;
    }

    /// <summary>
    /// 通过反射尝试设置地图节点上的布尔状态，字段不存在时安静跳过。
    /// </summary>
    private static void TrySetBoolProperty(object target, string propertyName, bool value)
    {
        PropertyInfo? property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property == null || property.PropertyType != typeof(bool) || property.SetMethod == null)
        {
            return;
        }

        try
        {
            property.SetValue(target, value);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书任意地图点可点击刷新：设置节点属性失败，节点类型={target.GetType().FullName}，属性={propertyName}，异常={ex.Message}。");
        }
    }

    /// <summary>
    /// 拼接当前房间日志文本，用于持续消灭所有敌人的排查。
    /// </summary>
    private static string GetCurrentRoomText(AbstractRoom room, int currentRoomCount)
    {
        return $"{room.GetType().Name}/{room.RoomType}/{room.ModelId}/Id={room.Id?.ToString() ?? "空"}/房间栈数量={currentRoomCount}";
    }

    /// <summary>
    /// 生成持续消灭敌人的房间去重键，避免同一战斗房间反复提交 kill all。
    /// </summary>
    private static string BuildAutoCombatRoomKey(AbstractRoom room, int currentRoomCount)
    {
        return $"{currentRoomCount}:{room.GetType().FullName}:{room.Id?.ToString() ?? "空"}:{room.ModelId}";
    }

    /// <summary>
    /// 尝试读取当前战斗房间的去重键和日志文本。
    /// </summary>
    private static bool TryGetCurrentCombatRoomKey(out string roomKey, out string roomText)
    {
        roomKey = string.Empty;
        roomText = string.Empty;
        RunState? runState = GetCurrentRunState();
        CombatState? combatState = CombatManager.Instance?.DebugOnlyGetState();
        if (runState?.CurrentRoom is not CombatRoom currentRoom || combatState == null)
        {
            return false;
        }

        roomKey = BuildAutoCombatRoomKey(currentRoom, runState.CurrentRoomCount);
        roomText = GetCurrentRoomText(currentRoom, runState.CurrentRoomCount);
        return true;
    }

    /// <summary>
    /// 消灭所有敌人的共用实现；优先执行 kill all 命令，失败后用 CreatureCmd.Kill 兜底。
    /// </summary>
    private async Task<string> KillAllEnemiesInternalAsync(string actionName, bool allowNoCombatMessage)
    {
        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            return GmUiText.ChronicleNotInRun;
        }

        if (await RunConsoleCommandAsync(actionName, _killConsoleCmd, ["all"]))
        {
            return GmUiText.ChronicleApplySuccess;
        }

        CombatState? state = CombatManager.Instance?.DebugOnlyGetState();
        IReadOnlyCollection<Creature> enemies = state?.Enemies.Where(enemy => enemy.IsAlive).ToList() ?? [];
        if (enemies.Count == 0)
        {
            return allowNoCombatMessage ? GmUiText.ChronicleNeedCombat : GmUiText.ChronicleNothingToDo;
        }

        await CreatureCmd.Kill(enemies, force: true);
        MainFile.Logger.Info($"岁月史书{actionName}使用兜底逻辑完成：敌人数量={enemies.Count}。");
        return GmUiText.ChronicleApplySuccess;
    }

    /// <summary>
    /// 按百分比修改伤害数值；供 Harmony Patch 调用。
    /// </summary>
    public static decimal ScaleDamage(decimal amount, Creature? target, Creature? dealer)
    {
        if (GmRunContextService.IsMultiplayer())
        {
            return amount;
        }

        if (amount <= 0)
        {
            return amount;
        }

        if (_alwaysPlayerTurnActive && (dealer?.IsEnemy == true || target?.IsPlayer == true))
        {
            return 0m;
        }

        int percent = dealer?.IsPlayer == true || target?.IsEnemy == true
            ? PlayerDamagePercent
            : EnemyDamagePercent;
        return Math.Max(0m, amount * percent / DefaultPercent);
    }

    /// <summary>
    /// 按商店倍率修改价格；供 Harmony Patch 调用。
    /// </summary>
    public static int ScaleShopPrice(int cost)
    {
        if (GmRunContextService.IsMultiplayer())
        {
            return cost;
        }

        return Math.Max(0, (int)Math.Round(cost * ShopPricePercent / (double)DefaultPercent));
    }

    /// <summary>
    /// 读取奖励倍率，供奖励补丁按当前配置补足奖励份数。
    /// </summary>
    public static int GetRewardMultiplier()
    {
        if (GmRunContextService.IsMultiplayer())
        {
            return DefaultRewardMultiplier;
        }

        return Math.Clamp(RewardMultiplier, DefaultRewardMultiplier, MaxRewardMultiplier);
    }

    /// <summary>
    /// 对单个锁定值做直接回填，避免锁定时反复刷控制台命令。
    /// </summary>
    private async Task ApplyLockedValueAsync(Player player, ChronicleValueKey key, int value)
    {
        switch (key)
        {
            case ChronicleValueKey.CurrentHp:
                await ApplyCurrentHpAsync(player, value, preferConsole: false);
                break;
            case ChronicleValueKey.MaxHp:
                await ApplyMaxHpAsync(player, value);
                break;
            case ChronicleValueKey.Gold:
                await ApplyGoldAsync(player, value, preferConsole: false);
                break;
            case ChronicleValueKey.Energy:
                await ApplyEnergyAsync(player, value, preferConsole: false);
                break;
            case ChronicleValueKey.MaxEnergy:
                ApplyMaxEnergy(player, value);
                break;
            case ChronicleValueKey.Stars:
                await ApplyStarsAsync(player, value, preferConsole: false);
                break;
            case ChronicleValueKey.OrbSlots:
                ApplyOrbSlots(player, value);
                break;
            case ChronicleValueKey.HandLimit:
                ApplyHandLimit(value);
                break;
            case ChronicleValueKey.CardsPerTurn:
                ApplyCardsPerTurn(value);
                break;
            case ChronicleValueKey.AscensionLevel:
                ApplyAscensionLevel(value);
                break;
        }
    }

    /// <summary>
    /// 应用当前生命，正向加血时优先走 heal 命令，最后用 CreatureCmd.SetCurrentHp 校准。
    /// </summary>
    private async Task<bool> ApplyCurrentHpAsync(Player player, int value, bool preferConsole)
    {
        int target = Math.Clamp(value, 0, Math.Max(1, player.Creature.MaxHp));
        int diff = target - player.Creature.CurrentHp;
        if (preferConsole && diff > 0)
        {
            await RunConsoleCommandAsync("设置生命", _healConsoleCmd, [diff.ToString()]);
        }

        if (player.Creature.CurrentHp != target)
        {
            await CreatureCmd.SetCurrentHp(player.Creature, target);
        }

        return player.Creature.CurrentHp == target;
    }

    /// <summary>
    /// 应用最大生命，使用游戏内部 CreatureCmd.SetMaxHp 并保证当前生命不超过最大值。
    /// </summary>
    private static async Task<bool> ApplyMaxHpAsync(Player player, int value)
    {
        int target = Math.Max(1, value);
        await CreatureCmd.SetMaxHp(player.Creature, target);
        if (player.Creature.CurrentHp > target)
        {
            await CreatureCmd.SetCurrentHp(player.Creature, target);
        }

        return player.Creature.MaxHp == target;
    }

    /// <summary>
    /// 应用金币，优先走 gold 差值命令，失败或不精确时用 PlayerCmd.SetGold 校准。
    /// </summary>
    private async Task<bool> ApplyGoldAsync(Player player, int value, bool preferConsole)
    {
        int target = Math.Max(0, value);
        int diff = target - player.Gold;
        if (preferConsole && diff != 0)
        {
            await RunConsoleCommandAsync("设置金币", _goldConsoleCmd, [diff.ToString()]);
        }

        if (player.Gold != target)
        {
            await PlayerCmd.SetGold(target, player);
        }

        return player.Gold == target;
    }

    /// <summary>
    /// 应用当前能量，优先走 energy 正差值命令，最后用 PlayerCmd.SetEnergy 校准。
    /// </summary>
    private async Task<bool> ApplyEnergyAsync(Player player, int value, bool preferConsole)
    {
        var combatState = player.PlayerCombatState;
        if (combatState == null)
        {
            return false;
        }

        int target = Math.Max(0, value);
        int current = combatState.Energy;
        int diff = target - current;
        if (preferConsole && diff > 0)
        {
            await RunConsoleCommandAsync("设置能量", _energyConsoleCmd, [diff.ToString()]);
        }

        if (combatState.Energy != target)
        {
            await PlayerCmd.SetEnergy(target, player);
        }

        return combatState.Energy == target;
    }

    /// <summary>
    /// 应用无限能量，只修改当前能量为 999，不修改最大能量上限。
    /// </summary>
    private async Task<bool> ApplyInfiniteEnergyAsync(bool force)
    {
        if (GmRunContextService.IsMultiplayer())
        {
            return await ApplyInfiniteEnergyByNetworkCommandAsync(force);
        }

        Player? player = GetCurrentPlayer();
        if (player?.PlayerCombatState == null)
        {
            return false;
        }

        int currentEnergy = player.PlayerCombatState.Energy;
        if (currentEnergy >= InfiniteEnergyValue || (!force && currentEnergy >= InfiniteEnergyRefillThreshold))
        {
            return true;
        }

        await PlayerCmd.SetEnergy(InfiniteEnergyValue, player);
        return player.PlayerCombatState.Energy == InfiniteEnergyValue;
    }

    /// <summary>
    /// 多人模式下用原生 energy 网络命令补能量，不直接写本机战斗状态。
    /// </summary>
    private async Task<bool> ApplyInfiniteEnergyByNetworkCommandAsync(bool force)
    {
        Player? player = GetCurrentPlayer();
        if (player?.PlayerCombatState == null)
        {
            return false;
        }

        int currentEnergy = player.PlayerCombatState.Energy;
        if (currentEnergy >= InfiniteEnergyValue || (!force && currentEnergy >= InfiniteEnergyRefillThreshold))
        {
            return true;
        }

        int delta = InfiniteEnergyValue - currentEnergy;
        if (delta <= 0)
        {
            return true;
        }

        (bool submitted, _) = await TrySubmitChronicleNetworkCommandResultAsync("无限能量补充", $"energy {delta}", requireCombat: true);
        return submitted;
    }

    /// <summary>
    /// 应用最大能量；该字段游戏对象直接提供 setter。
    /// </summary>
    private static bool ApplyMaxEnergy(Player player, int value)
    {
        player.MaxEnergy = Math.Max(0, value);
        return player.MaxEnergy == Math.Max(0, value);
    }

    /// <summary>
    /// 应用星星/充能球资源，优先走 stars 正差值命令，最后用 PlayerCmd.SetStars 校准。
    /// </summary>
    private async Task<bool> ApplyStarsAsync(Player player, int value, bool preferConsole)
    {
        var combatState = player.PlayerCombatState;
        if (combatState == null)
        {
            return false;
        }

        int target = Math.Max(0, value);
        int diff = target - combatState.Stars;
        if (preferConsole && diff > 0)
        {
            await RunConsoleCommandAsync("设置充能球", _starsConsoleCmd, [diff.ToString()]);
        }

        if (combatState.Stars != target)
        {
            await PlayerCmd.SetStars(target, player);
        }

        return combatState.Stars == target;
    }

    /// <summary>
    /// 应用能量球栏位，优先使用 OrbQueue 提供的增减容量方法。
    /// </summary>
    private static bool ApplyOrbSlots(Player player, int value)
    {
        // 重入保护：用户连点"应用"按钮、或锁定定时器并发触发时，避免两个调整流程互相踩 OrbQueue.Capacity；
        // 同时也阻止 AddCapacity 内部连锁触发 NOrbManager 异常时引发的二次调整调用。
        if (_isApplyingOrbSlots)
        {
            MainFile.Logger.Info($"岁月史书充能球栏位调整跳过：已有正在执行的调整流程，输入={value}。");
            return false;
        }

        var combatState = player.PlayerCombatState;
        if (combatState == null)
        {
            MainFile.Logger.Info($"岁月史书充能球栏位调整失败：PlayerCombatState 为空，玩家类型={player.GetType().FullName}，输入={value}。");
            return false;
        }

        if (combatState.OrbQueue == null)
        {
            MainFile.Logger.Info($"岁月史书充能球栏位调整失败：OrbQueue 为空，玩家类型={player.GetType().FullName}，战斗状态类型={combatState.GetType().FullName}，输入={value}。");
            return false;
        }

        _isApplyingOrbSlots = true;
        try
        {
            int before = combatState.OrbQueue.Capacity;
            int target = Math.Clamp(value, 0, MaxOrbSlots);
            int beforeOrbCount = ReadOrbQueueCount(combatState.OrbQueue);
            MainFile.Logger.Info($"岁月史书充能球栏位开始调整：输入={value}，目标={target}，玩家类型={player.GetType().FullName}，战斗状态类型={combatState.GetType().FullName}，队列类型={combatState.OrbQueue.GetType().FullName}，调整前容量={before}，当前球数量={beforeOrbCount}。");
            while (combatState.OrbQueue.Capacity < target)
            {
                int stepBefore = combatState.OrbQueue.Capacity;
                try
                {
                    combatState.OrbQueue.AddCapacity(1);
                }
                catch (Exception ex)
                {
                    // AddCapacity 内部可能触发 NOrbManager 渲染异常（典型场景：非机器人角色首次开槽，slot 列表未初始化）。
                    // 吞掉单次失败避免外传，并立即跳出循环；剩余容量交给后面的反射校准兜底。
                    MainFile.Logger.Info($"岁月史书充能球栏位增加抛出异常并被吞掉：调整前={stepBefore}，目标={target}，异常类型={ex.GetType().FullName}，异常={ex.Message}。");
                    break;
                }
                int stepAfter = combatState.OrbQueue.Capacity;
                MainFile.Logger.Info($"岁月史书充能球栏位增加：调整前={stepBefore}，调整后={stepAfter}，目标={target}。");
                if (stepAfter <= stepBefore)
                {
                    MainFile.Logger.Info($"岁月史书充能球栏位增加中止：AddCapacity 后容量没有增加，当前={stepAfter}，目标={target}。");
                    break;
                }
            }

            while (combatState.OrbQueue.Capacity > target)
            {
                int stepBefore = combatState.OrbQueue.Capacity;
                try
                {
                    combatState.OrbQueue.RemoveCapacity(1);
                }
                catch (Exception ex)
                {
                    MainFile.Logger.Info($"岁月史书充能球栏位减少抛出异常并被吞掉：调整前={stepBefore}，目标={target}，异常类型={ex.GetType().FullName}，异常={ex.Message}。");
                    break;
                }
                int stepAfter = combatState.OrbQueue.Capacity;
                MainFile.Logger.Info($"岁月史书充能球栏位减少：调整前={stepBefore}，调整后={stepAfter}，目标={target}。");
                if (stepAfter >= stepBefore)
                {
                    MainFile.Logger.Info($"岁月史书充能球栏位减少中止：RemoveCapacity 后容量没有减少，当前={stepAfter}，目标={target}。");
                    break;
                }
            }

            if (combatState.OrbQueue.Capacity != target)
            {
                MainFile.Logger.Info($"岁月史书充能球栏位常规调整未达标：当前={combatState.OrbQueue.Capacity}，目标={target}，开始尝试反射校准。");
                TryForceSetOrbQueueCapacity(combatState.OrbQueue, target);
            }

            int after = combatState.OrbQueue.Capacity;
            int afterOrbCount = ReadOrbQueueCount(combatState.OrbQueue);
            bool layoutRefreshed = TryRefreshOrbLayout(target);
            MainFile.Logger.Info($"岁月史书充能球栏位调整结束：输入={value}，目标={target}，调整前容量={before}，调整后容量={after}，调整前球数量={beforeOrbCount}，调整后球数量={afterOrbCount}，布局刷新={(layoutRefreshed ? "成功" : "跳过或失败")}。");
            return after == target;
        }
        finally
        {
            _isApplyingOrbSlots = false;
        }
    }

    /// <summary>
    /// 充能球栏位调整后，主动触发一次充能球布局刷新，尽量避免运行时容量与显示列表不同步。
    /// </summary>
    private static bool TryRefreshOrbLayout(int? targetCapacity = null)
    {
        try
        {
            Type? orbManagerType = typeof(CombatManager).Assembly.GetType("MegaCrit.Sts2.Core.Nodes.Orbs.NOrbManager");
            if (orbManagerType == null)
            {
                MainFile.Logger.Info("岁月史书充能球布局刷新失败：找不到 NOrbManager 类型。");
                return false;
            }

            object? orbManager = ResolveOrbManagerInstance(orbManagerType);
            if (orbManager == null)
            {
                MainFile.Logger.Info($"岁月史书充能球布局刷新失败：找不到 NOrbManager 实例，类型={orbManagerType.FullName}。");
                LogOrbManagerMembersOnce(orbManagerType);
                return false;
            }

            LogOrbManagerMembersOnce(orbManagerType);
            bool invoked = false;
            if (targetCapacity.HasValue)
            {
                // 有目标值：依赖 AddSlotAnim/RemoveSlotAnim 内部自带的 TweenLayout 完成动画，
                // 这里不能再额外调 TweenLayout，否则会 Kill 掉刚开始的动画导致新槽位卡在 (0,0)。
                invoked = TryEnsureOrbSlots(orbManagerType, orbManager, targetCapacity.Value);
            }
            else
            {
                // 无目标值：异常补救路径，只触发一次布局刷新作为兜底。
                invoked = TryInvokeInstanceMethod(orbManagerType, orbManager, "TweenLayout");
            }
            MainFile.Logger.Info($"岁月史书充能球布局刷新结束：目标栏位={(targetCapacity?.ToString() ?? "未指定")}，是否调用成功={invoked}。");
            return invoked;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书充能球布局刷新失败：异常类型={ex.GetType().FullName}，异常={ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 应用全局手牌上限，使用游戏公开静态字段。
    /// </summary>
    /// <summary>
    /// 褰撳厖鑳界悆甯冨眬鍑虹幇瓒婄晫寮傚父鏃讹紝涓诲姩灏濊瘯琛ユ晳锛岄伩鍏嶅紓甯哥户缁墿鏁ｅ埌鍗＄墖缁撶畻娴佺▼銆?
    /// </summary>
    public static void TryRecoverOrbLayoutAfterException(string source)
    {
        if (GmRunContextService.IsMultiplayer())
        {
            MainFile.Logger.Info($"岁月史书充能球异常补救在多人模式下已跳过：来源={source}。");
            return;
        }

        // 重入保护：Finalizer 调本方法 → 内部反射调 TweenLayout → 又抛 ArgumentOutOfRangeException → Finalizer 又调本方法，会形成同步递归死循环导致游戏卡死。
        // 第一次进入时置位，后续递归调用直接返回，让 Finalizer 自己走 return null 把异常吞掉。
        if (_isRecoveringOrbLayout)
        {
            MainFile.Logger.Info($"岁月史书充能球异常补救已在执行，跳过递归调用避免死循环：来源={source}。");
            return;
        }

        _isRecoveringOrbLayout = true;
        try
        {
            Player? player = GetCurrentPlayer();
            int? target = player?.PlayerCombatState?.OrbQueue?.Capacity;
            bool repaired = TryRefreshOrbLayout(target);
            MainFile.Logger.Info($"岁月史书充能球异常补救：来源={source}，目标栏位={(target?.ToString() ?? "未知")}，结果={(repaired ? "成功" : "失败")}。");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书充能球异常补救失败：来源={source}，异常={ex.Message}。");
        }
        finally
        {
            _isRecoveringOrbLayout = false;
        }
    }

    /// <summary>
    /// 瑙ｆ瀽 NOrbManager 鍗曚緥瀹炰緥锛屽吋瀹瑰睘鎬?瀛楁涓ょ鏆撮湶鏂瑰紡銆?
    /// </summary>
    private static object? ResolveOrbManagerInstance(Type orbManagerType)
    {
        PropertyInfo? instanceProperty = orbManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (instanceProperty?.GetValue(null) is { } fromProperty)
        {
            MainFile.Logger.Info($"岁月史书充能球布局刷新：NOrbManager 实例来源=Instance 属性，实例类型={fromProperty.GetType().FullName}。");
            return fromProperty;
        }

        FieldInfo? instanceField = orbManagerType.GetField("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                                   ?? orbManagerType.GetField("_instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                                   ?? orbManagerType.GetField("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        object? fromField = instanceField?.GetValue(null);
        if (fromField != null)
        {
            MainFile.Logger.Info($"岁月史书充能球布局刷新：NOrbManager 实例来源={instanceField!.Name} 字段，实例类型={fromField.GetType().FullName}。");
        }
        else
        {
            MainFile.Logger.Info("岁月史书充能球布局刷新：NOrbManager 静态实例属性/字段均为空。");
        }

        return fromField ?? ResolveOrbManagerFromSceneTree(orbManagerType);
    }

    /// <summary>
    /// 淇濆畧鍦拌ˉ榻愬厖鑳界悆妲戒綅鑺傜偣鏁伴噺锛屼紭鍏堣皟鐢ㄦ父鎴忚嚜韬殑 SpawnSlot/SpawnSlots 鏂规硶銆?
    /// </summary>
    private static bool TryEnsureOrbSlots(Type orbManagerType, object orbManager, int targetCapacity)
    {
        // 反编译游戏源码确认 AddSlotAnim(int amount) / RemoveSlotAnim(int amount) 的参数都是"增/减几个"，
        // 而且这两个方法内部已经调用了 TweenLayout，所以这里只调一次、传 delta 即可，
        // 避免循环或重复 TweenLayout 打断动画（之前的 bug：把 currentCount 当作 amount 传过去，新槽位卡在 (0,0)）。
        int target = Math.Clamp(targetCapacity, 0, MaxOrbSlots);
        int? slotCount = ReadOrbSlotCount(orbManagerType, orbManager);
        if (!slotCount.HasValue)
        {
            MainFile.Logger.Info($"岁月史书充能球布局补齐跳过：无法读取当前槽位节点数量，目标={target}。");
            return false;
        }

        int current = slotCount.Value;
        if (current == target)
        {
            MainFile.Logger.Info($"岁月史书充能球布局补齐跳过：当前槽位={current} 已与目标一致。");
            return true;
        }

        // amount 一定是正整数（要"新增/移除几个"），方向由 current 与 target 的大小关系决定。
        string methodName = current < target ? "AddSlotAnim" : "RemoveSlotAnim";
        int amount = Math.Abs(target - current);
        MainFile.Logger.Info($"岁月史书充能球布局调整开始：方法={methodName}，amount={amount}，当前槽位={current}，目标={target}。");
        bool invoked = TryInvokeInstanceMethod(orbManagerType, orbManager, methodName, amount);

        int? after = ReadOrbSlotCount(orbManagerType, orbManager);
        MainFile.Logger.Info($"岁月史书充能球布局调整结束：方法={methodName}，amount={amount}，最终槽位={(after?.ToString() ?? "未知")}，目标={target}，是否调用成功={invoked}。");
        return invoked;
    }

    /// <summary>
    /// 璇诲彇 NOrbManager 褰撳墠妲戒綅鑺傜偣鍒楄〃鏁伴噺銆?
    /// </summary>
    private static int? ReadOrbSlotCount(Type orbManagerType, object orbManager)
    {
        FieldInfo? slotsField = orbManagerType.GetField("_slots", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                 ?? orbManagerType.GetField("slots", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                 ?? orbManagerType.GetField("_orbs", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                 ?? orbManagerType.GetField("orbs", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (slotsField?.GetValue(orbManager) is System.Collections.ICollection collection)
        {
            MainFile.Logger.Info($"岁月史书充能球布局读取槽位：字段={slotsField.Name}，数量={collection.Count}。");
            return collection.Count;
        }

        MainFile.Logger.Info("岁月史书充能球布局读取槽位失败：未找到 _slots/slots/_orbs/orbs 集合字段。");
        return null;
    }

    /// <summary>
    /// 从 Godot 场景树递归查找 NOrbManager 节点，适配游戏没有暴露静态单例的版本。
    /// </summary>
    private static object? ResolveOrbManagerFromSceneTree(Type orbManagerType)
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree)
            {
                MainFile.Logger.Info($"岁月史书充能球布局刷新：当前 MainLoop 不是 SceneTree，类型={Engine.GetMainLoop()?.GetType().FullName ?? "null"}。");
                return null;
            }

            Node root = tree.Root;
            MainFile.Logger.Info($"岁月史书充能球布局刷新：开始从场景树查找 NOrbManager，根节点={root.GetType().FullName}/{root.Name}。");
            Node? node = FindNodeByType(root, orbManagerType, depth: 0);
            if (node == null)
            {
                MainFile.Logger.Info("岁月史书充能球布局刷新：场景树中未找到 NOrbManager 节点。");
                return null;
            }

            MainFile.Logger.Info($"岁月史书充能球布局刷新：NOrbManager 实例来源=场景树，节点类型={node.GetType().FullName}，节点路径={node.GetPath()}。");
            return node;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书充能球布局刷新：场景树查找 NOrbManager 失败，异常类型={ex.GetType().FullName}，异常={ex.Message}。");
            return null;
        }
    }

    /// <summary>
    /// 按类型递归查找 Godot 节点，并限制递归深度避免异常场景树导致卡顿。
    /// </summary>
    private static Node? FindNodeByType(Node parent, Type targetType, int depth)
    {
        if (depth > 32)
        {
            return null;
        }

        foreach (Node child in parent.GetChildren())
        {
            if (targetType.IsAssignableFrom(child.GetType()) || string.Equals(child.GetType().FullName, targetType.FullName, StringComparison.Ordinal))
            {
                return child;
            }

            Node? nested = FindNodeByType(child, targetType, depth + 1);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>
    /// 读取充能球队列当前球数量，优先读 Count，失败时再读内部集合。
    /// </summary>
    private static int ReadOrbQueueCount(object orbQueue)
    {
        Type type = orbQueue.GetType();
        PropertyInfo? countProperty = type.GetProperty("Count", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (countProperty?.GetValue(orbQueue) is int count)
        {
            return count;
        }

        FieldInfo? countField = type.GetField("Count", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                ?? type.GetField("_count", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                ?? type.GetField("count", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (countField?.GetValue(orbQueue) is int fieldCount)
        {
            return fieldCount;
        }

        object? orbs = ReadOrbQueueCollection(orbQueue);
        if (orbs is System.Collections.ICollection collection)
        {
            return collection.Count;
        }

        if (orbs is System.Collections.IEnumerable enumerable)
        {
            return enumerable.Cast<object?>().Count(item => item != null);
        }

        return 0;
    }

    /// <summary>
    /// 反射校准充能球队列容量，用于游戏增减容量方法没有生效时的兜底。
    /// </summary>
    private static bool TryForceSetOrbQueueCapacity(object orbQueue, int target)
    {
        Type type = orbQueue.GetType();
        try
        {
            PropertyInfo? capacityProperty = type.GetProperty("Capacity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (capacityProperty?.CanWrite == true)
            {
                capacityProperty.SetValue(orbQueue, target);
                MainFile.Logger.Info($"岁月史书充能球栏位反射校准：已写入 Capacity 属性，目标={target}，当前={capacityProperty.GetValue(orbQueue)}。");
                return capacityProperty.GetValue(orbQueue) is int propertyValue && propertyValue == target;
            }

            FieldInfo? capacityField = type.GetField("<Capacity>k__BackingField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                       ?? type.GetField("_capacity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                       ?? type.GetField("capacity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                       ?? type.GetField("Capacity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (capacityField == null)
            {
                MainFile.Logger.Info($"岁月史书充能球栏位反射校准失败：找不到容量字段，队列类型={type.FullName}。");
                return false;
            }

            capacityField.SetValue(orbQueue, target);
            MainFile.Logger.Info($"岁月史书充能球栏位反射校准：已写入字段={capacityField.Name}，目标={target}，当前={capacityField.GetValue(orbQueue)}。");
            return capacityField.GetValue(orbQueue) is int fieldValue && fieldValue == target;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书充能球栏位反射校准失败：队列类型={type.FullName}，目标={target}，异常类型={ex.GetType().FullName}，异常={ex.Message}。");
            return false;
        }
    }

    /// <summary>
    /// 读取充能球队列内部集合，用于日志统计当前已有充能球数量。
    /// </summary>
    private static object? ReadOrbQueueCollection(object orbQueue)
    {
        Type type = orbQueue.GetType();
        PropertyInfo? orbsProperty = type.GetProperty("Orbs", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                     ?? type.GetProperty("Items", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (orbsProperty != null)
        {
            return orbsProperty.GetValue(orbQueue);
        }

        FieldInfo? orbsField = type.GetField("Orbs", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? type.GetField("orbs", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? type.GetField("_orbs", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? type.GetField("Items", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                               ?? type.GetField("items", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return orbsField?.GetValue(orbQueue);
    }

    /// <summary>
    /// 鍙嶅皠璋冪敤瀹炰緥鏂规硶锛屽弬鏁扮鍚嶄笉鍖归厤鏃堕潤榛樿烦杩囥€?
    /// </summary>
    private static bool TryInvokeInstanceMethod(Type type, object target, string methodName, params object[] args)
    {
        MethodInfo? method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(candidate => candidate.Name == methodName && candidate.GetParameters().Length == args.Length);
        if (method == null)
        {
            MainFile.Logger.Info($"岁月史书充能球反射调用跳过：方法={methodName}/{args.Length} 未找到，类型={type.FullName}。");
            return false;
        }

        try
        {
            method.Invoke(target, args);
            MainFile.Logger.Info($"岁月史书充能球反射调用成功：方法={method.Name}/{args.Length}，类型={type.FullName}。");
            return true;
        }
        catch (Exception ex)
        {
            Exception actual = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
            MainFile.Logger.Info($"岁月史书充能球反射调用失败：方法={method.Name}/{args.Length}，类型={type.FullName}，异常类型={actual.GetType().FullName}，异常={actual.Message}。");
            return false;
        }
    }

    /// <summary>
    /// 棣栨鍛戒腑鏃惰褰?NOrbManager 鍏抽敭鎴愬憳锛屼究浜庡疄鏈烘帓鏌ョ増鏈樊寮傘€?
    /// </summary>
    private static void LogOrbManagerMembersOnce(Type orbManagerType)
    {
        if (_orbManagerMembersLogged)
        {
            return;
        }

        _orbManagerMembersLogged = true;
        try
        {
            string methods = string.Join(", ", orbManagerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(method => method.Name.Contains("Slot", StringComparison.OrdinalIgnoreCase) ||
                                 method.Name.Contains("Orb", StringComparison.OrdinalIgnoreCase) ||
                                 method.Name.Contains("Refresh", StringComparison.OrdinalIgnoreCase) ||
                                 method.Name.Contains("Layout", StringComparison.OrdinalIgnoreCase) ||
                                 method.Name.Contains("Init", StringComparison.OrdinalIgnoreCase) ||
                                 method.Name.Contains("Bind", StringComparison.OrdinalIgnoreCase) ||
                                 method.Name.Contains("Setup", StringComparison.OrdinalIgnoreCase) ||
                                 method.Name.Contains("Create", StringComparison.OrdinalIgnoreCase) ||
                                 method.Name.Contains("Spawn", StringComparison.OrdinalIgnoreCase) ||
                                 method.Name.Contains("OnPlayer", StringComparison.OrdinalIgnoreCase) ||
                                 method.Name.Contains("Combat", StringComparison.OrdinalIgnoreCase) ||
                                 method.Name.Contains("Begin", StringComparison.OrdinalIgnoreCase) ||
                                 method.Name.Contains("Enter", StringComparison.OrdinalIgnoreCase) ||
                                 method.Name.Contains("Enable", StringComparison.OrdinalIgnoreCase))
                .Select(method => $"{method.Name}/{method.GetParameters().Length}")
                .Distinct());
            string fields = string.Join(", ", orbManagerType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(field => field.Name.Contains("slot", StringComparison.OrdinalIgnoreCase) ||
                                field.Name.Contains("orb", StringComparison.OrdinalIgnoreCase) ||
                                field.Name.Contains("instance", StringComparison.OrdinalIgnoreCase) ||
                                field.Name.Contains("player", StringComparison.OrdinalIgnoreCase) ||
                                field.Name.Contains("combat", StringComparison.OrdinalIgnoreCase) ||
                                field.Name.Contains("state", StringComparison.OrdinalIgnoreCase) ||
                                field.Name.Contains("queue", StringComparison.OrdinalIgnoreCase))
                .Select(field => field.Name)
                .Distinct());
            MainFile.Logger.Info($"岁月史书充能球反射信息：方法=[{methods}]，字段=[{fields}]。");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书充能球反射信息记录失败：{ex.Message}");
        }
    }

    private static bool ApplyHandLimit(int value)
    {
        int target = Math.Max(1, value);
        HandLimitOverride = target;
        SaveCurrentState();
        MainFile.Logger.Info($"岁月史书手牌上限已设置：目标={target}。");
        return true;
    }

    /// <summary>
    /// 应用每回合抽牌数，修改 CombatManager 的基础抽牌字段。
    /// </summary>
    private static bool ApplyCardsPerTurn(int value)
    {
        int target = Math.Clamp(value, 0, 99);
        return TrySetStaticIntField(typeof(CombatManager), nameof(CombatManager.baseHandDrawCount), target);
    }

    /// <summary>
    /// 应用进阶等级，直接修改当前 RunState。
    /// </summary>
    private static bool ApplyAscensionLevel(int value)
    {
        RunState? state = GetCurrentRunState();
        if (state == null)
        {
            return false;
        }

        int target = Math.Clamp(value, 0, 20);
        FieldInfo? field = typeof(RunState).GetField("<AscensionLevel>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
        {
            return false;
        }

        field.SetValue(state, target);
        return state.AscensionLevel == target;
    }

    /// <summary>
    /// 读取静态整数字段；字段不存在时返回空。
    /// </summary>
    private static int? ReadStaticIntField(Type type, string fieldName)
    {
        FieldInfo? field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        return field?.GetValue(null) is int value ? value : null;
    }

    /// <summary>
    /// 设置静态整数字段；用于修改游戏暴露为字段而非属性的配置。
    /// </summary>
    private static bool TrySetStaticIntField(Type type, string fieldName, int value)
    {
        FieldInfo? field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (field == null)
        {
            return false;
        }

        field.SetValue(null, value);
        return field.GetValue(null) is int current && current == value;
    }

    /// <summary>
    /// 应用百分比倍率，限制在 0 到 9999 之间。
    /// </summary>
    private static bool ApplyPercent(ChronicleValueKey key, int value)
    {
        int percent = Math.Clamp(value, 0, 9999);
        switch (key)
        {
            case ChronicleValueKey.PlayerDamagePercent:
                PlayerDamagePercent = percent;
                break;
            case ChronicleValueKey.EnemyDamagePercent:
                EnemyDamagePercent = percent;
                break;
            case ChronicleValueKey.ShopPricePercent:
                ShopPricePercent = percent;
                break;
            case ChronicleValueKey.PotionEffectPercent:
                PotionEffectPercent = percent;
                break;
        }

        SaveCurrentState();
        return true;
    }

    /// <summary>
    /// 应用奖励倍率，只允许正整数，并设置保守上限避免奖励界面被大量奖励卡死。
    /// </summary>
    private static bool ApplyRewardMultiplier(int value)
    {
        int multiplier = Math.Clamp(value, DefaultRewardMultiplier, MaxRewardMultiplier);
        RewardMultiplier = multiplier;
        SaveCurrentState();
        MainFile.Logger.Info($"岁月史书奖励倍率已设置：输入={value}，实际倍率={RewardMultiplier}。");
        return true;
    }

    /// <summary>
    /// 把地图点转换成 UI 可展示的房间选项。
    /// </summary>
    private static ChronicleMapRoomOption CreateMapRoomOption(RunState state, MapPoint point)
    {
        bool isVisited = state.VisitedMapCoords.Contains(point.coord);
        bool isCurrent = state.CurrentMapCoord.HasValue && state.CurrentMapCoord.Value.Equals(point.coord);
        string name = BuildMapRoomName(point);
        string status = isCurrent
            ? GmUiText.Pick("当前", "Current")
            : isVisited
                ? GmUiText.Pick("已访问", "Visited")
                : GmUiText.Pick("未访问", "Unvisited");
        string detail = GmUiText.Pick(
            $"第 {point.coord.row + 1} 层，坐标=({point.coord.col},{point.coord.row})，状态={status}",
            $"Floor {point.coord.row + 1}, coord=({point.coord.col},{point.coord.row}), status={status}");
        return new ChronicleMapRoomOption(BuildMapRoomOptionId(point.coord), $"{name}\n{status}", detail, isVisited, isCurrent);
    }

    /// <summary>
    /// 拼接地图房间名称，让按钮文本能看懂层数、坐标和房型。
    /// </summary>
    private static string BuildMapRoomName(MapPoint point)
    {
        return GmUiText.Pick(
            $"第 {point.coord.row + 1} 层 {ToMapPointTypeText(point.PointType)} ({point.coord.col},{point.coord.row})",
            $"Floor {point.coord.row + 1} {ToMapPointTypeText(point.PointType)} ({point.coord.col},{point.coord.row})");
    }

    /// <summary>
    /// 把地图坐标转成稳定字符串 ID，避免 UI 直接保存游戏对象引用。
    /// </summary>
    private static string BuildMapRoomOptionId(MapCoord coord)
    {
        return $"{coord.col}:{coord.row}";
    }

    /// <summary>
    /// 从 UI 传回的字符串 ID 中解析地图坐标。
    /// </summary>
    private static bool TryParseMapRoomOptionId(string optionId, out MapCoord coord)
    {
        coord = new MapCoord(0, 0);
        string[] parts = optionId.Split(':');
        if (parts.Length != 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out int col) || !int.TryParse(parts[1], out int row))
        {
            return false;
        }

        coord = new MapCoord(col, row);
        return true;
    }

    /// <summary>
    /// 跳转前整理当前状态，避免奖励、地图或覆盖层界面挡住新房间。
    /// </summary>
    private static async Task PrepareRunForMapJumpAsync(RunManager manager, RunState state)
    {
        if (state.CurrentRoom is CombatRoom)
        {
            MainFile.Logger.Info("岁月史书任意房间跳转预处理：当前在战斗房间，先调用 ProceedFromTerminalRewardsScreen 离开奖励界面。");
            await manager.ProceedFromTerminalRewardsScreen();
        }

        CloseMapAndOverlayScreens();
    }

    /// <summary>
    /// 关闭地图和覆盖层，避免跳转成功后画面仍停留在旧界面。
    /// </summary>
    private static void CloseMapAndOverlayScreens()
    {
        try
        {
            if (NMapScreen.Instance?.IsOpen == true)
            {
                NMapScreen.Instance.Close();
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书任意房间跳转预处理：关闭地图页面失败，异常={ex.Message}。");
        }

        try
        {
            NOverlayStack? overlayStack = NOverlayStack.Instance;
            if (overlayStack != null && overlayStack.ScreenCount > 0)
            {
                overlayStack.HideOverlays();
                overlayStack.HideBackstop();
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书任意房间跳转预处理：隐藏覆盖层失败，异常={ex.Message}。");
        }
    }

    /// <summary>
    /// 把地图点类型转换成页面展示文本。
    /// </summary>
    private static string ToMapPointTypeText(MapPointType pointType)
    {
        return pointType switch
        {
            MapPointType.Shop => GmUiText.Pick("商店", "Shop"),
            MapPointType.Treasure => GmUiText.Pick("宝箱", "Treasure"),
            MapPointType.RestSite => GmUiText.Pick("营火", "Rest Site"),
            MapPointType.Monster => GmUiText.Pick("普通怪", "Monster"),
            MapPointType.Elite => GmUiText.Pick("精英", "Elite"),
            MapPointType.Boss => GmUiText.Pick("Boss", "Boss"),
            MapPointType.Ancient => GmUiText.Pick("先古之民", "Ancient"),
            MapPointType.Unknown => GmUiText.Pick("未知房间", "Unknown"),
            _ => pointType.ToString(),
        };
    }

    /// <summary>
    /// 手牌哨兵：每帧（高频）调用，仅做"手牌是否变少"的轻量观测；变少才触发补牌。
    /// 替代原先的"主定时器无脑补牌"轮询，避免每 1.5 秒触发一次完整抽牌动画造成的卡顿。
    /// 实现要点：
    ///   - 仅在 AlwaysDrawToHandLimitEnabled 时跑活，关闭时立即清基线避免下次开启误触发；
    ///   - 战斗外（PlayerCombatState 为 null）也清基线；
    ///   - 内部仅做 int 比较，跑空时几乎零开销；
    ///   - 真正补牌仍走 DrawToHandLimitInternalAsync，复用 _isDrawingToHandLimit 互斥锁、
    ///     摸牌区/弃牌区为空的兜底逻辑、以及完整日志。
    /// </summary>
    public async Task TryDrawIfHandShrunkAsync()
    {
        if (!AlwaysDrawToHandLimitEnabled)
        {
            // 开关已关：清基线，下次开启时哨兵第一次观测会重新记录初始值，不会误触发
            ResetHandWatcherState();
            return;
        }

        // 已经在补牌中：让正在跑的补牌流程跑完，避免重入；动画结束后下次哨兵再判断
        if (_isDrawingToHandLimit)
        {
            return;
        }

        Player? player = GetCurrentPlayer();
        PlayerCombatState? combatState = player?.PlayerCombatState;
        if (combatState == null)
        {
            // 不在战斗中（地图、商店、事件）：哨兵观测无意义，清基线
            ResetHandWatcherState();
            return;
        }

        int handLimit = GetEffectiveHandLimit();
        int currentHandCount = combatState.Hand.Cards.Count;
        int previousHandCount = _lastObservedHandCount;
        // 关键：先把"上次观测值"刷新为当前，无论后续是否补牌；
        // 这样补牌动画期间 hand 增加不会被识别成"上一次=4，本次=5，没减少"误判，
        // 因为补牌过程内的 _isDrawingToHandLimit 互斥已经把哨兵挡在外面。
        _lastObservedHandCount = currentHandCount;

        // 首次观测：如果读档或刚进战斗时手牌本来就没满，直接补一次；否则只记录基线。
        if (previousHandCount < 0)
        {
            if (currentHandCount < handLimit)
            {
                await DrawToHandLimitInternalAsync("手牌哨兵首次补牌");
                _lastObservedHandCount = combatState.Hand.Cards.Count;
            }
            return;
        }

        if (currentHandCount < previousHandCount)
        {
            _handDrawRequested = true;
            _lastHandShrinkAt = DateTime.UtcNow;
        }

        if (!_handDrawRequested)
        {
            return;
        }

        if (DateTime.UtcNow - _lastHandShrinkAt < HandDrawDebounceTime)
        {
            return;
        }

        _handDrawRequested = false;
        await DrawToHandLimitInternalAsync("手牌减少触发补牌");
        _lastObservedHandCount = combatState.Hand.Cards.Count;
    }

    /// <summary>
    /// 按当前手牌缺口抽牌到上限；摸牌区为空但弃牌区有牌时，交给游戏抽牌流程洗牌后继续抽。
    /// </summary>
    private async Task<string> DrawToHandLimitInternalAsync(string actionName)
    {
        if (_isDrawingToHandLimit)
        {
            return GmUiText.ChronicleNothingToDo;
        }

        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            return GmUiText.ChronicleNotInRun;
        }

        var combatState = player.PlayerCombatState;
        if (combatState == null)
        {
            return GmUiText.ChronicleNeedCombat;
        }

        int handLimit = GetEffectiveHandLimit();
        int handCount = combatState.Hand.Cards.Count;
        int handGap = Math.Max(0, handLimit - handCount);
        if (handGap <= 0)
        {
            _drawPileEmptyLogged = false;
            return GmUiText.ChronicleNothingToDo;
        }

        int drawPileCount = combatState.DrawPile.Cards.Count;
        int discardPileCount = combatState.DiscardPile.Cards.Count;
        if (drawPileCount <= 0 && discardPileCount <= 0)
        {
            if (!_drawPileEmptyLogged)
            {
                MainFile.Logger.Info($"岁月史书{actionName}停止：摸牌区和弃牌区都没有可摸的牌，当前手牌={handCount}，手牌上限={handLimit}。");
                _drawPileEmptyLogged = true;
            }

            return GmUiText.ChronicleNothingToDo;
        }

        int drawCount = handGap;
        _isDrawingToHandLimit = true;
        _drawPileEmptyLogged = false;
        try
        {
            bool shouldLogAutoDraw = ShouldLogAutoDraw(actionName);
            if (shouldLogAutoDraw)
            {
                MainFile.Logger.Info($"岁月史书{actionName}开始：当前手牌={handCount}，手牌上限={handLimit}，摸牌区={drawPileCount}，弃牌区={discardPileCount}，本次补牌={drawCount}。");
            }

            if (GmRunContextService.IsMultiplayer())
            {
                string message = await TrySubmitChronicleNetworkCommandAsync(actionName, $"draw {drawCount}", requireCombat: true);
                if (shouldLogAutoDraw || !string.Equals(message, GmUiText.ChronicleSubmitSuccess, StringComparison.Ordinal))
                {
                    MainFile.Logger.Info($"岁月史书多人{actionName}提交结束：结果={message}，当前手牌={combatState.Hand.Cards.Count}，摸牌区={combatState.DrawPile.Cards.Count}，弃牌区={combatState.DiscardPile.Cards.Count}。");
                }

                return message;
            }

            bool success = await RunConsoleCommandAsync(actionName, _drawConsoleCmd, [drawCount.ToString()], logCommand: shouldLogAutoDraw);
            if (shouldLogAutoDraw || !success)
            {
                MainFile.Logger.Info($"岁月史书{actionName}结束：success={success}，当前手牌={combatState.Hand.Cards.Count}，摸牌区={combatState.DrawPile.Cards.Count}，弃牌区={combatState.DiscardPile.Cards.Count}。");
            }
            return success ? GmUiText.ChronicleApplySuccess : GmUiText.ChronicleApplyFailed;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书{actionName}异常：{ex.Message}");
            return GmUiText.ChronicleApplyFailed;
        }
        finally
        {
            _isDrawingToHandLimit = false;
        }
    }

    /// <summary>
    /// 岁月史书手牌上限补丁使用的抽牌实现；复刻游戏抽牌主流程，但把硬编码 10 改成 GM 上限。
    /// </summary>
    public static async Task<IEnumerable<CardModel>> DrawCardsWithHandLimitAsync(PlayerChoiceContext choiceContext, decimal count, Player player, bool fromHandDraw)
    {
        if (CombatManager.Instance.IsOverOrEnding)
        {
            return Array.Empty<CardModel>();
        }

        ICombatState? combatState = player.Creature.CombatState;
        if (combatState == null)
        {
            return Array.Empty<CardModel>();
        }

        if (!Hook.ShouldDraw(combatState, player, fromHandDraw, out AbstractModel? modifier))
        {
            if (modifier != null)
            {
                await Hook.AfterPreventingDraw(combatState, modifier);
            }

            return Array.Empty<CardModel>();
        }

        List<CardModel> result = [];
        CardPile hand = PileType.Hand.GetPile(player);
        CardPile drawPile = PileType.Draw.GetPile(player);
        int drawsRequested = count > 0m ? (int)Math.Ceiling(count) : 0;
        if (drawsRequested == 0)
        {
            return result;
        }

        int handLimit = GetEffectiveHandLimit();
        int handGap = Math.Max(0, handLimit - hand.Cards.Count);
        if (handGap == 0)
        {
            CheckIfDrawIsPossibleWithHandLimit(player);
            return result;
        }

        for (int index = 0; index < drawsRequested; index++)
        {
            if (handGap <= 0)
            {
                break;
            }

            if (!CheckIfDrawIsPossibleWithHandLimit(player))
            {
                break;
            }

            await CardPileCmd.ShuffleIfNecessary(choiceContext, player);
            if (!CheckIfDrawIsPossibleWithHandLimit(player))
            {
                break;
            }

            CardModel? card = drawPile.Cards.FirstOrDefault();
            if (card == null || hand.Cards.Count >= handLimit)
            {
                break;
            }

            result.Add(card);
            await AddCardsToHandWithHandLimitAsync([card], hand, CardPilePosition.Bottom, null, skipVisuals: false, isChangingOwners: false);
            CombatManager.Instance.History.CardDrawn(combatState, card, fromHandDraw);
            await Hook.AfterCardDrawn(combatState, choiceContext, card, fromHandDraw);
            card.InvokeDrawn();
            NDebugAudioManager.Instance?.Play("card_deal.mp3", 0.25f, PitchVariance.Small);
            handGap = Math.Max(0, handLimit - hand.Cards.Count);
        }

        return result;
    }

    /// <summary>
    /// 岁月史书手牌上限补丁使用的发牌实现；当目标是手牌时按 GM 上限决定进手牌还是进弃牌堆。
    /// </summary>
    public static async Task<IReadOnlyList<CardPileAddResult>> AddCardsToHandWithHandLimitAsync(
        IEnumerable<CardModel> cards,
        CardPile newPile,
        CardPilePosition position,
        AbstractModel? source,
        bool skipVisuals,
        bool isChangingOwners)
    {
        List<CardModel> cardList = cards.ToList();
        if (cardList.Count == 0)
        {
            return Array.Empty<CardPileAddResult>();
        }

        if (newPile.Type != PileType.Hand)
        {
            return await CardPileCmd.Add(cardList, newPile, position, source, skipVisuals, isChangingOwners);
        }

        if (newPile.IsCombatPile && CombatManager.Instance.IsEnding)
        {
            return cardList.Select(card => new CardPileAddResult
            {
                success = false,
                cardAdded = card,
                oldPile = card.Pile,
                targetPile = newPile.Type,
                modifyingModels = null,
            }).ToList();
        }

        List<CardPileAddResult> results = [];
        Player? owningPlayer = cardList.FirstOrDefault()?.Owner;
        int handLimit = GetEffectiveHandLimit();
        foreach (CardModel card in cardList)
        {
            CardPile? oldPile = card.Pile;
            if (card.Owner == null || owningPlayer == null || card.Owner != owningPlayer || card.Owner.Creature.IsDead || card.HasBeenRemovedFromState)
            {
                results.Add(new CardPileAddResult
                {
                    success = false,
                    cardAdded = card,
                    oldPile = oldPile,
                    targetPile = newPile.Type,
                    modifyingModels = null,
                });
                continue;
            }

            CardPile targetPile = newPile.Cards.Count < handLimit
                ? newPile
                : PileType.Discard.GetPile(card.Owner);
            CardPileAddResult result = new()
            {
                success = true,
                cardAdded = card,
                oldPile = oldPile,
                targetPile = newPile.Type,
                modifyingModels = null,
            };
            results.Add(result);

            if (oldPile != null)
            {
                card.RemoveFromCurrentPile(skipVisuals);
            }

            targetPile.AddInternal(card, position switch
            {
                CardPilePosition.Bottom => -1,
                CardPilePosition.Top => 0,
                CardPilePosition.Random => card.Owner.RunState.Rng.Shuffle.NextInt(targetPile.Cards.Count + 1),
                _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
            });

            if (oldPile == null && targetPile.IsCombatPile && card.CombatState != null && !isChangingOwners)
            {
                await Hook.AfterCardEnteredCombat(card.CombatState, card);
            }

            if (targetPile.Type == PileType.Hand)
            {
                TryAttachCardNodeToHand(card, skipVisuals);
            }
            else
            {
                targetPile.InvokeCardAddFinished();
            }

            await Hook.AfterCardChangedPiles(card.Owner.RunState, card.CombatState, card, oldPile?.Type ?? PileType.None, source);
        }

        return results;
    }

    /// <summary>
    /// 检查当前是否还能抽牌，使用 GM 手牌上限替代游戏硬编码 10。
    /// </summary>
    private static bool CheckIfDrawIsPossibleWithHandLimit(Player player)
    {
        if (PileType.Draw.GetPile(player).Cards.Count + PileType.Discard.GetPile(player).Cards.Count == 0)
        {
            ThinkCmd.Play(new LocString("combat_messages", "NO_DRAW"), player.Creature, 2.0);
            return false;
        }

        if (PileType.Hand.GetPile(player).Cards.Count >= GetEffectiveHandLimit())
        {
            ThinkCmd.Play(new LocString("combat_messages", "HAND_FULL"), player.Creature, 2.0);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 把新进手牌的卡片挂到当前战斗手牌 UI；失败时只记录日志，不阻断数据层移动。
    /// </summary>
    private static void TryAttachCardNodeToHand(CardModel card, bool skipVisuals)
    {
        if (skipVisuals || NCombatRoom.Instance?.Ui?.Hand == null)
        {
            card.Pile?.InvokeCardAddFinished();
            return;
        }

        try
        {
            NCard? existingNode = NCard.FindOnTable(card);
            if (existingNode != null)
            {
                NCombatRoom.Instance.Ui.Hand.Add(existingNode);
                card.Pile?.InvokeCardAddFinished();
                return;
            }

            NCard? cardNode = NCard.Create(card);
            if (cardNode == null)
            {
                card.Pile?.InvokeCardAddFinished();
                return;
            }

            NCombatRoom.Instance.Ui.AddChild(cardNode);
            cardNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            NCombatRoom.Instance.Ui.Hand.Add(cardNode);
            card.Pile?.InvokeCardAddFinished();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书手牌上限补丁：挂载手牌 UI 失败，卡牌={card.Id.Entry}，异常={ex.Message}。");
            card.Pile?.InvokeCardAddFinished();
        }
    }

    /// <summary>
    /// 判断自动补牌是否需要记录普通成功日志；手牌哨兵的热路径做节流，手动按钮和异常仍保持详细。
    /// </summary>
    private bool ShouldLogAutoDraw(string actionName)
    {
        if (!string.Equals(actionName, "手牌减少触发补牌", StringComparison.Ordinal))
        {
            return true;
        }

        DateTime now = DateTime.UtcNow;
        if (now - _lastAutoDrawLogAt < AutoDrawLogInterval)
        {
            return false;
        }

        _lastAutoDrawLogAt = now;
        return true;
    }

    /// <summary>
    /// 执行只有控制台命令的操作，并转换成 UI 提示文本。
    /// </summary>
    private async Task<string> RunConsoleOnlyActionAsync(string actionName, AbstractConsoleCmd consoleCmd, string[] args)
    {
        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            return GmUiText.ChronicleNotInRun;
        }

        return await RunConsoleCommandAsync(actionName, consoleCmd, args)
            ? GmUiText.ChronicleApplySuccess
            : GmUiText.ChronicleApplyFailed;
    }

    /// <summary>
    /// 多人模式下把岁月史书允许开放的数值项转换成原生控制台网络命令。
    /// </summary>
    private async Task<string> ApplyValueByNetworkCommandAsync(ChronicleValueKey key, int value)
    {
        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            return GmUiText.ChronicleNotInRun;
        }

        return key switch
        {
            ChronicleValueKey.CurrentHp => await TrySubmitPositiveDeltaCommandAsync("治疗", "heal", value, player.Creature.CurrentHp, requireCombat: false),
            ChronicleValueKey.Gold => await TrySubmitDeltaCommandAsync("调整金币", "gold", value, player.Gold, requireCombat: false, allowNegative: true),
            ChronicleValueKey.Energy => await TrySubmitCombatResourceDeltaCommandAsync("增加能量", "energy", value, player.PlayerCombatState?.Energy),
            ChronicleValueKey.Stars => await TrySubmitCombatResourceDeltaCommandAsync("增加星星", "stars", value, player.PlayerCombatState?.Stars),
            _ => BlockChronicleMultiplayerSingleplayerOnly(key),
        };
    }

    /// <summary>
    /// 多人模式下提交战斗资源增量命令，资源状态为空时直接返回需要战斗提示。
    /// </summary>
    private async Task<string> TrySubmitCombatResourceDeltaCommandAsync(string actionName, string commandName, int targetValue, int? currentValue)
    {
        if (!currentValue.HasValue)
        {
            return GmUiText.ChronicleNeedCombat;
        }

        return await TrySubmitPositiveDeltaCommandAsync(actionName, commandName, targetValue, currentValue.Value, requireCombat: true);
    }

    /// <summary>
    /// 多人模式下只允许把目标值转换成正数增量，不做降值或本地校准。
    /// </summary>
    private async Task<string> TrySubmitPositiveDeltaCommandAsync(string actionName, string commandName, int targetValue, int currentValue, bool requireCombat)
    {
        return await TrySubmitDeltaCommandAsync(actionName, commandName, targetValue, currentValue, requireCombat, allowNegative: false);
    }

    /// <summary>
    /// 多人模式下把目标值转换为控制台命令增量；只在命令本身允许时才提交负数。
    /// </summary>
    private async Task<string> TrySubmitDeltaCommandAsync(string actionName, string commandName, int targetValue, int currentValue, bool requireCombat, bool allowNegative)
    {
        int delta = targetValue - currentValue;
        if (delta == 0)
        {
            return GmUiText.ChronicleNothingToDo;
        }

        if (delta < 0 && !allowNegative)
        {
            MainFile.Logger.Info($"岁月史书多人命令未提交：动作={actionName}，命令={commandName}，当前值={currentValue}，输入值={targetValue}，原因=该原生命令不支持负数。");
            return GmUiText.ChronicleMultiplayerPositiveDeltaOnly;
        }

        return await TrySubmitChronicleNetworkCommandAsync(actionName, $"{commandName} {delta}", requireCombat);
    }

    /// <summary>
    /// 多人模式下提交岁月史书原生控制台网络命令，不在本机提前执行命令对象。
    /// </summary>
    private async Task<string> TrySubmitChronicleNetworkCommandAsync(string actionName, string command, bool requireCombat)
    {
        return (await TrySubmitChronicleNetworkCommandResultAsync(actionName, command, requireCombat)).Message;
    }

    /// <summary>
    /// 多人模式下提交岁月史书原生控制台网络命令，并返回提交结果和 UI 文案。
    /// </summary>
    private async Task<(bool Submitted, string Message)> TrySubmitChronicleNetworkCommandResultAsync(string actionName, string command, bool requireCombat)
    {
        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            return (false, GmUiText.ChronicleNotInRun);
        }

        GmFeatureAvailability availability = GmRunContextService.GetNetworkConsoleCommandAvailability(requireCombat, GmUiText.ChronicleNeedCombat);
        if (!availability.IsAllowed)
        {
            MainFile.Logger.Info($"岁月史书多人命令被阻止：动作={actionName}，命令={command}，原因={availability.Reason}。");
            return (false, availability.Reason);
        }

        bool submitted = await GmNetworkCommandService.TryEnqueueConsoleCommandAsync(player, command, $"岁月史书{actionName}");
        MainFile.Logger.Info($"岁月史书多人命令提交结束：动作={actionName}，命令={command}，提交成功={submitted}。");
        return submitted ? (true, GmUiText.ChronicleSubmitSuccess) : (false, GmUiText.ChronicleApplyFailed);
    }

    /// <summary>
    /// 多人模式下按顺序提交一组控制台网络命令；只在提交前检查一次队列，适合 godmode 这种必须成对执行的切换命令。
    /// </summary>
    private async Task<(bool Submitted, string Message)> TrySubmitChronicleNetworkCommandsResultAsync(string actionName, IReadOnlyList<string> commands, bool requireCombat)
    {
        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            return (false, GmUiText.ChronicleNotInRun);
        }

        if (commands.Count == 0)
        {
            return (false, GmUiText.ChronicleNothingToDo);
        }

        GmFeatureAvailability availability = GmRunContextService.GetNetworkConsoleCommandAvailability(requireCombat, GmUiText.ChronicleNeedCombat);
        if (!availability.IsAllowed)
        {
            MainFile.Logger.Info($"岁月史书多人批量命令被阻止：动作={actionName}，命令=[{string.Join("; ", commands)}]，原因={availability.Reason}。");
            return (false, availability.Reason);
        }

        foreach (string command in commands)
        {
            bool submitted = await GmNetworkCommandService.TryEnqueueConsoleCommandAsync(player, command, $"岁月史书{actionName}");
            MainFile.Logger.Info($"岁月史书多人批量命令提交：动作={actionName}，命令={command}，提交成功={submitted}。");
            if (!submitted)
            {
                return (false, GmUiText.ChronicleApplyFailed);
            }
        }

        return (true, GmUiText.ChronicleSubmitSuccess);
    }

    /// <summary>
    /// 多人模式下拦截仍只能单人使用的岁月史书数值项。
    /// </summary>
    private static string BlockChronicleMultiplayerSingleplayerOnly(ChronicleValueKey key)
    {
        MainFile.Logger.Info($"岁月史书多人模式数值项已阻止：key={key}，原因=无法通过游戏内置网络命令安全同步。");
        return GmUiText.ChronicleMultiplayerCommandOnlyDisabled;
    }

    /// <summary>
    /// 统一执行游戏自带控制台命令，并等待命令可能返回的异步任务。
    /// </summary>
    private async Task<bool> RunConsoleCommandAsync(string actionName, AbstractConsoleCmd consoleCmd, string[] args, bool logCommand = true)
    {
        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            return false;
        }

        try
        {
            if (logCommand)
            {
                MainFile.Logger.Info($"岁月史书执行控制台命令：动作={actionName}，命令={consoleCmd.CmdName}，参数=[{string.Join(", ", args)}]。");
            }
            CmdResult result = consoleCmd.Process(player, args);
            if (result.task != null)
            {
                await result.task;
            }

            if (logCommand || !result.success)
            {
                MainFile.Logger.Info($"岁月史书控制台命令结束：动作={actionName}，命令={consoleCmd.CmdName}，success={result.success}，msg={result.msg}。");
            }
            return result.success;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书控制台命令失败：动作={actionName}，命令={consoleCmd.CmdName}，异常={ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 让全场敌人显示击晕意图；不替换动作对象，避免破坏怪物状态机。
    /// </summary>
    private void TryKeepEnemiesStunned()
    {
        CombatManager? manager = CombatManager.Instance;
        if (manager == null || !manager.IsInProgress || manager.IsOverOrEnding)
        {
            return;
        }

        try
        {
            Type? stunIntentType = ResolveIntentTypeByName("StunIntent");
            if (stunIntentType == null)
            {
                LogStunIntentAppliedCount(-2, "找不到击晕意图类型");
                return;
            }

            int appliedCount = 0;
            foreach (Creature enemy in GetAliveEnemies())
            {
                if (enemy.Monster?.NextMove == null || IsCurrentIntent(enemy, stunIntentType))
                {
                    continue;
                }

                AbstractIntent intent = (AbstractIntent)Activator.CreateInstance(stunIntentType)!;
                if (TrySetMoveIntents(enemy.Monster.NextMove, intent))
                {
                    appliedCount++;
                }
            }

            LogStunIntentAppliedCount(appliedCount, $"本次改写敌人数={appliedCount}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书总是玩家回合改写敌人击晕失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 移除明显负面的 Power，第一版按类型名做保守判断。
    /// </summary>
    private static void RemoveNegativePowers(Player player)
    {
        Creature creature = player.Creature;
        foreach (var power in creature.Powers.ToList())
        {
            string name = power.GetType().Name;
            if (!IsNegativePowerName(name))
            {
                continue;
            }

            creature.RemovePowerInternal(power);
        }
    }

    /// <summary>
    /// 判断 Power 类型名是否明显属于负面效果。
    /// </summary>
    private static bool IsNegativePowerName(string name)
    {
        return name.Contains("Weak", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Vulnerable", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Frail", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Poison", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("NoDraw", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("NoEnergy", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Shackle", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Entropy", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 多人模式下拦截岁月史书功能，避免直接修改运行时状态导致同步崩溃或卡死。
    /// </summary>
    private static bool TryBlockChronicleInMultiplayer(out string disabledMessage)
    {
        if (!GmRunContextService.IsMultiplayer())
        {
            disabledMessage = string.Empty;
            return false;
        }

        disabledMessage = GmUiText.ChronicleMultiplayerCommandOnlyDisabled;
        MainFile.Logger.Info($"岁月史书多人安全闸门阻止非命令功能：原因={disabledMessage}。");
        return true;
    }

    /// <summary>
    /// 多人模式下关闭岁月史书非命令型状态，保留能通过内置网络命令同步的开关。
    /// 整段包 _isSuppressingSave 防止"自动归零"动作把磁盘上的单人配置覆盖成 0。
    /// </summary>
    private void DisableChronicleRuntimeStateForMultiplayer()
    {
        bool previousSuppress = _isSuppressingSave;
        _isSuppressingSave = true;
        try
        {
            bool hadState =
                _lockedValues.Count > 0 ||
                NegativePowerImmuneEnabled ||
                GodModeEnabled ||
                _alwaysPlayerTurnActive ||
                _anyMapNodeClickableActive ||
                PlayerDamagePercent != DefaultPercent ||
                EnemyDamagePercent != DefaultPercent ||
                RewardMultiplier != DefaultRewardMultiplier ||
                ShopPricePercent != DefaultPercent ||
                PotionEffectPercent != DefaultPercent ||
                HandLimitOverride != DefaultHandLimit;

            _lockedValues.Clear();
            NegativePowerImmuneEnabled = false;
            GodModeEnabled = false;
            _alwaysPlayerTurnActive = false;
            _anyMapNodeClickableActive = false;
            _drawPileEmptyLogged = false;
            _lastGodModeRefreshRoomKey = null;
            _isRefreshingGodModeForCombat = false;
            _lastStunIntentAppliedCount = -1;
            PlayerDamagePercent = DefaultPercent;
            EnemyDamagePercent = DefaultPercent;
            RewardMultiplier = DefaultRewardMultiplier;
            ShopPricePercent = DefaultPercent;
            PotionEffectPercent = DefaultPercent;
            HandLimitOverride = DefaultHandLimit;
            if (!AlwaysDrawToHandLimitEnabled)
            {
                ResetHandWatcherState();
            }

            if (hadState)
            {
                MainFile.Logger.Info("岁月史书检测到多人模式，已关闭非命令型状态和全局倍率；命令型开关仍通过官方网络命令执行。");
            }
        }
        finally
        {
            _isSuppressingSave = previousSuppress;
        }
    }

    /// <summary>
    /// 获取当前玩家；没有局内状态时返回空。
    /// </summary>
    private static Player? GetCurrentPlayer()
    {
        return GmRunContextService.GetCurrentPlayer();
    }

    private static List<Creature> GetAliveEnemies()
    {
        CombatState? state = CombatManager.Instance?.DebugOnlyGetState();
        return state?.Enemies.Where(enemy => enemy.IsAlive).ToList() ?? [];
    }

    private static string GetEnemyId(Creature enemy)
    {
        return enemy.CombatId?.ToString() ?? enemy.GetHashCode().ToString();
    }

    private static ChronicleIntentOption? CreateIntentOption(Type intentType)
    {
        try
        {
            AbstractIntent intent = (AbstractIntent)Activator.CreateInstance(intentType)!;
            string name = GetLocalizedText(ReadIntentTitle(intent)) ?? intentType.Name.Replace("Intent", "", StringComparison.OrdinalIgnoreCase);
            return new ChronicleIntentOption(intentType.AssemblyQualifiedName ?? intentType.FullName ?? intentType.Name, SafeText(name, intentType.Name));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书读取意图失败：类型={intentType.FullName}，异常={ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 判断是否为游戏内部隐藏意图；隐藏意图没有本地化标题，读取会反复刷 BaseLib 警告。
    /// </summary>
    private static bool IsHiddenIntentType(Type intentType)
    {
        return intentType.Name.Contains("Hidden", StringComparison.OrdinalIgnoreCase) ||
               (intentType.FullName?.Contains("Hidden", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>
    /// 判断敌人当前意图是否已经是目标类型，避免定时器重复改写同一个敌人。
    /// </summary>
    private static bool IsCurrentIntent(Creature enemy, Type intentType)
    {
        return enemy.Monster?.NextMove?.Intents.Any(intent => intent.GetType() == intentType) == true;
    }

    /// <summary>
    /// 只改敌人原动作的意图显示；不替换动作对象，避免破坏怪物状态机。
    /// </summary>
    private static bool TrySetMoveIntents(MoveState move, AbstractIntent intent)
    {
        if (MoveStateIntentsField == null)
        {
            MainFile.Logger.Info("岁月史书总是玩家回合改写敌人击晕失败：找不到动作意图字段。");
            return false;
        }

        MoveStateIntentsField.SetValue(move, new AbstractIntent[] { intent });
        return true;
    }

    /// <summary>
    /// 按类型名解析意图类型，用于持续功能不依赖下拉框选项。
    /// </summary>
    private static Type? ResolveIntentTypeByName(string intentTypeName)
    {
        return typeof(AbstractIntent).Assembly.GetTypes()
            .FirstOrDefault(candidate => typeof(AbstractIntent).IsAssignableFrom(candidate) &&
                                         string.Equals(candidate.Name, intentTypeName, StringComparison.Ordinal));
    }

    /// <summary>
    /// 记录敌人击晕改写结果；只在数量变化时写日志，避免每 0.25 秒刷屏。
    /// </summary>
    private void LogStunIntentAppliedCount(int appliedCount, string detail)
    {
        if (_lastStunIntentAppliedCount == appliedCount)
        {
            return;
        }

        _lastStunIntentAppliedCount = appliedCount;
        MainFile.Logger.Info($"岁月史书总是玩家回合改写敌人击晕：{detail}。");
    }

    private static Type? ResolveIntentType(string intentId)
    {
        Type? type = Type.GetType(intentId, throwOnError: false);
        if (type != null && typeof(AbstractIntent).IsAssignableFrom(type))
        {
            return type;
        }

        return typeof(AbstractIntent).Assembly.GetTypes()
            .FirstOrDefault(candidate => typeof(AbstractIntent).IsAssignableFrom(candidate) &&
                                         string.Equals(candidate.FullName, intentId, StringComparison.Ordinal));
    }

    private static LocString? ReadIntentTitle(AbstractIntent intent)
    {
        PropertyInfo? property = typeof(AbstractIntent).GetProperty("IntentTitle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property?.GetValue(intent) as LocString;
    }

    private static string SafeText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string? GetLocalizedText(LocString? locString)
    {
        return GmLocalizationService.ResolveText(locString, "解析意图本地化文本失败");
    }

    /// <summary>
    /// 获取当前 RunState；没有局内状态时返回空。
    /// </summary>
    private static RunState? GetCurrentRunState()
    {
        return GmRunContextService.GetCurrentRunState();
    }
}
