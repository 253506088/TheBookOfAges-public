using Godot;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 筛选状态模块分文件：负责把每个页签的搜索、下拉和筛选勾选项独立记住，并提供恢复默认筛选能力。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 单个页签的筛选状态快照，只记录列表筛选条件，不记录已选对象和危险操作开关。
    /// </summary>
    private sealed class GmFilterState
    {
        /// <summary>搜索框文本。</summary>
        public string SearchText { get; set; } = "";

        /// <summary>通用分类筛选 key。</summary>
        public string CategoryKey { get; set; } = "";

        /// <summary>通用稀有度筛选 key。</summary>
        public string RarityKey { get; set; } = "";

        /// <summary>通用来源筛选 key。</summary>
        public string SourceKey { get; set; } = "";

        /// <summary>卡片自助类型筛选 key。</summary>
        public string CardTypeKey { get; set; } = "";

        /// <summary>卡片自助费用筛选 key。</summary>
        public string CardCostKey { get; set; } = "";

        /// <summary>卡片自助排序字段 key。</summary>
        public string CardSortModeKey { get; set; } = nameof(CardSortMode.Default);

        /// <summary>卡片自助排序方向 key。</summary>
        public string CardSortDirectionKey { get; set; } = nameof(CardSortDirection.Asc);

        /// <summary>卡片自助是否包含多人模式卡片。</summary>
        public bool IncludeMultiplayerCards { get; set; }

        /// <summary>卡片自助是否查看升级后卡面。</summary>
        public bool ShowUpgradedCards { get; set; }
    }

    /// <summary>
    /// 每个页签各自的筛选状态；附魔页会按内部步骤拆成两份，避免附魔筛选和卡片筛选互相覆盖。
    /// </summary>
    private static readonly Dictionary<string, GmFilterState> PageFilterStates = new(StringComparer.Ordinal);

    /// <summary>
    /// 当前页签恢复默认筛选按钮。
    /// </summary>
    private static Button? _resetFiltersButton;

    /// <summary>
    /// 正在把记忆状态写回控件时为 true，避免程序改值触发搜索刷新和状态保存。
    /// </summary>
    private static bool _isApplyingFilterState;

    /// <summary>
    /// 保存当前页签的筛选状态。
    /// </summary>
    private static void SaveCurrentFilterState()
    {
        if (_isApplyingFilterState || !HasCurrentPageFilters())
        {
            return;
        }

        PageFilterStates[BuildCurrentFilterStateKey()] = new GmFilterState
        {
            SearchText = _searchInput?.Text ?? "",
            CategoryKey = GetSelectedFilterKeyOrEmpty(_categoryFilter),
            RarityKey = GetSelectedFilterKeyOrEmpty(_rarityFilter),
            SourceKey = GetSelectedFilterKeyOrEmpty(_sourceFilter),
            CardTypeKey = GetSelectedFilterKeyOrEmpty(_cardTypeFilter),
            CardCostKey = GetSelectedFilterKeyOrEmpty(_cardCostFilter),
            CardSortModeKey = GetSelectedFilterKeyOrEmpty(_cardSortModeFilter),
            CardSortDirectionKey = GetSelectedFilterKeyOrEmpty(_cardSortDirectionFilter),
            IncludeMultiplayerCards = _cardMultiplayerCheckbox?.ButtonPressed ?? false,
            ShowUpgradedCards = _cardShowUpgradeCheckbox?.ButtonPressed ?? false,
        };
    }

    /// <summary>
    /// 把当前页签记住的筛选状态写回实际控件；没有记忆时使用该页签默认筛选。
    /// </summary>
    private static void ApplyCurrentFilterState()
    {
        if (!HasCurrentPageFilters())
        {
            return;
        }

        GmFilterState state = GetCurrentFilterStateOrDefault();
        _isApplyingFilterState = true;
        try
        {
            if (_searchInput != null)
            {
                _searchInput.Text = state.SearchText;
            }

            SelectFilterByKeyOrDefault(_categoryFilter, state.CategoryKey);
            SelectFilterByKeyOrDefault(_rarityFilter, state.RarityKey);
            SelectFilterByKeyOrDefault(_sourceFilter, state.SourceKey);
            SelectFilterByKeyOrDefault(_cardTypeFilter, state.CardTypeKey);
            SelectFilterByKeyOrDefault(_cardCostFilter, state.CardCostKey);
            SelectFilterByKeyOrDefault(_cardSortModeFilter, state.CardSortModeKey);
            SelectFilterByKeyOrDefault(_cardSortDirectionFilter, state.CardSortDirectionKey);

            if (_cardMultiplayerCheckbox != null)
            {
                _cardMultiplayerCheckbox.SetPressedNoSignal(state.IncludeMultiplayerCards);
            }

            if (_cardShowUpgradeCheckbox != null)
            {
                _cardShowUpgradeCheckbox.SetPressedNoSignal(state.ShowUpgradedCards);
            }
        }
        finally
        {
            _isApplyingFilterState = false;
        }
    }

    /// <summary>
    /// 将当前页签筛选条件恢复为默认值，并刷新当前列表；不清空任何已选对象。
    /// </summary>
    private static void ResetCurrentPageFiltersToDefault()
    {
        if (!HasCurrentPageFilters())
        {
            return;
        }

        PageFilterStates[BuildCurrentFilterStateKey()] = BuildDefaultFilterState();
        ApplyCurrentFilterState();
        RefreshCurrentPage();
    }

    /// <summary>
    /// 根据当前页签刷新“恢复默认筛选”按钮的显隐。
    /// </summary>
    private static void UpdateResetFiltersButtonVisibility()
    {
        if (_resetFiltersButton == null)
        {
            return;
        }

        _resetFiltersButton.Visible = HasCurrentPageFilters();
    }

    /// <summary>
    /// 判断当前页签是否存在列表筛选条件。
    /// </summary>
    private static bool HasCurrentPageFilters()
    {
        return _currentPage != GmPageMode.Chronicle;
    }

    /// <summary>
    /// 读取当前页签的筛选状态；没有保存过时返回默认状态。
    /// </summary>
    private static GmFilterState GetCurrentFilterStateOrDefault()
    {
        return PageFilterStates.TryGetValue(BuildCurrentFilterStateKey(), out GmFilterState? state)
            ? state
            : BuildDefaultFilterState();
    }

    /// <summary>
    /// 生成当前页签的默认筛选状态。
    /// </summary>
    private static GmFilterState BuildDefaultFilterState()
    {
        GmFilterState state = new();
        if (_currentPage == GmPageMode.GrantCards)
        {
            state.CategoryKey = CardService.GetCurrentCharacterKey() ?? "";
            state.CardSortModeKey = nameof(CardSortMode.Default);
            state.CardSortDirectionKey = nameof(CardSortDirection.Asc);
        }

        return state;
    }

    /// <summary>
    /// 构造当前页签的筛选状态 key；附魔页按内部步骤分开记忆。
    /// </summary>
    private static string BuildCurrentFilterStateKey()
    {
        return _currentPage == GmPageMode.EnchantCards
            ? $"{_currentPage}:{_enchantmentPageStep}"
            : _currentPage.ToString();
    }

    /// <summary>
    /// 读取下拉框当前稳定 key；下拉框为空时返回空字符串。
    /// </summary>
    private static string GetSelectedFilterKeyOrEmpty(OptionButton? optionButton)
    {
        return optionButton == null ? "" : GetSelectedFilterKey(optionButton);
    }

    /// <summary>
    /// 按 key 选择下拉项；key 为空或找不到时回到第 0 项。
    /// </summary>
    private static void SelectFilterByKeyOrDefault(OptionButton? optionButton, string? key)
    {
        if (optionButton == null)
        {
            return;
        }

        int index = 0;
        if (!string.IsNullOrEmpty(key) &&
            FilterOptionKeys.TryGetValue(optionButton, out List<string>? keys))
        {
            for (int i = 0; i < keys.Count; i++)
            {
                if (string.Equals(keys[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
        }

        if (optionButton.ItemCount > 0)
        {
            optionButton.Select(index);
        }
    }
}
