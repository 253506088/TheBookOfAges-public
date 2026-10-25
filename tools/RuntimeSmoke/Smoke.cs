using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using TheBookOfAges.TheBookOfAgesCode.Config;
using TheBookOfAges.TheBookOfAgesCode.Services;
using TheBookOfAges.TheBookOfAgesCode.UI;

/// <summary>仅在隔离离线测试环境运行的游戏内探针。</summary>
[ModInitializer(nameof(Initialize))]
public static class Smoke
{
    /// <summary>将检查推迟到游戏初始化完成后执行。</summary>
    public static void Initialize()
    {
        if (MegaCrit.Sts2.Core.Platform.Steam.SteamInitializer.Initialized ||
            !OS.GetUserDataDir().Contains("SlayTheSpire2-GmCompat-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("验证探针只允许在关闭 Steam 的 GM 专用隔离数据目录运行。");
        }
        GmModConfig.EnableDiagnosticExport = false;
        Callable.From(() =>
        {
            _ = MegaCrit.Sts2.Core.Helpers.CommandLineHelper.HasArg("fastmp")
                ? MultiplayerSmoke.RunAsync()
                : RunAsync();
        }).CallDeferred();
    }

    /// <summary>创建不保存的新局，检验面板和常用服务真实运行结果。</summary>
    private static async Task RunAsync()
    {
        SceneTree tree = (SceneTree)Engine.GetMainLoop();
        try
        {
            await tree.ToSignal(tree.CreateTimer(8), SceneTreeTimer.SignalName.Timeout);
            MegaCrit.Sts2.Core.Saves.SaveManager.Instance.Progress.EnableFtues = false;
            RunState run = await (NGame.Instance ?? throw new InvalidOperationException("游戏未初始化")).StartNewSingleplayerRun(ModelDb.Character<Ironclad>(), false,
                ModelDb.ActsByIndex.Select(x => x[0]).ToList(), [], "GMCOMPAT111", GameMode.Standard);
            await tree.ToSignal(tree.CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
            await StateRegressionSmoke.CheckSingleplayerAsync(run, tree);
            GmUiManager.TogglePanel();
            var layer = (CanvasLayer?)typeof(GmUiManager).GetField("_layer", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            Check("面板打开", layer?.Visible == true);
            Check("Esc关闭", GmUiManager.HandleInput(new InputEventKey { Keycode = Key.Escape, Pressed = true }) && layer?.Visible == false);
            var switchPage = typeof(GmUiManager).GetMethod("SwitchPage", BindingFlags.Static | BindingFlags.NonPublic)!;
            foreach (var page in Enum.GetValues(switchPage.GetParameters()[0].ParameterType))
            {
                switchPage.Invoke(null, [page]);
                Check("页签" + page, true);
            }
            CardService cards = new();
            var catalog = cards.GetAllCardsForBucket(false, false);
            Check("卡牌目录", catalog.Count > 100);
            var chosen = catalog.First(x => x.IdText.EndsWith("STRIKE_IRONCLAD", StringComparison.Ordinal));
            int before = run.Players[0].Deck.Cards.Count;
            var grant = await cards.GrantCardsAsync([chosen], false);
            Check("发卡", grant.SuccessCount == 1 && run.Players[0].Deck.Cards.Count == before + 1);
            var added = cards.GetDeckCards("", "All").Last(x => x.Id == chosen.Id);
            var upgrade = await cards.UpgradeDeckCardsAsync([added]);
            Check("升级卡", upgrade.SuccessCount == 1 && added.Card.CurrentUpgradeLevel == 1);
            var remove = await cards.RemoveDeckCardsAsync([added]);
            Check("删除卡", remove.SuccessCount == 1 && run.Players[0].Deck.Cards.Count == before);
            RelicService relics = new();
            var relicCatalog = relics.GetRelics("", "all", "all", "all");
            Check("遗物目录", relicCatalog.Count > 100);
            PotionService potions = new();
            var potionCatalog = potions.GetPotions("", "all", "all", "all");
            Check("药水目录", potionCatalog.Count > 30);
            var potion = potionCatalog.First(x => x.IdText.EndsWith("FIRE_POTION", StringComparison.Ordinal));
            Check("发药水", (await potions.GrantPotionsAsync([potion])).SuccessCount == 1);
            var relic = relicCatalog.First(x => x.IdText.EndsWith("ANCHOR", StringComparison.Ordinal));
            Check("发遗物", (await relics.GrantRelicsAsync([relic])).SuccessCount == 1);
            var owned = relics.GetOwnedRelics("", "all", "all", "all").First(x => x.Id == relic.Id);
            Check("删遗物", (await relics.RemoveRelicsAsync([owned])).SuccessCount == 1);
            await ChronicleService.Instance.ApplyValueAsync(ChronicleValueKey.HandLimit, 20);
            Check("原生手牌上限", CardPile.MaxCardsInHand == 20);
            await ChronicleService.Instance.ApplyValueAsync(ChronicleValueKey.HandLimit, 10);
            Check("默认手牌上限", CardPile.MaxCardsInHand == 10);
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster);
            await tree.ToSignal(tree.CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
            var player = run.Players[0];
            var combat = CombatManager.Instance.DebugOnlyGetState() ?? throw new InvalidOperationException("战斗不存在");
            var enemy = combat.Enemies.First();
            int hp = enemy.CurrentHp;
            await ChronicleService.Instance.ApplyValueAsync(ChronicleValueKey.PlayerDamagePercent, 200);
            await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), enemy, 5m, ValueProp.Unpowered | ValueProp.Unblockable, player.Creature);
            Check("单目标伤害倍率只应用一次", hp - enemy.CurrentHp == 10);
            hp = enemy.CurrentHp;
            var damageMethod = TheBookOfAges.TheBookOfAgesCode.Patches.ChronicleDamagePatch.TargetMethods().Single();
            object?[] damageArgs = damageMethod.GetParameters().Select(parameter => parameter.Name switch
            {
                "choiceContext" => (object)new ThrowingPlayerChoiceContext(),
                "targets" => Enumerable.Repeat(enemy, 1),
                "amount" => 2m,
                "props" => ValueProp.Unpowered | ValueProp.Unblockable,
                _ => null,
            }).ToArray();
            await (Task)damageMethod.Invoke(null, damageArgs)!;
            Check("无施法者伤害保留目标阵营", hp - enemy.CurrentHp == 4);
            await ChronicleService.Instance.ApplyValueAsync(ChronicleValueKey.PlayerDamagePercent, 100);
            await ChronicleService.Instance.ApplyValueAsync(ChronicleValueKey.HandLimit, 20);
            int handBefore = player.PlayerCombatState!.Hand.Cards.Count;
            var newCards = Enumerable.Range(0, 25).Select(_ => combat.CreateCard(chosen.Card, player)).ToList();
            await CardPileCmd.Add(newCards, player.PlayerCombatState!.Hand);
            GD.Print($"GM_SMOKE 手牌变化 {handBefore} -> {player.PlayerCombatState.Hand.Cards.Count}");
            Check("原生发牌超过十张", player.PlayerCombatState.Hand.Cards.Count == 20);
            var layout = MegaCrit.Sts2.Core.Helpers.HandPosHelper.GetPosition(20, 19);
            Check("二十张手牌布局", float.IsFinite(layout.X) && float.IsFinite(layout.Y));
            GD.Print("GM_SMOKE_ALL_PASSED");
            tree.Quit();
        }
        catch (Exception ex)
        {
            GD.PrintErr("GM_SMOKE_FAILED " + ex);
            tree.Quit(1);
        }
    }

    /// <summary>断言并写入便于机器校验的结果行。</summary>
    private static void Check(string name, bool pass)
    {
        GD.Print($"GM_SMOKE {name}: {pass}");
        if (!pass) throw new InvalidOperationException(name);
    }
}
