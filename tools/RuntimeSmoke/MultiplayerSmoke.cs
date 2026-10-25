using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using TheBookOfAges.TheBookOfAgesCode.GameActions;
using TheBookOfAges.TheBookOfAgesCode.Models;
using TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>通过游戏自带 fastmp 在本机两端验证同步及多人保护，只由隔离探针调用。</summary>
internal static class MultiplayerSmoke
{
    /// <summary>等双方进入房间后由房主提交 GM 动作，两端各自检查相同结果。</summary>
    public static async Task RunAsync()
    {
        SceneTree tree = (SceneTree)Engine.GetMainLoop();
        try
        {
            await Delay(tree, 6);
            SaveManager.Instance.Progress.EnableFtues = false;
            bool ready = false;
            RunState? run = null;
            for (int i = 0; i < 60; i++)
            {
                run = GmRunContextService.GetCurrentRunState();
                if (run?.Players.Count == 2 && run.CurrentRoom != null) break;
                var screen = tree.Root.FindChildren("*", "", true, false).OfType<NCharacterSelectScreen>().FirstOrDefault();
                var lobby = screen == null ? null : typeof(NCharacterSelectScreen).GetField("_lobby", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(screen) as StartRunLobby;
                if (!ready && lobby?.Players.Count == 2)
                {
                    lobby.SetReady(true);
                    ready = true;
                    GD.Print("GM_MP_READY");
                }
                await Delay(tree, 1);
            }
            if (run?.Players.Count != 2 || run.CurrentRoom == null) throw new InvalidOperationException("双端开局超时");
            await Delay(tree, 4);
            await StateRegressionSmoke.CheckMultiplayerAsync(run);
            var host = run.Players.OrderBy(p => p.NetId).First();
            bool isHost = CommandLineHelper.GetValue("fastmp")!.StartsWith("host", StringComparison.Ordinal);
            if (isHost)
            {
                await GmNetworkCommandService.TryEnqueueGameActionAsync(new GmDeckCardGrantAction(host, [new("CARD.STRIKE_IRONCLAD", 1, 1)], false), "联机验证发卡");
                await GmNetworkCommandService.TryEnqueueGameActionAsync(new GmPotionGrantAction(host, ["POTION.FIRE_POTION"], false), "联机验证发药水");
                await GmNetworkCommandService.TryEnqueueConsoleCommandAsync(host, "relic ANCHOR", "联机验证发遗物");
            }
            bool synced = false;
            for (int i = 0; i < 40; i++)
            {
                synced = host.Deck.Cards.Any(c => c.Id.Entry == "STRIKE_IRONCLAD" && c.CurrentUpgradeLevel == 1)
                    && host.PotionSlots.Any(p => p?.Id.Entry == "FIRE_POTION")
                    && host.Relics.Any(r => r.Id.Entry == "ANCHOR");
                if (synced) break;
                await Delay(tree, 1);
            }
            if (!synced) throw new InvalidOperationException("GM 同步动作未在本端全部生效");
            var client = run.Players.First(p => p.NetId != host.NetId);
            if (!isHost)
            {
                await GmNetworkCommandService.TryEnqueueGameActionAsync(new GmDeckCardGrantAction(client, [new("CARD.STRIKE_IRONCLAD", 2, 1)], false), "客户端回归发卡");
                await GmNetworkCommandService.TryEnqueueGameActionAsync(new GmPotionGrantAction(client, ["POTION.FIRE_POTION"], false), "客户端回归发药水");
            }
            await WaitForAsync(tree, () => client.Deck.Cards.Count(c => c.Id.Entry == "STRIKE_IRONCLAD" && c.CurrentUpgradeLevel == 1) == 2
                && client.PotionSlots.Any(p => p?.Id.Entry == "FIRE_POTION"), "客户端发卡及药水同步");
            var removalCards = run.Players.SelectMany(p => p.Deck.Cards)
                .Where(c => c.Id.Entry == "STRIKE_IRONCLAD" && c.CurrentUpgradeLevel == 1).ToList();
            var local = GmRunContextService.GetCurrentPlayer()!;
            var indexes = local.Deck.Cards.Select((card, index) => (card, index))
                .Where(item => removalCards.Contains(item.card)).Select(item => (uint)item.index).ToList();
            await GmNetworkCommandService.TryEnqueueGameActionAsync(new GmDeckCardRemoveAction(local, indexes, false), "双端原生删牌回归");
            await WaitForAsync(tree, () => removalCards.All(c => c.HasBeenRemovedFromState && c.Pile == null), "双端删牌完整移除");
            if (host.Deck.Cards.Count != 10 || client.Deck.Cards.Count != 10) throw new InvalidOperationException("删牌后数量不一致");
            if (!isHost)
            {
                await GmNetworkCommandService.TryEnqueueGameActionAsync(new GmDeckCardUpgradeAction(client, [0]), "客户端升级回归");
            }
            await WaitForAsync(tree, () => client.Deck.Cards[0].CurrentUpgradeLevel == 1, "客户端升级同步");
            typeof(ChronicleService).GetProperty(nameof(ChronicleService.HandLimitOverride))!.SetValue(null, 20);
            typeof(ChronicleService).GetProperty(nameof(ChronicleService.PlayerDamagePercent))!.SetValue(null, 200);
            bool guarded = GmRunContextService.IsMultiplayer()
                && !GmRunContextService.GetFeatureAvailability(GmFeature.Chronicle).IsAllowed
                && CardPile.MaxCardsInHand == 10
                && BaseLib.Patches.Hooks.MaxHandSizePatch.GetMaxHandSize(host, 10) == 10
                && ChronicleService.ScaleDamage(5, null, host.Creature) == 5;
            if (!guarded) throw new InvalidOperationException("多人状态保护失败");
            GD.Print($"GM_MP_PASSED role={(isHost ? "host" : "client")} host={host.NetId} deck={host.Deck.Cards.Count} potions={host.PotionSlots.Count(p => p != null)} relics={host.Relics.Count}");
            await Delay(tree, 8);
            tree.Quit();
        }
        catch (Exception ex)
        {
            GD.PrintErr("GM_MP_FAILED " + ex);
            tree.Quit(1);
        }
    }

    /// <summary>等待真实网络动作在本端生效，超时即失败，避免只检查提交返回值。</summary>
    private static async Task WaitForAsync(SceneTree tree, Func<bool> condition, string name)
    {
        for (int i = 0; i < 30; i++)
        {
            if (condition()) { GD.Print("GM_MP_CHECK " + name); return; }
            await Delay(tree, 1);
        }
        throw new InvalidOperationException("联机回归超时：" + name);
    }

    /// <summary>使用游戏主线程计时器等待，不在后台线程操作运行时状态。</summary>
    private static async Task Delay(SceneTree tree, double seconds)
    {
        await tree.ToSignal(tree.CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
    }
}
