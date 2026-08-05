namespace TheBookOfAges.TheBookOfAgesCode.Models;

/// <summary>
/// GM 下拉筛选项，使用稳定键值做逻辑判断，使用文本做界面展示。
/// </summary>
public sealed record GmFilterOption(string Key, string Text);
