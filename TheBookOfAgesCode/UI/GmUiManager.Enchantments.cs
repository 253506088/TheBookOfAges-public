using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using TheBookOfAges.TheBookOfAgesCode.Models;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 卡片附魔分页：负责附魔选择、可附魔卡片选择，以及第二步的数值输入与安全模式控制。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 创建附魔页第二阶段的当前附魔、手填数值和安全模式控制区。
    /// </summary>
    private static Control BuildEnchantmentControlRow()
    {
        _enchantmentControl = new VBoxContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(0, 78),
        };
        _enchantmentControl.AddThemeConstantOverride("separation", 6);

        HBoxContainer topRow = new()
        {
            CustomMinimumSize = new Vector2(0, 40),
        };
        topRow.AddThemeConstantOverride("separation", 8);
        _enchantmentControl.AddChild(topRow);

        _enchantmentSelectedLabel = new Label
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _enchantmentSelectedLabel.AddThemeColorOverride("font_color", FieldValueColor);
        topRow.AddChild(_enchantmentSelectedLabel);

        _enchantmentHintLabel = new Label
        {
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _enchantmentHintLabel.AddThemeColorOverride("font_color", AuxiliaryTextColor);
        _enchantmentControl.AddChild(_enchantmentHintLabel);

        ResetEnchantmentAmountControls(true);
        return _enchantmentControl;
    }

    /// <summary>
    /// 重置附魔页状态，进入页签时回到第一步，并恢复默认数值与安全模式。
    /// </summary>
    private static void ResetEnchantmentPageState()
    {
        _selectedCardEnchantment = null;
        _enchantmentPageStep = EnchantmentPageStep.SelectEnchantment;
        SelectedEnchantCards.Clear();
        ResetEnchantmentAmountControls(true);
    }

    /// <summary>
    /// 刷新附魔页；根据当前步骤展示附魔列表或可附魔牌组卡片。
    /// </summary>
    private static void RefreshEnchantmentPage()
    {
        if (_grid == null || _searchInput == null || _sourceFilter == null)
        {
            return;
        }

        UpdateEnchantmentControlVisibility();
        if (_enchantmentPageStep == EnchantmentPageStep.SelectCards)
        {
            RefreshEnchantCardGrid();
            return;
        }

        RefreshEnchantmentGrid();
    }

    /// <summary>
    /// 刷新第一步附魔列表。
    /// </summary>
    private static void RefreshEnchantmentGrid()
    {
        if (_grid == null || _searchInput == null || _sourceFilter == null)
        {
            return;
        }

        UpdateGridColumns();
        IReadOnlyList<EnchantmentDisplayInfo> enchantments = EnchantmentService.GetEnchantments(
            _searchInput.Text,
            GetSelectedFilterKey(_sourceFilter));

        ClearGridChildren();
        foreach (EnchantmentDisplayInfo enchantment in enchantments)
        {
            _grid.AddChild(CreateEnchantmentButton(enchantment));
        }

        UpdateSelectionStatus(enchantments.Count);
        UpdateConfirmButtonAvailability();
    }

    /// <summary>
    /// 刷新第二步可附魔卡片列表。
    /// </summary>
    private static void RefreshEnchantCardGrid()
    {
        if (_grid == null || _selectedCardEnchantment == null)
        {
            return;
        }

        UpdateGridColumns();
        IReadOnlyList<CardDisplayInfo> cards = GetCurrentVisibleEnchantCards();
        HashSet<CardModel> currentDeck = cards.Select(info => info.Card).ToHashSet();
        foreach (CardModel staleCard in SelectedEnchantCards.Keys.ToList())
        {
            if (!currentDeck.Contains(staleCard))
            {
                SelectedEnchantCards.Remove(staleCard);
            }
        }

        ClearGridChildren();
        _cardRenderDebugCounter = 0;
        foreach (CardDisplayInfo card in cards)
        {
            _grid.AddChild(CreateEnchantCardButton(card));
        }

        UpdateSelectionStatus(cards.Count);
        UpdateConfirmButtonAvailability();
    }

    /// <summary>
    /// 获取当前附魔、搜索和筛选后的可附魔牌组卡片。
    /// </summary>
    private static IReadOnlyList<CardDisplayInfo> GetCurrentVisibleEnchantCards()
    {
        if (_selectedCardEnchantment == null || _searchInput == null || _categoryFilter == null)
        {
            return [];
        }

        return EnchantmentService.GetEnchantableDeckCards(
            _selectedCardEnchantment,
            _searchInput.Text,
            GetSelectedFilterKey(_categoryFilter));
    }

    /// <summary>
    /// 创建第一步的附魔按钮。
    /// </summary>
    private static Button CreateEnchantmentButton(EnchantmentDisplayInfo enchantment)
    {
        Button button = new()
        {
            CustomMinimumSize = new Vector2(RelicButtonWidth, 150),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };

        VBoxContainer layout = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        layout.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layout.AddThemeConstantOverride("separation", 6);

        TextureRect icon = new()
        {
            Texture = enchantment.Icon,
            CustomMinimumSize = new Vector2(ItemIconDisplaySize, ItemIconDisplaySize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        layout.AddChild(icon);

        Label name = new()
        {
            Text = enchantment.Name,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        layout.AddChild(name);

        button.AddChild(layout);
        RefreshEnchantmentButtonVisual(button, enchantment);
        RegisterItemTooltip(button, BuildEnchantmentTooltip(enchantment));
        button.Pressed += () => SelectEnchantment(enchantment);
        return button;
    }

    /// <summary>
    /// 创建第二步的卡片按钮。
    /// </summary>
    private static Button CreateEnchantCardButton(CardDisplayInfo card)
    {
        int slotIndex = _cardRenderDebugCounter++;
        bool verbose = slotIndex < CardRenderDebugLimit;

        Button button = new()
        {
            CustomMinimumSize = new Vector2(CardButtonWidth, CardButtonHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            ClipContents = false,
        };

        Control? cardVisual = TryCreateCardVisual(card, showUpgraded: false, slotIndex, verbose);
        if (cardVisual != null)
        {
            button.AddChild(cardVisual);
        }
        else
        {
            CenterContainer fallbackSlot = new()
            {
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            fallbackSlot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            TextureRect fallback = new()
            {
                Texture = card.Icon,
                CustomMinimumSize = new Vector2(ItemIconDisplaySize, ItemIconDisplaySize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            fallbackSlot.AddChild(fallback);
            button.AddChild(fallbackSlot);
        }

        RefreshEnchantCardButtonVisual(button, card);
        RegisterItemTooltip(button, BuildCardTooltip(card));
        RegisterDragSelectableButton(
            button,
            () => SelectedEnchantCards.ContainsKey(card.Card),
            () => SelectEnchantCardByDrag(card, button),
            () => UnselectEnchantCardByDrag(card, button),
            _ => ToggleEnchantCardSelection(card, button),
            () => RefreshEnchantCardButtonVisual(button, card));
        return button;
    }

    /// <summary>
    /// 选择一个附魔并进入第二步。
    /// </summary>
    private static void SelectEnchantment(EnchantmentDisplayInfo enchantment)
    {
        SaveCurrentFilterState();
        _selectedCardEnchantment = enchantment;
        _enchantmentPageStep = EnchantmentPageStep.SelectCards;
        SelectedEnchantCards.Clear();
        ResetEnchantmentAmountControls(false);
        UpdateFilterOptions();
        ApplyCurrentFilterState();
        ApplyCurrentPageState();
        RefreshCurrentPage();
    }

    /// <summary>
    /// 返回第一步重新选择附魔。
    /// </summary>
    private static void BackToEnchantmentSelection()
    {
        SaveCurrentFilterState();
        _selectedCardEnchantment = null;
        _enchantmentPageStep = EnchantmentPageStep.SelectEnchantment;
        SelectedEnchantCards.Clear();
        ResetEnchantmentAmountControls(false);
        UpdateFilterOptions();
        ApplyCurrentFilterState();
        ApplyCurrentPageState();
        RefreshCurrentPage();
    }

    /// <summary>
    /// 附魔页进入选卡步骤时重置搜索和筛选。
    /// </summary>
    private static void ResetToolbarForEnchantmentCardStep()
    {
        if (_searchInput != null)
        {
            _searchInput.Text = "";
        }

        _categoryFilter?.Select(0);
        _rarityFilter?.Select(0);
        _sourceFilter?.Select(0);
    }

    /// <summary>
    /// 附魔页返回选附魔步骤时重置搜索和筛选。
    /// </summary>
    private static void ResetToolbarForEnchantmentSelectionStep()
    {
        if (_searchInput != null)
        {
            _searchInput.Text = "";
        }

        _categoryFilter?.Select(0);
        _rarityFilter?.Select(0);
        _sourceFilter?.Select(0);
    }

    /// <summary>
    /// 切换附魔选卡页单张卡片的待附魔状态。
    /// </summary>
    private static void ToggleEnchantCardSelection(CardDisplayInfo card, Button button)
    {
        if (SelectedEnchantCards.Remove(card.Card))
        {
            // 已选 -> 取消
        }
        else
        {
            SelectedEnchantCards[card.Card] = card;
        }

        RefreshEnchantCardButtonVisual(button, card);
        UpdateSelectionStatus(_grid?.GetChildCount());
    }

    /// <summary>
    /// 拖拽批量选中附魔页卡片。
    /// </summary>
    private static void SelectEnchantCardByDrag(CardDisplayInfo card, Button button)
    {
        SelectedEnchantCards[card.Card] = card;
        RefreshEnchantCardButtonVisual(button, card);
    }

    /// <summary>
    /// 拖拽批量取消附魔页卡片选择。
    /// </summary>
    private static void UnselectEnchantCardByDrag(CardDisplayInfo card, Button button)
    {
        SelectedEnchantCards.Remove(card.Card);
        RefreshEnchantCardButtonVisual(button, card);
    }

    /// <summary>
    /// 刷新附魔按钮外框。
    /// </summary>
    private static void RefreshEnchantmentButtonVisual(Button button, EnchantmentDisplayInfo enchantment)
    {
        bool selected = _selectedCardEnchantment != null && string.Equals(_selectedCardEnchantment.IdText, enchantment.IdText, StringComparison.OrdinalIgnoreCase);
        ApplySelectableItemCardStyle(button, selected, CardSelectedAccentColor, disabled: false);
    }

    /// <summary>
    /// 刷新附魔选卡按钮外框。
    /// </summary>
    private static void RefreshEnchantCardButtonVisual(Button button, CardDisplayInfo card)
    {
        ApplySelectableItemCardStyle(button, SelectedEnchantCards.ContainsKey(card.Card), CardSelectedAccentColor, disabled: false);
    }

    /// <summary>
    /// 根据当前页签和步骤显示或隐藏附魔页专属控制行。
    /// </summary>
    private static void UpdateEnchantmentControlVisibility()
    {
        bool visible = _currentPage == GmPageMode.EnchantCards && _enchantmentPageStep == EnchantmentPageStep.SelectCards;
        if (_enchantmentBackButton != null)
        {
            _enchantmentBackButton.Visible = visible;
        }

        if (_enchantmentControl == null || _enchantmentSelectedLabel == null || _enchantmentHintLabel == null)
        {
            return;
        }

        _enchantmentControl.Visible = false;
        if (_enchantmentFooterControl != null)
        {
            _enchantmentFooterControl.Visible = false;
        }
        if (!visible)
        {
            return;
        }

        _enchantmentControl.Visible = true;
        if (_enchantmentFooterControl != null)
        {
            _enchantmentFooterControl.Visible = true;
        }
        _enchantmentSelectedLabel.Text = _selectedCardEnchantment == null
            ? GmUiText.Pick("当前附魔：未选择", "Current enchantment: none")
            : GmUiText.Pick($"当前附魔：{_selectedCardEnchantment.Name}", $"Current enchantment: {_selectedCardEnchantment.Name}");
        if (_enchantmentAmountInput != null)
        {
            _enchantmentAmountInput.PlaceholderText = GmUiText.CardEnchantAmountPlaceholder;
        }

        bool showUnsafeToggle = ShouldShowEnchantmentUnsafeToggle();
        if (_enchantmentSafetyModeCheckbox != null)
        {
            _enchantmentSafetyModeCheckbox.Visible = showUnsafeToggle;
        }

        bool showAmountEditor = ShouldShowEnchantmentAmountEditor();
        if (_enchantmentAmountLabel != null)
        {
            _enchantmentAmountLabel.Visible = showAmountEditor;
        }

        if (_enchantmentAmountInput != null)
        {
            _enchantmentAmountInput.Visible = showAmountEditor;
        }

        string hintText = BuildEnchantmentControlHint();
        _enchantmentHintLabel.Visible = !string.IsNullOrWhiteSpace(hintText);
        _enchantmentHintLabel.Text = hintText;
        _enchantmentHintLabel.AddThemeColorOverride("font_color", GetEnchantmentHintColor());
        UpdateConfirmButtonAvailability();
    }

    /// <summary>
    /// 附魔悬浮 tooltip 内容。
    /// </summary>
    private static string BuildEnchantmentTooltip(EnchantmentDisplayInfo enchantment)
    {
        if (string.IsNullOrWhiteSpace(enchantment.CustomAmountHint))
        {
            return $"{enchantment.Name}\n{enchantment.Description}\n{enchantment.Source}";
        }

        return $"{enchantment.Name}\n{enchantment.Description}\n{enchantment.Source}\n{enchantment.CustomAmountHint}";
    }

    /// <summary>
    /// 重置附魔数值输入框与安全模式开关。
    /// </summary>
    private static void ResetEnchantmentAmountControls(bool resetSafetyMode)
    {
        if (_enchantmentAmountInput != null)
        {
            _enchantmentAmountInput.Text = EnchantmentCustomAmountMin.ToString();
        }

        if (resetSafetyMode && _enchantmentSafetyModeCheckbox != null)
        {
            _enchantmentSafetyModeCheckbox.SetPressedNoSignal(true);
        }
    }

    /// <summary>
    /// 输入过程中只保留数字，避免第二步出现非法字符。
    /// </summary>
    private static void OnEnchantmentAmountTextChanged(string text)
    {
        if (_enchantmentAmountInput == null)
        {
            return;
        }

        string sanitized = BuildDigitsOnlyText(text);
        if (!string.Equals(sanitized, text, StringComparison.Ordinal))
        {
            _enchantmentAmountInput.Text = sanitized;
            _enchantmentAmountInput.CaretColumn = sanitized.Length;
        }

        UpdateSelectionStatus(_grid?.GetChildCount());
        UpdateConfirmButtonAvailability();
    }

    /// <summary>
    /// 手填数值回车时做范围归一化，统一限制到 1~99。
    /// </summary>
    private static void OnEnchantmentAmountTextSubmitted(string text)
    {
        NormalizeEnchantmentAmountInput(text);
    }

    /// <summary>
    /// 手填数值输入框失焦时做范围归一化，避免留下空值。
    /// </summary>
    private static void OnEnchantmentAmountFocusExited()
    {
        NormalizeEnchantmentAmountInput(_enchantmentAmountInput?.Text);
    }

    /// <summary>
    /// 切换附魔安全模式时同步控制区和确认按钮状态。
    /// </summary>
    private static void OnEnchantmentSafetyModeToggled(bool enabled)
    {
        MainFile.Logger.Info($"附魔安全模式切换：已开启={enabled}。");
        UpdateEnchantmentControlVisibility();
        UpdateSelectionStatus(_grid?.GetChildCount());
    }

    /// <summary>
    /// 把手填数值规范到允许范围内，空值或非法值回退为最小值。
    /// </summary>
    private static void NormalizeEnchantmentAmountInput(string? text)
    {
        if (_enchantmentAmountInput == null)
        {
            return;
        }

        if (!int.TryParse(text, out int amount))
        {
            amount = EnchantmentCustomAmountMin;
        }

        amount = Math.Clamp(amount, EnchantmentCustomAmountMin, EnchantmentCustomAmountMax);
        _enchantmentAmountInput.Text = amount.ToString();
        UpdateSelectionStatus(_grid?.GetChildCount());
        UpdateConfirmButtonAvailability();
    }

    /// <summary>
    /// 读取当前附魔手填数值；如果当前附魔不开放输入框，则按默认值 1 处理。
    /// </summary>
    private static bool TryGetCurrentEnchantmentAmount(out int amount)
    {
        amount = EnchantmentCustomAmountMin;
        if (!ShouldShowEnchantmentAmountEditor())
        {
            return true;
        }

        return _enchantmentAmountInput != null &&
               int.TryParse(_enchantmentAmountInput.Text, out amount) &&
               amount >= EnchantmentCustomAmountMin &&
               amount <= EnchantmentCustomAmountMax;
    }

    /// <summary>
    /// 获取当前附魔手填数值；解析失败时兜底为最小值。
    /// </summary>
    private static int GetCurrentEnchantmentAmountOrDefault()
    {
        return TryGetCurrentEnchantmentAmount(out int amount) ? amount : EnchantmentCustomAmountMin;
    }

    /// <summary>
    /// 判断当前附魔页的手填数值是否合法。
    /// </summary>
    private static bool IsCurrentEnchantmentAmountValid()
    {
        return TryGetCurrentEnchantmentAmount(out _);
    }

    /// <summary>
    /// 当前附魔是否需要显示安全模式开关。
    /// </summary>
    private static bool ShouldShowEnchantmentUnsafeToggle()
    {
        return _selectedCardEnchantment?.CanCustomizeAmountWhenUnsafe ?? false;
    }

    /// <summary>
    /// 当前附魔是否应该显示数值输入框。
    /// </summary>
    private static bool ShouldShowEnchantmentAmountEditor()
    {
        if (_selectedCardEnchantment == null)
        {
            return false;
        }

        if (_selectedCardEnchantment.CanCustomizeAmountSafely)
        {
            return true;
        }

        return _selectedCardEnchantment.CanCustomizeAmountWhenUnsafe &&
               !(_enchantmentSafetyModeCheckbox?.ButtonPressed ?? true);
    }

    /// <summary>
    /// 生成附魔控制区提示文案。
    /// </summary>
    private static string BuildEnchantmentControlHint()
    {
        if (_selectedCardEnchantment == null)
        {
            return "";
        }

        if (_selectedCardEnchantment.CanCustomizeAmountSafely)
        {
            return _selectedCardEnchantment.CustomAmountHint;
        }

        if (!_selectedCardEnchantment.CanCustomizeAmountWhenUnsafe)
        {
            return _selectedCardEnchantment.CustomAmountHint;
        }

        bool safeModeEnabled = _enchantmentSafetyModeCheckbox?.ButtonPressed ?? true;
        return safeModeEnabled
            ? _selectedCardEnchantment.CustomAmountHint
            : GmUiText.CardEnchantUnsafeOverrideActiveHint;
    }

    /// <summary>
    /// 根据当前提示类型选择文案颜色。
    /// </summary>
    private static Color GetEnchantmentHintColor()
    {
        if (_selectedCardEnchantment == null)
        {
            return AuxiliaryTextColor;
        }

        if (_selectedCardEnchantment.CanCustomizeAmountSafely)
        {
            return AuxiliaryTextColor;
        }

        if (_selectedCardEnchantment.CanCustomizeAmountWhenUnsafe)
        {
            return DangerActionTextColor;
        }

        return AuxiliaryTextColor;
    }

    /// <summary>
    /// 从原始输入中构造只包含数字的文本。
    /// </summary>
    private static string BuildDigitsOnlyText(string text)
    {
        List<char> chars = [];
        foreach (char ch in text)
        {
            if (ch >= '0' && ch <= '9')
            {
                chars.Add(ch);
            }
        }

        return new string(chars.ToArray());
    }
}
