using Godot;
using TheBookOfAges.TheBookOfAgesCode.Models;
using System.Text;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 物品/怪物悬停说明分文件：rich-text 颜色标记解析、内联图片渲染、悬浮位置避让，以及怪物预览的尺寸估算。
/// 内容来自 GmUiManager.cs 的 Tooltip 段，行为与原实现一致，只做物理拆分。
/// 备注：ResolveMonsterPreviewSize 仅被 ShowMonsterTooltip 调用，暂随 Tooltip 一起搬来；
/// 后续 Monsters 模块独立时再讨论是否归位。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 给物品按钮注册手动悬停说明，避免自定义 Godot 节点触发桥接层异常。
    /// </summary>
    private static void RegisterItemTooltip(Control button, string tooltipText)
    {
        button.TooltipText = string.Empty;
        button.MouseEntered += () => ShowItemTooltip(button, tooltipText);
        button.MouseExited += () => HideItemTooltip(button);
        button.TreeExiting += () => HideItemTooltip(button);
    }

    /// <summary>
    /// 给控件注册动态悬浮提示：鼠标进入时实时读取文案，支持国际化切换后不重建控件也显示新语言。
    /// </summary>
    private static void RegisterItemTooltip(Control button, Func<string> tooltipProvider)
    {
        button.TooltipText = string.Empty;
        button.MouseEntered += () => ShowItemTooltip(button, tooltipProvider());
        button.MouseExited += () => HideItemTooltip(button);
        button.TreeExiting += () => HideItemTooltip(button);
    }

    /// <summary>
    /// 给怪物按钮注册手动悬停说明；列表保持轻量文本，仅在悬浮时尝试创建动态预览。
    /// </summary>
    private static void RegisterMonsterTooltip(Control button, MonsterDisplayInfo monster)
    {
        button.TooltipText = string.Empty;
        button.MouseEntered += () => ShowMonsterTooltip(button, monster);
        button.MouseExited += () => HideItemTooltip(button);
        button.TreeExiting += () => HideItemTooltip(button);
    }

    /// <summary>
    /// 显示物品悬停说明，并把描述里的颜色标记渲染成高亮文本。
    /// </summary>
    private static void ShowItemTooltip(Control owner, string tooltipText)
    {
        if (string.IsNullOrWhiteSpace(tooltipText))
        {
            HideItemTooltip(owner);
            return;
        }

        if (_root == null || !GodotObject.IsInstanceValid(_root))
        {
            return;
        }

        HideItemTooltip(null);
        PanelContainer tooltip = CreateItemTooltipPanel();
        RichTextLabel label = CreateItemTooltipLabel(tooltipText);
        AppendItemTooltipText(label, tooltipText);
        tooltip.AddChild(label);
        _root.AddChild(tooltip);
        PositionItemTooltip(owner, tooltip, tooltipText);
        _itemTooltip = tooltip;
        _itemTooltipOwner = owner;
    }

    /// <summary>
    /// 显示怪物悬停说明；仅在悬浮时懒加载动态预览，失败则自动退回纯文字说明。
    /// </summary>
    private static void ShowMonsterTooltip(Control owner, MonsterDisplayInfo monster)
    {
        if (_root == null || !GodotObject.IsInstanceValid(_root))
        {
            return;
        }

        HideItemTooltip(null);
        string tooltipText = BuildMonsterTooltip(monster);
        PanelContainer tooltip = CreateItemTooltipPanel();
        VBoxContainer content = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        content.AddThemeConstantOverride("separation", (int)MonsterTooltipContentGap);

        Vector2 previewSize = ResolveMonsterPreviewSize(owner);
        Control? preview = TryCreateMonsterPreviewControl(monster, previewSize);
        bool hasPreview = preview != null;
        if (preview != null)
        {
            content.AddChild(preview);
        }

        float previewWidth = hasPreview ? previewSize.X : 0f;
        float previewHeight = hasPreview ? previewSize.Y : 0f;
        float contentWidth = Math.Max(EstimateItemTooltipTextWidth(tooltipText), previewWidth);
        RichTextLabel label = CreateItemTooltipLabel(tooltipText);
        label.CustomMinimumSize = new Vector2(contentWidth, 0f);
        AppendItemTooltipText(label, tooltipText);
        content.AddChild(label);

        tooltip.AddChild(content);
        _root.AddChild(tooltip);

        float tooltipHeight = EstimateItemTooltipHeight(tooltipText);
        if (hasPreview)
        {
            tooltipHeight += previewHeight + MonsterTooltipContentGap;
        }

        PositionTooltip(owner, tooltip, contentWidth + ItemTooltipPadding * 2f, tooltipHeight);
        _itemTooltip = tooltip;
        _itemTooltipOwner = owner;
    }

    /// <summary>
    /// 隐藏当前物品悬停说明。
    /// </summary>
    private static void HideItemTooltip(Control? owner)
    {
        if (owner != null && _itemTooltipOwner != owner)
        {
            return;
        }

        if (_itemTooltip != null && GodotObject.IsInstanceValid(_itemTooltip))
        {
            _itemTooltip.QueueFree();
        }

        _itemTooltip = null;
        _itemTooltipOwner = null;
    }

    /// <summary>
    /// 根据当前屏幕尺寸计算怪物预览区域，优先吃掉可用空间，避免小窗看不全。
    /// </summary>
    private static Vector2 ResolveMonsterPreviewSize(Control owner)
    {
        Vector2 viewportSize = owner.GetViewportRect().Size;
        float width = Math.Clamp(viewportSize.X * MonsterTooltipPreviewWidthRatio, MonsterTooltipPreviewWidth, MonsterTooltipPreviewMaxWidth);
        float height = Math.Clamp(viewportSize.Y * MonsterTooltipPreviewHeightRatio, MonsterTooltipPreviewHeight, MonsterTooltipPreviewMaxHeight);
        return new Vector2(width, height);
    }

    /// <summary>
    /// 创建物品悬停说明外层面板。
    /// </summary>
    private static PanelContainer CreateItemTooltipPanel()
    {
        PanelContainer panel = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 1000,
        };

        StyleBoxFlat styleBox = new()
        {
            BgColor = new Color(0f, 0f, 0f, 0.94f),
            BorderColor = new Color(0.38f, 0.48f, 0.52f, 0.92f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = ItemTooltipPadding,
            ContentMarginTop = ItemTooltipPadding,
            ContentMarginRight = ItemTooltipPadding,
            ContentMarginBottom = ItemTooltipPadding,
        };
        panel.AddThemeStyleboxOverride("panel", styleBox);
        return panel;
    }

    /// <summary>
    /// 创建物品悬停说明文本控件。
    /// </summary>
    private static RichTextLabel CreateItemTooltipLabel(string text)
    {
        RichTextLabel label = new()
        {
            BbcodeEnabled = false,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(EstimateItemTooltipTextWidth(text), 0),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("normal_font_size", ItemTooltipFontSize);
        label.AddThemeColorOverride("default_color", new Color(0.92f, 0.92f, 0.88f));
        return label;
    }

    /// <summary>
    /// 根据鼠标位置摆放物品悬停说明，尽量避免贴到屏幕外。
    /// </summary>
    private static void PositionItemTooltip(Control owner, Control tooltip, string text)
    {
        float tooltipWidth = EstimateItemTooltipTextWidth(text) + ItemTooltipPadding * 2f;
        float tooltipHeight = EstimateItemTooltipHeight(text);
        PositionTooltip(owner, tooltip, tooltipWidth, tooltipHeight);
    }

    /// <summary>
    /// 根据鼠标位置摆放悬停说明，尽量避免贴到屏幕外。
    /// </summary>
    private static void PositionTooltip(Control owner, Control tooltip, float tooltipWidth, float tooltipHeight)
    {
        Vector2 mousePosition = owner.GetGlobalMousePosition();
        Vector2 viewportSize = owner.GetViewportRect().Size;
        Vector2 position = mousePosition + new Vector2(ItemTooltipCursorOffset, ItemTooltipCursorOffset);

        if (position.X + tooltipWidth > viewportSize.X - ItemTooltipCursorOffset)
        {
            position.X = mousePosition.X - tooltipWidth - ItemTooltipCursorOffset;
        }

        if (position.Y + tooltipHeight > viewportSize.Y - ItemTooltipCursorOffset)
        {
            position.Y = mousePosition.Y - tooltipHeight - ItemTooltipCursorOffset;
        }

        position.X = Math.Max(ItemTooltipCursorOffset, position.X);
        position.Y = Math.Max(ItemTooltipCursorOffset, position.Y);
        tooltip.GlobalPosition = position;
    }

    /// <summary>
    /// 把物品描述里的颜色标记解析成 RichTextLabel 的颜色段。
    /// </summary>
    private static void AppendItemTooltipText(RichTextLabel label, string text)
    {
        Stack<string> openedTags = new();
        StringBuilder buffer = new();

        for (int i = 0; i < text.Length; i++)
        {
            if (TryHandleItemTooltipTag(label, text, ref i, buffer, openedTags))
            {
                continue;
            }

            buffer.Append(text[i]);
        }

        FlushItemTooltipText(label, buffer);
        while (openedTags.Count > 0)
        {
            label.Pop();
            openedTags.Pop();
        }
    }

    /// <summary>
    /// 尝试处理一个物品提示颜色标记。
    /// </summary>
    private static bool TryHandleItemTooltipTag(RichTextLabel label, string text, ref int index, StringBuilder buffer, Stack<string> openedTags)
    {
        if (text[index] != '[')
        {
            return false;
        }

        int closeIndex = text.IndexOf(']', index + 1);
        if (closeIndex < 0)
        {
            return false;
        }

        string rawTag = text.Substring(index + 1, closeIndex - index - 1).Trim();
        if (TryAppendItemTooltipImage(label, text, rawTag, closeIndex, ref index, buffer))
        {
            return true;
        }

        if (TryOpenItemTooltipColor(label, rawTag, buffer, openedTags) || TryCloseItemTooltipColor(label, rawTag, buffer, openedTags))
        {
            index = closeIndex;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 尝试把 [img]资源路径[/img] 渲染成内联小图标。
    /// </summary>
    private static bool TryAppendItemTooltipImage(RichTextLabel label, string text, string rawTag, int tagCloseIndex, ref int index, StringBuilder buffer)
    {
        if (!string.Equals(rawTag, "img", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        const string closeTag = "[/img]";
        int imageEndIndex = text.IndexOf(closeTag, tagCloseIndex + 1, StringComparison.OrdinalIgnoreCase);
        if (imageEndIndex < 0)
        {
            return false;
        }

        string imagePath = text.Substring(tagCloseIndex + 1, imageEndIndex - tagCloseIndex - 1).Trim();
        Texture2D? image = LoadItemTooltipImage(imagePath);
        if (image == null)
        {
            FlushItemTooltipText(label, buffer);
            index = imageEndIndex + closeTag.Length - 1;
            return true;
        }

        FlushItemTooltipText(label, buffer);
        label.AddImage(image, ItemTooltipInlineImageSize, ItemTooltipInlineImageSize, null, InlineAlignment.Center, null);
        index = imageEndIndex + closeTag.Length - 1;
        return true;
    }

    /// <summary>
    /// 加载物品提示里的内联图片资源，并缓存成功加载的贴图。
    /// </summary>
    private static Texture2D? LoadItemTooltipImage(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return null;
        }

        if (ItemTooltipImageCache.TryGetValue(imagePath, out Texture2D? cachedImage))
        {
            return cachedImage;
        }

        try
        {
            Texture2D? image = ResourceLoader.Load<Texture2D>(imagePath);
            if (image != null)
            {
                ItemTooltipImageCache[imagePath] = image;
            }

            return image;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 物品提示图片加载失败：路径={imagePath}，异常={ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 尝试打开一个物品提示颜色标记，例如 [gold]。
    /// </summary>
    private static bool TryOpenItemTooltipColor(RichTextLabel label, string rawTag, StringBuilder buffer, Stack<string> openedTags)
    {
        if (rawTag.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        if (!ItemTooltipTagColors.TryGetValue(rawTag, out Color color))
        {
            FlushItemTooltipText(label, buffer);
            return true;
        }

        FlushItemTooltipText(label, buffer);
        label.PushColor(color);
        openedTags.Push(rawTag);
        return true;
    }

    /// <summary>
    /// 尝试关闭一个物品提示颜色标记，例如 [/gold]。
    /// </summary>
    private static bool TryCloseItemTooltipColor(RichTextLabel label, string rawTag, StringBuilder buffer, Stack<string> openedTags)
    {
        if (!rawTag.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        string tag = rawTag[1..].Trim();
        if (openedTags.Count == 0 || !string.Equals(openedTags.Peek(), tag, StringComparison.OrdinalIgnoreCase))
        {
            FlushItemTooltipText(label, buffer);
            return true;
        }

        FlushItemTooltipText(label, buffer);
        label.Pop();
        openedTags.Pop();
        return true;
    }

    /// <summary>
    /// 把暂存的普通文本写入物品悬停说明。
    /// </summary>
    private static void FlushItemTooltipText(RichTextLabel label, StringBuilder buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        label.AddText(buffer.ToString());
        buffer.Clear();
    }

    /// <summary>
    /// 估算物品悬停说明文本宽度，供自动换行和位置避让使用。
    /// </summary>
    private static float EstimateItemTooltipTextWidth(string text)
    {
        int longestLineUnits = 0;
        foreach (string line in StripItemTooltipKnownTags(text).Split('\n'))
        {
            longestLineUnits = Math.Max(longestLineUnits, CountItemTooltipDisplayUnits(line));
        }

        return Math.Clamp(longestLineUnits * 10.5f, ItemTooltipMinWidth, ItemTooltipMaxWidth - ItemTooltipPadding * 2f);
    }

    /// <summary>
    /// 估算物品悬停说明高度，避免在屏幕底部被截断。
    /// </summary>
    private static float EstimateItemTooltipHeight(string text)
    {
        float textWidth = EstimateItemTooltipTextWidth(text);
        int lines = 0;
        foreach (string line in StripItemTooltipKnownTags(text).Split('\n'))
        {
            lines += Math.Max(1, (int)Math.Ceiling(CountItemTooltipDisplayUnits(line) * 10.5f / textWidth));
        }

        return lines * (ItemTooltipFontSize + 8f) + ItemTooltipPadding * 2f;
    }

    /// <summary>
    /// 统计文本大概需要的显示宽度单位，中文按两个单位计算。
    /// </summary>
    private static int CountItemTooltipDisplayUnits(string text)
    {
        int units = 0;
        foreach (char character in text)
        {
            units += character > 127 ? 2 : 1;
        }

        return units;
    }

    /// <summary>
    /// 去掉已支持的颜色标记，供尺寸估算使用。
    /// </summary>
    private static string StripItemTooltipKnownTags(string text)
    {
        string stripped = text;
        foreach (string tag in ItemTooltipTagColors.Keys)
        {
            stripped = stripped.Replace($"[{tag}]", string.Empty, StringComparison.OrdinalIgnoreCase);
            stripped = stripped.Replace($"[/{tag}]", string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        return StripItemTooltipImages(stripped);
    }

    /// <summary>
    /// 去掉内联图片标记，供尺寸估算使用；图片按两个空格估算宽度。
    /// </summary>
    private static string StripItemTooltipImages(string text)
    {
        const string openTag = "[img]";
        const string closeTag = "[/img]";
        StringBuilder builder = new();
        int searchIndex = 0;

        while (searchIndex < text.Length)
        {
            int openIndex = text.IndexOf(openTag, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (openIndex < 0)
            {
                builder.Append(text, searchIndex, text.Length - searchIndex);
                break;
            }

            int closeIndex = text.IndexOf(closeTag, openIndex + openTag.Length, StringComparison.OrdinalIgnoreCase);
            if (closeIndex < 0)
            {
                builder.Append(text, searchIndex, text.Length - searchIndex);
                break;
            }

            builder.Append(text, searchIndex, openIndex - searchIndex);
            builder.Append("  ");
            searchIndex = closeIndex + closeTag.Length;
        }

        return builder.ToString();
    }
}
