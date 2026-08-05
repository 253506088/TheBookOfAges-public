using Godot;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.Services;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 荒疫模块分文件：荒疫按钮、搜索筛选、添加/移除模式和刷新逻辑。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 刷新荒疫页的网格内容。
    /// </summary>
    private static void RefreshBlightGrid()
    {
        if (_grid == null || _searchInput == null || _categoryFilter == null || _sourceFilter == null)
        {
            return;
        }

        IReadOnlyList<BlightDisplayInfo> blights = GetCurrentPageBlights();
        _grid.Columns = 1;

        ClearGridChildren();

        foreach (BlightDisplayInfo blight in blights)
        {
            _grid.AddChild(CreateBlightButton(blight));
        }

        UpdateSelectionStatus(blights.Count);
    }

    /// <summary>
    /// 创建荒疫页开关说明行。
    /// </summary>
    private static Control BuildBlightControlRow()
    {
        _blightControl = new HBoxContainer
        {
            Visible = false,
            CustomMinimumSize = new Vector2(0, 44),
        };

        Label label = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeColorOverride("font_color", FieldLabelColor);
        BindLabelText(label, () => GmUiText.Pick("荒疫开关", "Blight Toggles"));
        _blightControl.AddChild(label);

        Label hint = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        hint.AddThemeColorOverride("font_color", AuxiliaryTextColor);
        BindLabelText(hint, () => GmUiText.Pick("点击条目立即开启，再次点击立即关闭。", "Click an item to enable it; click again to disable it."));
        _blightControl.AddChild(hint);

        return _blightControl;
    }

    /// <summary>
    /// 根据当前页签显示或隐藏荒疫专属控制行。
    /// </summary>
    private static void UpdateBlightControlVisibility()
    {
        if (_blightControl == null)
        {
            return;
        }

        _blightControl.Visible = _currentPage == GmPageMode.BlightCatalog;
    }

    /// <summary>
    /// 创建单个荒疫按钮。
    /// </summary>
    private static Button CreateBlightButton(BlightDisplayInfo blight)
    {
        Button button = new()
        {
            CustomMinimumSize = new Vector2(0, 118),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            ClipContents = false,
        };

        HBoxContainer layout = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        layout.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layout.AddThemeConstantOverride("separation", 12);

        Label stateLabel = new()
        {
            Text = blight.IsEnabled ? GmUiText.Pick("已开", "ON") : GmUiText.Pick("未开", "OFF"),
            CustomMinimumSize = new Vector2(74, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stateLabel.Name = "BlightStateLabel";
        stateLabel.AddThemeColorOverride("font_color", blight.IsEnabled ? PrimaryActionTextColor : DangerActionTextColor);
        layout.AddChild(stateLabel);

        VBoxContainer textLayout = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };

        Label nameLabel = new()
        {
            Text = blight.Name,
            HorizontalAlignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        nameLabel.Name = "BlightNameLabel";
        nameLabel.AddThemeColorOverride("font_color", blight.IsEnabled ? PrimaryActionTextColor : SecondaryActionTextColor);
        textLayout.AddChild(nameLabel);

        RichTextLabel descriptionLabel = new()
        {
            BbcodeEnabled = false,
            FitContent = true,
            ScrollActive = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        descriptionLabel.Name = "BlightDescriptionLabel";
        descriptionLabel.AddThemeColorOverride("font_color", AuxiliaryTextColor);
        AppendItemTooltipText(descriptionLabel, blight.Description);
        textLayout.AddChild(descriptionLabel);

        layout.AddChild(textLayout);

        button.AddChild(layout);
        RefreshBlightButtonVisual(button, blight);
        RegisterItemTooltip(button, $"{blight.Name}\n{blight.Description}\n{blight.Category}\n{blight.Source}\n{blight.IdText}");
        button.Pressed += () => OnBlightButtonPressed(blight);
        return button;
    }

    /// <summary>
    /// 处理荒疫按钮点击，按照当前启用状态直接开关。
    /// </summary>
    private static async void OnBlightButtonPressed(BlightDisplayInfo blight)
    {
        GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(GmFeature.ToggleBlight);
        if (!availability.IsAllowed)
        {
            ShowToast(availability.Reason);
            SetStatusText(availability.Reason);
            return;
        }

        if (!BlightService.CanOperateBlights())
        {
            ShowToast(GmUiText.BlightNotInRun);
            return;
        }

        BlightApplyResult result = blight.IsEnabled
            ? await BlightService.RemoveBlightsAsync([blight])
            : await BlightService.AddBlightsAsync([blight]);
        ShowToast(blight.IsEnabled ? result.BuildRemoveSummary() : result.BuildAddSummary());
        RefreshCurrentPage();
    }

    /// <summary>
    /// 根据荒疫启用状态设置按钮外观。
    /// </summary>
    private static void RefreshBlightButtonVisual(Button button, BlightDisplayInfo blight)
    {
        ApplySelectableItemCardStyle(button, blight.IsEnabled, GrantSelectedAccentColor, disabled: false);
    }

    /// <summary>
    /// 读取当前荒疫页签需要展示的荒疫列表。
    /// </summary>
    private static IReadOnlyList<BlightDisplayInfo> GetCurrentPageBlights()
    {
        if (_searchInput == null || _categoryFilter == null || _sourceFilter == null)
        {
            return [];
        }

        return BlightService.GetBlights(_searchInput.Text, GetSelectedFilterKey(_categoryFilter), GetSelectedFilterKey(_sourceFilter));
    }

    /// <summary>
    /// 读取当前荒疫页签中可被当前操作模式选择的荒疫列表。
    /// </summary>
    private static IReadOnlyList<BlightDisplayInfo> GetCurrentSelectableBlights()
    {
        return GetCurrentPageBlights();
    }
}
