using Godot;
using TheBookOfAges.TheBookOfAgesCode.Models;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 页面视觉样式分文件：主题、字号、按钮/下拉/卡片样式与各类按钮的选中态高亮刷新。
/// 内容来自 GmUiManager.cs 的视觉样式段，行为与原实现一致，只做物理拆分。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 创建 GM 页面专用主题，只调整默认字号，不影响游戏原本的全局 UI 主题。
    /// </summary>
    private static Theme CreateGmTheme()
    {
        return new Theme
        {
            DefaultFontSize = GmUiFontSize,
        };
    }

    /// <summary>
    /// 给不在 GM 面板主题树里的控件单独设置字号，例如顶栏 GM 按钮。
    /// </summary>
    private static void ApplyGmFontSize(Control control)
    {
        control.AddThemeFontSizeOverride("font_size", GmUiFontSize);
    }

    /// <summary>
    /// 创建统一的圆角样式盒，给按钮和面板复用，减少样式散落。
    /// </summary>
    private static StyleBoxFlat CreateRoundedStyleBox(Color bgColor, Color borderColor, int cornerRadius, int borderWidth, int horizontalPadding = 10, int verticalPadding = 6)
    {
        return new StyleBoxFlat
        {
            BgColor = bgColor,
            BorderColor = borderColor,
            BorderWidthLeft = borderWidth,
            BorderWidthTop = borderWidth,
            BorderWidthRight = borderWidth,
            BorderWidthBottom = borderWidth,
            CornerRadiusTopLeft = cornerRadius,
            CornerRadiusTopRight = cornerRadius,
            CornerRadiusBottomRight = cornerRadius,
            CornerRadiusBottomLeft = cornerRadius,
            ContentMarginLeft = horizontalPadding,
            ContentMarginTop = verticalPadding,
            ContentMarginRight = horizontalPadding,
            ContentMarginBottom = verticalPadding,
        };
    }

    /// <summary>
    /// 给主面板应用统一底色和边框，提升整体层次感。
    /// </summary>
    private static void ApplyPanelVisualStyle(PanelContainer panel)
    {
        panel.AddThemeStyleboxOverride("panel", CreateRoundedStyleBox(PanelBgColor, PanelBorderColor, 14, 2, 16, 14));
    }

    /// <summary>
    /// 给左侧导航按钮应用统一视觉样式，并用禁用态突出当前页签。
    /// </summary>
    private static void ApplyNavButtonVisualStyle(Button button)
    {
        button.AddThemeStyleboxOverride("normal", CreateRoundedStyleBox(new Color(0.06f, 0.08f, 0.12f, 0.76f), new Color(0.22f, 0.29f, 0.37f, 0.84f), 10, 1));
        button.AddThemeStyleboxOverride("hover", CreateRoundedStyleBox(new Color(0.10f, 0.16f, 0.22f, 0.88f), new Color(0.38f, 0.55f, 0.70f, 0.94f), 10, 1));
        button.AddThemeStyleboxOverride("pressed", CreateRoundedStyleBox(new Color(0.14f, 0.21f, 0.30f, 0.94f), new Color(0.72f, 0.80f, 0.88f, 0.95f), 10, 1));
        button.AddThemeStyleboxOverride("focus", CreateRoundedStyleBox(new Color(0.10f, 0.16f, 0.22f, 0.90f), new Color(0.52f, 0.68f, 0.82f, 0.96f), 10, 2));
        button.AddThemeStyleboxOverride("disabled", CreateRoundedStyleBox(new Color(0.25f, 0.20f, 0.08f, 0.94f), new Color(0.88f, 0.72f, 0.30f, 0.96f), 10, 2));
        button.AddThemeColorOverride("font_color", NavTextNormalColor);
        button.AddThemeColorOverride("font_hover_color", NavTextHoverColor);
        button.AddThemeColorOverride("font_pressed_color", NavTextHoverColor);
        button.AddThemeColorOverride("font_disabled_color", NavTextActiveColor);
        button.AddThemeColorOverride("font_focus_color", NavTextHoverColor);
        button.AddThemeConstantOverride("icon_max_width", Mathf.RoundToInt(button.CustomMinimumSize.Y * GmUiNavIconScale));
    }

    /// <summary>
    /// 给主要动作按钮应用统一视觉样式。
    /// </summary>
    private static void ApplyPrimaryActionButtonStyle(Button button)
    {
        button.AddThemeStyleboxOverride("normal", CreateRoundedStyleBox(new Color(0.09f, 0.14f, 0.18f, 0.88f), new Color(0.28f, 0.44f, 0.54f, 0.95f), 8, 1));
        button.AddThemeStyleboxOverride("hover", CreateRoundedStyleBox(new Color(0.12f, 0.20f, 0.26f, 0.94f), new Color(0.44f, 0.66f, 0.76f, 0.98f), 8, 1));
        button.AddThemeStyleboxOverride("pressed", CreateRoundedStyleBox(new Color(0.16f, 0.26f, 0.32f, 0.97f), new Color(0.56f, 0.82f, 0.90f, 1.00f), 8, 1));
        button.AddThemeColorOverride("font_color", PrimaryActionTextColor);
        button.AddThemeColorOverride("font_hover_color", NavTextHoverColor);
        button.AddThemeColorOverride("font_pressed_color", NavTextHoverColor);
        button.AddThemeColorOverride("font_focus_color", NavTextHoverColor);
    }

    /// <summary>
    /// 给中性动作按钮应用统一视觉样式。
    /// </summary>
    private static void ApplySecondaryActionButtonStyle(Button button)
    {
        button.AddThemeStyleboxOverride("normal", CreateRoundedStyleBox(new Color(0.08f, 0.09f, 0.12f, 0.82f), new Color(0.23f, 0.27f, 0.33f, 0.90f), 8, 1));
        button.AddThemeStyleboxOverride("hover", CreateRoundedStyleBox(new Color(0.12f, 0.14f, 0.19f, 0.90f), new Color(0.36f, 0.43f, 0.52f, 0.95f), 8, 1));
        button.AddThemeStyleboxOverride("pressed", CreateRoundedStyleBox(new Color(0.16f, 0.18f, 0.24f, 0.94f), new Color(0.48f, 0.58f, 0.69f, 0.98f), 8, 1));
        button.AddThemeColorOverride("font_color", SecondaryActionTextColor);
        button.AddThemeColorOverride("font_hover_color", NavTextHoverColor);
        button.AddThemeColorOverride("font_pressed_color", NavTextHoverColor);
    }

    /// <summary>
    /// 给危险动作按钮应用统一视觉样式。
    /// </summary>
    private static void ApplyDangerActionButtonStyle(Button button)
    {
        button.AddThemeStyleboxOverride("normal", CreateRoundedStyleBox(new Color(0.20f, 0.10f, 0.10f, 0.86f), new Color(0.48f, 0.23f, 0.23f, 0.96f), 8, 1));
        button.AddThemeStyleboxOverride("hover", CreateRoundedStyleBox(new Color(0.28f, 0.13f, 0.13f, 0.92f), new Color(0.68f, 0.30f, 0.30f, 0.98f), 8, 1));
        button.AddThemeStyleboxOverride("pressed", CreateRoundedStyleBox(new Color(0.34f, 0.16f, 0.16f, 0.94f), new Color(0.84f, 0.40f, 0.40f, 1.00f), 8, 1));
        button.AddThemeColorOverride("font_color", DangerActionTextColor);
        button.AddThemeColorOverride("font_hover_color", NavTextHoverColor);
        button.AddThemeColorOverride("font_pressed_color", NavTextHoverColor);
    }

    /// <summary>
    /// 给下拉框应用统一视觉样式，提升筛选和快捷跳转的可读性。
    /// </summary>
    private static void ApplySelectVisualStyle(OptionButton optionButton)
    {
        optionButton.AddThemeStyleboxOverride("normal", CreateRoundedStyleBox(new Color(0.07f, 0.09f, 0.12f, 0.84f), new Color(0.23f, 0.29f, 0.36f, 0.92f), 8, 1));
        optionButton.AddThemeStyleboxOverride("hover", CreateRoundedStyleBox(new Color(0.10f, 0.14f, 0.20f, 0.90f), new Color(0.38f, 0.50f, 0.62f, 0.95f), 8, 1));
        optionButton.AddThemeStyleboxOverride("pressed", CreateRoundedStyleBox(new Color(0.13f, 0.18f, 0.25f, 0.94f), new Color(0.52f, 0.66f, 0.78f, 0.98f), 8, 1));
        optionButton.AddThemeStyleboxOverride("focus", CreateRoundedStyleBox(new Color(0.10f, 0.14f, 0.20f, 0.92f), new Color(0.58f, 0.74f, 0.86f, 0.98f), 8, 1));
        optionButton.AddThemeColorOverride("font_color", SecondaryActionTextColor);
        optionButton.AddThemeColorOverride("font_hover_color", NavTextHoverColor);
        optionButton.AddThemeColorOverride("font_pressed_color", NavTextHoverColor);
        optionButton.AddThemeColorOverride("font_focus_color", NavTextHoverColor);
    }

    /// <summary>
    /// 给遗物/药水/怪物卡片按钮应用选中态高亮样式，让选中框更显眼。
    /// </summary>
    private static void ApplySelectableItemCardStyle(Button button, bool selected, Color accentColor, bool disabled)
    {
        if (disabled)
        {
            StyleBoxFlat disabledStyle = CreateRoundedStyleBox(ItemCardOwnedBgColor, ItemCardOwnedBorderColor, 10, 2, 8, 8);
            button.AddThemeStyleboxOverride("normal", disabledStyle);
            button.AddThemeStyleboxOverride("hover", disabledStyle);
            button.AddThemeStyleboxOverride("pressed", disabledStyle);
            button.AddThemeStyleboxOverride("focus", disabledStyle);
            return;
        }

        if (!selected)
        {
            button.AddThemeStyleboxOverride("normal", CreateRoundedStyleBox(ItemCardNormalBgColor, ItemCardNormalBorderColor, 10, 1, 8, 8));
            button.AddThemeStyleboxOverride("hover", CreateRoundedStyleBox(ItemCardHoverBgColor, ItemCardHoverBorderColor, 10, 2, 8, 8));
            button.AddThemeStyleboxOverride("pressed", CreateRoundedStyleBox(ItemCardPressedBgColor, ItemCardHoverBorderColor, 10, 2, 8, 8));
            button.AddThemeStyleboxOverride("focus", CreateRoundedStyleBox(ItemCardHoverBgColor, ItemCardHoverBorderColor, 10, 2, 8, 8));
            return;
        }

        Color selectedBg = new(
            0.10f + accentColor.R * 0.20f,
            0.10f + accentColor.G * 0.20f,
            0.11f + accentColor.B * 0.20f,
            0.95f);
        Color selectedHoverBg = new(
            0.12f + accentColor.R * 0.22f,
            0.12f + accentColor.G * 0.22f,
            0.13f + accentColor.B * 0.22f,
            0.98f);
        Color selectedBorder = accentColor;
        Color selectedHoverBorder = accentColor.Lightened(0.12f);

        button.AddThemeStyleboxOverride("normal", CreateRoundedStyleBox(selectedBg, selectedBorder, 10, 3, 8, 8));
        button.AddThemeStyleboxOverride("hover", CreateRoundedStyleBox(selectedHoverBg, selectedHoverBorder, 10, 3, 8, 8));
        button.AddThemeStyleboxOverride("pressed", CreateRoundedStyleBox(selectedHoverBg, selectedHoverBorder, 10, 3, 8, 8));
        button.AddThemeStyleboxOverride("focus", CreateRoundedStyleBox(selectedHoverBg, selectedHoverBorder, 10, 4, 8, 8));
    }

    /// <summary>
    /// 按当前遗物状态刷新按钮外框高亮。
    /// </summary>
    private static void RefreshRelicButtonVisual(Button button, RelicDisplayInfo relic)
    {
        bool isOwned = ShouldBlockOwnedRelicGrant(relic);
        bool selected = CurrentSelectedRelics.ContainsKey(relic.IdText);
        Color accent = _currentPage == GmPageMode.GrantRelics ? GrantSelectedAccentColor : RemoveSelectedAccentColor;
        ApplySelectableItemCardStyle(button, selected, accent, isOwned);
    }

    /// <summary>
    /// 按当前药水状态刷新按钮外框高亮。
    /// </summary>
    private static void RefreshPotionButtonVisual(Button button, PotionDisplayInfo potion)
    {
        ApplySelectableItemCardStyle(button, SelectedGrantPotions.ContainsKey(potion.IdText), GrantSelectedAccentColor, disabled: false);
    }

    /// <summary>
    /// 按当前能力状态刷新按钮外框高亮。
    /// </summary>
    private static void RefreshPowerButtonVisual(Button button, PowerDisplayInfo power)
    {
        ApplySelectableItemCardStyle(button, SelectedPowers.ContainsKey(power.IdText), PowerSelectedAccentColor, disabled: false);
    }

    /// <summary>
    /// 按当前怪物状态刷新按钮外框高亮。
    /// </summary>
    private static void RefreshMonsterButtonVisual(Button button, MonsterDisplayInfo monster)
    {
        ApplySelectableItemCardStyle(button, SelectedSpawnMonsters.ContainsKey(monster.IdText), MonsterSelectedAccentColor, disabled: false);
    }
}
