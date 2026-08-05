using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode.GameActions;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.UI;

namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// 能力服务，负责读取能力目录并调用游戏内部命令执行能力增减与清除。
/// </summary>
public sealed class PowerService
{
    private const string AllFilterKey = "all";
    private const string SourceBaseGameKey = "base-game";
    private const string SourceModsKey = "mods";
    private const string TypeBuffKey = "buff";
    private const string TypeDebuffKey = "debuff";
    private const string TypeNeutralKey = "neutral";

    /// <summary>
    /// 从游戏当前 ModelDb 实时读取所有能力，并转换成页面展示数据。
    /// </summary>
    public IReadOnlyList<PowerDisplayInfo> GetPowers(string keyword, string typeFilterKey, string sourceFilterKey)
    {
        string normalizedKeyword = Normalize(keyword);
        return ModelDb.AllPowers
            .Where(power => power != null)
            .Select(TryBuildDisplayInfo)
            .OfType<PowerDisplayInfo>()
            .Where(info => MatchesKeyword(info, normalizedKeyword))
            .Where(info => MatchesType(info.TypeKey, typeFilterKey))
            .Where(info => MatchesSource(info.SourceKey, sourceFilterKey))
            .OrderBy(info => GetSourceOrder(info.SourceKey))
            .ThenBy(info => GetTypeOrder(info.TypeKey))
            .ThenBy(info => info.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 获取能力来源筛选项。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetSourceOptions()
    {
        return [
            new GmFilterOption(AllFilterKey, GmUiText.Pick("全部来源", "All Sources")),
            new GmFilterOption(SourceBaseGameKey, GmUiText.Pick("游戏本体", "Base Game")),
            new GmFilterOption(SourceModsKey, "Mods"),
        ];
    }

    /// <summary>
    /// 获取能力类型筛选项（增益/减益/中立）。
    /// </summary>
    public IReadOnlyList<GmFilterOption> GetTypeOptions()
    {
        return [
            new GmFilterOption(AllFilterKey, GmUiText.PowerTypeAll),
            new GmFilterOption(TypeBuffKey, GmUiText.PowerTypeBuff),
            new GmFilterOption(TypeDebuffKey, GmUiText.PowerTypeDebuff),
            new GmFilterOption(TypeNeutralKey, GmUiText.PowerTypeNeutral),
        ];
    }

    /// <summary>
    /// 判断当前是否可以操作玩家能力。
    /// </summary>
    public bool CanOperatePlayer()
    {
        return GmRunContextService.GetFeatureAvailability(GmFeature.ApplyPower).IsAllowed
            && GetCurrentPlayer()?.Creature != null;
    }

    /// <summary>
    /// 判断当前是否可以操作怪物能力。
    /// </summary>
    public bool CanOperateMonsters()
    {
        return GmRunContextService.GetFeatureAvailability(GmFeature.ApplyPower).IsAllowed
            && GetAliveEnemies().Count > 0;
    }

    /// <summary>
    /// 获取当前玩家作为能力操作目标。
    /// </summary>
    public bool TryGetPlayerTarget(out CreatureTargetInfo? target)
    {
        target = null;
        Player? player = GetCurrentPlayer();
        if (player?.Creature == null)
        {
            return false;
        }

        target = new CreatureTargetInfo
        {
            Id = "player",
            Name = GmUiText.Pick("玩家", "Player"),
            Creature = player.Creature,
        };
        return true;
    }

    /// <summary>
    /// 获取当前战斗中所有存活怪物，供 UI 多选目标使用。
    /// </summary>
    public IReadOnlyList<CreatureTargetInfo> GetMonsterTargetsInCombat()
    {
        List<Creature> enemies = GetAliveEnemies();
        List<CreatureTargetInfo> targets = [];
        for (int i = 0; i < enemies.Count; i++)
        {
            Creature enemy = enemies[i];
            targets.Add(new CreatureTargetInfo
            {
                Id = GetCreatureId(enemy),
                Name = BuildMonsterTargetName(enemy, i),
                Creature = enemy,
            });
        }

        return targets;
    }

    /// <summary>
    /// 批量对目标增减能力层数。
    /// </summary>
    public async Task<PowerApplyResult> ApplyPowersAsync(IEnumerable<PowerSelectionInfo> selectedPowers, IEnumerable<CreatureTargetInfo> targets)
    {
        PowerApplyResult result = new();
        List<PowerSelectionInfo> selections = selectedPowers.ToList();
        List<CreatureTargetInfo> targetList = targets.ToList();
        GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(GmFeature.ApplyPower);
        if (!availability.IsAllowed)
        {
            result.FailedCount += Math.Max(1, selections.Count) * Math.Max(1, targetList.Count);
            result.Messages.Add(availability.Reason);
            MainFile.Logger.Info($"能力赋予已被多人安全闸门阻止：能力数量={selections.Count}，目标数量={targetList.Count}，原因={availability.Reason}。");
            return result;
        }

        if (selections.Count == 0)
        {
            result.SkippedCount++;
            result.Messages.Add(GmUiText.PowerSelectFirst);
            return result;
        }

        if (targetList.Count == 0)
        {
            result.FailedCount++;
            result.Messages.Add(GmUiText.PowerTargetUnavailable);
            return result;
        }

        if (GmRunContextService.IsMultiplayer())
        {
            return await SubmitPowerApplyActionAsync(selections, targetList);
        }

        foreach (CreatureTargetInfo target in targetList)
        {
            foreach (PowerSelectionInfo selection in selections)
            {
                if (selection.AmountDelta == 0)
                {
                    result.SkippedCount++;
                    continue;
                }

                bool success = await TryApplyPowerDeltaAsync(target.Creature, selection.Info, selection.AmountDelta);
                if (success)
                {
                    result.SuccessCount++;
                    result.Messages.Add(FormatPowerApplyMessage(target.Name, selection.Info.Name, selection.AmountDelta));
                }
                else
                {
                    result.FailedCount++;
                    result.Messages.Add(FormatPowerApplyFailedMessage(target.Name, selection.Info.Name, selection.AmountDelta));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 一键清空指定目标身上的全部能力。对应 UI 上的【清除能力】模式。
    /// 不需要选择能力，对每个目标遍历其当前所有能力逐个移除。
    /// </summary>
    public async Task<PowerApplyResult> WipeAllPowersAsync(IEnumerable<CreatureTargetInfo> targets)
    {
        PowerApplyResult result = new();
        List<CreatureTargetInfo> targetList = targets.ToList();
        GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(GmFeature.ApplyPower);
        if (!availability.IsAllowed)
        {
            result.FailedCount += Math.Max(1, targetList.Count);
            result.Messages.Add(availability.Reason);
            MainFile.Logger.Info($"能力清空已被多人安全闸门阻止：目标数量={targetList.Count}，原因={availability.Reason}。");
            return result;
        }

        if (targetList.Count == 0)
        {
            result.FailedCount++;
            result.Messages.Add(GmUiText.PowerTargetUnavailable);
            return result;
        }

        if (GmRunContextService.IsMultiplayer())
        {
            return await SubmitPowerWipeActionAsync(targetList);
        }

        foreach (CreatureTargetInfo target in targetList)
        {
            // 拷贝一份再遍历，避免移除过程中底层 _powers 集合被修改导致迭代异常。
            List<PowerModel> powersSnapshot = target.Creature.Powers.ToList();
            if (powersSnapshot.Count == 0)
            {
                result.SkippedCount++;
                result.Messages.Add(FormatPowerWipeSkippedMessage(target.Name));
                continue;
            }

            foreach (PowerModel power in powersSnapshot)
            {
                string powerName = SafeText(GetLocalizedText(power.Title), power.Id.ToString());
                bool success = await TryRemovePowerAsync(power, target.Name, powerName);
                if (success)
                {
                    result.SuccessCount++;
                    result.Messages.Add(FormatPowerClearMessage(target.Name, powerName));
                }
                else
                {
                    result.FailedCount++;
                    result.Messages.Add(FormatPowerClearFailedMessage(target.Name, powerName));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 批量从目标上清除已选择的能力。
    /// </summary>
    public async Task<PowerApplyResult> ClearPowersAsync(IEnumerable<PowerDisplayInfo> selectedPowers, IEnumerable<CreatureTargetInfo> targets)
    {
        PowerApplyResult result = new();
        List<PowerDisplayInfo> powers = selectedPowers.ToList();
        List<CreatureTargetInfo> targetList = targets.ToList();
        GmFeatureAvailability availability = GmRunContextService.GetFeatureAvailability(GmFeature.ApplyPower);
        if (!availability.IsAllowed)
        {
            result.FailedCount += Math.Max(1, powers.Count) * Math.Max(1, targetList.Count);
            result.Messages.Add(availability.Reason);
            MainFile.Logger.Info($"能力删除已被多人安全闸门阻止：能力数量={powers.Count}，目标数量={targetList.Count}，原因={availability.Reason}。");
            return result;
        }

        if (powers.Count == 0)
        {
            result.SkippedCount++;
            result.Messages.Add(GmUiText.PowerSelectFirst);
            return result;
        }

        if (targetList.Count == 0)
        {
            result.FailedCount++;
            result.Messages.Add(GmUiText.PowerTargetUnavailable);
            return result;
        }

        if (GmRunContextService.IsMultiplayer())
        {
            return await SubmitPowerClearActionAsync(powers, targetList);
        }

        foreach (CreatureTargetInfo target in targetList)
        {
            foreach (PowerDisplayInfo power in powers)
            {
                PowerModel? existing = target.Creature.GetPowerById(power.Id);
                if (existing == null)
                {
                    result.SkippedCount++;
                    result.Messages.Add(FormatPowerClearSkippedMessage(target.Name, power.Name));
                    continue;
                }

                bool success = await TryRemovePowerAsync(existing, target.Name, power.Name);
                if (success)
                {
                    result.SuccessCount++;
                    result.Messages.Add(FormatPowerClearMessage(target.Name, power.Name));
                }
                else
                {
                    result.FailedCount++;
                    result.Messages.Add(FormatPowerClearFailedMessage(target.Name, power.Name));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 多人模式下提交能力赋予同步动作，不在服务层直接改本机目标能力。
    /// </summary>
    /// <param name="selections">要应用的能力选择。</param>
    /// <param name="targetList">要操作的目标列表。</param>
    private static async Task<PowerApplyResult> SubmitPowerApplyActionAsync(IReadOnlyList<PowerSelectionInfo> selections, IReadOnlyList<CreatureTargetInfo> targetList)
    {
        PowerApplyResult result = new();
        List<GmPowerApplyEntry> entries = [];
        foreach (PowerSelectionInfo selection in selections)
        {
            if (selection.AmountDelta == 0)
            {
                result.SkippedCount += targetList.Count;
                continue;
            }

            entries.Add(new GmPowerApplyEntry(selection.Info.IdText, selection.AmountDelta));
        }

        if (entries.Count == 0)
        {
            return result;
        }

        List<GmCreatureTargetRef> targetRefs = BuildTargetRefs(targetList, result, entries.Count);
        if (targetRefs.Count == 0)
        {
            return result;
        }

        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            result.FailedCount += targetRefs.Count * entries.Count;
            result.Messages.Add(GmUiText.MultiplayerLocalPlayerMissing);
            MainFile.Logger.Info($"能力赋予多人同步提交失败：目标数量={targetRefs.Count}，能力数量={entries.Count}，原因=当前玩家为空。");
            return result;
        }

        bool submitted = await GmNetworkCommandService.TryEnqueueGameActionAsync(new GmPowerApplyAction(player, targetRefs, entries), "能力赋予");
        int operationCount = targetRefs.Count * entries.Count;
        if (submitted)
        {
            result.SuccessCount += operationCount;
            result.Messages.Add(GmUiText.PowerApplySubmitted(operationCount));
        }
        else
        {
            result.FailedCount += operationCount;
            result.Messages.Add(GmUiText.PowerApplySubmitFailed(operationCount));
        }

        MainFile.Logger.Info($"能力赋予多人同步提交结束：提交成功={submitted}，目标数量={targetRefs.Count}，能力数量={entries.Count}，操作数={operationCount}。");
        return result;
    }

    /// <summary>
    /// 多人模式下提交指定能力删除同步动作，不在服务层直接移除能力。
    /// </summary>
    /// <param name="powers">要删除的能力列表。</param>
    /// <param name="targetList">要操作的目标列表。</param>
    private static async Task<PowerApplyResult> SubmitPowerClearActionAsync(IReadOnlyList<PowerDisplayInfo> powers, IReadOnlyList<CreatureTargetInfo> targetList)
    {
        PowerApplyResult result = new();
        List<string> powerIds = powers.Select(power => power.IdText).ToList();
        List<GmCreatureTargetRef> targetRefs = BuildTargetRefs(targetList, result, powerIds.Count);
        if (targetRefs.Count == 0)
        {
            return result;
        }

        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            result.FailedCount += targetRefs.Count * powerIds.Count;
            result.Messages.Add(GmUiText.MultiplayerLocalPlayerMissing);
            MainFile.Logger.Info($"能力删除多人同步提交失败：目标数量={targetRefs.Count}，能力数量={powerIds.Count}，原因=当前玩家为空。");
            return result;
        }

        bool submitted = await GmNetworkCommandService.TryEnqueueGameActionAsync(new GmPowerRemoveAction(player, targetRefs, powerIds, GmPowerRemoveMode.SelectedPowers), "能力删除");
        int operationCount = targetRefs.Count * powerIds.Count;
        if (submitted)
        {
            result.SuccessCount += operationCount;
            result.Messages.Add(GmUiText.PowerClearSubmitted(operationCount));
        }
        else
        {
            result.FailedCount += operationCount;
            result.Messages.Add(GmUiText.PowerClearSubmitFailed(operationCount));
        }

        MainFile.Logger.Info($"能力删除多人同步提交结束：提交成功={submitted}，目标数量={targetRefs.Count}，能力数量={powerIds.Count}，操作数={operationCount}。");
        return result;
    }

    /// <summary>
    /// 多人模式下提交清空能力同步动作，不在服务层直接遍历移除目标能力。
    /// </summary>
    /// <param name="targetList">要操作的目标列表。</param>
    private static async Task<PowerApplyResult> SubmitPowerWipeActionAsync(IReadOnlyList<CreatureTargetInfo> targetList)
    {
        PowerApplyResult result = new();
        List<GmCreatureTargetRef> targetRefs = BuildTargetRefs(targetList, result, 1);
        if (targetRefs.Count == 0)
        {
            return result;
        }

        Player? player = GetCurrentPlayer();
        if (player == null)
        {
            result.FailedCount += targetRefs.Count;
            result.Messages.Add(GmUiText.MultiplayerLocalPlayerMissing);
            MainFile.Logger.Info($"能力清空多人同步提交失败：目标数量={targetRefs.Count}，原因=当前玩家为空。");
            return result;
        }

        bool submitted = await GmNetworkCommandService.TryEnqueueGameActionAsync(new GmPowerRemoveAction(player, targetRefs, [], GmPowerRemoveMode.AllCurrentPowers), "能力清空");
        int operationCount = targetRefs.Count;
        if (submitted)
        {
            result.SuccessCount += operationCount;
            result.Messages.Add(GmUiText.PowerWipeSubmitted(operationCount));
        }
        else
        {
            result.FailedCount += operationCount;
            result.Messages.Add(GmUiText.PowerWipeSubmitFailed(operationCount));
        }

        MainFile.Logger.Info($"能力清空多人同步提交结束：提交成功={submitted}，目标数量={targetRefs.Count}。");
        return result;
    }

    /// <summary>
    /// 把 UI 目标转换成网络动作目标引用；无法稳定定位的目标会计入失败。
    /// </summary>
    /// <param name="targets">UI 选择的目标列表。</param>
    /// <param name="result">要累加失败信息的结果对象。</param>
    /// <param name="failedUnitsPerTarget">单个目标失效时折算的失败操作数。</param>
    private static List<GmCreatureTargetRef> BuildTargetRefs(IReadOnlyList<CreatureTargetInfo> targets, PowerApplyResult result, int failedUnitsPerTarget)
    {
        List<GmCreatureTargetRef> refs = [];
        foreach (CreatureTargetInfo target in targets)
        {
            GmCreatureTargetRef targetRef = GmCreatureTargetRef.FromTarget(target);
            if (!targetRef.IsValid)
            {
                result.FailedCount += Math.Max(1, failedUnitsPerTarget);
                result.Messages.Add(GmUiText.PowerTargetUnavailable);
                MainFile.Logger.Info($"能力多人同步跳过无法稳定定位的目标：目标={target.Name}，Id={target.Id}。");
                continue;
            }

            refs.Add(targetRef);
        }

        return refs;
    }

    /// <summary>
    /// 尝试对单个目标应用能力层数变化。
    /// 直接走兜底路径：已有能力直接设值，没有则直接挂载。
    /// （命令行路径 PowerCmd.Apply / ModifyAmount 存在 NRE，已屏蔽。）
    /// </summary>
    private static Task<bool> TryApplyPowerDeltaAsync(Creature target, PowerDisplayInfo powerInfo, int delta)
    {
        try
        {
            if (delta == 0)
            {
                return Task.FromResult(true);
            }

            PowerModel? existing = target.GetPowerById(powerInfo.Id);
            if (existing != null)
            {
                // 目标身上已有该能力，直接设值。
                bool success = TrySetPowerAmountDirect(existing, existing.Amount + delta);
                return Task.FromResult(success);
            }

            if (delta < 0 && !powerInfo.AllowNegative)
            {
                MainFile.Logger.Info($"能力变更跳过：目标={SafeText(target.Name, target.ModelId.ToString())}，能力={powerInfo.Name} 不允许负数层数，层数变化={delta:+#;-#;0}。");
                return Task.FromResult(false);
            }

            // 目标身上没有该能力，直接挂载。
            bool result = TryApplyNewPowerDirect(target, powerInfo, delta);
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"能力变更失败：目标={SafeText(target.Name, target.ModelId.ToString())}，能力={powerInfo.Name}，层数变化={delta:+#;-#;0}，{FormatException(ex)}");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// 直接设置能力层数作为命令失败兜底路径。
    /// </summary>
    private static bool TrySetPowerAmountDirect(PowerModel power, int targetAmount)
    {
        try
        {
            int before = power.Amount;
            power.SetAmount(targetAmount, false);
            return power.Amount != before;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"能力直接设值兜底失败：能力={SafeText(GetLocalizedText(power.Title), power.Id.ToString())}，目标层数={targetAmount}，{FormatException(ex)}");
            return false;
        }
    }

    /// <summary>
    /// 命令路径失败时绕过游戏 hook 链直接给目标挂载新能力。
    /// 注意：此路径不会触发 BeforePowerAmountChanged / ModifyPowerAmount 等钩子，
    /// 用作 GM 调试兜底，确保即使部分游戏 hook 异常也能把能力贴上去。
    /// </summary>
    private static bool TryApplyNewPowerDirect(Creature target, PowerDisplayInfo powerInfo, int delta)
    {
        try
        {
            // PowerCmd.Apply 部分成功的情况：能力可能已经被加到 _powers 列表，再走 ApplyInternal 会冲突。
            PowerModel? alreadyApplied = target.GetPowerById(powerInfo.Id);
            if (alreadyApplied != null)
            {
                int targetAmount = alreadyApplied.Amount == 0 ? delta : alreadyApplied.Amount;
                return TrySetPowerAmountDirect(alreadyApplied, targetAmount);
            }

            // 重新克隆一份 mutable 实例避免上一次 Apply 留下的中间状态污染。
            PowerModel freshPower = powerInfo.Power.ToMutable(0);
            freshPower.ApplyInternal(target, delta, false);
            PowerModel? after = target.GetPowerById(powerInfo.Id);
            if (after == null)
            {
                MainFile.Logger.Info($"能力直接挂载兜底未生效：目标={SafeText(target.Name, target.ModelId.ToString())}，能力={powerInfo.Name}，层数变化={delta:+#;-#;0}。");
                return false;
            }

            return ValidatePowerDeltaApplied(after, delta);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"能力直接挂载兜底失败：目标={SafeText(target.Name, target.ModelId.ToString())}，能力={powerInfo.Name}，层数变化={delta:+#;-#;0}，{FormatException(ex)}");
            return false;
        }
    }

    /// <summary>
    /// 统一格式化异常文本，附带类型、栈和 InnerException，方便排查 NRE 这类信息缺失的报错。
    /// </summary>
    private static string FormatException(Exception ex)
    {
        string inner = ex.InnerException == null
            ? string.Empty
            : $"，内部异常={ex.InnerException.GetType().Name}: {ex.InnerException.Message}";
        string stack = ex.StackTrace ?? "<no stack>";
        return $"异常={ex.GetType().Name}: {ex.Message}{inner}，堆栈={stack}";
    }

    /// <summary>
    /// 检查本次能力层数变化是否生效。
    /// </summary>
    private static bool ValidatePowerDeltaApplied(PowerModel power, int delta)
    {
        if (delta > 0)
        {
            return power.Amount > 0;
        }

        if (delta < 0)
        {
            return power.Amount < 0 || power.Amount == 0;
        }

        return true;
    }

    /// <summary>
    /// 尝试移除单个能力。
    /// </summary>
    private static async Task<bool> TryRemovePowerAsync(PowerModel power, string targetName, string powerName)
    {
        try
        {
            await PowerCmd.Remove(power);
            return true;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"能力清除失败：目标={targetName}，能力={powerName}，异常={ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 获取当前玩家；没有局内状态时返回空。
    /// </summary>
    private static Player? GetCurrentPlayer()
    {
        return GmRunContextService.GetCurrentPlayer();
    }

    /// <summary>
    /// 获取当前战斗中存活的怪物列表。
    /// </summary>
    private static List<Creature> GetAliveEnemies()
    {
        CombatManager? manager = CombatManager.Instance;
        if (manager == null || !manager.IsInProgress || manager.IsOverOrEnding)
        {
            return [];
        }

        CombatState? state = manager.DebugOnlyGetState();
        return state?.Enemies?.Where(enemy => enemy != null && enemy.IsAlive).ToList() ?? [];
    }

    /// <summary>
    /// 安全转换能力展示数据；单个第三方能力字段异常时不影响整个能力图鉴。
    /// </summary>
    private static PowerDisplayInfo? TryBuildDisplayInfo(PowerModel power)
    {
        try
        {
            return BuildDisplayInfo(power);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"能力展示数据构建失败：ID={SafeGetPowerIdText(power)}，类型={power.GetType().FullName}，{FormatException(ex)}。已跳过该能力。");
            return null;
        }
    }

    /// <summary>
    /// 把游戏能力模型转换成页面需要的简单字段。
    /// </summary>
    private static PowerDisplayInfo BuildDisplayInfo(PowerModel power)
    {
        string idText = power.Id.ToString();
        string? assemblyName = SafeReadValue<string?>(() => power.GetType().Assembly.GetName().Name, null, idText, "AssemblyName");
        string sourceKey = ToSourceKey(assemblyName);
        string typeKey = SafeReadValue(() => power.Type.ToString().ToLowerInvariant(), TypeNeutralKey, idText, "Type");
        string descriptionFallback = GmUiText.Pick("暂无描述或能力描述存在问题无法正常展示", "No description or the power description could not be displayed.");
        return new PowerDisplayInfo
        {
            Id = power.Id,
            IdText = idText,
            Name = SafeText(TryGetLocalizedText(() => power.Title, idText, "Title"), idText),
            Description = SafeText(TryGetLocalizedRawText(() => power.Description, idText, "Description"), descriptionFallback),
            TypeKey = typeKey,
            TypeText = ToTypeText(typeKey),
            SourceKey = sourceKey,
            Source = ToSourceText(sourceKey, assemblyName),
            AllowNegative = SafeReadValue(() => power.AllowNegative, false, idText, "AllowNegative"),
            Icon = SafeReadValue<Godot.Texture2D?>(() => power.Icon, null, idText, "Icon"),
            Power = power,
        };
    }

    /// <summary>
    /// 判断能力名称、描述或 ID 是否匹配搜索关键字。
    /// </summary>
    private static bool MatchesKeyword(PowerDisplayInfo info, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return Normalize(info.Name).Contains(keyword) ||
               Normalize(info.Description).Contains(keyword) ||
               Normalize(info.IdText).Contains(keyword);
    }

    /// <summary>
    /// 判断来源筛选是否命中，本体、当前 Mod 和其他 Mod 分开处理。
    /// </summary>
    private static bool MatchesSource(string valueKey, string filterKey)
    {
        if (string.IsNullOrWhiteSpace(filterKey) ||
            filterKey.Equals(AllFilterKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (filterKey.Equals(SourceModsKey, StringComparison.OrdinalIgnoreCase))
        {
            return !valueKey.Equals(SourceBaseGameKey, StringComparison.OrdinalIgnoreCase);
        }

        return valueKey.Equals(filterKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断能力类型筛选是否命中（增益/减益/中立）。
    /// </summary>
    private static bool MatchesType(string typeKey, string filterKey)
    {
        if (string.IsNullOrWhiteSpace(filterKey) ||
            filterKey.Equals(AllFilterKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return typeKey.Equals(filterKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 转换能力类型文本。
    /// </summary>
    private static string ToTypeText(string typeKey)
    {
        return typeKey switch
        {
            "buff" => GmUiText.Pick("增益", "Buff"),
            "debuff" => GmUiText.Pick("减益", "Debuff"),
            "neutral" => GmUiText.Pick("中立", "Neutral"),
            _ => typeKey,
        };
    }

    /// <summary>
    /// 能力类型排序权重，让增益能力排在减益能力前面。
    /// </summary>
    private static int GetTypeOrder(string typeKey)
    {
        return typeKey switch
        {
            "buff" => 0,
            "neutral" => 1,
            "debuff" => 2,
            _ => 9,
        };
    }

    /// <summary>
    /// 转换来源键值，本体、当前 Mod、其他 Mod 使用统一格式。
    /// </summary>
    private static string ToSourceKey(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName) || assemblyName.Equals("sts2", StringComparison.OrdinalIgnoreCase))
        {
            return SourceBaseGameKey;
        }

        return $"mod:{assemblyName}";
    }

    /// <summary>
    /// 转换来源文本，本体、当前 Mod、其他 Mod 分开显示。
    /// </summary>
    private static string ToSourceText(string sourceKey, string? assemblyName)
    {
        if (sourceKey.Equals(SourceBaseGameKey, StringComparison.OrdinalIgnoreCase))
        {
            return GmUiText.Pick("游戏本体", "Base Game");
        }

        if (string.Equals(assemblyName, "TheBookOfAges", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(assemblyName, MainFile.ModId, StringComparison.OrdinalIgnoreCase))
        {
            return "TheBookOfAges / GM Console";
        }

        return GmUiText.Pick($"{assemblyName} 模组", $"{assemblyName} Mod");
    }

    /// <summary>
    /// 来源排序权重，让本体来源排在 Mod 来源前面。
    /// </summary>
    private static int GetSourceOrder(string sourceKey)
    {
        if (sourceKey.Equals(SourceBaseGameKey, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (sourceKey.Equals($"mod:{MainFile.ModId}", StringComparison.OrdinalIgnoreCase) ||
            sourceKey.Equals("mod:TheBookOfAges", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    /// <summary>
    /// 构建怪物目标展示名，附带序号和当前生命，方便多选时快速区分。
    /// </summary>
    private static string BuildMonsterTargetName(Creature enemy, int index)
    {
        string name = SafeText(enemy.Name, enemy.ModelId.ToString());
        return GmUiText.Pick($"{index + 1}. {name}（HP {enemy.CurrentHp}）", $"{index + 1}. {name} (HP {enemy.CurrentHp})");
    }

    /// <summary>
    /// 获取生物稳定 ID；有 CombatId 时优先使用，保证同一场战斗内唯一。
    /// </summary>
    private static string GetCreatureId(Creature creature)
    {
        return creature.CombatId?.ToString() ?? creature.GetHashCode().ToString();
    }

    /// <summary>
    /// 能力发放成功明细文案。
    /// </summary>
    private static string FormatPowerApplyMessage(string targetName, string powerName, int delta)
    {
        string deltaText = delta.ToString("+#;-#;0");
        return GmUiText.Pick($"{targetName}：{powerName} 层数变化 {deltaText} 成功。", $"{targetName}: {powerName} delta {deltaText} applied.");
    }

    /// <summary>
    /// 能力发放失败明细文案。
    /// </summary>
    private static string FormatPowerApplyFailedMessage(string targetName, string powerName, int delta)
    {
        string deltaText = delta.ToString("+#;-#;0");
        return GmUiText.Pick($"{targetName}：{powerName} 层数变化 {deltaText} 失败。", $"{targetName}: {powerName} delta {deltaText} failed.");
    }

    /// <summary>
    /// 能力清除成功明细文案。
    /// </summary>
    private static string FormatPowerClearMessage(string targetName, string powerName)
    {
        return GmUiText.Pick($"{targetName}：已清除 {powerName}。", $"{targetName}: removed {powerName}.");
    }

    /// <summary>
    /// 能力清除失败明细文案。
    /// </summary>
    private static string FormatPowerClearFailedMessage(string targetName, string powerName)
    {
        return GmUiText.Pick($"{targetName}：清除 {powerName} 失败。", $"{targetName}: failed to remove {powerName}.");
    }

    /// <summary>
    /// 能力不存在时的跳过明细文案。
    /// </summary>
    private static string FormatPowerClearSkippedMessage(string targetName, string powerName)
    {
        return GmUiText.Pick($"{targetName}：未找到 {powerName}，已跳过。", $"{targetName}: {powerName} not found, skipped.");
    }

    /// <summary>
    /// 一键清除模式下，目标本身就没有任何能力时的跳过文案。
    /// </summary>
    private static string FormatPowerWipeSkippedMessage(string targetName)
    {
        return GmUiText.Pick($"{targetName}：身上没有能力，已跳过。", $"{targetName}: no powers to clear, skipped.");
    }

    /// <summary>
    /// 规范化搜索文本，避免大小写影响检索。
    /// </summary>
    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// 给空文本提供兜底文案，避免页面出现空白。
    /// </summary>
    private static string SafeText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    /// <summary>
    /// 安全读取能力 ID，避免日志记录时再次触发异常。
    /// </summary>
    private static string SafeGetPowerIdText(PowerModel power)
    {
        try
        {
            return power.Id.ToString();
        }
        catch
        {
            return power.GetType().FullName ?? power.GetType().Name;
        }
    }

    /// <summary>
    /// 安全读取能力普通字段；第三方能力 getter 异常时使用兜底值。
    /// </summary>
    private static T SafeReadValue<T>(Func<T> valueProvider, T fallback, string idText, string fieldName)
    {
        try
        {
            return valueProvider();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"能力字段读取失败：ID={idText}，字段={fieldName}，异常={ex.Message}。已使用兜底值。");
            return fallback;
        }
    }

    /// <summary>
    /// 解析游戏本地化文本，按玩家当前语言返回可读内容。
    /// </summary>
    private static string? GetLocalizedText(MegaCrit.Sts2.Core.Localization.LocString? locString)
    {
        return GmLocalizationService.ResolveText(locString, "解析能力本地化文本失败 / Failed to resolve power localized text");
    }

    /// <summary>
    /// 安全读取能力本地化文本；避免第三方能力属性 getter 异常拖垮能力图鉴。
    /// </summary>
    private static string? TryGetLocalizedText(Func<MegaCrit.Sts2.Core.Localization.LocString?> locStringProvider, string idText, string fieldName)
    {
        try
        {
            return GetLocalizedText(locStringProvider());
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"能力文本读取失败：ID={idText}，字段={fieldName}，异常={ex.Message}。已使用兜底文本。");
            return null;
        }
    }

    /// <summary>
    /// 解析游戏本地化原文，不触发 SmartFormat。
    /// 用于能力描述这类可能包含运行时占位符的文本。
    /// </summary>
    private static string? GetLocalizedRawText(MegaCrit.Sts2.Core.Localization.LocString? locString)
    {
        return GmLocalizationService.ResolveRawText(locString, "解析能力原始本地化文本失败 / Failed to resolve raw power localized text");
    }

    /// <summary>
    /// 安全读取能力本地化原文；描述属性可能依赖运行时对象，需要失败兜底。
    /// </summary>
    private static string? TryGetLocalizedRawText(Func<MegaCrit.Sts2.Core.Localization.LocString?> locStringProvider, string idText, string fieldName)
    {
        try
        {
            return GetLocalizedRawText(locStringProvider());
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"能力原始文本读取失败：ID={idText}，字段={fieldName}，异常={ex.Message}。已使用兜底文本。");
            return null;
        }
    }
}
