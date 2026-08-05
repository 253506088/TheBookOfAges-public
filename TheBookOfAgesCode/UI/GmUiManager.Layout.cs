using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.Services;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 面板布局模块分文件：面板根容器布局、Body / 工具栏 / 网格 / 底栏的搭建，浮动关闭按钮，Toast 提示，
/// 通用导航按钮和筛选下拉框工厂、筛选项联动、底栏选中数与确认按钮可用性、ResetToolbar，以及下拉选项的取值/查询工具方法。
/// 内容来自 GmUiManager.cs 的布局相关方法段，行为与原实现一致，只做物理拆分。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 卡片自助页的一键导出按钮。
    /// </summary>
    private static Button? _cardExportButton;

    /// <summary>
    /// 按当前屏幕大小调整 GM 页面尺寸，采用全屏铺满布局并保留可配置边距。
    /// </summary>
    private static void ApplyPanelLayout()
    {
        if (_panel == null)
        {
            return;
        }

        _panel.CustomMinimumSize = Vector2.Zero;
        _panel.OffsetLeft = PanelHorizontalMargin;
        _panel.OffsetTop = PanelVerticalMargin;
        _panel.OffsetRight = -PanelHorizontalMargin;
        _panel.OffsetBottom = -PanelVerticalMargin;
        UpdateGridColumns();
    }

    /// <summary>
    /// 创建右上角悬浮关闭按钮，不占用内容区高度。
    /// </summary>
    private static Control BuildFloatingCloseButton()
    {
        Button close = new()
        {
            Text = "X",
            CustomMinimumSize = new Vector2(58, 48),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            ZIndex = 20,
        };
        close.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        close.OffsetLeft = -70f;
        close.OffsetTop = 8f;
        close.OffsetRight = -12f;
        close.OffsetBottom = 56f;
        ApplyDangerActionButtonStyle(close);
        BindTooltip(close, () => GmUiText.Pick("关闭", "Close"));
        close.Pressed += () => SetPanelVisible(false);
        return close;
    }

    /// <summary>
    /// 创建左侧导航和右侧功能页面。
    /// </summary>
    private static Control BuildBody()
    {
        HBoxContainer body = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };

        VBoxContainer nav = new()
        {
            CustomMinimumSize = new Vector2(180, 0),
        };
        nav.AddThemeConstantOverride("separation", 6);
        _grantTabButton = CreateNavButton(() => GmUiText.Pick("遗物自助", "Relic Self-Service"), GmPageMode.GrantRelics, "遗物自助.png");
        _removeTabButton = CreateNavButton(() => GmUiText.Pick("遗物删除", "Remove Relics"), GmPageMode.RemoveRelics, "遗物删除.png");
        _potionGrantTabButton = CreateNavButton(() => GmUiText.Pick("药水自助", "Potion Self-Service"), GmPageMode.GrantPotions, "药水自助.png");
        _orbBoxTabButton = CreateNavButton(() => GmUiText.OrbBoxTab, GmPageMode.OrbBox, "机煲球盒.png");
        _blightTabButton = CreateNavButton(() => GmUiText.BlightTab, GmPageMode.BlightCatalog, "荒疫.png");
        _grantCardsTabButton = CreateNavButton(() => GmUiText.CardGrantTab, GmPageMode.GrantCards, "卡片自助.png");
        _removeCardsTabButton = CreateNavButton(() => GmUiText.CardRemoveTab, GmPageMode.RemoveCards, "卡片删除.png");
        _upgradeCardsTabButton = CreateNavButton(() => GmUiText.CardUpgradeTab, GmPageMode.UpgradeCards, "卡片强化.png");
        _enchantCardsTabButton = CreateNavButton(() => GmUiText.CardEnchantTab, GmPageMode.EnchantCards, "附魔自助.png");
        _powerCatalogTabButton = CreateNavButton(() => GmUiText.PowerCatalogTab, GmPageMode.PowerCatalog, "能力图鉴.png");
        _monsterCageTabButton = CreateNavButton(() => GmUiText.MonsterCageTab, GmPageMode.MonsterCage, "刷怪笼.png");
        _eventRegistryTabButton = CreateNavButton(() => GmUiText.EventRegistryTab, GmPageMode.EventRegistry, "事件任意门.png");
        _chronicleTabButton = CreateNavButton(() => GmUiText.ChronicleTab, GmPageMode.Chronicle, "岁月史书.png");
        nav.AddChild(_grantTabButton);
        nav.AddChild(_removeTabButton);
        nav.AddChild(_potionGrantTabButton);
        nav.AddChild(_orbBoxTabButton);
        nav.AddChild(_blightTabButton);
        nav.AddChild(_grantCardsTabButton);
        nav.AddChild(_removeCardsTabButton);
        nav.AddChild(_upgradeCardsTabButton);
        nav.AddChild(_enchantCardsTabButton);
        nav.AddChild(_powerCatalogTabButton);
        nav.AddChild(_monsterCageTabButton);
        nav.AddChild(_eventRegistryTabButton);
        nav.AddChild(_chronicleTabButton);
        body.AddChild(nav);

        body.AddChild(BuildRelicPanel());
        return body;
    }

    /// <summary>
    /// 创建左侧页签按钮。
    /// </summary>
    private static Button CreateNavButton(Func<string> textProvider, GmPageMode mode, string iconFileName)
    {
        Button button = new()
        {
            CustomMinimumSize = new Vector2(160, 72),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        ApplyNavButtonVisualStyle(button);
        ApplyNavButtonIcon(button, iconFileName);
        BindButtonText(button, textProvider);
        button.Pressed += () => SwitchPage(mode);
        return button;
    }

    /// <summary>
    /// 给左侧页签按钮挂上图标；图标缺失时保留纯文字，不影响使用。
    /// </summary>
    private static void ApplyNavButtonIcon(Button button, string iconFileName)
    {
        Texture2D? icon = LoadNavButtonIcon(iconFileName);
        if (icon == null)
        {
            return;
        }

        button.Icon = icon;
        button.ExpandIcon = false;
        button.IconAlignment = HorizontalAlignment.Left;
        button.Alignment = HorizontalAlignment.Left;
        button.AddThemeConstantOverride("h_separation", 10);
    }

    /// <summary>
    /// 从 Mod 输出目录或当前工作目录读取左侧页签图标。
    /// </summary>
    private static Texture2D? LoadNavButtonIcon(string iconFileName)
    {
        Texture2D? icon = LoadGmUiTexture(iconFileName);
        if (icon == null)
        {
            MainFile.Logger.Info($"页签图标未找到：{iconFileName}。");
        }

        return icon;
    }

    /// <summary>
    /// 创建右侧功能页面。
    /// </summary>
    private static Control BuildRelicPanel()
    {
        VBoxContainer panel = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };

        panel.AddChild(BuildToolbar());
        panel.AddChild(BuildChroniclePanel());
        panel.AddChild(BuildGrid());
        panel.AddChild(BuildFooter());
        BuildToast(panel);
        UpdateFilterOptions();
        ApplyCurrentPageState();
        return panel;
    }

    /// <summary>
    /// 创建搜索框和筛选下拉框。
    /// </summary>
    private static Control BuildToolbar()
    {
        _toolbar = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(0, 118),
        };

        _toolbar.AddChild(BuildPotionSlotControlRow());
        _toolbar.AddChild(BuildEventQuickActionsControl());
        _toolbar.AddChild(BuildPowerControlRow());
        _toolbar.AddChild(BuildEnchantmentControlRow());
        _toolbar.AddChild(BuildMonsterSafetyControlRow());
        _toolbar.AddChild(BuildBlightControlRow());

        _searchInput = new LineEdit
        {
            PlaceholderText = GmUiText.Pick("输入遗物名称或描述", "Search relic name or description"),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _searchInput.AddThemeColorOverride("font_color", SecondaryActionTextColor);
        _searchInput.AddThemeStyleboxOverride("normal", CreateRoundedStyleBox(new Color(0.07f, 0.09f, 0.12f, 0.86f), new Color(0.24f, 0.30f, 0.38f, 0.92f), 8, 1, 10, 8));
        _searchInput.AddThemeStyleboxOverride("focus", CreateRoundedStyleBox(new Color(0.10f, 0.15f, 0.21f, 0.92f), new Color(0.52f, 0.67f, 0.79f, 0.96f), 8, 1, 10, 8));
        _searchInput.TextChanged += _ => OnSearchInputChanged();
        _toolbar.AddChild(_searchInput);

        _filtersContainer = new HBoxContainer();
        _categoryFilter = CreateFilter(RelicService.GetCategoryOptions());
        _rarityFilter = CreateFilter(RelicService.GetRarityOptions());
        _sourceFilter = CreateFilter(RelicService.GetSourceOptions());
        _resetFiltersButton = new Button
        {
            CustomMinimumSize = new Vector2(168, 42),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        ApplySecondaryActionButtonStyle(_resetFiltersButton);
        BindButtonText(_resetFiltersButton, () => GmUiText.ResetFiltersButton);
        BindTooltip(_resetFiltersButton, () => GmUiText.ResetFiltersTooltip);
        _resetFiltersButton.Pressed += ResetCurrentPageFiltersToDefault;
        _filtersContainer.AddChild(_categoryFilter);
        _filtersContainer.AddChild(_rarityFilter);
        _filtersContainer.AddChild(_sourceFilter);
        _filtersContainer.AddChild(_resetFiltersButton);
        _toolbar.AddChild(_filtersContainer);

        _toolbar.AddChild(BuildCardExtraFiltersRow());

        return _toolbar;
    }

    /// <summary>
    /// 构造刷怪笼安全控制行，默认使用安全召唤，需要时可手动开启高风险跨房间召唤。
    /// </summary>
    private static Control BuildMonsterSafetyControlRow()
    {
        _monsterSafetyControl = new HBoxContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(0, 44),
        };

        _monsterRiskySummonCheckbox = new CheckBox
        {
            ButtonPressed = false,
            CustomMinimumSize = new Vector2(260, 40),
            FocusMode = Control.FocusModeEnum.None,
        };
        _monsterRiskySummonCheckbox.AddThemeColorOverride("font_color", SecondaryActionTextColor);
        BindButtonText(_monsterRiskySummonCheckbox, () => GmUiText.MonsterRiskySummonLabel);
        BindTooltip(_monsterRiskySummonCheckbox, () => GmUiText.MonsterRiskySummonTooltip);
        _monsterRiskySummonCheckbox.Toggled += _ => RefreshCurrentPage();
        _monsterSafetyControl.AddChild(_monsterRiskySummonCheckbox);

        return _monsterSafetyControl;
    }

    /// <summary>
    /// 构造卡片自助页签专属的第二行：卡片类型、费用筛选、排序 + 多人模式、查看升级两个勾选框。
    /// 默认隐藏，仅在切到卡片自助页签时显示。
    /// </summary>
    private static Control BuildCardExtraFiltersRow()
    {
        _cardExtraFilters = new HBoxContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(0, 44),
        };

        _cardTypeFilter = CreateFilter(CardService.GetTypeOptions());
        _cardCostFilter = CreateFilter(CardService.GetCostOptions());
        _cardSortModeFilter = CreateFilter(GetCardSortModeOptions());
        _cardSortDirectionFilter = CreateFilter(GetCardSortDirectionOptions());
        _cardSortModeFilter.CustomMinimumSize = new Vector2(150, 42);
        _cardSortModeFilter.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        _cardSortDirectionFilter.CustomMinimumSize = new Vector2(120, 42);
        _cardSortDirectionFilter.SizeFlagsHorizontal = Control.SizeFlags.Fill;

        _cardMultiplayerCheckbox = new CheckBox
        {
            ButtonPressed = false,
            CustomMinimumSize = new Vector2(160, 38),
            FocusMode = Control.FocusModeEnum.None,
        };
        BindButtonText(_cardMultiplayerCheckbox, () => GmUiText.CardMultiplayerLabel);
        _cardMultiplayerCheckbox.Toggled += _ =>
        {
            if (_isApplyingFilterState)
            {
                return;
            }

            SaveCurrentFilterState();
            RefreshCurrentPage();
        };

        _cardShowUpgradeCheckbox = new CheckBox
        {
            ButtonPressed = false,
            CustomMinimumSize = new Vector2(140, 38),
            FocusMode = Control.FocusModeEnum.None,
        };
        BindButtonText(_cardShowUpgradeCheckbox, () => GmUiText.CardShowUpgradeLabel);
        _cardShowUpgradeCheckbox.Toggled += _ =>
        {
            if (_isApplyingFilterState)
            {
                return;
            }

            SaveCurrentFilterState();
            RefreshCurrentPage();
        };

        _cardExportButton = new Button
        {
            CustomMinimumSize = new Vector2(180, 42),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        ApplySecondaryActionButtonStyle(_cardExportButton);
        BindButtonText(_cardExportButton, () => GmUiText.CardExportButton);
        _cardExportButton.Pressed += OnExportAllCardsPressed;

        _cardExtraFilters.AddChild(_cardTypeFilter);
        _cardExtraFilters.AddChild(_cardCostFilter);
        _cardExtraFilters.AddChild(_cardSortModeFilter);
        _cardExtraFilters.AddChild(_cardSortDirectionFilter);
        _cardExtraFilters.AddChild(_cardMultiplayerCheckbox);
        _cardExtraFilters.AddChild(_cardShowUpgradeCheckbox);
        _cardExtraFilters.AddChild(_cardExportButton);

        return _cardExtraFilters;
    }

    /// <summary>
    /// 导出当前游戏已注册的全部卡片名称。
    /// 文件按类型分组输出，完成后通过 toast 提醒用户去看日志里的路径。
    /// </summary>
    private static void OnExportAllCardsPressed()
    {
        try
        {
            CardService.ExportAllCardsByType();
            ShowToast(GmUiText.CardExportSuccess);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"[卡片导出] 导出失败：{ex.Message}");
            ShowToast(GmUiText.CardExportFailed);
        }
    }

    /// <summary>
    /// 获取卡牌自助排序字段选项。
    /// </summary>
    private static IReadOnlyList<GmFilterOption> GetCardSortModeOptions()
    {
        return [
            new GmFilterOption(nameof(CardSortMode.Default), GmUiText.CardSortDefault),
            new GmFilterOption(nameof(CardSortMode.Cost), GmUiText.CardSortCost),
            new GmFilterOption(nameof(CardSortMode.Damage), GmUiText.CardSortDamage),
            new GmFilterOption(nameof(CardSortMode.Name), GmUiText.CardSortName),
        ];
    }

    /// <summary>
    /// 获取卡牌自助排序方向选项。
    /// </summary>
    private static IReadOnlyList<GmFilterOption> GetCardSortDirectionOptions()
    {
        return [
            new GmFilterOption(nameof(CardSortDirection.Asc), GmUiText.CardSortAsc),
            new GmFilterOption(nameof(CardSortDirection.Desc), GmUiText.CardSortDesc),
        ];
    }

    /// <summary>
    /// 创建物品网格，一行显示多个遗物、药水、怪物或事件按钮。
    /// 同时创建虚拟化卡片容器（卡片自助页专用，与 GridContainer 互斥显示）。
    /// ScrollContainer 内只放一个 _gridScrollContent 作为唯一子节点，
    /// 里面包含 _grid（其他页签用）和 _cardVirtualSpacer（卡片页用），互斥显示。
    /// </summary>
    private static Control BuildGrid()
    {
        _gridScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
        };

        // ScrollContainer 的唯一子节点：VBoxContainer 包裹所有内容
        VBoxContainer scrollContent = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _gridScroll.AddChild(scrollContent);

        _grid = new GridContainer
        {
            Columns = 6,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        scrollContent.AddChild(_grid);

        // 虚拟化卡片占位容器：撑出正确的滚动高度，内部用绝对定位放 slot
        _cardVirtualSpacer = new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 0),
            Visible = false,
        };
        scrollContent.AddChild(_cardVirtualSpacer);

        _cardVirtualContainer = new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        // 绝对定位覆盖整个 spacer 区域
        _cardVirtualContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _cardVirtualSpacer.AddChild(_cardVirtualContainer);

        // 监听滚动事件，驱动虚拟化 slot 刷新
        _gridScroll.GetVScrollBar().ValueChanged += OnCardVirtualScrollChanged;

        return _gridScroll;
    }

    /// <summary>
    /// 创建底部状态栏和确认按钮。
    /// </summary>
    private static Control BuildFooter()
    {
        _footerContainer = new HBoxContainer()
        {
            CustomMinimumSize = new Vector2(0, 86),
        };

        _statusLabel = new RichTextLabel
        {
            Text = GmUiText.Pick("已选 0 个", "Selected 0"),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _statusLabel.AddThemeColorOverride("default_color", AuxiliaryTextColor);
        _footerContainer.AddChild(_statusLabel);

        _enchantmentFooterControl = new HBoxContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(0, 46),
        };
        _enchantmentFooterControl.AddThemeConstantOverride("separation", 8);

        _enchantmentAmountLabel = new Label
        {
            Visible = false,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(92, 0),
        };
        _enchantmentAmountLabel.AddThemeColorOverride("font_color", FieldLabelColor);
        BindLabelText(_enchantmentAmountLabel, () => GmUiText.CardEnchantAmountLabel);
        _enchantmentFooterControl.AddChild(_enchantmentAmountLabel);

        _enchantmentAmountInput = new LineEdit
        {
            Visible = false,
            CustomMinimumSize = new Vector2(84, 38),
            SelectAllOnFocus = true,
            PlaceholderText = GmUiText.CardEnchantAmountPlaceholder,
        };
        _enchantmentAmountInput.AddThemeColorOverride("font_color", FieldValueColor);
        _enchantmentAmountInput.AddThemeStyleboxOverride("normal", CreateRoundedStyleBox(new Color(0.07f, 0.09f, 0.12f, 0.88f), new Color(0.28f, 0.35f, 0.42f, 0.92f), 8, 1, 8, 8));
        _enchantmentAmountInput.AddThemeStyleboxOverride("focus", CreateRoundedStyleBox(new Color(0.11f, 0.16f, 0.22f, 0.94f), new Color(0.56f, 0.68f, 0.80f, 0.96f), 8, 1, 8, 8));
        _enchantmentAmountInput.TextChanged += OnEnchantmentAmountTextChanged;
        _enchantmentAmountInput.TextSubmitted += OnEnchantmentAmountTextSubmitted;
        _enchantmentAmountInput.FocusExited += OnEnchantmentAmountFocusExited;
        _enchantmentFooterControl.AddChild(_enchantmentAmountInput);

        _enchantmentSafetyModeCheckbox = new CheckBox
        {
            Visible = false,
            ButtonPressed = true,
            CustomMinimumSize = new Vector2(250, 38),
            FocusMode = Control.FocusModeEnum.None,
        };
        _enchantmentSafetyModeCheckbox.AddThemeColorOverride("font_color", SecondaryActionTextColor);
        BindButtonText(_enchantmentSafetyModeCheckbox, () => GmUiText.CardEnchantSafetyModeLabel);
        BindTooltip(_enchantmentSafetyModeCheckbox, () => GmUiText.CardEnchantSafetyModeTooltip);
        _enchantmentSafetyModeCheckbox.Toggled += OnEnchantmentSafetyModeToggled;
        _enchantmentFooterControl.AddChild(_enchantmentSafetyModeCheckbox);

        _footerContainer.AddChild(_enchantmentFooterControl);

        _enchantmentBackButton = new Button
        {
            Visible = false,
            CustomMinimumSize = new Vector2(170, 46),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        ApplySecondaryActionButtonStyle(_enchantmentBackButton);
        BindButtonText(_enchantmentBackButton, () => GmUiText.CardEnchantBackButton);
        _enchantmentBackButton.Pressed += BackToEnchantmentSelection;
        _footerContainer.AddChild(_enchantmentBackButton);

        _selectAllButton = new Button
        {
            CustomMinimumSize = new Vector2(126, 46),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        ApplySecondaryActionButtonStyle(_selectAllButton);
        BindButtonText(_selectAllButton, GetSelectAllButtonText);
        _selectAllButton.Pressed += ToggleSelectAllCurrentPage;
        _footerContainer.AddChild(_selectAllButton);

        _clearButton = new Button
        {
            CustomMinimumSize = new Vector2(126, 46),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        ApplySecondaryActionButtonStyle(_clearButton);
        BindButtonText(_clearButton, () => GmUiText.Pick("清空", "Clear"));
        _clearButton.Pressed += ClearSelection;
        _footerContainer.AddChild(_clearButton);

        _confirmButton = new Button
        {
            CustomMinimumSize = new Vector2(160, 46),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        ApplyPrimaryActionButtonStyle(_confirmButton);
        _confirmButton.Pressed += OnConfirmPressed;
        _footerContainer.AddChild(_confirmButton);

        return _footerContainer;
    }

    /// <summary>
    /// 创建自动消失的小提示。
    /// </summary>
    private static void BuildToast(Node parent)
    {
        _toastLabel = new Label
        {
            Visible = false,
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(0, 38),
        };
        _toastLabel.AddThemeColorOverride("font_color", SectionTitleColor);
        parent.AddChild(_toastLabel);

        _toastTimer = new Godot.Timer
        {
            OneShot = true,
            WaitTime = 3,
        };
        _toastTimer.Timeout += () => _toastLabel.Visible = false;
        parent.AddChild(_toastTimer);
    }

    /// <summary>
    /// 创建筛选下拉框。
    /// </summary>
    private static OptionButton CreateFilter(IEnumerable<GmFilterOption> items)
    {
        OptionButton optionButton = new()
        {
            CustomMinimumSize = new Vector2(250, 42),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        ApplySelectVisualStyle(optionButton);

        SetFilterItems(optionButton, items);
        optionButton.ItemSelected += _ =>
        {
            if (_isApplyingFilterState)
            {
                return;
            }

            SaveCurrentFilterState();
            RefreshCurrentPage();
        };
        return optionButton;
    }

    private static string? GetSelectedMetadata(OptionButton? optionButton)
    {
        if (optionButton == null || optionButton.Selected < 0)
        {
            return null;
        }

        Variant metadata = optionButton.GetItemMetadata(optionButton.Selected);
        return metadata.VariantType == Variant.Type.Nil ? null : metadata.AsString();
    }

    /// <summary>
    /// 更新底部状态栏普通文本。
    /// </summary>
    private static void SetStatusText(string text)
    {
        if (_statusLabel == null)
        {
            return;
        }

        _statusLabel.Clear();
        _statusLabel.Text = text;
    }

    /// <summary>
    /// 更新底部状态栏，并把游戏描述里的颜色标记渲染成高亮文本。
    /// </summary>
    private static void SetStatusRichText(string text)
    {
        if (_statusLabel == null)
        {
            return;
        }

        _statusLabel.Clear();
        AppendItemTooltipText(_statusLabel, text);
    }

    /// <summary>
    /// 更新底部已选数量和当前显示数量。
    /// </summary>
    private static void UpdateSelectionStatus(int? showingCount = null)
    {
        if (_statusLabel == null)
        {
            return;
        }

        UpdateSelectAllButtonState();

        if (TryGetCurrentPageFeature(out GmFeature feature))
        {
            GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(feature);
            if (!availability.IsAllowed)
            {
                SetStatusText(availability.Reason);
                return;
            }
        }

        int selectedCount = GetCurrentSelectedCount();
        if (_currentPage == GmPageMode.PowerCatalog)
        {
            string powerSelectedText = GmUiText.Pick($"已选 {selectedCount} 个能力", $"Selected {selectedCount} powers");
            if (_powerTargetMode == PowerTargetMode.Player)
            {
                if (!PowerService.CanOperatePlayer())
                {
                    SetStatusText(showingCount.HasValue
                        ? GmUiText.Pick($"{GmUiText.PowerNotInRun}    {powerSelectedText}    当前显示 {showingCount.Value} 个", $"{GmUiText.PowerNotInRun}    {powerSelectedText}    Showing {showingCount.Value}")
                        : GmUiText.Pick($"{GmUiText.PowerNotInRun}    {powerSelectedText}", $"{GmUiText.PowerNotInRun}    {powerSelectedText}"));
                    return;
                }

                string playerTargetText = GmUiText.Pick("目标 玩家", "Target Player");
                SetStatusText(showingCount.HasValue
                    ? GmUiText.Pick($"{powerSelectedText}    {playerTargetText}    当前显示 {showingCount.Value} 个", $"{powerSelectedText}    {playerTargetText}    Showing {showingCount.Value}")
                    : $"{powerSelectedText}    {playerTargetText}");
                return;
            }

            if (!PowerService.CanOperateMonsters())
            {
                SetStatusText(showingCount.HasValue
                    ? GmUiText.Pick($"{GmUiText.PowerNotInCombat}    {powerSelectedText}    当前显示 {showingCount.Value} 个", $"{GmUiText.PowerNotInCombat}    {powerSelectedText}    Showing {showingCount.Value}")
                    : GmUiText.Pick($"{GmUiText.PowerNotInCombat}    {powerSelectedText}", $"{GmUiText.PowerNotInCombat}    {powerSelectedText}"));
                return;
            }

            string monsterTargetText = _powerApplyAllMonsters
                ? GmUiText.Pick("目标 全部怪物", "Target All Monsters")
                : GmUiText.Pick($"目标 已选怪物 {SelectedPowerMonsterTargets.Count} 个", $"Target {SelectedPowerMonsterTargets.Count} monsters");
            SetStatusText(showingCount.HasValue
                ? GmUiText.Pick($"{powerSelectedText}    {monsterTargetText}    当前显示 {showingCount.Value} 个", $"{powerSelectedText}    {monsterTargetText}    Showing {showingCount.Value}")
                : $"{powerSelectedText}    {monsterTargetText}");
            return;
        }

        if (_currentPage == GmPageMode.MonsterCage)
        {
            string monsterSelectedText = GmUiText.Pick($"已选 {selectedCount} 个", $"Selected {selectedCount}");
            string monsterModeText = (_monsterRiskySummonCheckbox?.ButtonPressed ?? false)
                ? GmUiText.MonsterRiskyModeStatus
                : GmUiText.MonsterSafeModeStatus;
            if (!MonsterService.CanSpawnInCurrentRoom())
            {
                SetStatusText(showingCount.HasValue
                    ? GmUiText.Pick($"{GmUiText.MonsterNotInCombat}    {monsterModeText}    {monsterSelectedText}    当前显示 {showingCount.Value} 个", $"{GmUiText.MonsterNotInCombat}    {monsterModeText}    {monsterSelectedText}    Showing {showingCount.Value}")
                    : GmUiText.Pick($"{GmUiText.MonsterNotInCombat}    {monsterModeText}    {monsterSelectedText}", $"{GmUiText.MonsterNotInCombat}    {monsterModeText}    {monsterSelectedText}"));
                return;
            }

            SetStatusText(showingCount.HasValue
                ? GmUiText.Pick($"{monsterModeText}    {monsterSelectedText}    当前显示 {showingCount.Value} 个", $"{monsterModeText}    {monsterSelectedText}    Showing {showingCount.Value}")
                : $"{monsterModeText}    {monsterSelectedText}");
            return;
        }

        if (_currentPage == GmPageMode.BlightCatalog)
        {
            int enabledCount = BlightService.GetEnabledBlightCount();
            string enabledText = GmUiText.Pick($"当前已启用 {enabledCount} 个", $"Enabled {enabledCount}");
            if (!BlightService.CanOperateBlights())
            {
                SetStatusText(showingCount.HasValue
                    ? GmUiText.Pick($"{GmUiText.BlightNotInRun}    当前显示 {showingCount.Value} 个", $"{GmUiText.BlightNotInRun}    Showing {showingCount.Value}")
                    : GmUiText.BlightNotInRun);
                return;
            }

            SetStatusText(showingCount.HasValue
                ? GmUiText.Pick($"{enabledText}    当前显示 {showingCount.Value} 个    点击条目开关荒疫", $"{enabledText}    Showing {showingCount.Value}    Click items to toggle")
                : GmUiText.Pick($"{enabledText}    点击条目开关荒疫", $"{enabledText}    Click items to toggle"));
            return;
        }
        if (_currentPage == GmPageMode.RemoveCards)
        {
            // 删卡页：把「不在 run / 牌组空 / 战斗中提示」一起拼到状态栏
            string selectedText = GmUiText.Pick($"已选 {selectedCount} 张", $"Selected {selectedCount}");
            int childCount = _grid?.GetChildCount() ?? 0;
            int actualShowing = showingCount ?? childCount;

            // 不在 run 中：CardService.GetDeckCards 会返回空列表，actualShowing == 0
            string? prefix = null;
            if (actualShowing == 0)
            {
                // 区分「不在 run」与「牌组为空」：CardService.IsInCombat 不能直接判 run 状态，
                // 但 GetDeckCards 在不在 run 时返回 []，这里用更简单的统一文案：直接给「牌组空」即可，
                // 真正不在 run 的提示由确认按钮置灰 + 点确认时 RemoveDeckCardsAsync 给出 NotInRun toast。
                prefix = GmUiText.CardRemoveEmptyDeck;
            }
            else if (CardService.IsInCombat())
            {
                prefix = GmUiText.CardRemoveInCombatHint;
            }

            string body = showingCount.HasValue
                ? GmUiText.Pick($"{selectedText}    当前显示 {showingCount.Value} 张", $"{selectedText}    Showing {showingCount.Value}")
                : selectedText;
            SetStatusText(string.IsNullOrEmpty(prefix) ? body : $"{prefix}    {body}");
            return;
        }

        if (_currentPage == GmPageMode.UpgradeCards)
        {
            // 强化页：与删卡同样的状态栏拼接。空网格时给出「牌组没有可强化卡片」提示，
            // 因为这里不仅判 deck 为空，还需要排除「deck 有卡但都已满级」的情况；
            // 这两种场景在状态栏体感一致 —— 网格空 = 用户没东西可操作。
            string selectedText = GmUiText.Pick($"已选 {selectedCount} 张", $"Selected {selectedCount}");
            int childCount = _grid?.GetChildCount() ?? 0;
            int actualShowing = showingCount ?? childCount;

            string? prefix = null;
            if (actualShowing == 0)
            {
                prefix = GmUiText.CardUpgradeNoUpgradable;
            }
            else if (CardService.IsInCombat())
            {
                prefix = GmUiText.CardUpgradeInCombatHint;
            }

            string body = showingCount.HasValue
                ? GmUiText.Pick($"{selectedText}    当前显示 {showingCount.Value} 张", $"{selectedText}    Showing {showingCount.Value}")
                : selectedText;
            SetStatusText(string.IsNullOrEmpty(prefix) ? body : $"{prefix}    {body}");
            return;
        }

        if (_currentPage == GmPageMode.EnchantCards)
        {
            if (_enchantmentPageStep == EnchantmentPageStep.SelectEnchantment)
            {
                string selectedText = _selectedCardEnchantment == null
                    ? GmUiText.Pick("未选择附魔", "No enchantment selected")
                    : GmUiText.Pick($"当前附魔：{_selectedCardEnchantment.Name}", $"Current enchantment: {_selectedCardEnchantment.Name}");
                SetStatusText(showingCount.HasValue
                    ? GmUiText.Pick($"{selectedText}    当前显示 {showingCount.Value} 个", $"{selectedText}    Showing {showingCount.Value}")
                    : selectedText);
                return;
            }

            string cardSelectedText = GmUiText.Pick($"已选 {selectedCount} 张", $"Selected {selectedCount}");
            string enchantmentText = _selectedCardEnchantment == null
                ? GmUiText.Pick("未选择附魔", "No enchantment selected")
                : GmUiText.Pick($"当前附魔：{_selectedCardEnchantment.Name}。效果：{_selectedCardEnchantment.Description}", $"Current enchantment: {_selectedCardEnchantment.Name}. Effect: {_selectedCardEnchantment.Description}");
            string cardBody = showingCount.HasValue
                ? GmUiText.Pick($"{cardSelectedText}    当前显示 {showingCount.Value} 张", $"{cardSelectedText}    Showing {showingCount.Value}")
                : cardSelectedText;
            SetStatusRichText($"{enchantmentText}\n{cardBody}");
            return;
        }

        string defaultSelectedText = GmUiText.Pick($"已选 {selectedCount} 个", $"Selected {selectedCount}");
        SetStatusText(showingCount.HasValue
            ? GmUiText.Pick($"{defaultSelectedText}    当前显示 {showingCount.Value} 个", $"{defaultSelectedText}    Showing {showingCount.Value}")
            : defaultSelectedText);
    }

    /// <summary>
    /// 根据当前页签和运行状态更新确认按钮可用性。
    /// 刷怪笼在非战斗房间时禁用确认按钮，其他页签保持原逻辑。
    /// </summary>
    private static void UpdateConfirmButtonAvailability()
    {
        if (_confirmButton == null)
        {
            return;
        }

        UpdateSelectAllButtonState();
        _confirmButton.Visible = _currentPage != GmPageMode.BlightCatalog;
        if (_clearButton != null)
        {
            _clearButton.Visible = _currentPage != GmPageMode.BlightCatalog;
        }

        if (_isConfirming)
        {
            _confirmButton.Disabled = true;
            return;
        }

        if (TryGetCurrentPageFeature(out GmFeature feature))
        {
            GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(feature);
            if (!availability.IsAllowed)
            {
                _confirmButton.Disabled = true;
                SetStatusText(availability.Reason);
                return;
            }
        }

        if (_currentPage == GmPageMode.MonsterCage)
        {
            _confirmButton.Disabled = !MonsterService.CanSpawnInCurrentRoom();
            return;
        }

        if (_currentPage == GmPageMode.PowerCatalog)
        {
            _confirmButton.Disabled = _powerTargetMode == PowerTargetMode.Player
                ? !PowerService.CanOperatePlayer()
                : !PowerService.CanOperateMonsters();
            return;
        }

        if (_currentPage == GmPageMode.OrbBox)
        {
            _confirmButton.Disabled = !OrbService.CanGrantOrbs();
            return;
        }

        if (_currentPage == GmPageMode.BlightCatalog)
        {
            return;
        }

        if (_currentPage == GmPageMode.RemoveCards)
        {
            // 不在 run 中或网格里没卡时禁用按钮，避免空选误点
            int childCount = _grid?.GetChildCount() ?? 0;
            _confirmButton.Disabled = childCount == 0;
            return;
        }

        if (_currentPage == GmPageMode.UpgradeCards)
        {
            // 与删卡同口径：网格空（不在 run / 牌组空 / 无可升级卡）一律禁用
            int childCount = _grid?.GetChildCount() ?? 0;
            _confirmButton.Disabled = childCount == 0;
            return;
        }

        if (_currentPage == GmPageMode.EnchantCards)
        {
            _confirmButton.Disabled = _enchantmentPageStep == EnchantmentPageStep.SelectEnchantment ||
                                      _selectedCardEnchantment == null ||
                                      GetCurrentVisibleEnchantCards().Count == 0 ||
                                      !IsCurrentEnchantmentAmountValid();
            return;
        }

        _confirmButton.Disabled = false;
    }

    /// <summary>
    /// 切换当前删除页的全选状态：未全选时选中当前筛选结果，已全选时取消当前筛选结果。
    /// </summary>
    private static void ToggleSelectAllCurrentPage()
    {
        if (!IsSelectAllSupportedPage())
        {
            return;
        }

        if (AreAllCurrentVisibleItemsSelected())
        {
            UnselectCurrentVisibleItems();
        }
        else
        {
            SelectCurrentVisibleItems();
        }

        RefreshCurrentPage();
    }

    /// <summary>
    /// 选中当前删除页筛选后正在显示的全部对象。
    /// </summary>
    private static void SelectCurrentVisibleItems()
    {
        if (_currentPage == GmPageMode.RemoveCards)
        {
            foreach (CardDisplayInfo card in GetCurrentVisibleRemoveCards())
            {
                SelectedRemoveCards[card.Card] = card;
            }

            return;
        }

        if (_currentPage == GmPageMode.UpgradeCards)
        {
            foreach (CardDisplayInfo card in GetCurrentVisibleUpgradeCards())
            {
                SelectedUpgradeCards[card.Card] = card;
            }

            return;
        }

        if (_currentPage == GmPageMode.EnchantCards && _enchantmentPageStep == EnchantmentPageStep.SelectCards)
        {
            foreach (CardDisplayInfo card in GetCurrentVisibleEnchantCards())
            {
                SelectedEnchantCards[card.Card] = card;
            }

            return;
        }

        if (_currentPage == GmPageMode.RemoveRelics)
        {
            foreach (RelicDisplayInfo relic in GetCurrentPageRelics())
            {
                CurrentSelectedRelics[relic.IdText] = relic;
            }
        }
    }

    /// <summary>
    /// 取消当前删除页筛选后正在显示的全部对象。
    /// </summary>
    private static void UnselectCurrentVisibleItems()
    {
        if (_currentPage == GmPageMode.RemoveCards)
        {
            foreach (CardDisplayInfo card in GetCurrentVisibleRemoveCards())
            {
                SelectedRemoveCards.Remove(card.Card);
            }

            return;
        }

        if (_currentPage == GmPageMode.UpgradeCards)
        {
            foreach (CardDisplayInfo card in GetCurrentVisibleUpgradeCards())
            {
                SelectedUpgradeCards.Remove(card.Card);
            }

            return;
        }

        if (_currentPage == GmPageMode.EnchantCards && _enchantmentPageStep == EnchantmentPageStep.SelectCards)
        {
            foreach (CardDisplayInfo card in GetCurrentVisibleEnchantCards())
            {
                SelectedEnchantCards.Remove(card.Card);
            }

            return;
        }

        if (_currentPage == GmPageMode.RemoveRelics)
        {
            foreach (RelicDisplayInfo relic in GetCurrentPageRelics())
            {
                CurrentSelectedRelics.Remove(relic.IdText);
            }
        }
    }

    /// <summary>
    /// 判断当前删除页筛选后正在显示的对象是否已经全部选中。
    /// </summary>
    private static bool AreAllCurrentVisibleItemsSelected()
    {
        if (_currentPage == GmPageMode.RemoveCards)
        {
            IReadOnlyList<CardDisplayInfo> cards = GetCurrentVisibleRemoveCards();
            return cards.Count > 0 && cards.All(card => SelectedRemoveCards.ContainsKey(card.Card));
        }

        if (_currentPage == GmPageMode.UpgradeCards)
        {
            IReadOnlyList<CardDisplayInfo> cards = GetCurrentVisibleUpgradeCards();
            return cards.Count > 0 && cards.All(card => SelectedUpgradeCards.ContainsKey(card.Card));
        }

        if (_currentPage == GmPageMode.EnchantCards && _enchantmentPageStep == EnchantmentPageStep.SelectCards)
        {
            IReadOnlyList<CardDisplayInfo> cards = GetCurrentVisibleEnchantCards();
            return cards.Count > 0 && cards.All(card => SelectedEnchantCards.ContainsKey(card.Card));
        }

        if (_currentPage == GmPageMode.RemoveRelics)
        {
            IReadOnlyList<RelicDisplayInfo> relics = GetCurrentPageRelics();
            return relics.Count > 0 && relics.All(relic => CurrentSelectedRelics.ContainsKey(relic.IdText));
        }

        return false;
    }

    /// <summary>
    /// 判断当前删除页筛选后是否有可显示对象。
    /// </summary>
    private static bool HasCurrentVisibleItems()
    {
        if (_currentPage == GmPageMode.RemoveCards)
        {
            return GetCurrentVisibleRemoveCards().Count > 0;
        }

        if (_currentPage == GmPageMode.UpgradeCards)
        {
            return GetCurrentVisibleUpgradeCards().Count > 0;
        }

        if (_currentPage == GmPageMode.EnchantCards && _enchantmentPageStep == EnchantmentPageStep.SelectCards)
        {
            return GetCurrentVisibleEnchantCards().Count > 0;
        }

        if (_currentPage == GmPageMode.RemoveRelics)
        {
            return GetCurrentPageRelics().Count > 0;
        }

        return false;
    }
    /// <summary>
    /// 获取卡片删除页当前搜索和筛选后的牌组卡片。
    /// </summary>
    private static IReadOnlyList<CardDisplayInfo> GetCurrentVisibleRemoveCards()
    {
        if (_searchInput == null || _categoryFilter == null)
        {
            return [];
        }

        return CardService.GetDeckCardsSorted(_searchInput.Text, GetSelectedFilterKey(_categoryFilter));
    }

    /// <summary>
    /// 获取卡片强化页当前搜索和筛选后、且可升级未满级的牌组卡片。
    /// </summary>
    private static IReadOnlyList<CardDisplayInfo> GetCurrentVisibleUpgradeCards()
    {
        if (_searchInput == null || _categoryFilter == null)
        {
            return [];
        }

        return CardService.GetUpgradableDeckCardsSorted(_searchInput.Text, GetSelectedFilterKey(_categoryFilter));
    }

    /// <summary>
    /// 同步全选按钮的可见性、可用性和当前文案。
    /// </summary>
    private static void UpdateSelectAllButtonState()
    {
        if (_selectAllButton == null)
        {
            return;
        }

        bool visible = IsSelectAllSupportedPage();
        _selectAllButton.Visible = visible;
        _selectAllButton.Disabled = !visible || !HasCurrentVisibleItems();
        _selectAllButton.Text = GetSelectAllButtonText();
    }

    /// <summary>
    /// 判断当前页签是否支持全选按钮。
    /// </summary>
    private static bool IsSelectAllSupportedPage()
    {
        return _currentPage == GmPageMode.RemoveRelics || _currentPage == GmPageMode.RemoveCards || _currentPage == GmPageMode.UpgradeCards || (_currentPage == GmPageMode.EnchantCards && _enchantmentPageStep == EnchantmentPageStep.SelectCards);
    }

    /// <summary>
    /// 按当前选择状态返回全选按钮文案。
    /// </summary>
    private static string GetSelectAllButtonText()
    {
        return AreAllCurrentVisibleItemsSelected()
            ? GmUiText.CancelSelectAllButton
            : GmUiText.SelectAllButton;
    }

    /// <summary>
    /// 展示自动消失提示。
    /// </summary>
    private static void ShowToast(string text)
    {
        if (_toastLabel == null || _toastTimer == null)
        {
            return;
        }

        _toastLabel.Text = text;
        _toastLabel.Visible = true;
        _toastTimer.Start();
    }

    /// <summary>
    /// 获取下拉框当前选中文本。
    /// </summary>
    private static string GetSelectedText(OptionButton optionButton)
    {
        if (optionButton.Selected < 0)
        {
            return "";
        }

        return optionButton.GetItemText(optionButton.Selected);
    }

    /// <summary>
    /// 获取筛选下拉框当前选中的稳定键值。
    /// </summary>
    private static string GetSelectedFilterKey(OptionButton optionButton)
    {
        if (optionButton.Selected < 0)
        {
            return "";
        }

        if (FilterOptionKeys.TryGetValue(optionButton, out List<string>? keys) &&
            optionButton.Selected < keys.Count)
        {
            return keys[optionButton.Selected];
        }

        return GetSelectedText(optionButton);
    }

    /// <summary>
    /// 重置搜索和筛选，保证进入页签时看到完整列表。
    /// </summary>
    private static void ResetToolbar()
    {
        if (_searchInput != null)
        {
            _searchInput.Text = "";
        }

        _categoryFilter?.Select(0);
        _rarityFilter?.Select(0);
        _sourceFilter?.Select(0);
        _cardTypeFilter?.Select(0);
        _cardCostFilter?.Select(0);
        _cardSortModeFilter?.Select(0);
        _cardSortDirectionFilter?.Select(0);

        // 卡片页签：默认不勾选「多人模式卡片」、不勾选「查看升级」、角色筛选定位到当前角色
        if (_cardMultiplayerCheckbox != null)
        {
            _cardMultiplayerCheckbox.SetPressedNoSignal(false);
        }

        if (_cardShowUpgradeCheckbox != null)
        {
            _cardShowUpgradeCheckbox.SetPressedNoSignal(false);
        }

        if (_monsterRiskySummonCheckbox != null)
        {
            _monsterRiskySummonCheckbox.SetPressedNoSignal(false);
        }

        if (_currentPage == GmPageMode.GrantCards && _categoryFilter != null)
        {
            SelectFilterByKey(_categoryFilter, CardService.GetCurrentCharacterKey());
        }

        // 进入「卡片删除」页签时清掉旧的选中状态，避免上次离开时残留的选中跨入此次操作
        if (_currentPage == GmPageMode.RemoveCards)
        {
            SelectedRemoveCards.Clear();
        }

        // 进入「卡片强化」页签时同样清掉旧选中
        if (_currentPage == GmPageMode.UpgradeCards)
        {
            SelectedUpgradeCards.Clear();
        }

        if (_currentPage == GmPageMode.EnchantCards)
        {
            ResetEnchantmentPageState();
            UpdateFilterOptions();
        }
    }

    /// <summary>
    /// 按 key 把筛选下拉框选中到对应项；找不到时保留首项。
    /// </summary>
    private static void SelectFilterByKey(OptionButton optionButton, string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (!FilterOptionKeys.TryGetValue(optionButton, out List<string>? keys))
        {
            return;
        }

        for (int i = 0; i < keys.Count; i++)
        {
            if (string.Equals(keys[i], key, StringComparison.OrdinalIgnoreCase))
            {
                optionButton.Select(i);
                return;
            }
        }
    }

    /// <summary>
    /// 根据当前页签刷新筛选下拉框选项。
    /// </summary>
    private static void UpdateFilterOptions()
    {
        if (_categoryFilter == null || _rarityFilter == null || _sourceFilter == null)
        {
            return;
        }

        // 事件页签：只有分类和来源，没有稀有度
        if (_currentPage == GmPageMode.EventRegistry)
        {
            PopulateFilter(_categoryFilter, EventService.GetCategoryOptions());
            PopulateFilter(_sourceFilter, EventService.GetSourceOptions());
            return;
        }

        if (_currentPage == GmPageMode.MonsterCage)
        {
            PopulateFilter(_categoryFilter, MonsterService.GetTierOptions());
            PopulateFilter(_sourceFilter, MonsterService.GetSourceOptions());
            return;
        }

        if (_currentPage == GmPageMode.PowerCatalog)
        {
            PopulateFilter(_categoryFilter, PowerService.GetTypeOptions());
            PopulateFilter(_sourceFilter, PowerService.GetSourceOptions());
            return;
        }

        if (_currentPage == GmPageMode.OrbBox)
        {
            PopulateFilter(_categoryFilter, [new GmFilterOption("all", GmUiText.Pick("全部充能球", "All Orbs"))]);
            PopulateFilter(_sourceFilter, OrbService.GetSourceOptions());
            return;
        }

        if (_currentPage == GmPageMode.BlightCatalog)
        {
            PopulateFilter(_categoryFilter, BlightService.GetCategoryOptions());
            PopulateFilter(_sourceFilter, BlightService.GetSourceOptions());
            return;
        }

        if (_currentPage == GmPageMode.GrantPotions)
        {
            PopulateFilter(_categoryFilter, PotionService.GetCategoryOptions());
            PopulateFilter(_rarityFilter, PotionService.GetRarityOptions());
            PopulateFilter(_sourceFilter, PotionService.GetSourceOptions());
            return;
        }

        if (_currentPage == GmPageMode.GrantCards)
        {
            PopulateFilter(_categoryFilter, CardService.GetCharacterOptions());
            PopulateFilter(_rarityFilter, CardService.GetRarityOptions());
            PopulateFilter(_sourceFilter, CardService.GetSourceOptions());
            if (_cardTypeFilter != null)
            {
                PopulateFilter(_cardTypeFilter, CardService.GetTypeOptions());
            }

            if (_cardCostFilter != null)
            {
                PopulateFilter(_cardCostFilter, CardService.GetCostOptions());
            }

            if (_cardSortModeFilter != null)
            {
                PopulateFilter(_cardSortModeFilter, GetCardSortModeOptions());
            }

            if (_cardSortDirectionFilter != null)
            {
                PopulateFilter(_cardSortDirectionFilter, GetCardSortDirectionOptions());
            }

            return;
        }

        if (_currentPage == GmPageMode.RemoveCards)
        {
            // 删卡页只用一个合并筛选（类型 + 无色），rarity / source 都隐藏（在 UpdateEventPageVisibility 处理）
            PopulateFilter(_categoryFilter, CardService.GetDeckCategoryOptions());
            return;
        }

        if (_currentPage == GmPageMode.UpgradeCards)
        {
            // 强化页与删卡页共用同一份合并分类筛选；rarity / source 同样隐藏
            PopulateFilter(_categoryFilter, CardService.GetDeckCategoryOptions());
            return;
        }

        if (_currentPage == GmPageMode.EnchantCards)
        {
            if (_enchantmentPageStep == EnchantmentPageStep.SelectCards)
            {
                PopulateFilter(_categoryFilter, CardService.GetDeckCategoryOptions());
            }
            else
            {
                PopulateFilter(_sourceFilter, EnchantmentService.GetSourceOptions());
            }

            return;
        }

        PopulateFilter(_categoryFilter, RelicService.GetCategoryOptions());
        PopulateFilter(_rarityFilter, RelicService.GetRarityOptions());
        PopulateFilter(_sourceFilter, RelicService.GetSourceOptions());
    }

    /// <summary>
    /// 用指定文本重建筛选下拉框。
    /// </summary>
    private static void PopulateFilter(OptionButton optionButton, IEnumerable<GmFilterOption> items)
    {
        SetFilterItems(optionButton, items);
    }

    /// <summary>
    /// 给筛选下拉框写入稳定键值和展示文本。
    /// 重建选项后尝试恢复之前选中的 key，保证关闭面板再打开时筛选状态不丢失。
    /// </summary>
    private static void SetFilterItems(OptionButton optionButton, IEnumerable<GmFilterOption> items)
    {
        // 记住当前选中的 key，重建后尝试恢复。
        string? previousKey = null;
        if (optionButton.Selected >= 0 &&
            FilterOptionKeys.TryGetValue(optionButton, out List<string>? oldKeys) &&
            optionButton.Selected < oldKeys.Count)
        {
            previousKey = oldKeys[optionButton.Selected];
        }

        optionButton.Clear();
        List<string> keys = [];
        foreach (GmFilterOption item in items)
        {
            optionButton.AddItem(item.Text);
            keys.Add(item.Key);
        }

        FilterOptionKeys[optionButton] = keys;

        // 尝试恢复之前选中的 key；找不到时回退到第 0 项。
        int restoredIndex = 0;
        if (!string.IsNullOrEmpty(previousKey))
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (string.Equals(keys[i], previousKey, StringComparison.OrdinalIgnoreCase))
                {
                    restoredIndex = i;
                    break;
                }
            }
        }

        optionButton.Select(restoredIndex);
    }
}
