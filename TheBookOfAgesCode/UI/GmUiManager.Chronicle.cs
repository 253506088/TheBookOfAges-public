using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.Services;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 全局调控（Chronicle）模块分文件：调控面板的创建、双列布局、值/动作/开关行的工厂方法、敌方意图选择、锁/解锁定时器，以及刷新和应用逻辑。
/// 内容来自 GmUiManager.cs 的 Chronicle 相关方法段，行为与原实现一致，只做物理拆分。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 创建岁月史书页签专属表单，默认隐藏，不影响遗物、药水和事件页签。
    /// </summary>
    private static Control BuildChroniclePanel()
    {
        _chronicleScroll = new ScrollContainer
        {
            Visible = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };

        _chronicleRows = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(ChronicleContentWidth, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };

        CenterContainer center = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        center.AddChild(_chronicleRows);
        _chronicleScroll.AddChild(center);

        BuildChronicleRows(_chronicleRows);
        BuildChronicleLockTimer(_chronicleScroll);
        BuildHandWatcherTimer(_chronicleScroll);
        return _chronicleScroll;
    }

    /// <summary>
    /// 创建岁月史书所有功能行，左列放开关，右列放输入和按钮类复杂操作。
    /// </summary>
    private static void BuildChronicleRows(VBoxContainer parent)
    {
        HBoxContainer columns = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        columns.AddThemeConstantOverride("separation", 28);
        VBoxContainer leftColumn = CreateChronicleColumn(360f);
        VBoxContainer rightColumn = CreateChronicleColumn(800f);
        columns.AddChild(leftColumn);
        columns.AddChild(rightColumn);
        parent.AddChild(columns);

        leftColumn.AddChild(CreateChronicleSectionTitle(() => GmUiText.Pick("开关", "Toggles")));
        _alwaysKillAllEnemiesToggle = CreateChronicleAsyncToggleRow(leftColumn, () => GmUiText.Pick("消灭所有敌人", "Kill All Enemies"), async enabled => await ChronicleService.SetAlwaysKillAllEnemiesAsync(enabled));
        _godModeToggle = CreateChronicleAsyncToggleRow(leftColumn, () => GmUiText.Pick("上帝模式", "God Mode"), async enabled => await ChronicleService.SetGodModeAsync(enabled));
        RegisterItemTooltip(_godModeToggle, GetChronicleMultiplayerCommandOnlyTooltip);
        _infiniteEnergyToggle = CreateChronicleAsyncToggleRow(leftColumn, () => GmUiText.Pick("无限能量", "Infinite Energy"), async enabled => await ChronicleService.SetInfiniteEnergyAsync(enabled));
        _alwaysPlayerTurnToggle = CreateChronicleToggleRow(leftColumn, () => GmUiText.Pick("总是玩家回合", "Always Player Turn"), enabled => ChronicleService.SetAlwaysPlayerTurn(enabled));
        RegisterItemTooltip(_alwaysPlayerTurnToggle, GetChronicleMultiplayerCommandOnlyTooltip);
        _negativePowerImmuneToggle = CreateChronicleToggleRow(leftColumn, () => GmUiText.Pick("免疫所有负面效果", "Immune to Negative Powers"), enabled => ChronicleService.SetNegativePowerImmune(enabled));
        RegisterItemTooltip(_negativePowerImmuneToggle, GetChronicleMultiplayerCommandOnlyTooltip);
        _alwaysDrawToHandLimitToggle = CreateChronicleAsyncToggleRow(leftColumn, () => GmUiText.Pick("始终抽到上限", "Always Draw to Hand Limit"), async enabled =>
        {
            string message = await ChronicleService.SetAlwaysDrawToHandLimitAsync(enabled);
            UpdateHandWatcherTimerState();
            return message;
        });
        _anyMapNodeClickableToggle = CreateChronicleToggleRow(leftColumn, () => GmUiText.Pick("任意地图点可点击", "Any Map Node Clickable"), enabled => ChronicleService.SetAnyMapNodeClickable(enabled));
        RegisterItemTooltip(_anyMapNodeClickableToggle, GetChronicleMultiplayerCommandOnlyTooltip);

        rightColumn.AddChild(CreateChronicleSectionTitle(() => GmUiText.Pick("战斗操作", "Combat Actions")));
        rightColumn.AddChild(CreateEnemyIntentRow());
        rightColumn.AddChild(CreateChronicleNumberActionRow(() => GmUiText.Pick("抽牌", "Draw Cards"), "5", async value => await ChronicleService.DrawCardsAsync(value)));
        rightColumn.AddChild(CreateChronicleActionRow(() => GmUiText.Pick("抽牌到上限", "Draw to Hand Limit"), async () => await ChronicleService.DrawToHandLimitAsync()));

        rightColumn.AddChild(CreateChronicleSectionTitle(() => GmUiText.Pick("玩家数值", "Player Values")));
        rightColumn.AddChild(CreateChronicleValueRow(() => GmUiText.Pick("生命值", "HP"), ChronicleValueKey.CurrentHp));
        rightColumn.AddChild(CreateChronicleValueRow(() => GmUiText.Pick("最大生命值", "Max HP"), ChronicleValueKey.MaxHp));
        rightColumn.AddChild(CreateChronicleValueRow(() => GmUiText.Pick("钱袋", "Gold"), ChronicleValueKey.Gold));
        rightColumn.AddChild(CreateChronicleValueRow(() => GmUiText.Pick("能量", "Energy"), ChronicleValueKey.Energy));
        rightColumn.AddChild(CreateChronicleValueRow(() => GmUiText.Pick("最大能量值", "Max Energy"), ChronicleValueKey.MaxEnergy));
        rightColumn.AddChild(CreateChronicleValueRow(() => GmUiText.Pick("星星", "Stars"), ChronicleValueKey.Stars));
        rightColumn.AddChild(CreateChronicleValueRow(() => GmUiText.Pick("充能球栏位", "Orb Slots"), ChronicleValueKey.OrbSlots));
        rightColumn.AddChild(CreateChronicleValueRow(() => GmUiText.Pick("手牌上限", "Hand Limit"), ChronicleValueKey.HandLimit, tooltipProvider: () => GmUiText.ChronicleHandLimitTooltip));
        rightColumn.AddChild(CreateChronicleValueRow(() => GmUiText.Pick("每回合抽牌", "Cards Per Turn"), ChronicleValueKey.CardsPerTurn));
        rightColumn.AddChild(CreateChronicleValueRow(() => GmUiText.Pick("进阶模式等级", "Ascension Level"), ChronicleValueKey.AscensionLevel));

        rightColumn.AddChild(CreateChronicleSectionTitle(() => GmUiText.Pick("倍率数值", "Multipliers")));
        rightColumn.AddChild(CreateChronicleValueRow(() => GmUiText.Pick("敌人攻击倍率(%)", "Enemy Damage (%)"), ChronicleValueKey.EnemyDamagePercent));
        rightColumn.AddChild(CreateChronicleValueRow(() => GmUiText.Pick("玩家攻击倍率(%)", "Player Damage (%)"), ChronicleValueKey.PlayerDamagePercent));
        rightColumn.AddChild(CreateChronicleValueRow(() => GmUiText.Pick("奖励倍率", "Reward Multiplier"), ChronicleValueKey.RewardMultiplier));
        rightColumn.AddChild(CreateChronicleValueRow(() => GmUiText.Pick("商店价格倍率(%)", "Shop Price (%)"), ChronicleValueKey.ShopPricePercent));
        rightColumn.AddChild(CreateChronicleValueRow(() => GmUiText.Pick("药水效果倍率(%)", "Potion Effect (%)"), ChronicleValueKey.PotionEffectPercent));

        rightColumn.AddChild(CreateChronicleSectionTitle(() => GmUiText.Pick("地图跳转", "Map Jump")));
        rightColumn.AddChild(CreateChronicleActRow());

        rightColumn.AddChild(CreateChronicleSectionTitle(() => GmUiText.Pick("第一版暂不做", "Skipped in First Version")));
        rightColumn.AddChild(CreateChronicleActionRow(() => GmUiText.Pick("更改种子", "Change Seed"), () => Task.FromResult(GmUiText.ChroniclePendingFeature)));
        rightColumn.AddChild(CreateChronicleActionRow(() => GmUiText.Pick("编辑遗物计数模式", "Edit Relic Counters"), () => Task.FromResult(GmUiText.ChroniclePendingFeature)));

        rightColumn.AddChild(CreateChronicleSectionTitle(() => GmUiText.ChronicleDiagnosticsSectionTitle));
        rightColumn.AddChild(CreateChronicleActionRow(() => GmUiText.ChronicleThrowLogTestButton, RunChronicleLogThrowTestAsync));
    }

    /// <summary>
    /// 创建岁月史书列容器，用固定最小宽度保证左右两列布局稳定。
    /// </summary>
    private static VBoxContainer CreateChronicleColumn(float minWidth)
    {
        return new VBoxContainer
        {
            CustomMinimumSize = new Vector2(minWidth, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
    }

    /// <summary>
    /// 创建岁月史书分区标题。
    /// </summary>
    private static Label CreateChronicleSectionTitle(Func<string> textProvider)
    {
        Label label = new()
        {
            CustomMinimumSize = new Vector2(0, 42),
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeColorOverride("font_color", SectionTitleColor);
        BindLabelText(label, textProvider);
        return label;
    }

    /// <summary>
    /// 创建岁月史书数值行，包含当前值输入、锁定和应用按钮。
    /// </summary>
    private static Control CreateChronicleValueRow(Func<string> labelTextProvider, ChronicleValueKey key, bool isReadOnly = false, Func<string>? tooltipProvider = null)
    {
        HBoxContainer row = CreateChronicleBaseRow(labelTextProvider);
        LineEdit input = CreateChronicleInput("0");
        CheckBox lockBox = new()
        {
            CustomMinimumSize = new Vector2(104, 38),
            FocusMode = Control.FocusModeEnum.None,
        };
        ApplySecondaryActionButtonStyle(lockBox);
        BindButtonText(lockBox, () => GmUiText.ChronicleLockLabel);
        Button apply = CreateChronicleSmallButton(() => GmUiText.ChronicleApplyButton);

        ChronicleInputs[key] = input;
        ChronicleLocks[key] = lockBox;
        ChronicleApplyButtons[key] = apply;

        RegisterItemTooltip(lockBox, GetChronicleLockTooltip);
        RegisterItemTooltip(apply, () => GetChronicleValueApplyTooltip(key));
        lockBox.Toggled += locked => OnChronicleLockToggled(key, locked);
        apply.Pressed += async () => await OnChronicleValueApplyPressed(key);
        if (isReadOnly)
        {
            input.Editable = false;
            lockBox.Disabled = true;
            apply.Disabled = true;
            if (tooltipProvider != null)
            {
                row.MouseFilter = Control.MouseFilterEnum.Stop;
                foreach (Node child in row.GetChildren())
                {
                    if (child is Control childControl)
                    {
                        childControl.MouseFilter = Control.MouseFilterEnum.Ignore;
                    }
                }

                RegisterItemTooltip(row, tooltipProvider);
            }
        }

        row.AddChild(input);
        row.AddChild(lockBox);
        row.AddChild(apply);
        return row;
    }

    /// <summary>
    /// 创建岁月史书无输入操作行。
    /// </summary>
    private static Control CreateChronicleActionRow(Func<string> labelTextProvider, Func<Task<string>> action)
    {
        HBoxContainer row = CreateChronicleBaseRow(labelTextProvider);
        Button run = CreateChronicleSmallButton(() => GmUiText.ChronicleRunButton);
        run.Pressed += async () => await RunChronicleAction(action);
        row.AddChild(run);
        return row;
    }

    /// <summary>
    /// 创建岁月史书带数值输入的一次性操作行。
    /// </summary>
    private static Control CreateChronicleNumberActionRow(Func<string> labelTextProvider, string defaultValue, Func<int, Task<string>> action)
    {
        HBoxContainer row = CreateChronicleBaseRow(labelTextProvider);
        LineEdit input = CreateChronicleInput(defaultValue);
        Button run = CreateChronicleSmallButton(() => GmUiText.ChronicleRunButton);
        run.Pressed += async () =>
        {
            if (!int.TryParse(input.Text, out int value))
            {
                ShowToast(GmUiText.ChronicleInvalidNumber);
                return;
            }

            await RunChronicleAction(() => action(value));
        };
        row.AddChild(input);
        row.AddChild(run);
        return row;
    }

    /// <summary>
    /// 创建岁月史书开关行。
    /// </summary>
    private static CheckBox CreateChronicleToggleRow(VBoxContainer parent, Func<string> labelTextProvider, Func<bool, string> onToggle)
    {
        HBoxContainer row = CreateChronicleBaseRow(labelTextProvider);
        CheckBox toggle = new()
        {
            CustomMinimumSize = new Vector2(128, 38),
            FocusMode = Control.FocusModeEnum.None,
        };
        ApplySecondaryActionButtonStyle(toggle);
        BindButtonText(toggle, () => GmUiText.Pick("开启", "Enable"));
        toggle.Toggled += enabled => ShowToast(onToggle(enabled));
        row.AddChild(toggle);
        parent.AddChild(row);
        return toggle;
    }

    /// <summary>
    /// 创建岁月史书异步开关行，用于开启时需要立刻执行一次动作的功能。
    /// </summary>
    private static CheckBox CreateChronicleAsyncToggleRow(VBoxContainer parent, Func<string> labelTextProvider, Func<bool, Task<string>> onToggle)
    {
        HBoxContainer row = CreateChronicleBaseRow(labelTextProvider);
        CheckBox toggle = new()
        {
            CustomMinimumSize = new Vector2(128, 38),
            FocusMode = Control.FocusModeEnum.None,
        };
        ApplySecondaryActionButtonStyle(toggle);
        BindButtonText(toggle, () => GmUiText.Pick("开启", "Enable"));
        toggle.Toggled += async enabled =>
        {
            string message = await onToggle(enabled);
            ShowToast(message);
            RefreshChroniclePage();
        };
        row.AddChild(toggle);
        parent.AddChild(row);
        return toggle;
    }

    private static Control CreateEnemyIntentRow()
    {
        HBoxContainer row = CreateChronicleBaseRow(() => GmUiText.ChronicleEnemyIntentLabel);
        _enemyIntentTargetSelect = new OptionButton
        {
            CustomMinimumSize = new Vector2(180, 42),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        ApplySelectVisualStyle(_enemyIntentTargetSelect);
        _enemyIntentAllToggle = new CheckBox
        {
            CustomMinimumSize = new Vector2(92, 38),
            FocusMode = Control.FocusModeEnum.None,
        };
        ApplySecondaryActionButtonStyle(_enemyIntentAllToggle);
        BindButtonText(_enemyIntentAllToggle, () => GmUiText.ChronicleAllEnemiesLabel);
        _enemyIntentSelect = new OptionButton
        {
            CustomMinimumSize = new Vector2(180, 42),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        ApplySelectVisualStyle(_enemyIntentSelect);
        _enemyIntentApplyButton = CreateChronicleSmallButton(() => GmUiText.ChronicleApplyButton);
        RegisterItemTooltip(_enemyIntentApplyButton, GetChronicleMultiplayerCommandOnlyTooltip);
        _enemyIntentApplyButton.Pressed += async () => await RunChronicleAction(() => ChronicleService.ApplyEnemyIntentAsync(
            _enemyIntentAllToggle?.ButtonPressed == true,
            GetSelectedMetadata(_enemyIntentTargetSelect),
            GetSelectedMetadata(_enemyIntentSelect)));

        row.AddChild(_enemyIntentTargetSelect);
        row.AddChild(_enemyIntentAllToggle);
        row.AddChild(_enemyIntentSelect);
        row.AddChild(_enemyIntentApplyButton);
        return row;
    }

    /// <summary>
    /// 创建阶段跳转行。
    /// </summary>
    private static Control CreateChronicleActRow()
    {
        HBoxContainer row = CreateChronicleBaseRow(() => GmUiText.Pick("前往阶段", "Go to Act"));
        _chronicleActSelect = CreateEventActSelect();
        Button run = CreateChronicleSmallButton(() => GmUiText.EventGoButton);
        run.Pressed += async () =>
        {
            if (_chronicleActSelect == null)
            {
                return;
            }

            await RunChronicleAction(() => ChronicleService.JumpToActAsync(GetSelectedActCommandArg(_chronicleActSelect)));
        };
        row.AddChild(_chronicleActSelect);
        row.AddChild(run);
        return row;
    }

    /// <summary>
    /// 创建岁月史书通用行，左侧固定标签，右侧放控件。
    /// </summary>
    private static HBoxContainer CreateChronicleBaseRow(Func<string> labelTextProvider)
    {
        HBoxContainer row = new()
        {
            CustomMinimumSize = new Vector2(0, 48),
        };
        Label label = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(230, 0),
        };
        label.AddThemeColorOverride("font_color", FieldLabelColor);
        BindLabelText(label, labelTextProvider);
        row.AddChild(label);
        return row;
    }

    /// <summary>
    /// 创建岁月史书输入框。
    /// </summary>
    private static LineEdit CreateChronicleInput(string defaultValue)
    {
        LineEdit input = new()
        {
            Text = defaultValue,
            CustomMinimumSize = new Vector2(120, 38),
            SelectAllOnFocus = true,
        };
        input.AddThemeColorOverride("font_color", FieldValueColor);
        input.AddThemeStyleboxOverride("normal", CreateRoundedStyleBox(new Color(0.07f, 0.09f, 0.12f, 0.90f), new Color(0.30f, 0.35f, 0.42f, 0.92f), 8, 1, 10, 8));
        input.AddThemeStyleboxOverride("focus", CreateRoundedStyleBox(new Color(0.11f, 0.16f, 0.21f, 0.94f), new Color(0.56f, 0.67f, 0.78f, 0.96f), 8, 1, 10, 8));
        input.TextChanged += text => SanitizeChronicleIntegerInput(input, text);
        return input;
    }

    /// <summary>
    /// 过滤岁月史书整数输入框内容，只保留数字和最前面的负号。
    /// </summary>
    private static void SanitizeChronicleIntegerInput(LineEdit input, string text)
    {
        string sanitized = BuildIntegerText(text);
        if (string.Equals(text, sanitized, StringComparison.Ordinal))
        {
            return;
        }

        input.Text = sanitized;
        input.CaretColumn = sanitized.Length;
    }

    /// <summary>
    /// 从原始输入中构造合法整数文本；允许输入中间态的空文本和单独负号。
    /// </summary>
    private static string BuildIntegerText(string text)
    {
        List<char> chars = [];
        bool hasMinus = false;
        foreach (char ch in text)
        {
            if (ch >= '0' && ch <= '9')
            {
                chars.Add(ch);
                continue;
            }

            if (ch == '-' && chars.Count == 0 && !hasMinus)
            {
                chars.Add(ch);
                hasMinus = true;
            }
        }

        return new string(chars.ToArray());
    }

    /// <summary>
    /// 创建岁月史书小按钮。
    /// </summary>
    private static Button CreateChronicleSmallButton(Func<string> textProvider)
    {
        Button button = new()
        {
            CustomMinimumSize = new Vector2(118, 38),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        ApplyPrimaryActionButtonStyle(button);
        BindButtonText(button, textProvider);
        return button;
    }

    /// <summary>
    /// 执行岁月史书日志抛出检测：故意制造异常后立刻捕获，并输出 Error 日志触发诊断导出。
    /// </summary>
    private static Task<string> RunChronicleLogThrowTestAsync()
    {
        try
        {
            ThrowChronicleDiagnosticTestException();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"岁月史书日志抛出检测触发：这是玩家手动点击测试按钮生成的诊断异常，不代表游戏真实崩溃。异常类型={ex.GetType().FullName}，异常信息={ex.Message}，堆栈={ex.StackTrace}。");
            return Task.FromResult(GmUiText.ChronicleThrowLogTestSubmitted);
        }

        return Task.FromResult(GmUiText.ChronicleThrowLogTestFailed);
    }

    /// <summary>
    /// 故意抛出一个测试异常，用来验证诊断导出链路是否能捕获岁月史书输出的 Error 日志。
    /// </summary>
    private static void ThrowChronicleDiagnosticTestException()
    {
        throw new InvalidOperationException("岁月史书日志抛出检测测试异常。");
    }

    /// <summary>
    /// 创建锁定回填定时器，页面关闭后也继续生效。
    /// 间隔 1.5 秒：原 0.25 秒太激进，会让"始终抽到上限"等开关每 0.25 秒触发一次抽牌动画造成卡顿。
    /// 对锁定回填、能量回填、敌人击晕、负面清理这些场景，1.5 秒响应足够及时。
    /// </summary>
    private static void BuildChronicleLockTimer(Node parent)
    {
        _chronicleLockTimer = new Godot.Timer
        {
            WaitTime = 1.5,
            OneShot = false,
            Autostart = true,
        };
        _chronicleLockTimer.Timeout += OnChronicleLockTimerTimeout;
        parent.AddChild(_chronicleLockTimer);
    }

    /// <summary>
    /// 创建手牌哨兵定时器；只在"始终抽到上限"开启时启动，避免未启用功能时后台空转。
    /// </summary>
    private static void BuildHandWatcherTimer(Node parent)
    {
        _handWatcherTimer = new Godot.Timer
        {
            WaitTime = 0.18,
            OneShot = false,
            Autostart = false,
        };
        _handWatcherTimer.Timeout += OnHandWatcherTimerTimeout;
        parent.AddChild(_handWatcherTimer);
    }

    /// <summary>
    /// 手牌哨兵定时器回调：仅调 ChronicleService.TryDrawIfHandShrunkAsync。
    /// 服务侧已做"开关未启用 / 战斗外 / 手牌没减少 / 已经在补牌"全部早退，回调本身不做任何判断，避免重复。
    /// </summary>
    private static async void OnHandWatcherTimerTimeout()
    {
        if (_isHandWatcherTickRunning)
        {
            return;
        }

        _isHandWatcherTickRunning = true;
        try
        {
            await ChronicleService.TryDrawIfHandShrunkAsync();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书手牌哨兵定时器异常：异常类型={ex.GetType().FullName}，异常信息={ex.Message}。");
        }
        finally
        {
            _isHandWatcherTickRunning = false;
        }
    }

    /// <summary>
    /// 根据"始终抽到上限"开关状态启动或停止手牌哨兵，关闭时清理观测基线。
    /// </summary>
    private static void UpdateHandWatcherTimerState()
    {
        if (_handWatcherTimer == null)
        {
            return;
        }

        if (ChronicleService.AlwaysDrawToHandLimitEnabled)
        {
            if (_handWatcherTimer.IsStopped())
            {
                _handWatcherTimer.Start();
            }
            return;
        }

        _handWatcherTimer.Stop();
        ChronicleService.ResetHandWatcherState();
    }

    /// <summary>
    /// 刷新岁月史书页面当前值和锁定勾选状态。
    /// </summary>
    private static void RefreshChroniclePage()
    {
        bool canLockChronicleValues = !GmRunContextService.IsMultiplayer() && GmRunContextService.GetFeatureAvailability(GmFeature.Chronicle).IsAllowed;
        foreach ((ChronicleValueKey key, LineEdit input) in ChronicleInputs)
        {
            int? lockedValue = ChronicleService.GetLockedValue(key);
            int? currentValue = lockedValue ?? ChronicleService.GetCurrentValue(key);
            if (currentValue.HasValue && !input.HasFocus())
            {
                input.Text = currentValue.Value.ToString();
            }
        }

        foreach ((ChronicleValueKey key, Button applyButton) in ChronicleApplyButtons)
        {
            if (ChronicleInputs.TryGetValue(key, out LineEdit? input))
            {
                RefreshChronicleValueControl(key, input, applyButton);
            }
        }

        foreach ((ChronicleValueKey key, CheckBox checkBox) in ChronicleLocks)
        {
            RefreshChronicleLockControl(key, checkBox, canLockChronicleValues);
        }

        _alwaysKillAllEnemiesToggle?.SetPressedNoSignal(ChronicleService.AlwaysKillAllEnemiesEnabled);
        _godModeToggle?.SetPressedNoSignal(ChronicleService.GodModeEnabled);
        _infiniteEnergyToggle?.SetPressedNoSignal(ChronicleService.InfiniteEnergyEnabled);
        _alwaysPlayerTurnToggle?.SetPressedNoSignal(ChronicleService.AlwaysPlayerTurnEnabled);
        _alwaysDrawToHandLimitToggle?.SetPressedNoSignal(ChronicleService.AlwaysDrawToHandLimitEnabled);
        UpdateHandWatcherTimerState();
        _negativePowerImmuneToggle?.SetPressedNoSignal(ChronicleService.NegativePowerImmuneEnabled);
        _anyMapNodeClickableToggle?.SetPressedNoSignal(ChronicleService.AnyMapNodeClickableEnabled);
        RefreshChronicleMultiplayerControlAvailability();
        RefreshEnemyIntentControls();
        SetStatusText(GmRunContextService.IsMultiplayer() ? GmUiText.ChronicleMultiplayerCommandModeStatus : string.Empty);
    }

    /// <summary>
    /// 刷新岁月史书数值行；多人模式下只开放能走内置网络命令的数值。
    /// </summary>
    private static void RefreshChronicleValueControl(ChronicleValueKey key, LineEdit input, Button applyButton)
    {
        bool allowed = !GmRunContextService.IsMultiplayer() || ChronicleService.IsMultiplayerCommandValue(key);
        input.Editable = allowed;
        applyButton.Disabled = !allowed;
    }

    /// <summary>
    /// 刷新岁月史书多人模式下的非命令控件置灰状态。
    /// </summary>
    private static void RefreshChronicleMultiplayerControlAvailability()
    {
        bool commandOnlyMode = GmRunContextService.IsMultiplayer();
        SetDisabled(_godModeToggle, commandOnlyMode);
        SetDisabled(_alwaysPlayerTurnToggle, commandOnlyMode);
        SetDisabled(_negativePowerImmuneToggle, commandOnlyMode);
        SetDisabled(_anyMapNodeClickableToggle, commandOnlyMode);
        SetDisabled(_enemyIntentTargetSelect, commandOnlyMode);
        SetDisabled(_enemyIntentAllToggle, commandOnlyMode);
        SetDisabled(_enemyIntentSelect, commandOnlyMode);
        SetDisabled(_enemyIntentApplyButton, commandOnlyMode);
    }

    /// <summary>
    /// 安全设置控件禁用状态，避免空控件打断页面刷新。
    /// </summary>
    private static void SetDisabled(Control? control, bool disabled)
    {
        if (control is BaseButton button)
        {
            button.Disabled = disabled;
        }
    }

    /// <summary>
    /// 刷新单个岁月史书锁定框；多人模式下只禁用锁定，不影响旁边的应用按钮展示自身提示。
    /// </summary>
    private static void RefreshChronicleLockControl(ChronicleValueKey key, CheckBox checkBox, bool canLockChronicleValues)
    {
        checkBox.Disabled = !canLockChronicleValues;
        checkBox.SetPressedNoSignal(canLockChronicleValues && ChronicleService.IsLocked(key));
    }

    /// <summary>
    /// 获取岁月史书锁定框的悬浮提示；单人可用时不显示额外提示。
    /// </summary>
    private static string GetChronicleLockTooltip()
    {
        return GmRunContextService.GetFeatureAvailability(GmFeature.Chronicle).IsAllowed
            ? string.Empty
            : GmUiText.ChronicleLockMultiplayerDisabledTooltip;
    }

    /// <summary>
    /// 获取多人模式下非命令岁月史书功能的悬浮提示。
    /// </summary>
    private static string GetChronicleMultiplayerCommandOnlyTooltip()
    {
        return GmRunContextService.IsMultiplayer()
            ? GmUiText.ChronicleMultiplayerCommandOnlyDisabled
            : string.Empty;
    }

    /// <summary>
    /// 获取岁月史书数值应用按钮的悬浮提示；多人模式下说明非命令项不可用。
    /// </summary>
    private static string GetChronicleValueApplyTooltip(ChronicleValueKey key)
    {
        return GmRunContextService.IsMultiplayer() && !ChronicleService.IsMultiplayerCommandValue(key)
            ? GmUiText.ChronicleMultiplayerCommandOnlyDisabled
            : string.Empty;
    }

    private static void RefreshEnemyIntentControls()
    {
        RefreshEnemyIntentTargetSelect();
        RefreshEnemyIntentSelect();
    }

    /// <summary>
    /// 刷新敌人下拉框；差量更新：基于敌人 Id+Name 拼成签名，与上次相同就直接返回，
    /// 避免定时器每 1.5 秒就 Clear+AddItem 重建一次，造成下拉闪烁、选中重置与无意义分配。
    /// 敌人列表真正变化（进入/离开战斗、敌人死亡、本地化切换）时签名才会变，正确触发重建。
    /// </summary>
    private static void RefreshEnemyIntentTargetSelect()
    {
        if (_enemyIntentTargetSelect == null)
        {
            return;
        }

        IReadOnlyList<ChronicleEnemyOption> options = ChronicleService.GetEnemyOptions();
        string signature = BuildEnemyIntentTargetSignature(options);
        if (string.Equals(signature, _lastEnemyIntentTargetSignature, StringComparison.Ordinal))
        {
            return;
        }

        string? selected = GetSelectedMetadata(_enemyIntentTargetSelect);
        _enemyIntentTargetSelect.Clear();
        foreach (ChronicleEnemyOption enemy in options)
        {
            int index = _enemyIntentTargetSelect.ItemCount;
            _enemyIntentTargetSelect.AddItem(enemy.Name);
            _enemyIntentTargetSelect.SetItemMetadata(index, enemy.Id);
            if (string.Equals(enemy.Id, selected, StringComparison.Ordinal))
            {
                _enemyIntentTargetSelect.Select(index);
            }
        }

        if (_enemyIntentTargetSelect.ItemCount == 0)
        {
            _enemyIntentTargetSelect.AddItem(GmUiText.ChronicleNoEnemyOption);
        }

        _lastEnemyIntentTargetSignature = signature;
    }

    /// <summary>
    /// 把当前敌人下拉项拼成稳定签名，用于和上次刷新的状态做差量比较。
    /// 用 Id 和 Name 双重拼接，本地化切换造成 Name 变化时也能触发重建。
    /// </summary>
    private static string BuildEnemyIntentTargetSignature(IReadOnlyList<ChronicleEnemyOption> options)
    {
        if (options.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new(options.Count * 16);
        foreach (ChronicleEnemyOption enemy in options)
        {
            builder.Append(enemy.Id).Append('|').Append(enemy.Name).Append(';');
        }

        return builder.ToString();
    }

    private static void RefreshEnemyIntentSelect()
    {
        if (_enemyIntentSelect == null)
        {
            return;
        }

        if (_enemyIntentSelectInitialized)
        {
            return;
        }

        string? selected = GetSelectedMetadata(_enemyIntentSelect);
        _enemyIntentSelect.Clear();
        foreach (ChronicleIntentOption intent in ChronicleService.GetIntentOptions())
        {
            int index = _enemyIntentSelect.ItemCount;
            _enemyIntentSelect.AddItem(intent.Name);
            _enemyIntentSelect.SetItemMetadata(index, intent.Id);
            if (string.Equals(intent.Id, selected, StringComparison.Ordinal))
            {
                _enemyIntentSelect.Select(index);
            }
        }

        if (_enemyIntentSelect.ItemCount == 0)
        {
            _enemyIntentSelect.AddItem(GmUiText.ChronicleNoIntentOption);
        }

        _enemyIntentSelectInitialized = true;
    }

    /// <summary>
    /// 处理岁月史书数值应用按钮。
    /// </summary>
    private static async Task OnChronicleValueApplyPressed(ChronicleValueKey key)
    {
        if (!TryReadChronicleValue(key, out int value))
        {
            ShowToast(GmUiText.ChronicleInvalidNumber);
            return;
        }

        string message = await ChronicleService.ApplyValueAsync(key, value);
        ShowToast(message);
        if (string.Equals(message, GmUiText.ChronicleSubmitSuccess, StringComparison.Ordinal))
        {
            _ = RefreshAfterNetworkSubmitAsync(GmPageMode.Chronicle);
        }

        RefreshChroniclePage();
    }

    /// <summary>
    /// 处理岁月史书锁定勾选；锁定后关闭 GM 页面也继续生效。
    /// </summary>
    private static void OnChronicleLockToggled(ChronicleValueKey key, bool locked)
    {
        GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(GmFeature.Chronicle);
        if (!availability.IsAllowed)
        {
            ShowToast(GmUiText.ChronicleLockMultiplayerDisabledTooltip);
            ChronicleLocks[key].SetPressedNoSignal(false);
            MainFile.Logger.Info($"岁月史书锁定勾选已被多人安全闸门阻止：key={key}，原因={availability.Reason}。");
            return;
        }

        if (!TryReadChronicleValue(key, out int value))
        {
            ShowToast(GmUiText.ChronicleInvalidNumber);
            ChronicleLocks[key].SetPressedNoSignal(false);
            return;
        }

        ChronicleService.SetLocked(key, locked, value);
        ShowToast(locked ? GmUiText.ChronicleToggleOn : GmUiText.ChronicleToggleOff);
    }

    /// <summary>
    /// 读取岁月史书输入框里的整数。
    /// </summary>
    private static bool TryReadChronicleValue(ChronicleValueKey key, out int value)
    {
        value = 0;
        return ChronicleInputs.TryGetValue(key, out LineEdit? input) && int.TryParse(input.Text, out value);
    }

    /// <summary>
    /// 执行岁月史书按钮动作并展示结果。
    /// </summary>
    private static async Task RunChronicleAction(Func<Task<string>> action)
    {
        string message = await action();
        ShowToast(message);
        if (string.Equals(message, GmUiText.ChronicleSubmitSuccess, StringComparison.Ordinal))
        {
            _ = RefreshAfterNetworkSubmitAsync(GmPageMode.Chronicle);
        }

        RefreshChroniclePage();
    }

    /// <summary>
    /// 锁定定时器回调，持续回填岁月史书锁定值。
    /// 性能优化：先用 HasOngoingChronicleWork 快速门判断当前是否有锁定值或持续开关启用，
    /// 都没有时直接跳过 ApplyLockedValuesAsync，避免每 1.5 秒空跑反射 + 控制台命令；
    /// 但岁月史书页可见时仍照常 RefreshChroniclePage，让"当前值"显示保持实时。
    /// </summary>
    private static async void OnChronicleLockTimerTimeout()
    {
        try
        {
            if (!_isApplyingChronicleTick && ChronicleService.HasOngoingChronicleWork)
            {
                _isApplyingChronicleTick = true;
                try
                {
                    await ChronicleService.ApplyLockedValuesAsync();
                }
                finally
                {
                    _isApplyingChronicleTick = false;
                }
            }

            if (_currentPage == GmPageMode.Chronicle && _layer?.Visible == true)
            {
                RefreshChroniclePage();
            }
        }
        catch (Exception ex)
        {
            _isApplyingChronicleTick = false;
            MainFile.Logger.Info($"岁月史书锁定回填定时器异常：异常类型={ex.GetType().FullName}，异常信息={ex.Message}。");
        }
    }

    /// <summary>
    /// 根据当前页签显示或隐藏岁月史书专属表单。
    /// </summary>
    private static void UpdateChroniclePageVisibility()
    {
        bool isChroniclePage = _currentPage == GmPageMode.Chronicle;

        if (_searchInput != null)
        {
            _searchInput.Visible = !isChroniclePage;
        }

        if (_filtersContainer != null)
        {
            _filtersContainer.Visible = !isChroniclePage;
        }

        if (_toolbar != null)
        {
            _toolbar.CustomMinimumSize = isChroniclePage ? Vector2.Zero : new Vector2(0, 118);
            _toolbar.Visible = !isChroniclePage;
        }

        if (_gridScroll != null)
        {
            _gridScroll.Visible = !isChroniclePage;
        }

        if (_chronicleScroll != null)
        {
            _chronicleScroll.Visible = isChroniclePage;
        }

        if (isChroniclePage)
        {
            RefreshChroniclePage();
        }
    }
}
