using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.Services;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 能力图鉴模块分文件：能力按钮、模式（赋予/删除/清除）切换、目标选择、左右键调整层数等。
/// 内容来自 GmUiManager.cs 的能力相关方法段，行为与原实现一致，只做物理拆分。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 创建能力图鉴专属控制行，包含模式、目标和怪物目标选择。
    /// </summary>
    private static Control BuildPowerControlRow()
    {
        _powerControl = new VBoxContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(0, 94),
        };

        HBoxContainer topRow = new()
        {
            CustomMinimumSize = new Vector2(0, 44),
        };

        Label modeLabel = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(70, 0),
        };
        modeLabel.AddThemeColorOverride("font_color", FieldLabelColor);
        BindLabelText(modeLabel, () => GmUiText.PowerModeLabel);
        topRow.AddChild(modeLabel);

        _powerModeSelect = new OptionButton
        {
            CustomMinimumSize = new Vector2(220, 40),
        };
        ApplySelectVisualStyle(_powerModeSelect);
        BindOptionItems(_powerModeSelect, () => [GmUiText.PowerModeGrant, GmUiText.PowerModeClear, GmUiText.PowerModeWipe], 0);
        _powerModeSelect.ItemSelected += OnPowerModeSelected;
        topRow.AddChild(_powerModeSelect);

        Label targetLabel = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(70, 0),
        };
        targetLabel.AddThemeColorOverride("font_color", FieldLabelColor);
        BindLabelText(targetLabel, () => GmUiText.PowerTargetLabel);
        topRow.AddChild(targetLabel);

        _powerTargetSelect = new OptionButton
        {
            CustomMinimumSize = new Vector2(220, 40),
        };
        ApplySelectVisualStyle(_powerTargetSelect);
        BindOptionItems(_powerTargetSelect, () => [GmUiText.PowerTargetPlayer, GmUiText.PowerTargetMonster], 0);
        _powerTargetSelect.ItemSelected += OnPowerTargetSelected;
        topRow.AddChild(_powerTargetSelect);

        _powerAllMonstersToggle = new CheckBox
        {
            CustomMinimumSize = new Vector2(160, 40),
        };
        _powerAllMonstersToggle.AddThemeColorOverride("font_color", SecondaryActionTextColor);
        BindButtonText(_powerAllMonstersToggle, () => GmUiText.PowerTargetAllMonsters);
        _powerAllMonstersToggle.Toggled += OnPowerAllMonstersToggled;
        topRow.AddChild(_powerAllMonstersToggle);

        _powerControl.AddChild(topRow);

        _powerMonsterTargetsRow = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0, 44),
            Visible = false,
        };
        _powerMonsterTargetsRow.AddThemeConstantOverride("separation", 6);
        _powerControl.AddChild(_powerMonsterTargetsRow);

        return _powerControl;
    }

    /// <summary>
    /// 处理能力图鉴模式切换。下拉项顺序：0=赋予 / 1=删除 / 2=清除（一键清空）。
    /// 切换到清除模式时主动清空已选能力，避免上一个模式的选中状态干扰。
    /// </summary>
    private static void OnPowerModeSelected(long index)
    {
        _powerOperateMode = index switch
        {
            1 => PowerOperateMode.Clear,
            2 => PowerOperateMode.Wipe,
            _ => PowerOperateMode.Grant,
        };

        if (_powerOperateMode == PowerOperateMode.Wipe)
        {
            // 清除模式无须选择能力，避免遗留的"待删除"标记影响视觉。
            SelectedPowers.Clear();
        }

        UpdatePageTexts();
        RefreshCurrentPage();
    }

    /// <summary>
    /// 处理能力图鉴目标切换。
    /// </summary>
    private static void OnPowerTargetSelected(long index)
    {
        _powerTargetMode = index == 1 ? PowerTargetMode.Monsters : PowerTargetMode.Player;
        UpdatePowerMonsterTargetRow();
        UpdateConfirmButtonAvailability();
        UpdateSelectionStatus();
    }

    /// <summary>
    /// 处理能力图鉴“全部怪物”开关切换。
    /// </summary>
    private static void OnPowerAllMonstersToggled(bool enabled)
    {
        _powerApplyAllMonsters = enabled;
        UpdatePowerMonsterTargetRow();
        UpdateConfirmButtonAvailability();
        UpdateSelectionStatus();
    }

    /// <summary>
    /// 刷新能力图鉴怪物目标按钮行。
    /// </summary>
    private static void UpdatePowerMonsterTargetRow()
    {
        if (_powerMonsterTargetsRow == null || _powerAllMonstersToggle == null)
        {
            return;
        }

        bool showMonsterTargets = _currentPage == GmPageMode.PowerCatalog && _powerTargetMode == PowerTargetMode.Monsters;
        _powerMonsterTargetsRow.Visible = showMonsterTargets;
        _powerAllMonstersToggle.Visible = showMonsterTargets;
        if (!showMonsterTargets)
        {
            return;
        }

        IReadOnlyList<CreatureTargetInfo> targets = PowerService.GetMonsterTargetsInCombat();
        HashSet<string> validIds = targets.Select(target => target.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string id in SelectedPowerMonsterTargets.Keys.ToList())
        {
            if (!validIds.Contains(id))
            {
                SelectedPowerMonsterTargets.Remove(id);
            }
        }

        foreach (Node child in _powerMonsterTargetsRow.GetChildren())
        {
            child.QueueFree();
        }

        if (targets.Count == 0)
        {
            Label emptyLabel = new()
            {
                Text = GmUiText.PowerNotInCombat,
                VerticalAlignment = VerticalAlignment.Center,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            emptyLabel.AddThemeColorOverride("font_color", AuxiliaryTextColor);
            _powerMonsterTargetsRow.AddChild(emptyLabel);
            return;
        }

        Label titleLabel = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(96, 0),
            Text = GmUiText.PowerMonsterTargetLabel,
        };
        titleLabel.AddThemeColorOverride("font_color", FieldLabelColor);
        _powerMonsterTargetsRow.AddChild(titleLabel);

        foreach (CreatureTargetInfo target in targets)
        {
            Button button = new()
            {
                Text = target.Name,
                CustomMinimumSize = new Vector2(170, 38),
                FocusMode = Control.FocusModeEnum.None,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            };
            ApplySelectableItemCardStyle(button, SelectedPowerMonsterTargets.ContainsKey(target.Id), MonsterSelectedAccentColor, disabled: false);
            button.Disabled = _powerApplyAllMonsters;
            button.Pressed += () => OnPowerMonsterTargetPressed(target);
            _powerMonsterTargetsRow.AddChild(button);
        }
    }

    /// <summary>
    /// 处理能力图鉴怪物目标按钮点击：第一次选中，再次点击取消，支持多选。
    /// </summary>
    private static void OnPowerMonsterTargetPressed(CreatureTargetInfo target)
    {
        if (_powerApplyAllMonsters)
        {
            return;
        }

        if (!SelectedPowerMonsterTargets.Remove(target.Id))
        {
            SelectedPowerMonsterTargets[target.Id] = target;
        }

        UpdatePowerMonsterTargetRow();
        UpdateConfirmButtonAvailability();
        UpdateSelectionStatus();
    }

    /// <summary>
    /// 刷新能力图鉴页的网格内容。
    /// </summary>
    private static void RefreshPowerGrid()
    {
        if (_grid == null || _searchInput == null || _categoryFilter == null || _sourceFilter == null)
        {
            return;
        }

        IReadOnlyList<PowerDisplayInfo> powers = PowerService.GetPowers(
            _searchInput.Text,
            GetSelectedFilterKey(_categoryFilter),
            GetSelectedFilterKey(_sourceFilter));
        UpdateGridColumns();

        ClearGridChildren();

        foreach (PowerDisplayInfo power in powers)
        {
            _grid.AddChild(CreatePowerButton(power));
        }

        UpdatePowerMonsterTargetRow();
        UpdateSelectionStatus(powers.Count);
        UpdateConfirmButtonAvailability();
    }

    /// <summary>
    /// 创建单个能力按钮，只展示图标和名称，支持左键+1、Ctrl+左键+5、Shift+左键+10、右键-1 的层数选择。
    /// </summary>
    private static Button CreatePowerButton(PowerDisplayInfo power)
    {
        Button button = new()
        {
            CustomMinimumSize = new Vector2(178, 132),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            ClipContents = true,
        };

        VBoxContainer layout = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        layout.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        TextureRect iconRect = new()
        {
            Texture = power.Icon,
            CustomMinimumSize = new Vector2(ItemIconDisplaySize, ItemIconDisplaySize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        layout.AddChild(iconRect);

        Label nameLabel = new()
        {
            Text = BuildPowerButtonText(power),
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        nameLabel.Name = "PowerNameLabel";
        layout.AddChild(nameLabel);

        button.AddChild(layout);
        button.Modulate = GetPowerButtonColor(power);
        RefreshPowerButtonVisual(button, power);
        RegisterItemTooltip(button, BuildPowerTooltip(power));
        RegisterDragSelectableButton(
            button,
            () => SelectedPowers.ContainsKey(power.IdText),
            () => SelectPowerByDrag(power, button),
            () => UnselectPowerByDrag(power, button),
            (ctrlPressed, shiftPressed) => OnPowerButtonLeftPressed(power, button, ctrlPressed, shiftPressed),
            () => RefreshPowerButtonState(button, power),
            () => _powerOperateMode != PowerOperateMode.Wipe);
        button.GuiInput += inputEvent => OnPowerButtonRightInput(inputEvent, power, button);
        return button;
    }

    /// <summary>
    /// 处理能力按钮左键：赋予模式按修饰键增加层数，删除模式标记待删除，清除模式无须选择。
    /// </summary>
    private static void OnPowerButtonLeftPressed(PowerDisplayInfo power, Button button, bool ctrlPressed, bool shiftPressed)
    {
        if (_powerOperateMode == PowerOperateMode.Wipe)
        {
            // 清除模式不需要选择能力，给一次性提示，避免用户疑惑。
            ShowToast(GmUiText.PowerWipeHint);
            return;
        }

        if (_powerOperateMode == PowerOperateMode.Clear)
        {
            SelectedPowers[power.IdText] = new PowerSelectionInfo
            {
                Info = power,
                AmountDelta = 1,
            };
        }
        else
        {
            AdjustPowerSelection(power, GetPowerLeftClickDelta(ctrlPressed, shiftPressed));
        }

        RefreshPowerButtonState(button, power);
    }

    /// <summary>
    /// 计算能力图鉴左键点击增加的层数；Shift 优先于 Ctrl，避免同时按下时重复叠加。
    /// </summary>
    private static int GetPowerLeftClickDelta(bool ctrlPressed, bool shiftPressed)
    {
        if (shiftPressed)
        {
            return 10;
        }

        return ctrlPressed ? 5 : 1;
    }

    /// <summary>
    /// 处理能力按钮右键：赋予模式-1，删除模式取消标记，清除模式不响应。
    /// </summary>
    private static void OnPowerButtonRightInput(InputEvent inputEvent, PowerDisplayInfo power, Button button)
    {
        if (inputEvent is not InputEventMouseButton mouseButton ||
            !mouseButton.Pressed ||
            mouseButton.ButtonIndex != MouseButton.Right)
        {
            return;
        }

        if (_powerOperateMode == PowerOperateMode.Wipe)
        {
            return;
        }

        if (_powerOperateMode == PowerOperateMode.Clear)
        {
            SelectedPowers.Remove(power.IdText);
        }
        else
        {
            AdjustPowerSelection(power, -1);
        }

        RefreshPowerButtonState(button, power);
    }

    /// <summary>
    /// 拖拽批量选中能力；赋予模式只设置为 +1 层，删除模式标记为待删除。
    /// </summary>
    private static void SelectPowerByDrag(PowerDisplayInfo power, Button button)
    {
        if (_powerOperateMode == PowerOperateMode.Wipe)
        {
            return;
        }

        SelectedPowers[power.IdText] = new PowerSelectionInfo
        {
            Info = power,
            AmountDelta = 1,
        };
        RefreshPowerButtonState(button, power);
    }

    /// <summary>
    /// 拖拽批量取消能力选择。
    /// </summary>
    private static void UnselectPowerByDrag(PowerDisplayInfo power, Button button)
    {
        SelectedPowers.Remove(power.IdText);
        RefreshPowerButtonState(button, power);
    }

    /// <summary>
    /// 调整能力选择层数，层数归零时自动取消选择。
    /// </summary>
    private static void AdjustPowerSelection(PowerDisplayInfo power, int delta)
    {
        if (SelectedPowers.TryGetValue(power.IdText, out PowerSelectionInfo? selection))
        {
            selection.AmountDelta += delta;
            if (selection.AmountDelta == 0)
            {
                SelectedPowers.Remove(power.IdText);
            }
        }
        else if (delta != 0)
        {
            SelectedPowers[power.IdText] = new PowerSelectionInfo
            {
                Info = power,
                AmountDelta = delta,
            };
        }
    }

    /// <summary>
    /// 更新能力按钮内的名称标签文本。
    /// </summary>
    private static void UpdatePowerButtonText(Button button, PowerDisplayInfo power)
    {
        Label? label = button.FindChild("PowerNameLabel", true, false) as Label;
        if (label != null)
        {
            label.Text = BuildPowerButtonText(power);
        }
    }

    /// <summary>
    /// 刷新能力按钮选中态和文本，避免左右键后的显示不同步。
    /// </summary>
    private static void RefreshPowerButtonState(Button button, PowerDisplayInfo power)
    {
        UpdatePowerButtonText(button, power);
        button.Modulate = GetPowerButtonColor(power);
        RefreshPowerButtonVisual(button, power);
        UpdateSelectionStatus();
    }

    /// <summary>
    /// 根据当前页签显示或隐藏能力图鉴专属控制行。
    /// </summary>
    private static void UpdatePowerControlVisibility()
    {
        if (_powerControl == null || _powerModeSelect == null || _powerTargetSelect == null || _powerAllMonstersToggle == null)
        {
            return;
        }

        bool visible = _currentPage == GmPageMode.PowerCatalog;
        _powerControl.Visible = visible;
        if (!visible)
        {
            return;
        }

        _powerModeSelect.Select(_powerOperateMode switch
        {
            PowerOperateMode.Clear => 1,
            PowerOperateMode.Wipe => 2,
            _ => 0,
        });
        _powerTargetSelect.Select(_powerTargetMode == PowerTargetMode.Monsters ? 1 : 0);
        _powerAllMonstersToggle.SetPressedNoSignal(_powerApplyAllMonsters);
        UpdatePowerMonsterTargetRow();
    }

    /// <summary>
    /// 拼接能力按钮文本，赋予模式显示层数变化，清除模式显示待清除状态。
    /// </summary>
    private static string BuildPowerButtonText(PowerDisplayInfo power)
    {
        if (!SelectedPowers.TryGetValue(power.IdText, out PowerSelectionInfo? selection))
        {
            return power.Name;
        }

        if (_powerOperateMode == PowerOperateMode.Clear)
        {
            return $"{power.Name}\n{GmUiText.PowerMarkedForClear}";
        }

        return $"{power.Name}\n{GmUiText.PowerDeltaSelected(selection.AmountDelta)}";
    }

    /// <summary>
    /// 拼接能力按钮悬停提示。
    /// </summary>
    private static string BuildPowerTooltip(PowerDisplayInfo power)
    {
        return $"{power.Name}\n{power.Description}\n{power.TypeText}\n{power.Source}\nID: {power.IdText}";
    }

    /// <summary>
    /// 根据能力选择状态设置按钮颜色。
    /// </summary>
    private static Color GetPowerButtonColor(PowerDisplayInfo power)
    {
        return Colors.White;
    }
}
