using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.Config;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.Services;
using System.Reflection;
using System.Text;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM UI 管理器，只使用 Godot 内置控件，避免自定义 Godot 节点触发桥接层异常。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 订阅运行时配置变更，用于让卡片预热配置在游戏内切换后能及时生效。
    /// </summary>
    static GmUiManager()
    {
        GmModConfig.RuntimeConfigChanged += OnRuntimeConfigChanged;
    }

    /// <summary>
    /// GM 页面模式，用来区分当前展示的功能页签。
    /// </summary>
    private enum GmPageMode
    {
        GrantRelics,
        RemoveRelics,
        GrantPotions,
        OrbBox,
        BlightCatalog,
        GrantCards,
        RemoveCards,
        UpgradeCards,
        EnchantCards,
        PowerCatalog,
        MonsterCage,
        EventRegistry,
        Chronicle,
    }

    /// <summary>
    /// 能力图鉴操作模式：
    /// - Grant：赋予能力（按选中能力增减层数）
    /// - Clear：删除能力（按选中能力名从目标身上删除，对应 UI 文案【删除能力】）
    /// - Wipe：清除能力（不需要选能力，一键清空目标身上的全部能力，对应 UI 文案【清除能力】）
    /// </summary>
    private enum PowerOperateMode
    {
        Grant,
        Clear,
        Wipe,
    }

    /// <summary>
    /// 能力图鉴目标模式：玩家或怪物。
    /// </summary>
    private enum PowerTargetMode
    {
        Player,
        Monsters,
    }

    /// <summary>
    /// 卡片附魔页内部步骤：先选附魔，再选牌组卡片。
    /// </summary>
    private enum EnchantmentPageStep
    {
        SelectEnchantment,
        SelectCards,
    }

    /// <summary>
    /// 卡牌自助排序字段。
    /// </summary>
    private enum CardSortMode
    {
        Default,
        Cost,
        Damage,
        Name,
    }

    /// <summary>
    /// 卡牌自助排序方向。
    /// </summary>
    private enum CardSortDirection
    {
        Asc,
        Desc,
    }

    private const string GmButtonName = "the_book_of_ages_top_bar_button";
    private const float PanelHorizontalMargin = 0f;
    private const float PanelVerticalMargin = 0f;
    private const float RelicButtonWidth = 178f;
    private const float RelicButtonGap = 12f;
    /// <summary>
    /// 卡片格子单独配置：NCard 默认尺寸 300×422，缩放 0.65 后 ≈ 195×274，
    /// 给上下边框各留约 20px 余量后取 230×320，避免底部描述被裁。
    /// </summary>
    private const float CardButtonWidth = 230f;
    private const float CardButtonHeight = 320f;
    private const float CardScale = 0.65f;
    /// <summary>
    /// 卡面右上角「×N」徽章的尺寸与边距。
    /// </summary>
    private const float CardCountBadgeSize = 36f;
    private const float CardCountBadgeMargin = 6f;
    private const float ChronicleContentWidth = 1180f;
    /// <summary>
    /// GM 页面字号，Godot 默认字号通常是 16，这里放大 25% 到 20。
    /// </summary>
    private const int GmUiFontSize = 20;
    private const string GmButtonIconFolderName = "图片素材";
    private const string GmButtonIconFileName = "gm按钮.png";
    private const string GmUiIconResourceFolder = "res://TheBookOfAges/ui/gm_icons";
    private const float GmUiNavIconScale = 0.70f;
    private const float GmTopBarButtonSize = 74f;
    private const int MinGridColumns = 3;
    private const int MaxGridColumns = 9;
    /// <summary>
    /// 物品图标在按钮中的固定显示尺寸，防止不同贴图分辨率导致大小不一致。
    /// </summary>
    private const float ItemIconDisplaySize = 64f;
    private const int ItemTooltipFontSize = 20;
    private const float ItemTooltipMinWidth = 300f;
    private const float ItemTooltipMaxWidth = 860f;
    private const float ItemTooltipPadding = 14f;
    private const float ItemTooltipCursorOffset = 18f;
    private const int ItemTooltipInlineImageSize = 24;
    private const float MonsterTooltipPreviewWidth = 420f;
    private const float MonsterTooltipPreviewHeight = 320f;
    private const float MonsterTooltipPreviewMaxWidth = 760f;
    private const float MonsterTooltipPreviewMaxHeight = 560f;
    private const float MonsterTooltipPreviewWidthRatio = 0.42f;
    private const float MonsterTooltipPreviewHeightRatio = 0.42f;
    private const float MonsterTooltipContentGap = 10f;
    private const float MonsterPreviewContentPadding = 12f;
    private const int EnchantmentCustomAmountMin = 1;
    private const int EnchantmentCustomAmountMax = 99;
    private static readonly string[] MonsterPreviewAnimationCandidates = ["idle", "Idle", "idle_loop", "IdleLoop", "default", "Default"];
    private static readonly float[] MonsterPreviewRefitDelaySeconds = [0.04f, 0.16f, 0.36f];
    private static readonly Color PanelBgColor = new(0.03f, 0.05f, 0.08f, 0.90f);
    private static readonly Color PanelBorderColor = new(0.34f, 0.43f, 0.51f, 0.92f);
    private static readonly Color NavTextNormalColor = new(0.86f, 0.89f, 0.91f, 1f);
    private static readonly Color NavTextHoverColor = new(0.95f, 0.97f, 0.98f, 1f);
    private static readonly Color NavTextActiveColor = new(0.98f, 0.87f, 0.52f, 1f);
    private static readonly Color SectionTitleColor = new(0.96f, 0.84f, 0.46f, 1f);
    private static readonly Color FieldLabelColor = new(0.70f, 0.91f, 1.00f, 1f);
    private static readonly Color FieldValueColor = new(0.98f, 0.79f, 0.48f, 1f);
    private static readonly Color AuxiliaryTextColor = new(0.74f, 0.79f, 0.83f, 1f);
    private static readonly Color EventNameColor = new(1.00f, 0.84f, 0.45f, 1f);
    private static readonly Color EventCategoryColor = new(0.58f, 0.98f, 0.68f, 1f);
    private static readonly Color EventSourceColor = new(0.70f, 0.78f, 0.84f, 1f);
    private static readonly Color PrimaryActionTextColor = new(0.60f, 1.00f, 0.72f, 1f);
    private static readonly Color SecondaryActionTextColor = new(0.90f, 0.94f, 0.97f, 1f);
    private static readonly Color DangerActionTextColor = new(1.00f, 0.70f, 0.62f, 1f);
    private static readonly Color ItemCardNormalBgColor = new(0.06f, 0.08f, 0.11f, 0.86f);
    private static readonly Color ItemCardHoverBgColor = new(0.10f, 0.14f, 0.19f, 0.92f);
    private static readonly Color ItemCardPressedBgColor = new(0.13f, 0.18f, 0.25f, 0.94f);
    private static readonly Color ItemCardNormalBorderColor = new(0.23f, 0.28f, 0.35f, 0.92f);
    private static readonly Color ItemCardHoverBorderColor = new(0.42f, 0.56f, 0.68f, 0.95f);
    private static readonly Color ItemCardOwnedBgColor = new(0.13f, 0.13f, 0.13f, 0.90f);
    private static readonly Color ItemCardOwnedBorderColor = new(0.40f, 0.40f, 0.40f, 0.92f);
    private static readonly Color GrantSelectedAccentColor = new(0.48f, 1.00f, 0.60f, 1.00f);
    private static readonly Color RemoveSelectedAccentColor = new(1.00f, 0.55f, 0.50f, 1.00f);
    private static readonly Color PowerSelectedAccentColor = new(0.62f, 0.84f, 1.00f, 1.00f);
    private static readonly Color MonsterSelectedAccentColor = new(1.00f, 0.84f, 0.48f, 1.00f);
    private static readonly Color CardSelectedAccentColor = new(0.96f, 0.84f, 0.46f, 1.00f);
    private static readonly Color OrbSelectedAccentColor = new(0.38f, 0.92f, 1.00f, 1.00f);

    private static readonly RelicService RelicService = new();
    private static readonly PotionService PotionService = new();
    private static readonly OrbService OrbService = new();
    private static readonly BlightService BlightService = new();
    private static readonly PowerService PowerService = new();
    private static readonly MonsterService MonsterService = new();
    private static readonly EventService EventService = new();
    // 走单例：ChronicleService 已私有化构造，全局只有一个实例，UI / MainFile / Patch 共用同一份状态。
    private static readonly ChronicleService ChronicleService = TheBookOfAges.TheBookOfAgesCode.Services.ChronicleService.Instance;
    private static readonly CardService CardService = new();
    private static readonly EnchantmentService EnchantmentService = new();
    private static readonly FieldInfo? HoverTipTitleField = FindHoverTipBackingField("<Title>k__BackingField");
    private static readonly FieldInfo? HoverTipDescriptionField = FindHoverTipBackingField("<Description>k__BackingField");
    private static readonly FieldInfo? HoverTipIdField = FindHoverTipBackingField("<Id>k__BackingField");
    private static readonly Dictionary<string, Color> ItemTooltipTagColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gold"] = new Color(1.00f, 0.82f, 0.25f),
        ["blue"] = new Color(0.45f, 0.72f, 1.00f),
        ["red"] = new Color(1.00f, 0.38f, 0.34f),
        ["green"] = new Color(0.46f, 0.88f, 0.50f),
        ["purple"] = new Color(0.78f, 0.55f, 1.00f),
        ["pink"] = new Color(1.00f, 0.56f, 0.84f),
        ["orange"] = new Color(1.00f, 0.62f, 0.26f),
        ["lime"] = new Color(0.78f, 1.00f, 0.36f),
        ["cyan"] = new Color(0.42f, 0.96f, 1.00f),
        ["gray"] = new Color(0.72f, 0.76f, 0.78f),
        ["grey"] = new Color(0.72f, 0.76f, 0.78f),
        ["white"] = new Color(0.95f, 0.95f, 0.92f),
    };
    private static readonly Dictionary<string, Texture2D> ItemTooltipImageCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Texture2D?> GmUiTextureCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> MonsterPreviewDisabledIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, RelicDisplayInfo> SelectedGrantRelics = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, RelicDisplayInfo> SelectedRemoveRelics = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, PotionSelectionInfo> SelectedGrantPotions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, OrbSelectionInfo> SelectedGrantOrbs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CardSelectionInfo> SelectedGrantCards = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// 卡片删除页选择集：用 CardModel 实例做 key 区分牌组里同名重复卡（5 张打击各占一格）。
    /// 切换页签或离开 GM 面板时不主动清，进入删卡页 ResetToolbar 时再清。
    /// </summary>
    private static readonly Dictionary<CardModel, CardDisplayInfo> SelectedRemoveCards = new();
    /// <summary>
    /// 卡片强化页选择集：用 CardModel 实例做 key 区分牌组里同名重复卡。
    /// 进入强化页时由 ResetToolbar 清空。
    /// </summary>
    private static readonly Dictionary<CardModel, CardDisplayInfo> SelectedUpgradeCards = new();
    /// <summary>
    /// 卡片附魔页选择集：用 CardModel 实例做 key 区分牌组里同名重复卡。
    /// </summary>
    private static readonly Dictionary<CardModel, CardDisplayInfo> SelectedEnchantCards = new();
    private static readonly Dictionary<string, PowerSelectionInfo> SelectedPowers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CreatureTargetInfo> SelectedPowerMonsterTargets = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, MonsterDisplayInfo> SelectedSpawnMonsters = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<ChronicleValueKey, LineEdit> ChronicleInputs = new();
    private static readonly Dictionary<ChronicleValueKey, CheckBox> ChronicleLocks = new();
    private static readonly Dictionary<ChronicleValueKey, Button> ChronicleApplyButtons = new();
    private static readonly Dictionary<OptionButton, List<string>> FilterOptionKeys = new();
    private static readonly List<Action> LocalizedUiRefreshers = [];

    private static GmPageMode _currentPage = GmPageMode.GrantRelics;
    private static GmPageMode _rememberedPage = GmPageMode.GrantRelics;
    private static CanvasLayer? _layer;
    private static Control? _root;
    private static PanelContainer? _panel;
    private static PanelContainer? _itemTooltip;
    private static Control? _itemTooltipOwner;
    private static ScrollContainer? _gridScroll;
    private static Button? _grantTabButton;
    private static Button? _removeTabButton;
    private static Button? _potionGrantTabButton;
    private static Button? _orbBoxTabButton;
    private static Button? _blightTabButton;
    private static Button? _grantCardsTabButton;
    private static Button? _removeCardsTabButton;
    private static Button? _upgradeCardsTabButton;
    private static Button? _enchantCardsTabButton;
    private static Button? _powerCatalogTabButton;
    private static Button? _monsterCageTabButton;
    private static Button? _eventRegistryTabButton;
    private static Button? _chronicleTabButton;
    private static VBoxContainer? _toolbar;
    private static HBoxContainer? _filtersContainer;
    private static LineEdit? _searchInput;
    private static OptionButton? _categoryFilter;
    private static OptionButton? _rarityFilter;
    private static OptionButton? _sourceFilter;
    private static GridContainer? _grid;
    private static RichTextLabel? _statusLabel;
    private static Button? _selectAllButton;
    private static Button? _clearButton;
    private static Button? _confirmButton;
    private static HBoxContainer? _footerContainer;
    private static Label? _toastLabel;
    private static Godot.Timer? _toastTimer;
    private static HBoxContainer? _potionSlotControl;
    private static LineEdit? _potionSlotInput;
    private static Label? _potionSlotFilledLabel;
    private static int _potionSlotPendingValue;
    private static VBoxContainer? _eventQuickActionsControl;
    private static OptionButton? _eventActSelect;
    private static OptionButton? _eventTreasureSelect;
    private static VBoxContainer? _powerControl;
    private static OptionButton? _powerModeSelect;
    private static OptionButton? _powerTargetSelect;
    private static CheckBox? _powerAllMonstersToggle;
    private static HBoxContainer? _powerMonsterTargetsRow;
    private static HBoxContainer? _monsterSafetyControl;
    private static CheckBox? _monsterRiskySummonCheckbox;
    private static HBoxContainer? _blightControl;
    private static HBoxContainer? _cardExtraFilters;
    private static OptionButton? _cardTypeFilter;
    private static OptionButton? _cardCostFilter;
    private static OptionButton? _cardSortModeFilter;
    private static OptionButton? _cardSortDirectionFilter;
    private static CheckBox? _cardMultiplayerCheckbox;
    private static CheckBox? _cardShowUpgradeCheckbox;
    private static VBoxContainer? _enchantmentControl;
    private static HBoxContainer? _enchantmentFooterControl;
    private static Label? _enchantmentSelectedLabel;
    private static Label? _enchantmentAmountLabel;
    private static LineEdit? _enchantmentAmountInput;
    private static CheckBox? _enchantmentSafetyModeCheckbox;
    private static Label? _enchantmentHintLabel;
    private static Button? _enchantmentBackButton;
    private static EnchantmentDisplayInfo? _selectedCardEnchantment;
    private static EnchantmentPageStep _enchantmentPageStep = EnchantmentPageStep.SelectEnchantment;
    // 卡片自助相关字段已迁移到 GmUiManager.Cards.cs（虚拟化方案）
    private static ScrollContainer? _chronicleScroll;
    private static VBoxContainer? _chronicleRows;
    private static Godot.Timer? _chronicleLockTimer;
    /// <summary>
    /// 手牌哨兵定时器：高频（100ms）观察 PlayerCombatState.Hand.Cards.Count 是否减少，
    /// 仅在"始终抽到上限"开关启用且手牌真正减少时触发补牌；不参与主定时器的回填工作。
    /// 这样玩家不打牌时几乎零开销，避免主定时器无脑补牌带来的抽牌动画连环触发。
    /// </summary>
    private static Godot.Timer? _handWatcherTimer;
    /// <summary>岁月史书主回填定时器执行锁：上一轮还没结束时，本轮跳过重活，避免异步重入叠加卡顿。</summary>
    private static bool _isApplyingChronicleTick;
    /// <summary>岁月史书手牌哨兵执行锁：上一轮手牌检查还没结束时，本轮直接跳过。</summary>
    private static bool _isHandWatcherTickRunning;
    private static CheckBox? _alwaysKillAllEnemiesToggle;
    private static CheckBox? _godModeToggle;
    private static CheckBox? _infiniteEnergyToggle;
    private static CheckBox? _alwaysPlayerTurnToggle;
    private static CheckBox? _alwaysDrawToHandLimitToggle;
    private static CheckBox? _negativePowerImmuneToggle;
    private static OptionButton? _enemyIntentTargetSelect;
    private static CheckBox? _enemyIntentAllToggle;
    private static OptionButton? _enemyIntentSelect;
    private static Button? _enemyIntentApplyButton;
    private static bool _enemyIntentSelectInitialized;
    /// <summary>
    /// 上一次刷新敌人下拉用的"敌人列表签名"。差量更新依据：签名相同就跳过 Clear+AddItem，
    /// 避免定时器每 1.5 秒就把下拉重建一次造成 UI 闪烁与无意义分配。
    /// </summary>
    private static string? _lastEnemyIntentTargetSignature;
    private static OptionButton? _chronicleActSelect;
    private static CheckBox? _anyMapNodeClickableToggle;
    private static PowerOperateMode _powerOperateMode = PowerOperateMode.Grant;
    private static PowerTargetMode _powerTargetMode = PowerTargetMode.Player;
    private static bool _powerApplyAllMonsters;

    /// <summary>
    /// 获取当前页签对应的选择集合，避免发放和删除互相污染。
    /// </summary>
    private static Dictionary<string, RelicDisplayInfo> CurrentSelectedRelics => _currentPage == GmPageMode.GrantRelics ? SelectedGrantRelics : SelectedRemoveRelics;

    /// <summary>
    /// 处理游戏内配置页的运行时变更。
    /// 虚拟化方案下不再需要预热，此回调保留为扩展点。
    /// </summary>
    private static void OnRuntimeConfigChanged()
    {
        if (_layer?.Visible == true && _currentPage == GmPageMode.GrantRelics)
        {
            RefreshCurrentPage();
        }
    }

    /// <summary>
    /// 把 GM 入口挂到游戏顶栏，并准备 GM 页面。
    /// </summary>
    public static void AttachToTopBar(NTopBar topBar)
    {
        if (FindNodeByName(topBar, GmButtonName) == null)
        {
            BaseButton gmButton = CreateTopBarButton();
            gmButton.Pressed += TogglePanel;
            AddTopBarButton(topBar, gmButton);
        }

        EnsurePanel(topBar.GetTree());
    }

    /// <summary>
    /// 处理 H 快捷键，打开或关闭 GM 页面。
    /// </summary>
    public static void HandleHotkey(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey keyEvent)
        {
            return;
        }

        if (keyEvent.Pressed && !keyEvent.Echo && keyEvent.Keycode == Key.H)
        {
            if (_layer?.Visible == true && IsAnyGmTextInputFocused())
            {
                return;
            }

            TogglePanel();
        }
    }

    /// <summary>
    /// 判断 GM 面板内是否有文本输入框正在编辑；有焦点时不响应 H 热键，避免英文输入 h/H 时误关面板。
    /// </summary>
    private static bool IsAnyGmTextInputFocused()
    {
        if (_searchInput?.HasFocus() == true ||
            _potionSlotInput?.HasFocus() == true ||
            _enchantmentAmountInput?.HasFocus() == true)
        {
            return true;
        }

        return ChronicleInputs.Values.Any(input => input.HasFocus());
    }

    /// <summary>
    /// 统一处理 GM 面板相关输入：保留 H 热键开关，并在面板可见时拦截 Esc 只关闭面板。
    /// </summary>
    public static bool HandleInput(InputEvent inputEvent)
    {
        HandleHotkey(inputEvent);
        if (inputEvent is not InputEventKey keyEvent)
        {
            return false;
        }

        if (!keyEvent.Pressed || keyEvent.Echo || keyEvent.Keycode != Key.Escape)
        {
            return false;
        }

        if (_layer == null || !_layer.Visible)
        {
            return false;
        }

        // 优先关掉升级预览弹窗：玩家右键开了大图后按 Esc 时只关弹窗，不要把整个 GM 面板也关掉。
        if (_upgradePreviewLayer != null && GodotObject.IsInstanceValid(_upgradePreviewLayer))
        {
            CloseUpgradePreviewPopup();
            return true;
        }

        SetPanelVisible(false);
        return true;
    }

    /// <summary>
    /// 打开或关闭 GM 页面。
    /// </summary>
    public static void TogglePanel()
    {
        if (_layer == null)
        {
            MainFile.Logger.Info("GM 页面尚未创建。");
            return;
        }

        bool visible = !_layer.Visible;
        SetPanelVisible(visible);
        if (visible)
        {
            RestoreRememberedPage();
            RefreshCurrentPage();
            if (_currentPage == GmPageMode.GrantPotions)
            {
                RefreshPotionSlotControl();
            }
        }
    }

    /// <summary>
    /// 确保 GM 页面只创建一次。
    /// </summary>
    private static void EnsurePanel(SceneTree tree)
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer))
        {
            return;
        }

        _layer = new CanvasLayer
        {
            Layer = 120,
            Visible = false,
        };
        tree.Root.AddChild(_layer);
        BuildPanel(_layer);
    }

    /// <summary>
    /// 创建 GM 页面整体结构。
    /// </summary>
    private static void BuildPanel(CanvasLayer layer)
    {
        LocalizedUiRefreshers.Clear();
        _root = new Control
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Theme = CreateGmTheme(),
        };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_root);

        ColorRect dim = new()
        {
            Color = new Color(0f, 0f, 0f, 0.58f),
        };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);

        _panel = new PanelContainer();
        ApplyPanelVisualStyle(_panel);
        _panel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(_panel);

        VBoxContainer outer = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _panel.AddChild(outer);
        outer.AddChild(BuildBody());
        _root.AddChild(BuildFloatingCloseButton());
        ApplyPanelLayout();
        _root.Resized += ApplyPanelLayout;

        // 面板骨架搭完后按配置尝试启动卡片自助柔和预热；默认关闭，避免过早缓存不完整卡库。
        TriggerCardCachePreheat();
    }

    /// <summary>
    /// 刷新当前页签的网格内容。
    /// </summary>
    private static void RefreshCurrentPage()
    {
        if (_grid == null || _searchInput == null || _categoryFilter == null || _rarityFilter == null || _sourceFilter == null)
        {
            return;
        }

        // 离开卡片页：隐藏虚拟化容器，恢复 GridContainer 给其他页签使用。
        if (_currentPage != GmPageMode.GrantCards)
        {
            HideCardVirtualContainer();
        }

        if (_currentPage == GmPageMode.GrantPotions)
        {
            RefreshPotionGrid();
            return;
        }

        if (_currentPage == GmPageMode.OrbBox)
        {
            RefreshOrbGrid();
            return;
        }

        if (_currentPage == GmPageMode.BlightCatalog)
        {
            RefreshBlightGrid();
            return;
        }

        if (_currentPage == GmPageMode.GrantCards)
        {
            ShowCardVirtualContainer();
            RefreshCardGrid();
            return;
        }

        if (_currentPage == GmPageMode.RemoveCards)
        {
            RefreshRemoveCardGrid();
            return;
        }

        if (_currentPage == GmPageMode.UpgradeCards)
        {
            RefreshUpgradeCardGrid();
            return;
        }

        if (_currentPage == GmPageMode.EnchantCards)
        {
            RefreshEnchantmentPage();
            return;
        }

        if (_currentPage == GmPageMode.PowerCatalog)
        {
            RefreshPowerGrid();
            return;
        }

        if (_currentPage == GmPageMode.MonsterCage)
        {
            RefreshMonsterGrid();
            return;
        }

        if (_currentPage == GmPageMode.Chronicle)
        {
            RefreshChroniclePage();
            return;
        }

        // 事件名册页签走独立的刷新逻辑
        if (_currentPage == GmPageMode.EventRegistry)
        {
            RefreshEventGrid();
            return;
        }

        IReadOnlyList<RelicDisplayInfo> relics = GetCurrentPageRelics();
        UpdateGridColumns();

        ClearGridChildren();

        foreach (RelicDisplayInfo relic in relics)
        {
            _grid.AddChild(CreateRelicButton(relic));
        }

        UpdateSelectionStatus(relics.Count);
    }

    /// <summary>
    /// 统一控制 GM 页面的显示状态，关闭时让全屏根节点忽略鼠标，避免挡住游戏事件选项。
    /// 关闭面板时还会把 _grid 子树（含 ~180 个常驻 NCard 缓存按钮）的 ProcessMode 设成 Disabled，
    /// 让 NCard / Tween 等节点停止 _Process，避免后台持续吃帧导致游戏卡顿；打开时恢复 Inherit。
    /// 注意：仅切 _grid 一棵子树，岁月史书定时器在 _chronicleScroll 下，不受影响，关面板后持续开关仍生效。
    /// </summary>
    private static void SetPanelVisible(bool visible)
    {
        if (!visible)
        {
            SaveCurrentFilterState();
            _rememberedPage = _currentPage;
            HideItemTooltip(null);
            CloseUpgradePreviewPopup();
        }

        if (_layer != null)
        {
            _layer.Visible = visible;
        }

        if (_root != null)
        {
            _root.Visible = visible;
            _root.MouseFilter = visible ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        }

        // 关键性能优化：面板关闭时把卡片网格整棵子树暂停处理，让常驻的 NCard 节点不再每帧 _Process。
        // 不动 _layer 整体，是为了让岁月史书定时器（挂在 _chronicleScroll 下）在面板关闭后也能继续生效。
        if (_grid != null && GodotObject.IsInstanceValid(_grid))
        {
            _grid.ProcessMode = visible ? Node.ProcessModeEnum.Inherit : Node.ProcessModeEnum.Disabled;
        }

        // 虚拟化卡片 slot 也需要暂停/恢复
        SetCardSlotsProcessMode(visible);
    }

    /// <summary>
    /// 切换 GM 页签，并重置搜索筛选后刷新列表。
    /// </summary>
    private static void SwitchPage(GmPageMode mode)
    {
        if (_currentPage == mode)
        {
            return;
        }

        SaveCurrentFilterState();
        CloseUpgradePreviewPopup();
        _currentPage = mode;
        _rememberedPage = mode;
        UpdateFilterOptions();
        ApplyCurrentFilterState();
        ApplyCurrentPageState();
        RefreshCurrentPage();
    }

    /// <summary>
    /// 恢复最近停留的页签，让关闭后再次打开时仍回到上次页签。
    /// </summary>
    private static void RestoreRememberedPage()
    {
        if (_currentPage != _rememberedPage)
        {
            _currentPage = _rememberedPage;
        }

        // 不管页签是否切换，每次面板打开都强制刷一次筛选下拉的选项文本：
        // 否则用户中途切了游戏语言后再开 mod，停留在记忆页签时，
        // _currentPage == _rememberedPage 会跳过这步，下拉里的「全部类型 / 全部费用 / 全部稀有度」等选项
        // 仍是上次面板打开时构建的旧语言版本。
        // 注意：SetFilterItems 内部会自动恢复之前选中的 key，不会丢失筛选状态。
        UpdateFilterOptions();
        ApplyCurrentFilterState();
        ApplyCurrentPageState();
    }

    /// <summary>
    /// 按当前页签同步按钮禁用、专属控件显隐和页面刷新状态。
    /// </summary>
    private static void ApplyCurrentPageState()
    {
        RefreshLocalizedUi();
        UpdatePageTexts();
        UpdatePotionSlotControlVisibility();
        UpdatePowerControlVisibility();
        UpdateBlightControlVisibility();
        UpdateEventPageVisibility();
        UpdateChroniclePageVisibility();
        UpdateResetFiltersButtonVisibility();
        UpdateConfirmButtonAvailability();
    }

    /// <summary>
    /// 根据当前页签刷新按钮、搜索提示和确认按钮文本。
    /// </summary>
    private static void UpdatePageTexts()
    {
        if (_grantTabButton != null)
        {
            _grantTabButton.Disabled = _currentPage == GmPageMode.GrantRelics;
        }

        if (_removeTabButton != null)
        {
            _removeTabButton.Disabled = _currentPage == GmPageMode.RemoveRelics;
        }

        if (_potionGrantTabButton != null)
        {
            _potionGrantTabButton.Disabled = _currentPage == GmPageMode.GrantPotions;
        }

        if (_orbBoxTabButton != null)
        {
            _orbBoxTabButton.Disabled = _currentPage == GmPageMode.OrbBox;
        }

        if (_blightTabButton != null)
        {
            _blightTabButton.Disabled = _currentPage == GmPageMode.BlightCatalog;
        }

        if (_grantCardsTabButton != null)
        {
            _grantCardsTabButton.Disabled = _currentPage == GmPageMode.GrantCards;
        }

        if (_removeCardsTabButton != null)
        {
            _removeCardsTabButton.Disabled = _currentPage == GmPageMode.RemoveCards;
        }

        if (_upgradeCardsTabButton != null)
        {
            _upgradeCardsTabButton.Disabled = _currentPage == GmPageMode.UpgradeCards;
        }

        if (_enchantCardsTabButton != null)
        {
            _enchantCardsTabButton.Disabled = _currentPage == GmPageMode.EnchantCards;
        }

        if (_powerCatalogTabButton != null)
        {
            _powerCatalogTabButton.Disabled = _currentPage == GmPageMode.PowerCatalog;
        }

        if (_monsterCageTabButton != null)
        {
            _monsterCageTabButton.Disabled = _currentPage == GmPageMode.MonsterCage;
        }

        if (_eventRegistryTabButton != null)
        {
            _eventRegistryTabButton.Disabled = _currentPage == GmPageMode.EventRegistry;
        }

        if (_chronicleTabButton != null)
        {
            _chronicleTabButton.Disabled = _currentPage == GmPageMode.Chronicle;
        }

        if (_searchInput != null)
        {
            _searchInput.PlaceholderText = _currentPage switch
            {
                GmPageMode.GrantRelics => GmUiText.Pick("输入遗物名称或描述", "Search relic name or description"),
                GmPageMode.RemoveRelics => GmUiText.Pick("输入已持有遗物名称或描述", "Search owned relic name or description"),
                GmPageMode.GrantPotions => GmUiText.Pick("输入药水名称或描述", "Search potion name or description"),
                GmPageMode.OrbBox => GmUiText.OrbSearchPlaceholder,
                GmPageMode.BlightCatalog => GmUiText.BlightSearchPlaceholder,
                GmPageMode.GrantCards => GmUiText.CardSearchPlaceholder,
                GmPageMode.RemoveCards => GmUiText.CardRemoveSearchPlaceholder,
                GmPageMode.UpgradeCards => GmUiText.CardUpgradeSearchPlaceholder,
                GmPageMode.EnchantCards => _enchantmentPageStep == EnchantmentPageStep.SelectEnchantment
                    ? GmUiText.CardEnchantSearchPlaceholder
                    : GmUiText.CardEnchantCardSearchPlaceholder,
                GmPageMode.PowerCatalog => GmUiText.PowerSearchPlaceholder,
                GmPageMode.MonsterCage => GmUiText.MonsterSearchPlaceholder,
                GmPageMode.EventRegistry => GmUiText.EventSearchPlaceholder,
                GmPageMode.Chronicle => GmUiText.ChronicleTab,
                _ => GmUiText.Pick("输入名称或描述", "Search name or description"),
            };
        }

        if (_confirmButton != null)
        {
            _confirmButton.Text = _currentPage switch
            {
                GmPageMode.GrantRelics => GmUiText.Pick("确认发放", "Grant"),
                GmPageMode.RemoveRelics => GmUiText.Pick("确认删除", "Remove"),
                GmPageMode.GrantPotions => GmUiText.Pick("确认发放药水", "Grant Potions"),
                GmPageMode.OrbBox => GmUiText.OrbGrantConfirmButton,
                GmPageMode.GrantCards => GmUiText.CardConfirmButton,
                GmPageMode.RemoveCards => GmUiText.CardRemoveConfirmButton,
                GmPageMode.UpgradeCards => GmUiText.CardUpgradeConfirmButton,
                GmPageMode.EnchantCards => GmUiText.CardEnchantConfirmButton,
                GmPageMode.PowerCatalog => _powerOperateMode switch
                {
                    PowerOperateMode.Grant => GmUiText.PowerGrantConfirmButton,
                    PowerOperateMode.Clear => GmUiText.PowerClearConfirmButton,
                    PowerOperateMode.Wipe => GmUiText.PowerWipeConfirmButton,
                    _ => GmUiText.PowerGrantConfirmButton,
                },
                GmPageMode.MonsterCage => GmUiText.MonsterConfirmButton,
                GmPageMode.Chronicle => GmUiText.ChronicleApplyButton,
                _ => GmUiText.Pick("确认", "Confirm"),
            };
        }

        UpdateSelectionStatus();
    }

    /// <summary>
    /// 根据当前页签的格子尺寸动态计算列数，尽量不出现横向滚动条。
    /// 卡片自助页用更大的格子（230 宽），其他页签维持 178 宽的遗物按钮。
    /// </summary>
    private static void UpdateGridColumns()
    {
        if (_grid == null)
        {
            return;
        }

        // 卡片相关页（自助、删除、强化）需要更大的格子；其他页（遗物/药水/能力/事件/怪物）继续用 178
        bool isCardSizedPage = _currentPage == GmPageMode.GrantCards || _currentPage == GmPageMode.RemoveCards || _currentPage == GmPageMode.UpgradeCards || (_currentPage == GmPageMode.EnchantCards && _enchantmentPageStep == EnchantmentPageStep.SelectCards);
        float cellWidth = isCardSizedPage ? CardButtonWidth : RelicButtonWidth;

        float width = _gridScroll?.Size.X ?? 0f;
        if (width <= 0f)
        {
            _grid.Columns = isCardSizedPage ? 5 : 6;
            return;
        }

        int columns = Math.Clamp((int)Math.Floor(width / (cellWidth + RelicButtonGap)), MinGridColumns, MaxGridColumns);
        _grid.Columns = columns;
    }
}
