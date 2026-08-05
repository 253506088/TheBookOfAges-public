using TheBookOfAges.TheBookOfAgesCode.Services;

namespace TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>
/// GM 页面文本工具，按照游戏当前语言只显示中文或英文。
/// </summary>
public static class GmUiText
{
    public static bool IsChinese => GmLocalizationService.IsChinese;

    /// <summary>
    /// 多人模式下无法定位本机玩家时的提示。
    /// </summary>
    public static string MultiplayerLocalPlayerMissing => IsChinese
        ? "多人模式下没有找到当前玩家，已阻止 GM 操作，避免误改其他玩家。单人模式下可以正常使用。"
        : "Could not find the local player in multiplayer, so the GM action was blocked to avoid changing another player. This works normally in singleplayer.";

    /// <summary>
    /// 多人模式网络加载中禁止操作的提示。
    /// </summary>
    public static string MultiplayerNetworkLoadingDisabled => IsChinese
        ? "多人模式正在同步或加载，暂时不能执行该 GM 功能，避免联机状态不同步导致卡死或崩溃。单人模式下可以正常使用。"
        : "Multiplayer is syncing or loading, so this GM action is temporarily blocked to avoid desyncs, hangs, or crashes. This works normally in singleplayer.";

    /// <summary>
    /// 多人模式官方动作队列忙碌时的禁用提示。
    /// </summary>
    public static string MultiplayerActionQueueBusyDisabled => IsChinese
        ? "多人模式当前还有同步动作未执行完，请稍等队列空闲后再执行 GM 操作。"
        : "A multiplayer sync action is still pending. Wait for the queue to become idle before running another GM action.";

    /// <summary>
    /// 多人模式危险功能的通用禁用提示。
    /// </summary>
    public static string MultiplayerDangerousFeatureDisabled => IsChinese
        ? "多人模式下暂不开放该功能：它会修改战斗、地图或全局状态，可能导致联机状态不同步、卡死或崩溃。单人模式下可以正常使用。"
        : "This feature is not available in multiplayer yet because it changes combat, map, or global state and may cause desyncs, hangs, or crashes. It works normally in singleplayer.";

    /// <summary>
    /// 多人模式不稳定房间状态下禁止事件任意门的提示。
    /// </summary>
    public static string MultiplayerEventDisabledInUnstableRoom => IsChinese
        ? "多人模式下暂不开放事件任意门：它会跳转房间和改写地图流程，可能导致联机状态不同步、卡死或崩溃。单人模式下可以正常使用。"
        : "Event travel is not available in multiplayer yet because it changes room and map flow and may cause desyncs, hangs, or crashes. It works normally in singleplayer.";

    /// <summary>
    /// 多人模式不稳定房间状态下禁止房间跳转的提示。
    /// </summary>
    public static string MultiplayerRoomTravelDisabledInUnstableRoom => IsChinese
        ? "多人模式下暂不开放阶段或房间跳转：它会改写地图与当前房间流程，可能导致联机状态不同步。单人模式下可以正常使用。"
        : "Act or room travel is not available in multiplayer yet because it changes map and current-room flow and may cause desyncs. It works normally in singleplayer.";

    /// <summary>
    /// 多人模式药水栏位功能禁用提示。
    /// </summary>
    public static string MultiplayerPotionSlotsDisabled => IsChinese
        ? "多人模式下暂不开放药水栏位修改：它会直接改当前玩家药水槽集合，网络同步不明确。单人模式下可以正常使用。"
        : "Potion slot editing is not available in multiplayer yet because it directly changes the local potion slot list and may not sync safely. It works normally in singleplayer.";

    /// <summary>
    /// 多人模式充能球功能禁用提示。
    /// </summary>
    public static string MultiplayerOrbDisabled => IsChinese
        ? "多人模式下暂不开放球盒：它会修改战斗中的充能球队列，可能导致战斗状态不同步。单人模式下可以正常使用。"
        : "Orb granting is not available in multiplayer yet because it changes the combat orb queue and may desync combat state. It works normally in singleplayer.";

    /// <summary>
    /// 多人模式荒疫功能禁用提示。
    /// </summary>
    public static string MultiplayerBlightDisabled => IsChinese
        ? "多人模式下暂不开放荒疫开关：它会修改整局的 Modifier 和 Hook 状态，可能导致各端状态不同。单人模式下可以正常使用。"
        : "Blight toggles are not available in multiplayer yet because they change run modifiers and hooks, which may desync clients. They work normally in singleplayer.";

    /// <summary>
    /// 多人模式卡片附魔功能禁用提示。
    /// </summary>
    public static string MultiplayerEnchantDisabled => IsChinese
        ? "多人模式下暂不开放卡片附魔：它会直接改牌组里的具体卡片实例，网络同步不明确。单人模式下可以正常使用。"
        : "Card enchantment is not available in multiplayer yet because it directly changes a specific deck card instance and may not sync safely. It works normally in singleplayer.";

    /// <summary>
    /// 多人模式卡片强化功能禁用提示。
    /// </summary>
    public static string MultiplayerCardUpgradeDisabled => IsChinese
        ? "多人模式卡片强化只会通过同步动作执行；当前状态不适合提交。单人模式下可以正常使用。"
        : "Card upgrading in multiplayer only runs through a synced action, and the current state is not safe to submit. It works normally in singleplayer.";

    /// <summary>
    /// 多人模式战斗中卡片强化禁用提示。
    /// </summary>
    public static string MultiplayerCardUpgradeInCombatDisabled => IsChinese
        ? "多人模式下战斗中暂不开放牌组卡片强化：请在非战斗稳定状态下操作，避免战斗牌堆和牌组状态不同步。"
        : "Deck card upgrading is not available during multiplayer combat. Use it outside combat to avoid deck/combat pile desyncs.";

    /// <summary>
    /// 多人模式战斗中卡片附魔禁用提示。
    /// </summary>
    public static string MultiplayerCardEnchantInCombatDisabled => IsChinese
        ? "多人模式下战斗中暂不开放牌组卡片附魔：请在非战斗稳定状态下操作，避免战斗牌堆和牌组状态不同步。"
        : "Deck card enchantment is not available during multiplayer combat. Use it outside combat to avoid deck/combat pile desyncs.";

    /// <summary>
    /// 多人模式能力功能禁用提示。
    /// </summary>
    public static string MultiplayerPowerDisabled => IsChinese
        ? "多人模式下暂不开放能力图鉴操作：它会直接修改玩家或怪物身上的能力，可能导致战斗状态不同步。单人模式下可以正常使用。"
        : "Power editing is not available in multiplayer yet because it directly changes powers on players or monsters and may desync combat state. It works normally in singleplayer.";

    /// <summary>
    /// 多人模式召唤怪物功能禁用提示。
    /// </summary>
    public static string MultiplayerMonsterDisabled => IsChinese
        ? "多人模式下暂不开放召唤怪物：怪物列表和站位是联机战斗核心状态，当前实现可能导致卡死或崩溃。单人模式下可以正常使用。"
        : "Monster spawning is not available in multiplayer yet because enemy lists and positions are core synced combat state and may cause hangs or crashes. It works normally in singleplayer.";

    /// <summary>
    /// 多人模式岁月史书功能禁用提示。
    /// </summary>
    public static string MultiplayerChronicleDisabled => IsChinese
        ? "多人模式下岁月史书仅开放可通过游戏内置网络命令同步的功能；其他功能会被禁用，避免联机不同步。单人模式下可以正常使用。"
        : "In multiplayer, Chronicle only enables actions that can sync through native network console commands. Other actions are disabled to avoid desyncs. They work normally in singleplayer.";

    /// <summary>
    /// 多人模式岁月史书单人专用功能禁用提示。
    /// </summary>
    public static string ChronicleMultiplayerSingleplayerOnly => IsChinese
        ? "多人模式下该岁月史书功能仍只保留给单人：它会直接改运行时状态、持续回填或影响地图/倍率流程。"
        : "This Chronicle action remains singleplayer-only in multiplayer because it directly changes runtime state, repeats writes, or affects map/multiplier flow.";

    /// <summary>
    /// 多人模式岁月史书非命令功能禁用提示。
    /// </summary>
    public static string ChronicleMultiplayerCommandOnlyDisabled => IsChinese
        ? "联机下该功能不可用：它无法通过游戏内置网络命令安全同步。单机游玩时可以正常使用。"
        : "This Chronicle action is unavailable in multiplayer because it cannot be safely synced through a native network command. It works normally in singleplayer.";

    /// <summary>
    /// 多人模式岁月史书命令模式状态提示。
    /// </summary>
    public static string ChronicleMultiplayerCommandModeStatus => IsChinese
        ? "联机模式：岁月史书仅开放可通过游戏内置网络命令同步的功能，其他功能已禁用。"
        : "Multiplayer: Chronicle only enables actions that can sync through native network commands. Other actions are disabled.";

    /// <summary>
    /// 多人模式岁月史书数值只允许正数增量的提示。
    /// </summary>
    public static string ChronicleMultiplayerPositiveDeltaOnly => IsChinese
        ? "多人模式下该原生命令只能增加，不能直接降低或本地校准。"
        : "In multiplayer, this native command can only increase the value; direct decreases or local correction are blocked.";

    /// <summary>
    /// 多人模式岁月史书同步命令已提交提示。
    /// </summary>
    public static string ChronicleSubmitSuccess => IsChinese
        ? "岁月史书命令已提交同步。"
        : "Chronicle command sync submitted.";

    /// <summary>
    /// 岁月史书诊断工具分区标题。
    /// </summary>
    public static string ChronicleDiagnosticsSectionTitle => IsChinese ? "诊断工具" : "Diagnostics";

    /// <summary>
    /// 岁月史书日志抛出检测按钮文本。
    /// </summary>
    public static string ChronicleThrowLogTestButton => IsChinese ? "日志抛出检测" : "Log Throw Test";

    /// <summary>
    /// 岁月史书日志抛出检测已提交提示。
    /// </summary>
    public static string ChronicleThrowLogTestSubmitted => IsChinese
        ? "已写入一条测试异常日志，请查看诊断包是否生成。"
        : "A test exception log was written. Check whether a diagnostic package was created.";

    /// <summary>
    /// 岁月史书日志抛出检测异常未触发的兜底提示。
    /// </summary>
    public static string ChronicleThrowLogTestFailed => IsChinese
        ? "日志抛出检测未触发异常，请查看 GM 日志。"
        : "The log throw test did not trigger an exception. Check the GM log.";

    /// <summary>
    /// 多人模式升级版发卡禁用提示。
    /// </summary>
    public static string MultiplayerUpgradedCardGrantDisabled => IsChinese
        ? "多人模式下暂不支持直接发升级版卡片：当前只能安全地走原生发卡网络命令发基础版，避免牌组状态不同步。单人模式下可以正常使用。"
        : "Granting upgraded cards is not available in multiplayer yet. Only the native networked base-card command is used to avoid deck desyncs. This works normally in singleplayer.";

    public static string NoDescription => IsChinese ? "暂无描述" : "No description.";

    /// <summary>
    /// 恢复当前页签默认筛选按钮文本。
    /// </summary>
    public static string ResetFiltersButton => IsChinese ? "恢复默认筛选" : "Reset Filters";

    /// <summary>
    /// 恢复当前页签默认筛选按钮悬停提示。
    /// </summary>
    public static string ResetFiltersTooltip => IsChinese ? "只恢复当前页签的搜索和筛选，不清空已选内容。" : "Reset search and filters for this tab only. Selected items are kept.";

    public static string NotInRun => IsChinese ? "当前不在可发放遗物的游戏局内。" : "Not currently in a run.";

    public static string AlreadyOwnedNamed => IsChinese ? "{0} 已拥有，不能重复获取。" : "{0} is already owned.";

    public static string GrantSuccessNamed => IsChinese ? "{0} 发放成功。" : "{0} granted.";

    public static string GrantSubmittedNamed => IsChinese ? "{0} 已提交同步发放。" : "{0} grant sync submitted.";

    public static string GrantFailedNamed => IsChinese ? "{0} 发放失败。" : "{0} failed.";

    public static string RemoveSuccessNamed => IsChinese ? "{0} 删除成功。" : "{0} removed.";

    public static string RemoveSubmittedNamed => IsChinese ? "{0} 已提交同步删除。" : "{0} remove sync submitted.";

    public static string RemoveFailedNamed => IsChinese ? "{0} 删除失败。" : "{0} remove failed.";

    public static string RemoveMissingNamed => IsChinese ? "{0} 当前未持有，跳过删除。" : "{0} is not currently owned.";

    public static string PotionGrantSuccessNamed => IsChinese ? "{0} 发放成功。" : "{0} granted.";

    public static string PotionGrantSubmittedNamed => IsChinese ? "{0} 已提交同步发放。" : "{0} grant sync submitted.";

    public static string PotionGrantFailedNamed => IsChinese ? "{0} 发放失败。" : "{0} failed.";

    public static string PotionSlotsFullNamed => IsChinese ? "药水栏已满，{0} 未发放。" : "Potion slots are full. {0} was not granted.";

    public static string PotionSlotControl => IsChinese ? "药水栏位控制" : "Potion Slot Control";

    public static string PotionSlotDecreaseTip => IsChinese ? "减少一个药水栏" : "Remove one potion slot";

    public static string PotionSlotIncreaseTip => IsChinese ? "增加一个药水栏" : "Add one potion slot";

    public static string PotionSlotsCannotReduce(int filled, int target) => IsChinese
        ? $"已有 {filled} 瓶药水放入，无法降低到 {target} 个栏位。"
        : $"{filled} potion(s) are placed. Cannot reduce to {target} slots.";

    public static string PotionSlotsFilled(int filled) => IsChinese
        ? $"当前已放入: {filled}"
        : $"Filled: {filled}";

    /// <summary>
    /// 球盒页签标题。
    /// </summary>
    public static string OrbBoxTab => IsChinese ? "机煲球盒" : "Orb Box";

    /// <summary>
    /// 球盒搜索框占位文本。
    /// </summary>
    public static string OrbSearchPlaceholder => IsChinese ? "输入充能球名称、描述或 ID" : "Search orb name, description or ID";

    /// <summary>
    /// 球盒确认按钮文本。
    /// </summary>
    public static string OrbGrantConfirmButton => IsChinese ? "确认发放充能球" : "Grant Orbs";

    /// <summary>
    /// 球盒未选择提示。
    /// </summary>
    public static string OrbSelectFirst => IsChinese ? "请先选择至少一个充能球。" : "Select at least one orb first.";

    /// <summary>
    /// 球盒不在有效局内提示。
    /// </summary>
    public static string OrbNotInRun => IsChinese ? "当前不在可发放充能球的游戏局内。" : "Not currently in a run for granting orbs.";

    /// <summary>
    /// 球盒不在可操作战斗状态提示。
    /// </summary>
    public static string OrbNotInCombat => IsChinese ? "当前没有可操作的充能球队列。" : "No operable orb queue is available.";

    /// <summary>
    /// 球盒没有栏位提示。
    /// </summary>
    public static string OrbNoSlots => IsChinese ? "当前没有充能球栏位，未自动增加栏位。" : "No orb slots available. Slots were not added automatically.";

    /// <summary>
    /// 球盒单个充能球发放成功提示。
    /// </summary>
    public static string OrbGrantSuccessNamed => IsChinese ? "{0} 发放成功。" : "{0} granted.";

    /// <summary>
    /// 球盒单个充能球发放失败提示。
    /// </summary>
    public static string OrbGrantFailedNamed => IsChinese ? "{0} 发放失败。" : "{0} failed.";

    /// <summary>
    /// 多人模式单个充能球发放同步动作已提交提示。
    /// </summary>
    public static string OrbGrantSubmittedNamed => IsChinese ? "{0} 已提交同步发放。" : "{0} grant sync submitted.";

    /// <summary>
    /// 荒疫页签标题。
    /// </summary>
    public static string BlightTab => IsChinese ? "荒疫" : "Blights";

    /// <summary>
    /// 荒疫搜索框占位文本。
    /// </summary>
    public static string BlightSearchPlaceholder => IsChinese ? "输入荒疫名称、描述、来源或 ID" : "Search blight name, description, source or ID";

    /// <summary>
    /// 荒疫添加确认按钮文本。
    /// </summary>
    public static string BlightAddConfirmButton => IsChinese ? "确认添加荒疫" : "Add Blights";

    /// <summary>
    /// 荒疫移除确认按钮文本。
    /// </summary>
    public static string BlightRemoveConfirmButton => IsChinese ? "确认移除荒疫" : "Remove Blights";

    /// <summary>
    /// 荒疫未选择提示。
    /// </summary>
    public static string BlightSelectFirst => IsChinese ? "请先选择至少一个荒疫。" : "Select at least one blight first.";

    /// <summary>
    /// 荒疫不在有效局内提示。
    /// </summary>
    public static string BlightNotInRun => IsChinese ? "当前不在可操作荒疫的游戏局内。" : "Not currently in a run for operating blights.";

    /// <summary>
    /// 荒疫已启用提示。
    /// </summary>
    public static string BlightAlreadyEnabledNamed => IsChinese ? "{0} 已启用，跳过添加。" : "{0} is already enabled.";

    /// <summary>
    /// 荒疫未启用提示。
    /// </summary>
    public static string BlightNotEnabledNamed => IsChinese ? "{0} 当前未启用，跳过移除。" : "{0} is not enabled.";

    /// <summary>
    /// 荒疫添加成功提示。
    /// </summary>
    public static string BlightAddSuccessNamed => IsChinese ? "{0} 添加成功。" : "{0} added.";

    /// <summary>
    /// 荒疫添加失败提示。
    /// </summary>
    public static string BlightAddFailedNamed => IsChinese ? "{0} 添加失败。" : "{0} add failed.";

    /// <summary>
    /// 荒疫移除成功提示。
    /// </summary>
    public static string BlightRemoveSuccessNamed => IsChinese ? "{0} 移除成功。" : "{0} removed.";

    /// <summary>
    /// 荒疫移除失败提示。
    /// </summary>
    public static string BlightRemoveFailedNamed => IsChinese ? "{0} 移除失败。" : "{0} remove failed.";

    public static string BuildGrantSummary(int successCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"成功 {successCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Success {successCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总多人模式遗物发放同步命令提交结果。
    /// </summary>
    public static string BuildGrantSubmitSummary(int submittedCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"已提交同步发放 {submittedCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Grant sync submitted {submittedCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总删除遗物结果。
    /// </summary>
    public static string BuildRemoveSummary(int successCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"删除成功 {successCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Removed {successCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总多人模式遗物删除同步命令提交结果。
    /// </summary>
    public static string BuildRemoveSubmitSummary(int submittedCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"已提交同步删除 {submittedCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Remove sync submitted {submittedCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总药水发放结果。
    /// </summary>
    public static string BuildPotionGrantSummary(int successCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"药水发放成功 {successCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Potions granted {successCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总多人模式药水同步命令提交结果。
    /// </summary>
    public static string BuildPotionGrantSubmitSummary(int submittedCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"药水发放已提交同步 {submittedCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Potion grant sync submitted {submittedCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总充能球发放结果。
    /// </summary>
    public static string BuildOrbGrantSummary(int successCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"充能球发放成功 {successCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Orbs granted {successCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总多人模式充能球同步动作提交结果。
    /// </summary>
    public static string BuildOrbGrantSubmitSummary(int submittedCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"充能球发放已提交同步 {submittedCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Orb grant sync submitted {submittedCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总荒疫添加结果。
    /// </summary>
    public static string BuildBlightAddSummary(int successCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"荒疫添加成功 {successCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Blights added {successCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总荒疫移除结果。
    /// </summary>
    public static string BuildBlightRemoveSummary(int successCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"荒疫移除成功 {successCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Blights removed {successCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总能力发放结果。
    /// </summary>
    public static string BuildPowerGrantSummary(int successCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"能力发放成功 {successCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Powers applied {successCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总多人模式能力赋予同步动作提交结果。
    /// </summary>
    public static string BuildPowerGrantSubmitSummary(int submittedCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"能力赋予已提交同步 {submittedCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Power apply sync submitted {submittedCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总能力删除结果。对应 UI 上的【删除能力】模式（按选中的能力名删除）。
    /// </summary>
    public static string BuildPowerClearSummary(int successCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"能力删除成功 {successCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Powers deleted {successCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总多人模式能力删除同步动作提交结果。
    /// </summary>
    public static string BuildPowerClearSubmitSummary(int submittedCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"能力删除已提交同步 {submittedCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Power delete sync submitted {submittedCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总能力清除（一键清空目标全部能力）结果。
    /// </summary>
    public static string BuildPowerWipeSummary(int successCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"能力清除成功 {successCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Powers cleared {successCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总多人模式能力清空同步动作提交结果。
    /// </summary>
    public static string BuildPowerWipeSubmitSummary(int submittedCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"能力清空已提交同步 {submittedCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Power wipe sync submitted {submittedCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 多人模式能力赋予同步动作提交明细。
    /// </summary>
    public static string PowerApplySubmitted(int count)
    {
        return IsChinese ? $"能力赋予已提交同步 {count} 项。" : $"Power apply sync submitted for {count} item(s).";
    }

    /// <summary>
    /// 多人模式能力赋予同步动作提交失败明细。
    /// </summary>
    public static string PowerApplySubmitFailed(int count)
    {
        return IsChinese ? $"能力赋予同步提交失败 {count} 项。" : $"Power apply sync submit failed for {count} item(s).";
    }

    /// <summary>
    /// 多人模式能力删除同步动作提交明细。
    /// </summary>
    public static string PowerClearSubmitted(int count)
    {
        return IsChinese ? $"能力删除已提交同步 {count} 项。" : $"Power delete sync submitted for {count} item(s).";
    }

    /// <summary>
    /// 多人模式能力删除同步动作提交失败明细。
    /// </summary>
    public static string PowerClearSubmitFailed(int count)
    {
        return IsChinese ? $"能力删除同步提交失败 {count} 项。" : $"Power delete sync submit failed for {count} item(s).";
    }

    /// <summary>
    /// 多人模式能力清空同步动作提交明细。
    /// </summary>
    public static string PowerWipeSubmitted(int count)
    {
        return IsChinese ? $"能力清空已提交同步 {count} 个目标。" : $"Power wipe sync submitted for {count} target(s).";
    }

    /// <summary>
    /// 多人模式能力清空同步动作提交失败明细。
    /// </summary>
    public static string PowerWipeSubmitFailed(int count)
    {
        return IsChinese ? $"能力清空同步提交失败 {count} 个目标。" : $"Power wipe sync submit failed for {count} target(s).";
    }

    /// <summary>
    /// 能力图鉴页签标题。
    /// </summary>
    public static string PowerCatalogTab => IsChinese ? "能力图鉴" : "Power Catalog";

    /// <summary>
    /// 能力图鉴搜索框占位文本。
    /// </summary>
    public static string PowerSearchPlaceholder => IsChinese ? "输入能力名称、描述或 ID" : "Search power name, description or ID";

    /// <summary>
    /// 能力类型筛选：全部。
    /// </summary>
    public static string PowerTypeAll => IsChinese ? "全部类型" : "All Types";

    /// <summary>
    /// 能力类型筛选：增益（Buff）。
    /// </summary>
    public static string PowerTypeBuff => IsChinese ? "增益（Buff）" : "Buff";

    /// <summary>
    /// 能力类型筛选：减益（Debuff）。
    /// </summary>
    public static string PowerTypeDebuff => IsChinese ? "减益（Debuff）" : "Debuff";

    /// <summary>
    /// 能力类型筛选：中立。
    /// </summary>
    public static string PowerTypeNeutral => IsChinese ? "中立" : "Neutral";

    /// <summary>
    /// 能力图鉴发放模式文本。
    /// </summary>
    public static string PowerModeGrant => IsChinese ? "赋予能力" : "Apply Powers";

    /// <summary>
    /// 能力图鉴删除模式文本（按选中的能力删除）。
    /// </summary>
    public static string PowerModeClear => IsChinese ? "删除能力" : "Delete Powers";

    /// <summary>
    /// 能力图鉴清除模式文本（不需要选能力，一键清空目标身上的全部能力）。
    /// </summary>
    public static string PowerModeWipe => IsChinese ? "清除能力" : "Clear All Powers";

    /// <summary>
    /// 能力图鉴目标为玩家文本。
    /// </summary>
    public static string PowerTargetPlayer => IsChinese ? "玩家" : "Player";

    /// <summary>
    /// 能力图鉴目标为怪物文本。
    /// </summary>
    public static string PowerTargetMonster => IsChinese ? "怪物" : "Monsters";

    /// <summary>
    /// 能力图鉴全部怪物选项文本。
    /// </summary>
    public static string PowerTargetAllMonsters => IsChinese ? "全部怪物" : "All Monsters";

    /// <summary>
    /// 能力图鉴模式标签文本。
    /// </summary>
    public static string PowerModeLabel => IsChinese ? "模式" : "Mode";

    /// <summary>
    /// 能力图鉴目标标签文本。
    /// </summary>
    public static string PowerTargetLabel => IsChinese ? "目标" : "Target";

    /// <summary>
    /// 能力图鉴怪物目标标签文本。
    /// </summary>
    public static string PowerMonsterTargetLabel => IsChinese ? "怪物目标" : "Monster Targets";

    /// <summary>
    /// 能力图鉴确认发放按钮文本。
    /// </summary>
    public static string PowerGrantConfirmButton => IsChinese ? "确认赋予能力" : "Apply Powers";

    /// <summary>
    /// 能力图鉴确认删除按钮文本（按选中能力删除）。
    /// </summary>
    public static string PowerClearConfirmButton => IsChinese ? "确认删除能力" : "Delete Powers";

    /// <summary>
    /// 能力图鉴确认清除按钮文本（一键清空目标全部能力）。
    /// </summary>
    public static string PowerWipeConfirmButton => IsChinese ? "确认清除能力" : "Clear All Powers";

    /// <summary>
    /// 能力图鉴未选择能力时的提示。
    /// </summary>
    public static string PowerSelectFirst => IsChinese ? "请先选择至少一个能力。" : "Select at least one power first.";

    /// <summary>
    /// 能力图鉴玩家目标不可用提示。
    /// </summary>
    public static string PowerNotInRun => IsChinese ? "当前不在游戏局内，无法对玩家操作能力。" : "Not currently in a run. Cannot operate player powers.";

    /// <summary>
    /// 能力图鉴怪物目标不可用提示。
    /// </summary>
    public static string PowerNotInCombat => IsChinese ? "当前不在战斗中，无法对怪物操作能力。" : "Not currently in combat. Cannot operate monster powers.";

    /// <summary>
    /// 能力图鉴未选择怪物目标时的提示。
    /// </summary>
    public static string PowerMonsterTargetSelectFirst => IsChinese ? "请先选择至少一个怪物目标，或勾选全部怪物。" : "Select at least one monster target or enable all monsters.";

    /// <summary>
    /// 能力图鉴目标不可用的兜底提示。
    /// </summary>
    public static string PowerTargetUnavailable => IsChinese ? "当前没有可操作目标。" : "No valid targets available.";

    /// <summary>
    /// 能力图鉴能力按钮已选增量文本。
    /// </summary>
    public static string PowerDeltaSelected(int delta)
    {
        return IsChinese
            ? $"层数 {delta:+#;-#;0}"
            : $"Stacks {delta:+#;-#;0}";
    }

    /// <summary>
    /// 能力图鉴能力按钮待删除文本（在【删除能力】模式下，被选中的能力会显示这个标记）。
    /// </summary>
    public static string PowerMarkedForClear => IsChinese ? "待删除" : "To Delete";

    /// <summary>
    /// 能力图鉴【清除能力】模式提示：无须选择能力，确认即清空目标全部能力。
    /// </summary>
    public static string PowerWipeHint => IsChinese ? "清除模式无须选择能力，点击确认将清空所选目标身上的全部能力。" : "Clear-all mode does not require power selection. Confirm to wipe all powers from selected targets.";

    /// <summary>
    /// 刷怪笼页签标题。
    /// </summary>
    public static string MonsterCageTab => IsChinese ? "刷怪笼" : "Monster Cage";

    /// <summary>
    /// 刷怪笼搜索框占位文本。
    /// </summary>
    public static string MonsterSearchPlaceholder => IsChinese ? "输入怪物名称或 ID" : "Search monster name or ID";

    /// <summary>
    /// 刷怪笼确认按钮文本。
    /// </summary>
    public static string MonsterConfirmButton => IsChinese ? "确认召唤" : "Summon";

    /// <summary>
    /// 刷怪笼提示当前不在可召唤状态。
    /// </summary>
    public static string MonsterNotInCombat => IsChinese ? "当前不在战斗房间，无法召唤怪物。" : "Not currently in a combat room. Cannot summon monsters.";

    /// <summary>
    /// 刷怪笼未选择怪物时的提示。
    /// </summary>
    public static string MonsterSelectFirst => IsChinese ? "请先选择至少一个怪物。" : "Select at least one monster first.";

    /// <summary>
    /// 怪物召唤成功提示，{0} 会被替换为怪物名称。
    /// </summary>
    public static string MonsterSpawnSuccessNamed => IsChinese ? "{0} 召唤成功。" : "{0} summoned.";

    /// <summary>
    /// 怪物召唤失败提示，{0} 会被替换为怪物名称。
    /// </summary>
    public static string MonsterSpawnFailedNamed => IsChinese ? "{0} 召唤失败。" : "{0} summon failed.";

    /// <summary>
    /// 多人模式单个怪物召唤同步动作已提交提示，{0} 会被替换为怪物名称。
    /// </summary>
    public static string MonsterSpawnSubmittedNamed => IsChinese ? "{0} 已提交同步召唤。" : "{0} summon sync submitted.";

    /// <summary>
    /// 刷怪笼高风险跨房间召唤开关文本。
    /// </summary>
    public static string MonsterRiskySummonLabel => IsChinese ? "允许高风险跨房间召唤" : "Allow risky cross-room summon";

    /// <summary>
    /// 刷怪笼高风险跨房间召唤开关提示。
    /// </summary>
    public static string MonsterRiskySummonTooltip => IsChinese ? "关闭时只允许召唤适合当前房间类型的怪物；开启后允许强制召唤，但可能触发原生怪物机制异常。" : "When off, only monsters suitable for the current room type can be summoned. When on, forced summon is allowed but native monster mechanics may break.";

    /// <summary>
    /// 刷怪笼安全模式状态文本。
    /// </summary>
    public static string MonsterSafeModeStatus => IsChinese ? "安全召唤模式" : "Safe summon mode";

    /// <summary>
    /// 刷怪笼高风险模式状态文本。
    /// </summary>
    public static string MonsterRiskyModeStatus => IsChinese ? "高风险召唤模式" : "Risky summon mode";

    /// <summary>
    /// 刷怪笼安全模式悬停提示。
    /// </summary>
    public static string MonsterSafeModeTooltip => IsChinese ? "安全模式下确认召唤时会跳过该怪物。" : "Safe mode will skip this monster when confirming.";

    /// <summary>
    /// 刷怪笼高风险模式悬停提示。
    /// </summary>
    public static string MonsterRiskyModeTooltip => IsChinese ? "高风险模式已开启，仍会允许召唤该怪物。" : "Risky mode is enabled, so this monster can still be summoned.";

    /// <summary>
    /// 刷怪笼房间不匹配提示，{0} 为当前房间，{1} 为怪物品阶。
    /// </summary>
    public static string MonsterRoomMismatchHint(string currentRoom, string monsterTier)
    {
        return IsChinese
            ? $"房间不匹配：当前 {currentRoom}，怪物属于 {monsterTier}。"
            : $"Room mismatch: current {currentRoom}, monster belongs to {monsterTier}.";
    }

    /// <summary>
    /// 汇总怪物召唤结果。
    /// </summary>
    public static string BuildMonsterSpawnSummary(int successCount, int failedCount)
    {
        return IsChinese
            ? $"怪物召唤成功 {successCount}，失败 {failedCount}"
            : $"Monsters summoned {successCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总多人模式怪物召唤同步动作提交结果。
    /// </summary>
    public static string BuildMonsterSpawnSubmitSummary(int submittedCount, int failedCount)
    {
        return IsChinese
            ? $"怪物召唤已提交同步 {submittedCount}，失败 {failedCount}"
            : $"Monster summon sync submitted {submittedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 怪物页全部品阶筛选文本。
    /// </summary>
    public static string MonsterTierAll => IsChinese ? "全部品阶" : "All Tiers";

    /// <summary>
    /// 普通怪物品阶文本。
    /// </summary>
    public static string MonsterTierNormal => IsChinese ? "普通怪物" : "Normal";

    /// <summary>
    /// 精英怪物品阶文本。
    /// </summary>
    public static string MonsterTierElite => IsChinese ? "精英" : "Elite";

    /// <summary>
    /// Boss 怪物品阶文本。
    /// </summary>
    public static string MonsterTierBoss => IsChinese ? "Boss" : "Boss";

    /// <summary>
    /// 怪物没有可用预览图时的占位文本。
    /// </summary>
    public static string MonsterNoPreview => IsChinese ? "无预览" : "No Preview";

    /// <summary>
    /// 怪物血量范围文本。
    /// </summary>
    public static string MonsterHpRange(int minHp, int maxHp)
    {
        return IsChinese
            ? $"生命值 {minHp}-{maxHp}"
            : $"HP {minHp}-{maxHp}";
    }

    /// <summary>
    /// 事件任意门页签标题。
    /// </summary>
    public static string EventRegistryTab => IsChinese ? "事件任意门" : "Event Portal";

    /// <summary>
    /// 事件搜索框占位文本。
    /// </summary>
    public static string EventSearchPlaceholder => IsChinese ? "输入事件名称或 ID" : "Search event name or ID";

    /// <summary>
    /// 事件触发成功提示，{0} 会被替换为事件名称。
    /// </summary>
    public static string EventTriggerSuccessNamed => IsChinese ? "正在进入事件: {0}" : "Entering event: {0}";

    /// <summary>
    /// 多人模式事件任意门同步动作已提交提示，{0} 会被替换为事件名称。
    /// </summary>
    public static string EventTriggerSubmittedNamed => IsChinese ? "事件任意门已提交同步: {0}" : "Event travel sync submitted: {0}";

    /// <summary>
    /// 事件触发失败提示，{0} 会被替换为事件名称。
    /// </summary>
    public static string EventTriggerFailedNamed => IsChinese ? "{0} 触发失败。" : "{0} trigger failed.";

    /// <summary>
    /// 不在游戏局内时尝试触发事件的提示。
    /// </summary>
    public static string EventNotInRun => IsChinese ? "当前不在游戏局内，无法触发事件。" : "Not currently in a run. Cannot trigger event.";

    /// <summary>
    /// 事件页签快捷跳转区标题。
    /// </summary>
    public static string EventQuickJumpTitle => IsChinese ? "事件快捷跳转" : "Event Quick Jump";

    /// <summary>
    /// 阶段选择标签。
    /// </summary>
    public static string EventActSelectLabel => IsChinese ? "阶段" : "Act";

    /// <summary>
    /// 宝箱房选择标签。
    /// </summary>
    public static string EventTreasureSelectLabel => IsChinese ? "宝箱房选择" : "Treasure Room";

    /// <summary>
    /// 前往按钮文本。
    /// </summary>
    public static string EventGoButton => IsChinese ? "前往" : "Go";

    /// <summary>
    /// 进入商店按钮文本。
    /// </summary>
    public static string EventEnterShopButton => IsChinese ? "进入商店" : "Enter Shop";

    /// <summary>
    /// 进入营火按钮文本。
    /// </summary>
    public static string EventEnterRestSiteButton => IsChinese ? "进入营火" : "Enter Rest Site";

    /// <summary>
    /// 阶段跳转成功提示，{0} 会被替换为阶段名称。
    /// </summary>
    public static string EventActJumpSuccessNamed => IsChinese ? "正在前往阶段: {0}" : "Entering act: {0}";

    /// <summary>
    /// 房间跳转成功提示，{0} 会被替换为房间名称。
    /// </summary>
    public static string EventRoomJumpSuccessNamed => IsChinese ? "正在进入房间: {0}" : "Entering room: {0}";

    /// <summary>
    /// 快捷跳转失败提示，{0} 会被替换为目标名称。
    /// </summary>
    public static string EventQuickJumpFailedNamed => IsChinese ? "{0} 跳转失败。" : "{0} jump failed.";

    /// <summary>
    /// 岁月史书页签标题。
    /// </summary>
    public static string ChronicleTab => IsChinese ? "岁月史书" : "Chronicle";

    /// <summary>
    /// 岁月史书不在局内时的提示。
    /// </summary>
    public static string ChronicleNotInRun => IsChinese ? "当前不在游戏局内，无法修改岁月史书。" : "Not currently in a run.";

    /// <summary>
    /// 岁月史书需要战斗状态时的提示。
    /// </summary>
    public static string ChronicleNeedCombat => IsChinese ? "当前不在战斗中，无法执行该功能。" : "Not currently in combat.";

    /// <summary>
    /// 岁月史书输入不是整数时的提示。
    /// </summary>
    public static string ChronicleInvalidNumber => IsChinese ? "请输入整数。" : "Enter an integer.";

    /// <summary>
    /// 岁月史书应用成功提示。
    /// </summary>
    public static string ChronicleApplySuccess => IsChinese ? "岁月史书已改写。" : "Chronicle applied.";

    /// <summary>
    /// 岁月史书应用失败提示。
    /// </summary>
    public static string ChronicleApplyFailed => IsChinese ? "岁月史书改写失败。" : "Chronicle failed.";

    /// <summary>
    /// 岁月史书开关开启提示。
    /// </summary>
    public static string ChronicleToggleOn => IsChinese ? "已开启。" : "Enabled.";

    /// <summary>
    /// 岁月史书开关关闭提示。
    /// </summary>
    public static string ChronicleToggleOff => IsChinese ? "已关闭。" : "Disabled.";

    /// <summary>
    /// 岁月史书无需执行时的提示。
    /// </summary>
    public static string ChronicleNothingToDo => IsChinese ? "当前无需处理。" : "Nothing to do.";

    /// <summary>
    /// 岁月史书应用按钮文本。
    /// </summary>
    public static string ChronicleApplyButton => IsChinese ? "应用" : "Apply";

    /// <summary>
    /// 岁月史书锁定复选框文本。
    /// </summary>
    public static string ChronicleLockLabel => IsChinese ? "锁定" : "Lock";

    /// <summary>
    /// 多人模式下岁月史书锁定不可用时的悬浮提示。
    /// </summary>
    public static string ChronicleLockMultiplayerDisabledTooltip => IsChinese
        ? "多人模式下不开放锁定数值：锁定会持续回填运行时状态，可能导致联机不同步、卡死或崩溃。单人模式下可以正常使用。"
        : "Locked Chronicle values are not available in multiplayer because they repeatedly write runtime state and may cause desyncs, hangs, or crashes. They work normally in singleplayer.";

    /// <summary>
    /// 岁月史书执行按钮文本。
    /// </summary>
    public static string ChronicleRunButton => IsChinese ? "执行" : "Run";

    /// <summary>
    /// 岁月史书待实现功能提示。
    /// </summary>
    public static string ChroniclePendingFeature => IsChinese ? "该功能第一版先不做。" : "This feature is skipped in the first version.";

    /// <summary>
    /// 岁月史书“手牌上限”条目的悬浮提示。
    /// </summary>
    public static string ChronicleHandLimitTooltip => IsChinese
        ? "修改真实手牌上限；数值过大时手牌会变得拥挤。"
        : "Changes the real hand limit; very large values will crowd the hand UI.";

    /// <summary>
    /// 岁月史书倍率补丁暂未接入时的提示。
    /// </summary>
    public static string ChroniclePatchPending => IsChinese ? "该倍率入口已保留，运行时补丁后续单独接入。" : "This multiplier is saved, but its runtime patch is not wired yet.";

    /// <summary>
    /// 岁月史书当前没有可显示地图房间时的提示。
    /// </summary>
    public static string ChronicleNoMapRooms => IsChinese ? "当前没有可跳转的地图房间。" : "No map rooms available.";

    /// <summary>
    /// 岁月史书任意房间跳转成功提示，{0} 会被替换为目标房间名称。
    /// </summary>
    public static string ChronicleMapRoomJumpSuccessNamed => IsChinese ? "正在前往房间: {0}" : "Entering room: {0}";

    public static string ChronicleEnemyIntentLabel => IsChinese ? "改变敌人意图" : "Change Enemy Intent";

    public static string ChronicleAllEnemiesLabel => IsChinese ? "全选" : "All";

    public static string ChronicleNoEnemyOption => IsChinese ? "无存活敌人" : "No alive enemies";

    public static string ChronicleNoIntentOption => IsChinese ? "无可用意图" : "No intents";

    public static string ChronicleNoEnemySelected => IsChinese ? "请选择一个存活敌人。" : "Select an alive enemy.";

    public static string ChronicleNoIntentSelected => IsChinese ? "请选择目标意图。" : "Select a target intent.";

    // ============================================================
    // 卡片自助页签相关文案
    // ============================================================

    /// <summary>
    /// 卡片自助页签标题。
    /// </summary>
    public static string CardGrantTab => IsChinese ? "卡片自助" : "Grant Cards";

    /// <summary>
    /// 卡片搜索框占位文本。
    /// </summary>
    public static string CardSearchPlaceholder => IsChinese ? "输入卡片名称或描述" : "Search card name or description";

    /// <summary>
    /// 确认发放按钮文本。
    /// </summary>
    public static string CardConfirmButton => IsChinese ? "确认发放" : "Grant";

    /// <summary>
    /// 导出全部卡片按钮文本。
    /// </summary>
    public static string CardExportButton => IsChinese ? "导出全部卡片" : "Export All Cards";

    /// <summary>
    /// 卡片导出成功提示。
    /// </summary>
    public static string CardExportSuccess => IsChinese ? "卡片名单已导出，路径已写入日志。" : "Card list exported. See log for path.";

    /// <summary>
    /// 卡片导出失败提示。
    /// </summary>
    public static string CardExportFailed => IsChinese ? "卡片名单导出失败，请查看日志。" : "Card export failed. See log.";

    /// <summary>
    /// 多人模式卡片勾选框文本。
    /// </summary>
    public static string CardMultiplayerLabel => IsChinese ? "多人模式卡片" : "Multiplayer Cards";

    /// <summary>
    /// 卡牌自助排序选项文本。
    /// </summary>
    public static string CardSortDefault => IsChinese ? "默认排序" : "Default Sort";
    public static string CardSortCost => IsChinese ? "费用" : "Cost";
    public static string CardSortDamage => IsChinese ? "伤害" : "Damage";
    public static string CardSortName => IsChinese ? "A~Z" : "A~Z";
    public static string CardSortAsc => IsChinese ? "升序" : "Asc";
    public static string CardSortDesc => IsChinese ? "降序" : "Desc";

    /// <summary>
    /// 查看升级勾选框文本。
    /// </summary>
    public static string CardShowUpgradeLabel => IsChinese ? "查看升级" : "Show Upgraded";

    /// <summary>
    /// 卡片类型筛选项。
    /// </summary>
    public static string CardTypeAll => IsChinese ? "全部类型" : "All Types";
    public static string CardTypeAttack => IsChinese ? "攻击" : "Attack";
    public static string CardTypeSkill => IsChinese ? "技能" : "Skill";
    public static string CardTypePower => IsChinese ? "能力" : "Power";
    public static string CardTypeStatus => IsChinese ? "状态" : "Status";
    public static string CardTypeCurse => IsChinese ? "诅咒" : "Curse";
    public static string CardTypeQuest => IsChinese ? "任务" : "Quest";

    /// <summary>
    /// 费用筛选项。
    /// </summary>
    public static string CardCostAll => IsChinese ? "全部费用" : "All Costs";
    public static string CardCost3Plus => IsChinese ? "3+ 费" : "3+ Cost";
    public static string CardCostX => IsChinese ? "X 费" : "X Cost";

    /// <summary>
    /// 角色筛选项。
    /// </summary>
    public static string CardCharacterAll => IsChinese ? "全部角色" : "All Characters";
    public static string CardCharacterColorless => IsChinese ? "无色" : "Colorless";

    /// <summary>
    /// 卡片不在游戏局内的提示。
    /// </summary>
    public static string CardNotInRun => IsChinese ? "当前不在游戏局内，无法发放卡片。" : "Not currently in a run.";

    /// <summary>
    /// 单张卡发放成功提示（非战斗，{0} 为卡名）。
    /// </summary>
    public static string CardGrantSuccessNamed => IsChinese ? "{0} 已加入牌组。" : "{0} added to deck.";

    /// <summary>
    /// 多人模式单张卡发放同步动作已提交提示。
    /// </summary>
    public static string CardGrantSubmittedNamed => IsChinese ? "{0} 已提交同步发放。" : "{0} grant sync submitted.";

    /// <summary>
    /// 单张卡发放成功提示（战斗中同时进入牌组与手牌）。
    /// </summary>
    public static string CardGrantSuccessInCombatNamed => IsChinese ? "{0} 已加入牌组与手牌。" : "{0} added to deck and hand.";

    /// <summary>
    /// 单张卡发放失败提示。
    /// </summary>
    public static string CardGrantFailedNamed => IsChinese ? "{0} 发放失败。" : "{0} failed.";

    /// <summary>
    /// 「请先选择卡片」提示。
    /// </summary>
    public static string CardSelectFirstHint => IsChinese ? "请先选择至少一张卡片。" : "Select at least one card first.";

    /// <summary>
    /// 不可升级卡按钮上的小标记（在勾选「查看升级」时使用）。
    /// </summary>
    public static string CardCannotUpgradeMark => IsChinese ? "不可升级" : "Max";

    /// <summary>
    /// 网格底部状态栏文本：已选 N 张 + 当前显示 M 张 + 操作提示。
    /// </summary>
    public static string CardGridStatus(int selectedCount, int showingCount)
    {
        return IsChinese
            ? $"已选 {selectedCount} 张    当前显示 {showingCount} 张    （右键清零，Ctrl+左键 +5）"
            : $"Selected {selectedCount}    Showing {showingCount}    (Right-click clear, Ctrl+Left-click +5)";
    }

    /// <summary>
    /// 卡片按钮上的「已选 xN」后缀。
    /// </summary>
    public static string CardSelectedSuffix(int count)
    {
        return IsChinese ? $"已选 x{count}" : $"Selected x{count}";
    }

    /// <summary>
    /// 汇总卡片自助结果。
    /// </summary>
    public static string BuildCardGrantSummary(int successCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"卡片自助成功 {successCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Cards granted {successCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总多人模式卡片发放同步动作提交结果。
    /// </summary>
    public static string BuildCardGrantSubmitSummary(int submittedCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"卡片发放已提交同步 {submittedCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Card grant sync submitted {submittedCount}, skipped {skippedCount}, failed {failedCount}";
    }

    // ============================================================
    // 卡片删除页签相关文案
    // ============================================================

    /// <summary>
    /// 卡片删除页签标题。
    /// </summary>
    public static string CardRemoveTab => IsChinese ? "卡片删除" : "Remove Cards";

    /// <summary>
    /// 卡片删除页搜索框占位文本。
    /// </summary>
    public static string CardRemoveSearchPlaceholder => IsChinese ? "输入牌组中的卡片名称或描述" : "Search deck card name or description";

    /// <summary>
    /// 卡片删除页确认按钮文本。
    /// </summary>
    public static string CardRemoveConfirmButton => IsChinese ? "移除卡片" : "Remove Cards";

    /// <summary>
    /// 删除页全选按钮文本。
    /// </summary>
    public static string SelectAllButton => IsChinese ? "全选" : "Select All";

    /// <summary>
    /// 删除页取消全选按钮文本。
    /// </summary>
    public static string CancelSelectAllButton => IsChinese ? "取消全选" : "Unselect All";

    /// <summary>
    /// 卡片删除页【全部类型/无色】筛选下拉项的「全部」入口。
    /// </summary>
    public static string CardRemoveCategoryAll => IsChinese ? "全部" : "All";

    /// <summary>
    /// 卡片删除页玩家不在游戏局内时的提示。
    /// </summary>
    public static string CardRemoveNotInRun => IsChinese ? "当前不在游戏局内，无可删除的牌组卡片。" : "Not currently in a run. No deck cards to remove.";

    /// <summary>
    /// 卡片删除页牌组为空时的提示。
    /// </summary>
    public static string CardRemoveEmptyDeck => IsChinese ? "当前牌组为空。" : "Deck is empty.";

    /// <summary>
    /// 单张卡片删除成功提示，{0} 会被替换为卡名。
    /// </summary>
    public static string CardRemoveSuccessNamed => IsChinese ? "{0} 已从牌组移除。" : "{0} removed from deck.";

    /// <summary>
    /// 多人模式单张卡片删除同步动作已提交提示。
    /// </summary>
    public static string CardRemoveSubmittedNamed => IsChinese ? "{0} 已提交同步移除。" : "{0} remove sync submitted.";

    /// <summary>
    /// 单张卡片删除跳过提示，{0} 会被替换为卡名。
    /// </summary>
    public static string CardRemoveSkippedNamed => IsChinese ? "{0} 已跳过，卡片重复选择或当前状态不适合移除。" : "{0} skipped. It was selected twice or cannot be removed now.";

    /// <summary>
    /// 单张卡片删除失败提示，{0} 会被替换为卡名。
    /// </summary>
    public static string CardRemoveFailedNamed => IsChinese ? "{0} 移除失败。" : "{0} remove failed.";

    /// <summary>
    /// 卡片删除页未选择卡片时的提示。
    /// </summary>
    public static string CardRemoveSelectFirst => IsChinese ? "请先选择至少一张要移除的卡片。" : "Select at least one card to remove first.";

    /// <summary>
    /// 战斗中执行卡片删除的提示：仅作用于全局牌组，不影响当前战斗。
    /// </summary>
    public static string CardRemoveInCombatHint => IsChinese
        ? "战斗中删除仅修改全局牌组，本场战斗的手牌、抽牌堆、弃牌堆与消耗堆保持不变。"
        : "In combat: only the run deck is modified. The current battle's hand, draw, discard and exhaust piles are untouched.";

    /// <summary>
    /// 汇总卡片删除结果。
    /// </summary>
    public static string BuildCardRemoveSummary(int successCount, int failedCount)
    {
        return IsChinese
            ? $"卡片删除成功 {successCount}，失败 {failedCount}"
            : $"Cards removed {successCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总多人模式卡片删除同步动作提交结果。
    /// </summary>
    public static string BuildCardRemoveSubmitSummary(int submittedCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"卡片删除已提交同步 {submittedCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Card remove sync submitted {submittedCount}, skipped {skippedCount}, failed {failedCount}";
    }

    // ============================================================
    // 卡片强化页签相关文案
    // ============================================================

    /// <summary>
    /// 卡片强化页签标题。
    /// </summary>
    public static string CardUpgradeTab => IsChinese ? "卡片强化" : "Upgrade Cards";

    /// <summary>
    /// 卡片强化页搜索框占位文本。
    /// </summary>
    public static string CardUpgradeSearchPlaceholder => IsChinese ? "输入牌组中可升级卡片的名称或描述" : "Search upgradable deck card name or description";

    /// <summary>
    /// 卡片强化页确认按钮文本。
    /// </summary>
    public static string CardUpgradeConfirmButton => IsChinese ? "强化卡片" : "Upgrade Cards";

    /// <summary>
    /// 卡片强化页未选择卡片时的提示。
    /// </summary>
    public static string CardUpgradeSelectFirst => IsChinese ? "请先选择至少一张要强化的卡片。" : "Select at least one card to upgrade first.";

    /// <summary>
    /// 卡片强化页玩家不在游戏局内时的提示。
    /// </summary>
    public static string CardUpgradeNotInRun => IsChinese ? "当前不在游戏局内，无可强化的牌组卡片。" : "Not currently in a run. No deck cards to upgrade.";

    /// <summary>
    /// 卡片强化页牌组中没有任何可升级卡片时的提示。
    /// </summary>
    public static string CardUpgradeNoUpgradable => IsChinese ? "当前牌组里没有可强化的卡片。" : "No upgradable cards in deck.";

    /// <summary>
    /// 战斗中执行卡片强化的提示：仅作用于全局牌组，不影响当前战斗。
    /// </summary>
    public static string CardUpgradeInCombatHint => IsChinese
        ? "战斗中强化仅修改全局牌组，本场战斗的手牌、抽牌堆、弃牌堆与消耗堆保持不变。"
        : "In combat: only the run deck is upgraded. The current battle's hand, draw, discard and exhaust piles are untouched.";

    /// <summary>
    /// 单张卡片强化成功提示，{0} 会被替换为卡名。
    /// </summary>
    public static string CardUpgradeSuccessNamed => IsChinese ? "{0} 已强化。" : "{0} upgraded.";

    /// <summary>
    /// 多人模式单张卡片强化同步动作已提交提示，{0} 会被替换为卡名。
    /// </summary>
    public static string CardUpgradeSubmittedNamed => IsChinese ? "{0} 已提交同步强化。" : "{0} upgrade sync submitted.";

    /// <summary>
    /// 单张卡片强化失败提示，{0} 会被替换为卡名。
    /// </summary>
    public static string CardUpgradeFailedNamed => IsChinese ? "{0} 强化失败。" : "{0} upgrade failed.";

    /// <summary>
    /// 单张卡片不可强化（已满级或本身不可升级）的提示，{0} 会被替换为卡名。
    /// </summary>
    public static string CardUpgradeSkippedNamed => IsChinese ? "{0} 已满级或不可升级，已跳过。" : "{0} is max-level or not upgradable, skipped.";

    /// <summary>
    /// 卡片强化右键预览弹窗标题，提示玩家这是升级后的预览。
    /// </summary>
    public static string CardUpgradePreviewTitle => IsChinese ? "升级后预览（按 Esc 或点击空白处关闭）" : "Upgrade Preview (press Esc or click outside to close)";

    /// <summary>
    /// 汇总卡片强化结果。
    /// </summary>
    public static string BuildCardUpgradeSummary(int successCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"卡片强化成功 {successCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Cards upgraded {successCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总多人模式卡片强化同步动作提交结果。
    /// </summary>
    public static string BuildCardUpgradeSubmitSummary(int submittedCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"卡片强化已提交同步 {submittedCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Card upgrade sync submitted {submittedCount}, skipped {skippedCount}, failed {failedCount}";
    }

    // ============================================================
    // 卡片附魔页签相关文案
    // ============================================================

    /// <summary>
    /// 卡片附魔页签标题。
    /// </summary>
    public static string CardEnchantTab => IsChinese ? "附魔自助" : "Enchant Cards";

    /// <summary>
    /// 附魔搜索框占位文本。
    /// </summary>
    public static string CardEnchantSearchPlaceholder => IsChinese ? "输入附魔名称或描述" : "Search enchantment name or description";

    /// <summary>
    /// 可附魔卡片搜索框占位文本。
    /// </summary>
    public static string CardEnchantCardSearchPlaceholder => IsChinese ? "输入可附魔卡片名称或描述" : "Search enchantable deck card name or description";

    /// <summary>
    /// 卡片附魔确认按钮文本。
    /// </summary>
    public static string CardEnchantConfirmButton => IsChinese ? "确认附魔" : "Enchant";

    /// <summary>
    /// 返回附魔选择按钮文本。
    /// </summary>
    public static string CardEnchantBackButton => IsChinese ? "返回选择附魔" : "Back";

    /// <summary>
    /// 未选择附魔时的提示。
    /// </summary>
    public static string CardEnchantSelectFirst => IsChinese ? "请先选择一个附魔。" : "Select an enchantment first.";

    /// <summary>
    /// 未选择卡片时的提示。
    /// </summary>
    public static string CardEnchantSelectCardFirst => IsChinese ? "请先选择至少一张要附魔的卡片。" : "Select at least one card to enchant first.";

    /// <summary>
    /// 附魔页玩家不在游戏局内时的提示。
    /// </summary>
    public static string CardEnchantNotInRun => IsChinese ? "当前不在游戏局内，无法为牌组卡片附魔。" : "Not currently in a run. Cannot enchant deck cards.";

    /// <summary>
    /// 附魔手填数值标签。
    /// </summary>
    public static string CardEnchantAmountLabel => IsChinese ? "附魔数值" : "Enchant Amount";

    /// <summary>
    /// 附魔手填数值输入框占位文本。
    /// </summary>
    public static string CardEnchantAmountPlaceholder => IsChinese ? "1~99" : "1~99";

    /// <summary>
    /// 附魔手填数值非法时的提示。
    /// </summary>
    public static string CardEnchantAmountInvalid => IsChinese ? "请输入 1 到 99 的附魔数值。" : "Enter an enchantment amount from 1 to 99.";

    /// <summary>
    /// 附魔安全模式开关文案。
    /// </summary>
    public static string CardEnchantSafetyModeLabel => IsChinese ? "附魔安全模式（推荐）" : "Enchantment Safe Mode (Recommended)";

    /// <summary>
    /// 附魔安全模式开关悬停提示。
    /// </summary>
    public static string CardEnchantSafetyModeTooltip => IsChinese
        ? "开启时只允许已确认安全的附魔手填数值；关闭后会允许实验性强制写入，可能导致描述异常、效果异常或读档丢值。"
        : "When on, only known-safe enchantments can use custom amounts. When off, experimental forced values are allowed and may cause bad descriptions, bad effects, or lost values after reload.";

    /// <summary>
    /// 安全附魔的手填提示。
    /// </summary>
    public static string CardEnchantSafeCustomAmountHint => IsChinese ? "这个附魔支持手填数值，范围 1~99。" : "This enchantment supports custom values from 1 to 99.";

    /// <summary>
    /// 不支持手填数值的提示。
    /// </summary>
    public static string CardEnchantFixedAmountHint => IsChinese ? "这个附魔不走可调数值，当前仅支持默认值。" : "This enchantment does not use a customizable amount and only supports the default value.";

    /// <summary>
    /// 关闭安全模式后可强制手填的风险提示。
    /// </summary>
    public static string CardEnchantUnsafeOverrideHint => IsChinese
        ? "安全模式下不开放手填。关闭下方安全模式后可强制写入，但可能出现描述异常、效果异常或读档丢值。"
        : "Custom input is blocked in safe mode. Disable safe mode below to force a value, but descriptions, effects, or saved values may break.";

    /// <summary>
    /// 已关闭附魔安全模式时的高风险提示。
    /// </summary>
    public static string CardEnchantUnsafeOverrideActiveHint => IsChinese
        ? "你已关闭附魔安全模式：本次会强制写入手填数值，可能出现描述异常、效果异常或读档丢值。"
        : "Safe mode is off. This will force the custom value and may cause bad descriptions, bad effects, or lost values after reload.";

    /// <summary>
    /// 已自动判定为安全的 Mod 附魔提示。
    /// </summary>
    public static string CardEnchantModSafeCustomAmountHint => IsChinese
        ? "已自动判定这个 Mod 附魔由数值层数驱动，可安全手填 1~99。"
        : "This mod enchantment was detected as amount-driven and can safely use custom values from 1 to 99.";

    /// <summary>
    /// 检测到额外存档字段时的 Mod 附魔提示。
    /// </summary>
    public static string CardEnchantModSavedPropertyHint => IsChinese
        ? "检测到这个 Mod 附魔带有额外存档字段。安全模式下不开放手填；如执意使用，可关闭安全模式后强制写入。"
        : "This mod enchantment has extra saved fields. Safe mode blocks custom input; disable it only if you want to force a value.";

    /// <summary>
    /// 单张卡片附魔成功提示，{0} 是卡名，{1} 是附魔名。
    /// </summary>
    public static string CardEnchantSuccessNamed => IsChinese ? "{0} 已获得附魔：{1}。" : "{0} enchanted with {1}.";

    /// <summary>
    /// 多人模式单张卡片附魔同步动作已提交提示，{0} 是卡名，{1} 是附魔名。
    /// </summary>
    public static string CardEnchantSubmittedNamed => IsChinese ? "{0} 已提交同步附魔：{1}。" : "{0} enchant sync submitted: {1}.";

    /// <summary>
    /// 单张卡片附魔跳过提示，{0} 是卡名。
    /// </summary>
    public static string CardEnchantSkippedNamed => IsChinese ? "{0} 已跳过，卡片已有附魔或不满足附魔条件。" : "{0} skipped. It is already enchanted or not enchantable.";

    /// <summary>
    /// 单张卡片附魔失败提示，{0} 是卡名。
    /// </summary>
    public static string CardEnchantFailedNamed => IsChinese ? "{0} 附魔失败。" : "{0} enchant failed.";

    /// <summary>
    /// 汇总卡片附魔结果。
    /// </summary>
    public static string BuildCardEnchantSummary(int successCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"卡片附魔成功 {successCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Cards enchanted {successCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 汇总多人模式卡片附魔同步动作提交结果。
    /// </summary>
    public static string BuildCardEnchantSubmitSummary(int submittedCount, int skippedCount, int failedCount)
    {
        return IsChinese
            ? $"卡片附魔已提交同步 {submittedCount}，跳过 {skippedCount}，失败 {failedCount}"
            : $"Card enchant sync submitted {submittedCount}, skipped {skippedCount}, failed {failedCount}";
    }

    /// <summary>
    /// 按当前语言返回中文或英文文本。
    /// </summary>
    public static string Pick(string zh, string en)
    {
        return GmLocalizationService.Pick(zh, en);
    }
}
