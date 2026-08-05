using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.Services;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 页面确认/清空动作分文件：当前页签的批量执行入口、清空当前选择，
/// 以及它们依赖的"已选数量统计"和"能力图鉴目标解析"等辅助。
/// 内容来自 GmUiManager.cs 的确认动作段，行为与原实现一致，只做物理拆分。
/// 备注：GetSelectedPotionCopies/GetSelectedCardCopies 仅被 OnConfirmPressed 使用，
/// 暂随 Confirm 一起搬过来；后续如有更合适的归属再调整。
/// </summary>
public static partial class GmUiManager
{
    /// <summary>
    /// 确认按钮执行锁，避免用户双击导致同一批选择被重复发放、删除或召唤。
    /// </summary>
    private static bool _isConfirming;

    /// <summary>
    /// 点击确认后执行当前页签对应的批量动作。
    /// </summary>
    private static async void OnConfirmPressed()
    {
        if (_isConfirming)
        {
            ShowToast(GmUiText.Pick("操作正在执行，请稍候。", "Operation is running, please wait."));
            return;
        }

        _isConfirming = true;
        UpdateConfirmButtonAvailability();
        try
        {
            await OnConfirmPressedCore();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 确认动作异常：页签={_currentPage}，异常类型={ex.GetType().FullName}，异常={ex.Message}。");
            ShowToast(GmUiText.Pick("操作执行失败，请查看日志。", "Operation failed, please check logs."));
        }
        finally
        {
            _isConfirming = false;
            UpdateConfirmButtonAvailability();
        }
    }

    /// <summary>
    /// 执行确认按钮的实际业务逻辑；外层 OnConfirmPressed 负责防重入和异常兜底。
    /// </summary>
    private static async Task OnConfirmPressedCore()
    {
        UpdateConfirmButtonAvailability();

        if (TryBlockUnavailableFeatureForCurrentPage())
        {
            return;
        }

        // 能力图鉴的【清除能力】模式不需要选择能力，跳过空选校验，由 TryResolvePowerTargets 校验目标。
        bool skipEmptySelectionCheck = _currentPage == GmPageMode.PowerCatalog
            && _powerOperateMode == PowerOperateMode.Wipe;
        if (!skipEmptySelectionCheck && GetCurrentSelectedCount() == 0)
        {
            ShowToast(_currentPage switch
            {
                GmPageMode.GrantPotions => GmUiText.Pick("请先选择至少一个药水。", "Select at least one potion first."),
                GmPageMode.OrbBox => GmUiText.OrbSelectFirst,
                GmPageMode.GrantCards => GmUiText.CardSelectFirstHint,
                GmPageMode.RemoveCards => GmUiText.CardRemoveSelectFirst,
                GmPageMode.UpgradeCards => GmUiText.CardUpgradeSelectFirst,
                GmPageMode.EnchantCards => GmUiText.CardEnchantSelectCardFirst,
                GmPageMode.PowerCatalog => GmUiText.PowerSelectFirst,
                GmPageMode.MonsterCage => GmUiText.MonsterSelectFirst,
                _ => GmUiText.Pick("请先选择至少一个遗物。", "Select at least one relic first."),
            });
            return;
        }

        if (_currentPage == GmPageMode.GrantRelics)
        {
            RelicGrantResult result = await RelicService.GrantRelicsAsync(CurrentSelectedRelics.Values.ToList());
            CurrentSelectedRelics.Clear();
            string relicGrantSummary = GmRunContextService.IsMultiplayer()
                ? GmUiText.BuildGrantSubmitSummary(result.SuccessCount, result.SkippedCount, result.FailedCount)
                : result.BuildSummary();
            ShowToast(relicGrantSummary);
            RefreshCurrentPage();
            ScheduleMultiplayerRefreshIfSubmitted(GmPageMode.GrantRelics, result.SuccessCount);
            return;
        }

        if (_currentPage == GmPageMode.GrantPotions)
        {
            PotionGrantResult potionResult = await PotionService.GrantPotionsAsync(GetSelectedPotionCopies());
            SelectedGrantPotions.Clear();
            string potionSummary = GmRunContextService.IsMultiplayer()
                ? GmUiText.BuildPotionGrantSubmitSummary(potionResult.SuccessCount, potionResult.SkippedCount, potionResult.FailedCount)
                : potionResult.BuildSummary();
            ShowToast(potionSummary);
            RefreshCurrentPage();
            RefreshPotionSlotControl();
            ScheduleMultiplayerPotionRefreshIfSubmitted(potionResult.SuccessCount);
            return;
        }

        if (_currentPage == GmPageMode.OrbBox)
        {
            OrbGrantResult orbResult = await OrbService.GrantOrbsAsync(GetSelectedOrbCopies());
            SelectedGrantOrbs.Clear();
            string orbSummary = GmRunContextService.IsMultiplayer()
                ? GmUiText.BuildOrbGrantSubmitSummary(orbResult.SuccessCount, orbResult.SkippedCount, orbResult.FailedCount)
                : orbResult.BuildSummary();
            ShowToast(orbSummary);
            RefreshCurrentPage();
            return;
        }

        if (_currentPage == GmPageMode.GrantCards)
        {
            // 「查看升级」勾选时，发卡也按升级版走；勾选状态以点确认那一刻为准（WYSIWYG）。
            bool grantUpgraded = _cardShowUpgradeCheckbox?.ButtonPressed ?? false;
            CardGrantResult cardResult = await CardService.GrantCardsAsync(GetSelectedCardCopies(), grantUpgraded);
            SelectedGrantCards.Clear();
            // 把所有 bucket（包括非 active）的徽章状态拉成最新；否则切到其他 bucket 仍能看到旧的 ×N 数字
            SyncAllBucketCardBadges();
            string cardGrantSummary = GmRunContextService.IsMultiplayer()
                ? GmUiText.BuildCardGrantSubmitSummary(cardResult.SuccessCount, cardResult.SkippedCount, cardResult.FailedCount)
                : cardResult.BuildSummary();
            ShowToast(cardGrantSummary);
            RefreshCurrentPage();
            ScheduleMultiplayerRefreshIfSubmitted(GmPageMode.GrantCards, cardResult.SuccessCount);
            return;
        }

        if (_currentPage == GmPageMode.RemoveCards)
        {
            // 拷贝列表再清空选中：避免 RemoveDeckCardsAsync 中迭代时被外部清掉
            List<CardDisplayInfo> toRemove = SelectedRemoveCards.Values.ToList();
            CardGrantResult cardRemoveResult = await CardService.RemoveDeckCardsAsync(toRemove);
            SelectedRemoveCards.Clear();
            string cardRemoveSummary = GmRunContextService.IsMultiplayer()
                ? GmUiText.BuildCardRemoveSubmitSummary(cardRemoveResult.SuccessCount, cardRemoveResult.SkippedCount, cardRemoveResult.FailedCount)
                : GmUiText.BuildCardRemoveSummary(cardRemoveResult.SuccessCount, cardRemoveResult.FailedCount);
            ShowToast(cardRemoveSummary);
            RefreshCurrentPage();
            ScheduleMultiplayerRefreshIfSubmitted(GmPageMode.RemoveCards, cardRemoveResult.SuccessCount);
            return;
        }

        if (_currentPage == GmPageMode.UpgradeCards)
        {
            // 同删卡：先拷贝再清空，避免迭代过程中外部清空选中表
            List<CardDisplayInfo> toUpgrade = SelectedUpgradeCards.Values.ToList();
            CardGrantResult cardUpgradeResult = await CardService.UpgradeDeckCardsAsync(toUpgrade);
            SelectedUpgradeCards.Clear();
            string summary = GmRunContextService.IsMultiplayer()
                ? GmUiText.BuildCardUpgradeSubmitSummary(cardUpgradeResult.SuccessCount, cardUpgradeResult.SkippedCount, cardUpgradeResult.FailedCount)
                : GmUiText.BuildCardUpgradeSummary(cardUpgradeResult.SuccessCount, cardUpgradeResult.SkippedCount, cardUpgradeResult.FailedCount);
            ShowToast(summary);
            RefreshCurrentPage();
            ScheduleMultiplayerRefreshIfSubmitted(GmPageMode.UpgradeCards, cardUpgradeResult.SuccessCount);
            return;
        }

        if (_currentPage == GmPageMode.EnchantCards)
        {
            if (_selectedCardEnchantment == null)
            {
                ShowToast(GmUiText.CardEnchantSelectFirst);
                return;
            }

            if (!IsCurrentEnchantmentAmountValid())
            {
                ShowToast(GmUiText.CardEnchantAmountInvalid);
                return;
            }

            List<CardDisplayInfo> toEnchant = SelectedEnchantCards.Values.ToList();
            CardEnchantResult cardEnchantResult = await EnchantmentService.EnchantDeckCardsAsync(_selectedCardEnchantment, toEnchant, GetCurrentEnchantmentAmountOrDefault());
            SelectedEnchantCards.Clear();
            string cardEnchantSummary = GmRunContextService.IsMultiplayer()
                ? GmUiText.BuildCardEnchantSubmitSummary(cardEnchantResult.SuccessCount, cardEnchantResult.SkippedCount, cardEnchantResult.FailedCount)
                : cardEnchantResult.BuildSummary();
            ShowToast(cardEnchantSummary);
            RefreshCurrentPage();
            ScheduleMultiplayerRefreshIfSubmitted(GmPageMode.EnchantCards, cardEnchantResult.SuccessCount);
            return;
        }

        if (_currentPage == GmPageMode.PowerCatalog)
        {
            if (!TryResolvePowerTargets(out IReadOnlyList<CreatureTargetInfo> targets, out string targetError))
            {
                ShowToast(targetError);
                return;
            }

            if (_powerOperateMode == PowerOperateMode.Grant)
            {
                PowerApplyResult powerGrantResult = await PowerService.ApplyPowersAsync(SelectedPowers.Values.ToList(), targets);
                string powerGrantSummary = GmRunContextService.IsMultiplayer()
                    ? GmUiText.BuildPowerGrantSubmitSummary(powerGrantResult.SuccessCount, powerGrantResult.SkippedCount, powerGrantResult.FailedCount)
                    : powerGrantResult.BuildGrantSummary();
                ShowToast(powerGrantSummary);
            }
            else if (_powerOperateMode == PowerOperateMode.Clear)
            {
                PowerApplyResult powerClearResult = await PowerService.ClearPowersAsync(SelectedPowers.Values.Select(selection => selection.Info).ToList(), targets);
                string powerClearSummary = GmRunContextService.IsMultiplayer()
                    ? GmUiText.BuildPowerClearSubmitSummary(powerClearResult.SuccessCount, powerClearResult.SkippedCount, powerClearResult.FailedCount)
                    : powerClearResult.BuildClearSummary();
                ShowToast(powerClearSummary);
            }
            else
            {
                // Wipe：一键清空所选目标身上的全部能力。
                PowerApplyResult powerWipeResult = await PowerService.WipeAllPowersAsync(targets);
                string powerWipeSummary = GmRunContextService.IsMultiplayer()
                    ? GmUiText.BuildPowerWipeSubmitSummary(powerWipeResult.SuccessCount, powerWipeResult.SkippedCount, powerWipeResult.FailedCount)
                    : powerWipeResult.BuildWipeSummary();
                ShowToast(powerWipeSummary);
            }

            SelectedPowers.Clear();
            RefreshCurrentPage();
            return;
        }

        if (_currentPage == GmPageMode.MonsterCage)
        {
            bool allowRiskyCrossRoomSummon = _monsterRiskySummonCheckbox?.ButtonPressed ?? false;
            MonsterSpawnResult monsterResult = await MonsterService.SpawnMonstersAsync(SelectedSpawnMonsters.Values.ToList(), allowRiskyCrossRoomSummon);
            SelectedSpawnMonsters.Clear();
            string monsterSummary = GmRunContextService.IsMultiplayer()
                ? GmUiText.BuildMonsterSpawnSubmitSummary(monsterResult.SuccessCount, monsterResult.FailedCount)
                : monsterResult.BuildSummary();
            ShowToast(monsterSummary);
            RefreshCurrentPage();
            return;
        }

        RelicRemoveResult removeResult = await RelicService.RemoveRelicsAsync(CurrentSelectedRelics.Values.ToList());
        CurrentSelectedRelics.Clear();
        string relicRemoveSummary = GmRunContextService.IsMultiplayer()
            ? GmUiText.BuildRemoveSubmitSummary(removeResult.SuccessCount, removeResult.SkippedCount, removeResult.FailedCount)
            : removeResult.BuildSummary();
        ShowToast(relicRemoveSummary);
        RefreshCurrentPage();
    }

    /// <summary>
    /// 多人模式下兜底阻止当前页签的危险确认动作。
    /// </summary>
    private static bool TryBlockUnavailableFeatureForCurrentPage()
    {
        if (!TryGetCurrentPageFeature(out GmFeature feature))
        {
            return false;
        }

        GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(feature);
        if (availability.IsAllowed)
        {
            return false;
        }

        ShowToast(availability.Reason);
        SetStatusText(availability.Reason);
        MainFile.Logger.Info($"GM 多人功能已阻止：页签={_currentPage}，功能={feature}，执行方式={availability.ExecutionMode}，原因={availability.Reason}。");
        return true;
    }

    /// <summary>
    /// 多人模式下网络动作只是先入队，稍后再补一次刷新，避免玩家看到旧牌组误判为未生效。
    /// </summary>
    /// <param name="page">提交动作时所在页签。</param>
    /// <param name="submittedCount">成功提交到网络队列的数量。</param>
    private static void ScheduleMultiplayerRefreshIfSubmitted(GmPageMode page, int submittedCount)
    {
        if (!GmRunContextService.IsMultiplayer() || submittedCount <= 0)
        {
            return;
        }

        _ = RefreshAfterNetworkSubmitAsync(page);
    }

    /// <summary>
    /// 多人模式下药水动作入队后延迟刷新药水页和栏位数量。
    /// </summary>
    /// <param name="submittedCount">成功提交到网络队列的数量。</param>
    private static void ScheduleMultiplayerPotionRefreshIfSubmitted(int submittedCount)
    {
        if (!GmRunContextService.IsMultiplayer() || submittedCount <= 0)
        {
            return;
        }

        _ = RefreshPotionAfterNetworkSubmitAsync();
    }

    /// <summary>
    /// 延迟刷新药水页；如果玩家已经切页，就不打扰当前页面。
    /// </summary>
    private static async Task RefreshPotionAfterNetworkSubmitAsync()
    {
        try
        {
            await Task.Delay(900);
            if (_currentPage != GmPageMode.GrantPotions)
            {
                return;
            }

            RefreshCurrentPage();
            RefreshPotionSlotControl();
            MainFile.Logger.Info("GM 多人药水提交后延迟刷新完成。");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 多人药水提交后延迟刷新异常：异常={ex.Message}。");
        }
    }

    /// <summary>
    /// 延迟刷新当前页签；如果玩家已经切页，就不打扰当前页面。
    /// </summary>
    /// <param name="page">提交动作时所在页签。</param>
    private static async Task RefreshAfterNetworkSubmitAsync(GmPageMode page)
    {
        try
        {
            await Task.Delay(900);
            if (_currentPage != page)
            {
                return;
            }

            RefreshCurrentPage();
            MainFile.Logger.Info($"GM 多人提交后延迟刷新完成：页签={page}。");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 多人提交后延迟刷新异常：页签={page}，异常={ex.Message}。");
        }
    }

    /// <summary>
    /// 把当前 GM 页签映射成多人模式安全闸门使用的功能枚举。
    /// </summary>
    private static bool TryGetCurrentPageFeature(out GmFeature feature)
    {
        if (_currentPage == GmPageMode.Chronicle)
        {
            feature = GmFeature.Chronicle;
            return false;
        }

        feature = _currentPage switch
        {
            GmPageMode.GrantRelics => GmFeature.GrantRelic,
            GmPageMode.RemoveRelics => GmFeature.RemoveRelic,
            GmPageMode.GrantPotions => GmFeature.GrantPotion,
            GmPageMode.OrbBox => GmFeature.GrantOrb,
            GmPageMode.BlightCatalog => GmFeature.ToggleBlight,
            GmPageMode.GrantCards => GmFeature.GrantCard,
            GmPageMode.RemoveCards => GmFeature.RemoveCard,
            GmPageMode.UpgradeCards => GmFeature.UpgradeCard,
            GmPageMode.EnchantCards => GmFeature.EnchantCard,
            GmPageMode.PowerCatalog => GmFeature.ApplyPower,
            GmPageMode.MonsterCage => GmFeature.SpawnMonster,
            GmPageMode.EventRegistry => GmFeature.TriggerEvent,
            _ => GmFeature.Chronicle,
        };

        return true;
    }

    /// <summary>
    /// 清空当前选择。
    /// </summary>
    private static void ClearSelection()
    {
        if (_currentPage == GmPageMode.GrantPotions)
        {
            SelectedGrantPotions.Clear();
        }
        else if (_currentPage == GmPageMode.OrbBox)
        {
            SelectedGrantOrbs.Clear();
        }
        else if (_currentPage == GmPageMode.GrantCards)
        {
            SelectedGrantCards.Clear();
            SyncAllBucketCardBadges();
        }
        else if (_currentPage == GmPageMode.RemoveCards)
        {
            SelectedRemoveCards.Clear();
        }
        else if (_currentPage == GmPageMode.UpgradeCards)
        {
            SelectedUpgradeCards.Clear();
        }
        else if (_currentPage == GmPageMode.EnchantCards)
        {
            if (_enchantmentPageStep == EnchantmentPageStep.SelectCards)
            {
                SelectedEnchantCards.Clear();
            }
            else
            {
                ResetEnchantmentPageState();
            }
        }
        else if (_currentPage == GmPageMode.PowerCatalog)
        {
            SelectedPowers.Clear();
            SelectedPowerMonsterTargets.Clear();
            _powerApplyAllMonsters = false;
            _powerAllMonstersToggle?.SetPressedNoSignal(false);
        }
        else if (_currentPage == GmPageMode.MonsterCage)
        {
            SelectedSpawnMonsters.Clear();
        }
        else
        {
            CurrentSelectedRelics.Clear();
        }

        RefreshCurrentPage();
    }

    /// <summary>
    /// 获取当前页签已选择数量，药水页按重复选择数量累加。
    /// </summary>
    private static int GetCurrentSelectedCount()
    {
        if (_currentPage == GmPageMode.Chronicle || _currentPage == GmPageMode.EventRegistry)
        {
            return 0;
        }

        if (_currentPage == GmPageMode.PowerCatalog)
        {
            // 【清除能力】模式无须选择能力，给确认按钮一个非零计数避免被置灰；
            // 真正的目标校验由 TryResolvePowerTargets 在确认时完成。
            if (_powerOperateMode == PowerOperateMode.Wipe)
            {
                return 1;
            }

            return SelectedPowers.Count;
        }

        if (_currentPage == GmPageMode.MonsterCage)
        {
            return SelectedSpawnMonsters.Count;
        }

        if (_currentPage == GmPageMode.GrantCards)
        {
            return SelectedGrantCards.Values.Sum(selection => selection.Count);
        }

        if (_currentPage == GmPageMode.OrbBox)
        {
            return SelectedGrantOrbs.Values.Sum(selection => selection.Count);
        }

        if (_currentPage == GmPageMode.BlightCatalog)
        {
            return 0;
        }

        if (_currentPage == GmPageMode.RemoveCards)
        {
            return SelectedRemoveCards.Count;
        }

        if (_currentPage == GmPageMode.UpgradeCards)
        {
            return SelectedUpgradeCards.Count;
        }

        if (_currentPage == GmPageMode.EnchantCards)
        {
            return _enchantmentPageStep == EnchantmentPageStep.SelectCards
                ? SelectedEnchantCards.Count
                : 0;
        }

        return _currentPage == GmPageMode.GrantPotions
            ? SelectedGrantPotions.Values.Sum(selection => selection.Count)
            : CurrentSelectedRelics.Count;
    }

    /// <summary>
    /// 按选择数量展开药水列表，供批量发放逻辑逐个处理。
    /// </summary>
    private static IReadOnlyList<PotionDisplayInfo> GetSelectedPotionCopies()
    {
        return SelectedGrantPotions.Values
            .SelectMany(selection => Enumerable.Repeat(selection.Info, selection.Count))
            .ToList();
    }

    /// <summary>
    /// 按选择数量展开卡片列表，支持同一张卡发放多张。
    /// </summary>
    private static IReadOnlyList<CardDisplayInfo> GetSelectedCardCopies()
    {
        return SelectedGrantCards.Values
            .SelectMany(selection => Enumerable.Repeat(selection.Info, selection.Count))
            .ToList();
    }

    /// <summary>
    /// 按选择数量展开充能球列表，供批量发放逻辑逐个处理。
    /// </summary>
    private static IReadOnlyList<OrbDisplayInfo> GetSelectedOrbCopies()
    {
        return SelectedGrantOrbs.Values
            .SelectMany(selection => Enumerable.Repeat(selection.Info, selection.Count))
            .ToList();
    }

    /// <summary>
    /// 解析能力图鉴本次操作目标（玩家或怪物），并在不满足条件时返回可直接展示给用户的提示文案。
    /// </summary>
    private static bool TryResolvePowerTargets(out IReadOnlyList<CreatureTargetInfo> targets, out string errorMessage)
    {
        targets = [];
        errorMessage = "";

        if (_powerTargetMode == PowerTargetMode.Player)
        {
            if (!PowerService.TryGetPlayerTarget(out CreatureTargetInfo? playerTarget) || playerTarget == null)
            {
                errorMessage = GmUiText.PowerNotInRun;
                return false;
            }

            targets = [playerTarget];
            return true;
        }

        IReadOnlyList<CreatureTargetInfo> allMonsterTargets = PowerService.GetMonsterTargetsInCombat();
        if (allMonsterTargets.Count == 0)
        {
            errorMessage = GmUiText.PowerNotInCombat;
            return false;
        }

        if (_powerApplyAllMonsters)
        {
            targets = allMonsterTargets;
            return true;
        }

        List<CreatureTargetInfo> selectedTargets = allMonsterTargets
            .Where(target => SelectedPowerMonsterTargets.ContainsKey(target.Id))
            .ToList();
        if (selectedTargets.Count == 0)
        {
            errorMessage = GmUiText.PowerMonsterTargetSelectFirst;
            return false;
        }

        targets = selectedTargets;
        return true;
    }
}
