using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using System.Reflection;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 顶栏入口分文件：创建/挂载顶栏 GM 按钮、悬停说明，以及把 GM 按钮放到原生地图按钮左边的兜底布局。
/// 内容来自 GmUiManager.cs 的顶栏接入段，行为与原实现一致，只做物理拆分。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 创建顶栏 GM 图标按钮。
    /// </summary>
    private static BaseButton CreateTopBarButton()
    {
        Texture2D? icon = LoadGmUiTexture(GmButtonIconFileName);
        if (icon != null)
        {
            TextureButton iconButton = new()
            {
                TextureNormal = icon,
                TextureHover = icon,
                TexturePressed = icon,
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            };
            ConfigureTopBarButton(iconButton);
            return iconButton;
        }

        Button button = new()
        {
            Text = "GM",
        };
        ConfigureTopBarButton(button);
        ApplyGmFontSize(button);
        return button;
    }

    /// <summary>
    /// 设置顶栏 GM 按钮的通用属性，让图标按钮和文字兜底按钮保持同一尺寸。
    /// </summary>
    private static void ConfigureTopBarButton(Control button)
    {
        button.Name = GmButtonName;
        button.TooltipText = string.Empty;
        button.CustomMinimumSize = new Vector2(GmTopBarButtonSize, GmTopBarButtonSize);
        button.FocusMode = Control.FocusModeEnum.None;
        button.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        RegisterTopBarHoverTip(button);
    }

    /// <summary>
    /// 给顶栏 GM 按钮挂游戏原生悬停说明，避免使用 Godot 默认 Tooltip 样式。
    /// </summary>
    private static void RegisterTopBarHoverTip(Control button)
    {
        button.MouseEntered += () => ShowTopBarHoverTip(button);
        button.MouseExited += () => HideTopBarHoverTip(button);
        button.TreeExiting += () => HideTopBarHoverTip(button);
    }

    /// <summary>
    /// 显示顶栏 GM 按钮的原生悬停说明。
    /// </summary>
    private static void ShowTopBarHoverTip(Control owner)
    {
        try
        {
            HideTopBarHoverTip(owner);
            HoverTip hoverTip = CreateTopBarHoverTip();
            NHoverTipSet.CreateAndShow(owner, hoverTip, HoverTip.GetHoverTipAlignment(owner, 1f));
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 顶栏提示显示失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 隐藏顶栏 GM 按钮的原生悬停说明。
    /// </summary>
    private static void HideTopBarHoverTip(Control owner)
    {
        try
        {
            NHoverTipSet.Remove(owner);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 顶栏提示隐藏失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 创建顶栏 GM 按钮的原生悬停说明内容。
    /// </summary>
    private static HoverTip CreateTopBarHoverTip()
    {
        object boxedHoverTip = default(HoverTip);
        SetHoverTipField(boxedHoverTip, HoverTipTitleField, GmUiText.Pick("GM面板(H)", "GM Panel (H)"));
        SetHoverTipField(boxedHoverTip, HoverTipDescriptionField, GmUiText.Pick("管理游戏运行的规则", "Manage runtime game rules"));
        SetHoverTipField(boxedHoverTip, HoverTipIdField, GmButtonName);
        return (HoverTip)boxedHoverTip;
    }

    /// <summary>
    /// 查找 HoverTip 的自动属性字段，用原始文本避开本地化表依赖。
    /// </summary>
    private static FieldInfo? FindHoverTipBackingField(string fieldName)
    {
        return typeof(HoverTip).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
    }

    /// <summary>
    /// 写入 HoverTip 的内部字段，保持原生提示框 UI，同时允许 GM 面板使用动态文本。
    /// </summary>
    private static void SetHoverTipField(object boxedHoverTip, FieldInfo? field, object? value)
    {
        field?.SetValue(boxedHoverTip, value);
    }

    /// <summary>
    /// 把 GM 按钮插到原生地图按钮左边，找不到地图按钮时才使用旧的顶栏悬浮位置兜底。
    /// </summary>
    private static void AddTopBarButton(NTopBar topBar, BaseButton gmButton)
    {
        Node? mapButton = FindNativeMapButton(topBar);
        Node? targetParent = mapButton?.GetParent();
        if (mapButton != null && targetParent != null)
        {
            targetParent.AddChild(gmButton);
            targetParent.MoveChild(gmButton, mapButton.GetIndex());
            return;
        }

        topBar.AddChild(gmButton);
        ApplyTopBarFallbackLayout(gmButton);
        MainFile.Logger.Info("GM 顶栏未找到原生地图按钮，已使用旧位置兜底。");
    }

    /// <summary>
    /// 找到原生地图按钮节点，用于把 GM 按钮放到它左边。
    /// </summary>
    private static Node? FindNativeMapButton(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            if (IsNativeMapButton(child))
            {
                return child;
            }

            Node? nested = FindNativeMapButton(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>
    /// 判断节点是否是游戏原生地图按钮，优先按类型名判断，避免误匹配普通地图相关子节点。
    /// </summary>
    private static bool IsNativeMapButton(Node node)
    {
        string typeName = node.GetType().Name;
        if (typeName.Contains("NTopBarMapButton", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return node is BaseButton && node.Name.ToString().Contains("Map", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 按名称递归查找节点，用于避免 GM 按钮被重复创建。
    /// </summary>
    private static Node? FindNodeByName(Node parent, string nodeName)
    {
        foreach (Node child in parent.GetChildren())
        {
            if (string.Equals(child.Name.ToString(), nodeName, StringComparison.Ordinal))
            {
                return child;
            }

            Node? nested = FindNodeByName(child, nodeName);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>
    /// 地图按钮定位失败时保留旧的顶栏悬浮布局，保证 GM 入口仍可用。
    /// </summary>
    private static void ApplyTopBarFallbackLayout(Control button)
    {
        button.CustomMinimumSize = new Vector2(96, 54);
        button.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        button.OffsetLeft = -600;
        button.OffsetRight = -504;
        button.OffsetTop = 54;
        button.OffsetBottom = 108;
    }

}
