using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using TheBookOfAges.TheBookOfAgesCode;
using TheBookOfAges.TheBookOfAgesCode.GameActions;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>配置恢复和网络提交的边界回归，仅由隔离游戏探针调用。</summary>
internal static class StateRegressionSmoke
{
    /// <summary>验证跨配置加载不会留下旧的运行时状态。</summary>
    public static async Task CheckSingleplayerAsync(RunState run, SceneTree tree)
    {
        if (!ChronicleStateRepository.TryGetCurrentFilePath(out string path)) throw new InvalidOperationException("配置路径不可用");
        List<string> failures = [];
        var cards = new CardService();
        var chosen = cards.GetAllCardsForBucket(false, false).First(c => c.Id.Entry == "STRIKE_IRONCLAD");
        await cards.GrantCardsAsync([chosen], false);
        var granted = cards.GetDeckCards("", "All").Last(c => c.Id == chosen.Id);
        await cards.RemoveDeckCardsAsync([granted]);
        // 游戏会保留历史卡片引用；正确的移除标志是 HasBeenRemovedFromState，而非从引用表抹掉。
        Record("删牌清理运行状态", granted.Card.HasBeenRemovedFromState && granted.Card.Pile == null, failures);
        MethodInfo onRunStarted = typeof(MainFile).GetMethod("OnRunStarted", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var test in new (string Name, string? Json)[]
        {
            ("缺失配置", null),
            ("损坏配置", "{broken"),
            ("未来版本配置", "{\"Version\":999,\"HandLimit\":50,\"LockedValues\":{}}"),
            ("空锁定字典", "{\"Version\":1,\"LockedValues\":null}"),
        })
        {
            ChronicleService.Instance.LoadFrom(new ChronicleSavedState { HandLimit = 20, PlayerDamagePercent = 200, AlwaysPlayerTurn = true });
            if (test.Json == null) ChronicleStateRepository.TryDelete(false);
            else
            {
                using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
                file.StoreString(test.Json);
            }
            onRunStarted.Invoke(null, [run]);
            await tree.ToSignal(tree.CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout);
            Record(test.Name, ChronicleService.GetEffectiveHandLimit() == 10 && ChronicleService.PlayerDamagePercent == 100 && !ChronicleService.Instance.AlwaysPlayerTurnEnabled, failures);
        }
        ChronicleService.Instance.ResetToDefaults();
        var state = new ChronicleSavedState { HandLimit = 17, PlayerDamagePercent = 150 };
        Record("有效配置保存", ChronicleStateRepository.TrySave(state), failures);
        Record("有效配置读取", ChronicleStateRepository.TryLoad()?.HandLimit == 17, failures);
        state.HandLimit = 18;
        Record("有效配置覆盖", ChronicleStateRepository.TrySave(state) && ChronicleStateRepository.TryLoad()?.HandLimit == 18, failures);
        if (OperatingSystem.IsWindows())
        {
            string fullPath = ProjectSettings.GlobalizePath(path);
            FileAttributes attributes = File.GetAttributes(fullPath);
            File.SetAttributes(fullPath, attributes | FileAttributes.ReadOnly);
            try
            {
                state.HandLimit = 19;
                Record("替换失败保留旧配置", !ChronicleStateRepository.TrySave(state) && ChronicleStateRepository.TryLoad()?.HandLimit == 18, failures);
            }
            finally { File.SetAttributes(fullPath, attributes); }
        }
        ChronicleStateRepository.TryDelete(false);
        if (failures.Count > 0) throw new InvalidOperationException("配置边界失败：" + string.Join("、", failures));
    }

    /// <summary>验证空输入、错误归属和加载期请求不会进入联机队列。</summary>
    public static async Task CheckMultiplayerAsync(RunState run)
    {
        Player local = GmRunContextService.GetCurrentPlayer() ?? throw new InvalidOperationException("本机玩家不存在");
        Player other = run.Players.First(p => p.NetId != local.NetId);
        List<string> failures = [];
        await ExpectRejected("空玩家", () => GmNetworkCommandService.TryEnqueueConsoleCommandAsync(null!, "gold 0", "空值回归"), failures);
        await ExpectRejected("空动作", () => GmNetworkCommandService.TryEnqueueGameActionAsync(null!, "空值回归"), failures);
        await ExpectRejected("错误归属动作", () => GmNetworkCommandService.TryEnqueueGameActionAsync(new GmDeckCardGrantAction(other, [], false), "归属回归"), failures);
        var net = RunManager.Instance.NetService;
        net.SetGameLoading(true);
        try
        {
            Record("加载期遗物闸门", !GmRunContextService.GetFeatureAvailability(GmFeature.GrantRelic).IsAllowed, failures);
            await ExpectRejected("加载期控制台", () => GmNetworkCommandService.TryEnqueueConsoleCommandAsync(local, "gold 0", "加载期回归"), failures);
            await ExpectRejected("加载期动作", () => GmNetworkCommandService.TryEnqueueGameActionAsync(new GmDeckCardGrantAction(local, [], false), "加载期回归"), failures);
        }
        finally { net.SetGameLoading(false); }
        if (failures.Count > 0) throw new InvalidOperationException("联机边界失败：" + string.Join("、", failures));
    }

    /// <summary>失败请求应返回 false，抛出异常也视为回归失败。</summary>
    private static async Task ExpectRejected(string name, Func<Task<bool>> request, List<string> failures)
    {
        try { Record(name, !await request(), failures); }
        catch (Exception ex) { GD.Print($"GM_BOUNDARY_EXCEPTION {name}: {ex.GetType().Name}"); Record(name, false, failures); }
    }

    /// <summary>记录所有边界的结果后统一失败，方便一次看清多个根因。</summary>
    private static void Record(string name, bool passed, List<string> failures)
    {
        GD.Print($"GM_BOUNDARY {name}: {passed}");
        if (!passed) failures.Add(name);
    }
}
