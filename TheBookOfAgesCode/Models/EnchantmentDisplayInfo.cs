using Godot;
using MegaCrit.Sts2.Core.Models;

namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 附魔列表展示数据，避免 UI 直接散落读取游戏模型字段。
/// </summary>
public sealed class EnchantmentDisplayInfo
{
    /// <summary>
    /// 游戏内部的附魔 ID。
    /// </summary>
    public required ModelId Id { get; init; }

    /// <summary>
    /// 附魔 ID 字符串，用于选择字典和日志展示。
    /// </summary>
    public required string IdText { get; init; }

    /// <summary>
    /// 已经本地化好的附魔名称。
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 已经本地化好的附魔描述。
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// 来源标识（"BaseGame" / "Mods"）。
    /// </summary>
    public required string SourceKey { get; init; }

    /// <summary>
    /// 来源显示文本。
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// 附魔图标。
    /// </summary>
    public required Texture2D? Icon { get; init; }

    /// <summary>
    /// 当前附魔是否支持在安全模式下直接手填数值。
    /// </summary>
    public required bool CanCustomizeAmountSafely { get; init; }

    /// <summary>
    /// 当前附魔是否允许在关闭安全模式后强制手填数值。
    /// </summary>
    public required bool CanCustomizeAmountWhenUnsafe { get; init; }

    /// <summary>
    /// 当前附魔是否来自第三方 Mod。
    /// </summary>
    public required bool IsModEnchantment { get; init; }

    /// <summary>
    /// 当前附魔关于手填数值的说明文本。
    /// </summary>
    public required string CustomAmountHint { get; init; }

    /// <summary>
    /// 原始附魔模型，执行时必须 ToMutable 后再使用。
    /// </summary>
    public required EnchantmentModel Model { get; init; }
}
