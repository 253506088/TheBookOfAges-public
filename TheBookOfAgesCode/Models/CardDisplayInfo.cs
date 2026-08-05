using Godot;
using MegaCrit.Sts2.Core.Models;

namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// 卡片列表展示数据，避免 UI 直接依赖复杂的游戏对象。
/// 卡名 / 描述 / 类型 / 稀有度 / 费用 / 来源等字段都已经按当前游戏语言本地化好。
/// </summary>
public sealed class CardDisplayInfo
{
    /// <summary>
    /// 游戏内部的卡片 ID。
    /// </summary>
    public required ModelId Id { get; init; }

    /// <summary>
    /// 卡片 ID 字符串，用于控制台命令和字典 key。
    /// </summary>
    public required string IdText { get; init; }

    /// <summary>
    /// 已经本地化好的卡名（如启用「查看升级」会带 +1 后缀）。
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 已经本地化好的卡片描述。
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// 卡片类型枚举名（用于筛选匹配，如 "Attack"）。
    /// </summary>
    public required string TypeKey { get; init; }

    /// <summary>
    /// 卡片类型显示文本（如 "攻击" 或 "Attack"）。
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// 稀有度枚举名（用于筛选匹配，如 "Common"）。
    /// </summary>
    public required string RarityKey { get; init; }

    /// <summary>
    /// 稀有度显示文本（如 "普通"）。
    /// </summary>
    public required string Rarity { get; init; }

    /// <summary>
    /// 标准费用值；X 费时此值为 -1。
    /// </summary>
    public required int Cost { get; init; }

    /// <summary>
    /// 是否 X 费卡。
    /// </summary>
    public required bool CostsX { get; init; }

    /// <summary>
    /// 费用显示文本（如 "0"、"1"、"X"）。
    /// </summary>
    public required string CostText { get; init; }

    /// <summary>
    /// 卡池标识，使用卡池 Title 或 "Colorless"。
    /// </summary>
    public required string CharacterKey { get; init; }

    /// <summary>
    /// 卡池显示名（如 "铁甲战士" / "无色"）。
    /// </summary>
    public required string Character { get; init; }

    /// <summary>
    /// 是否公有/无色卡池。
    /// </summary>
    public required bool IsColorless { get; init; }

    /// <summary>
    /// 是否仅多人模式可用的卡片。
    /// </summary>
    public required bool IsMultiplayerOnly { get; init; }

    /// <summary>
    /// 是否还能升级（已到顶级或不可升级时为 false）。
    /// </summary>
    public required bool CanUpgrade { get; init; }

    /// <summary>
    /// 来源标识（"BaseGame" / "ThisMod" / 其他 mod 程序集名）。
    /// </summary>
    public required string SourceKey { get; init; }

    /// <summary>
    /// 来源显示文本。
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// 卡片立绘贴图，用作网格按钮图标。
    /// </summary>
    public required Texture2D? Icon { get; init; }

    /// <summary>
    /// 原始 CardModel 引用，用于发放和取得 mutable 副本。
    /// </summary>
    public required CardModel Card { get; init; }
}
