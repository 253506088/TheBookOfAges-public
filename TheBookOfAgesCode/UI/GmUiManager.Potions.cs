using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.Services;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 药水模块分文件：药水按钮、药水栏位（Slot）的输入控件、增减/校验/恢复，以及刷新逻辑。
/// 内容来自 GmUiManager.cs 的药水相关方法段，行为与原实现一致，只做物理拆分。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 创建药水栏位控制行，默认隐藏，只在药水发放页签显示。
    /// </summary>
    private static Control BuildPotionSlotControlRow()
    {
        _potionSlotControl = new HBoxContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(0, 48),
        };

        Label titleLabel = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(150, 0),
        };
        titleLabel.AddThemeColorOverride("font_color", FieldLabelColor);
        BindLabelText(titleLabel, () => GmUiText.PotionSlotControl);
        _potionSlotControl.AddChild(titleLabel);

        Button decreaseBtn = new()
        {
            Text = "-",
            CustomMinimumSize = new Vector2(48, 38),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        ApplySecondaryActionButtonStyle(decreaseBtn);
        BindTooltip(decreaseBtn, () => GmUiText.PotionSlotDecreaseTip);
        decreaseBtn.Pressed += OnPotionSlotDecreasePressed;
        _potionSlotControl.AddChild(decreaseBtn);

        _potionSlotInput = new LineEdit
        {
            CustomMinimumSize = new Vector2(72, 38),
            SelectAllOnFocus = true,
        };
        _potionSlotInput.AddThemeColorOverride("font_color", FieldValueColor);
        _potionSlotInput.AddThemeStyleboxOverride("normal", CreateRoundedStyleBox(new Color(0.07f, 0.09f, 0.12f, 0.88f), new Color(0.28f, 0.35f, 0.42f, 0.92f), 8, 1, 8, 8));
        _potionSlotInput.AddThemeStyleboxOverride("focus", CreateRoundedStyleBox(new Color(0.11f, 0.16f, 0.22f, 0.94f), new Color(0.56f, 0.68f, 0.80f, 0.96f), 8, 1, 8, 8));
        _potionSlotInput.TextSubmitted += OnPotionSlotTextSubmitted;
        _potionSlotInput.FocusExited += OnPotionSlotFocusExited;
        _potionSlotControl.AddChild(_potionSlotInput);

        Button increaseBtn = new()
        {
            Text = "+",
            CustomMinimumSize = new Vector2(48, 38),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        ApplySecondaryActionButtonStyle(increaseBtn);
        BindTooltip(increaseBtn, () => GmUiText.PotionSlotIncreaseTip);
        increaseBtn.Pressed += OnPotionSlotIncreasePressed;
        _potionSlotControl.AddChild(increaseBtn);

        _potionSlotFilledLabel = new Label
        {
            VerticalAlignment = VerticalAlignment.Center,
        };
        _potionSlotFilledLabel.AddThemeColorOverride("font_color", AuxiliaryTextColor);
        _potionSlotControl.AddChild(_potionSlotFilledLabel);

        return _potionSlotControl;
    }

    /// <summary>
    /// 刷新药水发放页的网格内容。
    /// </summary>
    private static void RefreshPotionGrid()
    {
        if (_grid == null || _searchInput == null || _categoryFilter == null || _rarityFilter == null || _sourceFilter == null)
        {
            return;
        }

        IReadOnlyList<PotionDisplayInfo> potions = PotionService.GetPotions(
            _searchInput.Text,
            GetSelectedFilterKey(_categoryFilter),
            GetSelectedFilterKey(_rarityFilter),
            GetSelectedFilterKey(_sourceFilter));
        UpdateGridColumns();

        ClearGridChildren();

        foreach (PotionDisplayInfo potion in potions)
        {
            _grid.AddChild(CreatePotionButton(potion));
        }

        UpdateSelectionStatus(potions.Count);
    }

    /// <summary>
    /// 创建单个药水按钮，只展示图标和名称，不展示游戏内部 ID。
    /// 使用 TextureRect + Label 组合布局取代 Button.Icon，避免超大药水贴图撑爆网格。
    /// </summary>
    private static Button CreatePotionButton(PotionDisplayInfo potion)
    {
        Button button = new()
        {
            CustomMinimumSize = new Vector2(178, 110),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            ClipContents = true,
        };

        // 用固定尺寸图标和文字标签组合，避免原始药水贴图尺寸参与按钮布局
        VBoxContainer layout = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        layout.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        TextureRect iconRect = new()
        {
            Texture = potion.Icon,
            CustomMinimumSize = new Vector2(ItemIconDisplaySize, ItemIconDisplaySize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        layout.AddChild(iconRect);

        Label nameLabel = new()
        {
            Text = BuildPotionButtonText(potion),
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        nameLabel.Name = "PotionNameLabel";
        layout.AddChild(nameLabel);

        button.AddChild(layout);
        PanelContainer countBadge = CreatePotionCountBadge();
        button.AddChild(countBadge);
        button.Modulate = GetPotionButtonColor(potion);
        RefreshPotionButtonVisual(button, potion);
        SyncPotionCountBadge(button, potion);
        RegisterItemTooltip(button, $"{potion.Name}\n{potion.Description}\n{potion.Rarity}\n{potion.Category}\n{potion.Source}");
        RegisterDragSelectableButton(
            button,
            () => SelectedGrantPotions.ContainsKey(potion.IdText),
            () => SelectPotionByDrag(potion, button),
            () => UnselectPotionByDrag(potion, button),
            _ => OnPotionButtonPressed(potion, button),
            () => RefreshPotionSelectionVisual(button, potion));
        return button;
    }

    /// <summary>
    /// 处理药水按钮点击：每次点击会让该药水选择数量加一。
    /// </summary>
    private static void OnPotionButtonPressed(PotionDisplayInfo potion, Button button)
    {
        if (SelectedGrantPotions.TryGetValue(potion.IdText, out PotionSelectionInfo? selection))
        {
            selection.Count++;
        }
        else
        {
            SelectedGrantPotions[potion.IdText] = new PotionSelectionInfo
            {
                Info = potion,
                Count = 1,
            };
        }

        RefreshPotionSelectionVisual(button, potion);
        UpdateSelectionStatus();
    }

    /// <summary>
    /// 拖拽批量选中药水；只保证数量为 1，不重复累加，避免一拖发太多。
    /// </summary>
    private static void SelectPotionByDrag(PotionDisplayInfo potion, Button button)
    {
        if (!SelectedGrantPotions.ContainsKey(potion.IdText))
        {
            SelectedGrantPotions[potion.IdText] = new PotionSelectionInfo
            {
                Info = potion,
                Count = 1,
            };
        }

        RefreshPotionSelectionVisual(button, potion);
    }

    /// <summary>
    /// 拖拽批量取消药水选择，相当于把该药水数量清零。
    /// </summary>
    private static void UnselectPotionByDrag(PotionDisplayInfo potion, Button button)
    {
        SelectedGrantPotions.Remove(potion.IdText);
        RefreshPotionSelectionVisual(button, potion);
    }

    /// <summary>
    /// 同步药水按钮文本、颜色和外框。
    /// </summary>
    private static void RefreshPotionSelectionVisual(Button button, PotionDisplayInfo potion)
    {
        SyncPotionCountBadge(button, potion);
        UpdatePotionButtonText(button, potion);
        button.Modulate = GetPotionButtonColor(potion);
        RefreshPotionButtonVisual(button, potion);
    }

    /// <summary>
    /// 创建药水按钮右上角数量徽标，用来显示同一种药水累计选择次数。
    /// </summary>
    private static PanelContainer CreatePotionCountBadge()
    {
        PanelContainer badge = new()
        {
            Name = "PotionCountBadge",
            CustomMinimumSize = new Vector2(CardCountBadgeSize, CardCountBadgeSize),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            ZIndex = 10,
        };
        badge.AnchorLeft = 1f;
        badge.AnchorTop = 0f;
        badge.AnchorRight = 1f;
        badge.AnchorBottom = 0f;
        badge.OffsetLeft = -CardCountBadgeSize - CardCountBadgeMargin;
        badge.OffsetTop = CardCountBadgeMargin;
        badge.OffsetRight = -CardCountBadgeMargin;
        badge.OffsetBottom = CardCountBadgeSize + CardCountBadgeMargin;
        badge.AddThemeStyleboxOverride("panel",
            CreateRoundedStyleBox(new Color(0.05f, 0.07f, 0.10f, 0.92f),
                                  GrantSelectedAccentColor,
                                  6, 2, 0, 0));

        Label label = new()
        {
            Name = "PotionCountLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", new Color(0.90f, 1f, 0.92f, 1f));
        label.AddThemeFontSizeOverride("font_size", 22);
        badge.AddChild(label);

        return badge;
    }

    /// <summary>
    /// 把当前药水的已选数量同步到按钮右上角徽标；数量为 0 时隐藏徽标。
    /// </summary>
    private static void SyncPotionCountBadge(Button button, PotionDisplayInfo potion)
    {
        PanelContainer? badge = button.GetNodeOrNull<PanelContainer>("PotionCountBadge");
        if (badge == null)
        {
            return;
        }

        Label? label = badge.GetNodeOrNull<Label>("PotionCountLabel");
        if (SelectedGrantPotions.TryGetValue(potion.IdText, out PotionSelectionInfo? selection) && selection.Count > 0)
        {
            if (label != null)
            {
                label.Text = $"x{selection.Count}";
            }

            badge.Visible = true;
        }
        else
        {
            if (label != null)
            {
                label.Text = string.Empty;
            }

            badge.Visible = false;
        }
    }

    /// <summary>
    /// 更新药水按钮内的名称标签文本，支持显示同一种药水的重复选择数量。
    /// </summary>
    private static void UpdatePotionButtonText(Button button, PotionDisplayInfo potion)
    {
        Label? label = button.GetNodeOrNull<Label>("VBoxContainer/PotionNameLabel");
        if (label != null)
        {
            label.Text = BuildPotionButtonText(potion);
        }
    }

    /// <summary>
    /// 刷新药水栏位控制显示，读取当前玩家的最大栏位和已放入数量。
    /// </summary>
    private static void RefreshPotionSlotControl()
    {
        if (_potionSlotInput == null || _potionSlotFilledLabel == null)
        {
            MainFile.Logger.Info("刷新药水栏位控件跳过：输入框或已占用标签为空。");
            return;
        }

        int maxSlots = PotionService.GetMaxPotionSlots();
        int filled = PotionService.GetFilledPotionSlots();
        _potionSlotPendingValue = maxSlots;
        _potionSlotInput.Text = maxSlots.ToString();
        _potionSlotFilledLabel.Text = GmUiText.PotionSlotsFilled(filled);
        MainFile.Logger.Info($"刷新药水栏位控件完成：最大栏位={maxSlots}，已占用={filled}。");
    }

    /// <summary>
    /// 根据当前页签显示或隐藏药水栏位控制行。
    /// </summary>
    private static void UpdatePotionSlotControlVisibility()
    {
        if (_potionSlotControl == null)
        {
            return;
        }

        _potionSlotControl.Visible = _currentPage == GmPageMode.GrantPotions;
        if (_potionSlotControl.Visible)
        {
            RefreshPotionSlotControl();
        }
    }

    private static void OnPotionSlotDecreasePressed()
    {
        if (!TryReadPotionSlotInput(out int current))
        {
            MainFile.Logger.Info($"减少药水栏位失败：输入值无法解析，当前文本={_potionSlotInput?.Text ?? "空"}。");
            return;
        }

        MainFile.Logger.Info($"点击减少药水栏位：当前输入={current}，目标输入={current - 1}。");
        ApplyPotionSlotValue(current - 1);
    }

    private static void OnPotionSlotIncreasePressed()
    {
        if (!TryReadPotionSlotInput(out int current))
        {
            MainFile.Logger.Info($"增加药水栏位失败：输入值无法解析，当前文本={_potionSlotInput?.Text ?? "空"}。");
            return;
        }

        MainFile.Logger.Info($"点击增加药水栏位：当前输入={current}，目标输入={current + 1}。");
        ApplyPotionSlotValue(current + 1);
    }

    private static void OnPotionSlotTextSubmitted(string text)
    {
        if (int.TryParse(text, out int value))
        {
            MainFile.Logger.Info($"提交药水栏位输入：原始文本={text}，目标栏位={value}。");
            ApplyPotionSlotValue(value);
        }
        else
        {
            MainFile.Logger.Info($"提交药水栏位输入失败：无法解析文本={text}。");
            RevertPotionSlotInput();
        }
    }

    private static void OnPotionSlotFocusExited()
    {
        if (_potionSlotInput == null)
        {
            return;
        }

        if (!int.TryParse(_potionSlotInput.Text, out int _))
        {
            MainFile.Logger.Info($"药水栏位输入框失焦恢复：无法解析文本={_potionSlotInput.Text}。");
            RevertPotionSlotInput();
        }
    }

    private static bool TryReadPotionSlotInput(out int value)
    {
        value = 0;
        bool success = _potionSlotInput != null && int.TryParse(_potionSlotInput.Text, out value);
        MainFile.Logger.Info($"读取药水栏位输入：成功={success}，文本={_potionSlotInput?.Text ?? "空"}，数值={value}。");
        return success;
    }

    private static void ApplyPotionSlotValue(int value)
    {
        MainFile.Logger.Info($"开始应用药水栏位数量：目标栏位={value}。");
        if (PotionService.TrySetMaxPotionSlots(value, out string? error))
        {
            MainFile.Logger.Info($"应用药水栏位数量成功：目标栏位={value}。");
            RefreshPotionSlotControl();
        }
        else
        {
            MainFile.Logger.Info($"应用药水栏位数量失败：目标栏位={value}，错误={error}。");
            ShowToast(error);
            RevertPotionSlotInput();
        }
    }

    private static void RevertPotionSlotInput()
    {
        if (_potionSlotInput != null)
        {
            _potionSlotInput.Text = _potionSlotPendingValue.ToString();
            MainFile.Logger.Info($"恢复药水栏位输入框：恢复值={_potionSlotPendingValue}。");
        }
    }

    /// <summary>
    /// 拼接药水按钮文本，支持显示同一种药水的重复选择数量。
    /// </summary>
    private static string BuildPotionButtonText(PotionDisplayInfo potion)
    {
        if (SelectedGrantPotions.TryGetValue(potion.IdText, out PotionSelectionInfo? selection))
        {
            return GmUiText.Pick($"{potion.Name}\n已选 x{selection.Count}", $"{potion.Name}\nSelected x{selection.Count}");
        }

        return potion.Name;
    }

    /// <summary>
    /// 根据药水选择状态设置按钮颜色。
    /// </summary>
    private static Color GetPotionButtonColor(PotionDisplayInfo potion)
    {
        return Colors.White;
    }
}
