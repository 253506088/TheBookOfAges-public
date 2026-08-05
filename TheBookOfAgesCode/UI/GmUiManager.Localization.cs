using Godot;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 页面国际化分文件：注册并刷新跟随当前游戏语言变化的静态文案绑定。
/// 内容来自 GmUiManager.cs 的本地化绑定段，行为与原实现一致，只做物理拆分。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 注册一个界面文案刷新动作，并在注册时立即执行一次。
    /// </summary>
    private static void RegisterLocalizedUiRefresher(Action refresher)
    {
        LocalizedUiRefreshers.Add(refresher);
        refresher();
    }

    /// <summary>
    /// 刷新所有创建时绑定的静态文案，避免界面语言切换后仍保留旧文本。
    /// </summary>
    private static void RefreshLocalizedUi()
    {
        foreach (Action refresher in LocalizedUiRefreshers)
        {
            refresher();
        }
    }

    /// <summary>
    /// 绑定标签文本，让它跟随当前游戏语言刷新。
    /// </summary>
    private static void BindLabelText(Label label, Func<string> textProvider)
    {
        RegisterLocalizedUiRefresher(() => label.Text = textProvider());
    }

    /// <summary>
    /// 绑定按钮或复选框文本，让它跟随当前游戏语言刷新。
    /// </summary>
    private static void BindButtonText(Button button, Func<string> textProvider)
    {
        RegisterLocalizedUiRefresher(() => button.Text = textProvider());
    }

    /// <summary>
    /// 绑定控件提示文本，让它跟随当前游戏语言刷新。
    /// </summary>
    private static void BindTooltip(Control control, Func<string> textProvider)
    {
        RegisterLocalizedUiRefresher(() => control.TooltipText = textProvider());
    }

    /// <summary>
    /// 绑定下拉框静态选项列表，并尽量保留当前选中项。
    /// </summary>
    private static void BindOptionItems(OptionButton optionButton, Func<IReadOnlyList<string>> itemsProvider, int defaultSelectedIndex = 0)
    {
        RegisterLocalizedUiRefresher(() =>
        {
            int selectedIndex = optionButton.Selected >= 0 ? optionButton.Selected : defaultSelectedIndex;
            optionButton.Clear();
            foreach (string item in itemsProvider())
            {
                optionButton.AddItem(item);
            }

            if (optionButton.ItemCount > 0)
            {
                optionButton.Select(Math.Clamp(selectedIndex, 0, optionButton.ItemCount - 1));
            }
        });
    }
}
