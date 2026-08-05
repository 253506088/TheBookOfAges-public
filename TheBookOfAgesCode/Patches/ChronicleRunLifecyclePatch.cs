using System.Reflection;
using HarmonyLib;
using TheBookOfAges.TheBookOfAgesCode.Services;

namespace TheBookOfAges.TheBookOfAgesCode.Patches;

/// <summary>
/// 岁月史书存档生命周期补丁；钩住游戏自带 RunSaveManager 的"删除当前 Run 存档"两个入口，
/// 让 Mod 配置文件和 current_run.save 同生共死：通关 / 死亡 / 放弃 → Mod 配置一起删 + 内存清零。
/// 玩家删整个 profile 时游戏会递归删 profile 目录，Mod 文件被一起干掉，不需要额外钩。
/// </summary>
[HarmonyPatch]
public static class ChronicleRunLifecyclePatch
{
    /// <summary>
    /// 游戏自带 RunSaveManager 类型的完全限定名；不直接 typeof() 是为了避免 Publicize 没开时编译失败。
    /// </summary>
    private const string RunSaveManagerTypeName = "MegaCrit.Sts2.Core.Saves.Managers.RunSaveManager";

    /// <summary>
    /// 单人存档删除方法名；与游戏内 RunSaveManager.DeleteCurrentRun 一致。
    /// </summary>
    private const string DeleteCurrentRunMethodName = "DeleteCurrentRun";

    /// <summary>
    /// 多人存档删除方法名；与游戏内 RunSaveManager.DeleteCurrentMultiplayerRun 一致。
    /// </summary>
    private const string DeleteCurrentMultiplayerRunMethodName = "DeleteCurrentMultiplayerRun";

    /// <summary>
    /// 用 AccessTools 按名字定位删除方法；找不到时静默返回空列表，不让 Harmony 因方法缺失而崩。
    /// </summary>
    public static IEnumerable<MethodBase> TargetMethods()
    {
        Type? type = AccessTools.TypeByName(RunSaveManagerTypeName);
        if (type == null)
        {
            MainFile.Logger.Info($"岁月史书存档生命周期补丁跳过：找不到 {RunSaveManagerTypeName} 类型。");
            return [];
        }

        List<MethodBase> methods = new(2);
        AppendMethodIfExists(methods, type, DeleteCurrentRunMethodName);
        AppendMethodIfExists(methods, type, DeleteCurrentMultiplayerRunMethodName);

        if (methods.Count == 0)
        {
            MainFile.Logger.Info($"岁月史书存档生命周期补丁跳过：未在 {type.FullName} 上找到 {DeleteCurrentRunMethodName}/{DeleteCurrentMultiplayerRunMethodName} 方法。");
        }

        return methods;
    }

    /// <summary>
    /// 在游戏删除完 current_run 存档后，紧接着删 Mod 自己的配置文件并把内存归零。
    /// 用 __originalMethod.Name 区分单/多人，避免依赖可能尚未刷新的 GmRunContextService.IsMultiplayer。
    /// </summary>
    public static void Postfix(MethodBase __originalMethod)
    {
        try
        {
            bool multiplayer = string.Equals(__originalMethod.Name, DeleteCurrentMultiplayerRunMethodName, StringComparison.Ordinal);
            MainFile.Logger.Info($"岁月史书存档生命周期补丁触发：方法={__originalMethod.Name}，多人={multiplayer}。");

            ChronicleStateRepository.TryDelete(multiplayer);
            ChronicleService.Instance.ResetToDefaults();
        }
        catch (Exception ex)
        {
            MainFile.Logger.Info($"岁月史书存档生命周期补丁异常：方法={__originalMethod.Name}，异常类型={ex.GetType().Name}，异常={ex.Message}。");
        }
    }

    /// <summary>
    /// 用 AccessTools 反射查方法；用 NonPublic 兜底，万一游戏改成 internal 也能命中。
    /// </summary>
    private static void AppendMethodIfExists(List<MethodBase> methods, Type type, string methodName)
    {
        MethodInfo? method = AccessTools.Method(type, methodName);
        if (method != null)
        {
            methods.Add(method);
            return;
        }

        MainFile.Logger.Info($"岁月史书存档生命周期补丁未找到方法：类型={type.FullName}，方法={methodName}。");
    }
}
