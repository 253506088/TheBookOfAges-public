using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using TheBookOfAges.TheBookOfAgesCode.UI;

namespace TheBookOfAges.TheBookOfAgesCode.Patches;

/// <summary>
/// 顶栏补丁，用于在游戏顶栏创建完成后插入 GM 入口。
/// </summary>
[HarmonyPatch(typeof(NTopBar), nameof(NTopBar._Ready))]
public static class TopBarPatch
{
    /// <summary>
    /// 顶栏 Ready 后挂接 GM 按钮，失败时只写日志，不影响游戏原 UI。
    /// </summary>
    public static void Postfix(NTopBar __instance)
    {
        try
        {
            GmUiManager.AttachToTopBar(__instance);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"GM 顶栏入口创建失败 / Failed to create GM top bar entry: {ex.Message}");
        }
    }
}

/// <summary>
/// 顶栏输入补丁：处理 GM 快捷键，并在 GM 面板打开时拦截 Esc 防止透传到游戏设置。
/// </summary>
[HarmonyPatch(typeof(NTopBar), nameof(NTopBar._Input))]
public static class TopBarInputPatch
{
    /// <summary>
    /// 优先处理 GM 相关输入；当 Esc 被 GM 面板消费时，阻止原始输入逻辑继续执行。
    /// </summary>
    public static bool Prefix(NTopBar __instance, Godot.InputEvent inputEvent)
    {
        if (!GmUiManager.HandleInput(inputEvent))
        {
            return true;
        }

        __instance.GetViewport()?.SetInputAsHandled();
        return false;
    }
}
