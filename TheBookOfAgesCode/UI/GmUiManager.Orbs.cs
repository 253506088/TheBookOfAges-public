using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.Services;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 球盒模块分文件：充能球按钮、搜索筛选、选择数量和刷新逻辑。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 刷新球盒页的网格内容。
    /// </summary>
    private static void RefreshOrbGrid()
    {
        if (_grid == null || _searchInput == null || _sourceFilter == null)
        {
            return;
        }

        IReadOnlyList<OrbDisplayInfo> orbs = OrbService.GetOrbs(
            _searchInput.Text,
            GetSelectedFilterKey(_sourceFilter));
        UpdateGridColumns();

        ClearGridChildren();

        foreach (OrbDisplayInfo orb in orbs)
        {
            _grid.AddChild(CreateOrbButton(orb));
        }

        UpdateSelectionStatus(orbs.Count);
    }

    /// <summary>
    /// 创建单个充能球按钮，只展示图标和名称，不展示游戏内部 ID。
    /// </summary>
    private static Button CreateOrbButton(OrbDisplayInfo orb)
    {
        Button button = new()
        {
            CustomMinimumSize = new Vector2(178, 110),
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
        };
        layout.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        TextureRect iconRect = new()
        {
            Texture = orb.Icon,
            CustomMinimumSize = new Vector2(ItemIconDisplaySize, ItemIconDisplaySize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        layout.AddChild(iconRect);

        Label nameLabel = new()
        {
            Text = BuildOrbButtonText(orb),
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        nameLabel.Name = "OrbNameLabel";
        layout.AddChild(nameLabel);

        button.AddChild(layout);
        PanelContainer countBadge = CreateOrbCountBadge();
        button.AddChild(countBadge);
        RefreshOrbButtonVisual(button, orb);
        SyncOrbCountBadge(button, orb);
        RegisterItemTooltip(button, $"{orb.Name}\n{orb.Description}\n{orb.Source}\n{orb.IdText}");
        RegisterDragSelectableButton(
            button,
            () => SelectedGrantOrbs.ContainsKey(orb.IdText),
            () => SelectOrbByDrag(orb, button),
            () => UnselectOrbByDrag(orb, button),
            ctrlPressed => OnOrbButtonLeftClick(orb, button, ctrlPressed),
            () => RefreshOrbSelectionVisual(button, orb));
        button.GuiInput += inputEvent => OnOrbButtonGuiInput(inputEvent, orb, button);
        return button;
    }

    /// <summary>
    /// 充能球按钮鼠标事件：左键由拖拽选择模块接管，右键清零。
    /// </summary>
    private static void OnOrbButtonGuiInput(InputEvent inputEvent, OrbDisplayInfo orb, Button button)
    {
        if (inputEvent is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed)
        {
            return;
        }

        if (mouseEvent.ButtonIndex == MouseButton.Right)
        {
            SelectedGrantOrbs.Remove(orb.IdText);
            RefreshOrbSelectionVisual(button, orb);
            UpdateSelectionStatus(_grid?.GetChildCount());
            button.AcceptEvent();
        }
    }

    /// <summary>
    /// 处理球盒页普通左键单击：默认 +1，按住 Ctrl 时 +5。
    /// </summary>
    private static void OnOrbButtonLeftClick(OrbDisplayInfo orb, Button button, bool ctrlPressed)
    {
        int delta = ctrlPressed ? 5 : 1;
        AdjustOrbSelection(orb, delta);
        RefreshOrbSelectionVisual(button, orb);
        UpdateSelectionStatus(_grid?.GetChildCount());
    }

    /// <summary>
    /// 给指定充能球增加选择数量；不存在则新建条目。
    /// </summary>
    private static void AdjustOrbSelection(OrbDisplayInfo orb, int delta)
    {
        if (delta <= 0)
        {
            return;
        }

        if (SelectedGrantOrbs.TryGetValue(orb.IdText, out OrbSelectionInfo? selection))
        {
            selection.Count += delta;
            return;
        }

        SelectedGrantOrbs[orb.IdText] = new OrbSelectionInfo
        {
            Info = orb,
            Count = delta,
        };
    }

    /// <summary>
    /// 拖拽批量选中充能球；只保证数量为 1，不重复累加。
    /// </summary>
    private static void SelectOrbByDrag(OrbDisplayInfo orb, Button button)
    {
        if (!SelectedGrantOrbs.ContainsKey(orb.IdText))
        {
            SelectedGrantOrbs[orb.IdText] = new OrbSelectionInfo
            {
                Info = orb,
                Count = 1,
            };
        }

        RefreshOrbSelectionVisual(button, orb);
    }

    /// <summary>
    /// 拖拽批量取消充能球选择，相当于把该充能球数量清零。
    /// </summary>
    private static void UnselectOrbByDrag(OrbDisplayInfo orb, Button button)
    {
        SelectedGrantOrbs.Remove(orb.IdText);
        RefreshOrbSelectionVisual(button, orb);
    }

    /// <summary>
    /// 同步充能球按钮文本和外框。
    /// </summary>
    private static void RefreshOrbSelectionVisual(Button button, OrbDisplayInfo orb)
    {
        SyncOrbCountBadge(button, orb);
        UpdateOrbButtonText(button, orb);
        RefreshOrbButtonVisual(button, orb);
    }

    /// <summary>
    /// 创建充能球按钮右上角「×N」徽章，用来显示同一种球累计选择数量。
    /// </summary>
    private static PanelContainer CreateOrbCountBadge()
    {
        PanelContainer badge = new()
        {
            Name = "OrbCountBadge",
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
                                  OrbSelectedAccentColor,
                                  6, 2, 0, 0));

        Label label = new()
        {
            Name = "OrbCountLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", new Color(0.72f, 1f, 1f, 1f));
        label.AddThemeFontSizeOverride("font_size", 22);
        badge.AddChild(label);

        return badge;
    }

    /// <summary>
    /// 把当前充能球的已选数量同步到按钮右上角徽章；N=0 时隐藏徽章。
    /// </summary>
    private static void SyncOrbCountBadge(Button button, OrbDisplayInfo orb)
    {
        PanelContainer? badge = button.GetNodeOrNull<PanelContainer>("OrbCountBadge");
        if (badge == null)
        {
            return;
        }
        Label? label = badge.GetNodeOrNull<Label>("OrbCountLabel");

        if (SelectedGrantOrbs.TryGetValue(orb.IdText, out OrbSelectionInfo? selection) && selection.Count > 0)
        {
            if (label != null)
            {
                label.Text = $"×{selection.Count}";
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
    /// 更新充能球按钮内的名称标签文本，支持显示同一种充能球的重复选择数量。
    /// </summary>
    private static void UpdateOrbButtonText(Button button, OrbDisplayInfo orb)
    {
        Label? label = button.GetNodeOrNull<Label>("VBoxContainer/OrbNameLabel");
        if (label != null)
        {
            label.Text = BuildOrbButtonText(orb);
        }
    }

    /// <summary>
    /// 拼接充能球按钮文本，支持显示同一种充能球的重复选择数量。
    /// </summary>
    private static string BuildOrbButtonText(OrbDisplayInfo orb)
    {
        if (SelectedGrantOrbs.TryGetValue(orb.IdText, out OrbSelectionInfo? selection))
        {
            return GmUiText.Pick($"{orb.Name}\n已选 x{selection.Count}", $"{orb.Name}\nSelected x{selection.Count}");
        }

        return orb.Name;
    }

    /// <summary>
    /// 根据充能球选择状态设置按钮外观。
    /// </summary>
    private static void RefreshOrbButtonVisual(Button button, OrbDisplayInfo orb)
    {
        ApplySelectableItemCardStyle(button, SelectedGrantOrbs.ContainsKey(orb.IdText), OrbSelectedAccentColor, disabled: false);
    }
}
