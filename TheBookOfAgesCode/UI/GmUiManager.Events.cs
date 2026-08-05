using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.Services;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 事件模块分文件：事件按钮、阶段/宝箱/房间快速跳转控件，以及事件页可见性切换。
/// 内容来自 GmUiManager.cs 的事件相关方法段，行为与原实现一致，只做物理拆分。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 创建事件页签的快捷跳转区，阶段和房间都尽量走游戏自带控制台命令。
    /// </summary>
    private static Control BuildEventQuickActionsControl()
    {
        _eventQuickActionsControl = new VBoxContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(0, 156),
        };

        Label title = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(0, 30),
        };
        title.AddThemeColorOverride("font_color", SectionTitleColor);
        BindLabelText(title, () => GmUiText.EventQuickJumpTitle);
        _eventQuickActionsControl.AddChild(title);

        _eventActSelect = CreateEventActSelect();
        _eventQuickActionsControl.AddChild(CreateEventQuickActionRow(() => GmUiText.EventActSelectLabel, _eventActSelect, OnEventActGoPressed));

        _eventTreasureSelect = CreateEventTreasureSelect();
        _eventQuickActionsControl.AddChild(CreateEventQuickActionRow(() => GmUiText.EventTreasureSelectLabel, _eventTreasureSelect, OnEventTreasureGoPressed));

        HBoxContainer roomButtons = new()
        {
            CustomMinimumSize = new Vector2(0, 44),
        };
        roomButtons.AddChild(CreateEventQuickActionButton(() => GmUiText.EventEnterShopButton, () => OnEventRoomGoPressed("Shop", GmUiText.EventEnterShopButton)));
        roomButtons.AddChild(CreateEventQuickActionButton(() => GmUiText.EventEnterRestSiteButton, () => OnEventRoomGoPressed("RestSite", GmUiText.EventEnterRestSiteButton)));
        _eventQuickActionsControl.AddChild(roomButtons);

        return _eventQuickActionsControl;
    }

    /// <summary>
    /// 创建阶段选择下拉框，参数使用 act 控制台命令支持的数字阶段。
    /// </summary>
    private static OptionButton CreateEventActSelect()
    {
        OptionButton optionButton = new()
        {
            CustomMinimumSize = new Vector2(250, 42),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        ApplySelectVisualStyle(optionButton);

        BindOptionItems(optionButton, () => [
            GmUiText.Pick("第一阶段", "Act 1"),
            GmUiText.Pick("第二阶段", "Act 2"),
            GmUiText.Pick("第三阶段", "Act 3"),
            GmUiText.Pick("终局", "Finale"),
        ], 3);
        return optionButton;
    }

    /// <summary>
    /// 创建宝箱选择下拉框；当前 STS2 原生 room 命令只支持进入 Treasure 房间。
    /// </summary>
    private static OptionButton CreateEventTreasureSelect()
    {
        OptionButton optionButton = new()
        {
            CustomMinimumSize = new Vector2(250, 42),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        ApplySelectVisualStyle(optionButton);
        BindTooltip(optionButton, () => GmUiText.Pick("当前使用 room Treasure，游戏原生命令暂未区分宝箱大小。", "Uses room Treasure. The native command does not separate chest sizes yet."));
        BindOptionItems(optionButton, () => [GmUiText.Pick("小宝箱", "Small Chest")]);
        return optionButton;
    }

    /// <summary>
    /// 创建带标签、下拉框和前往按钮的快捷跳转行。
    /// </summary>
    private static Control CreateEventQuickActionRow(Func<string> labelTextProvider, OptionButton optionButton, Action onGoPressed)
    {
        HBoxContainer row = new()
        {
            CustomMinimumSize = new Vector2(0, 44),
        };

        Label label = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(120, 0),
        };
        label.AddThemeColorOverride("font_color", FieldLabelColor);
        BindLabelText(label, labelTextProvider);
        row.AddChild(label);
        row.AddChild(optionButton);
        row.AddChild(CreateEventQuickActionButton(() => GmUiText.EventGoButton, onGoPressed));
        return row;
    }

    /// <summary>
    /// 创建事件页签快捷跳转按钮，统一按钮大小和点击样式。
    /// </summary>
    private static Button CreateEventQuickActionButton(Func<string> textProvider, Action onPressed)
    {
        Button button = new()
        {
            CustomMinimumSize = new Vector2(136, 40),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        ApplyPrimaryActionButtonStyle(button);
        BindButtonText(button, textProvider);
        button.Pressed += onPressed;
        return button;
    }

    /// <summary>
    /// 刷新事件名册页的网格内容，列出所有事件按钮。
    /// </summary>
    private static void RefreshEventGrid()
    {
        if (_grid == null || _searchInput == null || _categoryFilter == null || _sourceFilter == null)
        {
            return;
        }

        IReadOnlyList<EventDisplayInfo> events = EventService.GetAllEvents(
            _searchInput.Text,
            GetSelectedFilterKey(_categoryFilter),
            GetSelectedFilterKey(_sourceFilter));
        UpdateGridColumns();

        ClearGridChildren();

        foreach (EventDisplayInfo evt in events)
        {
            _grid.AddChild(CreateEventButton(evt));
        }

        // 事件页签不需要「已选 x 个」，只显示当前展示数量
        if (_statusLabel != null)
        {
            SetStatusText(GmUiText.Pick($"共 {events.Count} 个事件", $"{events.Count} events"));
        }
    }

    /// <summary>
    /// 创建单个事件按钮，显示事件名称和分类。
    /// 点击即触发事件，不走「选中→确认」两步流程。
    /// </summary>
    private static Button CreateEventButton(EventDisplayInfo evt)
    {
        Button button = new()
        {
            TooltipText = $"{evt.Name}\n{evt.Category}\n{evt.Source}\nID: {evt.IdText}",
            CustomMinimumSize = new Vector2(178, 108),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            ClipContents = true,
        };
        ApplyPrimaryActionButtonStyle(button);

        VBoxContainer layout = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        layout.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        Label nameLabel = new()
        {
            Text = evt.Name,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        nameLabel.AddThemeColorOverride("font_color", EventNameColor);
        layout.AddChild(nameLabel);

        Label categoryLabel = new()
        {
            Text = evt.Category,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        categoryLabel.AddThemeColorOverride("font_color", EventCategoryColor);
        layout.AddChild(categoryLabel);

        Label sourceLabel = new()
        {
            Text = evt.Source,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        sourceLabel.AddThemeColorOverride("font_color", EventSourceColor);
        layout.AddChild(sourceLabel);

        button.AddChild(layout);

        button.Pressed += () => OnEventButtonPressed(evt);
        return button;
    }

    /// <summary>
    /// 事件按钮点击处理：立即触发该事件并关闭 GM 页面。
    /// 不同于遗物/药水的批量选择模式，事件是单击即跳转。
    /// </summary>
    private static async void OnEventButtonPressed(EventDisplayInfo evt)
    {
        GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(GmFeature.TriggerEvent);
        if (!availability.IsAllowed)
        {
            ShowToast(availability.Reason);
            SetStatusText(availability.Reason);
            return;
        }

        bool success = await EventService.TriggerEventAsync(evt);
        if (success)
        {
            string messageTemplate = GmRunContextService.IsMultiplayer()
                ? GmUiText.EventTriggerSubmittedNamed
                : GmUiText.EventTriggerSuccessNamed;
            ShowToast(string.Format(messageTemplate, evt.Name));
            // 触发成功后关闭 GM 页面，让玩家直接进入事件
            SetPanelVisible(false);
        }
        else
        {
            MainFile.Logger.Info($"GM 事件按钮触发失败：事件名称={evt.Name}，事件ID={evt.IdText}，分类={evt.Category}，来源={evt.Source}。");
            // 触发失败时判断是否因为不在局内
            RunManager? manager = RunManager.Instance;
            if (manager == null || !manager.IsInProgress || manager.IsGameOver)
            {
                ShowToast(GmUiText.EventNotInRun);
            }
            else
            {
                ShowToast(string.Format(GmUiText.EventTriggerFailedNamed, evt.Name));
            }
        }
    }

    /// <summary>
    /// 处理阶段前往按钮：读取下拉框并调用 act 控制台命令。
    /// </summary>
    private static async void OnEventActGoPressed()
    {
        GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(GmFeature.ActTravel);
        if (!availability.IsAllowed)
        {
            ShowToast(availability.Reason);
            SetStatusText(availability.Reason);
            return;
        }

        if (_eventActSelect == null)
        {
            return;
        }

        string displayName = GetSelectedText(_eventActSelect);
        bool success = await EventService.JumpToActAsync(GetSelectedActCommandArg(_eventActSelect), displayName);
        HandleEventQuickJumpResult(success, displayName, isActJump: true);
    }

    /// <summary>
    /// 处理宝箱前往按钮：当前通过 room Treasure 进入宝箱房。
    /// </summary>
    private static void OnEventTreasureGoPressed()
    {
        string displayName = _eventTreasureSelect != null ? GetSelectedText(_eventTreasureSelect) : GmUiText.Pick("小宝箱", "Small Chest");
        OnEventRoomGoPressed("Treasure", displayName);
    }

    /// <summary>
    /// 处理房间快捷按钮：商店、营火、宝箱共用 room 控制台命令。
    /// </summary>
    private static async void OnEventRoomGoPressed(string roomCommandArg, string displayName)
    {
        GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(GmFeature.RoomTravel);
        if (!availability.IsAllowed)
        {
            ShowToast(availability.Reason);
            SetStatusText(availability.Reason);
            return;
        }

        bool success = await EventService.JumpToRoomAsync(roomCommandArg, displayName);
        HandleEventQuickJumpResult(success, displayName, isActJump: false);
    }

    /// <summary>
    /// 根据阶段下拉框选项返回 act 控制台命令参数。
    /// </summary>
    private static string GetSelectedActCommandArg(OptionButton optionButton)
    {
        return optionButton.Selected switch
        {
            0 => "1",
            1 => "2",
            2 => "3",
            _ => "4",
        };
    }

    /// <summary>
    /// 统一处理快捷跳转结果，成功后关闭 GM 页面让玩家直接看到目标房间。
    /// </summary>
    private static void HandleEventQuickJumpResult(bool success, string displayName, bool isActJump)
    {
        if (success)
        {
            string messageTemplate = isActJump ? GmUiText.EventActJumpSuccessNamed : GmUiText.EventRoomJumpSuccessNamed;
            ShowToast(string.Format(messageTemplate, displayName));
            SetPanelVisible(false);
            return;
        }

        RunManager? manager = RunManager.Instance;
        ShowToast(manager == null || !manager.IsInProgress || manager.IsGameOver
            ? GmUiText.EventNotInRun
            : string.Format(GmUiText.EventQuickJumpFailedNamed, displayName));
    }

    /// <summary>
    /// 根据当前页签显示或隐藏事件页签、刷怪笼页签和能力图鉴页签的专属控件。
    /// 事件页签不需要底部确认栏；事件、岁月史书、刷怪笼和能力图鉴都不需要稀有度筛选框。
    /// </summary>
    private static void UpdateEventPageVisibility()
    {
        bool isEventPage = _currentPage == GmPageMode.EventRegistry;
        bool isChroniclePage = _currentPage == GmPageMode.Chronicle;
        bool isMonsterPage = _currentPage == GmPageMode.MonsterCage;
        bool isPowerPage = _currentPage == GmPageMode.PowerCatalog;
        bool isOrbPage = _currentPage == GmPageMode.OrbBox;
        bool isBlightPage = _currentPage == GmPageMode.BlightCatalog;
        bool isCardPage = _currentPage == GmPageMode.GrantCards;
        bool isRemoveCardPage = _currentPage == GmPageMode.RemoveCards;
        bool isUpgradeCardPage = _currentPage == GmPageMode.UpgradeCards;
        bool isEnchantPage = _currentPage == GmPageMode.EnchantCards;
        bool isEnchantCardStep = isEnchantPage && _enchantmentPageStep == EnchantmentPageStep.SelectCards;

        // 事件页签隐藏底部确认栏，因为不走选中→确认流程
        if (_footerContainer != null)
        {
            _footerContainer.Visible = !isEventPage && !isChroniclePage;
        }

        // 删卡 / 强化页只用一个合并筛选，稀有度筛选隐藏；其他页签按原规则
        if (_rarityFilter != null)
        {
            _rarityFilter.Visible = !isEventPage && !isChroniclePage && !isMonsterPage && !isPowerPage && !isOrbPage && !isBlightPage && !isRemoveCardPage && !isUpgradeCardPage && !isEnchantPage;
        }

        // 能力图鉴使用分类筛选展示类型（增益/减益），不再隐藏；卡片相关页签都复用分类筛选
        if (_categoryFilter != null)
        {
            _categoryFilter.Visible = !isChroniclePage && !isOrbPage && (!isEnchantPage || isEnchantCardStep);
        }

        // 删卡 / 强化 / 附魔选卡步骤都不需要来源筛选（牌组里都是当前 run 的卡），同步隐藏
        if (_sourceFilter != null)
        {
            _sourceFilter.Visible = !isChroniclePage && !isRemoveCardPage && !isUpgradeCardPage && !isEnchantCardStep;
        }

        // 事件快捷跳转区只在事件页签显示，避免占用其他页签的筛选空间
        if (_eventQuickActionsControl != null)
        {
            _eventQuickActionsControl.Visible = isEventPage;
        }

        // 卡片自助页签专属第二行（类型 / 费用 / 排序 / 多人 / 查看升级），删卡页不显示
        if (_cardExtraFilters != null)
        {
            _cardExtraFilters.Visible = isCardPage;
        }

        if (_monsterSafetyControl != null)
        {
            _monsterSafetyControl.Visible = isMonsterPage;
        }

        UpdateEnchantmentControlVisibility();
    }
}
