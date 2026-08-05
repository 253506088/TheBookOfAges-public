using Godot;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.Config;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.Services;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 遗物模块分文件：遗物按钮的创建、点击、文本与高亮刷新，以及当前页签遗物列表的拉取。
/// 内容来自 GmUiManager.cs 的遗物相关方法段，行为与原实现一致，只做物理拆分。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 创建单个遗物按钮，只展示图标和名称，不展示游戏内部 ID。
    /// 使用 TextureRect + Label 组合布局取代 Button.Icon，
    /// 因为 Button.Icon 不支持限制图标显示大小，不同遗物贴图分辨率不同会撑爆格子。
    /// </summary>
    private static Button CreateRelicButton(RelicDisplayInfo relic)
    {
        Button button = new()
        {
            CustomMinimumSize = new Vector2(178, 132),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            ClipContents = true,
        };

        // 用垂直布局容器包裹图标和文字，让它们上下排列居中
        VBoxContainer layout = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        layout.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        // 图标区域：固定大小、居中缩放，不管原始贴图多大都压缩到统一尺寸
        TextureRect iconRect = new()
        {
            Texture = relic.Icon,
            CustomMinimumSize = new Vector2(ItemIconDisplaySize, ItemIconDisplaySize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        layout.AddChild(iconRect);

        // 名称标签
        Label nameLabel = new()
        {
            Text = BuildRelicButtonText(relic),
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        // 用 Name 属性标记，方便后续 OnRelicButtonPressed 更新文本
        nameLabel.Name = "RelicNameLabel";
        layout.AddChild(nameLabel);

        button.AddChild(layout);
        button.Modulate = GetRelicButtonColor(relic);
        RefreshRelicButtonVisual(button, relic);
        RegisterItemTooltip(button, $"{relic.Name}\n{relic.Description}\n{relic.Rarity}\n{relic.Category}\n{relic.Source}");
        RegisterDragSelectableButton(
            button,
            () => CurrentSelectedRelics.ContainsKey(relic.IdText),
            () => SelectRelicByDrag(relic, button),
            () => UnselectRelicByDrag(relic, button),
            _ => OnRelicButtonPressed(relic, button),
            () => RefreshRelicSelectionVisual(button, relic),
            () => !ShouldBlockOwnedRelicGrant(relic));
        return button;
    }

    /// <summary>
    /// 处理遗物按钮点击：按配置判断已拥有遗物是否拦截，其余情况切换选中状态。
    /// </summary>
    private static void OnRelicButtonPressed(RelicDisplayInfo relic, Button button)
    {
        if (ShouldBlockOwnedRelicGrant(relic))
        {
            ShowToast(string.Format(GmUiText.AlreadyOwnedNamed, relic.Name));
            return;
        }

        if (CurrentSelectedRelics.Remove(relic.IdText))
        {
            UpdateRelicButtonText(button, relic);
        }
        else
        {
            CurrentSelectedRelics[relic.IdText] = relic;
            UpdateRelicButtonText(button, relic);
        }

        button.Modulate = GetRelicButtonColor(relic);
        RefreshRelicButtonVisual(button, relic);
        UpdateSelectionStatus();
    }

    /// <summary>
    /// 拖拽批量选中遗物；开启禁止重复发放时，遗物自助页里已拥有的遗物不允许选中。
    /// </summary>
    private static void SelectRelicByDrag(RelicDisplayInfo relic, Button button)
    {
        if (ShouldBlockOwnedRelicGrant(relic))
        {
            return;
        }

        CurrentSelectedRelics[relic.IdText] = relic;
        RefreshRelicSelectionVisual(button, relic);
    }

    /// <summary>
    /// 拖拽批量取消遗物选择。
    /// </summary>
    private static void UnselectRelicByDrag(RelicDisplayInfo relic, Button button)
    {
        CurrentSelectedRelics.Remove(relic.IdText);
        RefreshRelicSelectionVisual(button, relic);
    }

    /// <summary>
    /// 同步遗物按钮文本、颜色和外框。
    /// </summary>
    private static void RefreshRelicSelectionVisual(Button button, RelicDisplayInfo relic)
    {
        UpdateRelicButtonText(button, relic);
        button.Modulate = GetRelicButtonColor(relic);
        RefreshRelicButtonVisual(button, relic);
    }

    /// <summary>
    /// 更新遗物按钮内的名称标签文本。
    /// 因为按钮使用 TextureRect + Label 组合布局，文本在子节点 RelicNameLabel 上。
    /// </summary>
    private static void UpdateRelicButtonText(Button button, RelicDisplayInfo relic)
    {
        Label? label = button.GetNodeOrNull<Label>("VBoxContainer/RelicNameLabel");
        if (label != null)
        {
            label.Text = BuildRelicButtonText(relic);
        }
    }

    /// <summary>
    /// 拼接遗物按钮文本。
    /// </summary>
    private static string BuildRelicButtonText(RelicDisplayInfo relic)
    {
        if (ShouldBlockOwnedRelicGrant(relic))
        {
            return GmUiText.Pick($"{relic.Name}\n已拥有", $"{relic.Name}\nOwned");
        }

        if (CurrentSelectedRelics.ContainsKey(relic.IdText))
        {
            return _currentPage == GmPageMode.GrantRelics
                ? GmUiText.Pick($"{relic.Name}\n已选", $"{relic.Name}\nSelected")
                : GmUiText.Pick($"{relic.Name}\n待删除", $"{relic.Name}\nTo Remove");
        }

        return relic.Name;
    }

    /// <summary>
    /// 根据拥有和选中状态设置按钮颜色。
    /// </summary>
    private static Color GetRelicButtonColor(RelicDisplayInfo relic)
    {
        if (ShouldBlockOwnedRelicGrant(relic))
        {
            return new Color(0.58f, 0.58f, 0.58f, 0.78f);
        }

        return Colors.White;
    }

    /// <summary>
    /// 判断遗物自助页是否要阻止已拥有遗物再次选择。
    /// 配置关闭时允许重复发放；配置开启时保留旧逻辑。
    /// </summary>
    private static bool ShouldBlockOwnedRelicGrant(RelicDisplayInfo relic)
    {
        return _currentPage == GmPageMode.GrantRelics &&
               GmModConfig.ShouldPreventDuplicateRelicGrant &&
               relic.IsOwned;
    }

    /// <summary>
    /// 读取当前遗物页签需要展示的遗物列表。
    /// </summary>
    private static IReadOnlyList<RelicDisplayInfo> GetCurrentPageRelics()
    {
        if (_searchInput == null || _categoryFilter == null || _rarityFilter == null || _sourceFilter == null)
        {
            return [];
        }

        return _currentPage == GmPageMode.GrantRelics
            ? RelicService.GetRelics(_searchInput.Text, GetSelectedFilterKey(_categoryFilter), GetSelectedFilterKey(_rarityFilter), GetSelectedFilterKey(_sourceFilter))
            : RelicService.GetOwnedRelics(_searchInput.Text, GetSelectedFilterKey(_categoryFilter), GetSelectedFilterKey(_rarityFilter), GetSelectedFilterKey(_sourceFilter));
    }
}
