using Godot;
using TheBookOfAges.TheBookOfAgesCode.Models;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 卡片强化页的右键大图预览弹窗分文件：
/// 右键单张卡 → 屏幕中央弹一张升级版大卡预览，按 Esc / 点击空白处 / 再次右键 关闭。
/// 复用 TryCreateCardVisual(showUpgraded:true) 渲染，不引入新的 NCard 路径。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 升级预览大图整体放大倍数：在卡牌自助 CardScale=0.65 的基础上再放大，
    /// 让单卡占屏幕中央一大块，看清升级后的标题/能量水晶/描述差异。
    /// </summary>
    private const float UpgradePreviewScale = 1.4f;

    /// <summary>
    /// 升级预览的浮动根节点；同一时刻最多只存一个，再次右键先关旧弹窗再开新。
    /// </summary>
    private static Control? _upgradePreviewLayer;

    /// <summary>
    /// 弹出升级版大图预览：
    /// - 强化页右键卡片调用。
    /// - 即使本次右键的卡是已选中的，也只做预览，不改选中状态（OnUpgradeCardButtonGuiInput 已确保只在右键 down 触发）。
    /// - 已经有预览打开时，先 Close 再开新一份，避免叠加。
    /// </summary>
    private static void ShowUpgradePreviewPopup(CardDisplayInfo card)
    {
        if (_root == null || !GodotObject.IsInstanceValid(_root))
        {
            return;
        }

        CloseUpgradePreviewPopup();

        Control layer = new()
        {
            // 让整个屏幕都能接收鼠标事件，用来吃掉「点空白处关闭」
            MouseFilter = Control.MouseFilterEnum.Stop,
            ZIndex = 1500,
        };
        layer.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        // 半透明黑色遮罩：点击空白处也算点这层，处理"点外部关闭"
        ColorRect dim = new()
        {
            Color = new Color(0f, 0f, 0f, 0.72f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.GuiInput += inputEvent => OnUpgradePreviewBackgroundGuiInput(inputEvent);
        layer.AddChild(dim);

        // 居中容器：吃掉容器内的点击，避免点到大卡时也触发关闭
        CenterContainer center = new()
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(center);

        VBoxContainer panel = new()
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        panel.AddThemeConstantOverride("separation", 18);
        center.AddChild(panel);

        // 顶部标题（提示如何关闭）
        Label hint = new()
        {
            Text = GmUiText.CardUpgradePreviewTitle,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hint.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.55f));
        hint.AddThemeFontSizeOverride("font_size", 20);
        panel.AddChild(hint);

        // 大卡视觉：用一个 CenterContainer 包裹 holder，避免 NCard 子节点 anchor 居中靠 holder.Position 实现
        Control? cardVisual = TryCreateCardVisual(card, showUpgraded: true, slotIndex: 0, verbose: false);
        Control cardSlot = BuildUpgradePreviewCardSlot(card, cardVisual);
        panel.AddChild(cardSlot);

        // 底部关闭按钮（鼠标用户优先选择，比 Esc 更直观）
        Button closeButton = new()
        {
            Text = GmUiText.Pick("关闭", "Close"),
            CustomMinimumSize = new Vector2(160, 44),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        ApplyDangerActionButtonStyle(closeButton);
        closeButton.Pressed += CloseUpgradePreviewPopup;
        panel.AddChild(closeButton);

        _root.AddChild(layer);
        _upgradePreviewLayer = layer;

        // 注意：Esc 关闭由 HandleInput 拦截，但 HandleInput 当前只关 GM 面板。
        // 这里给 layer 自己挂一份 InputEvent 监听，吃掉 Esc 关弹窗，并阻止冒泡到面板 / 游戏。
        layer.GuiInput += inputEvent => OnUpgradePreviewLayerGuiInput(inputEvent);
    }

    /// <summary>
    /// 构造升级预览的卡片槽：把 NCard 视觉放在固定尺寸的 Control 内并整体放大显示。
    /// 失败时回退立绘 + 卡名文字。
    /// </summary>
    private static Control BuildUpgradePreviewCardSlot(CardDisplayInfo card, Control? cardVisual)
    {
        // 大卡尺寸 = NCard.defaultSize × CardScale × UpgradePreviewScale；用 holder.Scale 放大后视觉
        // 占用的实际像素也按这个比例。CardScale=0.65, defaultSize=300×422 → 195×274 → ×1.4 ≈ 273×384
        float slotW = CardButtonWidth * UpgradePreviewScale;
        float slotH = CardButtonHeight * UpgradePreviewScale;
        Control slot = new()
        {
            CustomMinimumSize = new Vector2(slotW, slotH),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ClipContents = false,
        };

        if (cardVisual is MegaCrit.Sts2.Core.Nodes.Cards.Holders.NPreviewCardHolder holder)
        {
            // 在 TryCreateCardVisual 设的 Scale 上叠一层 UpgradePreviewScale；
            // PivotOffset 已经设成 (0,0)，所以视觉左上 = holder.Position，按比例放大不需要再调中心。
            holder.Scale *= UpgradePreviewScale;
            slot.AddChild(holder);
            return slot;
        }

        // 兜底：仅展示立绘 + 名称
        VBoxContainer fallback = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        fallback.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        fallback.AddThemeConstantOverride("separation", 12);

        TextureRect portrait = new()
        {
            Texture = card.Icon,
            CustomMinimumSize = new Vector2(slotW, slotH * 0.7f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        fallback.AddChild(portrait);

        Label name = new()
        {
            Text = $"{card.Name}+1",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        name.AddThemeFontSizeOverride("font_size", 22);
        fallback.AddChild(name);

        slot.AddChild(fallback);
        return slot;
    }

    /// <summary>
    /// 升级预览背景点击事件：点击空白处（含遮罩）直接关闭。
    /// </summary>
    private static void OnUpgradePreviewBackgroundGuiInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed)
        {
            return;
        }

        // 左键 / 右键 / 中键任意按下都视为关闭意图
        CloseUpgradePreviewPopup();
    }

    /// <summary>
    /// 升级预览层级的键盘事件：Esc / 右键再次按下 时关闭。
    /// 这里和背景点击是两条独立的事件路径：点击空白处走 dim.GuiInput；
    /// 而在大卡上空 / 标题上空按 Esc 走 layer.GuiInput。
    /// </summary>
    private static void OnUpgradePreviewLayerGuiInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Key.Escape)
        {
            CloseUpgradePreviewPopup();
            return;
        }

        // 双重右键 = 再次按右键关闭，方便玩家"右键打开 → 看完 → 再右键关掉"。
        if (inputEvent is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Right)
        {
            CloseUpgradePreviewPopup();
        }
    }

    /// <summary>
    /// 关闭升级预览弹窗；多次调用安全。
    /// </summary>
    private static void CloseUpgradePreviewPopup()
    {
        if (_upgradePreviewLayer == null)
        {
            return;
        }

        if (GodotObject.IsInstanceValid(_upgradePreviewLayer))
        {
            _upgradePreviewLayer.QueueFree();
        }

        _upgradePreviewLayer = null;
    }
}
