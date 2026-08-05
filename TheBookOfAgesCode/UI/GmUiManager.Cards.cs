using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.Config;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.Services;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 卡片模块分文件：卡片自助页的虚拟化滚动渲染、卡片删除/强化页、交互逻辑。
/// 核心优化：仿游戏原生百科的滑动窗口虚拟化，只创建视口内可见数量的 slot，
/// 滚动/筛选时通过 ReassignToCard 换数据，不新建/不销毁节点。
/// </summary>
public static partial class GmUiManager
{
    // ============================================================
    // 虚拟化卡片自助页：核心数据结构
    // ============================================================

    /// <summary>
    /// 虚拟化 slot：固定数量的 Button（内含 NPreviewCardHolder + NCard），
    /// 通过 ReassignToCard 复用同一组节点渲染不同卡片数据。
    /// </summary>
    private sealed class CardSlot
    {
        /// <summary>外层按钮，承载点击/拖拽交互。</summary>
        public Button Button = null!;
        /// <summary>卡片 holder，负责缩放和视觉渲染。</summary>
        public NPreviewCardHolder? Holder;
        /// <summary>holder 内部的 NCard 实例。</summary>
        public NCard? Card;
        /// <summary>右上角「×N」选中数量徽章。</summary>
        public PanelContainer? Badge;
        /// <summary>当前 slot 绑定的数据索引（-1 表示空闲/隐藏）。</summary>
        public int DataIndex = -1;
        /// <summary>当前 slot 绑定的卡片 IdText，用于快速判断是否需要刷新。</summary>
        public string? BoundCardId;
    }

    /// <summary>虚拟化 slot 池：固定数量，只在首次进入卡片页时创建。</summary>
    private static readonly List<CardSlot> _cardSlots = new();

    /// <summary>当前经过筛选+排序后的卡片数据列表（纯数据，不涉及 UI 节点）。</summary>
    private static List<CardDisplayInfo> _virtualizedFilteredCards = new();

    /// <summary>当前 bucket 维度的全量卡片数据缓存（不含用户筛选）。</summary>
    private static List<CardDisplayInfo> _virtualizedAllCards = new();

    /// <summary>bucket 数据缓存：避免切换升级版/多人模式时重复调用 GetAllCardsForBucket。</summary>
    private static readonly Dictionary<(bool ShowUpgraded, bool IncludeMultiplayer), List<CardDisplayInfo>> _bucketDataCache = new();

    /// <summary>当前虚拟化使用的 bucket key。</summary>
    private static (bool ShowUpgraded, bool IncludeMultiplayer)? _virtualizedBucketKey;

    /// <summary>虚拟化卡片容器：绝对定位，slot 通过 Position 手动布局。</summary>
    private static Control? _cardVirtualContainer;

    /// <summary>虚拟化占位容器：撑出正确的滚动高度。</summary>
    private static Control? _cardVirtualSpacer;

    /// <summary>虚拟化列数。</summary>
    private static int _virtualizedColumns = 4;

    /// <summary>虚拟化行间距。</summary>
    private const float CardVirtualGapX = 12f;
    private const float CardVirtualGapY = 12f;

    /// <summary>上一次滚动位置，用于判断是否需要刷新 slot。</summary>
    private static float _lastCardScrollY = -1f;

    /// <summary>上一次视口高度，用于检测窗口大小变化。</summary>
    private static float _lastCardViewportHeight = -1f;

    /// <summary>虚拟化 slot 是否已初始化。</summary>
    private static bool _cardSlotsInitialized;

    /// <summary>上次构建虚拟化数据时的语言。</summary>
    private static bool? _virtualizedLanguageIsChinese;

    /// <summary>搜索输入防抖序列号。</summary>
    private static int _cardSearchDebounceSeq;

    /// <summary>搜索输入防抖窗口（秒）。</summary>
    private const float CardSearchDebounceSeconds = 0.3f;

    /// <summary>卡片渲染诊断计数器。</summary>
    private static int _cardRenderDebugCounter;
    private const int CardRenderDebugLimit = 3;


    // ============================================================
    // 虚拟化卡片自助页：初始化与 slot 池管理
    // ============================================================

    /// <summary>
    /// 计算虚拟化需要的 slot 数量：(可见行数 + 2 行缓冲) × 列数。
    /// 确保滚动时有足够的 slot 覆盖视口 + 上下各一行缓冲。
    /// </summary>
    private static int CalculateSlotCount()
    {
        float viewportHeight = _gridScroll?.Size.Y ?? 600f;
        float rowHeight = CardButtonHeight + CardVirtualGapY;
        int visibleRows = (int)Math.Ceiling(viewportHeight / rowHeight) + 2;
        return visibleRows * _virtualizedColumns;
    }

    /// <summary>slot 池是否正在异步初始化中。</summary>
    private static bool _cardSlotsBuilding;

    /// <summary>
    /// 初始化虚拟化 slot 池：异步分帧创建，每帧创建若干个 slot，避免单帧卡死。
    /// 只在首次进入卡片自助页时调用一次。
    /// </summary>
    private static void EnsureCardSlotsInitialized()
    {
        if (_cardSlotsInitialized || _cardSlotsBuilding)
        {
            return;
        }

        if (_cardVirtualContainer == null || _gridScroll == null)
        {
            return;
        }

        _cardSlotsBuilding = true;
        int slotCount = CalculateSlotCount();
        MainFile.Logger.Info($"[卡片自助-虚拟化] 开始异步初始化 slot 池：slotCount={slotCount}，columns={_virtualizedColumns}");
        _ = BuildCardSlotsAsync(slotCount);
    }

    /// <summary>每帧创建的 slot 数量：平衡首屏速度与单帧卡顿。</summary>
    private const int SlotBuildBatchSize = 4;

    /// <summary>
    /// 异步分帧创建 slot 池：每帧创建 SlotBuildBatchSize 个，创建完毕后立即刷新视口。
    /// </summary>
    private static async Task BuildCardSlotsAsync(int slotCount)
    {
        try
        {
            for (int i = 0; i < slotCount; i++)
            {
                CardSlot slot = CreateCardSlot(i);
                _cardSlots.Add(slot);

                if (_cardVirtualContainer != null && GodotObject.IsInstanceValid(_cardVirtualContainer))
                {
                    _cardVirtualContainer.AddChild(slot.Button);
                    slot.Button.Visible = false;
                }

                // 每 SlotBuildBatchSize 个让出一帧
                if ((i + 1) % SlotBuildBatchSize == 0 && i + 1 < slotCount)
                {
                    // 已创建的 slot 先刷新到视口（渐进式显示）
                    if (_virtualizedFilteredCards.Count > 0)
                    {
                        _lastCardScrollY = -1f;
                        RefreshVirtualizedSlots();
                    }
                    await WaitOneFrameAsync();
                }
            }

            _cardSlotsInitialized = true;
            _cardSlotsBuilding = false;
            MainFile.Logger.Info($"[卡片自助-虚拟化] slot 池初始化完成，共 {_cardSlots.Count} 个");

            // 最终刷新一次确保所有 slot 正确显示
            _lastCardScrollY = -1f;
            RefreshVirtualizedSlots();
        }
        catch (Exception ex)
        {
            _cardSlotsBuilding = false;
            MainFile.Logger.Info($"[卡片自助-虚拟化] slot 池初始化异常：{ex.Message}");
        }
    }

    /// <summary>等下一帧 ProcessFrame 信号。</summary>
    private static async Task WaitOneFrameAsync()
    {
        if (_root == null || !GodotObject.IsInstanceValid(_root))
        {
            return;
        }
        SceneTree? tree = _root.GetTree();
        if (tree == null)
        {
            return;
        }
        await _root.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>
    /// 创建单个虚拟化 slot：Button + NCard + NPreviewCardHolder + 徽章。
    /// slot 创建后不绑定任何卡片数据，等 OnCardScrollChanged 时动态绑定。
    /// </summary>
    private static CardSlot CreateCardSlot(int slotIndex)
    {
        Button button = new()
        {
            CustomMinimumSize = new Vector2(CardButtonWidth, CardButtonHeight),
            Size = new Vector2(CardButtonWidth, CardButtonHeight),
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            ClipContents = false,
        };

        CardSlot slot = new() { Button = button };

        // 创建 NCard + NPreviewCardHolder（用一个占位 CardModel 初始化）
        try
        {
            // 用 ModelDb.AllCards 的第一张卡做初始化占位，后续 ReassignSlot 会换掉
            CardModel? placeholderModel = null;
            foreach (CardModel? card in ModelDb.AllCards)
            {
                if (card != null)
                {
                    placeholderModel = card;
                    break;
                }
            }

            if (placeholderModel == null)
            {
                MainFile.Logger.Info($"[卡片自助-虚拟化] slot #{slotIndex} 创建失败：无可用 CardModel");
                return slot;
            }

            NCard? nCard = NCard.Create(placeholderModel, ModelVisibility.Visible);
            if (nCard == null)
            {
                MainFile.Logger.Info($"[卡片自助-虚拟化] slot #{slotIndex} NCard.Create 返回 null");
                return slot;
            }

            NPreviewCardHolder? holder = NPreviewCardHolder.Create(nCard, false, false);
            if (holder == null)
            {
                nCard.QueueFree();
                MainFile.Logger.Info($"[卡片自助-虚拟化] slot #{slotIndex} NPreviewCardHolder.Create 返回 null");
                return slot;
            }

            holder.SetCardScale(new Vector2(CardScale, CardScale));
            holder.MouseFilter = Control.MouseFilterEnum.Ignore;
            holder.PivotOffset = Vector2.Zero;
            holder.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            holder.Size = NCard.defaultSize;

            slot.Holder = holder;
            slot.Card = nCard;
            button.AddChild(holder);

            // holder 进入树后刷新视觉 + 设置鼠标穿透 + 居中
            holder.Ready += () =>
            {
                MakeSubtreeMouseTransparent(holder);
                CenterHolderInButton(button, holder);
                if (slot.DataIndex >= 0 && slot.DataIndex < _virtualizedFilteredCards.Count)
                {
                    CardDisplayInfo card = _virtualizedFilteredCards[slot.DataIndex];
                    bool showUpgraded = _cardShowUpgradeCheckbox?.ButtonPressed ?? false;
                    CardPreviewMode previewMode = showUpgraded ? CardPreviewMode.Upgrade : CardPreviewMode.Normal;
                    RefreshCardVisualOnReady(card, nCard, previewMode);
                }
            };
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"[卡片自助-虚拟化] slot #{slotIndex} 创建异常：{ex.Message}");
        }

        // 右上角「×N」选中数量徽章
        PanelContainer countBadge = CreateCardCountBadge();
        button.AddChild(countBadge);
        slot.Badge = countBadge;

        // 鼠标交互：动态查 slot.DataIndex 获取当前绑定的卡
        button.GuiInput += inputEvent => OnVirtualizedCardSlotGuiInput(inputEvent, slot);

        // 拖拽批量选择支持：回调动态查 slot.DataIndex
        RegisterDragSelectableButton(
            button,
            isSelected: () =>
            {
                if (slot.DataIndex < 0 || slot.DataIndex >= _virtualizedFilteredCards.Count) return false;
                CardDisplayInfo c = _virtualizedFilteredCards[slot.DataIndex];
                return SelectedGrantCards.TryGetValue(c.IdText, out CardSelectionInfo? sel) && sel.Count > 0;
            },
            select: () =>
            {
                if (slot.DataIndex < 0 || slot.DataIndex >= _virtualizedFilteredCards.Count) return;
                CardDisplayInfo c = _virtualizedFilteredCards[slot.DataIndex];
                if (!SelectedGrantCards.ContainsKey(c.IdText))
                {
                    SelectedGrantCards[c.IdText] = new CardSelectionInfo { Info = c, Count = 1 };
                }
                SyncCardCountBadge(slot.Button, c);
                RefreshCardButtonVisual(slot.Button, c);
            },
            unselect: () =>
            {
                if (slot.DataIndex < 0 || slot.DataIndex >= _virtualizedFilteredCards.Count) return;
                CardDisplayInfo c = _virtualizedFilteredCards[slot.DataIndex];
                SelectedGrantCards.Remove(c.IdText);
                SyncCardCountBadge(slot.Button, c);
                RefreshCardButtonVisual(slot.Button, c);
            },
            singleClick: ctrlPressed =>
            {
                if (slot.DataIndex < 0 || slot.DataIndex >= _virtualizedFilteredCards.Count) return;
                CardDisplayInfo c = _virtualizedFilteredCards[slot.DataIndex];
                int delta = ctrlPressed ? 5 : 1;
                AdjustCardSelection(c, delta);
                UpdateVirtualizedSlotAfterSelectionChange(slot, c);
            },
            refresh: () =>
            {
                if (slot.DataIndex < 0 || slot.DataIndex >= _virtualizedFilteredCards.Count) return;
                CardDisplayInfo c = _virtualizedFilteredCards[slot.DataIndex];
                SyncCardCountBadge(slot.Button, c);
                RefreshCardButtonVisual(slot.Button, c);
            },
            keepBindingOnTreeExit: true); // slot 永远不离开树，不需要自动注销

        return slot;
    }

    /// <summary>
    /// 把 holder 在 button 内居中。
    /// </summary>
    private static void CenterHolderInButton(Button button, NPreviewCardHolder holder)
    {
        if (!GodotObject.IsInstanceValid(button) || !GodotObject.IsInstanceValid(holder))
        {
            return;
        }

        Vector2 parentSize = button.Size;
        if (parentSize.X <= 0f || parentSize.Y <= 0f)
        {
            // 延迟到下一帧
            Callable.From(() => CenterHolderInButton(button, holder)).CallDeferred();
            return;
        }

        holder.Position = parentSize / 2f;
    }


    // ============================================================
    // 虚拟化卡片自助页：数据层与滚动驱动
    // ============================================================

    /// <summary>
    /// 刷新卡片自助页（虚拟化版）：
    /// 1. 确保 slot 池已初始化
    /// 2. 加载/缓存当前 bucket 的全量数据
    /// 3. 应用用户筛选得到 _virtualizedFilteredCards
    /// 4. 更新占位高度 + 刷新可见 slot
    /// </summary>
    private static void RefreshCardGrid()
    {
        if (_gridScroll == null || _searchInput == null || _categoryFilter == null || _rarityFilter == null ||
            _sourceFilter == null || _cardTypeFilter == null || _cardCostFilter == null)
        {
            return;
        }

        bool includeMultiplayer = _cardMultiplayerCheckbox?.ButtonPressed ?? false;
        bool showUpgraded = _cardShowUpgradeCheckbox?.ButtonPressed ?? false;
        var bucketKey = (showUpgraded, includeMultiplayer);

        // 计算列数
        UpdateVirtualizedColumns();

        // 确保 slot 池就绪
        EnsureCardSlotsInitialized();

        // 确保数据层就绪（语言切换检测 + bucket 数据加载）
        EnsureVirtualizedData(bucketKey);

        // 应用筛选 + 刷新视口
        ApplyVirtualizedFilter();
    }

    /// <summary>
    /// 计算虚拟化列数（基于 _gridScroll 宽度）。
    /// </summary>
    private static void UpdateVirtualizedColumns()
    {
        if (_gridScroll == null)
        {
            return;
        }

        float availableWidth = _gridScroll.Size.X - 20f; // 减去滚动条宽度
        int cols = Math.Max(1, (int)((availableWidth + CardVirtualGapX) / (CardButtonWidth + CardVirtualGapX)));
        _virtualizedColumns = cols;
    }

    /// <summary>
    /// 确保虚拟化数据层就绪：检测语言切换、加载 bucket 数据。
    /// </summary>
    private static void EnsureVirtualizedData((bool ShowUpgraded, bool IncludeMultiplayer) bucketKey)
    {
        // 语言切换检测
        bool currentIsChinese = GmLocalizationService.IsChinese;
        if (_virtualizedLanguageIsChinese.HasValue && _virtualizedLanguageIsChinese != currentIsChinese)
        {
            MainFile.Logger.Info($"[卡片自助-虚拟化] 检测到语言切换，清空数据缓存");
            _virtualizedAllCards.Clear();
            _virtualizedFilteredCards.Clear();
            _virtualizedBucketKey = null;
            _bucketDataCache.Clear();
            _upgradedModelCache.Clear();
            InvalidateAllSlotBindings();
        }
        _virtualizedLanguageIsChinese = currentIsChinese;

        // bucket 数据加载（带缓存：同一 bucketKey 只加载一次）
        if (_virtualizedBucketKey != bucketKey || _virtualizedAllCards.Count == 0)
        {
            // 尝试从缓存获取
            if (!_bucketDataCache.TryGetValue(bucketKey, out List<CardDisplayInfo>? cachedData))
            {
                IReadOnlyList<CardDisplayInfo> allCards = CardService.GetAllCardsForBucket(
                    includeMultiplayer: bucketKey.IncludeMultiplayer,
                    showUpgraded: bucketKey.ShowUpgraded);
                cachedData = new List<CardDisplayInfo>(allCards);
                _bucketDataCache[bucketKey] = cachedData;
                MainFile.Logger.Info($"[卡片自助-虚拟化] 加载 bucket 数据：showUpgraded={bucketKey.ShowUpgraded}，includeMultiplayer={bucketKey.IncludeMultiplayer}，共 {allCards.Count} 张");
            }

            _virtualizedAllCards = cachedData;
            _virtualizedBucketKey = bucketKey;

            // bucket 切换时必须清除所有 slot 的绑定状态，
            // 否则 IdText 相同的卡片会命中缓存跳过 ReassignToCard，导致视觉不更新。
            InvalidateAllSlotBindings();
        }
    }

    /// <summary>
    /// 清除所有 slot 的绑定状态，强制下次 RefreshVirtualizedSlots 时重新 ReassignToCard。
    /// </summary>
    private static void InvalidateAllSlotBindings()
    {
        foreach (CardSlot slot in _cardSlots)
        {
            slot.DataIndex = -1;
            slot.BoundCardId = null;
        }
    }

    /// <summary>
    /// 应用用户筛选 + 排序，更新占位高度，刷新可见 slot。
    /// 这是所有筛选/搜索/排序变更的统一入口，纯数据操作 + 少量 slot 刷新，极快。
    /// </summary>
    private static void ApplyVirtualizedFilter()
    {
        // 1. 筛选
        List<CardDisplayInfo> filtered = _virtualizedAllCards.Where(MatchesActiveUserFilter).ToList();

        // 2. 排序
        _virtualizedFilteredCards = SortActiveCardList(filtered);

        // 3. 更新占位高度（撑滚动条）
        UpdateVirtualizedSpacerHeight();

        // 4. 强制刷新所有可见 slot
        _lastCardScrollY = -1f; // 强制刷新
        RefreshVirtualizedSlots();

        // 5. 更新底栏数字
        UpdateSelectionStatus(_virtualizedFilteredCards.Count);
    }

    /// <summary>
    /// 更新虚拟化占位容器高度，让 ScrollContainer 产生正确的滚动范围。
    /// </summary>
    private static void UpdateVirtualizedSpacerHeight()
    {
        if (_cardVirtualSpacer == null)
        {
            return;
        }

        int totalRows = (_virtualizedFilteredCards.Count + _virtualizedColumns - 1) / _virtualizedColumns;
        float totalHeight = totalRows * (CardButtonHeight + CardVirtualGapY) - CardVirtualGapY;
        totalHeight = Math.Max(totalHeight, 0f);
        _cardVirtualSpacer.CustomMinimumSize = new Vector2(0, totalHeight);
    }

    /// <summary>
    /// 滚动回调：根据当前滚动位置计算应显示的数据范围，刷新 slot 绑定。
    /// </summary>
    private static void RefreshVirtualizedSlots()
    {
        if (_gridScroll == null || _cardVirtualContainer == null || _cardSlots.Count == 0)
        {
            return;
        }

        float scrollY = _gridScroll.ScrollVertical;
        float viewportHeight = _gridScroll.Size.Y;

        // 如果滚动位置和视口高度都没变，跳过刷新
        if (Math.Abs(scrollY - _lastCardScrollY) < 1f && Math.Abs(viewportHeight - _lastCardViewportHeight) < 1f)
        {
            return;
        }
        _lastCardScrollY = scrollY;
        _lastCardViewportHeight = viewportHeight;

        float rowHeight = CardButtonHeight + CardVirtualGapY;
        float colWidth = CardButtonWidth + CardVirtualGapX;

        // 计算视口内第一个可见行
        int firstVisibleRow = Math.Max(0, (int)(scrollY / rowHeight) - 1); // 多取一行缓冲
        int firstDataIndex = firstVisibleRow * _virtualizedColumns;

        bool showUpgraded = _cardShowUpgradeCheckbox?.ButtonPressed ?? false;

        // 只遍历已创建的 slot（异步构建中可能还没全部就绪）
        int availableSlots = _cardSlots.Count;
        for (int i = 0; i < availableSlots; i++)
        {
            int dataIdx = firstDataIndex + i;
            CardSlot slot = _cardSlots[i];

            if (dataIdx < 0 || dataIdx >= _virtualizedFilteredCards.Count)
            {
                // 超出数据范围，隐藏 slot
                if (slot.Button.Visible)
                {
                    slot.Button.Visible = false;
                    slot.DataIndex = -1;
                    slot.BoundCardId = null;
                }
                continue;
            }

            CardDisplayInfo card = _virtualizedFilteredCards[dataIdx];

            // 计算 slot 的绝对位置
            int row = dataIdx / _virtualizedColumns;
            int col = dataIdx % _virtualizedColumns;
            Vector2 pos = new(col * colWidth, row * rowHeight);
            slot.Button.Position = pos;
            slot.Button.Visible = true;

            // 如果 slot 已经绑定了正确的卡片，只需同步徽章（选中状态可能变了）
            if (slot.DataIndex == dataIdx && slot.BoundCardId == card.IdText)
            {
                SyncCardCountBadge(slot.Button, card);
                RefreshCardButtonVisual(slot.Button, card);
                continue;
            }

            // 需要重新绑定：用 ReassignToCard 换数据
            ReassignSlotToCard(slot, card, dataIdx, showUpgraded);
        }
    }

    /// <summary>
    /// 将 slot 重新绑定到新的卡片数据：通过 ReassignToCard 复用 NCard 实例。
    /// </summary>
    private static void ReassignSlotToCard(CardSlot slot, CardDisplayInfo card, int dataIndex, bool showUpgraded)
    {
        slot.DataIndex = dataIndex;
        slot.BoundCardId = card.IdText;

        if (slot.Holder == null || slot.Card == null ||
            !GodotObject.IsInstanceValid(slot.Holder) || !GodotObject.IsInstanceValid(slot.Card))
        {
            slot.Button.Visible = false;
            return;
        }

        // 决定渲染用的 CardModel（使用缓存避免重复克隆）
        CardModel modelToRender = GetModelToRender(card, showUpgraded);

        // 核心：用游戏自带的 ReassignToCard 换模型数据，不新建 NCard
        try
        {
            slot.Holder.ReassignToCard(modelToRender, PileType.None, null, ModelVisibility.Visible);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"[卡片自助-虚拟化] ReassignToCard 失败：{card.IdText} / {ex.Message}");
            slot.Button.Visible = false;
            return;
        }

        // 确保 holder 居中
        CenterHolderInButton(slot.Button, slot.Holder);

        // 同步徽章 + 选中外框
        SyncCardCountBadge(slot.Button, card);
        RefreshCardButtonVisual(slot.Button, card);

        // 更新 tooltip
        RegisterItemTooltip(slot.Button, BuildCardTooltip(card));
    }

    /// <summary>升级版 CardModel 缓存：IdText → 升级后的 mutable clone。避免每次 ReassignSlot 都重新克隆。</summary>
    private static readonly Dictionary<string, CardModel> _upgradedModelCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 获取渲染用的 CardModel：升级版走缓存，基础版直接返回原模型。
    /// </summary>
    private static CardModel GetModelToRender(CardDisplayInfo card, bool showUpgraded)
    {
        if (!showUpgraded || !card.Card.IsUpgradable || card.Card.CurrentUpgradeLevel >= card.Card.MaxUpgradeLevel)
        {
            return card.Card;
        }

        if (_upgradedModelCache.TryGetValue(card.IdText, out CardModel? cached))
        {
            return cached;
        }

        try
        {
            CardModel upgradedClone = card.Card.ToMutable();
            upgradedClone.UpgradeInternal();
            _upgradedModelCache[card.IdText] = upgradedClone;
            return upgradedClone;
        }
        catch
        {
            return card.Card;
        }
    }

    /// <summary>
    /// 滚动事件监听：ScrollContainer 的 VScrollBar 值变化时触发。
    /// </summary>
    private static void OnCardVirtualScrollChanged(double _value)
    {
        if (_currentPage == GmPageMode.GrantCards)
        {
            RefreshVirtualizedSlots();
        }
    }


    // ============================================================
    // 虚拟化卡片自助页：交互逻辑（动态绑定）
    // ============================================================

    /// <summary>
    /// 虚拟化 slot 的鼠标事件：只处理右键清零（左键由拖拽选择模块接管）。
    /// </summary>
    private static void OnVirtualizedCardSlotGuiInput(InputEvent inputEvent, CardSlot slot)
    {
        if (inputEvent is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed)
        {
            return;
        }

        if (slot.DataIndex < 0 || slot.DataIndex >= _virtualizedFilteredCards.Count)
        {
            return;
        }

        CardDisplayInfo card = _virtualizedFilteredCards[slot.DataIndex];

        if (mouseEvent.ButtonIndex == MouseButton.Right)
        {
            SelectedGrantCards.Remove(card.IdText);
            UpdateVirtualizedSlotAfterSelectionChange(slot, card);
            slot.Button.AcceptEvent();
        }
    }

    /// <summary>
    /// 选择数量变化后同步 slot 的徽章 + 外框 + 底栏数字。
    /// </summary>
    private static void UpdateVirtualizedSlotAfterSelectionChange(CardSlot slot, CardDisplayInfo card)
    {
        SyncCardCountBadge(slot.Button, card);
        RefreshCardButtonVisual(slot.Button, card);
        UpdateSelectionStatus(_virtualizedFilteredCards.Count);
    }

    /// <summary>
    /// 搜索框内容变更回调：
    /// - 卡片页：走 300ms 防抖后调 ApplyVirtualizedFilter。
    /// - 其他页签：原地 RefreshCurrentPage。
    /// </summary>
    private static async void OnSearchInputChanged()
    {
        if (_isApplyingFilterState)
        {
            return;
        }

        SaveCurrentFilterState();

        // 非卡片页直接刷
        if (_currentPage != GmPageMode.GrantCards)
        {
            RefreshCurrentPage();
            return;
        }

        int mySeq = ++_cardSearchDebounceSeq;
        if (_root == null || !GodotObject.IsInstanceValid(_root))
        {
            return;
        }
        SceneTree? tree = _root.GetTree();
        if (tree == null)
        {
            return;
        }

        // 等 300ms 防抖
        SceneTreeTimer timer = tree.CreateTimer(CardSearchDebounceSeconds);
        await _root.ToSignal(timer, SceneTreeTimer.SignalName.Timeout);

        // 序列号已变 / 页签已切 → 丢弃
        if (mySeq != _cardSearchDebounceSeq || _currentPage != GmPageMode.GrantCards)
        {
            return;
        }

        ApplyVirtualizedFilter();
    }

    /// <summary>当前用户态筛选条件下，info 是否应该显示。</summary>
    private static bool MatchesActiveUserFilter(CardDisplayInfo info)
    {
        if (_searchInput == null || _categoryFilter == null || _rarityFilter == null ||
            _sourceFilter == null || _cardTypeFilter == null || _cardCostFilter == null)
        {
            return true;
        }
        return CardService.MatchesUserFilters(
            info,
            _searchInput.Text,
            GetSelectedFilterKey(_categoryFilter),
            GetSelectedFilterKey(_cardTypeFilter),
            GetSelectedFilterKey(_rarityFilter),
            GetSelectedFilterKey(_cardCostFilter),
            GetSelectedFilterKey(_sourceFilter));
    }

    /// <summary>
    /// 按卡牌自助当前排序控件返回排序后的卡牌列表。
    /// </summary>
    private static List<CardDisplayInfo> SortActiveCardList(IReadOnlyList<CardDisplayInfo> cards)
    {
        CardSortMode mode = GetActiveCardSortMode();
        CardSortDirection direction = GetActiveCardSortDirection();
        if (mode == CardSortMode.Default)
        {
            return direction == CardSortDirection.Desc
                ? cards.Reverse().ToList()
                : cards.ToList();
        }

        IOrderedEnumerable<CardDisplayInfo> ordered = mode switch
        {
            CardSortMode.Cost => cards.OrderBy(GetCardCostSortValue),
            CardSortMode.Damage => cards.OrderBy(info => CardService.GetCardDamageSortValue(info)),
            CardSortMode.Name => cards.OrderBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => cards.OrderBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase),
        };

        ordered = mode switch
        {
            CardSortMode.Cost => ordered.ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase),
            CardSortMode.Damage => ordered.ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase),
            CardSortMode.Name => ordered.ThenBy(info => info.IdText, StringComparer.OrdinalIgnoreCase),
            _ => ordered,
        };

        return direction == CardSortDirection.Desc
            ? ordered.Reverse().ToList()
            : ordered.ToList();
    }

    private static CardSortMode GetActiveCardSortMode()
    {
        string key = _cardSortModeFilter == null ? nameof(CardSortMode.Default) : GetSelectedFilterKey(_cardSortModeFilter);
        return Enum.TryParse(key, out CardSortMode mode) ? mode : CardSortMode.Default;
    }

    private static CardSortDirection GetActiveCardSortDirection()
    {
        string key = _cardSortDirectionFilter == null ? nameof(CardSortDirection.Asc) : GetSelectedFilterKey(_cardSortDirectionFilter);
        return Enum.TryParse(key, out CardSortDirection direction) ? direction : CardSortDirection.Asc;
    }

    private static int GetCardCostSortValue(CardDisplayInfo info)
    {
        return info.CostsX ? int.MaxValue : info.Cost;
    }

    /// <summary>
    /// 给指定卡片增加选择数量。
    /// </summary>
    private static void AdjustCardSelection(CardDisplayInfo card, int delta)
    {
        if (delta <= 0)
        {
            return;
        }

        if (SelectedGrantCards.TryGetValue(card.IdText, out CardSelectionInfo? selection))
        {
            selection.Count += delta;
            return;
        }

        SelectedGrantCards[card.IdText] = new CardSelectionInfo
        {
            Info = card,
            Count = delta,
        };
    }

    /// <summary>
    /// 按当前卡片选择状态刷新按钮外框高亮。
    /// </summary>
    private static void RefreshCardButtonVisual(Button button, CardDisplayInfo card)
    {
        bool selected = SelectedGrantCards.TryGetValue(card.IdText, out CardSelectionInfo? selection) && selection.Count > 0;
        ApplySelectableItemCardStyle(button, selected, CardSelectedAccentColor, disabled: false);
    }

    /// <summary>
    /// 把当前卡片的已选数量同步到按钮右上角徽章。
    /// </summary>
    private static void SyncCardCountBadge(Button button, CardDisplayInfo card)
    {
        PanelContainer? badge = button.GetNodeOrNull<PanelContainer>("CardCountBadge");
        if (badge == null)
        {
            return;
        }
        Label? label = badge.GetNodeOrNull<Label>("CardCountLabel");

        if (SelectedGrantCards.TryGetValue(card.IdText, out CardSelectionInfo? selection) && selection.Count > 0)
        {
            if (label != null)
            {
                label.Text = $"×{selection.Count}";
            }
            badge.Visible = true;
        }
        else
        {
            if (label != null)
            {
                label.Text = string.Empty;
            }
            badge.Visible = false;
        }
    }

    /// <summary>
    /// 创建一个右上角「×N」徽章。
    /// </summary>
    private static PanelContainer CreateCardCountBadge()
    {
        PanelContainer badge = new()
        {
            Name = "CardCountBadge",
            CustomMinimumSize = new Vector2(CardCountBadgeSize, CardCountBadgeSize),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
            ZIndex = 10,
        };
        badge.AnchorLeft = 1f;
        badge.AnchorTop = 0f;
        badge.AnchorRight = 1f;
        badge.AnchorBottom = 0f;
        badge.OffsetLeft = -CardCountBadgeSize - CardCountBadgeMargin;
        badge.OffsetTop = CardCountBadgeMargin;
        badge.OffsetRight = -CardCountBadgeMargin;
        badge.OffsetBottom = CardCountBadgeSize + CardCountBadgeMargin;
        badge.AddThemeStyleboxOverride("panel",
            CreateRoundedStyleBox(new Color(0.05f, 0.07f, 0.10f, 0.92f),
                                  new Color(0.96f, 0.84f, 0.46f, 0.95f),
                                  6, 2, 0, 0));

        Label label = new()
        {
            Name = "CardCountLabel",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.55f, 1f));
        label.AddThemeFontSizeOverride("font_size", 22);
        badge.AddChild(label);

        return badge;
    }

    /// <summary>
    /// 卡片悬浮 tooltip 内容。
    /// </summary>
    private static string BuildCardTooltip(CardDisplayInfo card)
    {
        string costLabel = GmUiText.IsChinese ? "费用" : "Cost";
        return $"{card.Name}\n{card.Description}\n{card.Type}  •  {card.Rarity}  •  {costLabel} {card.CostText}\n{card.Character}\n{card.Source}";
    }


    // ============================================================
    // 虚拟化卡片自助页：页签切换与生命周期
    // ============================================================

    /// <summary>
    /// 进入卡片自助页时显示虚拟化容器，隐藏 GridContainer。
    /// </summary>
    private static void ShowCardVirtualContainer()
    {
        if (_cardVirtualSpacer != null)
        {
            _cardVirtualSpacer.Visible = true;
        }
        if (_grid != null)
        {
            _grid.Visible = false;
        }
    }

    /// <summary>
    /// 离开卡片自助页时隐藏虚拟化容器，恢复 GridContainer。
    /// </summary>
    private static void HideCardVirtualContainer()
    {
        if (_cardVirtualSpacer != null)
        {
            _cardVirtualSpacer.Visible = false;
        }
        if (_grid != null)
        {
            _grid.Visible = true;
        }

        // 隐藏所有 slot
        foreach (CardSlot slot in _cardSlots)
        {
            if (GodotObject.IsInstanceValid(slot.Button))
            {
                slot.Button.Visible = false;
            }
            slot.DataIndex = -1;
            slot.BoundCardId = null;
        }
    }

    /// <summary>
    /// 面板关闭时暂停虚拟化 slot 的 _Process，避免后台 NCard 持续吃帧。
    /// </summary>
    private static void SetCardSlotsProcessMode(bool enabled)
    {
        if (_cardVirtualContainer != null && GodotObject.IsInstanceValid(_cardVirtualContainer))
        {
            _cardVirtualContainer.ProcessMode = enabled ? Node.ProcessModeEnum.Inherit : Node.ProcessModeEnum.Disabled;
        }
    }

    /// <summary>
    /// 把 SelectedGrantCards 的最新状态同步到所有可见 slot 的徽章。
    /// 用于发卡完成 / 清空选择后刷新。
    /// </summary>
    private static void SyncAllBucketCardBadges()
    {
        foreach (CardSlot slot in _cardSlots)
        {
            if (slot.DataIndex < 0 || slot.DataIndex >= _virtualizedFilteredCards.Count)
            {
                continue;
            }
            if (!GodotObject.IsInstanceValid(slot.Button) || !slot.Button.Visible)
            {
                continue;
            }
            CardDisplayInfo card = _virtualizedFilteredCards[slot.DataIndex];
            SyncCardCountBadge(slot.Button, card);
            RefreshCardButtonVisual(slot.Button, card);
        }
    }

    /// <summary>
    /// 预热入口（虚拟化方案下为空操作，保留接口兼容）。
    /// </summary>
    public static void TriggerCardCachePreheat()
    {
        // 虚拟化方案不需要预热：首屏只创建 ~24 个 slot，耗时 < 200ms
    }

    // ============================================================
    // 卡片删除页（保持原有逻辑，数据量小不需要虚拟化）
    // ============================================================

    /// <summary>
    /// 刷新「卡片删除」页网格。
    /// </summary>
    private static void RefreshRemoveCardGrid()
    {
        if (_grid == null || _searchInput == null || _categoryFilter == null)
        {
            return;
        }

        UpdateGridColumns();

        IReadOnlyList<CardDisplayInfo> deckCards = CardService.GetDeckCardsSorted(
            _searchInput.Text,
            GetSelectedFilterKey(_categoryFilter));

        HashSet<CardModel> currentDeck = deckCards.Select(info => info.Card).ToHashSet();
        foreach (CardModel staleCard in SelectedRemoveCards.Keys.ToList())
        {
            if (!currentDeck.Contains(staleCard))
            {
                SelectedRemoveCards.Remove(staleCard);
            }
        }

        ClearGridChildren();

        _cardRenderDebugCounter = 0;
        foreach (CardDisplayInfo card in deckCards)
        {
            _grid.AddChild(CreateRemoveCardButton(card));
        }

        UpdateSelectionStatus(deckCards.Count);
        UpdateConfirmButtonAvailability();
    }

    /// <summary>
    /// 创建卡片删除页的单张卡按钮。
    /// </summary>
    private static Button CreateRemoveCardButton(CardDisplayInfo card)
    {
        int slotIndex = _cardRenderDebugCounter++;
        bool verbose = slotIndex < CardRenderDebugLimit;

        Button button = new()
        {
            CustomMinimumSize = new Vector2(CardButtonWidth, CardButtonHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            ClipContents = false,
        };

        Control? cardVisual = TryCreateCardVisual(card, showUpgraded: false, slotIndex, verbose);
        if (cardVisual != null)
        {
            button.AddChild(cardVisual);
        }
        else
        {
            CenterContainer fallbackSlot = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
            fallbackSlot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            TextureRect fallback = new()
            {
                Texture = card.Icon,
                CustomMinimumSize = new Vector2(ItemIconDisplaySize, ItemIconDisplaySize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            fallbackSlot.AddChild(fallback);
            button.AddChild(fallbackSlot);
        }

        RefreshRemoveCardButtonVisual(button, card);
        RegisterItemTooltip(button, BuildCardTooltip(card));
        RegisterDragSelectableButton(
            button,
            () => SelectedRemoveCards.ContainsKey(card.Card),
            () => SelectRemoveCardByDrag(card, button),
            () => UnselectRemoveCardByDrag(card, button),
            _ => ToggleRemoveCardSelection(card, button),
            () => RefreshRemoveCardButtonVisual(button, card));
        return button;
    }

    private static void ToggleRemoveCardSelection(CardDisplayInfo card, Button button)
    {
        if (SelectedRemoveCards.Remove(card.Card)) { }
        else { SelectedRemoveCards[card.Card] = card; }
        RefreshRemoveCardButtonVisual(button, card);
        UpdateSelectionStatus(_grid?.GetChildCount());
    }

    private static void SelectRemoveCardByDrag(CardDisplayInfo card, Button button)
    {
        SelectedRemoveCards[card.Card] = card;
        RefreshRemoveCardButtonVisual(button, card);
    }

    private static void UnselectRemoveCardByDrag(CardDisplayInfo card, Button button)
    {
        SelectedRemoveCards.Remove(card.Card);
        RefreshRemoveCardButtonVisual(button, card);
    }

    private static void RefreshRemoveCardButtonVisual(Button button, CardDisplayInfo card)
    {
        bool selected = SelectedRemoveCards.ContainsKey(card.Card);
        ApplySelectableItemCardStyle(button, selected, RemoveSelectedAccentColor, disabled: false);
    }


    // ============================================================
    // 卡片强化页（保持原有逻辑）
    // ============================================================

    /// <summary>
    /// 刷新「卡片强化」页网格。
    /// </summary>
    private static void RefreshUpgradeCardGrid()
    {
        if (_grid == null || _searchInput == null || _categoryFilter == null)
        {
            return;
        }

        UpdateGridColumns();

        IReadOnlyList<CardDisplayInfo> deckCards = CardService.GetUpgradableDeckCardsSorted(
            _searchInput.Text,
            GetSelectedFilterKey(_categoryFilter));

        HashSet<CardModel> currentDeck = deckCards.Select(info => info.Card).ToHashSet();
        foreach (CardModel staleCard in SelectedUpgradeCards.Keys.ToList())
        {
            if (!currentDeck.Contains(staleCard))
            {
                SelectedUpgradeCards.Remove(staleCard);
            }
        }

        ClearGridChildren();

        _cardRenderDebugCounter = 0;
        foreach (CardDisplayInfo card in deckCards)
        {
            _grid.AddChild(CreateUpgradeCardButton(card));
        }

        UpdateSelectionStatus(deckCards.Count);
        UpdateConfirmButtonAvailability();
    }

    /// <summary>
    /// 创建卡片强化页的单张卡按钮。
    /// </summary>
    private static Button CreateUpgradeCardButton(CardDisplayInfo card)
    {
        int slotIndex = _cardRenderDebugCounter++;
        bool verbose = slotIndex < CardRenderDebugLimit;

        Button button = new()
        {
            CustomMinimumSize = new Vector2(CardButtonWidth, CardButtonHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            ClipContents = false,
        };

        Control? cardVisual = TryCreateCardVisual(card, showUpgraded: false, slotIndex, verbose);
        if (cardVisual != null)
        {
            button.AddChild(cardVisual);
        }
        else
        {
            CenterContainer fallbackSlot = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
            fallbackSlot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            TextureRect fallback = new()
            {
                Texture = card.Icon,
                CustomMinimumSize = new Vector2(ItemIconDisplaySize, ItemIconDisplaySize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            fallbackSlot.AddChild(fallback);
            button.AddChild(fallbackSlot);
        }

        RefreshUpgradeCardButtonVisual(button, card);
        RegisterItemTooltip(button, BuildCardTooltip(card));
        RegisterDragSelectableButton(
            button,
            () => SelectedUpgradeCards.ContainsKey(card.Card),
            () => SelectUpgradeCardByDrag(card, button),
            () => UnselectUpgradeCardByDrag(card, button),
            _ => ToggleUpgradeCardSelection(card, button),
            () => RefreshUpgradeCardButtonVisual(button, card));
        button.GuiInput += inputEvent => OnUpgradeCardButtonGuiInput(inputEvent, card);
        return button;
    }

    private static void OnUpgradeCardButtonGuiInput(InputEvent inputEvent, CardDisplayInfo card)
    {
        if (inputEvent is not InputEventMouseButton mouseEvent || !mouseEvent.Pressed) return;
        if (mouseEvent.ButtonIndex != MouseButton.Right) return;
        ShowUpgradePreviewPopup(card);
    }

    private static void ToggleUpgradeCardSelection(CardDisplayInfo card, Button button)
    {
        if (SelectedUpgradeCards.Remove(card.Card)) { }
        else { SelectedUpgradeCards[card.Card] = card; }
        RefreshUpgradeCardButtonVisual(button, card);
        UpdateSelectionStatus(_grid?.GetChildCount());
    }

    private static void SelectUpgradeCardByDrag(CardDisplayInfo card, Button button)
    {
        SelectedUpgradeCards[card.Card] = card;
        RefreshUpgradeCardButtonVisual(button, card);
    }

    private static void UnselectUpgradeCardByDrag(CardDisplayInfo card, Button button)
    {
        SelectedUpgradeCards.Remove(card.Card);
        RefreshUpgradeCardButtonVisual(button, card);
    }

    private static void RefreshUpgradeCardButtonVisual(Button button, CardDisplayInfo card)
    {
        bool selected = SelectedUpgradeCards.ContainsKey(card.Card);
        ApplySelectableItemCardStyle(button, selected, RemoveSelectedAccentColor, disabled: false);
    }


    // ============================================================
    // 共用工具方法：TryCreateCardVisual / RefreshCardVisualOnReady / MakeSubtreeMouseTransparent
    // （删卡页和强化页仍需要这些方法创建一次性 NCard）
    // ============================================================

    /// <summary>
    /// 用游戏自带 NCard + NPreviewCardHolder 渲染卡片视觉（用于删卡页/强化页的一次性创建）。
    /// 失败时返回 null，外层会用 Portrait 立绘做兜底。
    /// </summary>
    private static Control? TryCreateCardVisual(CardDisplayInfo card, bool showUpgraded, int slotIndex, bool verbose)
    {
        try
        {
            CardModel modelToRender = card.Card;
            if (showUpgraded && card.Card.IsUpgradable && card.Card.CurrentUpgradeLevel < card.Card.MaxUpgradeLevel)
            {
                try
                {
                    CardModel upgradedClone = card.Card.ToMutable();
                    upgradedClone.UpgradeInternal();
                    modelToRender = upgradedClone;
                }
                catch (Exception upgradeEx)
                {
                    MainFile.Logger.Info($"[卡片自助] 升级版克隆失败：{card.IdText} / {upgradeEx.Message}");
                }
            }

            NCard? nCard = NCard.Create(modelToRender, ModelVisibility.Visible);
            if (nCard == null)
            {
                return null;
            }

            NPreviewCardHolder? holder = NPreviewCardHolder.Create(nCard, false, false);
            if (holder == null)
            {
                nCard.QueueFree();
                return null;
            }

            holder.SetCardScale(new Vector2(CardScale, CardScale));
            holder.MouseFilter = Control.MouseFilterEnum.Ignore;
            holder.PivotOffset = Vector2.Zero;
            holder.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            holder.Size = NCard.defaultSize;

            CardPreviewMode previewMode = showUpgraded ? CardPreviewMode.Upgrade : CardPreviewMode.Normal;
            holder.Ready += () => OnHolderReady(card, holder, nCard, previewMode, slotIndex, verbose);

            return holder;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"NCard 创建失败：{card.IdText} / {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// holder 进入场景树后统一处理（用于删卡页/强化页）。
    /// </summary>
    private static void OnHolderReady(CardDisplayInfo card, NPreviewCardHolder holder, NCard nCard, CardPreviewMode previewMode, int slotIndex, bool verbose)
    {
        RefreshCardVisualOnReady(card, nCard, previewMode);
        MakeSubtreeMouseTransparent(holder);

        if (holder.GetParent() is Control parent)
        {
            parent.Resized += () => CenterHolderInParentLegacy(parent, holder);
            CenterHolderInParentLegacy(parent, holder);
            Callable.From(() => CenterHolderInParentLegacy(parent, holder)).CallDeferred();
        }
    }

    /// <summary>
    /// 把 holder 在父 Control 内居中（删卡页/强化页用）。
    /// </summary>
    private static void CenterHolderInParentLegacy(Control parent, NPreviewCardHolder holder)
    {
        if (!GodotObject.IsInstanceValid(parent) || !GodotObject.IsInstanceValid(holder))
        {
            return;
        }

        Vector2 parentSize = parent.Size;
        if (parentSize.X <= 0f || parentSize.Y <= 0f)
        {
            return;
        }

        holder.Position = parentSize / 2f;
    }

    /// <summary>
    /// NCard 进入场景树后补刷视觉。
    /// </summary>
    private static void RefreshCardVisualOnReady(CardDisplayInfo card, NCard nCard, CardPreviewMode previewMode)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(nCard) || nCard.Model == null)
            {
                return;
            }

            if (!nCard.IsNodeReady())
            {
                return;
            }

            nCard.UpdateVisuals(PileType.None, previewMode);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"NCard 刷新视觉失败：{card.IdText} / {ex.Message}");
        }
    }

    /// <summary>
    /// 递归把 root 及其所有 Control 后代的 MouseFilter 设为 Ignore。
    /// </summary>
    private static void MakeSubtreeMouseTransparent(Node root)
    {
        if (!GodotObject.IsInstanceValid(root))
        {
            return;
        }

        if (root is Control control)
        {
            control.MouseFilter = Control.MouseFilterEnum.Ignore;
        }

        foreach (Node child in root.GetChildren())
        {
            MakeSubtreeMouseTransparent(child);
        }
    }

    /// <summary>
    /// 清掉 _grid 上的所有子节点（用于删卡页/强化页/其他页签刷新前）。
    /// 虚拟化方案下不再有 bucket 缓存按钮需要保护。
    /// </summary>
    private static void ClearGridChildren()
    {
        if (_grid == null)
        {
            return;
        }
        foreach (Node child in _grid.GetChildren())
        {
            child.QueueFree();
        }
    }
}
