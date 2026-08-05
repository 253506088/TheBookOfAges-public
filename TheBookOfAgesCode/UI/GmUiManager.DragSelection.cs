using Godot;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 页面拖拽批量选择模块：统一处理遗物、药水、卡片按钮的左键按住拖动选中与取消选择。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 拖拽选择的单个按钮绑定信息，封装不同页签的选中、取消和刷新逻辑。
    /// </summary>
    private sealed class DragSelectableBinding
    {
        /// <summary>当前绑定的按钮节点。</summary>
        public required Button Button { get; init; }

        /// <summary>判断当前对象是否已经被选中。</summary>
        public required Func<bool> IsSelected { get; init; }

        /// <summary>把当前对象设置为选中。</summary>
        public required Action Select { get; init; }

        /// <summary>把当前对象取消选中。</summary>
        public required Action Unselect { get; init; }

        /// <summary>普通单击时执行的原有逻辑，参数依次表示 Ctrl 和 Shift 是否按住。</summary>
        public required Action<bool, bool> SingleClick { get; init; }

        /// <summary>选择状态变化后刷新按钮外观。</summary>
        public required Action Refresh { get; init; }

        /// <summary>判断当前对象是否允许参与拖拽批量选择。</summary>
        public required Func<bool> CanDrag { get; init; }
    }

    /// <summary>按钮到拖拽绑定信息的映射，用于按当前网格顺序计算拖拽范围。</summary>
    private static readonly Dictionary<Button, DragSelectableBinding> DragSelectableBindings = new();

    /// <summary>本次左键按下时的起点绑定；为空表示当前没有待处理拖拽。</summary>
    private static DragSelectableBinding? _dragStartBinding;

    /// <summary>本次拖拽是否已经进入批量模式；未进入时左键释放仍走普通单击。</summary>
    private static bool _isDragSelectionActive;

    /// <summary>本次拖拽是否为批量选中；false 表示批量取消。</summary>
    private static bool _dragShouldSelect;

    /// <summary>本次拖拽起点按下时 Ctrl 是否按住，供卡片自助普通单击保留 Ctrl+左键 +5。</summary>
    private static bool _dragStartCtrlPressed;

    /// <summary>本次拖拽起点按下时 Shift 是否按住，供需要大步进的页签使用。</summary>
    private static bool _dragStartShiftPressed;

    /// <summary>本次拖拽已经处理过的按钮，避免药水和卡片数量被反复累加。</summary>
    private static readonly HashSet<Button> DragProcessedButtons = [];

    /// <summary>
    /// 注册一个支持左键拖拽批量选择的按钮。
    /// </summary>
    private static void RegisterDragSelectableButton(
        Button button,
        Func<bool> isSelected,
        Action select,
        Action unselect,
        Action<bool> singleClick,
        Action refresh,
        Func<bool>? canDrag = null,
        bool keepBindingOnTreeExit = false)
    {
        RegisterDragSelectableButton(
            button,
            isSelected,
            select,
            unselect,
            (ctrlPressed, _) => singleClick(ctrlPressed),
            refresh,
            canDrag,
            keepBindingOnTreeExit);
    }

    /// <summary>
    /// 注册一个支持左键拖拽批量选择的按钮，并允许普通单击读取 Ctrl/Shift 修饰键。
    /// </summary>
    private static void RegisterDragSelectableButton(
        Button button,
        Func<bool> isSelected,
        Action select,
        Action unselect,
        Action<bool, bool> singleClick,
        Action refresh,
        Func<bool>? canDrag = null,
        bool keepBindingOnTreeExit = false)
    {
        DragSelectableBinding binding = new()
        {
            Button = button,
            IsSelected = isSelected,
            Select = select,
            Unselect = unselect,
            SingleClick = singleClick,
            Refresh = refresh,
            CanDrag = canDrag ?? (() => true),
        };

        DragSelectableBindings[button] = binding;
        button.GuiInput += inputEvent => OnDragSelectableButtonGuiInput(inputEvent, binding);
        button.MouseEntered += () => OnDragSelectableButtonMouseEntered(binding);
        if (!keepBindingOnTreeExit)
        {
            button.TreeExiting += () => UnregisterDragSelectableButton(button);
        }
    }

    /// <summary>
    /// 注销已经离开场景树且不会复用的拖拽按钮，避免旧按钮残留在映射里。
    /// </summary>
    private static void UnregisterDragSelectableButton(Button button)
    {
        DragSelectableBindings.Remove(button);
        DragProcessedButtons.Remove(button);
        if (_dragStartBinding?.Button == button)
        {
            ResetDragSelectionState();
        }
    }

    /// <summary>
    /// 处理支持拖拽按钮的鼠标输入：左键按下记录起点，左键释放决定单击或结束拖拽。
    /// </summary>
    private static void OnDragSelectableButtonGuiInput(InputEvent inputEvent, DragSelectableBinding binding)
    {
        if (inputEvent is not InputEventMouseButton mouseEvent || mouseEvent.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (mouseEvent.Pressed)
        {
            BeginDragSelection(binding, mouseEvent.CtrlPressed, mouseEvent.ShiftPressed);
            binding.Button.AcceptEvent();
            return;
        }

        FinishDragSelection(binding);
        binding.Button.AcceptEvent();
    }

    /// <summary>
    /// 鼠标按住左键进入另一个按钮时，启动或推进批量拖拽选择。
    /// </summary>
    private static void OnDragSelectableButtonMouseEntered(DragSelectableBinding binding)
    {
        if (_dragStartBinding == null || !Input.IsMouseButtonPressed(MouseButton.Left))
        {
            ResetDragSelectionState();
            return;
        }

        if (_dragStartBinding == binding || !_dragStartBinding.CanDrag())
        {
            return;
        }

        if (!_isDragSelectionActive)
        {
            _isDragSelectionActive = true;
            _dragShouldSelect = !_dragStartBinding.IsSelected();
        }

        ApplyDragRange(binding);
    }

    /// <summary>
    /// 开始一次可能的拖拽；如果后续没有进入其他格子，则释放时会退化为普通单击。
    /// </summary>
    private static void BeginDragSelection(DragSelectableBinding binding, bool ctrlPressed, bool shiftPressed)
    {
        ResetDragSelectionState();
        _dragStartBinding = binding;
        _dragStartCtrlPressed = ctrlPressed;
        _dragStartShiftPressed = shiftPressed;
    }

    /// <summary>
    /// 结束当前拖拽：没有真正拖动时执行普通单击，已经拖动时只清理状态。
    /// </summary>
    private static void FinishDragSelection(DragSelectableBinding releaseBinding)
    {
        if (_dragStartBinding == null)
        {
            ResetDragSelectionState();
            return;
        }

        if (!_isDragSelectionActive)
        {
            _dragStartBinding.SingleClick(_dragStartCtrlPressed, _dragStartShiftPressed);
            ResetDragSelectionState();
            return;
        }

        ApplyDragRange(releaseBinding);
        ResetDragSelectionState();
        UpdateSelectionStatus();
    }

    /// <summary>
    /// 按当前网格显示顺序，把起点到当前按钮之间的闭区间应用本轮拖拽模式。
    /// </summary>
    private static void ApplyDragRange(DragSelectableBinding currentBinding)
    {
        if (_grid == null || _dragStartBinding == null)
        {
            return;
        }

        List<DragSelectableBinding> orderedBindings = GetCurrentVisibleDragBindings();
        int startIndex = orderedBindings.IndexOf(_dragStartBinding);
        int currentIndex = orderedBindings.IndexOf(currentBinding);
        if (startIndex < 0 || currentIndex < 0)
        {
            return;
        }

        int from = Math.Min(startIndex, currentIndex);
        int to = Math.Max(startIndex, currentIndex);
        for (int i = from; i <= to; i++)
        {
            ApplyDragSelectionToBinding(orderedBindings[i]);
        }

        UpdateSelectionStatus();
    }

    /// <summary>
    /// 获取当前网格中可见且支持拖拽的按钮绑定，顺序与页面显示顺序一致。
    /// </summary>
    private static List<DragSelectableBinding> GetCurrentVisibleDragBindings()
    {
        if (_currentPage == GmPageMode.GrantCards)
        {
            return GetCurrentVisibleCardSlotDragBindings();
        }

        if (_grid == null)
        {
            return [];
        }

        List<DragSelectableBinding> bindings = [];
        foreach (Node child in _grid.GetChildren())
        {
            if (child is Button button && button.Visible && DragSelectableBindings.TryGetValue(button, out DragSelectableBinding? binding))
            {
                bindings.Add(binding);
            }
        }

        return bindings;
    }

    /// <summary>
    /// 获取卡片自助页当前可见的虚拟化卡片按钮绑定，顺序按真实卡片数据索引排列。
    /// </summary>
    private static List<DragSelectableBinding> GetCurrentVisibleCardSlotDragBindings()
    {
        List<DragSelectableBinding> bindings = [];
        List<CardSlot> visibleSlots = [];
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
            if (!DragSelectableBindings.ContainsKey(slot.Button))
            {
                continue;
            }
            visibleSlots.Add(slot);
        }

        visibleSlots.Sort((left, right) => left.DataIndex.CompareTo(right.DataIndex));
        foreach (CardSlot slot in visibleSlots)
        {
            bindings.Add(DragSelectableBindings[slot.Button]);
        }

        return bindings;
    }

    /// <summary>
    /// 对单个按钮应用本轮拖拽模式，并刷新按钮视觉。
    /// </summary>
    private static void ApplyDragSelectionToBinding(DragSelectableBinding binding)
    {
        if (DragProcessedButtons.Contains(binding.Button) || !binding.CanDrag())
        {
            return;
        }

        if (_dragShouldSelect)
        {
            binding.Select();
        }
        else
        {
            binding.Unselect();
        }

        binding.Refresh();
        DragProcessedButtons.Add(binding.Button);
    }

    /// <summary>
    /// 清理当前拖拽状态，为下一次左键操作做准备。
    /// </summary>
    private static void ResetDragSelectionState()
    {
        _dragStartBinding = null;
        _isDragSelectionActive = false;
        _dragShouldSelect = false;
        _dragStartCtrlPressed = false;
        _dragStartShiftPressed = false;
        DragProcessedButtons.Clear();
    }
}
